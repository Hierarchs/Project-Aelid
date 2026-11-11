# Project Aelid

An open-world fantasy MMORPG in **Unity 6 (URP)**: a fully 3D low-poly world rendered through a custom **hybrid ASCII** shader, seen from a fixed isometric camera. Souls-like combat, Elder Scrolls-style exploration, Asciilands' look, and Berserk's darkness.

- Creative brief: [docs/VISION.md](docs/VISION.md)
- Technical architecture and status: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)

## Status: Milestone 1, visual foundation (working)

- Hybrid ASCII renderer (URP RenderGraph feature): a midnight-blue background, clumped grass strokes, mixed stone lettering, restrained water lines, and an outlined player silhouette
- Fixed-angle isometric follow camera with a locked zoom and world-aligned glyphs
- Playable low-poly swordsman with procedural idle, walk, and run animation
- Day/night cycle with morning mist, cold moonlight, and warm firelight
- Generated test area: village, wheat field, forest, pond, hilltop ruins
- Water: planar reflections, a rippling moon reflection, ripple rings around wading characters, depth-blended shorelines, line-glyph water
- 30 automated tests (20 edit mode + 10 play mode)

Not built yet: weather, combat, enemies, multiplayer (see the roadmap in the architecture doc).

## Open it

1. Unity Hub → **Add** → **Add project from disk** → select this folder. Use editor **6000.6.8f1** (Unity 6.6).
2. Open `Assets/_Project/Scenes/VerticalSlice.unity` and press **Play**. The scene starts at nightfall.

| Input | Action |
|---|---|
| WASD / arrows / left stick | Move |
| Shift / B (gamepad) | Run |
| F1 | Toggle ASCII (compare with the raw 3D view) |
| F2 / F3 | Glyph density finer / coarser |
| F4 | Toggle glyph snapping (shows the shimmer it prevents) |
| T | Fast time (×60) |
| [ / ] | Time −1 h / +1 h |
| H | Toggle the developer HUD |

**Tuning the look:** select `Environment/Global Volume` (day) or `Night Volume` and edit the **Aelid/ASCII** override. Lighting colors over the day are gradients on `Environment` → `Day Night Cycle`.

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
