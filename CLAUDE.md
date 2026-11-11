# Aelid: notes for AI assistants

- Read `docs/VISION.md` (creative brief, owned by the creative director) and `docs/ARCHITECTURE.md` before changing systems. Never silently change the vision: fixed isometric camera, hybrid ASCII look (a 3D game with an ASCII effect), server-authoritative design.
- Unity 6.6 (6000.6.8f1), URP 17.6 using the **RenderGraph** API only (no Compatibility Mode), Input System only (`activeInputHandler: 1`).
- Each module is its own asmdef. Keep `Aelid.Gameplay` free of rendering references and `Aelid.Rendering` free of gameplay references.
- `PlayerMotor` must only consume `PlayerIntent` + dt (future server-side validation).
- ASCII surface classes use Rendering Layer bits 1-7. Change `AsciiSurfaceLayers`, `AsciiGlyphAtlas` vocabularies, and `AsciiSurfaceId.shader` together.
- Camera textures in URP intermediates may be stored bottom-up. Shaders use `_AsciiOrientation` (queried via `GetTextureUVOrigin`), never `UNITY_UV_STARTS_AT_TOP`.
- Verify visual changes with `AsciiCaptureTool.BuildAndCaptureFromCommandLine` (batch mode; writes `Captures/` + `stability.txt`). Run EditMode and PlayMode tests after code changes. Batch mode fails if the user has the project open in the Editor.
- The first render after loading a scene in batch mode has a stale post grade; the capture tool renders a warm-up frame.
- Water look lives in `Water.shader` property defaults (the builder resets `Water_Pond.mat` to them). Reflection cameras are `CameraType.Reflection` and must stay skipped by the ASCII pass.
- `PlayerMotor` sets `CharacterController.minMoveDistance = 0`; the default drops moves at high frame rates.
