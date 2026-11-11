#ifndef AELID_ASCII_COMMON_INCLUDED
#define AELID_ASCII_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Must match AsciiGlyphAtlas.cs / AsciiSurfaceClass.cs
#define ASCII_LEVELS          10
#define ASCII_GLYPH           8
#define ASCII_ATLAS_SIZE      128
#define ASCII_COL_EDGE_V      10
#define ASCII_COL_EDGE_H      11
#define ASCII_COL_EDGE_SLASH  12
#define ASCII_COL_EDGE_BACK   13
#define ASCII_COL_SPARKLE     14
#define ASCII_CLASS_EXEMPT    1
#define ASCII_CLASS_WATER     3
#define ASCII_CLASS_SKY       15

// All passes use integer pixel addressing (Load), so they never depend on UV
// conventions or RT padding. Whether pixel row 0 is the top or the bottom of
// the screen depends on how URP oriented the camera texture (it renders
// intermediate textures flipped on D3D), so C# queries RenderGraph for the
// texture's UV origin and passes it in _AsciiOrientation.x.

float4 _AsciiCell;        // x: cell size px, z: cell count x, w: cell count y
float4 _AsciiSource;      // x: width, y: height, zw: 1/size
float4 _AsciiOffset;      // xy: sub-cell offset px (screen right/up), zw: world cell origin
float4 _AsciiTone;        // x: exposure, y: contrast, z: gamma, w: dither
float4 _AsciiColor;       // x: glyph brightness, y: background fill, z: saturation, w: color normalize
float4 _AsciiEdge;        // x: threshold, y: depth sensitivity, z: normal sensitivity, w: enabled
float4 _AsciiMisc;        // x: time, y: water ripple speed, z: exempt outline, w: intensity
float4 _AsciiOutlineColor;
float4 _AsciiDebug;       // x: debug view (AsciiDebugView)
float4 _AsciiOrientation; // x: 1 when pixel row 0 is the top of the screen, 0 when it is the bottom

bool AsciiRowsGoDown()
{
    return _AsciiOrientation.x > 0.5;
}
float4 _AsciiClassParams[16]; // per class: x ascii amount, y atlas row, z edges on, w dither scale
float4 _AsciiClassParams2[16];// per class: x density floor, y sparkle weight
float4 _AsciiSparkle;         // rgb: sparkle color (HDR), a: density
float4 _AsciiSparkle2;        // x: self-lit, y: twinkle, z: color jitter, w: density floor scale

struct AsciiAttributes
{
    uint vertexID : SV_VertexID;
};

struct AsciiVaryings
{
    float4 positionCS : SV_POSITION;
};

AsciiVaryings AsciiVert(AsciiAttributes input)
{
    AsciiVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    return output;
}

float AsciiLuminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// Integer hash -> [0,1). Stable for a given world cell.
float AsciiHash(int2 p)
{
    uint2 q = asuint(p) * uint2(1597334677u, 3812015801u);
    uint n = (q.x ^ q.y) * 1597334677u;
    return n * (1.0 / 4294967296.0);
}

// Second independent hash stream for the same cell.
float AsciiHash2(int2 p)
{
    return AsciiHash(p * int2(7919, 104729) + int2(31337, 271));
}

// Cell index anchored to the world (via the snapped camera's cell origin).
int2 AsciiWorldCell(int2 cell)
{
    int2 screenUp = AsciiRowsGoDown() ? int2(cell.x, -cell.y) : cell;
    return screenUp + int2(_AsciiOffset.zw);
}

#endif
