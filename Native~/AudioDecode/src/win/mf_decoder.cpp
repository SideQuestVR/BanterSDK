// mf_decoder.cpp - the Windows backend: Media Foundation's audio decoder transforms.
//
// Media Foundation is resolved at run time (LoadLibraryExW from System32 + GetProcAddress), so the
// DLL loads on Windows N editions and reports BSAD_E_UNSUPPORTED there. The decoders are
// synchronous MFTs: ProcessInput, then ProcessOutput until it needs more input.
#include "bsaudiodecode.h"
#include "core/backend.h"

#include <windows.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>

#include <cstdio>
#include <cstring>
#include <mutex>
#include <thread>
#include <type_traits>

namespace bsad {
namespace {

using Microsoft::WRL::ComPtr;

// {A7FB87AF-2D02-42FB-A4D4-05CD93843BDD}, MFAudioFormat_Dolby_DDPlus.
const GUID kSubtypeEac3 = {0xa7fb87af, 0x2d02, 0x42fb, {0xa4, 0xd4, 0x05, 0xcd, 0x93, 0x84, 0x3b, 0xdd}};


// A decoder that rejects this many units in a row has stopped working.
constexpr int kMaxRejectedInARow = 64;

struct MfApi {
    decltype(&::MFStartup) Startup = nullptr;
    decltype(&::MFShutdown) Shutdown = nullptr;
    decltype(&::MFTEnumEx) TEnumEx = nullptr;
    decltype(&::MFCreateMediaType) CreateMediaType = nullptr;
    decltype(&::MFCreateSample) CreateSample = nullptr;
    decltype(&::MFCreateMemoryBuffer) CreateMemoryBuffer = nullptr;
};

MfApi LoadMf() noexcept {
    MfApi api;
    // System32 only: never an mfplat.dll planted beside the application.
    HMODULE platform = LoadLibraryExW(L"mfplat.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (platform == nullptr) {
        return api;
    }
    auto resolve = [platform](auto& slot, const char* name) {
        slot = reinterpret_cast<std::remove_reference_t<decltype(slot)>>(reinterpret_cast<void*>(GetProcAddress(platform, name)));
        return slot != nullptr;
    };
    const bool ok = resolve(api.Startup, "MFStartup") && resolve(api.Shutdown, "MFShutdown") &&
                    resolve(api.TEnumEx, "MFTEnumEx") && resolve(api.CreateMediaType, "MFCreateMediaType") &&
                    resolve(api.CreateSample, "MFCreateSample") && resolve(api.CreateMemoryBuffer, "MFCreateMemoryBuffer");
    return ok ? api : MfApi{};
}

// Null without Media Foundation. The first call also keeps the MTA alive for the process.
const MfApi* Mf() noexcept {
    static const MfApi api = [] {
        CO_MTA_USAGE_COOKIE cookie = nullptr;
        CoIncrementMTAUsage(&cookie);
        return LoadMf();
    }();
    return api.Startup != nullptr ? &api : nullptr;
}

// Puts the calling thread in the MTA, once per thread. Never undone: a CoUninitialize at thread
// exit would run under the loader lock, and the MTA usage reference keeps the apartment alive.
// False when the thread is already an STA.
bool JoinMta() noexcept {
    thread_local int state = 0;  // 0 not tried, 1 in the MTA, 2 an STA
    if (state == 0) {
        state = SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) ? 1 : 2;
    }
    return state == 1;
}

// Runs work in the MTA: inline when this thread can join it, otherwise on a short-lived worker, so
// a transform is never tied to an STA it will not be called from.
template <typename Work>
void RunInMta(Work&& work) {
    if (JoinMta()) {
        work();
        return;
    }
    std::thread worker([&work] {
        const HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        work();
        if (SUCCEEDED(hr)) {
            CoUninitialize();
        }
    });
    worker.join();
}

std::string HrText(HRESULT hr) {
    char text[16];
    snprintf(text, sizeof(text), "0x%08lX", static_cast<unsigned long>(hr));
    return text;
}

std::string Utf8(const wchar_t* text) {
    if (text == nullptr || *text == L'\0') {
        return {};
    }
    const int length = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (length <= 1) {
        return {};
    }
    std::string result(static_cast<size_t>(length - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr, nullptr);
    return result;
}

struct ActivateList {
    IMFActivate** items = nullptr;
    UINT32 count = 0;
    ActivateList() = default;
    ActivateList(const ActivateList&) = delete;
    ActivateList& operator=(const ActivateList&) = delete;
    ~ActivateList() {
        for (UINT32 i = 0; i < count; ++i) {
            if (items[i] != nullptr) {
                items[i]->Release();
            }
        }
        CoTaskMemFree(items);
    }
};

std::string FriendlyName(IMFActivate* activate) {
    wchar_t* name = nullptr;
    UINT32 length = 0;
    std::string result;
    if (SUCCEEDED(activate->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &name, &length))) {
        result = Utf8(name);
        CoTaskMemFree(name);
    }
    return result;
}

const GUID& SubtypeOf(int codec) {
    return codec == BSAD_CODEC_AAC ? MFAudioFormat_AAC : kSubtypeEac3;
}

const char* CodecText(int codec) {
    return codec == BSAD_CODEC_AAC ? "AAC" : "E-AC-3";
}

HRESULT EnumerateDecoders(int codec, ActivateList& list) {
    const MFT_REGISTER_TYPE_INFO input{MFMediaType_Audio, SubtypeOf(codec)};
    return Mf()->TEnumEx(MFT_CATEGORY_AUDIO_DECODER, MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                         &input, nullptr, &list.items, &list.count);
}

class MfDecoder final : public Backend {
public:
    ~MfDecoder() override {
        m_output.Reset();
        m_outputBuffer.Reset();
        m_transform.Reset();
        if (m_activate) {
            m_activate->ShutdownObject();
            m_activate.Reset();
        }
        if (m_started) {
            Mf()->Shutdown();
        }
    }

    int Open(const OpenRequest& request, std::string& error) {
        const MfApi* mf = Mf();
        if (mf == nullptr) {
            error = "Media Foundation is not available on this system";
            return BSAD_E_UNSUPPORTED;
        }
        HRESULT hr = mf->Startup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(hr)) {
            error = "Media Foundation did not start (" + HrText(hr) + ")";
            return BSAD_E_UNSUPPORTED;
        }
        m_started = true;
        m_codec = request.codec;
        m_nominalRate = request.rate > 0 ? request.rate : 48000;

        ComPtr<IMFMediaType> input;
        hr = mf->CreateMediaType(&input);
        if (FAILED(hr)) {
            error = "MFCreateMediaType failed (" + HrText(hr) + ")";
            return BSAD_E_FAILED;
        }
        input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        input->SetGUID(MF_MT_SUBTYPE, SubtypeOf(request.codec));
        input->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, static_cast<UINT32>(m_nominalRate));
        input->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, static_cast<UINT32>(request.channels > 0 ? request.channels : 2));
        if (request.codec == BSAD_CODEC_AAC) {
            // The tail of HEAACWAVEINFO (payload type 0: raw access units, 1: ADTS; no profile preference),
            // then the AudioSpecificConfig when there is one.
            const UINT32 payload = request.setup.empty() ? 1 : 0;
            std::vector<uint8_t> user(12, 0);
            user[0] = static_cast<uint8_t>(payload);
            user[2] = 0xFE;
            user.insert(user.end(), request.setup.begin(), request.setup.end());
            input->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE, payload);
            input->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            input->SetBlob(MF_MT_USER_DATA, user.data(), static_cast<UINT32>(user.size()));
        }

        ActivateList list;
        hr = EnumerateDecoders(request.codec, list);
        if (FAILED(hr) || list.count == 0) {
            error = std::string("this system has no ") + CodecText(request.codec) + " decoder";
            return BSAD_E_UNSUPPORTED;
        }
        HRESULT refused = S_OK;
        for (UINT32 i = 0; i < list.count && !m_transform; ++i) {
            ComPtr<IMFTransform> transform;
            hr = list.items[i]->ActivateObject(IID_PPV_ARGS(&transform));
            if (FAILED(hr)) {
                refused = hr;
                continue;
            }
            hr = transform->SetInputType(0, input.Get(), 0);
            if (FAILED(hr)) {
                refused = hr;
                transform.Reset();
                list.items[i]->ShutdownObject();
                continue;
            }
            m_transform = transform;
            m_activate = list.items[i];
            m_name = FriendlyName(list.items[i]);
        }
        if (!m_transform) {
            error = std::string("the system ") + CodecText(request.codec) + " decoder refused this stream (" + HrText(refused) + ")";
            return BSAD_E_REJECTED;
        }
        if (!ChooseOutputType(error)) {
            return m_passThroughOnly ? BSAD_E_UNSUPPORTED : BSAD_E_REJECTED;
        }
        m_transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        m_transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
        return BSAD_OK;
    }

    int Feed(const uint8_t* data, size_t bytes, OutputQueue& output, int& damaged, std::string& error) override {
        JoinMta();
        const MfApi* mf = Mf();
        ComPtr<IMFMediaBuffer> buffer;
        ComPtr<IMFSample> sample;
        HRESULT hr = mf->CreateMemoryBuffer(static_cast<DWORD>(bytes), &buffer);
        if (SUCCEEDED(hr)) {
            BYTE* to = nullptr;
            hr = buffer->Lock(&to, nullptr, nullptr);
            if (SUCCEEDED(hr)) {
                std::memcpy(to, data, bytes);
                buffer->Unlock();
                hr = buffer->SetCurrentLength(static_cast<DWORD>(bytes));
            }
        }
        if (SUCCEEDED(hr)) {
            hr = mf->CreateSample(&sample);
        }
        if (SUCCEEDED(hr)) {
            hr = sample->AddBuffer(buffer.Get());
        }
        if (FAILED(hr)) {
            error = "could not hand a unit to Media Foundation (" + HrText(hr) + ")";
            return BSAD_E_FAILED;
        }
        const LONGLONG duration = 10000000LL * (m_codec == BSAD_CODEC_AAC ? 1024 : 1536) / m_nominalRate;
        sample->SetSampleTime(m_time);
        sample->SetSampleDuration(duration);
        m_time += duration;
        if (m_discontinuity) {
            sample->SetUINT32(MFSampleExtension_Discontinuity, TRUE);
            m_discontinuity = false;
        }

        hr = m_transform->ProcessInput(0, sample.Get(), 0);
        if (hr == MF_E_NOTACCEPTING) {
            const int drained = Drain(output, damaged, error);
            if (drained != BSAD_OK) {
                return drained;
            }
            hr = m_transform->ProcessInput(0, sample.Get(), 0);
        }
        if (FAILED(hr)) {
            ++damaged;
            if (++m_rejectedInARow >= kMaxRejectedInARow) {
                error = std::string("the system ") + CodecText(m_codec) + " decoder rejected " + std::to_string(m_rejectedInARow) +
                        " units in a row (" + HrText(hr) + ")";
                return BSAD_E_FAILED;
            }
            return BSAD_OK;
        }
        m_rejectedInARow = 0;
        return Drain(output, damaged, error);
    }

    void Flush() override {
        JoinMta();
        m_transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
        m_discontinuity = true;
    }

    int Finish(OutputQueue& output, int& damaged, std::string& error) override {
        JoinMta();
        // The decoder holds back about one unit; draining releases it and leaves it ready for more.
        m_transform->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN, 0);
        return Drain(output, damaged, error);
    }

    std::string Name() const override { return m_name; }

private:
    // Picks float, or 16-bit PCM when float isn't offered, with the most channels up to 7.1.
    bool ChooseOutputType(std::string& error) {
        ComPtr<IMFMediaType> best;
        int bestScore = -1;
        int offered = 0;
        for (DWORD i = 0;; ++i) {
            ComPtr<IMFMediaType> type;
            const HRESULT hr = m_transform->GetOutputAvailableType(0, i, &type);
            if (FAILED(hr)) {
                break;
            }
            ++offered;
            GUID subtype = GUID_NULL;
            type->GetGUID(MF_MT_SUBTYPE, &subtype);
            const UINT32 bits = MFGetAttributeUINT32(type.Get(), MF_MT_AUDIO_BITS_PER_SAMPLE, 0);
            const UINT32 channels = MFGetAttributeUINT32(type.Get(), MF_MT_AUDIO_NUM_CHANNELS, 0);
            int score;
            if (subtype == MFAudioFormat_Float && bits == 32) {
                score = 1000;
            } else if (subtype == MFAudioFormat_PCM && bits == 16) {
                score = 0;
            } else {
                continue;
            }
            if (channels == 0 || channels > static_cast<UINT32>(kMaxChannels)) {
                continue;
            }
            score += static_cast<int>(channels);
            if (score > bestScore) {
                bestScore = score;
                best = type;
            }
        }
        if (!best) {
            // Some systems keep this decoder for pass-through to a receiver only (seen on Windows 11 25H2).
            error = std::string("the system ") + CodecText(m_codec) +
                    (offered > 0 ? " decoder only passes the stream through to an external receiver; it does not decode it"
                                 : " decoder offers no output of up to 8 channels");
            m_passThroughOnly = offered > 0;
            return false;
        }
        HRESULT hr = m_transform->SetOutputType(0, best.Get(), 0);
        if (FAILED(hr)) {
            error = std::string("the system ") + CodecText(m_codec) + " decoder refused its own output format (" + HrText(hr) + ")";
            return false;
        }
        m_float = bestScore >= 1000;
        m_format.rate = static_cast<int>(MFGetAttributeUINT32(best.Get(), MF_MT_AUDIO_SAMPLES_PER_SECOND, 0));
        m_format.channels = static_cast<int>(MFGetAttributeUINT32(best.Get(), MF_MT_AUDIO_NUM_CHANNELS, 0));
        m_format.mask = MFGetAttributeUINT32(best.Get(), MF_MT_AUDIO_CHANNEL_MASK, 0);

        MFT_OUTPUT_STREAM_INFO info{};
        hr = m_transform->GetOutputStreamInfo(0, &info);
        if (FAILED(hr)) {
            error = "GetOutputStreamInfo failed (" + HrText(hr) + ")";
            return false;
        }
        m_providesSamples = (info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
        m_output.Reset();
        m_outputBuffer.Reset();
        if (!m_providesSamples) {
            const DWORD size = info.cbSize > 65536 ? info.cbSize : 65536;
            hr = Mf()->CreateMemoryBuffer(size, &m_outputBuffer);
            if (SUCCEEDED(hr)) {
                hr = Mf()->CreateSample(&m_output);
            }
            if (SUCCEEDED(hr)) {
                hr = m_output->AddBuffer(m_outputBuffer.Get());
            }
            if (FAILED(hr)) {
                error = "could not make an output buffer (" + HrText(hr) + ")";
                return false;
            }
        }
        return true;
    }

    int Drain(OutputQueue& output, int& damaged, std::string& error) {
        for (int guard = 0; guard < 64; ++guard) {
            MFT_OUTPUT_DATA_BUFFER data{};
            data.dwStreamID = 0;
            if (!m_providesSamples) {
                m_outputBuffer->SetCurrentLength(0);
                data.pSample = m_output.Get();
            }
            DWORD status = 0;
            const HRESULT hr = m_transform->ProcessOutput(0, 1, &data, &status);
            if (data.pEvents != nullptr) {
                data.pEvents->Release();
            }
            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) {
                return BSAD_OK;
            }
            if (hr == MF_E_TRANSFORM_STREAM_CHANGE) {
                if (!ChooseOutputType(error)) {
                    return BSAD_E_FAILED;
                }
                continue;
            }
            if (FAILED(hr)) {
                if (m_providesSamples && data.pSample != nullptr) {
                    data.pSample->Release();
                }
                ++damaged;
                return BSAD_OK;
            }
            IMFSample* produced = data.pSample;
            if (produced != nullptr) {
                Collect(produced, output);
                if (m_providesSamples) {
                    produced->Release();
                }
            }
        }
        return BSAD_OK;
    }

    void Collect(IMFSample* sample, OutputQueue& output) {
        ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(sample->ConvertToContiguousBuffer(&buffer))) {
            return;
        }
        BYTE* bytes = nullptr;
        DWORD length = 0;
        if (FAILED(buffer->Lock(&bytes, nullptr, &length))) {
            return;
        }
        const size_t frameBytes = static_cast<size_t>(m_format.channels) * (m_float ? 4 : 2);
        if (frameBytes > 0 && m_format.rate > 0) {
            output.Append(m_format, bytes, length / frameBytes, !m_float);
        }
        buffer->Unlock();
    }

    ComPtr<IMFActivate> m_activate;
    ComPtr<IMFTransform> m_transform;
    ComPtr<IMFSample> m_output;
    ComPtr<IMFMediaBuffer> m_outputBuffer;
    std::string m_name;
    Format m_format;
    LONGLONG m_time = 0;
    int m_codec = 0;
    int m_nominalRate = 48000;
    int m_rejectedInARow = 0;
    bool m_started = false;
    bool m_float = false;
    bool m_providesSamples = false;
    bool m_discontinuity = false;
    bool m_passThroughOnly = false;
};

}  // namespace

bool PlatformHasDecoder(int codec) {
    if (codec != BSAD_CODEC_AAC && codec != BSAD_CODEC_EAC3) {
        return false;
    }
    static std::mutex lock;
    static int known[3] = {-1, -1, -1};
    std::lock_guard<std::mutex> guard(lock);
    if (known[codec] < 0) {
        // A decoder that is listed may still only pass the stream through: open one to find out.
        bool found = false;
        if (Mf() != nullptr) {
            OpenRequest request;
            request.codec = codec;
            request.rate = 48000;
            request.channels = 2;
            if (codec == BSAD_CODEC_AAC) {
                request.setup = {0x11, 0x90};  // AAC-LC, 48 kHz, stereo
            }
            std::string error;
            RunInMta([&] {
                MfDecoder probe;
                found = probe.Open(request, error) == BSAD_OK;
            });
        }
        known[codec] = found ? 1 : 0;
    }
    return known[codec] == 1;
}

int PlatformOpen(const OpenRequest& request, std::unique_ptr<Backend>& backend, std::string& error) {
    auto decoder = std::make_unique<MfDecoder>();
    int result = BSAD_E_FAILED;
    RunInMta([&] { result = decoder->Open(request, error); });
    if (result != BSAD_OK) {
        // Released in the MTA as well: the transform belongs there.
        RunInMta([&] { decoder.reset(); });
        return result;
    }
    backend = std::move(decoder);
    return BSAD_OK;
}

}  // namespace bsad
