# Oak Grove kit

Original files from `Aelid-Oak-Grove-Kit.zip`, preserved without modification.

| Files | Location |
|---|---|
| Four tree FBXs | [Unity tree assets](../../Assets/_Project/Art/Environment/OakGrove/FBX/Trees/) |
| Four leaf-spray FBXs | [Unity spray assets](../../Assets/_Project/Art/Environment/OakGrove/FBX/Sprays/) |
| 2048 × 2048 RGBA leaf atlas | [Unity atlas](../../Assets/_Project/Art/Environment/OakGrove/Oak-Leaf-Spray-Atlas.png) |
| Editable Blender source | [Aelid-Oak-Grove.blend](Aelid-Oak-Grove.blend) |
| Preview | [Aelid-Oak-Foliage-Preview.png](Aelid-Oak-Foliage-Preview.png) |
| Original kit notes | [START-HERE.txt](START-HERE.txt) |

The Blender source stays outside `Assets` so Unity imports the supplied FBXs
without also converting the source scene. Paths in the original notes refer to
the archive layout; use the locations above in this repository.

These assets have not been placed in the game scene or wired to Unity materials.
The notes describe the remaining alpha-clipped foliage material, vertex-color
wind, shadow/depth, and ASCII surface-pass integration. In-game appearance and
performance still require Unity validation.
