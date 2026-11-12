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

The Vertical Slice uses the four oak models throughout **World → Trees**.
Four individual landmarks sit under **World → Oak Grove Trees**. Broadcrown
stands by the camp, Windlean by the pond, Splitfork by the ruins, and Ancient
at the forest edge.

The tree FBXs map to [Unity materials](../../Assets/_Project/Art/Environment/OakGrove/Materials/)
with vertex-color wind. The leaves use the cutout atlas for color, shadows,
depth, and ASCII surface coverage. Bark uses the Wood glyphs and leaves use
the Foliage glyphs.
