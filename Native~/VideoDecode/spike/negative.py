# Negative controls for the spike's own detectors. Each clip is wrong on purpose, so a clean result would mean a blind detector.
import os, subprocess, sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
X264 = "aud=1:keyint=30:min-keyint=30:scenecut=0"
COMMON = ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-bf", "2", "-b:v", "8M", "-x264-params", X264, "-f", "h264"]
SRC = ["-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=30:d=3", "-f", "lavfi", "-i", "color=black:s=1024x32:r=30:d=3"]
GOOD = "geq=lum='if(bitand(N,pow(2,floor(X/64))),235,16)'"
OFF = "geq=lum='if(bitand(N+1,pow(2,floor(X/64))),235,16)'"

# A frame removed after the counter was burned in: the decoder must report a jump.
skip = f"[1:v]format=gray,{GOOD},split[a][b];[0:v][a]overlay=0:0[t];[t][b]overlay=0:H-32,format=yuv420p,select='not(eq(n\\,40))',setpts=N/30/TB"
subprocess.run(["ffmpeg", "-y", "-v", "error"] + SRC + ["-filter_complex", skip] + COMMON + [os.path.join(OUT, "skip.h264")], check=True)

# Top and bottom counters disagree on every frame, which is how a torn frame looks to the consumer.
tear = f"[1:v]format=gray,split[x][y];[x]{GOOD}[a];[y]{OFF}[b];[0:v][a]overlay=0:0[t];[t][b]overlay=0:H-32,format=yuv420p"
subprocess.run(["ffmpeg", "-y", "-v", "error"] + SRC + ["-filter_complex", tear] + COMMON + [os.path.join(OUT, "tear.h264")], check=True)
print("made")
