# Negative control for the range source: each mutant is a mistake this code could really make.
import os, re, shutil, subprocess, sys

ROOT, FX = sys.argv[1], sys.argv[2]
MUTANTS = [
    ("quiet read counts as the end", "if (read < 0)", "if (read <= 0)"),
    ("no stall limit", "if (waited >= _stallMilliseconds)", "if (false)"),
    ("ignores cancellation", "_fetcher.Wait(0);", ""),
    ("trusts a 200 on a range", 'if (response.Status == 200 && position > 0)', "if (false)"),
    ("a dropped connection is silent", 'return response.Interrupted && window.Count == 0 ? " dropped the connection" : null;', "return null;"),
    ("always the biggest window", "var want = Math.Max(window.Target, Math.Min(count, _largestWindow));", "var want = _largestWindow;"),
    ("gives up on a stall part way", 'return window.Count > 0 ? null : " stopped sending";', 'return " stopped sending";'),
    ("never tries again", "if (attempt + 1 >= MaxEmptyAttempts)", "if (true)"),
    ("tries for ever", "if (attempt + 1 >= MaxEmptyAttempts)", "if (false)"),
    ("tracks share one window", "value % (Windows - 1)", "0"),
    ("a window never grows", "window.Target = forward ? Math.Min(_largestWindow, window.Target * 2) : SmallestWindow;", "window.Target = SmallestWindow;"),
    ("the reader names no lane", "laned.Lane = _tables.IndexOf(best);", "", "PlainMp4Reader.cs"),
]
work = os.path.join(ROOT, "mut")
# The runtime's own Core, copied and mutated; the test project compiles whatever VideoStreamCore names.
CORE = os.path.normpath(os.path.join(ROOT, "..", "..", "Runtime", "Scripts", "VideoStream", "Core"))
ONLY = set(sys.argv[3:])
for name, old, new, *where in [m for m in MUTANTS if not ONLY or m[0] in ONLY]:
    if os.path.exists(work):
        shutil.rmtree(work)
    shutil.copytree(CORE, os.path.join(work, "core"), ignore=shutil.ignore_patterns("*.meta"))
    os.makedirs(os.path.join(work, "tests"))
    for f in os.listdir(os.path.join(ROOT, "tests")):
        p = os.path.join(ROOT, "tests", f)
        if os.path.isfile(p):
            shutil.copy(p, os.path.join(work, "tests", f))
    src = os.path.join(work, "core", where[0] if where else "HttpRangeSource.cs")
    text = open(src, encoding="utf-8", newline="").read().replace("\r\n", "\n")
    assert old in text, old
    open(src, "w", encoding="utf-8", newline="").write(text.replace(old, new, 1))
    env = dict(os.environ, MP4_FIXTURES=FX, VideoStreamCore=os.path.join(work, "core"), AudioStreamCore=os.environ.get("AudioStreamCore") or os.path.normpath(os.path.join(ROOT, "..", "..", "Runtime", "Scripts", "AudioStream", "Core")))
    try:
        r = subprocess.run(["dotnet", "run", "-c", "Release", "--", "--where", "class == VideoStreamTests.RangeTests"], cwd=os.path.join(work, "tests"),
                           capture_output=True, text=True, env=env, timeout=45)
        m = re.search(r"Test Count: (\d+), Passed: (\d+), Failed: (\d+)", r.stdout)
        verdict = "NOT CAUGHT" if m and m.group(3) == "0" else "caught"
        print(f"{name:<34} {verdict:<11} {m.group(0) if m else 'build or runner failed: ' + (r.stdout + r.stderr)[-140:]}", flush=True)
    except subprocess.TimeoutExpired:
        print(f"{name:<34} caught      (hung, killed at 45 s)", flush=True)
shutil.rmtree(work, ignore_errors=True)
