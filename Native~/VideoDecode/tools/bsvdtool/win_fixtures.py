# Fixtures for bsvdtool's Windows checks, generated locally with ffmpeg (on PATH). Run: python win_fixtures.py <out dir>
#
# Counter clips: a 16-bit value per frame as black/white blocks (64 px per bit, bit b centred at x = 32 + 64 b, white = 1)
# in two 32-row strips, top (rows 0-31) and bottom (rows h-32 to h-1) of the source picture:
#   top    = N | 0x8000
#   bottom = (32767 - N) | 0x8000
# so a frame read upside down, torn (top and bottom from different frames) or cropped wrongly is caught, and N is the
# frame's display index. Each clip has a .pkt sidecar written by the same encode: one line per access unit in decode
# order, "<pts in frames> <dts> <bytes> <K|N>", so the tool feeds exact units with their real presentation times (B-frames
# reorder them). The bars clips of spike/fixtures.py have no B-frames and need no sidecar.
import os
import subprocess
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

STRIPS = ("[1:v]format=gray,split[s1][s2];"
          "[s1]geq=lum='if(bitand(bitor(N,32768),pow(2,floor(X/64))),235,16)'[a];"
          "[s2]geq=lum='if(bitand(bitor(32767-N,32768),pow(2,floor(X/64))),235,16)'[b];"
          "[0:v][a]overlay=0:0[t];[t][b]overlay=0:H-32,format={fmt}")


def run(args):
    print(args[-1], flush=True)
    subprocess.run(["ffmpeg", "-y", "-v", "error"] + args, check=True)


def counter(name, background, size, seconds, codec_args, fmt="yuv420p"):
    out = os.path.join(OUT, name)
    joiner = ":" if "=" in background else "="
    src = ["-f", "lavfi", "-i", f"{background}{joiner}s={size}:r=30:d={seconds}",
           "-f", "lavfi", "-i", f"color=black:s=1024x32:r=30:d={seconds}",
           "-filter_complex", STRIPS.format(fmt=fmt)]
    stats = ["-stats_mux_pre_fmt", "{ptsi} {dts} {size} {key}", "-stats_mux_pre", out + ".pkt"]
    run(src + codec_args + stats + [out])
    total = sum(int(line.split()[2]) for line in open(out + ".pkt"))
    if total != os.path.getsize(out):
        raise SystemExit(f"{name}: sidecar sizes {total} != file {os.path.getsize(out)}")


X264 = "aud=1:keyint=30:min-keyint=30:scenecut=0:colorprim=bt709:transfer=bt709:colormatrix=bt709"
X265 = "aud=1:keyint=30:min-keyint=30:bframes=2:scenecut=0:log-level=error"
H264_HIGH = ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-bf", "2", "-x264-params", X264, "-f", "h264"]

counter("o1080.h264", "testsrc2", "1920x1080", 10, ["-b:v", "8M"] + H264_HIGH)
counter("o1080base.h264", "testsrc2", "1920x1080", 10,
        ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "baseline", "-b:v", "8M", "-x264-params", X264, "-f", "h264"])
counter("o2160.h264", "testsrc2", "3840x2160", 6, ["-b:v", "30M"] + H264_HIGH)
counter("o1080.hevc", "testsrc2", "1920x1080", 10, ["-c:v", "libx265", "-preset", "veryfast", "-b:v", "6M", "-x265-params", X265, "-f", "hevc"])
counter("o1080m10.hevc", "testsrc2", "1920x1080", 4,
        ["-c:v", "libx265", "-preset", "veryfast", "-b:v", "6M", "-x265-params", X265, "-f", "hevc"], fmt="yuv420p10le")
# Black between the strips: rows that must be black show a crop or scale error (1920x1080 is coded as 1088 and cropped).
counter("k1080.h264", "color=c=black", "1920x1080", 3, ["-b:v", "4M"] + H264_HIGH)
# SD without any colour description: the decoder must fall back to BT.601 limited by height.
counter("sd360.h264", "testsrc2", "640x360", 3,
        ["-c:v", "libx264", "-preset", "veryfast", "-bf", "2", "-x264-params", "aud=1:keyint=30", "-f", "h264"])
print("done")
