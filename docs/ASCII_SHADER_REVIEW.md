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
- Minimum density ramps of 0.66 for grass and 0.46 for stone retain dense glyphs
  through much of the shadow range.
- Material grain, fractures, relief normals, glyph variation, sparkles and edge
  detection can contribute detail simultaneously.
- Cell reduction averages pixels within a cell; neighboring cells still make
  largely independent color and character decisions.
- Normal-based edge detection can turn small surface changes into strong strokes.

## Initial Unity comparison

F6 in Play Mode switches to the clean diagnostic baseline. It disables glyph
dithering, brightness jitter, grass sparkles and normal-based edge strokes. Ground
microdetail and stone grain run at 15% of their material settings, and stone relief
normals are disabled. Depth edges, world-anchored patches and the existing shadow
density are available for comparison with the current look.

`AsciiCaptureTool.CaptureBaselineFromCommandLine` captures the saved Hilltop Ruins
scene at gameplay zoom and 1920 × 1080. It saves a pair at 14:00, a pair at 01:00,
and a 24-frame horizontal pan in daylight. The first comparison ran on Direct3D 12
and is in `Captures/AsciiBaseline/`.

Grass clumps and stone lettering read more consistently in the clean captures.
Several pillar faces lose definition with depth edges alone, and the player is
still hard to pick out at night. Both modes stayed stable in this fixed-zoom pan.

## Proposed next work

1. Preserve larger lighting/color shapes through gentle grouping or filtering
   within the same surface. Protect silhouettes and material boundaries. Trial
   restrained brightness/color bands.
2. Group glyph variation into coherent grass clumps, stone plates and cliff
   layers. Use compatible glyph families within each patch and keep patterns
   anchored to the world.
3. Restore detail selectively. Retune shadow density to provide quiet areas.
   Emphasize silhouettes and major cracks; suppress fine relief in edge detection.
4. Compare standing still, walking, zooming, and time-of-day changes. Check that
   the player, paths, objects and shadows remain readable and that camera
   stability is retained.

Dense lettering remains part of the desired style. Further material detail should
wait until the larger lighting and color shapes read clearly in the renderer.

## Existing implementation

- `148e4e7`: Oak Grove models, leaf atlas, source and documentation.
- `941de3c`: Varied ruins paving and saved mesh update.
- `4a9cb82`: Grass/dirt textures, shared transitions and ASCII coverage.
- `879c22a`: Layered cliff and fractured stone surface materials.

See [ARCHITECTURE.md](ARCHITECTURE.md) for implementation and validation details.
