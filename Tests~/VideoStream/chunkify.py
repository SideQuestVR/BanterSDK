# Regroups a plain MP4's samples into chunks of several samples per track, as real world files are laid out (each track's
# samples together for about half a second, the tracks alternating). Only the chunk tables (stsc, stco) and the order of
# the bytes in mdat change; every sample, time and flag stays the same, which ffprobe then checks independently.
# Usage: python -I chunkify.py <in.mp4> <out.mp4> <seconds per chunk>
import struct, sys

CONTAINERS = {b"moov", b"trak", b"mdia", b"minf", b"stbl", b"edts", b"dinf"}


def parse(data, at, end):
    boxes = []
    while at + 8 <= end:
        size, kind = struct.unpack(">I4s", data[at:at + 8])
        header = 8
        if size == 1:
            size = struct.unpack(">Q", data[at + 8:at + 16])[0]
            header = 16
        elif size == 0:
            size = end - at
        body = (at + header, at + size)
        boxes.append([kind, parse(data, *body) if kind in CONTAINERS else data[body[0]:body[1]]])
        at += size
    return boxes


def serial(boxes):
    out = b""
    for kind, body in boxes:
        payload = serial(body) if isinstance(body, list) else body
        out += struct.pack(">I4s", len(payload) + 8, kind) + payload
    return out


def find(boxes, kind):
    return [b for b in boxes if b[0] == kind]


def table(body, entry, at=8):
    count = struct.unpack(">I", body[4:8])[0]
    return [struct.unpack(entry, body[at + i * struct.calcsize(entry):at + (i + 1) * struct.calcsize(entry)]) for i in range(count)]


src, dst, seconds = sys.argv[1], sys.argv[2], float(sys.argv[3])
data = open(src, "rb").read()
top = parse(data, 0, len(data))
moov = find(top, b"moov")[0]
tracks = []
for trak in find(moov[1], b"trak"):
    mdia = find(trak[1], b"mdia")[0]
    timescale = struct.unpack(">I", find(mdia[1], b"mdhd")[0][1][12:16])[0]
    stbl = find(find(mdia[1], b"minf")[0][1], b"stbl")[0]
    get = lambda k: find(stbl[1], k)[0][1]
    stsz = get(b"stsz")
    uniform, count = struct.unpack(">II", stsz[4:12])
    sizes = [uniform] * count if uniform else [struct.unpack(">I", stsz[12 + 4 * i:16 + 4 * i])[0] for i in range(count)]
    stsc = table(get(b"stsc"), ">III")
    chunks = [o[0] for o in table(get(b"stco"), ">I")]
    offsets, sample = [], 0
    for c in range(len(chunks)):
        run = max(i for i in range(len(stsc)) if stsc[i][0] <= c + 1)
        position = chunks[c]
        for _ in range(stsc[run][1]):
            offsets.append(position)
            position += sizes[sample]
            sample += 1
    times, t = [], 0
    for n, delta in table(get(b"stts"), ">II"):
        for _ in range(n):
            times.append(t / timescale)
            t += delta
    tracks.append({"stbl": stbl, "sizes": sizes, "offsets": offsets, "times": times})

# Groups of samples per track, each covering about `seconds`, then all groups in time order.
groups = []
for index, tr in enumerate(tracks):
    start = 0
    while start < len(tr["sizes"]):
        end = start
        while end < len(tr["sizes"]) and tr["times"][end] - tr["times"][start] < seconds:
            end += 1
        groups.append((tr["times"][start], index, start, end))
        start = end
groups.sort()

payload, chunk_offsets = bytearray(), [[] for _ in tracks]
for _, index, start, end in groups:
    tr = tracks[index]
    chunk_offsets[index].append(len(payload))
    for s in range(start, end):
        payload += data[tr["offsets"][s]:tr["offsets"][s] + tr["sizes"][s]]


def rebuild(base):
    for index, tr in enumerate(tracks):
        runs, entries = [], []
        for _, i, start, end in groups:
            if i == index:
                runs.append(end - start)
        for n, per in enumerate(runs):
            if not entries or entries[-1][1] != per:
                entries.append((n + 1, per))
        stsc = struct.pack(">II", 0, len(entries)) + b"".join(struct.pack(">III", first, per, 1) for first, per in entries)
        stco = struct.pack(">II", 0, len(runs)) + b"".join(struct.pack(">I", base + o) for o in chunk_offsets[index])
        boxes = tr["stbl"][1]
        for box in boxes:
            if box[0] == b"stsc":
                box[1] = stsc
            elif box[0] == b"stco":
                box[1] = stco
    return serial([b for b in top if b[0] == b"ftyp"]) + serial([moov])


head = rebuild(0)
base = len(head) + 8
head = rebuild(base)
assert len(head) + 8 == base
with open(dst, "wb") as fh:
    fh.write(head + struct.pack(">I4s", len(payload) + 8, b"mdat") + payload)
print(dst, "chunks per track:", [len(c) for c in chunk_offsets], "bytes:", len(head) + 8 + len(payload))
