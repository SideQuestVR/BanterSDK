# Fixtures and an independent oracle for the plain MP4 reader. ffprobe reports every packet; the tests must match it exactly.
import csv, json, os, subprocess, sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
AV = ["-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=4", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=4"]
X264 = ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-bf", "2", "-g", "30", "-pix_fmt", "yuv420p", "-x264-params", "aud=1"]
AAC = ["-c:a", "aac", "-b:a", "96k"]


def make(name, args):
    subprocess.run(["ffmpeg", "-y", "-v", "error"] + args + [os.path.join(OUT, name)], check=True)


make("first.mp4", AV + X264 + AAC + ["-movflags", "+faststart"])
make("last.mp4", AV + X264 + AAC)
make("baseline.mp4", AV + ["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "baseline", "-pix_fmt", "yuv420p"] + AAC + ["-movflags", "+faststart"])
make("hevc.mp4", AV + ["-c:v", "libx265", "-preset", "veryfast", "-tag:v", "hvc1", "-x265-params", "bframes=2:log-level=error", "-pix_fmt", "yuv420p"] + AAC + ["-movflags", "+faststart"])
make("hev1.mp4", AV + ["-c:v", "libx265", "-preset", "veryfast", "-tag:v", "hev1", "-x265-params", "log-level=error", "-pix_fmt", "yuv420p"] + AAC)
make("videoonly.mp4", ["-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=4"] + X264)
make("audioonly.m4a", ["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=4"] + AAC)
make("stereo44.mp4", ["-f", "lavfi", "-i", "testsrc2=s=320x240:r=25:d=4", "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100:d=4", "-ac", "2"] + X264 + AAC)
make("fragmented.mp4", AV + X264 + AAC + ["-movflags", "+frag_keyframe+empty_moov"])
# Each track in half-second chunks, as the real world files are (ffmpeg alternates sample by sample, which a single window
# copes with and so hides the problem; chunkify.py regroups them). Big enough that the chunks sit far apart.
make("interleaved_src.mp4", ["-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=30"]
     + ["-c:v", "libx264", "-preset", "veryfast", "-b:v", "4M", "-bf", "2", "-g", "60", "-pix_fmt", "yuv420p"] + AAC + ["-movflags", "+faststart"])
subprocess.run([sys.executable, "-I", os.path.join(os.path.dirname(os.path.abspath(__file__)), "chunkify.py"), os.path.join(OUT, "interleaved_src.mp4"), os.path.join(OUT, "interleaved.mp4"), "0.5"], check=True)
os.remove(os.path.join(OUT, "interleaved_src.mp4"))
make("tiny.mp4", ["-f", "lavfi", "-i", "testsrc2=s=64x48:r=10:d=1", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:d=1"] + X264 + AAC + ["-movflags", "+faststart"])

for name in sorted(os.listdir(OUT)):
    if not name.endswith((".mp4", ".m4a")) or name == "fragmented.mp4":
        continue
    path = os.path.join(OUT, name)
    r = subprocess.run(["ffprobe", "-v", "error", "-show_data_hash", "MD5", "-show_entries",
                        "stream=index,codec_type,time_base,width,height:packet=stream_index,pts,dts,flags,size,data_hash", "-of", "json", path],
                       capture_output=True, text=True, check=True)
    d = json.loads(r.stdout)
    tb = {}
    kinds = {}
    for s in d["streams"]:
        a, b = s["time_base"].split("/")
        tb[s["index"]] = (int(a), int(b))
        kinds[s["index"]] = s["codec_type"]

    def us(v, i):
        a, b = tb[i]
        return int(v) * a * 1000000 / b

    with open(path + ".csv", "w", newline="") as fh:
        w = csv.writer(fh)
        for p in d["packets"]:
            i = p["stream_index"]
            w.writerow([kinds[i], us(p["pts"], i), us(p["dts"], i), 1 if "K" in p["flags"] else 0, p["size"], p["data_hash"].split(":")[1]])
    print(name, len(d["packets"]), "packets")
