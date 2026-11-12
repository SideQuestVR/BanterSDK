// bsadtool - drives the bsaudiodecode.h API exactly as the C# side does, for tests on a PC or,
// over adb shell, on a device.
//
//   bsadtool caps
//   bsadtool decode aac  <input.adts> <output.f32> [--asc <hex> | --adts] [--channels <n>] [--reset-at <unit>]
//   bsadtool decode eac3 <input.ec3>  <output.f32> [--reset-at <unit>]
//
// decode writes interleaved float samples and prints one "format" line per format it saw, then a
// "done" line: units fed, frames out, damaged units.
#include "bsaudiodecode.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#if defined(__ANDROID__)
#include <dlfcn.h>
#elif defined(_WIN32)
#include <windows.h>
#endif

namespace {

#if defined(_WIN32)
// A fault inside a system decoder is a finding: name the module it happened in.
LONG WINAPI ReportFault(EXCEPTION_POINTERS* info) {
    void* address = info->ExceptionRecord->ExceptionAddress;
    HMODULE module = nullptr;
    char path[MAX_PATH] = "?";
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           static_cast<LPCSTR>(address), &module)) {
        GetModuleFileNameA(module, path, MAX_PATH);
    }
    printf("fault 0x%08lx in %s at +0x%llx\n", info->ExceptionRecord->ExceptionCode, path,
           static_cast<unsigned long long>(static_cast<char*>(address) - reinterpret_cast<char*>(module)));
    fflush(stdout);
    return EXCEPTION_EXECUTE_HANDLER;
}
#endif

#if defined(__ANDROID__)
// A command-line process has no binder thread pool, and MediaCodec needs one for its callbacks.
void StartBinderThreadPool() {
    void* binder = dlopen("libbinder_ndk.so", RTLD_NOW);
    if (binder == nullptr) {
        return;
    }
    auto setMax = reinterpret_cast<bool (*)(uint32_t)>(dlsym(binder, "ABinderProcess_setThreadPoolMaxThreadCount"));
    auto start = reinterpret_cast<void (*)()>(dlsym(binder, "ABinderProcess_startThreadPool"));
    if (setMax != nullptr) {
        setMax(4);
    }
    if (start != nullptr) {
        start();
    }
}
#endif

const int kRates[] = {96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350};

bool ReadFile(const char* path, std::vector<uint8_t>& bytes) {
    FILE* file = fopen(path, "rb");
    if (file == nullptr) {
        return false;
    }
    uint8_t chunk[65536];
    size_t got;
    while ((got = fread(chunk, 1, sizeof(chunk), file)) > 0) {
        bytes.insert(bytes.end(), chunk, chunk + got);
    }
    fclose(file);
    return true;
}

std::vector<uint8_t> FromHex(const char* text) {
    std::vector<uint8_t> bytes;
    for (size_t i = 0; text[i] != '\0' && text[i + 1] != '\0'; i += 2) {
        char pair[3] = {text[i], text[i + 1], 0};
        bytes.push_back(static_cast<uint8_t>(strtoul(pair, nullptr, 16)));
    }
    return bytes;
}

struct Unit {
    size_t offset;
    size_t length;
};

// ADTS frames, as payloads with their headers stripped. Fills the first header's fields.
std::vector<Unit> AdtsUnits(const std::vector<uint8_t>& data, int& profile, int& rateIndex, int& channelConfiguration, bool whole) {
    std::vector<Unit> units;
    size_t at = 0;
    bool first = true;
    while (at + 7 <= data.size()) {
        if (data[at] != 0xFF || (data[at + 1] & 0xF6) != 0xF0) {
            ++at;
            continue;
        }
        const bool protectedFrame = (data[at + 1] & 1) == 0;
        const size_t length = ((data[at + 3] & 3u) << 11) | (static_cast<size_t>(data[at + 4]) << 3) | (data[at + 5] >> 5);
        const size_t header = protectedFrame ? 9 : 7;
        if (length <= header || at + length > data.size()) {
            ++at;
            continue;
        }
        if (first) {
            profile = data[at + 2] >> 6;
            rateIndex = (data[at + 2] >> 2) & 0xF;
            channelConfiguration = ((data[at + 2] & 1) << 2) | (data[at + 3] >> 6);
            first = false;
        }
        units.push_back(whole ? Unit{at, length} : Unit{at + header, length - header});
        at += length;
    }
    return units;
}

// E-AC-3 (and AC-3) syncframes.
std::vector<Unit> SyncFrames(const std::vector<uint8_t>& data, int& rate, int& channels) {
    static const int kAc3Rates[] = {48000, 44100, 32000};
    static const int kModeChannels[] = {2, 1, 2, 3, 3, 4, 4, 5};
    std::vector<Unit> units;
    size_t at = 0;
    bool first = true;
    while (at + 7 <= data.size()) {
        if (data[at] != 0x0B || data[at + 1] != 0x77) {
            ++at;
            continue;
        }
        const size_t length = ((((data[at + 2] & 7u) << 8) | data[at + 3]) + 1) * 2;
        if (length < 8 || at + length > data.size()) {
            ++at;
            continue;
        }
        if (first) {
            const int fscod = data[at + 4] >> 6;
            rate = fscod == 3 ? kAc3Rates[(data[at + 4] >> 4) & 3] / 2 : kAc3Rates[fscod];
            channels = kModeChannels[(data[at + 4] >> 1) & 7] + (data[at + 4] & 1);
            first = false;
        }
        units.push_back({at, length});
        at += length;
    }
    return units;
}

int Caps() {
    printf("api %d\n", BSAD_GetApiVersion());
    const int codecs[] = {BSAD_CODEC_AAC, BSAD_CODEC_EAC3};
    const char* names[] = {"aac", "eac3"};
    for (int i = 0; i < 2; ++i) {
        const int available = BSAD_IsAvailable(codecs[i]);
        std::string decoder;
        if (available) {
            const uint8_t lcStereo48k[] = {0x11, 0x90};
            BSAD_Decoder* opened = nullptr;
            char message[256];
            const int result = BSAD_Open(codecs[i], 48000, 2, codecs[i] == BSAD_CODEC_AAC ? lcStereo48k : nullptr,
                                         codecs[i] == BSAD_CODEC_AAC ? 2 : 0, &opened, message, sizeof(message));
            if (result == BSAD_OK) {
                char name[256];
                BSAD_DecoderName(opened, name, sizeof(name));
                decoder = name;
                BSAD_Close(opened);
            } else {
                decoder = std::string("open failed: ") + message;
            }
        }
        printf("%s available=%d decoder=%s\n", names[i], available, decoder.c_str());
    }
    return 0;
}

int Decode(int argc, char** argv) {
    if (argc < 5) {
        fprintf(stderr, "usage: bsadtool decode aac|eac3 <input> <output.f32> [--asc hex] [--reset-at unit]\n");
        return 2;
    }
    const bool aac = strcmp(argv[2], "aac") == 0;
    std::vector<uint8_t> input;
    if (!ReadFile(argv[3], input)) {
        fprintf(stderr, "cannot read %s\n", argv[3]);
        return 2;
    }
    std::vector<uint8_t> asc;
    long resetAt = -1;
    bool wholeAdts = false;
    int channelsOverride = 0;
    for (int i = 5; i < argc; ++i) {
        if (strcmp(argv[i], "--adts") == 0) {
            wholeAdts = true;
        } else if (strcmp(argv[i], "--asc") == 0 && i + 1 < argc) {
            asc = FromHex(argv[++i]);
        } else if (strcmp(argv[i], "--reset-at") == 0 && i + 1 < argc) {
            resetAt = strtol(argv[++i], nullptr, 10);
        } else if (strcmp(argv[i], "--channels") == 0 && i + 1 < argc) {
            channelsOverride = static_cast<int>(strtol(argv[++i], nullptr, 10));
        }
    }

    std::vector<Unit> units;
    int rate = 0;
    int channels = 0;
    if (aac) {
        int profile = 1;
        int rateIndex = 3;
        int configuration = 2;
        units = AdtsUnits(input, profile, rateIndex, configuration, wholeAdts);
        if (asc.empty() && !wholeAdts) {
            const int objectType = profile + 1;
            asc.push_back(static_cast<uint8_t>((objectType << 3) | (rateIndex >> 1)));
            asc.push_back(static_cast<uint8_t>(((rateIndex & 1) << 7) | (configuration << 3)));
        }
        rate = rateIndex < 13 ? kRates[rateIndex] : 0;
        channels = configuration == 7 ? 8 : configuration == 0 ? 2 : configuration;
    } else {
        units = SyncFrames(input, rate, channels);
    }
    if (channelsOverride > 0) {
        channels = channelsOverride;
    }
    if (units.empty()) {
        fprintf(stderr, "no units in %s\n", argv[3]);
        return 2;
    }

    BSAD_Decoder* decoder = nullptr;
    char message[512];
    int result = BSAD_Open(aac ? BSAD_CODEC_AAC : BSAD_CODEC_EAC3, rate, channels, asc.empty() ? nullptr : asc.data(),
                           static_cast<int32_t>(asc.size()), &decoder, message, sizeof(message));
    if (result != BSAD_OK) {
        printf("open result=%d message=%s\n", result, message);
        return 3;
    }
    char name[256];
    BSAD_DecoderName(decoder, name, sizeof(name));
    printf("decoder %s\n", name);

    FILE* out = fopen(argv[4], "wb");
    if (out == nullptr) {
        fprintf(stderr, "cannot write %s\n", argv[4]);
        BSAD_Close(decoder);
        return 2;
    }
    const int32_t maxFrames = 4096;
    std::vector<float> samples(static_cast<size_t>(maxFrames) * BSAD_MAX_CHANNELS);
    int32_t format[BSAD_FORMAT_LENGTH] = {};
    int32_t lastRate = -1, lastChannels = -1, lastMask = -1;
    long long frames = 0;
    int damaged = 0;
    auto take = [&](const uint8_t* data, int32_t offset, int32_t count) -> bool {
        const int32_t got = BSAD_Decode(decoder, data, offset, count, samples.data(), maxFrames, format);
        if (got < 0) {
            BSAD_Message(decoder, message, sizeof(message));
            printf("stopped result=%d message=%s\n", got, message);
            return false;
        }
        damaged = format[BSAD_FORMAT_DAMAGED];
        if (got > 0) {
            if (format[BSAD_FORMAT_RATE] != lastRate || format[BSAD_FORMAT_CHANNELS] != lastChannels || format[BSAD_FORMAT_MASK] != lastMask) {
                lastRate = format[BSAD_FORMAT_RATE];
                lastChannels = format[BSAD_FORMAT_CHANNELS];
                lastMask = format[BSAD_FORMAT_MASK];
                printf("format at=%lld rate=%d channels=%d mask=0x%x\n", frames, lastRate, lastChannels, static_cast<unsigned>(lastMask));
            }
            fwrite(samples.data(), sizeof(float), static_cast<size_t>(got) * static_cast<size_t>(format[BSAD_FORMAT_CHANNELS]), out);
            frames += got;
        }
        return true;
    };
    bool running = true;
    for (size_t i = 0; i < units.size() && running; ++i) {
        if (resetAt >= 0 && static_cast<long>(i) == resetAt) {
            BSAD_Reset(decoder);
            printf("reset at unit %zu, frames %lld\n", i, frames);
        }
        running = take(input.data(), static_cast<int32_t>(units[i].offset), static_cast<int32_t>(units[i].length));
    }
    // Whatever the decoder still holds.
    if (running && BSAD_Finish(decoder) != BSAD_OK) {
        BSAD_Message(decoder, message, sizeof(message));
        printf("finish failed message=%s\n", message);
    }
    for (int i = 0; i < 16 && running; ++i) {
        const long long before = frames;
        running = take(nullptr, 0, 0);
        if (frames == before) {
            break;
        }
    }
    fclose(out);
    BSAD_Close(decoder);
    printf("done units=%zu frames=%lld damaged=%d\n", units.size(), frames, damaged);
    return running ? 0 : 4;
}

}  // namespace

int main(int argc, char** argv) {
#if defined(__ANDROID__)
    StartBinderThreadPool();
#elif defined(_WIN32)
    SetUnhandledExceptionFilter(ReportFault);
#endif
    if (argc >= 2 && strcmp(argv[1], "caps") == 0) {
        return Caps();
    }
    if (argc >= 2 && strcmp(argv[1], "decode") == 0) {
        return Decode(argc, argv);
    }
    fprintf(stderr, "usage: bsadtool caps | decode aac|eac3 <input> <output.f32> [--asc hex] [--reset-at unit]\n");
    return 2;
}
