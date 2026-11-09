// bsvdtool (Windows): drives BSVideoDecode's C API as Runtime/Scripts/VideoStream/VideoDecoderNative.cs does (Create,
// Feed with BSVD_AGAIN retries, SetClock every frame, SetTarget, the present render event, GetStatus, LockFrame on the
// CPU path, Destroy, GetLiveCount), with a stand-in for Unity: its plugin interfaces and device events, a D3D11 device
// that only this "render thread" uses, and real RGBA32 target textures read back after each present.
//
//   bsvdtool caps [--adapter <name>]
//   bsvdtool all <fixture dir> [--adapter <name>] [--dll <BSVideoDecode.dll>] [--hz 90]
//   bsvdtool play <clip> [--target WxH] [--cpu] [--check counter|crop|bars|none] [--dump <file.ppm>] [--adapter <name>]
//   bsvdtool test seek|destroy <fixture dir> [--cpu] [--repeat n] [--rounds n] [--sample n] [--adapter <name>]
//
// Fixtures, in one folder: tools/bsvdtool/win_fixtures.py (counter, crop, SD and 10-bit clips with .pkt sidecars) and
// spike/fixtures.py (bars601full/lim and bars709full/lim.h264, no B-frames, no sidecar). Every check prints its numbers;
// `all` ends with RESULT PASS or RESULT FAIL and exits 0 only on PASS.
#include "win_harness.h"

#include "win/win.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <functional>
#include <map>
#include <memory>
#include <psapi.h>
#include <random>
#include <string>
#include <thread>
#include <timeapi.h>
#include <vector>

using namespace tool;

namespace {

// ---------------------------------------------------------------- options and results

struct Options {
    std::string adapter, dll, dump, check = "counter", fixtures;
    int hz = 90;
    int targetWidth = 0, targetHeight = 0;
    bool cpu = false;
    int repeat = 1, rounds = 20, sampleEvery = 0;
    std::string clip2;  // test destroy: the third decoder's clip
};
Options gOpt;

struct Result {
    std::string name;
    bool pass;
    std::string detail;
};
std::vector<Result> gResults;

void Report(const std::string& name, bool pass, const std::string& detail) {
    std::printf("%-30s %s  %s\n", name.c_str(), pass ? "PASS" : "FAIL", detail.c_str());
    gResults.push_back({name, pass, detail});
}

std::string Fmt(const char* format, ...) {
    char buffer[1024];
    va_list args;
    va_start(args, format);
    std::vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    return buffer;
}

// ---------------------------------------------------------------- one decoder under test

enum class Check { None, Counter, Crop, Bars };

struct Stream {
    Api* api = nullptr;
    const Clip* clip = nullptr;
    Check check = Check::Counter;
    std::string label;
    int32_t handle = 0;
    bool cpu = false;
    int width = 0, height = 0;  // the target
    ComPtr<ID3D11Texture2D> target, rows;

    // the clock, as the player sets it each frame
    bool clockSet = false, running = false;
    double rate = 1.0;
    int64_t clockBase = 0, clockAt = 0;

    // the feeding thread
    std::thread feeder;
    std::atomic<bool> stopFeeding{false}, fed{false};
    std::atomic<int32_t> feedError{0};
    std::atomic<int64_t> again{0};

    // observations
    int64_t seenShown = 0;
    int last = -1, first = -1;
    long frames = 0, order = 0, ptsWrong = 0, tears = 0, flipped = 0, garbage = 0, skips = 0, cropBad = 0;
    double barWorst = -1;
    int barWorstIndex = -1;
    std::vector<double> lateMs;
    std::vector<int> shown;
    BSVD_Status status{};

    ~Stream() { StopFeeding(); }

    int64_t MediaNow() const {
        if (!clockSet) return BSVD_NO_TIME;
        return running ? clockBase + (int64_t)((double)(NowUs() - clockAt) * rate) : clockBase;
    }
    void StartClock(int64_t mediaUs, bool run, double r = 1.0) {
        clockSet = true;
        running = run;
        rate = r;
        clockBase = mediaUs;
        clockAt = NowUs();
        api->SetClock(handle, mediaUs, run ? 1 : 0);
    }
    void StartFeeding(size_t from, size_t to, bool end) {
        StopFeeding();
        stopFeeding = false;
        fed = false;
        feeder = std::thread([this, from, to, end] {
            for (size_t i = from; i < to && !stopFeeding; ++i) {
                const auto& u = clip->units[i];
                for (;;) {
                    int32_t r = api->Feed(handle, clip->data.data() + u.at, (int32_t)u.size, u.ptsUs, u.key ? BSVD_UNIT_KEYFRAME : 0);
                    if (r == BSVD_OK) break;
                    if (r == BSVD_AGAIN) {
                        again++;
                        if (stopFeeding) return;
                        std::this_thread::sleep_for(std::chrono::milliseconds(2));
                        continue;
                    }
                    feedError = r;
                    return;
                }
            }
            if (end && !stopFeeding) api->EndOfStream(handle);
            fed = true;
        });
    }
    void StopFeeding() {
        stopFeeding = true;
        if (feeder.joinable()) feeder.join();
    }
    void ResetObservations() {
        last = first = -1;
        frames = order = ptsWrong = tears = flipped = garbage = skips = cropBad = 0;
        barWorst = -1;
        lateMs.clear();
        shown.clear();
    }
};

// Source picture row s (0 = top) and column x to the target's memory layout (scaled, bottom row first).
int MemRow(int s, int sourceHeight, int h) { return h - 1 - (int)(((int64_t)2 * s + 1) * h / (2 * (int64_t)sourceHeight)); }
int MemCol(int x, int sourceWidth, int w) { return (int)(((int64_t)2 * x + 1) * w / (2 * (int64_t)sourceWidth)); }

std::vector<int> SourceRows(Check check, int sh) {
    switch (check) {
        case Check::Counter: return {16, sh - 16};
        case Check::Crop: return {1, 16, 30, 40, sh / 2, sh - 37, sh - 31, sh - 17, sh - 16, sh - 2};
        case Check::Bars: return {sh / 2};
        default: return {};
    }
}

struct Picture {
    std::function<const uint8_t*(int sourceRow)> row;
    int w = 0, h = 0, sw = 0, sh = 0;
};

int Bits(const Picture& p, int sourceRow) {
    const uint8_t* row = p.row(sourceRow);
    int v = 0;
    for (int b = 0; b < 16; ++b)
        if (row[(size_t)MemCol(32 + 64 * b, p.sw, p.w) * 4] > 128) v |= 1 << b;
    return v;
}

const int kBars[8][3] = {{192, 192, 192}, {192, 192, 0}, {0, 192, 192}, {0, 192, 0}, {192, 0, 192}, {192, 0, 0}, {0, 0, 192}, {64, 64, 64}};

void Analyze(Stream& s, const Picture& p, int64_t pts) {
    s.frames++;
    int expected = FrameIndex(pts);
    if (s.check == Check::Counter || s.check == Check::Crop) {
        int top = Bits(p, 16), bottom = Bits(p, p.sh - 16);
        if (!(top & 0x8000) || !(bottom & 0x8000)) {
            s.garbage++;
            return;
        }
        int n = top & 0x7FFF, nb = 32767 - (bottom & 0x7FFF), counter = n;
        if (n != nb) s.tears++;
        if (n != expected) {
            if (32767 - n == expected) {
                s.flipped++;
                counter = expected;
            } else {
                s.ptsWrong++;
            }
        }
        if (s.last >= 0) {
            if (counter <= s.last) s.order++;
            else s.skips += counter - s.last - 1;
        }
        s.last = counter;
        if (s.first < 0) s.first = counter;
        s.shown.push_back(counter);
        if (s.check == Check::Crop) {
            // Strips at rows 0-31 and h-32 to h-1, black between: a wrong crop or scale moves a strip edge.
            int topValue = counter | 0x8000, bottomValue = (32767 - counter) | 0x8000;
            const int sh = p.sh;
            const std::pair<int, int> want[] = {{1, topValue},  {16, topValue},       {30, topValue},
                                                {40, 0},        {sh / 2, 0},          {sh - 37, 0},
                                                {sh - 31, bottomValue}, {sh - 17, bottomValue}, {sh - 2, bottomValue}};
            for (auto& [row, value] : want)
                if (Bits(p, row) != value) {
                    s.cropBad++;
                    break;
                }
        }
    } else if (s.check == Check::Bars) {
        const uint8_t* row = p.row(p.sh / 2);
        for (int b = 0; b < 8; ++b) {
            const uint8_t* px = row + (size_t)MemCol((2 * b + 1) * p.sw / 16, p.sw, p.w) * 4;
            for (int c = 0; c < 3; ++c) {
                double e = std::fabs((double)px[c] - kBars[b][c]);
                if (e > s.barWorst) {
                    s.barWorst = e;
                    s.barWorstIndex = b;
                }
            }
        }
    }
}

// ---------------------------------------------------------------- the stand-in render thread

struct Harness {
    Api* api = nullptr;
    Device* unity = nullptr;
    int hz = 90;

    bool Open(Stream& s, const Clip& clip, Check check, int w, int h, bool cpu, bool setTarget = true, int maxWidth = 0,
              DXGI_FORMAT format = DXGI_FORMAT_R8G8B8A8_TYPELESS, int textureWidth = 0, int textureHeight = 0) {
        s.api = api;
        s.clip = &clip;
        s.check = check;
        s.cpu = cpu;
        s.width = w;
        s.height = h;
        BSVD_Config c{};
        c.codec = clip.codec;
        c.colourMatrix = 0;
        c.fullRange = -1;
        c.maxWidth = maxWidth;
        c.maxHeight = maxWidth;
        s.handle = api->Create(&c, nullptr, 0);
        if (s.handle <= 0) {
            std::printf("  BSVD_Create failed: %d\n", s.handle);
            return false;
        }
        if (!cpu) {
            // As Unity makes an RGBA32 Texture2D on D3D11: typeless, so it can be sampled as sRGB or linear.
            D3D11_TEXTURE2D_DESC td{};
            td.Width = (UINT)(textureWidth ? textureWidth : w);
            td.Height = (UINT)(textureHeight ? textureHeight : h);
            td.MipLevels = 1;
            td.ArraySize = 1;
            td.Format = format;
            td.SampleDesc.Count = 1;
            td.Usage = D3D11_USAGE_DEFAULT;
            td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            if (FAILED(unity->device->CreateTexture2D(&td, nullptr, &s.target))) return false;
            td.Width = (UINT)w;
            td.Height = 16;
            td.Usage = D3D11_USAGE_STAGING;
            td.BindFlags = 0;
            td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            if (FAILED(unity->device->CreateTexture2D(&td, nullptr, &s.rows))) return false;
        }
        if (setTarget) SetTarget(s);
        return true;
    }

    void SetTarget(Stream& s) { api->SetTarget(s.handle, s.cpu ? nullptr : s.target.Get(), s.width, s.height); }

    void Inspect(Stream& s) {
        if (s.cpu) {
            void* pixels = nullptr;
            int32_t w = 0, h = 0;
            int32_t r = api->LockFrame(s.handle, &pixels, &w, &h);
            api->GetStatus(s.handle, &s.status, (int32_t)sizeof(s.status));
            if (r != 1) return;
            Picture p;
            p.w = w;
            p.h = h;
            p.sw = s.status.videoWidth;
            p.sh = s.status.videoHeight;
            const uint8_t* base = (const uint8_t*)pixels;
            p.row = [&](int sourceRow) { return base + (size_t)MemRow(sourceRow, p.sh, h) * (size_t)w * 4; };
            Note(s, p);
            api->UnlockFrame(s.handle);
            return;
        }
        api->GetStatus(s.handle, &s.status, (int32_t)sizeof(s.status));
        if (s.status.framesShown == s.seenShown) return;
        s.seenShown = s.status.framesShown;
        if (s.check == Check::None || s.status.videoHeight <= 0) {
            Note(s, Picture{});
            return;
        }
        std::vector<int> sources = SourceRows(s.check, s.status.videoHeight);
        auto* ctx = unity->context.Get();
        for (size_t i = 0; i < sources.size(); ++i) {
            int row = MemRow(sources[i], s.status.videoHeight, s.height);
            D3D11_BOX box{0, (UINT)row, 0, (UINT)s.width, (UINT)row + 1, 1};
            ctx->CopySubresourceRegion(s.rows.Get(), 0, 0, (UINT)i, 0, s.target.Get(), 0, &box);
        }
        D3D11_MAPPED_SUBRESOURCE m{};
        if (FAILED(ctx->Map(s.rows.Get(), 0, D3D11_MAP_READ, 0, &m))) return;
        Picture p;
        p.w = s.width;
        p.h = s.height;
        p.sw = s.status.videoWidth;
        p.sh = s.status.videoHeight;
        const uint8_t* base = (const uint8_t*)m.pData;
        p.row = [&](int sourceRow) {
            for (size_t i = 0; i < sources.size(); ++i)
                if (sources[i] == sourceRow) return base + i * m.RowPitch;
            std::printf("bsvdtool bug: source row %d was not read back\n", sourceRow);
            std::abort();
            return base;
        };
        Note(s, p);
        ctx->Unmap(s.rows.Get(), 0);
    }

    void Note(Stream& s, const Picture& p) {
        int64_t pts = s.status.shownPtsUs;
        if (s.frames > 0 && s.clockSet && s.running) s.lateMs.push_back((double)(s.MediaNow() - pts) / 1000.0);
        if (p.row) Analyze(s, p, pts);
        else s.frames++;
    }

    // Unity's frame: clocks, the present event, then whatever looks at the textures. Until done() or timeoutMs.
    bool Run(const std::vector<Stream*>& streams, const std::function<bool()>& done, int timeoutMs) {
        // As the C# side: the function and the id the library reserved (the stand-in Unity hands out real ids, so
        // BSVD_EVENT_PRESENT would be ignored).
        BSVD_RenderEvent present = api->GetRenderEventFunc();
        const int eventId = api->GetRenderEventId();
        int64_t start = NowUs(), tick = 1000000 / hz;
        int64_t next = start;
        for (;;) {
            for (Stream* s : streams)
                if (s->handle > 0 && s->clockSet) api->SetClock(s->handle, s->MediaNow(), s->running ? 1 : 0);
            present(eventId);
            for (Stream* s : streams)
                if (s->handle > 0) Inspect(*s);
            if (done && done()) return true;
            if (NowUs() - start > (int64_t)timeoutMs * 1000) return false;
            next += tick;
            int64_t wait = next - NowUs();
            if (wait > 0) std::this_thread::sleep_for(std::chrono::microseconds(wait));
            else next = NowUs();
        }
    }

    // Keeps presenting until every destroyed decoder has been released. The time it took, or -1.
    int64_t Drain(int timeoutMs = 3000) {
        int64_t start = NowUs();
        bool ok = Run({}, [&] { return api->GetLiveCount() == 0; }, timeoutMs);
        return ok ? (NowUs() - start) / 1000 : -1;
    }

    void Destroy(Stream& s) {
        s.StopFeeding();
        if (s.handle > 0) api->Destroy(s.handle);
        s.handle = 0;
    }
};

double Percentile(std::vector<double> v, double q) {
    if (v.empty()) return 0;
    std::sort(v.begin(), v.end());
    return v[std::min(v.size() - 1, (size_t)(q * (double)(v.size() - 1) + 0.5))];
}

std::string Summary(const Stream& s) {
    return Fmt("shown=%ld/%lld decoded=%lld dropped=%lld order=%ld pts=%ld tears=%ld flipped=%ld garbage=%ld skips=%ld "
               "late_ms p50=%.1f p95=%.1f max=%.1f again=%lld",
               s.frames, (long long)s.clip->frames, (long long)s.status.framesDecoded, (long long)s.status.framesDropped, s.order,
               s.ptsWrong, s.tears, s.flipped, s.garbage, s.skips, Percentile(s.lateMs, 0.5), Percentile(s.lateMs, 0.95),
               s.lateMs.empty() ? 0.0 : *std::max_element(s.lateMs.begin(), s.lateMs.end()), (long long)s.again.load());
}

// ---------------------------------------------------------------- tests

struct Spec {
    const Clip* clip;
    Check check;
    int w, h;
    std::string label;
};

// Plays clips together as a player would: preroll with no clock (the first picture must appear), then the clock runs at
// 1x from 0 until every clip has shown its last frame (or maxFrames). Checks order, presentation times against the
// burned-in counters, tearing, orientation, skips and lateness.
bool Paced(Harness& h, const std::string& name, const std::vector<Spec>& specs, bool cpu, int maxFrames = 0,
           double skipBudget = 0.01, double lateBudgetMs = 15.0) {
    std::vector<std::unique_ptr<Stream>> streams;
    std::vector<Stream*> raw;
    for (auto& spec : specs) {
        auto s = std::make_unique<Stream>();
        s->label = spec.label;
        if (!h.Open(*s, *spec.clip, spec.check, spec.w, spec.h, cpu)) {
            Report(name, false, "could not open " + spec.clip->name);
            return false;
        }
        raw.push_back(s.get());
        streams.push_back(std::move(s));
    }
    for (auto* s : raw) s->StartFeeding(0, s->clip->units.size(), true);
    bool preroll = h.Run(raw, [&] {
        for (auto* s : raw) if (s->frames < 1) return false;
        return true;
    }, 8000);
    for (auto* s : raw) s->StartClock(0, true);
    int64_t startUs = NowUs();
    auto lastFrame = [&](Stream* s) { return maxFrames > 0 ? std::min(maxFrames, s->clip->frames) - 1 : s->clip->frames - 1; };
    bool finished = h.Run(raw, [&] {
        for (auto* s : raw) {
            if (s->status.state == BSVD_STATE_FAILED) continue;
            if (s->status.shownPtsUs == BSVD_NO_TIME || s->status.shownPtsUs < FramePts(lastFrame(s))) return false;
        }
        return true;
    }, (int)(1000.0 * (maxFrames > 0 ? maxFrames : 300) / kFps) + 6000);
    double wall = (double)(NowUs() - startUs) / 1e6;
    bool pass = preroll && finished;
    std::string detail;
    for (auto* s : raw) {
        bool ok = s->status.state != BSVD_STATE_FAILED && s->first == 0 && s->order == 0 && s->ptsWrong == 0 && s->tears == 0 &&
                  s->flipped == 0 && s->garbage == 0 && s->cropBad == 0 && s->feedError == 0 &&
                  s->skips <= (long)std::ceil(skipBudget * (lastFrame(s) + 1)) && Percentile(s->lateMs, 0.95) <= lateBudgetMs;
        if (s->check == Check::Bars) ok = s->status.state != BSVD_STATE_FAILED && s->barWorst >= 0 && s->barWorst <= 2.0;
        pass = pass && ok;
        std::string line = Fmt("\n    %s %s first=%d %s", s->label.c_str(), s->clip->name.c_str(), s->first, Summary(*s).c_str());
        if (s->check == Check::Crop) line += Fmt(" crop_bad=%ld", s->cropBad);
        if (s->check == Check::Bars) line += Fmt(" bar_worst=%.0f (bar %d) colour=%d/%d", s->barWorst, s->barWorstIndex, s->status.colourMatrix, s->status.fullRange);
        if (s->status.state == BSVD_STATE_FAILED) line += " FAILED: " + h.api->Text(s->handle, BSVD_TEXT_MESSAGE);
        detail += line;
    }
    for (auto* s : raw) h.Destroy(*s);
    int64_t drainMs = h.Drain();
    pass = pass && drainMs >= 0;
    Report(name, pass, Fmt("preroll=%s finished=%s wall=%.1fs released_in=%lldms", preroll ? "yes" : "NO", finished ? "yes" : "NO", wall,
                           (long long)drainMs) + detail);
    return pass;
}

// The first picture appears with no clock, stays while the clock is paused, and appears for a target set late.
void Preroll(Harness& h, const Clip& clip, bool cpu) {
    {
        Stream s;
        h.Open(s, clip, Check::Counter, 1920, 1080, cpu);
        s.StartFeeding(0, clip.units.size(), false);
        h.Run({&s}, nullptr, 1000);
        int64_t shownNoClock = s.status.framesShown;
        int64_t ptsNoClock = s.status.shownPtsUs;
        s.StartClock(0, false);
        h.Run({&s}, nullptr, 500);
        bool pass = shownNoClock == 1 && ptsNoClock == 0 && s.first == 0 && s.status.framesShown == 1 && s.status.heldFrames > 0 &&
                    s.garbage == 0 && s.tears == 0 && s.flipped == 0;
        Report(cpu ? "cpu preroll" : "preroll", pass,
               Fmt("no clock 1s: shown=%lld pts=%lld counter=%d; paused at 0 for 0.5s: shown=%lld held=%d decoded=%lld",
                   (long long)shownNoClock, (long long)ptsNoClock, s.first, (long long)s.status.framesShown, s.status.heldFrames,
                   (long long)s.status.framesDecoded));
        h.Destroy(s);
        h.Drain();
    }
    {
        Stream s;
        h.Open(s, clip, Check::Counter, 1920, 1080, cpu, false);
        s.StartFeeding(0, clip.units.size(), false);
        h.Run({&s}, nullptr, 500);
        BSVD_Status before = s.status;
        h.SetTarget(s);
        h.Run({&s}, [&] { return s.frames > 0; }, 3000);
        bool pass = before.framesShown == 0 && before.videoWidth == 1920 && before.videoHeight == 1080 && s.first == 0;
        Report(cpu ? "cpu preroll, late target" : "preroll, late target", pass,
               Fmt("before target: shown=%lld decoded=%lld size=%dx%d; after: first counter=%d", (long long)before.framesShown,
                   (long long)before.framesDecoded, before.videoWidth, before.videoHeight, s.first));
        h.Destroy(s);
        h.Drain();
    }
}

// Seeks: Flush with a discard time, feed from the keyframe before it, the clock paused at the target: the first picture
// after the flush must be exactly the target frame, and nothing from before the flush may appear.
void Seek(Harness& h, const Clip& clip, bool cpu) {
    Stream s;
    h.Open(s, clip, Check::Counter, 1920, 1080, cpu);
    s.StartFeeding(0, clip.units.size(), true);
    h.Run({&s}, [&] { return s.frames > 0; }, 5000);
    s.StartClock(0, true);
    h.Run({&s}, nullptr, 1000);
    int beforeLast = s.last;

    auto seekTo = [&](int frame) {
        s.StopFeeding();
        h.api->Flush(s.handle, FramePts(frame));
        BSVD_Status st{};
        h.api->GetStatus(s.handle, &st, (int32_t)sizeof(st));
        s.StartClock(FramePts(frame), false);
        s.ResetObservations();
        s.StartFeeding(clip.KeyBefore(frame), clip.units.size(), true);
        return st.shownPtsUs;
    };
    struct Case {
        int frame;
        int64_t shownAfterFlush;
        int first;
        int64_t ms;
    };
    std::vector<Case> cases;
    for (int frame : {165, 60, 241}) {
        int64_t at = NowUs();
        int64_t shownAfterFlush = seekTo(frame);
        h.Run({&s}, [&] { return s.frames > 0; }, 3000);
        cases.push_back({frame, shownAfterFlush, s.first, (NowUs() - at) / 1000});
    }
    // A storm of seeks while frames are held, then play on from the last one.
    std::mt19937 random(7);
    for (int i = 0; i < 12; ++i) {
        seekTo(30 + (int)(random() % 240));
        h.Run({&s}, nullptr, 10 + (int)(random() % 60));
    }
    int target = 200;
    seekTo(target);
    h.Run({&s}, [&] { return s.frames > 0; }, 3000);
    int stormFirst = s.first;
    s.StartClock(FramePts(target), true);
    h.Run({&s}, [&] { return s.last >= 290; }, 5000);
    bool pass = beforeLast > 20 && stormFirst == target && s.order == 0 && s.ptsWrong == 0 && s.tears == 0 && s.garbage == 0 &&
                s.flipped == 0 && s.last >= 290 && s.status.state != BSVD_STATE_FAILED;
    std::string detail = Fmt("played to %d; ", beforeLast);
    for (auto& c : cases) {
        pass = pass && c.first == c.frame && c.shownAfterFlush == BSVD_NO_TIME;
        detail += Fmt("seek %d: first=%d in %lldms (shownPts after flush %s); ", c.frame, c.first, (long long)c.ms,
                      c.shownAfterFlush == BSVD_NO_TIME ? "none" : "SET");
    }
    detail += Fmt("after 12 rapid seeks, seek %d: first=%d, then played to %d: order=%ld pts=%ld tears=%ld skips=%ld dropped=%lld",
                  target, stormFirst, s.last, s.order, s.ptsWrong, s.tears, s.skips, (long long)s.status.framesDropped);
    std::string gaps;
    for (size_t i = 1, n = 0; i < s.shown.size() && n < 8; ++i)
        if (s.shown[i] != s.shown[i - 1] + 1) {
            gaps += Fmt(" %d->%d", s.shown[i - 1], s.shown[i]);
            n++;
        }
    if (!gaps.empty()) detail += "; gaps:" + gaps;
    Report(cpu ? "cpu seek and discard" : "seek and discard", pass, detail);
    h.Destroy(s);
    h.Drain();
}

PROCESS_MEMORY_COUNTERS_EX Memory() {
    PROCESS_MEMORY_COUNTERS_EX m{};
    m.cb = sizeof(m);
    GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&m, sizeof(m));
    return m;
}

DWORD Handles() {
    DWORD n = 0;
    GetProcessHandleCount(GetCurrentProcess(), &n);
    return n;
}

// Destroys decoders mid-stream, with feeders still feeding, frames held and the clock running: every one must be
// released by later present events, with no leak over many rounds.
void DestroyWhileRunning(Harness& h, const Clip& a, const Clip& b, bool cpu, int rounds) {
    std::mt19937 random(11);
    auto round = [&](int i) -> int64_t {
        std::vector<std::unique_ptr<Stream>> streams;
        std::vector<Stream*> raw;
        for (int k = 0; k < 3; ++k) {
            auto s = std::make_unique<Stream>();
            h.Open(*s, k == 2 ? b : a, Check::None, 1280, 720, cpu);
            s->StartFeeding(0, s->clip->units.size(), true);
            s->StartClock(0, true);
            raw.push_back(s.get());
            streams.push_back(std::move(s));
        }
        h.Run(raw, nullptr, 50 + (int)(random() % 350));
        if (i % 4 == 3) {
            h.api->DestroyAll();
            for (auto* s : raw) s->handle = 0;
        } else {
            for (auto* s : raw) {
                h.api->Destroy(s->handle);
                s->handle = 0;
            }
        }
        int64_t ms = h.Drain();
        for (auto* s : raw) s->StopFeeding();
        return ms;
    };
    // Measured after a few rounds, so the pools Media Foundation and the drivers fill once don't count.
    for (int i = -4; i <= 0; ++i) round(i);
    auto memory0 = Memory();
    DWORD handles0 = Handles();
    int64_t worst = 0;
    int stuck = 0;
    long long peak[2] = {0, 0};  // the working set's peak in the first and second half of the rounds
    for (int i = 1; i <= rounds; ++i) {
        int64_t ms = round(i);
        if (ms < 0) stuck++;
        worst = std::max(worst, ms);
        long long& half = peak[i > rounds / 2 ? 1 : 0];
        half = std::max(half, (long long)(Memory().WorkingSetSize >> 20));
        if (gOpt.sampleEvery > 0 && i % gOpt.sampleEvery == 0)
            std::printf("  round %4d: private %lld MB, working set %lld MB, handles %lu, live %d\n", i,
                        (long long)(Memory().PrivateUsage >> 20), (long long)(Memory().WorkingSetSize >> 20), Handles(),
                        h.api->GetLiveCount());
    }
    auto memory1 = Memory();
    DWORD handles1 = Handles();
    long handleGrowth = (long)handles1 - (long)handles0;
    // Windows' HEVC Video Extension keeps one handle per decoder that has decoded (reproduced without this library, with
    // any shutdown sequence): allowed for, and reported.
    long hevcDecoders = (long)rounds * ((b.codec == BSVD_CODEC_HEVC ? 1 : 0) + (a.codec == BSVD_CODEC_HEVC ? 2 : 0));
    // Memory is sampled right after each round's decoders are freed, so single samples dip; private bytes also swing by
    // 100+ MB with GPU driver churn. A leak shows as the second half's peak working set climbing past the first's.
    bool pass = stuck == 0 && h.api->GetLiveCount() == 0 && handleGrowth < hevcDecoders + 16 && peak[1] - peak[0] < 64;
    Report(cpu ? "cpu destroy while running" : "destroy while running", pass,
           Fmt("%d rounds of 3 decoders (every 4th DestroyAll) after 5 to warm up: released in <= %lldms, stuck=%d, live=%d; "
               "handles %lu -> %lu (HEVC extension allowance %ld), peak working set %lld MB then %lld MB, private %lld MB -> %lld MB",
               rounds, (long long)worst, stuck, h.api->GetLiveCount(), handles0, handles1, hevcDecoders, peak[0], peak[1],
               (long long)(memory0.PrivateUsage >> 20), (long long)(memory1.PrivateUsage >> 20)));
}

// Bad input never crashes or hangs: oversize video, garbage units, a target texture of the wrong size or format.
void Errors(Harness& h, const Clip& clip, bool cpu) {
    {
        Stream s;
        h.Open(s, clip, Check::None, 1280, 720, cpu, true, 1280);
        s.StartFeeding(0, std::min<size_t>(60, clip.units.size()), false);
        h.Run({&s}, [&] { return s.status.state == BSVD_STATE_FAILED; }, 3000);
        std::string message = h.api->Text(s.handle, BSVD_TEXT_MESSAGE);
        int32_t feed = h.api->Feed(s.handle, clip.data.data(), 16, 0, 0);
        Report(cpu ? "cpu too large" : "too large", s.status.state == BSVD_STATE_FAILED && s.status.error == BSVD_ERROR_TOO_LARGE &&
                                                        feed == BSVD_E_FAILED && !message.empty(),
               Fmt("maxWidth 1280, 1920x1080 video: state=%d error=%d feed after=%d message=\"%s\"", s.status.state, s.status.error, feed,
                   message.c_str()));
        h.Destroy(s);
        h.Drain();
    }
    {
        Stream s;
        h.Open(s, clip, Check::None, 640, 360, cpu);
        std::mt19937 random(3);
        std::vector<uint8_t> junk(20000);
        int accepted = 0;
        for (int i = 0; i < 300; ++i) {
            for (auto& byte : junk) byte = (uint8_t)random();
            if (i % 3 == 0) {  // a start code and an IDR slice header byte, then noise
                junk[0] = 0;
                junk[1] = 0;
                junk[2] = 1;
                junk[3] = (uint8_t)(clip.codec == BSVD_CODEC_HEVC ? 0x26 : 0x65);
            }
            int32_t r = h.api->Feed(s.handle, junk.data(), (int32_t)(100 + random() % (junk.size() - 100)), FramePts(i), BSVD_UNIT_KEYFRAME);
            accepted += r == BSVD_OK;
            if (r == BSVD_AGAIN) std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
        s.StartClock(0, true);
        h.Run({&s}, nullptr, 1500);
        h.Destroy(s);
        int64_t ms = h.Drain();
        Report(cpu ? "cpu garbage input" : "garbage input", ms >= 0,
               Fmt("300 random units flagged as keyframes: accepted=%d decoded=%lld state=%d error=%d, released in %lldms", accepted,
                   (long long)s.status.framesDecoded, s.status.state, s.status.error, (long long)ms));
    }
    if (cpu) return;
    struct Bad {
        const char* what;
        DXGI_FORMAT format;
        int tw, th;
    } bad[] = {{"target texture of the wrong size", DXGI_FORMAT_R8G8B8A8_TYPELESS, 640, 360},
               {"target texture of the wrong format", DXGI_FORMAT_B8G8R8A8_UNORM, 0, 0}};
    for (auto& b : bad) {
        Stream s;
        h.Open(s, clip, Check::None, 1280, 720, cpu, true, 0, b.format, b.tw, b.th);
        s.StartFeeding(0, std::min<size_t>(30, clip.units.size()), false);
        h.Run({&s}, [&] { return s.status.state == BSVD_STATE_FAILED; }, 3000);
        std::string message = h.api->Text(s.handle, BSVD_TEXT_MESSAGE);
        Report(b.what, s.status.state == BSVD_STATE_FAILED && s.status.error == BSVD_ERROR_GRAPHICS,
               Fmt("state=%d error=%d message=\"%s\"", s.status.state, s.status.error, message.c_str()));
        h.Destroy(s);
        h.Drain();
    }
}

void Caps(Api& api) {
    std::printf("api version %d, %s, render event id %d\n", api.GetApiVersion(), api.name.c_str(), api.GetRenderEventId());
    std::printf("path %d: %s\n", api.GetPath(), api.Text(0, BSVD_TEXT_PLATFORM).c_str());
    std::printf("H.264 %s, HEVC %s\n", api.IsCodecSupported(BSVD_CODEC_H264) ? "supported" : "NOT supported",
                api.IsCodecSupported(BSVD_CODEC_HEVC) ? "supported" : "NOT supported");
}

// Switches the stand-in Unity to another renderer, as a device restart does.
void SwitchRenderer(UnityGfxRenderer renderer, ID3D11Device* device) {
    unity::SendDeviceEvent(kUnityGfxDeviceEventShutdown);
    unity::SetRenderer(renderer);
    unity::SetDevice(device);
    unity::SendDeviceEvent(kUnityGfxDeviceEventInitialize);
}

std::map<std::string, Clip> gClips;

const Clip* Need(const std::string& name) {
    auto it = gClips.find(name);
    if (it != gClips.end()) return &it->second;
    Clip clip;
    std::string why;
    if (!LoadClip(gOpt.fixtures + "\\" + name, clip, why)) {
        Report("fixture " + name, false, why + " (tools/bsvdtool/win_fixtures.py <dir>; bars: spike/fixtures.py <dir>)");
        return nullptr;
    }
    return &(gClips[name] = std::move(clip));
}

bool HevcOk(Api& api) { return api.IsCodecSupported(BSVD_CODEC_HEVC) != 0; }

void GpuSuite(Harness& h) {
    Api& api = *h.api;
    const Clip* o1080 = Need("o1080.h264");
    const Clip* base = Need("o1080base.h264");
    const Clip* hevc = Need("o1080.hevc");
    const Clip* m10 = Need("o1080m10.hevc");
    const Clip* o2160 = Need("o2160.h264");
    const Clip* crop = Need("k1080.h264");
    const Clip* sd = Need("sd360.h264");
    if (!o1080 || !base || !hevc || !o2160 || !crop || !sd || !m10) return;

    Paced(h, "h264 high, B-frames", {{o1080, Check::Counter, 1920, 1080, "1080p"}}, false);
    Paced(h, "h264 baseline", {{base, Check::Counter, 1920, 1080, "1080p"}}, false);
    Paced(h, "crop 1088 -> 1080, orientation", {{crop, Check::Crop, 1920, 1080, "1080p"}}, false);
    if (HevcOk(api)) {
        Paced(h, "hevc, B-frames", {{hevc, Check::Counter, 1920, 1080, "1080p"}}, false);
        Paced(h, "hevc main10", {{m10, Check::Counter, 1920, 1080, "1080p"}}, false);
    } else {
        Report("hevc", true, "SKIPPED: no HEVC decoder on this system");
    }
    Paced(h, "h264 4K", {{o2160, Check::Counter, 3840, 2160, "2160p"}}, false);
    Paced(h, "scaled 4K -> 720p", {{o2160, Check::Counter, 1280, 720, "720p"}}, false, 120);
    Paced(h, "scaled 1080p -> 2560x1440", {{o1080, Check::Counter, 2560, 1440, "1440p"}}, false, 120);
    for (const char* name : {"bars601lim.h264", "bars601full.h264", "bars709lim.h264", "bars709full.h264"}) {
        const Clip* bars = Need(name);
        if (bars) Paced(h, std::string("colour ") + name, {{bars, Check::Bars, 1920, 1080, "bars"}}, false);
    }
    {
        // The colour the decoder chose: the bitstream's, else by height.
        struct Want {
            const char* clip;
            int matrix, full;
        } wants[] = {{"bars601lim.h264", 601, 0}, {"bars601full.h264", 601, 1}, {"bars709lim.h264", 709, 0},
                     {"bars709full.h264", 709, 1}, {"sd360.h264", 601, 0},      {"o1080.hevc", 709, 0}};
        std::string detail;
        bool pass = true;
        for (auto& w : wants) {
            const Clip* c = Need(w.clip);
            if (!c || (c->codec == BSVD_CODEC_HEVC && !HevcOk(api))) continue;
            Stream s;
            h.Open(s, *c, Check::None, 640, 360, false);
            s.StartFeeding(0, c->units.size(), false);
            h.Run({&s}, [&] { return s.status.colourMatrix != 0; }, 3000);
            bool ok = s.status.colourMatrix == w.matrix && s.status.fullRange == w.full;
            pass = pass && ok;
            detail += Fmt("%s %d/%d%s; ", w.clip, s.status.colourMatrix, s.status.fullRange, ok ? "" : Fmt(" (want %d/%d)", w.matrix, w.full).c_str());
            h.Destroy(s);
            h.Drain();
        }
        Report("colour reported", pass, detail);
    }
    Preroll(h, *o1080, false);
    Seek(h, *o1080, false);
    Paced(h, "4 x 1080p + 4K at once",
          {{o1080, Check::Counter, 1920, 1080, "a"},
           {o1080, Check::Counter, 1920, 1080, "b"},
           {base, Check::Counter, 1920, 1080, "c"},
           {HevcOk(api) ? hevc : o1080, Check::Counter, 1920, 1080, "d"},
           {o2160, Check::Counter, 3840, 2160, "4K"}},
          false, 180, 0.03, 25.0);
    DestroyWhileRunning(h, *o1080, HevcOk(api) ? *hevc : *base, false, 20);
    Errors(h, *o1080, false);
}

int RunAll(const std::string& adapter) {
    Device device;
    std::string why;
    if (!MakeUnityDevice(adapter, device, why)) {
        Report("unity device", false, why);
        return 1;
    }
    std::printf("adapters:");
    for (auto& n : AdapterNames()) std::printf(" [%s]", n.c_str());
    std::printf("\nstand-in Unity device on: %s\n\n", device.adapter.c_str());

    Api api;
    StaticApi(api);
    Harness h{&api, &device, gOpt.hz};

    std::printf("before UnityPluginLoad: path %d (%s)\n", api.GetPath(), api.Text(0, BSVD_TEXT_PLATFORM).c_str());
    Report("no Unity: no path", api.GetPath() == BSVD_PATH_NONE, Fmt("path=%d", api.GetPath()));
    unity::SetRenderer(kUnityGfxRendererD3D11);
    unity::SetDevice(device.device.Get());
    int idBefore = api.GetRenderEventId();
    api.PluginLoad(unity::Interfaces());
    Caps(api);
    Report("render event id reserved at load", idBefore == BSVD_EVENT_PRESENT && api.GetRenderEventId() == unity::LastReservedEventId() &&
           api.GetRenderEventId() != BSVD_EVENT_PRESENT,
           Fmt("before load %d, after %d, Unity reserved %d", idBefore, api.GetRenderEventId(), unity::LastReservedEventId()));
    Report("D3D11 path", api.GetPath() == BSVD_PATH_D3D11 && api.IsCodecSupported(BSVD_CODEC_H264),
           api.Text(0, BSVD_TEXT_PLATFORM));
    {
        const Clip* o1080 = Need("o1080.h264");
        if (o1080) {
            Stream s;
            h.Open(s, *o1080, Check::None, 640, 360, false);
            s.StartFeeding(0, 30, false);
            h.Run({&s}, [&] { return s.status.framesShown > 0; }, 3000);  // drawn, so a video processor exists
            std::printf("decoder: %s\n", api.Text(s.handle, BSVD_TEXT_DECODER).c_str());
            int mirror = bsvd::win::Switches().mirrorCap;
            Report("flip method", mirror >= 0,
                   mirror == 1   ? "the video processor mirrors (VideoProcessorSetStreamMirror)"
                   : mirror == 0 ? "the video processor can't mirror: the shader pass flips"
                                 : "no video processor was made");
            h.Destroy(s);
            h.Drain();
        }
    }

    GpuSuite(h);

    // The fallbacks this machine doesn't take by itself.
    const Clip* o1080 = Need("o1080.h264");
    const Clip* crop = Need("k1080.h264");
    if (o1080 && crop) {
        bsvd::win::Switches().shaderFlip = true;
        Paced(h, "shader flip: crop", {{crop, Check::Crop, 1920, 1080, "1080p"}}, false);
        Paced(h, "shader flip: scaled 1080p -> 720p", {{o1080, Check::Counter, 1280, 720, "720p"}}, false, 90);
        bsvd::win::Switches().shaderFlip = false;
        bsvd::win::Switches().systemMemory = true;
        Paced(h, "system memory upload: crop", {{crop, Check::Crop, 1920, 1080, "1080p"}}, false);
        Paced(h, "system memory upload: h264", {{o1080, Check::Counter, 1920, 1080, "1080p"}}, false, 120);
        bsvd::win::Switches().systemMemory = false;
    }

    // Unity on Direct3D 12 (or Vulkan): the CPU path.
    SwitchRenderer(kUnityGfxRendererD3D12, nullptr);
    Report("D3D12: CPU path", api.GetPath() == BSVD_PATH_CPU, api.Text(0, BSVD_TEXT_PLATFORM));
    if (o1080 && crop) {
        Paced(h, "cpu h264 1080p", {{o1080, Check::Counter, 1920, 1080, "1080p"}}, true);
        Paced(h, "cpu crop, orientation", {{crop, Check::Crop, 1920, 1080, "1080p"}}, true);
        Paced(h, "cpu scaled 1080p -> 720p", {{o1080, Check::Counter, 1280, 720, "720p"}}, true, 120);
        const Clip* o2160 = Need("o2160.h264");
        if (o2160) Paced(h, "cpu 4K (capped 1080p)", {{o2160, Check::Counter, 1920, 1080, "1080p"}}, true, 120, 0.05, 25.0);
        const Clip* hevc = Need("o1080.hevc");
        if (hevc && HevcOk(api)) Paced(h, "cpu hevc", {{hevc, Check::Counter, 1920, 1080, "1080p"}}, true, 120);
        const Clip* m10 = Need("o1080m10.hevc");
        if (m10 && HevcOk(api)) Paced(h, "cpu hevc main10", {{m10, Check::Counter, 1920, 1080, "1080p"}}, true, 90);
        for (const char* name : {"bars601lim.h264", "bars709full.h264"}) {
            const Clip* bars = Need(name);
            if (bars) Paced(h, std::string("cpu colour ") + name, {{bars, Check::Bars, 1920, 1080, "bars"}}, true);
        }
        Preroll(h, *o1080, true);
        Seek(h, *o1080, true);
        DestroyWhileRunning(h, *o1080, *crop, true, 8);
        Errors(h, *o1080, true);
    }

    // Batch mode: nothing to draw with.
    SwitchRenderer(kUnityGfxRendererNull, nullptr);
    {
        BSVD_Config c{};
        c.codec = BSVD_CODEC_H264;
        int32_t handle = api.Create(&c, nullptr, 0);
        Report("null renderer: no path", api.GetPath() == BSVD_PATH_NONE && handle == BSVD_E_UNSUPPORTED,
               Fmt("path=%d create=%d (%s)", api.GetPath(), handle, api.Text(0, BSVD_TEXT_PLATFORM).c_str()));
    }
    api.PluginUnload();

    // The built DLL, as Unity loads it.
    if (!gOpt.dll.empty() && o1080 && crop) {
        Api dll;
        std::wstring path(gOpt.dll.begin(), gOpt.dll.end());
        if (!DllApi(path, dll, why)) {
            Report("dll load", false, why);
        } else {
            Report("dll: no Unity: no path", dll.GetPath() == BSVD_PATH_NONE, Fmt("path=%d", dll.GetPath()));
            SwitchRenderer(kUnityGfxRendererD3D11, device.device.Get());
            dll.PluginLoad(unity::Interfaces());
            Caps(dll);
            Report("dll: render event id reserved at load",
                   dll.GetRenderEventId() == unity::LastReservedEventId() && dll.GetRenderEventId() != BSVD_EVENT_PRESENT,
                   Fmt("id %d, Unity reserved %d", dll.GetRenderEventId(), unity::LastReservedEventId()));
            Harness d{&dll, &device, gOpt.hz};
            Report("dll: D3D11 path", dll.GetPath() == BSVD_PATH_D3D11, dll.Text(0, BSVD_TEXT_PLATFORM));
            Paced(d, "dll: crop, orientation", {{crop, Check::Crop, 1920, 1080, "1080p"}}, false);
            Paced(d, "dll: h264", {{o1080, Check::Counter, 1920, 1080, "1080p"}}, false, 90);
            Preroll(d, *o1080, false);
            dll.PluginUnload();
            Report("dll: unload", dll.GetLiveCount() == 0, Fmt("live=%d", dll.GetLiveCount()));
            FreeLibrary(dll.module);
        }
    }

    int failed = 0;
    for (auto& r : gResults) failed += r.pass ? 0 : 1;
    std::printf("\n%zu checks, %d failed\n", gResults.size(), failed);
    for (auto& r : gResults)
        if (!r.pass) std::printf("  FAILED: %s\n", r.name.c_str());
    std::printf("RESULT %s\n", failed ? "FAIL" : "PASS");
    return failed ? 1 : 0;
}

// ---------------------------------------------------------------- test: one check, repeated

// bsvdtool test seek|destroy <fixture dir> [--cpu] [--repeat n] [--rounds n] [--sample n] [--adapter name]
int OneTest(const std::string& name) {
    Device device;
    std::string why;
    if (!MakeUnityDevice(gOpt.adapter, device, why)) {
        std::printf("%s\n", why.c_str());
        return 2;
    }
    Api api;
    StaticApi(api);
    unity::SetRenderer(gOpt.cpu ? kUnityGfxRendererD3D12 : kUnityGfxRendererD3D11);
    unity::SetDevice(gOpt.cpu ? nullptr : device.device.Get());
    api.PluginLoad(unity::Interfaces());
    std::printf("%s on %s\n", api.Text(0, BSVD_TEXT_PLATFORM).c_str(), device.adapter.c_str());
    Harness h{&api, &device, gOpt.hz};
    const Clip* o1080 = Need("o1080.h264");
    const Clip* other = Need(!gOpt.clip2.empty() ? gOpt.clip2 : gOpt.cpu ? "k1080.h264" : "o1080.hevc");
    if (!o1080 || !other) return 2;
    for (int i = 0; i < gOpt.repeat; ++i) {
        if (name == "seek") Seek(h, *o1080, gOpt.cpu);
        else if (name == "destroy") DestroyWhileRunning(h, *o1080, *other, gOpt.cpu, gOpt.rounds);
        else {
            std::printf("unknown test %s\n", name.c_str());
            return 2;
        }
    }
    api.PluginUnload();
    int failed = 0;
    for (auto& r : gResults) failed += r.pass ? 0 : 1;
    std::printf("%zu runs, %d failed\n", gResults.size(), failed);
    return failed ? 1 : 0;
}

// ---------------------------------------------------------------- play: one clip, by hand

void Dump(Device& device, ID3D11Texture2D* target, const std::string& path) {
    D3D11_TEXTURE2D_DESC td{};
    target->GetDesc(&td);
    td.Usage = D3D11_USAGE_STAGING;
    td.BindFlags = 0;
    td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if (FAILED(device.device->CreateTexture2D(&td, nullptr, &staging))) return;
    device.context->CopyResource(staging.Get(), target);
    D3D11_MAPPED_SUBRESOURCE m{};
    if (FAILED(device.context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &m))) return;
    FILE* f = std::fopen(path.c_str(), "wb");
    if (f) {
        std::fprintf(f, "P6\n%u %u\n255\n", td.Width, td.Height);
        // Rows are stored bottom first; written top first, the image looks as Unity shows it.
        for (int y = (int)td.Height - 1; y >= 0; --y) {
            const uint8_t* row = (const uint8_t*)m.pData + (size_t)y * m.RowPitch;
            for (UINT x = 0; x < td.Width; ++x) std::fwrite(row + x * 4, 1, 3, f);
        }
        std::fclose(f);
        std::printf("wrote %s\n", path.c_str());
    }
    device.context->Unmap(staging.Get(), 0);
}

int Play(const std::string& file) {
    Clip clip;
    std::string why;
    if (!LoadClip(file, clip, why)) {
        std::printf("%s\n", why.c_str());
        return 2;
    }
    Device device;
    if (!MakeUnityDevice(gOpt.adapter, device, why)) {
        std::printf("%s\n", why.c_str());
        return 2;
    }
    Api api;
    StaticApi(api);
    unity::SetRenderer(gOpt.cpu ? kUnityGfxRendererD3D12 : kUnityGfxRendererD3D11);
    unity::SetDevice(gOpt.cpu ? nullptr : device.device.Get());
    api.PluginLoad(unity::Interfaces());
    Caps(api);
    Harness h{&api, &device, gOpt.hz};
    Check check = gOpt.check == "crop" ? Check::Crop : gOpt.check == "bars" ? Check::Bars : gOpt.check == "none" ? Check::None : Check::Counter;
    int w = gOpt.targetWidth ? gOpt.targetWidth : 1920, hh = gOpt.targetHeight ? gOpt.targetHeight : 1080;
    if (!gOpt.dump.empty() && !gOpt.cpu) {
        Stream s;
        h.Open(s, clip, check, w, hh, false);
        s.StartFeeding(0, clip.units.size(), false);
        h.Run({&s}, [&] { return s.frames > 0; }, 5000);
        Dump(device, s.target.Get(), gOpt.dump);
        h.Destroy(s);
        h.Drain();
    }
    bool pass = Paced(h, "play " + clip.name, {{&clip, check, w, hh, Fmt("%dx%d", w, hh)}}, gOpt.cpu);
    api.PluginUnload();
    return pass ? 0 : 1;
}

}  // namespace

int main(int argc, char** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    timeBeginPeriod(1);
    std::vector<std::string> positional;
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        auto value = [&]() { return i + 1 < argc ? std::string(argv[++i]) : std::string(); };
        if (a == "--adapter") gOpt.adapter = value();
        else if (a == "--dll") gOpt.dll = value();
        else if (a == "--dump") gOpt.dump = value();
        else if (a == "--check") gOpt.check = value();
        else if (a == "--hz") gOpt.hz = std::max(10, std::atoi(value().c_str()));
        else if (a == "--cpu") gOpt.cpu = true;
        else if (a == "--repeat") gOpt.repeat = std::max(1, std::atoi(value().c_str()));
        else if (a == "--rounds") gOpt.rounds = std::max(1, std::atoi(value().c_str()));
        else if (a == "--sample") gOpt.sampleEvery = std::max(0, std::atoi(value().c_str()));
        else if (a == "--clip2") gOpt.clip2 = value();
        else if (a == "--target") std::sscanf(value().c_str(), "%dx%d", &gOpt.targetWidth, &gOpt.targetHeight);
        else positional.push_back(a);
    }
    if (positional.empty()) {
        std::printf("usage: bsvdtool caps | all <fixture dir> [--dll path] | test seek|destroy <fixture dir> [--repeat n] [--rounds n] [--sample n]\n"
                    "       | play <clip> [--target WxH] [--cpu] [--check ...] [--dump f.ppm]\n"
                    "       [--adapter name] [--hz n]\n");
        return 2;
    }
    if (positional[0] == "caps") {
        Device device;
        std::string why;
        Api api;
        StaticApi(api);
        std::printf("adapters:");
        for (auto& n : AdapterNames()) std::printf(" [%s]", n.c_str());
        std::printf("\n");
        if (MakeUnityDevice(gOpt.adapter, device, why)) {
            unity::SetRenderer(kUnityGfxRendererD3D11);
            unity::SetDevice(device.device.Get());
            api.PluginLoad(unity::Interfaces());
        }
        Caps(api);
        api.PluginUnload();
        return api.IsCodecSupported(BSVD_CODEC_H264) ? 0 : 1;
    }
    if (positional[0] == "all" && positional.size() > 1) {
        gOpt.fixtures = positional[1];
        return RunAll(gOpt.adapter);
    }
    if (positional[0] == "play" && positional.size() > 1) return Play(positional[1]);
    if (positional[0] == "test" && positional.size() > 2) {
        gOpt.fixtures = positional[2];
        return OneTest(positional[1]);
    }
    std::printf("unknown command\n");
    return 2;
}
