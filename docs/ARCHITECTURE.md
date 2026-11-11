# Architecture: Milestone 1 (visual foundation)

Status as of 2026-11-10. **Working** means built, run, and checked by captures or automated tests. **Proposed** means planned but not built.

## Modules

Each module is its own assembly, so compile-time dependencies stay explicit and one-way.

| Assembly | Folder | Depends on | Responsibility |
|---|---|---|---|
| `Aelid.Rendering` | `Scripts/Rendering` | URP | Hybrid ASCII renderer feature, glyph atlas, surface classes, glyph-stable camera snapping |
| `Aelid.Environment` | `Scripts/Environment` | URP | Day/night cycle, night lights, flicker, placeholder water |
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
              └─► [4] Composite    per pixel: draw glyph; Exempt pixels stay 3D; outline; sub-cell shift
                                    → becomes the camera color for post-processing
```

- **Cost:** passes 2 and 3 run at 1/64 of the pixel count (8 px cells). The real cost is one full-resolution read in Reduce, one full-resolution write in Composite, and redrawing only the renderers that carry a class tag. GPU timings haven't been profiled yet.
- **Glyphs:** 8×8 bitmaps drawn in `AsciiGlyphAtlas.cs` (original art, no font files). Each vocabulary is sorted by ink coverage at build time. Edge glyphs `| - / \` run corner to corner, so lines connect across cells.
- **Surface classes:** the class is stored in Rendering Layer bits 1–7 (bit 0 stays Default, and bits 8+ are free for light layers). Tag objects with the `AsciiSurface` component, or by setting Rendering Layers in the inspector.

| Class | Glyph vocabulary (dark → bright) | Shadow density floor | ASCII amount | Use |
|---|---|---|---|---|
| Default | ` .-:=+%*@#` | 0.30 | 100% | dirt, sand, snow, misc |
| Exempt | (none) | n/a | 0% (configurable) | players, important enemies: full 3D with a dark rim |
| Foliage | ` .,;o8*&♣♠` | 0.60 | 100% | trees, bushes |
| Water | ` .-~=░≈≋▒▓` + animated ripple | 0.15 | 100% | water |
| Stone | ` .:oOS8QMB` (thin letters) | 0.55 | 100% | rocks, cliffs, ruins |
| Emissive | ` .'^+%*A@#` | 0.40 | 45% | fire, lamps, spells |
| Grass | 8 custom thin blade glyphs + sparkles | 0.65 | 100% | grassland |
| Wood | ` .-=+xNH@#` | 0.50 | 100% | buildings, fences |

- **Light response (style target: dark terminal-style ASCII).** The background is near-black, and glyph color is the *lit* surface color, so light pools, shadows and firelight read through color rather than glyph count. A per-class **density floor** keeps shadowed surfaces textured with dim glyphs instead of empty space. Fully unlit cells still go black. World-anchored **color jitter** and stronger dithering on grass break up repetition.
- **Sparkles:** a world-anchored sprinkle of accent glyphs on grass (and lightly on foliage). By day they are scene-lit white flowers. The Night volume turns them into self-lit, slowly pulsing HDR fireflies that bloom.
- **Configuration:** the `Aelid/ASCII` volume component sets intensity, cell size (density), exposure, contrast, gamma, dither, density floor, glyph brightness, background fill, saturation, color normalize, color jitter, sparkles (density, color, self-lit, twinkle), edge sensitivity, Exempt and Emissive amounts, outline, and water ripple. Because it is a volume, settings blend: the Night volume raises glyph exposure and brightness and switches flowers to fireflies. Weather and local areas can do the same later.

### Glyph stability (working, measured)

A screen-fixed glyph grid shimmers when the world slides under it. `AsciiCamera` snaps the camera to whole cells on its view plane, and the Composite pass shifts the finished image by the leftover whole pixels. The view still scrolls smoothly, but every glyph stays locked to the world. Dithering is hashed by world cell, not screen cell, so it is stable too.

Measured by `Aelid ▸ Capture Review Screenshots` (written to `Captures/stability.txt`): the camera moves 3 px right and 2 px up per frame, and the test counts pixels that change after compensating for the scroll:

| Mode | Changed pixels |
|---|---|
| Raw 3D (reference) | 0.00% |
| ASCII, snapping off | ~42% (dense textures make an unsnapped grid shimmer badly) |
| **ASCII, snapping on** | **0.00%** |

This is exact for the orthographic camera, which is the default. In perspective mode it is exact only at the player's depth.

## Camera (working)

`IsometricCameraRig`: fixed pitch (52°) and yaw (0° = looking north, Asciilands style), orthographic by default, smooth follow with slight look-ahead, and mouse-wheel zoom between 5 and 13 m half-height. Perspective is a toggle (F5, narrow FOV) for comparison.

## Player (working, placeholder art)

`PlayerInputReader` (Input System, actions defined in code) produces a `PlayerIntent` struct, and `PlayerController` maps it into camera-relative world space. `PlayerMotor` (CharacterController) consumes only `PlayerIntent` and `dt`, with no input or camera access. That is deliberate: the same motor can run on a server for authoritative validation and on the client for prediction. The character is built from boxes and animated by `ProceduralHumanoidAnimator` (limb swing, bob, lean, breathing). Swap in a rigged model and Animator later without touching the motor.

## Day/night (working)

`DayNightCycle` moves one directional light: the sun by day, the moon by night (URP shadows only one main light). The handover happens at the horizon, where both are dark. It also drives trilight ambient, fog, the procedural skybox, and the Night volume weight. All color and density curves are gradients you can edit in the inspector. `NightLight` fades lamps and windows in at dusk, and `FlickerLight` animates firelight.

## Tests and tools

- **Edit mode (5):** glyph ramps are monotonic, atlas layout, rendering-layer tagging, cell-size scaling, and snap math over 200 random positions.
- **Play mode (7):** loads the real scene and drives it with a virtual keyboard. W walks north and the camera follows; sprint speed; ground contact; snap offsets stay within one cell; time progresses and night falls; F1 toggles ASCII. Any logged error fails the run.
- **Captures:** 7 review shots (ASCII and raw) across times of day, the glyph atlas preview, and the stability report.
- **Debug views:** `AsciiRuntimeOverrides.DebugView` shows scene passthrough, cell color, surface class, glyph color, or the atlas tiled across the screen (useful for orientation checks).

## Known limitations

- 1-cell border: the sub-cell shift repeats the edge glyph column or row at the screen edge (at most 7 px). An overscan render target would remove it.
- MSAA must stay off in the URP asset (the surface-ID pass shares the camera depth buffer). The ASCII quantization makes MSAA pointless anyway.
- Only opaque renderers can carry a class. Transparent effects (particles, spells) get the Default treatment until a transparent ID pass exists.
- Water is a CPU-animated faceted mesh using URP/Lit, with no real reflections yet.
- XR is not supported by the ASCII passes.
- The scene is generated by `Aelid ▸ Build Vertical Slice Scene`. Rebuilding overwrites manual edits to that scene.

## Proposed next milestones (not built)

1. **Weather M2:** a central `WeatherController` layered on `DayNightCycle`, with rain and snow particles (and an ASCII treatment for them via a transparent ID pass), wet and snowy surface response, and fog and wind. Snow accumulation would use a world-space top-down mask.
2. **Combat M3:** stamina, light and heavy attacks, dodge with i-frames, and one enemy with telegraphed attacks, all built on the intent/motor split.
3. **Look M2:** water shader with reflections, per-region ASCII volumes, an optional 6/10/12 px density preset menu, and an overscan border fix.
4. **Networking M4:** a two-player authoritative prototype (Netcode for GameObjects or a custom transport), replicating `PlayerIntent`.
