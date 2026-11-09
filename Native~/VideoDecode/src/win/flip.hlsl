// The flip pass for video processors that can't mirror: copies the converted picture into a ring slot upside down, pixel
// for pixel (Unity stores texture rows bottom first). Compiled at build time by fxc into flip_vs.h / flip_ps.h.

float4 FlipVs(uint id : SV_VertexID) : SV_Position {
    // One triangle covering the target.
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

Texture2D<float4> source : register(t0);

float4 FlipPs(float4 position : SV_Position) : SV_Target {
    uint width, height;
    source.GetDimensions(width, height);
    return source.Load(int3((int)position.x, (int)height - 1 - (int)position.y, 0));
}
