#version 450
// The sampler carries the YCbCr conversion (matrix and range set from the stream), so this reads RGB straight from the
// decoder's buffer. Target pixel centres map onto the cropped picture, flipped vertically: the target's first row in
// memory is the picture's bottom row, the order Unity stores texture rows in. Coordinates are clamped to the centres of
// the crop's edge texels so filtering never reaches the buffer's padding.
layout(set = 0, binding = 0) uniform sampler2D frame;
layout(push_constant) uniform Map {
    vec2 scale;   // per target pixel, in buffer UV (y negative: the flip)
    vec2 offset;  // buffer UV of the target's origin
    vec2 lo;      // clamp, buffer UV
    vec2 hi;
} map;
layout(location = 0) out vec4 color;
void main() {
    vec2 uv = clamp(gl_FragCoord.xy * map.scale + map.offset, map.lo, map.hi);
    color = vec4(texture(frame, uv).rgb, 1.0);
}
