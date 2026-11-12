// Hybrid ASCII conversion, three full-screen passes driven by AsciiRendererFeature:
//   0 Reduce    (cell res, MRT)  average color + class, cell depth + normal, optional color split
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

            int ReadSurfaceId(int2 p)
            {
                return (int)round(LOAD_TEXTURE2D(_AsciiSurfaceId, p).r * 255.0);
            }

            int ReadSurfaceClass(int2 p, float depth)
            {
                return AsciiIsSky(depth) ? ASCII_CLASS_SKY : ReadSurfaceId(p);
            }

            struct ShapeSample
            {
                float3 color;
                float depth;
                int surface;
            };

            ShapeSample ReadShapeSample(int2 p)
            {
                ShapeSample s;
                float raw = LOAD_TEXTURE2D(_AsciiDepth, p).r;
                s.surface = ReadSurfaceClass(p, raw);
                s.depth = s.surface == ASCII_CLASS_SKY ? _ProjectionParams.z : AsciiLinearDepth(raw);
                float3 color = max(LOAD_TEXTURE2D(_AsciiSceneColor, p).rgb, 0.0);
                s.color = color / (1.0 + AsciiLuminance(color));
                return s;
            }

            float ColorDistance(float3 a, float3 b)
            {
                return dot(abs(a - b), float3(0.3333, 0.3333, 0.3333));
            }

            // a,b and c,d are the two candidate halves. Both sides must be coherent and the
            // contrast must agree across both pairs. Single leaves and diagonal specks fail this test.
            float SplitScore(ShapeSample a, ShapeSample b, ShapeSample c, ShapeSample d)
            {
                if (a.surface != b.surface || c.surface != d.surface)
                    return 0.0;

                float acrossDepth = min(AsciiRelativeDepth(a.depth, c.depth), AsciiRelativeDepth(b.depth, d.depth));
                if (a.surface == c.surface && (a.surface == ASCII_CLASS_FOLIAGE || acrossDepth < 0.025))
                    return 0.0;
                float withinDepth = max(AsciiRelativeDepth(a.depth, b.depth), AsciiRelativeDepth(c.depth, d.depth));
                if (withinDepth > max(0.006, 0.27 * acrossDepth))
                    return 0.0;

                float across = min(ColorDistance(a.color, c.color), ColorDistance(b.color, d.color));
                float within = 0.5 * (ColorDistance(a.color, b.color) + ColorDistance(c.color, d.color));
                float contrastFloor = max(0.012, 0.17 * AsciiLuminance(0.25 * (a.color + b.color + c.color + d.color)));
                return across > contrastFloor && within < 0.55 * across ? across - within : 0.0;
            }

            // Adapted from Asciicker's horizontal/vertical cell resolve (gumix / msokalski).
            // https://github.com/msokalski/asciicker/blob/master/render.cpp#L3314-L3353
            int FitSplit(int2 origin, int cs)
            {
                int low = (cs / 2 - 1) / 2;
                int high = (cs + cs / 2) / 2;
                ShapeSample a = ReadShapeSample(origin + int2(low, low));
                ShapeSample b = ReadShapeSample(origin + int2(high, low));
                ShapeSample c = ReadShapeSample(origin + int2(low, high));
                ShapeSample d = ReadShapeSample(origin + int2(high, high));
                if (!AsciiFitsSurface(a.surface) || !AsciiFitsSurface(b.surface)
                    || !AsciiFitsSurface(c.surface) || !AsciiFitsSurface(d.surface))
                    return 0;

                float x = SplitScore(a, c, b, d);
                float y = SplitScore(a, b, c, d);
                if (x > 1.05 * y) return ASCII_SPLIT_X;
                if (y > 1.05 * x) return ASCII_SPLIT_Y;
                return 0;
            }

            void FragReduce(AsciiVaryings input, out float4 outColor : SV_Target0,
                out float4 outGeometry : SV_Target1, out float4 outSplit : SV_Target2)
            {
                int2 cell = int2(input.positionCS.xy);
                int cs = (int)_AsciiCell.x;
                int2 size = int2(_AsciiSource.xy);
                int2 origin = cell * cs;
                int2 center = min(origin + cs / 2, size - 1);
                float raw = LOAD_TEXTURE2D(_AsciiDepth, center).r;
                int surfaceClass = ReadSurfaceClass(center, raw);
                float3 normal = _AsciiHasNormals > 0.5 ? LOAD_TEXTURE2D(_AsciiNormals, center).xyz : float3(0, 1, 0);
                float depth = surfaceClass == ASCII_CLASS_SKY ? _ProjectionParams.z : AsciiLinearDepth(raw);

                int split = 0;
                if (_AsciiDebug.y > 0.5 && all(origin + cs <= size) && AsciiFitsSurface(surfaceClass))
                    split = FitSplit(origin, cs);

                // Box-average every pixel of the cell (one full-res read in total).
                float3 sum = 0.0;
                float3 firstHalf = 0.0;
                float count = 0.0;
                [loop] for (int y = 0; y < cs; y++)
                {
                    [loop] for (int x = 0; x < cs; x++)
                    {
                        int2 p = min(origin + int2(x, y), size - 1);
                        float3 color = min(LOAD_TEXTURE2D(_AsciiSceneColor, p).rgb, 64.0);
                        sum += color;
                        if (split != 0)
                        {
                            // A thin blade, a waterline or a flame can fall between all four probes.
                            if (!AsciiFitsSurface(ReadSurfaceId(p)))
                                split = 0;
                            else if ((split == ASCII_SPLIT_X ? x : y) < cs / 2)
                                firstHalf += color;
                        }
                        count += 1.0;
                    }
                }
                outColor = float4(sum / max(count, 1.0), surfaceClass);
                outGeometry = float4(depth, SafeNormalize(normal));
                outSplit = float4(firstHalf / (cs * (cs / 2)), split);
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
            TEXTURE2D_FLOAT(_AsciiCellSplit);

            static const int2 kNeighbours[8] =
            {
                int2(-1, -1), int2(0, -1), int2(1, -1),
                int2(-1,  0),              int2(1,  0),
                int2(-1,  1), int2(0,  1), int2(1,  1)
            };

            // Returns an edge column (10..13) or -1.
            int DetectEdge(int2 cell, int2 cells, float4 centerGeometry, bool foliage)
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
                    int neighbourClass = (int)(LOAD_TEXTURE2D(_AsciiCellColor, nb).a + 0.5);
                    if (neighbourClass == ASCII_CLASS_EXEMPT)
                        continue;

                    float4 gn = LOAD_TEXTURE2D(_AsciiCellGeometry, nb);

                    // Silhouettes: only the nearer (occluding) side draws the line,
                    // which keeps outlines one cell thick.
                    float relative = (gn.x - dc) / max(dc, 1e-3);
                    float depthDiff = saturate(relative * _AsciiEdge.y);

                    if (foliage)
                    {
                        // Keep the outer canopy rim. A gap needs background beyond its first
                        // cell before it can turn a leaf glyph into a line.
                        int2 beyond = clamp(cell + o * 2, int2(0, 0), cells - 1);
                        int beyondClass = (int)(LOAD_TEXTURE2D(_AsciiCellColor, beyond).a + 0.5);
                        if (neighbourClass == ASCII_CLASS_FOLIAGE || beyondClass == ASCII_CLASS_FOLIAGE
                            || beyondClass == ASCII_CLASS_EXEMPT || all(beyond == nb))
                            continue;

                        float farther = (LOAD_TEXTURE2D(_AsciiCellGeometry, beyond).x - dc) / max(dc, 1e-3);
                        depthDiff = min(depthDiff, saturate(farther * _AsciiEdge.y));
                    }

                    // Creases: compare against one half of the neighbourhood only,
                    // so each crease is also drawn once.
                    bool forwardHalf = (o.y > 0) || (o.y == 0 && o.x > 0);
                    float normalDiff = forwardHalf && !foliage ? (1.0 - saturate(dot(gn.yzw, nc))) * _AsciiEdge.z : 0.0;

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

            // Continuous variation across patches of cells, sampled on the same
            // world grid as the glyphs so clumps do not slide with the camera.
            float PatchDensity(int2 worldCell)
            {
                float2 patch = float2(worldCell) / 11.0;
                int2 corner = int2(floor(patch));
                float2 t = smoothstep(0.0, 1.0, frac(patch));
                float lower = lerp(AsciiHash(corner), AsciiHash(corner + int2(1, 0)), t.x);
                float upper = lerp(AsciiHash(corner + int2(0, 1)), AsciiHash(corner + int2(1, 1)), t.x);
                return lerp(0.28, 1.17, smoothstep(0.16, 0.80, lerp(lower, upper, t.y)));
            }

            float3 GroupCellColor(int2 cell, int2 cells, float4 color, float4 geometry, int surfaceClass)
            {
                float3 sum = color.rgb;
                float weight = 1.0;
                // The axial neighbours avoid crossing a corner diagonally into a different shape.
                static const int2 axes[4] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };
                [unroll] for (int i = 0; i < 4; i++)
                {
                    int2 p = clamp(cell + axes[i], int2(0, 0), cells - 1);
                    float4 other = LOAD_TEXTURE2D(_AsciiCellColor, p);
                    if ((int)(other.a + 0.5) != surfaceClass || LOAD_TEXTURE2D(_AsciiCellSplit, p).a != 0.0)
                        continue;

                    float4 g = LOAD_TEXTURE2D(_AsciiCellGeometry, p);
                    float depthWeight = 1.0 - smoothstep(0.003, 0.012, AsciiRelativeDepth(g.x, geometry.x));
                    float normalWeight = surfaceClass == ASCII_CLASS_FOLIAGE ? 1.0 : smoothstep(0.9, 0.99, dot(g.yzw, geometry.yzw));
                    // Leave strong lighting and hue boundaries, including cast shadows, crisp.
                    float contrast = AsciiLuminance(abs(other.rgb - color.rgb)) / max(AsciiLuminance(max(other.rgb, color.rgb)), 0.02);
                    float w = 0.35 * depthWeight * normalWeight * (1.0 - smoothstep(0.15, 0.55, contrast));
                    sum += other.rgb * w;
                    weight += w;
                }
                return sum / weight;
            }

            bool SplitContinues(int2 cell, int2 cells, int split)
            {
                int2 along = split == ASCII_SPLIT_X ? int2(0, 1) : int2(1, 0);
                int2 before = cell - along;
                int2 after = cell + along;
                return (all(before >= 0) && LOAD_TEXTURE2D(_AsciiCellSplit, max(before, int2(0, 0))).a == split)
                    || (all(after < cells) && LOAD_TEXTURE2D(_AsciiCellSplit, min(after, cells - 1)).a == split);
            }

            float4 FragSelect(AsciiVaryings input) : SV_Target
            {
                int2 cell = int2(input.positionCS.xy);
                int2 cells = int2(_AsciiCell.zw);

                float4 c0 = LOAD_TEXTURE2D(_AsciiCellColor, cell);
                float4 g0 = LOAD_TEXTURE2D(_AsciiCellGeometry, cell);
                int surfaceClass = clamp((int)(c0.a + 0.5), 0, 15);
                bool fitShapes = _AsciiDebug.y > 0.5;
                if (fitShapes)
                {
                    float4 split = LOAD_TEXTURE2D(_AsciiCellSplit, cell);
                    if (split.a > 0.0 && SplitContinues(cell, cells, (int)split.a))
                        return split;
                    if (AsciiFitsSurface(surfaceClass) && surfaceClass != ASCII_CLASS_SKY)
                        c0.rgb = GroupCellColor(cell, cells, c0, g0, surfaceClass);
                }
                float4 classParams = _AsciiClassParams[surfaceClass];
                float4 classParams2 = _AsciiClassParams2[surfaceClass];

                // --- Density from luminance (exposure only affects glyph choice) ---
                float lumHdr = AsciiLuminance(c0.rgb * _AsciiTone.x);
                float lum = lumHdr / (1.0 + lumHdr);
                lum = saturate((lum - 0.5) * _AsciiTone.y + 0.5);
                lum = pow(max(lum, 1e-5), _AsciiTone.z);

                // Density floor: textured surfaces keep dim glyphs in shadow, so
                // lighting reads through glyph colour rather than empty space.
                // Cells with essentially no light still fall to black.
                float densityFloor = saturate(classParams2.x * _AsciiSparkle2.w);
                float hasLight = saturate(lumHdr * 40.0);
                float density = lerp(densityFloor * hasLight, 1.0, lum);

                int2 worldCell = AsciiWorldCell(cell);
                float patchDensity = PatchDensity(worldCell);
                float patch = lerp(1.0, patchDensity, classParams2.z);
                density *= patch;
                float noise = AsciiHash(worldCell) - 0.5;
                density += noise * _AsciiTone.w * classParams.w / ASCII_LEVELS;

                if (surfaceClass == ASCII_CLASS_WATER)
                {
                    float t = _AsciiMisc.x * _AsciiMisc.y;
                    density += 0.07 * sin(t + worldCell.x * 0.55 + worldCell.y * 1.3)
                             + 0.04 * sin(t * 1.7 - worldCell.x * 0.31 + worldCell.y * 0.77);
                }

                int glyph = clamp((int)(saturate(density) * ASCII_LEVELS), 0, ASCII_LEVELS - 1);

                // --- Sparkles: sparse world-anchored accents (flowers / fireflies) ---
                bool sparkle = classParams2.y > 0.0 && AsciiHash2(worldCell) < _AsciiSparkle.a * classParams2.y;
                if (sparkle)
                    glyph = ASCII_COL_SPARKLE;

                // --- Line glyphs on silhouettes and creases ---
                if (_AsciiEdge.w > 0.5 && classParams.z > 0.5 && surfaceClass != ASCII_CLASS_SKY)
                {
                    int edge = DetectEdge(cell, cells, g0, fitShapes && surfaceClass == ASCII_CLASS_FOLIAGE);
                    if (edge >= 0)
                    {
                        glyph = edge;
                        sparkle = false;
                    }
                }

                // --- Glyph color: the lit surface colour, so light pools read directly ---
                float3 color = max(c0.rgb, 0.0);
                float l = max(AsciiLuminance(color), 1e-4);
                color = max(lerp(l.xxx, color, _AsciiColor.z), 0.0);
                float normalizeBoost = lerp(1.0, clamp(0.35 / l, 0.25, 6.0), _AsciiColor.w);
                float jitter = 1.0 + (AsciiHash(worldCell + int2(17, 59)) - 0.5) * 2.0 * _AsciiSparkle2.z;
                float3 foreground = color * _AsciiColor.x * normalizeBoost * jitter * lerp(1.0, patch, 0.65);
                if (surfaceClass == ASCII_CLASS_EXEMPT)
                {
                    // Lift the character's strokes in low light while preserving
                    // bright highlights and the dark gaps between strokes.
                    float lift = lerp(2.6, 1.0, smoothstep(0.03, 0.4, lumHdr));
                    foreground = max(foreground * lift, _AsciiBackgroundColor.rgb * 3.0);
                }

                if (sparkle)
                {
                    float phase = AsciiHash(worldCell + int2(91, 13)) * 6.2831853;
                    float pulse = 0.35 + 0.65 * saturate(0.5 + 0.5 * sin(_AsciiMisc.x * 2.1 + phase));
                    float twinkle = lerp(1.0, pulse, _AsciiSparkle2.y);
                    float sceneLight = saturate(lumHdr * 1.5);           // flowers only show where lit
                    float emission = lerp(sceneLight, 1.0, _AsciiSparkle2.x); // fireflies glow on their own
                    foreground = _AsciiSparkle.rgb * emission * twinkle;
                }

                int row = (int)classParams.y;
                bool alternate = fitShapes ? patchDensity < 0.72 : AsciiHash2(worldCell + int2(73, 11)) < 0.5;
                if (classParams2.w > 0.0 && alternate)
                    row = (int)classParams2.w;
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
            float _AelidMorningMist;

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
                // Let the fog already present in the lit scene tint the gaps between glyphs.
                float backgroundFill = max(_AsciiColor.y, _AelidMorningMist * 0.09);
                float3 background = max(cellAverage * backgroundFill, _AsciiBackgroundColor.rgb);
                float3 ascii;
                float ink = 0.0;
                bool hasSplit = code == ASCII_SPLIT_X || code == ASCII_SPLIT_Y;
                if (hasSplit)
                {
                    int firstSize = cs / 2;
                    bool first = (code == ASCII_SPLIT_X ? local.x : local.y) < firstSize;
                    float3 second = (cellAverage * cs - glyphData.rgb * firstSize) / (cs - firstSize);
                    float3 color = max(first ? glyphData.rgb : second, 0.0);
                    float luminance = AsciiLuminance(color);
                    float fill = max(backgroundFill, 0.20 * _AsciiColor.x);
                    ascii = max(lerp(luminance.xxx, color, _AsciiColor.z) * fill, _AsciiBackgroundColor.rgb);
                }
                else
                {
                    int row = code >> 4;
                    int col = code & 15;
                    int ly = AsciiRowsGoDown() ? local.y : cs - 1 - local.y;
                    int gx = (local.x * ASCII_GLYPH) / cs;
                    int gy = (ly * ASCII_GLYPH) / cs;
                    // Alternate blade lean without rotating edge glyphs or changing camera stability.
                    if (row == 6 && col < ASCII_LEVELS && AsciiHash2(AsciiWorldCell(cell) + int2(43, 71)) < 0.5)
                        gx = ASCII_GLYPH - 1 - gx;
                    int2 atlasTexel = int2(col * ASCII_GLYPH + gx, ASCII_ATLAS_SIZE - 1 - (row * ASCII_GLYPH + gy));
                    ink = LOAD_TEXTURE2D(_AsciiGlyphAtlas, atlasTexel).r;
                    ascii = lerp(background, glyphData.rgb, ink);
                }

                float3 scene = LOAD_TEXTURE2D(_AsciiSceneColor, qc).rgb;
                int pixelClass = LoadClass(qc, size);
                float amount = saturate(_AsciiClassParams[pixelClass].x) * _AsciiMisc.w;
                float3 result = lerp(scene, ascii, amount);

                // Dark outside rim for bright ground, light inside rim for shadows.
                if (_AsciiMisc.z > 0.0)
                {
                    int r = max(1, cs / 8);
                    bool exempt = pixelClass == ASCII_CLASS_EXEMPT;
                    bool rim = (LoadClass(qc + int2(r, 0), size) == ASCII_CLASS_EXEMPT) != exempt
                            || (LoadClass(qc - int2(r, 0), size) == ASCII_CLASS_EXEMPT) != exempt
                            || (LoadClass(qc + int2(0, r), size) == ASCII_CLASS_EXEMPT) != exempt
                            || (LoadClass(qc - int2(0, r), size) == ASCII_CLASS_EXEMPT) != exempt;
                    if (rim)
                    {
                        float opacity = _AsciiMisc.z * _AsciiMisc.w;
                        result = exempt
                            ? lerp(result, max(result, 0.73), opacity * 0.38)
                            : lerp(result, _AsciiOutlineColor.rgb, opacity);
                    }
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
                else if (debugView == 4) result = hasSplit ? ascii : glyphData.rgb;
                else if (debugView == 5) result = lerp(float3(0.05, 0.05, 0.1), float3(1, 1, 0.8), ink);
                else if (debugView == 6)
                    result = float3(0.0, code == ASCII_SPLIT_X ? 1.0 : 0.0, code == ASCII_SPLIT_Y ? 1.0 : 0.0);

                return float4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
