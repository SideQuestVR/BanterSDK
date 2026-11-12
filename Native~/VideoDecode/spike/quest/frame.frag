#version 450
// The sampler carries the YCbCr conversion, so this reads RGB straight from the decoder's buffer. The target is the
// crop size, so each target pixel lands on one source texel centre: scale is 1/buffer size, offset the crop origin.
layout(set = 0, binding = 0) uniform sampler2D frame;
layout(push_constant) uniform Crop { vec2 scale; vec2 offset; } crop;
layout(location = 0) out vec4 color;
void main() {
    color = vec4(texture(frame, gl_FragCoord.xy * crop.scale + crop.offset).rgb, 1.0);
}
