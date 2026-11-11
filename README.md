# Project Aelid

An open-world fantasy MMORPG in **Unity 6 (URP)**: a fully 3D low-poly world rendered through a custom **hybrid ASCII** shader, seen from a fixed isometric camera. Souls-like combat, Elder Scrolls-style exploration, Asciilands' look, and Berserk's darkness.

- Creative brief: [docs/VISION.md](docs/VISION.md)
- Technical architecture and status: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)

## Status: Milestone 2, weather prototype

- Hybrid ASCII renderer (URP RenderGraph feature): dark terminal-style look where glyph colour carries the lighting, per-surface glyph vocabularies (thin grass blades, letter-textured stone), flower/firefly sparkles, edge line glyphs, and characters kept in full 3D
- Glyph-stable camera: 0.00% glyph change while scrolling (measured; ~42% without it)
- Fixed-angle isometric follow camera with zoom
- Controllable character (walk/run) with placeholder procedural animation
- Day/night cycle: bright days, cold moonlit nights, warm firelight
- Generated test area: village, wheat field, forest, pond, hilltop ruins
- Water: planar reflections, ripple rings around wading characters, depth-blended shorelines, line-glyph water
- Weather: clear, rain, thunderstorms, snow, and snowstorms with gradual transitions, stronger storm gusts, and lightning flashes
- Automated tests for rendering, movement, water, day/night, and storm wind (edit mode + play mode)

Combat, enemies, and multiplayer are planned in the [architecture roadmap](docs/ARCHITECTURE.md#proposed-next-milestones).

## Open it

1. Unity Hub → **Add** → **Add project from disk** → select this folder. Use editor **6000.6.8f1** (Unity 6.6).
2. Open `Assets/_Project/Scenes/VerticalSlice.unity` and press **Play**.

| Input | Action |
|---|---|
| WASD / arrows / left stick | Move |
| Shift / B (gamepad) | Run |
| Mouse wheel | Zoom |
| F1 | Toggle ASCII (compare with the raw 3D view) |
| F2 / F3 | Glyph density finer / coarser |
| F4 | Toggle glyph snapping (shows the shimmer it prevents) |
| F5 | Orthographic / perspective |
| F6 | Cycle clear → rain → thunderstorm → snow → snowstorm |
| T | Fast time (×60) |
| [ / ] | Time −1 h / +1 h |
| H | Hide the HUD |

**Tuning the look:** select `Environment/Global Volume` (day) or `Night Volume` and edit the **Aelid/ASCII** override. Lighting colors over the day are gradients on `Environment` → `Day Night Cycle`.

**Reviewing weather:** walk to the wheat field southwest of the village and cycle weather with **F6**. Give each preset three seconds to settle. **F1** compares ASCII grass bending with the movement of the 3D wheat, bushes, and tree crowns. On `Environment` → **Weather Controller**, set **Automatic Change Seconds** to zero for manual testing.

## Repository layout

```
Assets/_Project/
  Scripts/{Rendering,Environment,Gameplay,Debugging,Editor}   one assembly each
  Shaders/            Ascii.shader (3 passes), AsciiSurfaceId.shader
  Scenes/             VerticalSlice.unity (generated)
  Generated/          meshes and materials from the scene builder
  Settings/           volume profiles
  Tests/{EditMode,PlayMode}
Assets/Settings/      URP assets (ASCII feature registered on both renderers)
docs/                 vision and architecture
```

## Command-line workflow

These commands need the project to be closed in the Editor. Unity allows one instance per project.

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe"
# Regenerate the scene and write review screenshots + stability report to Captures/
"$UNITY" -batchmode -projectPath . -executeMethod Aelid.EditorTools.AsciiCaptureTool.BuildAndCaptureFromCommandLine -logFile build.log
# Tests
"$UNITY" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
"$UNITY" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

Inside the Editor, the same tools are under the **Aelid** menu.
