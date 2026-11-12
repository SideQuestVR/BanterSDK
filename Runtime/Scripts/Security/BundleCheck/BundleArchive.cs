using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using LZ4ps;

namespace BS.Security
{
    /// <summary>Random-access bytes a bundle is read from: the decrypted bundle in memory, or (offline tools) a remote file.</summary>
    internal abstract class BundleSource
    {
        internal abstract long Length { get; }

        /// <summary>Reads exactly <paramref name="count"/> bytes at <paramref name="position"/>, or throws.</summary>
        internal abstract void Read(long position, byte[] buffer, int offset, int count);

        /// <summary>The backing array, when the bytes already sit in one (no copy needed to decompress a block).</summary>
        internal virtual bool TryGetArray(out byte[] array, out int offset)
        {
            array = null;
            offset = 0;
            return false;
        }
    }

    internal sealed class ByteArrayBundleSource : BundleSource
    {
        readonly byte[] data;
        readonly int start, length;

        internal ByteArrayBundleSource(byte[] data) : this(data, 0, data?.Length ?? 0) { }

        internal ByteArrayBundleSource(byte[] data, int start, int length)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (start < 0 || length < 0 || start > data.Length - length) throw new ArgumentOutOfRangeException(nameof(length));
            this.data = data;
            this.start = start;
            this.length = length;
        }

        internal override long Length => length;

        internal override void Read(long position, byte[] buffer, int offset, int count)
        {
            if (position < 0 || count < 0 || position > length - (long)count)
                throw new BundleRefusedException(BundleRule.Malformed, "read past the end of the bundle");
            Buffer.BlockCopy(data, start + (int)position, buffer, offset, count);
        }

        internal override bool TryGetArray(out byte[] array, out int offset)
        {
            array = data;
            offset = start;
            return true;
        }
    }

    /// <summary>
    /// A UnityFS bundle's container: header, block table and directory, validated, and its uncompressed data served
    /// on demand (LZ4 blocks decompressed as they are touched, a small LRU of them kept; an LZMA bundle's single block
    /// decompressed once). Header and table structures come from AssetsTools.NET; every size it would allocate from is
    /// bounded first.
    /// </summary>
    internal sealed class BundleArchive
    {
        /// <summary>One file inside the bundle.</summary>
        internal sealed class Node
        {
            internal string Name;
            internal long Offset;
            internal long Size;
            internal uint Flags;
            internal bool IsSerializedFlag => (Flags & 4) != 0;
        }

        const int MaxBlocksInfoBytes = 16 * 1024 * 1024;
        const int MaxNodes = 4096;

        /// <summary>
        /// Decoded LZ4 blocks kept at most, in bytes (a block is at most MaxLz4BlockBytes, 16 MiB; Unity writes 128 KiB):
        /// the LRU below drops the oldest before it holds more. Peak managed memory of one check is then about this, plus
        /// the block being decoded, plus what the rules capture (graph JSON). An LZMA bundle (.bee sections are LZ4 in every
        /// world surveyed; raw LZMA downloads are recompressed to LZ4 before the check) needs instead its decoded prefix,
        /// up to MaxLzmaBlockBytes (512 MiB Android, 1 GiB elsewhere), plus the decoder's window, at most that prefix too.
        /// </summary>
        internal const long MaxBlockCacheBytes =
#if UNITY_ANDROID && !UNITY_EDITOR
            64L * 1024 * 1024;
#else
            256L * 1024 * 1024;
#endif

        readonly BundleSource source;
        readonly BundleCheckOptions options;
        readonly BundleCheckStats stats;
        internal readonly List<Node> Nodes = new List<Node>();
        internal readonly string EngineVersion;
        internal readonly long DataLength;

        // block table, in uncompressed order
        long[] blockStart;     // uncompressed offset
        int[] blockSize;       // uncompressed size (LZMA: may be large)
        long[] blockSource;    // absolute offset of the compressed bytes in the source
        uint[] blockStored;    // compressed size
        byte[] blockType;      // 0 stored, 1 LZMA, 2/3 LZ4
        byte[] lzmaData;       // the LZMA bundle's data up to lzmaPrefix, once decompressed
        int lzmaBlock = -1;
        /// <summary>
        /// How much of an LZMA bundle the checker needs: up to the end of the last file it may read. Resource payloads
        /// (.resS/.resource) come last and are never read, so their (usually large) tail is not decompressed.
        /// </summary>
        long lzmaPrefix = long.MaxValue;

        // tiny LRU of decompressed LZ4 blocks
        readonly int[] cacheIndex;
        readonly byte[][] cacheData;
        readonly long[] cacheUse;
        long useClock;
        long cachedBytes;
        byte[] compressedScratch;

        BundleArchive(BundleSource source, BundleCheckOptions options, BundleCheckStats stats, string engineVersion, long dataLength)
        {
            this.source = source;
            this.options = options;
            this.stats = stats;
            EngineVersion = engineVersion;
            DataLength = dataLength;
            var size = Math.Max(4, options.BlockCacheSize);
            cacheIndex = new int[size];
            cacheData = new byte[size][];
            cacheUse = new long[size];
            for (var i = 0; i < size; i++) cacheIndex[i] = -1;
        }

        static BundleRefusedException Bad(string why) => new BundleRefusedException(BundleRule.Malformed, why);

        internal static BundleArchive Open(BundleSource source, BundleCheckOptions options, BundleCheckStats stats)
        {
            var length = source.Length;
            if (length < 64) throw Bad("too short for a bundle");

            // ---- header: signature, versions and the file-stream header, parsed from a bounded copy
            var headLength = (int)Math.Min(length, 1024);
            var head = new byte[headLength];
            source.Read(0, head, 0, headLength);
            if (head[0] != 'U' || head[1] != 'n' || head[2] != 'i' || head[3] != 't' || head[4] != 'y' || head[5] != 'F' || head[6] != 'S' || head[7] != 0)
                throw new BundleRefusedException(BundleRule.Unsupported, "not a UnityFS bundle");
            var header = new AssetBundleHeader();
            try
            {
                header.Read(new AssetsFileReader(new MemoryStream(head, false)));
            }
            catch (Exception e)
            {
                throw Bad("unreadable bundle header (" + e.GetType().Name + ")");
            }
            if (header.Version < 6 || header.Version > 8) throw new BundleRefusedException(BundleRule.Unsupported, "bundle format " + header.Version);
            if (header.GenerationVersion.Length > 64 || header.EngineVersion.Length > 64) throw Bad("header version strings too long");
            var fs = header.FileStreamHeader;
            var flags = (uint)fs.Flags;
            // 0x3F compression, 0x40 combined, 0x80 info at end, 0x100 old web plugin, 0x200 info padding; 0x400 = Unity's
            // own bundle encryption, which this app never keys (Unity could not load it either).
            if ((flags & ~0x3FFu) != 0 || (flags & 0x100) != 0) throw new BundleRefusedException(BundleRule.Unsupported, "bundle flags 0x" + flags.ToString("x"));
            var infoCompression = (int)(flags & 0x3F);
            if (infoCompression > 3) throw new BundleRefusedException(BundleRule.Unsupported, "block-info compression " + infoCompression);
            if (fs.TotalFileSize <= 0 || fs.TotalFileSize > length) throw Bad("total size outside the bundle");
            if (fs.CompressedSize == 0 || fs.CompressedSize > MaxBlocksInfoBytes || fs.DecompressedSize < 24 || fs.DecompressedSize > MaxBlocksInfoBytes)
                throw Bad("block-info size out of range");
            if (infoCompression == 0 && fs.CompressedSize != fs.DecompressedSize) throw Bad("stored block info with two sizes");

            long infoOffset, dataOffset;
            try
            {
                infoOffset = header.GetBundleInfoOffset();
                dataOffset = header.GetFileDataOffset();
            }
            catch (Exception e)
            {
                throw Bad("bundle layout (" + e.GetType().Name + ")");
            }
            if (infoOffset < 0 || infoOffset > fs.TotalFileSize - fs.CompressedSize) throw Bad("block info outside the bundle");
            if (dataOffset < 0 || dataOffset > fs.TotalFileSize) throw Bad("data outside the bundle");

            // ---- block table and directory
            BundleCheckBudget.ChargeCurrent(fs.CompressedSize + fs.DecompressedSize);
            var packed = new byte[fs.CompressedSize];
            source.Read(infoOffset, packed, 0, packed.Length);
            var info = new byte[fs.DecompressedSize];
            Decompress(infoCompression, packed, 0, packed.Length, info, "block info");
            // AssetsTools allocates from these counts: bound them by the table's own size first (big-endian).
            if (info.Length < 24) throw Bad("block info too short");
            var blockCount = ReadInt32BE(info, 16);
            if (blockCount < 0 || blockCount > (info.Length - 24) / 10) throw Bad("block count out of range");
            var dirAt = 20 + blockCount * 10;
            var nodeCount = ReadInt32BE(info, dirAt);
            if (nodeCount < 0 || nodeCount > MaxNodes || nodeCount > (info.Length - dirAt - 4) / 21) throw Bad("file count out of range");
            var table = new AssetBundleBlockAndDirInfo();
            try
            {
                table.Read(new AssetsFileReader(new MemoryStream(info, false)) { BigEndian = true });
            }
            catch (Exception e)
            {
                throw Bad("unreadable block info (" + e.GetType().Name + ")");
            }

            // ---- blocks
            var blocks = table.BlockInfos ?? Array.Empty<AssetBundleBlockInfo>();
            long uncompressed = 0, compressed = 0;
            bool anyLz4 = false, anyLzma = false;
            foreach (var block in blocks)
            {
                var type = block.GetCompressionType();
                if (type == 1) anyLzma = true;
                else if (type == 2 || type == 3) anyLz4 = true;
                else if (type != 0) throw new BundleRefusedException(BundleRule.Unsupported, "block compression " + type);
                if (type == 0 && block.CompressedSize != block.DecompressedSize) throw Bad("stored block with two sizes");
                if ((type == 2 || type == 3) && block.DecompressedSize > (uint)options.MaxLz4BlockBytes)
                    throw new BundleRefusedException(BundleRule.TooLarge, "LZ4 block of " + block.DecompressedSize + " bytes");
                if (block.DecompressedSize > int.MaxValue || block.CompressedSize > int.MaxValue) throw new BundleRefusedException(BundleRule.TooLarge, "block over 2 GiB");
                uncompressed += block.DecompressedSize;
                compressed += block.CompressedSize;
            }
            if (anyLz4 && anyLzma) throw new BundleRefusedException(BundleRule.Unsupported, "mixed LZ4 and LZMA blocks");
            if (anyLzma && blocks.Length != 1) throw new BundleRefusedException(BundleRule.Unsupported, "LZMA bundle with several blocks");
            if (compressed > fs.TotalFileSize - dataOffset) throw Bad("blocks run past the end of the bundle");

            var archive = new BundleArchive(source, options, stats, header.EngineVersion, uncompressed);
            archive.blockStart = new long[blocks.Length];
            archive.blockSize = new int[blocks.Length];
            archive.blockSource = new long[blocks.Length];
            archive.blockStored = new uint[blocks.Length];
            archive.blockType = new byte[blocks.Length];
            long u = 0, c = dataOffset;
            for (var i = 0; i < blocks.Length; i++)
            {
                archive.blockStart[i] = u;
                archive.blockSize[i] = (int)blocks[i].DecompressedSize;
                archive.blockSource[i] = c;
                archive.blockStored[i] = blocks[i].CompressedSize;
                archive.blockType[i] = blocks[i].GetCompressionType();
                u += blocks[i].DecompressedSize;
                c += blocks[i].CompressedSize;
            }
            stats.UncompressedBytes = uncompressed;
            stats.Compression = anyLzma ? "LZMA" : anyLz4 ? "LZ4" : "none";
            stats.EngineVersion = BundleNames.Clean(header.EngineVersion, 40);

            // ---- directory
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in table.DirectoryInfos ?? new List<AssetBundleDirectoryInfo>())
            {
                if (entry.Offset < 0 || entry.DecompressedSize < 0 || entry.Offset > uncompressed - entry.DecompressedSize)
                    throw Bad("file outside the bundle data");
                if (string.IsNullOrEmpty(entry.Name) || entry.Name.Length > 1024) throw Bad("bad file name");
                foreach (var ch in entry.Name)
                {
                    if (char.IsControl(ch)) throw Bad("control character in a file name");
                }
                if (!names.Add(entry.Name)) throw Bad("duplicate file name");
                archive.Nodes.Add(new Node { Name = entry.Name, Offset = entry.Offset, Size = entry.DecompressedSize, Flags = entry.Flags });
            }
            if (anyLzma)
            {
                long needed = 0;
                foreach (var node in archive.Nodes)
                {
                    if (!node.IsSerializedFlag && IsResourceName(node.Name)) continue;
                    needed = Math.Max(needed, node.Offset + node.Size);
                }
                archive.lzmaPrefix = needed;
                if (needed > options.MaxLzmaBlockBytes)
                    throw new BundleRefusedException(BundleRule.TooLarge, "LZMA data to read of " + needed / (1024 * 1024) + " MiB, over this platform's "
                                                                          + options.MaxLzmaBlockBytes / (1024 * 1024) + " MiB; it needs re-publishing with LZ4 compression");
            }
            return archive;
        }

        /// <summary>Unity's resource payload files (texture/mesh streaming data, audio/video): never serialized files.</summary>
        internal static bool IsResourceName(string name) =>
            name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase);

        static int ReadInt32BE(byte[] data, int at)
        {
            if (at < 0 || at > data.Length - 4) throw Bad("block info truncated");
            return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
        }

        /// <summary>Exactly-sized decompression; anything that does not decode to the declared size is refused.</summary>
        static void Decompress(int type, byte[] input, int inputOffset, int inputLength, byte[] output, string what)
        {
            try
            {
                switch (type)
                {
                    case 0:
                        if (inputLength != output.Length) throw Bad(what + ": stored size mismatch");
                        Buffer.BlockCopy(input, inputOffset, output, 0, inputLength);
                        return;
                    case 1:
                        DecodeLzma(input, inputOffset, inputLength, output, what);
                        return;
                    case 2:
                    case 3:
                        LZ4Codec.Decode64(input, inputOffset, inputLength, output, 0, output.Length, true);
                        return;
                    default:
                        throw new BundleRefusedException(BundleRule.Unsupported, what + ": compression " + type);
                }
            }
            catch (BundleRefusedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw Bad(what + ": does not decompress (" + e.GetType().Name + ")");
            }
        }

        /// <summary>Decodes only the first output.Length bytes of an LZMA block (5 property bytes, then the stream).</summary>
        static void DecompressLzmaPrefix(byte[] input, int inputOffset, int inputLength, byte[] output, string what)
        {
            try
            {
                DecodeLzma(input, inputOffset, inputLength, output, what);
            }
            catch (BundleRefusedException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw Bad(what + ": does not decompress (" + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// Decodes the first output.Length bytes of an LZMA stream (5 property bytes, then the data). The decoder
        /// allocates its window from the dictionary size in those bytes, so it is clamped to the output length: no match
        /// can reach further back than the output already decoded, so the output is the same. Property bytes no encoder
        /// writes are refused. The compressed input is read through the check's budget (<see cref="BundleCheckBudget"/>).
        /// </summary>
        internal static void DecodeLzma(byte[] input, int inputOffset, int inputLength, byte[] output, string what)
        {
            if (inputLength < 5) throw Bad(what + ": LZMA data too short");
            var properties = ClampLzmaProperties(input, inputOffset, output.Length, what);
            var decoder = new SevenZip.Compression.LZMA.Decoder();
            decoder.SetDecoderProperties(properties);
            using (var compressedStream = new BudgetedReadStream(new MemoryStream(input, inputOffset + 5, inputLength - 5, false)))
            using (var outStream = new MemoryStream(output, true))
            {
                decoder.Code(compressedStream, outStream, inputLength - 5, output.Length, null);
                if (outStream.Position != output.Length) throw Bad(what + ": LZMA size mismatch");
            }
        }

        /// <summary>
        /// The 5 LZMA property bytes at <paramref name="at"/>, checked (lc &lt;= 8, lp &lt;= 4, pb &lt;= 4, lc + lp &lt;= 8: literal
        /// tables of at most 768 KiB) and with the dictionary clamped to the output length (at least 4 KiB, the decoder's floor).
        /// </summary>
        internal static byte[] ClampLzmaProperties(byte[] input, int at, int outputLength, string what)
        {
            if (at < 0 || at > input.Length - 5) throw Bad(what + ": LZMA data too short");
            var properties = new byte[5];
            Buffer.BlockCopy(input, at, properties, 0, 5);
            int lc = properties[0] % 9, rest = properties[0] / 9, lp = rest % 5, pb = rest / 5;
            if (properties[0] >= 9 * 5 * 5 || pb > 4 || lc + lp > 8) throw Bad(what + ": LZMA properties out of range");
            var dictionary = (uint)properties[1] | (uint)properties[2] << 8 | (uint)properties[3] << 16 | (uint)properties[4] << 24;
            var limit = (uint)Math.Max(4096, outputLength);
            if (dictionary > limit)
            {
                properties[1] = (byte)limit;
                properties[2] = (byte)(limit >> 8);
                properties[3] = (byte)(limit >> 16);
                properties[4] = (byte)(limit >> 24);
            }
            return properties;
        }

        /// <summary>Reads uncompressed bundle bytes [position, position + count).</summary>
        internal void Read(long position, byte[] buffer, int offset, int count)
        {
            if (position < 0 || count < 0 || position > DataLength - count) throw Bad("read past the end of the bundle data");
            BundleCheckBudget.ChargeCurrent(count);
            while (count > 0)
            {
                var index = FindBlock(position);
                var within = position - blockStart[index];
                var take = (int)Math.Min(count, blockSize[index] - within);
                if (blockType[index] == 0)
                {
                    source.Read(blockSource[index] + within, buffer, offset, take);
                }
                else
                {
                    var data = Block(index);
                    if (within + take > data.Length)
                    {
                        // Past the decoded LZMA prefix: decode the whole block after all.
                        if (blockSize[index] > options.MaxLzmaBlockBytes)
                            throw new BundleRefusedException(BundleRule.TooLarge, "LZMA block of " + blockSize[index] / (1024 * 1024) + " MiB, over this platform's "
                                                                                  + options.MaxLzmaBlockBytes / (1024 * 1024) + " MiB; it needs re-publishing with LZ4 compression");
                        lzmaPrefix = long.MaxValue;
                        lzmaBlock = -1;
                        data = Block(index);
                    }
                    Buffer.BlockCopy(data, (int)within, buffer, offset, take);
                }
                position += take;
                offset += take;
                count -= take;
            }
        }

        int FindBlock(long position)
        {
            int lo = 0, hi = blockStart.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (blockStart[mid] <= position) lo = mid;
                else hi = mid - 1;
            }
            // Zero-sized blocks share a start; step past them.
            while (lo < blockStart.Length - 1 && blockSize[lo] == 0) lo++;
            return lo;
        }

        byte[] Block(int index)
        {
            if (blockType[index] == 1)
            {
                if (lzmaBlock != index)
                {
                    lzmaData = Decompressed(index);
                    lzmaBlock = index;
                }
                return lzmaData;
            }
            var oldest = 0;
            for (var i = 0; i < cacheIndex.Length; i++)
            {
                if (cacheIndex[i] == index)
                {
                    cacheUse[i] = ++useClock;
                    return cacheData[i];
                }
                if (cacheUse[i] < cacheUse[oldest]) oldest = i;
            }
            var data = Decompressed(index);
            // Room by count (the oldest slot) and by bytes (older blocks go until this one fits under the cap).
            Evict(oldest);
            while (cachedBytes + data.Length > MaxBlockCacheBytes && EvictOldest()) { }
            cacheIndex[oldest] = index;
            cacheData[oldest] = data;
            cacheUse[oldest] = ++useClock;
            cachedBytes += data.Length;
            return data;
        }

        void Evict(int slot)
        {
            if (cacheData[slot] != null) cachedBytes -= cacheData[slot].Length;
            cacheData[slot] = null;
            cacheIndex[slot] = -1;
            cacheUse[slot] = 0;
        }

        /// <summary>Drops the least recently used cached block; false when none is left.</summary>
        bool EvictOldest()
        {
            var oldest = -1;
            for (var i = 0; i < cacheIndex.Length; i++)
            {
                if (cacheData[i] != null && (oldest < 0 || cacheUse[i] < cacheUse[oldest])) oldest = i;
            }
            if (oldest < 0) return false;
            Evict(oldest);
            return true;
        }

        byte[] Decompressed(int index)
        {
            var stored = (int)blockStored[index];
            byte[] input;
            int inputOffset;
            if (source.TryGetArray(out var array, out var arrayOffset))
            {
                if (blockSource[index] > source.Length - stored) throw Bad("block past the end of the bundle");
                input = array;
                inputOffset = checked(arrayOffset + (int)blockSource[index]);
            }
            else
            {
                if (compressedScratch == null || compressedScratch.Length < stored) compressedScratch = new byte[Math.Max(stored, 256 * 1024)];
                source.Read(blockSource[index], compressedScratch, 0, stored);
                input = compressedScratch;
                inputOffset = 0;
            }
            var length = blockType[index] == 1 ? (int)Math.Min(blockSize[index], Math.Max(0, lzmaPrefix - blockStart[index])) : blockSize[index];
            BundleCheckBudget.ChargeCurrent(length); // before allocating it
            var output = new byte[length];
            if (blockType[index] == 1 && length < blockSize[index]) DecompressLzmaPrefix(input, inputOffset, stored, output, "block " + index);
            else Decompress(blockType[index], input, inputOffset, stored, output, "block " + index);
            stats.BlocksDecompressed++;
            stats.BytesDecompressed += output.Length;
            return output;
        }
    }

    /// <summary>
    /// Read-only pass-through that charges <see cref="BundleCheckBudget.Current"/> every 64 KiB, so a long LZMA decode
    /// keeps checking its clock and cancellation (the decoder reads its input a byte at a time).
    /// </summary>
    internal sealed class BudgetedReadStream : Stream
    {
        const int Step = 64 * 1024;
        readonly Stream inner;
        int sinceCharge;

        internal BudgetedReadStream(Stream inner)
        {
            this.inner = inner;
        }

        void Count(int bytes)
        {
            sinceCharge += bytes;
            if (sinceCharge < Step) return;
            BundleCheckBudget.ChargeCurrent(sinceCharge);
            sinceCharge = 0;
        }

        public override int ReadByte()
        {
            var value = inner.ReadByte();
            if (value >= 0) Count(1);
            return value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
