// Colour: which matrix and range to convert with, the H.264 SPS fields that say so, parameter sets, and the CPU path's
// YUV to RGBA conversion.
#include "core/core.h"

#include <algorithm>
#include <cmath>

namespace bsvd {

int MatrixFromCode(int code) {
    switch (code) {
        case 1: return 709;   // BT.709
        case 5:               // BT.470 BG (PAL)
        case 6: return 601;   // SMPTE 170M (NTSC)
        case 9:               // BT.2020 non-constant
        case 10: return 2020; // BT.2020 constant (converted as non-constant: close enough for a fallback)
        default: return 0;
    }
}

Colour ResolveColour(Colour fromBitstream, Colour fromDecoder, Colour fromContainer, int height) {
    // The container's colr box outranks the decoder's report: a decoder fills in its own default when the stream says
    // nothing (Quest's reports 709 limited for HEVC with no colour description), which would hide an explicit colr.
    Colour c;
    c.matrix = fromBitstream.matrix ? fromBitstream.matrix
             : fromContainer.matrix ? fromContainer.matrix
             : fromDecoder.matrix ? fromDecoder.matrix
             : (height == 0 || height >= 720 ? 709 : 601);
    c.fullRange = fromBitstream.fullRange >= 0 ? fromBitstream.fullRange
                : fromContainer.fullRange >= 0 ? fromContainer.fullRange
                : fromDecoder.fullRange >= 0 ? fromDecoder.fullRange
                : 0;
    return c;
}

namespace {

// Reads RBSP bits: emulation prevention bytes (00 00 03) are removed first. Reading past the end yields zeros and sets
// the overrun flag, so a damaged SPS can never read out of bounds.
class Bits {
public:
    Bits(const uint8_t* nal, size_t size) {
        size = std::min<size_t>(size, 4096);
        rbsp_.reserve(size);
        int zeros = 0;
        for (size_t i = 0; i < size; ++i) {
            uint8_t b = nal[i];
            if (zeros >= 2 && b == 3) {
                zeros = 0;
                continue;
            }
            zeros = b == 0 ? zeros + 1 : 0;
            rbsp_.push_back(b);
        }
    }

    bool Overrun() const { return overrun_; }

    uint32_t Bit() {
        if (pos_ >= rbsp_.size() * 8) {
            overrun_ = true;
            return 0;
        }
        uint32_t bit = (rbsp_[pos_ >> 3] >> (7 - (pos_ & 7))) & 1;
        pos_++;
        return bit;
    }

    uint32_t Read(int n) {
        uint32_t v = 0;
        for (int i = 0; i < n; ++i) v = (v << 1) | Bit();
        return v;
    }

    uint32_t Ue() {
        int zeros = 0;
        while (Bit() == 0) {
            if (++zeros > 31 || overrun_) {
                overrun_ = true;
                return 0;
            }
        }
        if (zeros == 0) return 0;
        uint64_t value = ((uint64_t)1 << zeros) - 1 + Read(zeros);
        return value > 0xFFFFFFFFull ? 0xFFFFFFFFu : (uint32_t)value;
    }

    int32_t Se() {
        uint32_t k = Ue();
        int64_t v = (k & 1) ? (int64_t)((k + 1ull) / 2) : -(int64_t)(k / 2);
        return (int32_t)std::max<int64_t>(INT32_MIN, std::min<int64_t>(INT32_MAX, v));
    }

private:
    std::vector<uint8_t> rbsp_;
    size_t pos_ = 0;
    bool overrun_ = false;
};

void SkipScalingList(Bits& b, int size) {
    int last = 8, next = 8;
    for (int j = 0; j < size; ++j) {
        if (next != 0) {
            int delta = b.Se();
            next = ((last + delta) % 256 + 256) % 256;
        }
        last = next == 0 ? last : next;
    }
}

}  // namespace

bool ParseH264Sps(const uint8_t* nal, size_t size, Colour& colour, int& width, int& height) {
    if (!nal || size < 4 || H264Type(nal) != 7) return false;
    Bits b(nal + 1, size - 1);
    uint32_t profile = b.Read(8);
    b.Read(16);  // constraint flags, level
    b.Ue();      // seq_parameter_set_id
    uint32_t chroma = 1;
    bool separatePlanes = false;
    if (profile == 100 || profile == 110 || profile == 122 || profile == 244 || profile == 44 || profile == 83 || profile == 86 ||
        profile == 118 || profile == 128 || profile == 138 || profile == 139 || profile == 134 || profile == 135) {
        chroma = b.Ue();
        if (chroma > 3) return false;
        if (chroma == 3) separatePlanes = b.Bit() != 0;
        b.Ue();   // bit_depth_luma_minus8
        b.Ue();   // bit_depth_chroma_minus8
        b.Bit();  // qpprime_y_zero_transform_bypass_flag
        if (b.Bit()) {  // seq_scaling_matrix_present_flag
            int lists = chroma != 3 ? 8 : 12;
            for (int i = 0; i < lists; ++i)
                if (b.Bit()) SkipScalingList(b, i < 6 ? 16 : 64);
        }
    }
    b.Ue();  // log2_max_frame_num_minus4
    uint32_t pocType = b.Ue();
    if (pocType == 0) {
        b.Ue();
    } else if (pocType == 1) {
        b.Bit();
        b.Se();
        b.Se();
        uint32_t cycle = b.Ue();
        if (cycle > 255) return false;
        for (uint32_t i = 0; i < cycle; ++i) b.Se();
    } else if (pocType != 2) {
        return false;
    }
    b.Ue();   // max_num_ref_frames
    b.Bit();  // gaps_in_frame_num_value_allowed_flag
    uint32_t widthMbs = b.Ue() + 1;
    uint32_t heightUnits = b.Ue() + 1;
    uint32_t frameMbsOnly = b.Bit();
    if (!frameMbsOnly) b.Bit();
    b.Bit();  // direct_8x8_inference_flag
    uint32_t cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
    if (b.Bit()) {
        cropLeft = b.Ue();
        cropRight = b.Ue();
        cropTop = b.Ue();
        cropBottom = b.Ue();
    }
    if (b.Overrun() || widthMbs > 1024 || heightUnits > 1024) return false;
    uint32_t arrayType = separatePlanes ? 0 : chroma;
    uint32_t subWidth = arrayType == 1 || arrayType == 2 ? 2 : 1;
    uint32_t subHeight = arrayType == 1 ? 2 : 1;
    uint32_t unitX = arrayType == 0 ? 1 : subWidth;
    uint32_t unitY = (arrayType == 0 ? 1 : subHeight) * (2 - frameMbsOnly);
    int64_t w = (int64_t)widthMbs * 16 - (int64_t)unitX * (cropLeft + cropRight);
    int64_t h = (int64_t)heightUnits * 16 * (2 - frameMbsOnly) - (int64_t)unitY * (cropTop + cropBottom);
    if (w > 0 && h > 0) {
        width = (int)w;
        height = (int)h;
    }
    if (!b.Bit()) return !b.Overrun();  // vui_parameters_present_flag
    if (b.Bit()) {                      // aspect_ratio_info_present_flag
        if (b.Read(8) == 255) b.Read(32);
    }
    if (b.Bit()) b.Bit();  // overscan
    if (b.Bit()) {         // video_signal_type_present_flag
        b.Read(3);         // video_format
        uint32_t full = b.Bit();
        uint32_t matrix = 0;
        if (b.Bit()) {     // colour_description_present_flag
            b.Read(8);     // colour_primaries
            b.Read(8);     // transfer_characteristics
            matrix = b.Read(8);
        }
        if (b.Overrun()) return false;
        colour.fullRange = (int)full;
        if (MatrixFromCode((int)matrix)) colour.matrix = MatrixFromCode((int)matrix);
    }
    return !b.Overrun();
}

void FindParameterSets(int codec, const uint8_t* data, size_t size, ParameterSets& out) {
    static const uint8_t kStart[4] = {0, 0, 0, 1};
    ForEachNal(data, size, [&](const uint8_t* nal, size_t n) {
        std::vector<uint8_t>* to = nullptr;
        if (codec == BSVD_CODEC_HEVC) {
            int type = HevcType(nal);
            if (type == 32) to = &out.vps;
            else if (type == 33) to = &out.sps;
            else if (type == 34) to = &out.pps;
        } else {
            int type = H264Type(nal);
            if (type == 7) to = &out.sps;
            else if (type == 8) to = &out.pps;
        }
        if (!to) return;
        to->insert(to->end(), kStart, kStart + 4);
        to->insert(to->end(), nal, nal + n);
    });
}

void ConvertToRgba(const YuvPicture& in, Colour colour, uint8_t* rgba, int width, int height) {
    if (!rgba || width <= 0 || height <= 0) return;
    if (!in.y || !in.u || !in.v || in.width <= 0 || in.height <= 0) {
        std::fill(rgba, rgba + (size_t)width * (size_t)height * 4, (uint8_t)0);
        return;
    }
    double kr, kg1, kg2, kb;  // R = Y + kr*V; G = Y - kg1*U - kg2*V; B = Y + kb*U
    switch (colour.matrix) {
        case 601: kr = 1.402; kg1 = 0.344136; kg2 = 0.714136; kb = 1.772; break;
        case 2020: kr = 1.4746; kg1 = 0.164553; kg2 = 0.571353; kb = 1.8814; break;
        default: kr = 1.5748; kg1 = 0.187324; kg2 = 0.468124; kb = 1.8556; break;
    }
    bool full = colour.fullRange == 1;
    double ys = full ? 1.0 : 255.0 / 219.0, cs = full ? 1.0 : 255.0 / 224.0;
    int yOffset = full ? 0 : 16;
    const int one = 1 << 16;
    int Y = (int)std::lround(ys * one), R = (int)std::lround(kr * cs * one), G1 = (int)std::lround(kg1 * cs * one),
        G2 = (int)std::lround(kg2 * cs * one), B = (int)std::lround(kb * cs * one);

    thread_local std::vector<int> columns;
    columns.resize((size_t)width);
    for (int x = 0; x < width; ++x) columns[(size_t)x] = (int)(((int64_t)2 * x + 1) * in.width / (2 * (int64_t)width));
    auto clamp = [](int v) { return (uint8_t)(v < 0 ? 0 : v > 255 ? 255 : v); };

    for (int row = 0; row < height; ++row) {
        int sy = (int)(((int64_t)2 * row + 1) * in.height / (2 * (int64_t)height));
        const uint8_t* yRow = in.y + (size_t)sy * (size_t)in.yStride;
        const uint8_t* uRow = in.u + (size_t)(sy / 2) * (size_t)in.uvStride;
        const uint8_t* vRow = in.v + (size_t)(sy / 2) * (size_t)in.uvStride;
        uint8_t* out = rgba + (size_t)(height - 1 - row) * (size_t)width * 4;  // bottom row first
        for (int x = 0; x < width; ++x) {
            int sx = columns[(size_t)x];
            size_t c = (size_t)(sx / 2) * (size_t)in.uvPixelStride;
            int y = (yRow[sx] - yOffset) * Y + one / 2;
            int u = uRow[c] - 128, v = vRow[c] - 128;
            out[0] = clamp((y + R * v) >> 16);
            out[1] = clamp((y - G1 * u - G2 * v) >> 16);
            out[2] = clamp((y + B * u) >> 16);
            out[3] = 255;
            out += 4;
        }
    }
}

}  // namespace bsvd
