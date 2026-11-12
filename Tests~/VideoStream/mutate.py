# Negative control: break the reader on purpose, in a copy, and confirm the tests notice. A suite that can't fail proves nothing.
import os, re, shutil, subprocess, sys

ROOT = sys.argv[1]
FX = sys.argv[2]
BOUND = "if (entries > (uint)(end - body - 8) / 8)"
MUTANTS = [
    ("ignore the edit list", "table.Shift = mediaTime - emptyTicks;", "table.Shift = 0;", 1),
    ("drop composition offsets", "offsets[sample++] = offset;", "offsets[sample++] = 0;", 1),
    ("off-by-one keyframes", "var index = (long)Big32(_moov, body + 8 + i * 4) - 1;", "var index = (long)Big32(_moov, body + 8 + i * 4);", 1),
    ("no size-table bound", "if (uniform == 0 && count > (uint)(end - body - 12) / 4)", "if (false)", 1),
    ("no sample count cap", "if (count > MaxSamples)", "if (false)", 1),
    ("no time-table bound", BOUND, "if (false)", 1),
    ("no ctts bound", BOUND, "if (false)", 2),
    ("no chunk-table bound", "if (runs > (uint)(stscEnd - stsc - 8) / 12 || chunkCount > (uint)(chunksEnd - chunks - 8) / width)", "if (false)", 1),
    ("no keyframe-table bound", "if (count > (uint)(end - body - 8) / 4)", "if (false)", 1),
    ("keyframe past the samples", "if (index <= last || index >= table.Size.Length)", "if (false)", 1),
    ("no short time-table check", "if (sample != dts.Length)", "if (false)", 1),
    ("no short chunk-table check", "if (sample != offsets.Length)", "if (false)", 1),
]
work = os.path.join(ROOT, "mut")
# The runtime's own Core, copied and mutated; the test project compiles whatever VideoStreamCore names.
CORE = os.path.normpath(os.path.join(ROOT, "..", "..", "Runtime", "Scripts", "VideoStream", "Core"))


def replace_nth(text, old, new, n):
    at = -1
    for _ in range(n):
        at = text.index(old, at + 1)
    return text[:at] + new + text[at + len(old):]


# The first run changes nothing and must pass, or every "caught" below means nothing.
for name, old, new, nth in [("control (no mutation)", None, None, 0)] + MUTANTS:
    if os.path.exists(work):
        shutil.rmtree(work)
    shutil.copytree(CORE, os.path.join(work, "core"), ignore=shutil.ignore_patterns("*.meta"))
    os.makedirs(os.path.join(work, "tests"))
    for f in os.listdir(os.path.join(ROOT, "tests")):
        p = os.path.join(ROOT, "tests", f)
        if os.path.isfile(p):
            shutil.copy(p, os.path.join(work, "tests", f))
    if old is not None:
        src = os.path.join(work, "core", "PlainMp4Reader.cs")
        text = open(src, encoding="utf-8").read()
        open(src, "w", encoding="utf-8").write(replace_nth(text, old, new, nth))
    env = dict(os.environ, MP4_FIXTURES=FX, VideoStreamCore=os.path.join(work, "core"),
               AudioStreamCore=os.path.normpath(os.path.join(ROOT, "..", "..", "Runtime", "Scripts", "AudioStream", "Core")))
    # The random-damage test is slow, and these mutants are aimed at the reader's directed tests; the session and review
    # tests need SESSION_FIXTURES, and without it they fail whatever the mutant.
    where = "test !~ /Damaged_files/ && class !~ /SessionTests|ReviewTests/"
    try:
        r = subprocess.run(["dotnet", "run", "-c", "Release", "--", "--where", where], cwd=os.path.join(work, "tests"),
                           capture_output=True, text=True, env=env, timeout=120)
        m = re.search(r"Test Count: (\d+), Passed: (\d+), Failed: (\d+)", r.stdout)
        if old is None:
            if not m or m.group(3) != "0":
                print(f"{name:<30} FAILED      {m.group(0) if m else 'runner died: ' + r.stdout[-120:]}; stopping", flush=True)
                break
            print(f"{name:<30} passes      {m.group(0)}", flush=True)
            continue
        verdict = "NOT CAUGHT" if m and m.group(3) == "0" else "caught"
        print(f"{name:<30} {verdict:<11} {m.group(0) if m else 'runner died: ' + r.stdout[-120:]}", flush=True)
    except subprocess.TimeoutExpired:
        print(f"{name:<30} caught      (hung, killed at 120 s)", flush=True)
shutil.rmtree(work, ignore_errors=True)
