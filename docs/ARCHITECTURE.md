# Architecture: Milestone 1 (visual foundation)

Status as of 2026-11-10. **Working** means built, run, and checked by captures or automated tests. **Proposed** means planned but not built.

## Modules

Each module is its own assembly, so compile-time dependencies stay explicit and one-way.

| Assembly | Folder | Depends on | Responsibility |
|---|---|---|---|
| `Aelid.Rendering` | `Scripts/Rendering` | URP | Hybrid ASCII renderer feature, volumetric fog, glyph atlas, surface classes, glyph-stable camera snapping |
| `Aelid.Environment` | `Scripts/Environment` | URP | Day/night cycle, cloud shadows, night lights, flicker, water (reflection, ripples, water bodies) |
| `Aelid.Gameplay` | `Scripts/Gameplay` | Input System | Input → intent → motor, procedural animation, isometric camera rig |
| `Aelid.World` | `Scripts/World` | (engine only) | Region rule tables, region outlines, keep-clear zones, deterministic region generator |
| `Aelid.Debugging` | `Scripts/Debugging` | all of the above | Dev HUD and hotkeys |
| `Aelid.Editor` | `Scripts/Editor` | all | Scene builder, project setup, capture and stability tool |
| `Aelid.Tests.*` | `Tests/` | all | Edit-mode and play-mode tests |

Gameplay does not reference Rendering, and Rendering does not reference Gameplay. The camera rig just positions a camera; ASCII snapping is a separate component (`AsciiCamera`) that runs after it.

## Hybrid ASCII pipeline (working)

`AsciiRendererFeature` is a URP 17 RenderGraph feature added to `PC_Renderer` and `Mobile_Renderer`. It runs at **Before Post-Processing**, so bloom and color grading apply to the glyphs (fire and magic glyphs glow).

```
lit 3D frame ─┬─► [1] Surface ID   renderer classes + ground coverage → R8 class mask (full res)
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

`DayNightCycle` moves one directional light between the sun and moon. The handover happens at the horizon, where both are dark. It also drives trilight ambient, fog, the procedural skybox, and the Night volume weight. Violet light and mist build around dawn and fade by midmorning. **Dawn Tint** and **Dawn Fog Density** are editable in the inspector alongside the fog color gradient and density curve. `NightLight` fades lamps and windows in at dusk, and `FlickerLight` animates firelight.

## Cloud shadows (prototype)

`CloudShadows` (on the Environment object) generates a seamlessly tiling cloud
texture and sets it as the celestial light's URP light cookie. Clouds therefore dim
only direct sun or moon light; ambient light, emissives, lamps and fires are
untouched, and cast shadows soften under a cloud as they should. The cookie is
projected in world space, so shade stays locked to the ground: the stability test
still measures 0.00% shimmer. In play mode the pattern drifts with **Wind**
(metres per second over the ground). It holds still in edit mode, so captures are
repeatable.

Settings: **Coverage** (the shaded fraction; the weather hook), **Opacity** (how much
direct light a cloud blocks, 0.75), **Softness** (edge width), **Tile Size** (100 m;
clouds are about a quarter of that) and **Seed**. Larger clouds were tried: at the
gameplay zoom a frame then sits inside one cloud and the scene just looks darker.
Ground, Stone, Water and the foliage wind shader declare `_LIGHT_COOKIES`; URP Lit
already does. Edit the foliage shader through `URPFoliageWind.shader.txt` too.

In the ASCII view the effect is subtler than in 3D. The grass glyphs' world-anchored
patches are at a similar scale, so clouds read as darker zones crossing the glyph
texture rather than distinct shapes. On average, direct sunlight drops by about
coverage × opacity (30% at the defaults). **Aelid → Setup → Add Cloud Shadows to Open
Scene** adds it to a scene without a rebuild; full builds include it.

## Volumetric fog and light shafts (prototype)

`VolumetricFogFeature` (RenderGraph, registered by `ProjectSetup` on both renderers)
runs after transparents, so the ASCII pass quantises the fogged image. Reflection and
preview cameras are skipped.

1. **Fog March** (half resolution): each view ray is clipped to the fog layer below
   **Top** and marched in fixed steps (32). Each step samples the main-light shadow map,
   so tree crowns and buildings cut the fog into shafts. It also samples the cloud
   cookie, so cloud shadows fall through the mist as diagonal rays at low sun. Sky
   ambient and up to 8 visible point or spot lights add light, so lanterns, windows and
   fires get halos. Scattering uses a Henyey-Greenstein phase.
2. **Fog Composite** (full resolution): a depth-aware 2×2 upsample, blended as
   colour × transmittance + in-scattered light.

Settings live in the `VolumetricFog` volume component, so the day and night profiles
blend it. The medium is a thin haze below Top plus ground mist that thins with height
above **Base Height**, so it pools in hollows and around the pond. Optional noise
drifts with **Wind** in play mode. Morning mist adds density through DayNightCycle's
global `_AelidMorningMist`, so dawn needs no profile of its own. **Max Opacity** caps
how much the fog may hide (day 0.3, night 0.6), keeping characters readable.

Lit lights burn the fog off around them. Each local light clears a pocket of
**Clearing Radius** × its range (0.5), by up to **Light Clearing** (0.85). The pocket
is scaled by how brightly the light is lit, so lamps that NightLight switches off
by day clear nothing. The campfire and the village lamps stand in clear air, with
fog and halos at the pocket's edge. `VolumetricFogFeature.Clearing` mirrors the
shader's curve for tests.

Glyph stability: march samples sit mid-step with no per-pixel or per-frame noise, and
the half-resolution grid lines up with the snapped ASCII cells. The stability test
still reads 0.00% with snapping on. An earlier world-hashed march offset produced
visible blocks and was removed.

Tuning findings: from the 52° top-down camera, midday shafts are barely visible. Haze
above the canopy is evenly lit and only veils the scene, so the day profile is a light
haze in hollows. Shafts show best at low sun and at night: cloud-shadow rays through
the morning mist, tree-shadow streaks, and halos. The built-in distance fog that
DayNightCycle drives is still on and stacks with this, mostly at dawn. Removing or
reducing it is a separate look decision. Neither pass has been GPU-profiled.
**Aelid → Setup → Add Volumetric Fog to Slice Profiles** applies the fog to existing
profiles in place; full builds include it.

## Water (working)

The water renders as lit 3D, and the ASCII pass then turns it into horizontal line glyphs (`≡ ≈ = ~ -`). Reflections, ripples and light all come through the glyphs.

- **`PlanarReflection`** (on the water): a hidden `CameraType.Reflection` camera mirrors the game camera across the surface and renders the world above it into a half-resolution HDR texture. It uses an ordinary rotation, not a mirrored view matrix, so no culling inversion is needed; the shader samples at `u = 1 - u`. An oblique near plane clips everything below the water. Because the game camera moves in whole ASCII cells, the mirror camera moves in whole texels and reflections stay glyph-stable. The open sky reflects at `skyBrightness` (0.4), so water stays dark and reflected objects stand out. The ASCII pass skips this camera.
- **`Water.shader`** reads camera depth and opaque color for bed refraction, shoreline foam, and depth absorption. World-space waves and ripple rings distort the planar reflection and a stylized moon disc whose position and visibility follow `DayNightCycle`. The builder restores `Water_Pond.mat` from the shader defaults.
- **`WaterRipples`**: a 16-slot global ring buffer (`_WaterRipples`, `_WaterTime`) that anything can emit into. **`WaterRippleEmitter`** on a character emits a splash on entering, one ring per stride, and soft rings while standing. **`WaterBody`** answers "is this point in water, and at what height?"
- The surface-ID pass draws transparent renderers after opaques, so water over a wading character's legs uses water glyphs and the exposed body uses the character blend.

## Cliff and stone surfaces

`Aelid/Stone` adds world-space surface texture to `Terrain_Rock`, `Rock`, `Stone`,
`Ruin_Stone`, and `Ruin_Moss` while retaining their existing base colours. Three
planar projections blend by surface normal, so the pattern does not stretch up
steep faces or require mesh UVs. Irregular cell boundaries form fractures, broad
noise creates mineral variation, and filtered fine grain adds smaller detail.

Natural cliff presets add wavy bedding bands by world height, strongest on steep
faces. Flat stone reads as fractured plates instead. The carved ruin and general
masonry presets disable geological banding and use finer, quieter joints. Sparse
lichen follows upward ledges and recessed seams, with a stronger amount on the
existing mossy ruins material.

A bounded surface-gradient normal gives shallow lighting relief. Forward lighting
and the depth-normal pass evaluate the same relief; shadow and depth passes retain
the original geometry. Meshes, silhouettes, collision, the scene, and Stone ASCII
class bits are unchanged. The existing ASCII pass receives the textured colour
and relief normals without adding another render pass.

`StoneMaterials` assigns the presets when a material first adopts the shader and
keeps later texture-detail tuning. Full scene builds use it automatically. **Aelid → Setup
→ Refresh Stone Materials** updates the five materials without rebuilding scene
objects. Saved materials already reference the shader, so no scene rebuild is
needed. Fracture frequency, layer strength, joint contrast, mineral grain, relief,
and lichen remain editable per material.

Validation: 18 DXC shader entry/variant compilations passed against the pinned URP
headers, covering forward/clustered lighting, cascaded and screen-space shadows,
instancing, depth, relief normals, octahedral normals, and punctual shadows. The
material setup compiled against Unity's managed assemblies, and managed checks
confirmed all five saved presets. The local Unity editor verified the layered cliff
material in the saved scene and a full rebuild. The three-projection fracture
calculation has not been profiled.

## Grass and dirt surfaces

`Aelid/Ground` replaces flat colours on the three existing grass/dirt materials.
It evaluates texture in world XZ coordinates without requiring mesh UVs: broad
grass clumps, dry tips, fine blade strokes, damp/dry soil, small clods, and sparse
pebbles. Detail strokes use pixel-footprint filtering. Nothing animates, so the
material does not introduce deliberate texture movement beneath the ASCII grid.

The grass and dirt buckets share the same colour field. Path shoulders blend
with irregular edges, and the ruins have patches of exposed earth rather than
material boundaries following terrain triangles. `GroundLayout.hlsl` is generated
from the builder's path segments, ruins centre, and path width. Both full scene
builds and **Aelid → Setup → Refresh Ground Materials** keep it in sync; the refresh
command updates materials/layout without replacing scene objects. The saved
materials already reference the shader, so a scene rebuild is unnecessary.

The surface-ID RenderGraph pass draws ground's own `AelidGroundSurfaceId` pass
after the opaque override IDs and before water IDs. Its world-locked coverage
dithering chooses the existing Grass or Default class per pixel from the same
blend used for colour. Grass blades and flowers therefore follow the grassy
shoulders, while the path keeps dirt glyphs. Ground depth and normals are still
tested normally, so foreground characters and water retain their classes. The
composite also mirrors some grass density glyphs using the existing world-cell
hash; line glyphs remain unchanged.

Tune colours, detail strength, and path shoulder width on the ground materials.
For unrelated ground areas, turn off **Follow Vertical Slice Paths** and use
**Grass Amount**. The shader includes forward lighting, shadow caster, depth,
depth-normal, and ASCII coverage passes; grass/dirt geometry and colliders are
unchanged. The additional ID draws and procedural texture cost are not profiled.

Validation: 26 DXC entry/variant compilations passed against the pinned URP
headers, including the changed ASCII shader; ground setup/tests compile against
Unity's managed assemblies; the managed layout export test and generated-layout
comparison passed. The local Unity editor verified the ground surface ID pass in
the saved scene and a full rebuild. The path shoulders are visible in the dawn
captures.

## Region generation (prototype)

Wild vegetation and rocks come from **regions**, modelled on the Region Editor
that Bethesda used to populate Oblivion's Cyrodiil: an outline drawn on the world,
plus a rule table saying what grows inside it. Everything a region places is baked
into the scene as ordinary prefab instances under the region's `Generated` child,
so the result can be hand-edited, and a server can load the same layout.

- **`RegionRuleSet`** (asset, `World/Regions/*.asset`): rules run top to bottom.
  Each rule has prefab *variants* (like SpeedTree seeds), density, footprint radius,
  clustering, slope and height limits, sink, size/yaw/tilt variance, conform-to-slope,
  an *Is A Tree* flag, and an optional parent. A child rule spawns in a ring of
  *Radius From Parent* around each placed parent: ferns and leaf litter under oaks,
  mushrooms on fallen logs, rubble around boulders. *Patchiness* is an Aelid
  addition that modulates density with broad noise to make glades and drifts.
- **`RegionArea`** (component): outline, edge fall-off (density fades in from the
  border), priority, and override. Higher priority generates first and claims ground.
  An overriding region suppresses lower-priority regions inside it, crossfading over
  its fall-off band.
- **`RegionExclusion`** (component): keep-clear circles and capsule paths. The slice
  builder adds them for the village, pond, ruins, camp, field, roads, and on every
  hand-placed landmark and prop, so generation works around hand-made content.
- **`RegionGenerator`**: pure and deterministic. Top-level rules split the region into
  a world-anchored grid of 2 × radius blocks, and each block rolls density once.
  Every block and parent slot has its own hashed random stream, keyed by rule name,
  so moving a corner or reordering rules does not reshuffle the rest. Candidates then
  pass clustering, the outline and fall-off, overrides, keep-clear zones, slope and
  height, and footprint overlap. Two trees need `(rA + rB) × Tree Spacing`; a child
  only needs to clear its own parent's radius.
- **Editor:** **Aelid → World → Generate Regions / Clear Generated Objects**, or the
  buttons on any `RegionArea` (outline corners are draggable in the Scene view).
  Generate always rebuilds every region, because they share ground. **Aelid → World →
  Reset Slice Region Rules** restores the code defaults; otherwise scene rebuilds
  keep Inspector tuning. **Aelid → World → Install Slice Regions in Open Scene**
  (batch: `VerticalSliceBuilder.InstallRegionsFromCommandLine`) swaps the old
  `Trees`/`Rocks`/`Bushes` scatter for regions without a full rebuild, leaving the rest
  of the scene alone. That is how the current `VerticalSlice.unity` got its regions.

The slice has three regions. **Northwood** (north and west, override, 10 m fall-off)
is the oak forest: groves with ferns, litter and mushrooms, rare ancient oaks,
deadfall, understory bushes, fern beds and mossy rocks. **Heath** (the whole map,
lowest priority) is open country: clustered copses, flowering bushes, boulders with
rubble, wildflower drifts and tall grass. **Highlands** (the whole map, height-gated)
adds faceted crag boulders with scree, slabs on slopes, brambles and windswept oaks
on the rising rim. It replaces the earlier random scatter of blob trees, bushes and
rocks; the crag boulders reuse those rock meshes and the Stone shader preset.

Every tree in the slice is one of the four Oak Grove oaks (Broadcrown, Splitfork,
Windlean, Ancient), either a hand-placed landmark or a generated `Forest_Oak_*`
prefab. The old procedural pine and blob-oak meshes and materials are gone.

Result (2026-11-11): 1,182 objects: Highlands 368, Northwood 370, Heath 444. That
includes 109 oaks, against 230 blob trees before, plus understory, deadfall, rocks,
flowers and grass that the old scatter did not have. Each Generate logs, for every
rule, the number placed out of the candidates tried and why the rest were dropped
(outline, fall-off, override, keep-clear, no ground, slope, height, overlap). That is
the first place to look when a rule places too little.

Limits: footprints are circles, so long logs use most of their half-length as
radius. Generate replaces every generated object, so hand-edit only objects outside
`Generated`, or move them out first. Oblivion also painted ground textures under
objects and spawned grass at runtime from ground textures; neither is built here.

Validation: nine edit-mode generator tests: determinism, the density/radius count,
slope and height limits, child rings, no overlaps with crown spacing, edge fall-off,
keep-clear zones, override, and clustering. One play-mode scene test checks that the
regions are generated and that nothing is placed in the village. Review captures of
the forest, rim and heath were rendered in batch mode.

## Broken Cloister

The prefab northeast of the pond uses the 38-piece Broken Cloister layout from
the modular ruin kit. Its stone paving and masonry have mesh colliders. Ivy uses
the foliage glyphs, and the stonework uses the stone glyphs.

## Ruins paving (Unity validation pending)

`RuinFloorTiles` adds six repeating stone treatments to the existing 14 paving
positions: lightly worn slabs, partly buried stones, lifted edges, long narrow
slabs, chipped corners, and split slabs with open diagonal seams. Heights and
tilts stay small enough for the player's 0.4 m step offset. The tiles remain in
`Ruins_Rubble` with its existing Stone surface class, material, and mesh collider;
they add no renderers or scene objects.

The builder consumes the same random values as before, so the new shapes do not
reshuffle vegetation or other scenery. The generated rubble mesh can be updated
without changing `VerticalSlice.unity`, keeping independently placed vines and
props intact. A full scene rebuild still overwrites hand edits; avoid it when
merging custom scenery.

Geometry tests cover closed, outward-facing stones, bounded step heights, and
repeatability. Unity import, play tests, and visual capture status should be
checked separately before treating the new paving as validated in game.

## Tests and tools

- **Edit mode (47):** glyph ramps are monotonic, atlas layout, rendering-layer tagging, cell-size scaling, snap math over 200 random positions, reflection mirror math, the reflection camera's setup, and ripple expiry; plus ruin paving geometry and repeatability, ground materials and layout export, the region generator, cloud shadows (tiling, coverage, opacity, cookie assignment), and volumetric fog (shader compiles, phase normalisation, light clearing, off by default, renderer and profile setup).
- **Play mode (11):** loads the real scene and drives it with virtual input. Covers movement, sprinting, camera follow and framing lock, snap offsets, wading, morning mist, the night-only moon reflection, the ASCII toggle, and generated regions. Any logged error fails the run.
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
