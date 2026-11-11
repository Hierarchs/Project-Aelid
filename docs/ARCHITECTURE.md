# Architecture: Milestone 1 (visual foundation)

Status as of 2026-11-10. **Working** means built, run, and checked by captures or automated tests. **Proposed** means planned but not built.

## Modules

Each module is its own assembly, so compile-time dependencies stay explicit and one-way.

| Assembly | Folder | Depends on | Responsibility |
|---|---|---|---|
| `Aelid.Rendering` | `Scripts/Rendering` | URP | Hybrid ASCII renderer feature, glyph atlas, surface classes, glyph-stable camera snapping |
| `Aelid.Environment` | `Scripts/Environment` | URP | Day/night cycle, night lights, flicker, water (reflection, ripples, water bodies) |
| `Aelid.Gameplay` | `Scripts/Gameplay` | Input System | Input → intent → motor, procedural animation, isometric camera rig |
| `Aelid.Debugging` | `Scripts/Debugging` | all of the above | Dev HUD and hotkeys |
| `Aelid.Editor` | `Scripts/Editor` | all | Scene builder, project setup, capture and stability tool |
| `Aelid.Tests.*` | `Tests/` | all | Edit-mode and play-mode tests |

Gameplay does not reference Rendering, and Rendering does not reference Gameplay. The camera rig just positions a camera; ASCII snapping is a separate component (`AsciiCamera`) that runs after it.

## Hybrid ASCII pipeline (working)

`AsciiRendererFeature` is a URP 17 RenderGraph feature added to `PC_Renderer` and `Mobile_Renderer`. It runs at **Before Post-Processing**, so bloom and color grading apply to the glyphs (fire and magic glyphs glow).

```
lit 3D frame ─┬─► [1] Surface ID   redraw tagged renderers → R8 class mask (full res)
              ├─► [2] Reduce       per cell: average color, class, depth, normal (cell res, MRT)
              │       [3] Select   per cell: glyph = density ramp + dither, or line glyph on edges
              └─► [4] Composite    per pixel: blend glyphs by surface class; outline; sub-cell shift
                                    → becomes the camera color for post-processing
```

- **Cost:** passes 2 and 3 run at 1/64 of the pixel count (8 px cells). The real cost is one full-resolution read in Reduce, one full-resolution write in Composite, and redrawing only the renderers that carry a class tag. GPU timings haven't been profiled yet.
- **Glyphs:** 8×8 bitmaps drawn in `AsciiGlyphAtlas.cs` (original art, no font files). Each vocabulary is sorted by ink coverage. Grass, foliage, stone, and wood select alternate ramps on the world grid. Thin edge glyphs connect across cells.
- **Surface classes:** the class is stored in Rendering Layer bits 1–7 (bit 0 stays Default, and bits 8+ are free for light layers). Tag objects with the `AsciiSurface` component, or by setting Rendering Layers in the inspector.

| Class | Glyph vocabulary (dark → bright) | Shadow density floor | ASCII amount | Use |
|---|---|---|---|---|
| Default | Sparse punctuation | 0.08 | 100% | dirt, sand, snow, misc |
| Exempt | Punctuation and stacked strokes | 0.43 | 86% in the vertical slice | player character with brighter strokes in low light and a dark silhouette outline |
| Foliage | Mixed round glyphs and letters | 0.44 | 100% | trees, bushes |
| Water | Thin horizontal strokes and dashes | 0.16 | 100% | water |
| Stone | Two ramps of thin letters | 0.46 | 100% | rocks, cliffs, ruins |
| Emissive | ` .'^+%*A@#` | 0.40 | 45% | fire, lamps, spells |
| Grass | Asymmetric blade glyphs, mirrored variants, and sparkles | 0.66 | 100% | grassland |
| Wood | Punctuation and thin letters | 0.39 | 100% | buildings, fences |

- **Light response.** The midnight-blue background gives the glyphs room to breathe. Glyph colors follow the lit 3D scene, with cool moonlight and warm pools around lamps and fires. A per-class density floor and world-anchored patches vary the concentration of strokes across grass, stone, and water. Fully unlit cells fade to the background.
- **Sparkles:** a world-anchored sprinkle of accent glyphs on grass (and lightly on foliage). By day they are scene-lit white flowers. The Night volume turns them into self-lit, slowly pulsing HDR fireflies that bloom.
- **Configuration:** the `Aelid/ASCII` volume component sets intensity, cell size (density), exposure, contrast, gamma, dither, density floor, glyph brightness, background fill, saturation, color normalize, color jitter, sparkles (density, color, self-lit, twinkle), edge sensitivity, Exempt and Emissive amounts, outline, and water ripple. Because it is a volume, settings blend: the Night volume raises glyph exposure and brightness and switches flowers to fireflies. Weather and local areas can do the same later.

### Glyph stability (working, measured)

A screen-fixed glyph grid shimmers when the world slides under it. `AsciiCamera` snaps the camera to whole cells on its view plane, and the Composite pass shifts the finished image by the leftover whole pixels. The view still scrolls smoothly, but every glyph stays locked to the world. Dithering is hashed by world cell, not screen cell, so it is stable too.

Measured by `Aelid ▸ Capture Review Screenshots` (written to `Captures/stability.txt`): the camera moves 3 px right and 2 px up per frame, and the test counts pixels that change after compensating for the scroll:

| Mode | Changed pixels |
|---|---|
| Raw 3D (reference) | 0.00% |
| ASCII, snapping off | ~25% |
| **ASCII, snapping on** | **0.00%** |

The snapper uses the URP raster dimensions so its cell grid matches the shader when render scale is below 1. The camera-scroll captures cover the village and ground-plane movement beside the pond in morning fog and moonlight.

## Camera (working)

`IsometricCameraRig` follows the player at a fixed 52° pitch and north-facing yaw. The playable scene locks orthographic projection and zoom at an 8 m half-height because changing zoom rescales the glyph grid. Capture tools can set framing for individual shots.

## Player (working)

`PlayerInputReader` (Input System, actions defined in code) produces a `PlayerIntent` struct, and `PlayerController` maps it into camera-relative world space. `PlayerMotor` (CharacterController) consumes only `PlayerIntent` and `dt`, with no input or camera access. That is deliberate: the same motor can run on a server for authoritative validation and on the client for prediction. The swordsman's FBX has six pivots driven by `ProceduralHumanoidAnimator` for limb swing, bob, lean, and breathing. The editable model is in `Art/Blender/Aelid Swordsman.blend`.

## Day/night (working)

`DayNightCycle` moves one directional light between the sun and moon. The handover happens at the horizon, where both are dark. It also drives trilight ambient, fog, the procedural skybox, and the Night volume weight. Cool morning mist peaks shortly after sunrise and fades by midmorning. The fog color gradient and density curve are editable in the inspector. `NightLight` fades lamps and windows in at dusk, and `FlickerLight` animates firelight.

## Water (working)

The water renders as lit 3D, and the ASCII pass then turns it into horizontal line glyphs (`≡ ≈ = ~ -`). Reflections, ripples and light all come through the glyphs.

- **`PlanarReflection`** (on the water): a hidden `CameraType.Reflection` camera mirrors the game camera across the surface and renders the world above it into a half-resolution HDR texture. It uses an ordinary rotation, not a mirrored view matrix, so no culling inversion is needed; the shader samples at `u = 1 - u`. An oblique near plane clips everything below the water. Because the game camera moves in whole ASCII cells, the mirror camera moves in whole texels and reflections stay glyph-stable. The open sky reflects at `skyBrightness` (0.4), so water stays dark and reflected objects stand out. The ASCII pass skips this camera.
- **`Water.shader`** reads camera depth and opaque color for bed refraction, shoreline foam, and depth absorption. World-space waves and ripple rings distort the planar reflection and a stylized moon disc whose position and visibility follow `DayNightCycle`. The builder restores `Water_Pond.mat` from the shader defaults.
- **`WaterRipples`**: a 16-slot global ring buffer (`_WaterRipples`, `_WaterTime`) that anything can emit into. **`WaterRippleEmitter`** on a character emits a splash on entering, one ring per stride, and soft rings while standing. **`WaterBody`** answers "is this point in water, and at what height?"
- The surface-ID pass draws transparent renderers after opaques, so water over a wading character's legs uses water glyphs and the exposed body uses the character blend.

## Tests and tools

- **Edit mode (9):** glyph ramps are monotonic, atlas layout, rendering-layer tagging, cell-size scaling, snap math over 200 random positions, reflection mirror math (surface points and mirror images), the reflection camera's setup, and ripple expiry.
- **Play mode (10):** loads the real scene and drives it with virtual input. Covers movement, sprinting, camera follow and framing lock, snap offsets, wading, morning mist, the night-only moon reflection, and the ASCII toggle.
- **Captures:** 13 review shots (ASCII and raw), including morning mist and moonlight at the pond, plus the glyph atlas preview, reflection textures, and the stability report. Water time is frozen so shots are repeatable.
- **Debug views:** `AsciiRuntimeOverrides.DebugView` shows scene passthrough, cell color, surface class, glyph color, or the atlas tiled across the screen (useful for orientation checks).

## Known limitations

- 1-cell border: the sub-cell shift repeats the edge glyph column or row at the screen edge (at most 7 px). An overscan render target would remove it.
- MSAA must stay off in the URP asset (the surface-ID pass shares the camera depth buffer). The ASCII quantization makes MSAA pointless anyway.
- Transparent renderers can carry a class, but depth-tested only against opaque depth. Particles behind the water surface are hidden because water writes depth.
- Reflections are planar, so they assume flat water. Each water plane costs one extra (shadowless, half-resolution) scene render while it's on screen. The moon isn't mirrored: it travels the southern sky, behind the north-facing camera.
- Water reflections are correct only for the game camera; the Scene view shows the game camera's reflection texture.
- XR is not supported by the ASCII passes.
- The scene is generated by `Aelid ▸ Build Vertical Slice Scene`. Rebuilding overwrites manual edits to that scene.

## Proposed next milestones (not built)

1. **Weather M2:** a central `WeatherController` layered on `DayNightCycle`, with rain and snow particles (and an ASCII treatment for them via a transparent ID pass), wet and snowy surface response, and fog and wind. Snow accumulation would use a world-space top-down mask.
2. **Combat M3:** stamina, light and heavy attacks, dodge with i-frames, and one enemy with telegraphed attacks, all built on the intent/motor split.
3. **Look M2:** per-region ASCII volumes, an optional 6/10/12 px density preset menu, an overscan border fix, rivers/streams (flowing water reusing `Water.shader`), and wading slowdown.
4. **Networking M4:** a two-player authoritative prototype (Netcode for GameObjects or a custom transport), replicating `PlayerIntent`.
