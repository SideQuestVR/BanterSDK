// bsvdtool on Android: drives the C API as VideoDecoderNative.cs does, against a stand-in for Unity's Vulkan device
// (android_host), and checks every result with numbers. Run over adb shell from /data/local/tmp/bsvd (run-android.sh).
//
//   bsvdtool startup                                     paths per renderer, the start-up hook, event id and config
//   bsvdtool order <clip> [--cpu] [--frames n]           every frame shown in order with its time (clock stepped)
//   bsvdtool bars <clip> <601|709> <0|1> [--cpu]         colour bars within 2/255
//   bsvdtool orient <clip>                               GPU frame equals the CPU path's frame, not its mirror image
//   bsvdtool paced <clip>[:n],<clip>[:n] [--loops n] [--cpu]  running clock, 90 Hz consumer, on-time release
//   bsvdtool preroll <clip>                              paused first picture, flush with discard while paused
//   bsvdtool seek <clip>                                 flushes while playing: no stale frames
//   bsvdtool eos <clip>                                  end of stream reaches ENDED with every frame accounted for
//   bsvdtool destroy <clip>                              destroy in every state; live count and objects back to 0
//
// The render event records into a secondary command buffer inside a render pass, as Unity 6 on Quest 3 gave it (graphics
// jobs, its default); --primary on any check gives it the frame's primary buffer instead, outside a render pass when its
// id is reserved and configured so. --deferred: Unity's submissions and queue access callbacks run two frames late, on
// another thread. Every check also fails on any of the host's own counts (android_host.h: recording into Unity's buffer
// inside a render pass, submitting outside a queue callback, forbidden calls in the callback, wrong layouts).
//
// Memory rows are bottom-first (the header's contract): the picture's top counter (source row 16) is memory row
// height-17 and its bottom counter (source row height-16) is memory row 15.
#include "android_clip.h"
#include "android_host.h"

#include "android/droid.h"
#include "bsvideodecode.h"

#include <dlfcn.h>
#include <unistd.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>
#include <thread>
#include <vector>

using namespace tool;

namespace {

int gArgc = 0;
char** gArgv = nullptr;
std::string gFx = ".";
bool gCpu = false;

std::string Arg(const char* name, const char* fallback) {
    for (int i = 1; i + 1 < gArgc; ++i)
        if (!std::strcmp(gArgv[i], name)) return gArgv[i + 1];
    return fallback;
}
bool Flag(const char* name) {
    for (int i = 1; i < gArgc; ++i)
        if (!std::strcmp(gArgv[i], name)) return true;
    return false;
}
bool ValueLess(const char* flag) {
    return !std::strcmp(flag, "--cpu") || !std::strcmp(flag, "--unorm") || !std::strcmp(flag, "--primary") || !std::strcmp(flag, "--deferred");
}
std::string Positional(int index) {
    int seen = 0;
    for (int i = 1; i < gArgc; ++i) {
        if (gArgv[i][0] == '-' && gArgv[i][1] == '-') {
            if (i + 1 < gArgc && gArgv[i + 1][0] != '-' && !ValueLess(gArgv[i])) ++i;
            continue;
        }
        if (seen++ == index) return gArgv[i];
    }
    return "";
}

// A command-line process has no binder thread pool, which AImageReader and MediaCodec need; the NDK stubs don't export
// these, so they are looked up.
void StartBinderPool() {
    void* lib = dlopen("libbinder_ndk.so", RTLD_NOW);
    auto setMax = lib ? (bool (*)(uint32_t))dlsym(lib, "ABinderProcess_setThreadPoolMaxThreadCount") : nullptr;
    auto start = lib ? (void (*)())dlsym(lib, "ABinderProcess_startThreadPool") : nullptr;
    if (setMax) setMax(4);
    if (start) start();
    if (!start) std::printf("binder thread pool NOT started\n");
}

void SleepMs(double ms) { std::this_thread::sleep_for(std::chrono::microseconds((long long)(ms * 1000))); }

BSVD_Status Status(int32_t h) {
    BSVD_Status s{};
    BSVD_GetStatus(h, &s, (int32_t)sizeof s);
    return s;
}

std::string Text(int32_t h, int which) {
    char buffer[512] = {};
    BSVD_GetText(h, which, buffer, (int32_t)sizeof buffer);
    return buffer;
}

int ReadCounter(const uint8_t* px, uint32_t width, int row) {
    int v = 0;
    for (int b = 0; b < 16; ++b) {
        uint32_t x = 32 + 64 * (uint32_t)b;
        if (x >= width) break;
        if (px[((size_t)row * width + x) * 4] > 128) v |= 1 << b;
    }
    return v;
}
int TopRow(uint32_t height) { return (int)height - 17; }
int BottomRow(uint32_t) { return 15; }

// The memory rows FrameNumber reads when a picture sourceHeight tall fills a target height tall.
std::vector<int> CounterRows(uint32_t height, uint32_t sourceHeight) {
    return {(int)height - 1 - (int)((uint64_t)16 * height / sourceHeight),
            (int)height - 1 - (int)((uint64_t)(sourceHeight - 16) * height / sourceHeight)};
}

// The frame number from both strips. spike/fixtures.py (c*, no .pkt): the same N in both. win_fixtures.py (o*, sd*, with
// .pkt): top N | 0x8000, bottom (32767 - N) | 0x8000, so a picture upside down reads as the wrong frame. Pictures
// narrower than 1056 px show only the low bits. torn: the strips disagree (torn, or upside down).
// sourceWidth/Height: the picture's own size when it was scaled to width x height (the CPU path caps at 1920x1080).
int FrameNumber(const uint8_t* px, uint32_t width, uint32_t height, bool marked, bool& torn, uint32_t sourceWidth = 0,
                uint32_t sourceHeight = 0) {
    if (!sourceWidth || !sourceHeight) {
        sourceWidth = width;
        sourceHeight = height;
    }
    auto read = [&](uint32_t sourceRow) {
        int row = (int)height - 1 - (int)((uint64_t)sourceRow * height / sourceHeight);
        int v = 0;
        for (int b = 0; b < 16; ++b) {
            uint32_t x = (uint32_t)((uint64_t)(32 + 64 * (uint32_t)b) * width / sourceWidth);
            if (32 + 64 * (uint32_t)b >= sourceWidth) break;
            if (px[((size_t)row * width + x) * 4] > 128) v |= 1 << b;
        }
        return v;
    };
    int top = read(16), bottom = read(sourceHeight - 16);
    int bits = 0;
    while (bits < 16 && 32 + 64 * (uint32_t)bits < sourceWidth) bits++;
    int mask = bits >= 16 ? 0xFFFF : (1 << bits) - 1;
    if (marked) {
        int n = top & mask & 0x7FFF;
        torn = (bottom & mask & 0x7FFF) != ((32767 - n) & mask & 0x7FFF) || (bits >= 16 && (!(top & 0x8000) || !(bottom & 0x8000)));
        return n;
    }
    torn = top != bottom;
    return top;
}

bool LoadFixture(const std::string& name, Clip& clip) {
    std::string path = name.find('/') == std::string::npos ? gFx + "/" + name : name;
    if (!LoadClip(path, clip)) return false;
    std::printf("clip %s: %zu units, %d frames, %dx%d, %s, times from %s\n", clip.name.c_str(), clip.units.size(), clip.frames, clip.width,
                clip.height, clip.hevc ? "HEVC" : "H.264", clip.fromPkt ? ".pkt" : "decode order");
    return true;
}

int32_t Create(const Clip& clip, bool withSets = true, int matrix = 0, int full = -1) {
    BSVD_Config c{};
    c.codec = clip.hevc ? BSVD_CODEC_HEVC : BSVD_CODEC_H264;
    c.width = clip.width;
    c.height = clip.height;
    c.colourMatrix = matrix;
    c.fullRange = full;
    std::vector<uint8_t> sets = withSets ? clip.ParameterSets() : std::vector<uint8_t>();
    int32_t h = BSVD_Create(&c, sets.empty() ? nullptr : sets.data(), (int32_t)sets.size());
    if (h <= 0) std::printf("BSVD_Create failed: %d (%s)\n", h, Text(0, BSVD_TEXT_PLATFORM).c_str());
    return h;
}

// Render events until every destroyed decoder is gone.
bool WaitReleased(double seconds) {
    double start = NowSeconds();
    while (BSVD_GetLiveCount() > 0 && NowSeconds() - start < seconds) {
        TheHost().Frame();
        SleepMs(5);
    }
    TheHost().Finish();
    int live = BSVD_GetLiveCount();
    if (live) std::printf("FAILED %d decoders still live after %.1f s\n", live, seconds);
    return live == 0;
}

bool CheckLeaks(const char* when) {
    auto& l = bsvd::droid::Live();
    int codecs = l.codecs, readers = l.readers, images = l.images, imports = l.imports, vulkan = l.vulkan, decoders = l.decoders;
    std::printf("live objects %s: decoders=%d codecs=%d readers=%d images=%d imports=%d vulkan=%d\n", when, decoders, codecs, readers, images,
                imports, vulkan);
    return !codecs && !readers && !images && !imports && !vulkan && !decoders;
}

// ---------------------------------------------------------------- device load numbers

struct Load {
    unsigned long long idle0 = 0, total0 = 0;
    std::vector<int> gpu;
    double sampled = 0;

    static bool Cpu(unsigned long long& idle, unsigned long long& total) {
        FILE* f = std::fopen("/proc/stat", "r");
        if (!f) return false;
        unsigned long long v[10] = {};
        int n = std::fscanf(f, "cpu %llu %llu %llu %llu %llu %llu %llu %llu %llu %llu", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5], &v[6], &v[7],
                            &v[8], &v[9]);
        std::fclose(f);
        if (n < 5) return false;
        total = 0;
        for (int i = 0; i < n; ++i) total += v[i];
        idle = v[3] + v[4];
        return true;
    }
    void Start() { Cpu(idle0, total0); }
    void Sample() {
        double now = NowSeconds();
        if (now - sampled < 0.5) return;
        sampled = now;
        FILE* f = std::fopen("/sys/class/kgsl/kgsl-3d0/gpu_busy_percentage", "r");
        if (!f) return;
        int v = -1;
        if (std::fscanf(f, "%d", &v) == 1 && v >= 0) gpu.push_back(v);
        std::fclose(f);
    }
    void Print() {
        unsigned long long idle = 0, total = 0;
        if (Cpu(idle, total) && total > total0)
            std::printf("device_cpu_busy=%.1f%% (all cores)\n", 100.0 * (1.0 - (double)(idle - idle0) / (double)(total - total0)));
        if (!gpu.empty()) {
            double sum = 0;
            for (int v : gpu) sum += v;
            std::printf("gpu_busy_avg=%.0f%% samples=%zu\n", sum / gpu.size(), gpu.size());
        }
        FILE* f = std::fopen("/proc/self/status", "r");
        if (!f) return;
        char line[256];
        std::string out;
        while (std::fgets(line, sizeof line, f))
            if (!std::strncmp(line, "VmHWM", 5) || !std::strncmp(line, "VmRSS", 5)) {
                std::string l(line);
                l.erase(std::remove(l.begin(), l.end(), '\n'), l.end());
                l.erase(std::remove(l.begin(), l.end(), ' '), l.end());
                std::replace(l.begin(), l.end(), '\t', ' ');
                out += l + " ";
            }
        std::fclose(f);
        std::printf("memory %s\n", out.c_str());
    }
};

// ---------------------------------------------------------------- order: every frame, in order, with its time

bool TestOrder() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    int limit = std::atoi(Arg("--frames", "0").c_str());
    int n = limit > 0 ? std::min(limit, clip.frames) : clip.frames;
    int32_t h = Create(clip);
    if (h <= 0) return false;
    Host& host = TheHost();
    // --target WxH: a texture of another size, which the frames are scaled to fill.
    int tw = clip.width, th = clip.height;
    std::sscanf(Arg("--target", "").c_str(), "%dx%d", &tw, &th);
    HostTexture* tex = gCpu ? nullptr : host.MakeTexture((uint32_t)tw, (uint32_t)th);
    BSVD_SetTarget(h, tex, tw, th);
    if (tw != clip.width || th != clip.height) std::printf("target %dx%d for a %dx%d picture\n", tw, th, clip.width, clip.height);
    Feeder feeder;
    feeder.Start(h, &clip, 0, 1, true);

    long long orderErrors = 0, tears = 0, ptsErrors = 0, checked = 0, extra = 0;
    long long lastShown = 0;
    double began = NowSeconds(), worstStep = 0;
    int k = 0;
    for (; k < n; ++k) {
        int64_t pts = clip.PtsOf(k);
        BSVD_SetClock(h, pts, 0);
        bool seen = false;
        double start = NowSeconds();
        while (!seen && NowSeconds() - start < 3.0) {
            if (gCpu) {
                host.Frame();
                void* px = nullptr;
                int32_t w = 0, hh = 0;
                int32_t r = BSVD_LockFrame(h, &px, &w, &hh);
                if (r == 1) {
                    BSVD_Status s = Status(h);
                    seen = true;
                    if (s.shownPtsUs != pts) {
                        if (ptsErrors++ < 4) std::printf("frame %d: shown time %lld, expected %lld\n", k, (long long)s.shownPtsUs, (long long)pts);
                    }
                    auto* bytes = static_cast<const uint8_t*>(px);
                    bool torn = false;
                    int top = FrameNumber(bytes, (uint32_t)w, (uint32_t)hh, clip.fromPkt, torn, (uint32_t)clip.width, (uint32_t)clip.height);
                    if (torn && tears++ < 4) std::printf("frame %d: torn or upside down\n", k);
                    if (top != k && orderErrors++ < 4) std::printf("frame %d: picture says %d\n", k, top);
                    checked++;
                    BSVD_UnlockFrame(h);
                } else if (r < 0) {
                    std::printf("BSVD_LockFrame: %d\n", r);
                    break;
                }
            } else {
                host.Frame([&, k, pts](std::vector<Readback>& reads) {
                    BSVD_Status s = Status(h);
                    if (s.framesShown == lastShown) return;
                    if (s.framesShown > lastShown + 1) extra += s.framesShown - lastShown - 1;
                    lastShown = s.framesShown;
                    seen = true;
                    if (s.shownPtsUs != pts && ptsErrors++ < 4)
                        std::printf("frame %d: shown time %lld, expected %lld\n", k, (long long)s.shownPtsUs, (long long)pts);
                    Readback r;
                    r.texture = tex;
                    r.rows = CounterRows(tex->height, (uint32_t)clip.height);
                    r.inspect = [&, k](const uint8_t* px, uint32_t w, uint32_t hh) {
                        bool torn = false;
                        int top = FrameNumber(px, w, hh, clip.fromPkt, torn, (uint32_t)clip.width, (uint32_t)clip.height);
                        if (torn && tears++ < 4)
                            std::printf("frame %d: torn or upside down (top %d bottom %d)\n", k, ReadCounter(px, w, TopRow(hh)), ReadCounter(px, w, BottomRow(hh)));
                        if (top != k && orderErrors++ < 4) std::printf("frame %d: picture says %d\n", k, top);
                        checked++;
                    };
                    reads.push_back(r);
                });
            }
            if (!seen) SleepMs(1);
        }
        worstStep = std::max(worstStep, NowSeconds() - start);
        if (!seen) {
            BSVD_Status s = Status(h);
            std::printf("frame %d never shown within 3 s (state %d, decoded %lld, held %d, queued %d, error %d: %s)\n", k, s.state,
                        (long long)s.framesDecoded, s.heldFrames, s.queuedUnits, s.error, Text(h, BSVD_TEXT_MESSAGE).c_str());
            break;
        }
    }
    host.Finish();
    double seconds = NowSeconds() - began;
    BSVD_Status s = Status(h);
    std::printf("decoder=%s path=%d video=%dx%d colour=%d/%d\n", Text(h, BSVD_TEXT_DECODER).c_str(), s.path, s.videoWidth, s.videoHeight,
                s.colourMatrix, s.fullRange);
    std::printf("frames=%d checked=%lld order_errors=%lld tears=%lld pts_errors=%lld extra_shown=%lld decoded=%lld shown=%lld dropped=%lld "
                "steps_per_s=%.1f worst_step_ms=%.1f\n",
                n, checked, orderErrors, tears, ptsErrors, extra, (long long)s.framesDecoded, (long long)s.framesShown, (long long)s.framesDropped,
                k / seconds, worstStep * 1000);
    feeder.Stop();
    BSVD_Destroy(h);
    bool released = WaitReleased(5);
    if (tex) host.DestroyTexture(tex);
    return released && k == n && checked == n && !orderErrors && !tears && !ptsErrors && !extra && s.videoWidth == clip.width &&
           s.videoHeight == clip.height;
}

// ---------------------------------------------------------------- bars: colour conversion

const int kBars[8][3] = {{192, 192, 192}, {192, 192, 0}, {0, 192, 192}, {0, 192, 0}, {192, 0, 192}, {192, 0, 0}, {0, 0, 192}, {64, 64, 64}};

double BarError(const uint8_t* px, uint32_t w, uint32_t h, bool print) {
    int row = (int)h - 1 - (int)h / 2;  // the source picture's row h/2, bottom-first
    double worst = 0;
    for (int b = 0; b < 8; ++b) {
        const uint8_t* p = px + ((size_t)row * w + (size_t)((b * 2 + 1) * (int)w / 16)) * 4;
        for (int c = 0; c < 3; ++c) worst = std::max(worst, std::fabs((double)p[c] - kBars[b][c]));
        if (print)
            std::printf("bar %d: %d %d %d (want %d %d %d)\n", b, p[0], p[1], p[2], kBars[b][0], kBars[b][1], kBars[b][2]);
    }
    return worst;
}

bool TestBars() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    int matrix = std::atoi(Positional(2).c_str());
    int full = std::atoi(Positional(3).c_str());
    // The container says nothing: the colour must come from the bitstream (the SPS), not the driver's suggestion.
    int32_t h = Create(clip, true, 0, -1);
    if (h <= 0) return false;
    Host& host = TheHost();
    HostTexture* tex = gCpu ? nullptr : host.MakeTexture((uint32_t)clip.width, (uint32_t)clip.height);
    BSVD_SetTarget(h, tex, clip.width, clip.height);
    Feeder feeder;
    feeder.Start(h, &clip, 0, 1, true);
    double error = -1;
    double start = NowSeconds();
    bool seen = false;
    while (!seen && NowSeconds() - start < 3.0) {
        if (gCpu) {
            host.Frame();
            void* px = nullptr;
            int32_t w = 0, hh = 0;
            if (BSVD_LockFrame(h, &px, &w, &hh) == 1) {
                seen = true;
                error = BarError(static_cast<const uint8_t*>(px), (uint32_t)w, (uint32_t)hh, true);
                BSVD_UnlockFrame(h);
            }
        } else {
            host.Frame([&](std::vector<Readback>& reads) {
                if (Status(h).framesShown == 0) return;
                seen = true;
                Readback r;
                r.texture = tex;
                r.rows = {(int)tex->height - 1 - (int)tex->height / 2};
                r.inspect = [&](const uint8_t* px, uint32_t w, uint32_t hh) { error = BarError(px, w, hh, true); };
                reads.push_back(r);
            });
        }
        if (!seen) SleepMs(2);
    }
    host.Finish();
    BSVD_Status s = Status(h);
    std::printf("decoder=%s path=%d colour in use: matrix %d full %d (expected %d %d) worst_bar_error=%.0f (of 255)\n",
                Text(h, BSVD_TEXT_DECODER).c_str(), s.path, s.colourMatrix, s.fullRange, matrix, full, error);
    feeder.Stop();
    BSVD_Destroy(h);
    bool released = WaitReleased(5);
    if (tex) host.DestroyTexture(tex);
    return released && seen && error >= 0 && error <= 2.0 && s.colourMatrix == matrix && s.fullRange == full;
}

// ---------------------------------------------------------------- orient: GPU frame against the CPU path's

// The first picture of a clip through the path the renderer gives.
bool FirstPicture(const Clip& clip, std::vector<uint8_t>& out) {
    Host& host = TheHost();
    bool cpu = BSVD_GetPath() == BSVD_PATH_CPU;
    int32_t h = Create(clip);
    if (h <= 0) return false;
    HostTexture* tex = cpu ? nullptr : host.MakeTexture((uint32_t)clip.width, (uint32_t)clip.height);
    BSVD_SetTarget(h, tex, clip.width, clip.height);
    Feeder feeder;
    feeder.Start(h, &clip, 0, 1, true);
    bool seen = false;
    double start = NowSeconds();
    while (!seen && NowSeconds() - start < 3.0) {
        if (cpu) {
            host.Frame();
            void* px = nullptr;
            int32_t w = 0, hh = 0;
            if (BSVD_LockFrame(h, &px, &w, &hh) == 1) {
                seen = true;
                out.assign(static_cast<uint8_t*>(px), static_cast<uint8_t*>(px) + (size_t)w * hh * 4);
                BSVD_UnlockFrame(h);
            }
        } else {
            host.Frame([&](std::vector<Readback>& reads) {
                if (Status(h).framesShown == 0) return;
                seen = true;
                Readback r;
                r.texture = tex;
                r.inspect = [&](const uint8_t* px, uint32_t w, uint32_t hh) { out.assign(px, px + (size_t)w * hh * 4); };
                reads.push_back(r);
            });
        }
        if (!seen) SleepMs(2);
    }
    host.Finish();
    feeder.Stop();
    BSVD_Destroy(h);
    bool released = WaitReleased(5);
    if (tex) host.DestroyTexture(tex);
    return released && seen && !out.empty();
}

bool TestOrient() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    Host& host = TheHost();
    std::vector<uint8_t> cpu, gpu;
    host.Shutdown();
    host.Initialize(kUnityGfxRendererOpenGLES30);
    bool okCpu = BSVD_GetPath() == BSVD_PATH_CPU && FirstPicture(clip, cpu);
    host.Shutdown();
    host.Initialize(kUnityGfxRendererVulkan);
    bool okGpu = BSVD_GetPath() == BSVD_PATH_VULKAN && FirstPicture(clip, gpu);
    if (!okCpu || !okGpu || cpu.size() != gpu.size()) {
        std::printf("FAILED couldn't get both pictures (cpu %d, gpu %d)\n", okCpu, okGpu);
        return false;
    }
    size_t w = (size_t)clip.width, h = (size_t)clip.height;
    double same = 0, mirrored = 0;
    for (size_t y = 0; y < h; ++y)
        for (size_t x = 0; x < w; ++x)
            for (int c = 0; c < 3; ++c) {
                int g = gpu[(y * w + x) * 4 + c];
                same += std::abs(g - (int)cpu[(y * w + x) * 4 + c]);
                mirrored += std::abs(g - (int)cpu[((h - 1 - y) * w + x) * 4 + c]);
            }
    same /= (double)(w * h * 3);
    mirrored /= (double)(w * h * 3);
    bool tornGpu = false, tornCpu = false;
    int topGpu = FrameNumber(gpu.data(), (uint32_t)w, (uint32_t)h, clip.fromPkt, tornGpu);
    int topCpu = FrameNumber(cpu.data(), (uint32_t)w, (uint32_t)h, clip.fromPkt, tornCpu);
    if (tornGpu) topGpu = -1;
    if (tornCpu) topCpu = -1;
    std::printf("mean_abs_diff gpu_vs_cpu=%.2f gpu_vs_cpu_mirrored=%.2f (of 255); top counter gpu %d cpu %d; alpha gpu %d cpu %d\n", same,
                mirrored, topGpu, topCpu, gpu[3], cpu[3]);
    return same < 3.0 && mirrored > same * 3 + 5 && gpu[3] == 255 && cpu[3] == 255 && topGpu == 0 && topCpu == 0;
}

// ---------------------------------------------------------------- streams under a running clock

struct Stream {
    const Clip* clip = nullptr;
    int32_t h = 0;
    HostTexture* tex = nullptr;
    std::unique_ptr<Feeder> feeder{new Feeder()};
    long long lastShown = 0, tears = 0, order = 0, skipped = 0, ptsErrors = 0, checked = 0, late = 0, stale = 0;
    int last = -1;
    int floor = -1;      // after a seek: nothing below this may show
    int firstAfter = -1; // the first picture after the latest seek
    double worstLagMs = 0;
    bool ended = false;
};

// Asks for a readback when a stream showed a new frame this event, and checks it when it arrives.
void Observe(Stream& s, int64_t clockUs, std::vector<Readback>& reads) {
    BSVD_Status st = Status(s.h);
    if (st.framesShown == s.lastShown) return;
    s.lastShown = st.framesShown;
    int expected = s.clip->DisplayOf(st.shownPtsUs);
    if (clockUs != bsvd::kNoTime) {
        double lag = (double)(clockUs - st.shownPtsUs) / 1000.0;
        s.worstLagMs = std::max(s.worstLagMs, lag);
        // Shown more than a frame plus a 90 Hz interval behind the clock.
        if (lag > 1000.0 / Clip::kFps + 1000.0 / 90 + 1) s.late++;
    }
    if (!s.tex) return;
    Readback r;
    r.texture = s.tex;
    r.rows = {TopRow(s.tex->height), BottomRow(s.tex->height)};
    int floor = s.floor;
    Stream* sp = &s;
    r.inspect = [sp, expected, floor](const uint8_t* px, uint32_t w, uint32_t hh) {
        Stream& s = *sp;
        bool torn = false;
        int top = FrameNumber(px, w, hh, s.clip->fromPkt, torn);
        s.checked++;
        if (torn && s.tears++ < 4)
            std::printf("stream %d: torn or upside-down frame, top %d bottom %d\n", s.h, ReadCounter(px, w, TopRow(hh)), ReadCounter(px, w, BottomRow(hh)));
        if (top != expected && s.ptsErrors++ < 4) std::printf("stream %d: time says frame %d, picture says %d\n", s.h, expected, top);
        if (floor >= 0) {
            if (top < floor && s.stale++ < 4) std::printf("stream %d: frame %d shown after a seek to %d\n", s.h, top, floor);
            if (s.firstAfter < 0) s.firstAfter = top;
        }
        int frames = s.clip->frames;
        if (s.last >= 0 && floor < 0) {
            if (top > s.last) s.skipped += top - s.last - 1;
            else if (s.last - top > frames / 2) s.skipped += frames - 1 - s.last + top;  // the next loop
            else if (s.order++ < 4) std::printf("stream %d: frame %d after %d\n", s.h, top, s.last);
        }
        s.last = top;
    };
    reads.push_back(r);
}

void TakeCpu(Stream& s, int64_t clockUs);

bool StartStreams(std::vector<Stream>& streams, int loops, bool endOfStream) {
    Host& host = TheHost();
    for (Stream& s : streams) {
        s.h = Create(*s.clip);
        if (s.h <= 0) return false;
        s.tex = gCpu ? nullptr : host.MakeTexture((uint32_t)s.clip->width, (uint32_t)s.clip->height);
        BSVD_SetTarget(s.h, s.tex, s.clip->width, s.clip->height);
        s.feeder->Start(s.h, s.clip, 0, loops, endOfStream);
    }
    // Preroll: every stream shows its first picture before the clock starts.
    double start = NowSeconds();
    for (;;) {
        bool all = true;
        for (Stream& s : streams) all = all && Status(s.h).framesShown >= 1;
        if (all) break;
        if (gCpu)
            for (Stream& s : streams)
                if (Status(s.h).framesShown == 0) TakeCpu(s, bsvd::kNoTime);  // the CPU path's caller takes the picture
        if (NowSeconds() - start > 5) {
            std::printf("FAILED not every stream showed its first picture within 5 s\n");
            return false;
        }
        host.Frame();
        SleepMs(1000.0 / 90);
    }
    std::printf("preroll: every first picture in %.0f ms\n", (NowSeconds() - start) * 1000);
    return true;
}

// CPU path: the caller takes frames itself.
void TakeCpu(Stream& s, int64_t clockUs) {
    void* px = nullptr;
    int32_t w = 0, hh = 0;
    if (BSVD_LockFrame(s.h, &px, &w, &hh) != 1) return;
    BSVD_Status st = Status(s.h);
    s.lastShown = st.framesShown;
    int expected = s.clip->DisplayOf(st.shownPtsUs);
    if (clockUs != bsvd::kNoTime) {
        double lag = (double)(clockUs - st.shownPtsUs) / 1000.0;
        s.worstLagMs = std::max(s.worstLagMs, lag);
        if (lag > 1000.0 / Clip::kFps + 1000.0 / 90 + 1) s.late++;
    }
    auto* bytes = static_cast<const uint8_t*>(px);
    bool torn = false;
    int top = FrameNumber(bytes, (uint32_t)w, (uint32_t)hh, s.clip->fromPkt, torn, (uint32_t)s.clip->width, (uint32_t)s.clip->height);
    s.checked++;
    if (torn) s.tears++;
    if (top != expected && s.ptsErrors++ < 4) std::printf("stream %d: time says frame %d, picture says %d\n", s.h, expected, top);
    int frames = s.clip->frames;
    if (s.last >= 0) {
        if (top > s.last) s.skipped += top - s.last - 1;
        else if (s.last - top > frames / 2) s.skipped += frames - 1 - s.last + top;
        else if (s.order++ < 4) std::printf("stream %d: frame %d after %d\n", s.h, top, s.last);
    }
    s.last = top;
    BSVD_UnlockFrame(s.h);
}

bool TestPaced() {
    // clip[:count],clip[:count]
    std::string mix = Positional(1);
    int loops = std::max(1, std::atoi(Arg("--loops", "1").c_str()));
    std::vector<std::unique_ptr<Clip>> clips;
    std::vector<Stream> streams;
    size_t at = 0;
    while (at < mix.size()) {
        size_t end = mix.find(',', at);
        std::string item = mix.substr(at, end == std::string::npos ? std::string::npos : end - at);
        at = end == std::string::npos ? mix.size() : end + 1;
        size_t colon = item.find(':');
        int count = colon == std::string::npos ? 1 : std::atoi(item.substr(colon + 1).c_str());
        auto clip = std::make_unique<Clip>();
        if (!LoadFixture(item.substr(0, colon), *clip)) return false;
        for (int i = 0; i < count; ++i) {
            streams.emplace_back();
            streams.back().clip = clip.get();
        }
        clips.push_back(std::move(clip));
    }
    Host& host = TheHost();
    Load load;
    load.Start();
    if (!StartStreams(streams, loops, true)) return false;
    double longest = 0;
    for (Stream& s : streams) longest = std::max(longest, (double)s.clip->frames * loops / Clip::kFps);
    double t0 = NowSeconds();
    long long frame = 0;
    std::vector<float> eventMs, callbackMs, ownMs;
    while (NowSeconds() - t0 < longest + 3) {
        int64_t clockUs = (int64_t)((NowSeconds() - t0) * 1e6);
        // As the C# side does: the media time from the audio clock, every frame.
        for (Stream& s : streams) BSVD_SetClock(s.h, clockUs, 1);
        host.Frame([&](std::vector<Readback>& reads) {
            if (gCpu) return;
            for (Stream& s : streams) Observe(s, clockUs, reads);
        });
        eventMs.push_back((float)host.lastEventMs);
        // Run inside the event unless deferred; then the event's own work is what is left.
        if (host.lastCallbackMs > 0) callbackMs.push_back((float)host.lastCallbackMs);
        ownMs.push_back((float)(host.lastEventMs - (host.lag == 0 ? host.lastCallbackMs : 0)));
        if (gCpu)
            for (Stream& s : streams) TakeCpu(s, clockUs);
        load.Sample();
        bool all = true;
        for (Stream& s : streams) {
            if (!s.ended && Status(s.h).state == BSVD_STATE_ENDED) s.ended = true;
            all = all && s.ended;
        }
        if (all && NowSeconds() - t0 > longest + 0.3) break;
        frame++;
        double next = t0 + frame / 90.0, now = NowSeconds();
        if (next > now) SleepMs((next - now) * 1000);
    }
    host.Finish();
    double wall = NowSeconds() - t0;

    long long tears = 0, order = 0, skipped = 0, ptsErrors = 0, checked = 0, late = 0, expected = 0, decoded = 0, shown = 0, dropped = 0;
    int ended = 0, failed = 0;
    double worstLag = 0;
    for (Stream& s : streams) {
        BSVD_Status st = Status(s.h);
        tears += s.tears;
        order += s.order;
        skipped += s.skipped;
        ptsErrors += s.ptsErrors;
        checked += s.checked;
        late += s.late;
        expected += (long long)s.clip->frames * loops;
        decoded += st.framesDecoded;
        shown += st.framesShown;
        dropped += st.framesDropped;
        ended += s.ended;
        failed += st.state == BSVD_STATE_FAILED;
        worstLag = std::max(worstLag, s.worstLagMs);
        if (st.state == BSVD_STATE_FAILED) std::printf("stream %d failed: %s\n", s.h, Text(s.h, BSVD_TEXT_MESSAGE).c_str());
    }
    {
        // When the slow render events happened: start-up work (imports, pipelines) or steady state.
        int slow = 0, first = -1, last = -1, after2s = 0;
        for (size_t i = 0; i < eventMs.size(); ++i)
            if (eventMs[i] > 4.0f) {
                slow++;
                if (first < 0) first = (int)i;
                last = (int)i;
                if (i > 180) after2s++;
            }
        std::printf("render events over 4 ms: %d (first at event %d, last at %d; %d after the first 2 s)\n", slow, first, last, after2s);
    }
    std::sort(eventMs.begin(), eventMs.end());
    std::printf("streams=%zu path=%s decoder=%s wall=%.1fs ended=%d failed=%d\n", streams.size(), gCpu ? "CPU" : "Vulkan",
                Text(streams[0].h, BSVD_TEXT_DECODER).c_str(), wall, ended, failed);
    std::printf("expected=%lld decoded=%lld shown=%lld dropped=%lld checked=%lld skipped=%lld order_errors=%lld tears=%lld pts_errors=%lld "
                "late=%lld worst_lag_ms=%.1f\n",
                expected, decoded, shown, dropped, checked, skipped, order, tears, ptsErrors, late, worstLag);
    if (!eventMs.empty())
        std::printf("render_event_ms p50=%.3f p99=%.3f max=%.2f (n=%zu, the plugin's present event alone%s)\n", eventMs[eventMs.size() / 2],
                    eventMs[(size_t)(eventMs.size() * 0.99)], eventMs.back(), eventMs.size(),
                    host.lag == 0 && !callbackMs.empty() ? ", with the queue callback Unity ran inside it" : "");
    std::sort(ownMs.begin(), ownMs.end());
    if (!ownMs.empty() && !callbackMs.empty())
        std::printf("render_event_ms_without_callback p50=%.3f p99=%.3f max=%.2f\n", ownMs[ownMs.size() / 2],
                    ownMs[(size_t)(ownMs.size() * 0.99)], ownMs.back());
    std::sort(callbackMs.begin(), callbackMs.end());
    if (!callbackMs.empty())
        std::printf("queue_callback_ms p50=%.3f p99=%.3f max=%.2f (n=%zu frames with a callback: recording + vkQueueSubmit)\n",
                    callbackMs[callbackMs.size() / 2], callbackMs[(size_t)(callbackMs.size() * 0.99)], callbackMs.back(), callbackMs.size());
    load.Print();
    for (Stream& s : streams) {
        s.feeder->Stop();
        BSVD_Destroy(s.h);
    }
    bool released = WaitReleased(5);
    for (Stream& s : streams)
        if (s.tex) host.DestroyTexture(s.tex);
    return released && !failed && ended == (int)streams.size() && decoded == expected && !tears && !order && !ptsErrors &&
           skipped * 100 <= expected && checked > 0;
}

// ---------------------------------------------------------------- preroll and a flush while paused

bool TestPreroll() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    Host& host = TheHost();
    std::vector<Stream> streams(1);
    Stream& s = streams[0];
    s.clip = &clip;
    s.h = Create(clip);
    if (s.h <= 0) return false;
    s.tex = host.MakeTexture((uint32_t)clip.width, (uint32_t)clip.height);
    BSVD_SetTarget(s.h, s.tex, clip.width, clip.height);
    s.feeder->Start(s.h, &clip, 0, 1, false);
    bool ok = true;

    // No clock at all: the first picture alone.
    std::vector<int> seen;
    auto run = [&](double seconds) {
        double start = NowSeconds();
        while (NowSeconds() - start < seconds) {
            host.Frame([&](std::vector<Readback>& reads) {
                Readback r;
                r.texture = s.tex;
                r.rows = {TopRow(s.tex->height), BottomRow(s.tex->height)};
                r.inspect = [&](const uint8_t* px, uint32_t w, uint32_t hh) {
                    bool torn = false;
                    int top = FrameNumber(px, w, hh, clip.fromPkt, torn);
                    if (torn) top = -1;
                    if (seen.empty() || seen.back() != top) seen.push_back(top);
                };
                if (Status(s.h).framesShown > 0) reads.push_back(r);
            });
            SleepMs(1000.0 / 90);
        }
        host.Finish();
    };
    run(1.5);
    BSVD_Status st = Status(s.h);
    std::printf("no clock: shown=%lld shown_pts=%lld held=%d pictures seen:", (long long)st.framesShown, (long long)st.shownPtsUs, st.heldFrames);
    for (int v : seen) std::printf(" %d", v);
    std::printf("\n");
    ok = ok && st.framesShown == 1 && st.shownPtsUs == clip.PtsOf(0) && seen.size() == 1 && seen[0] == 0;

    // A paused clock at the start changes nothing.
    BSVD_SetClock(s.h, clip.PtsOf(0), 0);
    seen.clear();
    run(0.5);
    st = Status(s.h);
    std::printf("paused at 0: shown=%lld pictures seen:", (long long)st.framesShown);
    for (int v : seen) std::printf(" %d", v);
    std::printf("\n");
    ok = ok && st.framesShown == 1 && seen.size() == 1 && seen[0] == 0;

    // A flush with frames before 160 discarded, the clock still paused at 0: the old picture stays until 160 replaces it.
    const int target = 160;
    s.feeder->Stop();
    BSVD_Flush(s.h, clip.PtsOf(target));
    st = Status(s.h);
    bool resetTimes = st.shownPtsUs == BSVD_NO_TIME && st.newestPtsUs == BSVD_NO_TIME;
    size_t from = clip.KeyUnitFor(target);
    s.feeder->Start(s.h, &clip, from, 1, false);
    seen.clear();
    run(1.5);
    st = Status(s.h);
    std::printf("flush to %d (fed from unit %zu, display %d): times reset %d, shown=%lld shown_pts=%lld dropped=%lld pictures seen:", target, from,
                clip.units[from].display, resetTimes ? 1 : 0, (long long)st.framesShown, (long long)st.shownPtsUs, (long long)st.framesDropped);
    for (int v : seen) std::printf(" %d", v);
    std::printf("\n");
    ok = ok && resetTimes && st.framesShown == 2 && st.shownPtsUs == clip.PtsOf(target) && !seen.empty() && seen.back() == target;
    for (size_t i = 0; i < seen.size(); ++i) ok = ok && (seen[i] == 0 || seen[i] == target);

    // Then play from there.
    double t0 = NowSeconds();
    s.last = -1;
    s.lastShown = Status(s.h).framesShown;
    while (NowSeconds() - t0 < 1.5) {
        int64_t clockUs = clip.PtsOf(target) + (int64_t)((NowSeconds() - t0) * 1e6);
        BSVD_SetClock(s.h, clockUs, 1);
        host.Frame([&](std::vector<Readback>& reads) { Observe(s, clockUs, reads); });
        SleepMs(1000.0 / 90);
    }
    host.Finish();
    std::printf("playing from %d: checked=%lld last=%d skipped=%lld order_errors=%lld tears=%lld pts_errors=%lld late=%lld\n", target, s.checked,
                s.last, s.skipped, s.order, s.tears, s.ptsErrors, s.late);
    ok = ok && s.checked >= 30 && !s.order && !s.tears && !s.ptsErrors && s.skipped <= 1 && s.last > target + 30;
    s.feeder->Stop();
    BSVD_Destroy(s.h);
    ok = WaitReleased(5) && ok;
    host.DestroyTexture(s.tex);
    return ok;
}

// ---------------------------------------------------------------- seeks while playing

bool TestSeek() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    Host& host = TheHost();
    std::vector<Stream> streams(1);
    Stream& s = streams[0];
    s.clip = &clip;
    if (!StartStreams(streams, 1, false)) return false;
    bool ok = true;
    int64_t base = 0;
    double t0 = NowSeconds();
    auto play = [&](double seconds) {
        double start = NowSeconds();
        while (NowSeconds() - start < seconds) {
            int64_t clockUs = base + (int64_t)((NowSeconds() - t0) * 1e6);
            BSVD_SetClock(s.h, clockUs, 1);
            host.Frame([&](std::vector<Readback>& reads) { Observe(s, clockUs, reads); });
            SleepMs(1000.0 / 90);
        }
        host.Finish();
    };
    play(1.5);
    std::printf("played to frame %d: checked=%lld\n", s.last, s.checked);
    for (int target : {200, 60, 250}) {
        s.feeder->Stop();
        BSVD_Flush(s.h, clip.PtsOf(target));
        base = clip.PtsOf(target);
        t0 = NowSeconds();
        BSVD_SetClock(s.h, base, 1);
        s.feeder->Start(s.h, &clip, clip.KeyUnitFor(target), 1, false);
        s.floor = target;
        s.firstAfter = -1;
        s.last = -1;
        long long staleBefore = s.stale;
        double start = NowSeconds();
        // The first picture after the seek.
        while (s.firstAfter < 0 && NowSeconds() - start < 3) play(0.05);
        double firstMs = (NowSeconds() - start) * 1000;
        s.floor = -1;  // from here, plain order checks
        play(1.0);
        std::printf("seek to %d: first picture %d after ~%.0f ms, stale=%lld, then up to %d; order_errors=%lld tears=%lld pts_errors=%lld\n",
                    target, s.firstAfter, firstMs, s.stale - staleBefore, s.last, s.order, s.tears, s.ptsErrors);
        // With the clock running, the first picture is the newest one due when it is ready (the contract), so it may be
        // a few frames past the target: no further than the time the decoder took to produce it, plus a frame.
        int latest = target + (int)(firstMs / (1000.0 / Clip::kFps)) + 2;
        ok = ok && s.firstAfter >= target && s.firstAfter <= latest && s.stale == staleBefore && s.last > target + 15;
    }
    std::printf("total: checked=%lld skipped=%lld order_errors=%lld tears=%lld pts_errors=%lld stale=%lld late=%lld\n", s.checked, s.skipped,
                s.order, s.tears, s.ptsErrors, s.stale, s.late);
    ok = ok && !s.order && !s.tears && !s.ptsErrors && !s.stale;
    s.feeder->Stop();
    BSVD_Destroy(s.h);
    ok = WaitReleased(5) && ok;
    host.DestroyTexture(s.tex);
    return ok;
}

// ---------------------------------------------------------------- end of stream

bool TestEos() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    Host& host = TheHost();
    std::vector<Stream> streams(1);
    Stream& s = streams[0];
    s.clip = &clip;
    if (!StartStreams(streams, 1, true)) return false;
    double t0 = NowSeconds(), endedAt = -1;
    double duration = (double)clip.frames / Clip::kFps;
    while (NowSeconds() - t0 < duration + 3) {
        int64_t clockUs = (int64_t)((NowSeconds() - t0) * 1e6);
        BSVD_SetClock(s.h, clockUs, 1);
        host.Frame([&](std::vector<Readback>& reads) { Observe(s, clockUs, reads); });
        if (endedAt < 0 && Status(s.h).state == BSVD_STATE_ENDED) endedAt = NowSeconds() - t0;
        if (endedAt >= 0 && NowSeconds() - t0 > endedAt + 0.5) break;
        SleepMs(1000.0 / 90);
    }
    host.Finish();
    BSVD_Status st = Status(s.h);
    std::printf("clip %.2f s: ended at %.2f s, state %d, decoded=%lld shown=%lld dropped=%lld held=%d last picture %d\n", duration, endedAt,
                st.state, (long long)st.framesDecoded, (long long)st.framesShown, (long long)st.framesDropped, st.heldFrames, s.last);
    // A Feed after the end is refused; a flush makes the decoder take input again.
    int32_t refused = BSVD_Feed(s.h, clip.data.data() + clip.units[0].offset, (int32_t)clip.units[0].size, 0, BSVD_UNIT_KEYFRAME);
    BSVD_Flush(s.h, BSVD_NO_TIME);
    int stateAfterFlush = Status(s.h).state;
    std::printf("feed after end: %d; state after a flush: %d\n", refused, stateAfterFlush);
    bool ok = endedAt > 0 && endedAt < duration + 1.0 && st.framesDecoded == clip.frames && st.framesShown + st.framesDropped == st.framesDecoded &&
              s.last == clip.frames - 1 && refused == BSVD_E_ARGUMENT && stateAfterFlush == BSVD_STATE_RUNNING;
    s.feeder->Stop();
    BSVD_Destroy(s.h);
    ok = WaitReleased(5) && ok;
    host.DestroyTexture(s.tex);
    return ok;
}

// ---------------------------------------------------------------- destroy in every state

bool TestDestroy() {
    Clip clip;
    if (!LoadFixture(Positional(1), clip)) return false;
    Host& host = TheHost();
    bool ok = true;
    std::vector<HostTexture*> textures;
    auto make = [&](bool target) {
        int32_t h = Create(clip);
        if (h > 0 && target) {
            HostTexture* t = gCpu ? nullptr : host.MakeTexture((uint32_t)clip.width, (uint32_t)clip.height);
            if (t) textures.push_back(t);
            BSVD_SetTarget(h, t, clip.width, clip.height);
        }
        return h;
    };
    auto frames = [&](double seconds, const std::vector<int32_t>& playing, double t0) {
        double start = NowSeconds();
        while (NowSeconds() - start < seconds) {
            for (int32_t h : playing) BSVD_SetClock(h, (int64_t)((NowSeconds() - t0) * 1e6), 1);
            host.Frame();
            SleepMs(1000.0 / 90);
        }
    };

    // 1: destroyed at once, before any input.
    int32_t a = make(false);
    ok = ok && a > 0 && BSVD_Destroy(a) == BSVD_OK && BSVD_GetStatus(a, nullptr, 0) == BSVD_E_ARGUMENT;
    BSVD_Status probe{};
    ok = ok && BSVD_GetStatus(a, &probe, (int32_t)sizeof probe) == BSVD_E_HANDLE && BSVD_Destroy(a) == BSVD_E_HANDLE;

    // 2: paused on its first picture, frames held in the decoder.
    int32_t b = make(true);
    Feeder fb;
    fb.Start(b, &clip, 0, 1, false);
    // 3 and 4: playing; 4 is fed only 90 units and ends.
    int32_t c = make(true), d = make(true);
    Feeder fc, fd;
    fc.Start(c, &clip, 0, 1, false);
    fd.Start(d, &clip, 0, 1, true, 90);
    double t0 = NowSeconds();
    frames(1.5, {c, d}, t0);
    BSVD_Status sb = Status(b);
    std::printf("paused decoder: shown=%lld held=%d; playing: shown=%lld\n", (long long)sb.framesShown, sb.heldFrames,
                (long long)Status(c).framesShown);
    // On the CPU path a frame counts as shown only when the caller takes it with BSVD_LockFrame, which this test doesn't.
    ok = ok && (gCpu || sb.framesShown == 1) && sb.heldFrames > 0;
    fb.Stop();
    BSVD_Destroy(b);
    fc.Stop();
    BSVD_Destroy(c);  // mid-play, frames in flight on the GPU
    frames(2.0, {d}, t0);
    int stateD = Status(d).state;
    fd.Stop();
    BSVD_Destroy(d);
    std::printf("decoder fed 90 units: state %d before destroy\n", stateD);
    ok = ok && stateD == BSVD_STATE_ENDED;
    // 5 and 6: BSVD_DestroyAll while playing.
    int32_t e = make(true), f = make(true);
    Feeder fe, ff;
    fe.Start(e, &clip, 0, 1, false);
    ff.Start(f, &clip, 0, 1, false);
    frames(1.0, {e, f}, NowSeconds());
    BSVD_DestroyAll();
    fe.Stop();
    ff.Stop();
    double start = NowSeconds();
    ok = WaitReleased(5) && ok;
    std::printf("released all in %.0f ms; live count %d\n", (NowSeconds() - start) * 1000, BSVD_GetLiveCount());
    ok = CheckLeaks("after destroy") && ok;
    for (HostTexture* t : textures) host.DestroyTexture(t);
    return ok;
}

// ---------------------------------------------------------------- start-up: paths, the hook, the event config

bool TestStartup() {
    Host& host = TheHost();
    bool ok = true;
    host.Load();
    std::printf("after load: path %d, hooks registered %d (priority %d), platform: %s\n", BSVD_GetPath(), host.hooks, host.hookPriority,
                Text(0, BSVD_TEXT_PLATFORM).c_str());
    ok = ok && BSVD_GetPath() == BSVD_PATH_NONE && host.hooks == 1 && host.hookPriority < kUnityVulkanInitCallbackMaxPriority;
    // The library reserves exactly one event id at load, and that is the id the C# side is told to issue.
    const int eventId = Host::EventId();
    bool reservedOne = host.reserved.size() == 1 && host.reserved[0].count == 1 && host.reserved[0].base == eventId;
    std::printf("render event id %d (what the C# side issues); reserved at load:", eventId);
    for (const Host::Range& r : host.reserved) std::printf(" %d+%d", r.base, r.count);
    std::printf("%s -> %s\n", host.reserved.empty() ? " nothing" : "", reservedOne ? "as specified" : "WRONG");
    ok = ok && reservedOne;
    BSVD_Config cfg{};
    cfg.codec = BSVD_CODEC_H264;
    int32_t none = BSVD_Create(&cfg, nullptr, 0);
    std::printf("create with no device: %d\n", none);
    ok = ok && none == BSVD_E_UNSUPPORTED;
    int h264 = BSVD_IsCodecSupported(BSVD_CODEC_H264), hevc = BSVD_IsCodecSupported(BSVD_CODEC_HEVC), bad = BSVD_IsCodecSupported(7);
    std::printf("codecs: H.264 %d, HEVC %d, 7 %d; api version %d\n", h264, hevc, bad, BSVD_GetApiVersion());
    ok = ok && h264 == 1 && hevc == 1 && bad == 0 && BSVD_GetApiVersion() == BSVD_API_VERSION;

    struct Case {
        const char* name;
        DeviceOptions options;
        int renderer;
        int path;
    };
    DeviceOptions full, noFeature, noForeign, noAhb, vulkan10;
    noFeature.ycbcrFeature = false;
    noForeign.foreign = false;
    noAhb.ahb = false;
    vulkan10.vulkan11 = false;  // YCbCr by its extension, but no extended image usage for the sRGB storage intermediate
    Case cases[] = {{"Vulkan device with everything", full, kUnityGfxRendererVulkan, BSVD_PATH_VULKAN},
                    {"Vulkan device without samplerYcbcrConversion", noFeature, kUnityGfxRendererVulkan, BSVD_PATH_CPU},
                    {"Vulkan device without the foreign queue extension", noForeign, kUnityGfxRendererVulkan, BSVD_PATH_CPU},
                    {"Vulkan device without buffer import", noAhb, kUnityGfxRendererVulkan, BSVD_PATH_CPU},
                    {"Vulkan 1.0 device without VK_KHR_maintenance2", vulkan10, kUnityGfxRendererVulkan, BSVD_PATH_CPU},
                    {"OpenGL ES", full, kUnityGfxRendererOpenGLES30, BSVD_PATH_CPU},
                    {"the null renderer", full, kUnityGfxRendererNull, BSVD_PATH_NONE}};
    for (const Case& c : cases) {
        host.eventConfigured = false;
        if (!host.CreateVulkan(c.options)) return false;
        host.Initialize(c.renderer);
        int path = BSVD_GetPath();
        std::printf("%s: path %d (want %d); %s\n", c.name, path, c.path, Text(0, BSVD_TEXT_PLATFORM).c_str());
        ok = ok && path == c.path;
        if (c.path == BSVD_PATH_VULKAN) {
            const UnityVulkanPluginEventConfig& e = host.eventConfig;
            bool config = host.eventConfigured && e.renderPassPrecondition == kUnityVulkanRenderPass_EnsureOutside &&
                          e.graphicsQueueAccess == kUnityVulkanGraphicsQueueAccess_DontCare &&
                          e.flags == (kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState);
            // Configured with the id it reserved and hands out, so Unity would honour it.
            int reservedId = host.reserved.empty() ? -1 : host.reserved[0].base;
            bool sameId = host.configuredId == reservedId && reservedId == Host::EventId() && host.RunsOutsideRenderPass(Host::EventId());
            std::printf("  event config: set %d, render pass %d, queue %d, flags %u -> %s; configured id %d, reserved %d, "
                        "BSVD_GetRenderEventId %d -> %s\n",
                        host.eventConfigured, (int)e.renderPassPrecondition, (int)e.graphicsQueueAccess, e.flags, config ? "as specified" : "WRONG",
                        host.configuredId, reservedId, Host::EventId(), sameId ? "one id" : "WRONG (configured under an id Unity didn't reserve)");
            ok = ok && config && sameId;
            // A decoder made now runs on Vulkan; render events with no target do nothing.
            Clip clip;
            if (LoadFixture("o1080.h264", clip)) {
                int32_t d = Create(clip);
                ok = ok && d > 0 && Status(d).path == BSVD_PATH_VULKAN;
                for (int i = 0; i < 5; ++i) host.Frame();
                BSVD_Destroy(d);
                ok = WaitReleased(5) && ok;
            }
        }
        host.Shutdown();
        ok = ok && BSVD_GetPath() == BSVD_PATH_NONE;
        host.DestroyVulkan();
    }
    host.Unload();
    ok = host.Clean() && ok;
    return CheckLeaks("at the end") && ok;
}

}  // namespace

int main(int argc, char** argv) {
    setvbuf(stdout, nullptr, _IONBF, 0);
    gArgc = argc;
    gArgv = argv;
    gFx = Arg("--fx", ".");
    gCpu = Flag("--cpu");
    // Unity 6 on Android records plugin events into secondary command buffers inside a render pass (graphics jobs, its
    // default); --primary hands the event the frame's primary buffer instead, as Unity does without graphics jobs.
    // --deferred: Unity runs its submissions and the queue access callbacks two frames late, on another thread.
    TheHost().secondary = !Flag("--primary");
    TheHost().lag = Flag("--deferred") ? 2 : 0;
    std::printf("render event command buffer: %s; queue access callbacks %s\n",
                TheHost().secondary ? "secondary inside a render pass (graphics jobs)" : "primary", TheHost().lag ? "two frames late, on another thread" : "run at once");
    StartBinderPool();
    std::string test = argc > 1 ? argv[1] : "";
    bool pass = false;
    if (test == "startup") {
        pass = TestStartup();
        std::printf("RESULT %s\n", pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }
    struct Entry {
        const char* name;
        bool (*run)();
    };
    const Entry tests[] = {{"order", TestOrder}, {"bars", TestBars}, {"orient", TestOrient}, {"paced", TestPaced},
                           {"preroll", TestPreroll}, {"seek", TestSeek}, {"eos", TestEos}, {"destroy", TestDestroy}};
    const Entry* chosen = nullptr;
    for (const Entry& e : tests)
        if (test == e.name) chosen = &e;
    if (!chosen) {
        std::printf("usage: bsvdtool startup | order <clip> [--frames n] | bars <clip> <matrix> <full> | orient <clip> |\n"
                    "       paced <clip[:n],...> [--loops n] | preroll <clip> | seek <clip> | eos <clip> | destroy <clip>\n"
                    "       [--cpu] (CPU path: Unity on OpenGL ES) [--fx dir]\n"
                    "       [--primary] (the render event records into a primary command buffer, not a secondary one)\n"
                    "       [--deferred] (Unity's submissions and queue callbacks run two frames late, on another thread)\n");
        return 2;
    }
    Host& host = TheHost();
    if (Flag("--unorm")) host.textureFormat = VK_FORMAT_R8G8B8A8_UNORM;  // a linear texture instead of an sRGB one
    host.Load();
    if (!host.CreateVulkan(DeviceOptions())) return 2;
    host.Initialize(gCpu ? kUnityGfxRendererOpenGLES30 : kUnityGfxRendererVulkan);
    int path = BSVD_GetPath();
    std::printf("device %s; path %d; render event %d%s; %s\n", host.deviceName.c_str(), path, Host::EventId(),
                path != BSVD_PATH_VULKAN                                      ? ""
                : !host.secondary && host.RunsOutsideRenderPass(Host::EventId()) ? " (runs outside render passes)"
                                                                              : " (runs INSIDE a render pass)",
                Text(0, BSVD_TEXT_PLATFORM).c_str());
    if (path != (gCpu ? BSVD_PATH_CPU : BSVD_PATH_VULKAN)) {
        std::printf("RESULT FAIL (wrong path)\n");
        return 1;
    }
    double start = NowSeconds();
    pass = chosen->run();
    host.Shutdown();
    pass = host.Clean() && pass;
    pass = CheckLeaks("after shutdown") && pass;
    host.DestroyVulkan();
    host.Unload();
    std::printf("time %.1f s\nRESULT %s\n", NowSeconds() - start, pass ? "PASS" : "FAIL");
    return pass ? 0 : 1;
}
