// bsvdtool on Android: clips and the feeder. See android_clip.h.
#include "android_clip.h"

#include "bsvideodecode.h"
#include "core/core.h"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace tool {

int Clip::DisplayOf(int64_t pts) const {
    long long index = std::llround((double)pts * kFps / 1e6);
    if (frames <= 0) return (int)index;
    long long m = index % frames;
    return (int)(m < 0 ? m + frames : m);
}

size_t Clip::KeyUnitFor(int display) const {
    size_t best = 0;
    int bestDisplay = -1;
    for (size_t i = 0; i < units.size(); ++i)
        if (units[i].key && units[i].display <= display && units[i].display > bestDisplay) {
            best = i;
            bestDisplay = units[i].display;
        }
    return best;
}

std::vector<uint8_t> Clip::ParameterSets() const {
    for (const ClipUnit& u : units) {
        if (!u.key) continue;
        bsvd::ParameterSets sets;
        bsvd::FindParameterSets(hevc ? BSVD_CODEC_HEVC : BSVD_CODEC_H264, data.data() + u.offset, u.size, sets);
        std::vector<uint8_t> out = sets.vps;
        out.insert(out.end(), sets.sps.begin(), sets.sps.end());
        out.insert(out.end(), sets.pps.begin(), sets.pps.end());
        return out;
    }
    return {};
}

bool LoadClip(const std::string& path, Clip& clip) {
    clip = Clip();
    clip.path = path;
    size_t slash = path.find_last_of('/');
    clip.name = slash == std::string::npos ? path : path.substr(slash + 1);
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return std::printf("cannot open %s\n", path.c_str()), false;
    std::fseek(f, 0, SEEK_END);
    long n = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    clip.data.resize((size_t)std::max(0L, n));
    size_t got = std::fread(clip.data.data(), 1, clip.data.size(), f);
    std::fclose(f);
    if (got != clip.data.size()) return std::printf("short read on %s\n", path.c_str()), false;
    clip.hevc = clip.name.size() > 5 && clip.name.compare(clip.name.size() - 5, 5, ".hevc") == 0;

    // Units start at access unit delimiters.
    const auto& d = clip.data;
    std::vector<size_t> starts;
    for (size_t i = 0; i + 4 < d.size(); ++i) {
        if (d[i] || d[i + 1]) continue;
        size_t p;
        if (d[i + 2] == 1) p = i + 3;
        else if (d[i + 2] == 0 && d[i + 3] == 1) p = i + 4;
        else continue;
        bool aud = clip.hevc ? ((d[p] >> 1) & 0x3F) == 35 : (d[p] & 0x1F) == 9;
        if (aud) {
            starts.push_back(i);
            i = p;
        }
    }
    for (size_t i = 0; i < starts.size(); ++i) {
        ClipUnit u;
        u.offset = starts[i];
        u.size = (i + 1 < starts.size() ? starts[i + 1] : d.size()) - starts[i];
        clip.units.push_back(u);
    }
    if (clip.units.empty()) return std::printf("no access units in %s\n", path.c_str()), false;

    FILE* pkt = std::fopen((path + ".pkt").c_str(), "r");
    if (pkt) {
        std::vector<std::pair<int, bool>> lines;
        int pts, dts, size;
        char kind[8];
        while (std::fscanf(pkt, "%d %d %d %7s", &pts, &dts, &size, kind) == 4) lines.push_back({pts, kind[0] == 'K'});
        std::fclose(pkt);
        if (lines.size() != clip.units.size())
            return std::printf("%s: %zu units but %zu packet lines\n", path.c_str(), clip.units.size(), lines.size()), false;
        for (size_t i = 0; i < lines.size(); ++i) {
            clip.units[i].display = lines[i].first;
            clip.units[i].key = lines[i].second;
        }
        clip.fromPkt = true;
    } else {
        for (size_t i = 0; i < clip.units.size(); ++i) {
            clip.units[i].display = (int)i;
            clip.units[i].key = false;
            bsvd::ForEachNal(d.data() + clip.units[i].offset, clip.units[i].size, [&](const uint8_t* nal, size_t) {
                int type = clip.hevc ? bsvd::HevcType(nal) : bsvd::H264Type(nal);
                if (clip.hevc ? (type >= 16 && type <= 21) : type == 5) clip.units[i].key = true;
            });
        }
    }
    clip.frames = 0;
    for (const ClipUnit& u : clip.units) clip.frames = std::max(clip.frames, u.display + 1);

    if (!clip.hevc) {
        for (const ClipUnit& u : clip.units) {
            bsvd::ParameterSets sets;
            bsvd::FindParameterSets(BSVD_CODEC_H264, d.data() + u.offset, u.size, sets);
            if (sets.sps.size() <= 4) continue;
            bsvd::Colour c;
            int w = 0, h = 0;
            if (bsvd::ParseH264Sps(sets.sps.data() + 4, sets.sps.size() - 4, c, w, h) && w > 0 && h > 0) {
                clip.width = w;
                clip.height = h;
            }
            break;
        }
    }
    return true;
}

void Feeder::Start(int32_t handle, const Clip* clip, size_t fromUnit, int loops, bool endOfStream, size_t maxUnits) {
    Stop();
    stop_ = false;
    done_ = false;
    fed_ = 0;
    error_ = 0;
    thread_ = std::thread([=] {
        size_t count = 0;
        for (int loop = 0; loop < loops && !stop_; ++loop) {
            for (size_t i = loop == 0 ? fromUnit : 0; i < clip->units.size() && !stop_ && count < maxUnits; ++i, ++count) {
                const ClipUnit& u = clip->units[i];
                for (;;) {
                    int32_t r = BSVD_Feed(handle, clip->data.data() + u.offset, (int32_t)u.size, clip->PtsOf(u.display, loop),
                                          u.key ? BSVD_UNIT_KEYFRAME : 0);
                    if (r == BSVD_OK) break;
                    if (r != BSVD_AGAIN) {
                        error_ = r;
                        done_ = true;
                        return;
                    }
                    if (stop_) break;
                    std::this_thread::sleep_for(std::chrono::milliseconds(2));
                }
                fed_++;
            }
        }
        if (!stop_ && endOfStream) BSVD_EndOfStream(handle);
        done_ = true;
    });
}

void Feeder::Stop() {
    stop_ = true;
    if (thread_.joinable()) thread_.join();
    done_ = true;
}

}  // namespace tool
