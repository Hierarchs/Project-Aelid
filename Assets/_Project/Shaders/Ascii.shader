// Hybrid ASCII conversion, three full-screen passes driven by AsciiRendererFeature:
//   0 Reduce    (cell res, MRT)  average color + class, cell depth + normal
//   1 Select    (cell res)       glyph per cell: density ramp, dithering, edge lines
//   2 Composite (full res)       draw glyphs, keep Exempt pixels 3D, outline, sub-cell shift
Shader "Hidden/Aelid/Ascii"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "AsciiCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "AsciiReduce"

            HLSLPROGRAM
            #pragma vertex AsciiVert
            #pragma fragment FragReduce

            TEXTURE2D(_AsciiSceneColor);
            TEXTURE2D_FLOAT(_AsciiDepth);
            TEXTURE2D(_AsciiNormals);
            TEXTURE2D(_AsciiSurfaceId);
            float _AsciiHasNormals;

            float AsciiLinearDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            bool AsciiIsSky(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 1e-7;
            #else
                return raw >= 1.0 - 1e-7;
            #endif
            }

            void FragReduce(AsciiVaryings input, out float4 outColor : SV_Target0, out float4 outGeometry : SV_Target1)
            {
                int2 cell = int2(input.positionCS.xy);
                int cs = (int)_AsciiCell.x;
                int2 size = int2(_AsciiSource.xy);
                int2 origin = cell * cs;

                // Box-average every pixel of the cell (one full-res read in total).
                float3 sum = 0.0;
                float count = 0.0;
                [loop] for (int y = 0; y < cs; y++)
                {
                    [loop] for (int x = 0; x < cs; x++)
                    {
                        int2 p = min(origin + int2(x, y), size - 1);
                        sum += min(LOAD_TEXTURE2D(_AsciiSceneColor, p).rgb, 64.0);
                        count += 1.0;
                    }
                }
                float3 average = sum / max(count, 1.0);

                int2 center = min(origin + cs / 2, size - 1);
                float raw = LOAD_TEXTURE2D(_AsciiDepth, center).r;
                float surfaceClass = round(LOAD_TEXTURE2D(_AsciiSurfaceId, center).r * 255.0);
                float3 normal = _AsciiHasNormals > 0.5 ? LOAD_TEXTURE2D(_AsciiNormals, center).xyz : float3(0, 1, 0);

                float depth;
                if (AsciiIsSky(raw))
                {
                    surfaceClass = ASCII_CLASS_SKY;
                    depth = _ProjectionParams.z;
                }
                else
                {
                    depth = AsciiLinearDepth(raw);
                }

                outColor = float4(average, surfaceClass);
                outGeometry = float4(depth, SafeNormalize(normal));
            }
            ENDHLSL
        }

        Pass
        {
            Name "AsciiSelect"

            HLSLPROGRAM
            #pragma vertex AsciiVert
            #pragma fragment FragSelect

            TEXTURE2D_FLOAT(_AsciiCellColor);
            TEXTURE2D_FLOAT(_AsciiCellGeometry);

            static const int2 kNeighbours[8] =
            {
                int2(-1, -1), int2(0, -1), int2(1, -1),
                int2(-1,  0),              int2(1,  0),
                int2(-1,  1), int2(0,  1), int2(1,  1)
            };

            // Returns an edge column (10..13) or -1.
            int DetectEdge(int2 cell, int2 cells, float4 centerGeometry)
            {
                float dc = centerGeometry.x;
                float3 nc = centerGeometry.yzw;
                float sxx = 0.0, syy = 0.0, sxy = 0.0, strength = 0.0;

                [unroll] for (int k = 0; k < 8; k++)
                {
                    int2 o = kNeighbours[k];
                    int2 nb = clamp(cell + o, int2(0, 0), cells - 1);

                    // Exempt surfaces (characters) get a pixel outline in the
                    // composite instead; line glyphs around them read as noise.
                    if ((int)(LOAD_TEXTURE2D(_AsciiCellColor, nb).a + 0.5) == ASCII_CLASS_EXEMPT)
                        continue;

                    float4 gn = LOAD_TEXTURE2D(_AsciiCellGeometry, nb);

                    // Silhouettes: only the nearer (occluding) side draws the line,
                    // which keeps outlines one cell thick.
                    float relative = (gn.x - dc) / max(dc, 1e-3);
                    float depthDiff = saturate(relative * _AsciiEdge.y);

                    // Creases: compare against one half of the neighbourhood only,
                    // so each crease is also drawn once.
                    bool forwardHalf = (o.y > 0) || (o.y == 0 && o.x > 0);
                    float normalDiff = forwardHalf ? (1.0 - saturate(dot(gn.yzw, nc))) * _AsciiEdge.z : 0.0;

                    float d = max(depthDiff, normalDiff);
                    float w = (o.x != 0 && o.y != 0) ? 0.5 : 1.0;
                    float2 of = float2(o);
                    sxx += d * w * of.x * of.x;
                    syy += d * w * of.y * of.y;
                    sxy += d * w * of.x * of.y;
                    strength = max(strength, d);
                }

                if (strength <= _AsciiEdge.x)
                    return -1;

                // Dominant direction of change (structure tensor), then the line
                // runs perpendicular to it.
                float theta = 0.5 * atan2(2.0 * sxy, sxx - syy);
                if (AsciiRowsGoDown())
                    theta = -theta; // work in screen-up space
                float lineAngle = theta + HALF_PI;
                lineAngle -= PI * floor(lineAngle / PI);
                float deg = degrees(lineAngle);

                if (deg < 22.5 || deg >= 157.5) return ASCII_COL_EDGE_H;
                if (deg < 67.5) return ASCII_COL_EDGE_SLASH;
                if (deg < 112.5) return ASCII_COL_EDGE_V;
                return ASCII_COL_EDGE_BACK;
            }

            float4 FragSelect(AsciiVaryings input) : SV_Target
            {
                int2 cell = int2(input.positionCS.xy);
                int2 cells = int2(_AsciiCell.zw);

                float4 c0 = LOAD_TEXTURE2D(_AsciiCellColor, cell);
                float4 g0 = LOAD_TEXTURE2D(_AsciiCellGeometry, cell);
                int surfaceClass = clamp((int)(c0.a + 0.5), 0, 15);
                float4 classParams = _AsciiClassParams[surfaceClass];

                // --- Density from luminance (exposure only affects glyph choice) ---
                float lumHdr = AsciiLuminance(c0.rgb * _AsciiTone.x);
                float lum = lumHdr / (1.0 + lumHdr);
                lum = saturate((lum - 0.5) * _AsciiTone.y + 0.5);
                lum = pow(max(lum, 1e-5), _AsciiTone.z);

                int2 worldCell = AsciiWorldCell(cell);
                float noise = AsciiHash(worldCell) - 0.5;
                lum += noise * _AsciiTone.w * classParams.w / ASCII_LEVELS;

                if (surfaceClass == ASCII_CLASS_WATER)
                {
                    float t = _AsciiMisc.x * _AsciiMisc.y;
                    lum += 0.07 * sin(t + worldCell.x * 0.55 + worldCell.y * 1.3)
                         + 0.04 * sin(t * 1.7 - worldCell.x * 0.31 + worldCell.y * 0.77);
                }

                int glyph = clamp((int)(saturate(lum) * ASCII_LEVELS), 0, ASCII_LEVELS - 1);

                // --- Line glyphs on silhouettes and creases ---
                if (_AsciiEdge.w > 0.5 && classParams.z > 0.5 && surfaceClass != ASCII_CLASS_SKY)
                {
                    int edge = DetectEdge(cell, cells, g0);
                    if (edge >= 0)
                        glyph = edge;
                }

                // --- Glyph color ---
                float3 color = max(c0.rgb, 0.0);
                float l = max(AsciiLuminance(color), 1e-4);
                color = max(lerp(l.xxx, color, _AsciiColor.z), 0.0);
                float normalizeBoost = lerp(1.0, clamp(0.35 / l, 0.25, 6.0), _AsciiColor.w);
                float3 foreground = color * _AsciiColor.x * normalizeBoost;

                int row = (int)classParams.y;
                return float4(foreground, row * 16 + glyph);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AsciiComposite"

            HLSLPROGRAM
            #pragma vertex AsciiVert
            #pragma fragment FragComposite

            TEXTURE2D(_AsciiSceneColor);
            TEXTURE2D(_AsciiSurfaceId);
            TEXTURE2D_FLOAT(_AsciiCellColor);
            TEXTURE2D_FLOAT(_AsciiCellGlyph);
            TEXTURE2D(_AsciiGlyphAtlas);

            int LoadClass(int2 p, int2 size)
            {
                p = clamp(p, int2(0, 0), size - 1);
                return (int)(LOAD_TEXTURE2D(_AsciiSurfaceId, p).r * 255.0 + 0.5);
            }

            float4 FragComposite(AsciiVaryings input) : SV_Target
            {
                int2 p = int2(input.positionCS.xy);
                int2 size = int2(_AsciiSource.xy);
                int cs = (int)_AsciiCell.x;

                // Shift by the camera's sub-cell remainder so the view scrolls
                // smoothly while glyphs stay locked to the world.
                int2 offset = AsciiRowsGoDown() ? int2(_AsciiOffset.x, -_AsciiOffset.y) : int2(_AsciiOffset.xy);
                int2 q = p + offset;
                int2 cellRaw = int2(floor(float2(q) / cs));
                int2 local = q - cellRaw * cs;
                int2 cell = clamp(cellRaw, int2(0, 0), int2(_AsciiCell.zw) - 1);
                int2 qc = clamp(q, int2(0, 0), size - 1);

                float4 glyphData = LOAD_TEXTURE2D(_AsciiCellGlyph, cell);
                float3 cellAverage = LOAD_TEXTURE2D(_AsciiCellColor, cell).rgb;
                int code = (int)(glyphData.a + 0.5);
                if ((int)_AsciiDebug.x == 5)
                {
                    // Row index counted from the top so the atlas reads like the source table.
                    int screenRow = AsciiRowsGoDown() ? cell.y : int(_AsciiCell.w) - 1 - cell.y;
                    code = (screenRow % 16) * 16 + (cell.x % 16);
                    glyphData.rgb = 1.0;
                }
                int row = code >> 4;
                int col = code & 15;

                int ly = AsciiRowsGoDown() ? local.y : cs - 1 - local.y; // glyph row, counted from the top
                int gx = (local.x * ASCII_GLYPH) / cs;
                int gy = (ly * ASCII_GLYPH) / cs;
                int2 atlasTexel = int2(col * ASCII_GLYPH + gx, ASCII_ATLAS_SIZE - 1 - (row * ASCII_GLYPH + gy));
                float ink = LOAD_TEXTURE2D(_AsciiGlyphAtlas, atlasTexel).r;

                float3 background = cellAverage * _AsciiColor.y;
                float3 ascii = lerp(background, glyphData.rgb, ink);

                float3 scene = LOAD_TEXTURE2D(_AsciiSceneColor, qc).rgb;
                int pixelClass = LoadClass(qc, size);
                float amount = saturate(_AsciiClassParams[pixelClass].x) * _AsciiMisc.w;
                float3 result = lerp(scene, ascii, amount);

                // Dark rim around Exempt silhouettes (characters) for readability.
                if (_AsciiMisc.z > 0.0 && pixelClass != ASCII_CLASS_EXEMPT)
                {
                    int r = max(1, cs / 8);
                    bool rim = LoadClass(qc + int2(r, 0), size) == ASCII_CLASS_EXEMPT
                            || LoadClass(qc - int2(r, 0), size) == ASCII_CLASS_EXEMPT
                            || LoadClass(qc + int2(0, r), size) == ASCII_CLASS_EXEMPT
                            || LoadClass(qc - int2(0, r), size) == ASCII_CLASS_EXEMPT;
                    if (rim)
                        result = lerp(result, _AsciiOutlineColor.rgb, _AsciiMisc.z * _AsciiMisc.w);
                }

                int debugView = (int)_AsciiDebug.x;
                if (debugView == 1) result = scene;
                else if (debugView == 2) result = cellAverage;
                else if (debugView == 3)
                {
                    static const float3 kClassColors[8] =
                    {
                        float3(0.5, 0.5, 0.5), float3(1, 0, 1), float3(0, 0.6, 0), float3(0, 0.3, 1),
                        float3(0.6, 0.6, 0.7), float3(1, 0.6, 0), float3(0.6, 1, 0.2), float3(0.6, 0.35, 0.1)
                    };
                    result = kClassColors[pixelClass & 7] * (0.4 + 0.6 * saturate(AsciiLuminance(scene) * 2.0));
                }
                else if (debugView == 4) result = glyphData.rgb;
                else if (debugView == 5) result = lerp(float3(0.05, 0.05, 0.1), float3(1, 1, 0.8), ink);

                return float4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
