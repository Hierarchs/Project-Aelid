# ASCII shader review — November 11, 2026

The user prefers the coherent appearance of the [visual references](visual-references/README.md)
and reports excessive noise in the actual shader. Those references are generated
mockups; actual Unity captures are needed to evaluate progress toward them.

## Keep

- Real 3D lighting, shadows and depth.
- Separate glyph vocabularies for materials.
- Readable characters through the hybrid surface exemption.
- Camera-grid stabilization for the fixed isometric view.

## Noise suspects identified in the code

- Global dithering is 0.7, amplified by 3.2 for grass and 2.6 for stone.
- Per-cell brightness jitter is 0.28, allowing approximately ±28% variation.
- Minimum density ramps of 0.65 for grass and 0.55 for stone retain dense glyphs
  through much of the shadow range.
- Material grain, fractures, relief normals, glyph variation, sparkles and edge
  detection can contribute detail simultaneously.
- Cell reduction averages pixels within a cell; neighboring cells still make
  largely independent color and character decisions.
- Normal-based edge detection can turn small surface changes into strong strokes.

These are code-based hypotheses about the reported appearance. The recent ground
and stone changes passed compilation checks but have not been inspected in Unity.

## Proposed next work

1. Capture the current renderer from fixed views at gameplay zoom: daylight,
   night, and a short camera pan. Use identical resolution and lighting for each
   comparison. A local licensed editor can provide these while cloud licensing
   remains unavailable.
2. Create a clean diagnostic preset: zero dithering, brightness jitter and
   sparkles; minimal material grain and relief; silhouette edges first. Retain
   the current settings as a comparison preset.
3. Preserve larger lighting/color shapes through gentle grouping or filtering
   within the same surface. Protect silhouettes and material boundaries. Trial
   restrained brightness/color bands.
4. Group glyph variation into coherent grass clumps, stone plates and cliff
   layers. Use compatible glyph families within each patch and keep patterns
   anchored to the world.
5. Restore detail selectively. Retune shadow density to provide quiet areas.
   Emphasize silhouettes and major cracks; suppress fine relief in edge detection.
6. Compare standing still, walking, zooming, and time-of-day changes. Check that
   the player, paths, objects and shadows remain readable and that camera
   stability is retained.

This cleanup was discussed and deferred until the user returns home. It has not
been implemented. Dense lettering remains part of the desired style; the goal
is coherent detail and readable shapes. Further material detail should wait
until that hierarchy has been evaluated with actual captures.

## Existing implementation

- `148e4e7`: Oak Grove models, leaf atlas, source and documentation.
- `941de3c`: Varied ruins paving and saved mesh update.
- `4a9cb82`: Grass/dirt textures, shared transitions and ASCII coverage.
- `879c22a`: Layered cliff and fractured stone surface materials.

See [ARCHITECTURE.md](ARCHITECTURE.md) for implementation and validation details.
