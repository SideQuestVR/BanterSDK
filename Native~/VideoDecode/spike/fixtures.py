# V1 Windows spike fixtures, all generated locally with ffmpeg.
#  - counter clips: a 16-bit frame counter as black/white blocks in strips at the top AND bottom of every frame, so
#    decode order is checked exactly, and a top/bottom mismatch means a torn frame.
#  - bars clips: eight known RGB bars, written as exact YCbCr for each matrix and range, to check the colour conversion.
import os, subprocess, sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

STRIP = ("[1:v]format=gray,geq=lum='if(bitand(N,pow(2,floor(X/64))),235,16)',split[a][b];"
         "[0:v][a]overlay=0:0[t];[t][b]overlay=0:H-32,format=yuv420p")


def run(args):
    print(" ".join(args[-1:]), flush=True)
    subprocess.run(["ffmpeg", "-y", "-v", "error"] + args, check=True)


def counter(name, size, seconds, fps, codec_args):
    src = ["-f", "lavfi", "-i", f"testsrc2=s={size}:r={fps}:d={seconds}",
           "-f", "lavfi", "-i", f"color=black:s=1024x32:r={fps}:d={seconds}", "-filter_complex", STRIP]
    run(src + codec_args + [os.path.join(OUT, name)])


X264 = "aud=1:keyint=30:min-keyint=30:scenecut=0:colorprim=bt709:transfer=bt709:colormatrix=bt709"
counter("c1080.h264", "1920x1080", 10, 30, ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-bf", "2", "-b:v", "8M", "-x264-params", X264, "-f", "h264"])
counter("c1080base.h264", "1920x1080", 10, 30, ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "baseline", "-b:v", "8M", "-x264-params", X264, "-f", "h264"])
counter("c2160.h264", "3840x2160", 6, 30, ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-bf", "2", "-b:v", "30M", "-x264-params", X264, "-f", "h264"])
counter("c1080.hevc", "1920x1080", 10, 30, ["-c:v", "libx265", "-preset", "veryfast", "-b:v", "6M", "-x265-params", "aud=1:keyint=30:min-keyint=30:bframes=2:scenecut=0:log-level=error", "-f", "hevc"])

BARS = [(192, 192, 192), (192, 192, 0), (0, 192, 192), (0, 192, 0), (192, 0, 192), (192, 0, 0), (0, 0, 192), (64, 64, 64)]
W, H, FRAMES = 1920, 1080, 30


def ycbcr(rgb, matrix, full):
    kr, kb = (0.2126, 0.0722) if matrix == 709 else (0.299, 0.114)
    r, g, b = (v / 255 for v in rgb)
    y = kr * r + (1 - kr - kb) * g + kb * b
    cb = (b - y) / (2 * (1 - kb))
    cr = (r - y) / (2 * (1 - kr))
    if full:
        return round(y * 255), round(cb * 255 + 128), round(cr * 255 + 128)
    return round(16 + y * 219), round(128 + cb * 224), round(128 + cr * 224)


for matrix in (709, 601):
    for full in (False, True):
        tag = f"bars{matrix}{'full' if full else 'lim'}"
        bw = W // 8
        vals = [ycbcr(c, matrix, full) for c in BARS]
        yrow = b"".join(bytes([v[0]]) * bw for v in vals)
        urow = b"".join(bytes([v[1]]) * (bw // 2) for v in vals)
        vrow = b"".join(bytes([v[2]]) * (bw // 2) for v in vals)
        frame = yrow * H + urow * (H // 2) + vrow * (H // 2)
        raw = os.path.join(OUT, tag + ".yuv")
        with open(raw, "wb") as fh:
            for _ in range(FRAMES):
                fh.write(frame)
        cm = "bt709" if matrix == 709 else "smpte170m"
        params = f"aud=1:keyint=30:colormatrix={cm}:colorprim={cm}:transfer={cm}:fullrange={'on' if full else 'off'}"
        run(["-f", "rawvideo", "-pix_fmt", "yuv420p", "-s", f"{W}x{H}", "-r", "30", "-i", raw,
             "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high444" if False else "high", "-qp", "2", "-bf", "0",
             "-x264-params", params, "-f", "h264", os.path.join(OUT, tag + ".h264")])
        os.remove(raw)

print("done")
