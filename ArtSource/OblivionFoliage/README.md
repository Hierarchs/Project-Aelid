# Oblivion-style Modular Foliage Pack

22 game-ready props inspired by the Cyrodiil forests of *The Elder Scrolls IV: Oblivion*, styled to sit
beside Aelid's [Oak Grove kit](../OakGrove/README.md). Everything was generated procedurally in Blender 5.3.
Trees come from Oak Grove.

## Oak Grove style match
The colours are sampled from `Aelid-Oak-Grove.blend` and hard-coded at the top of `build_foliage.py`
(`OAK_LEAF`, `OAK_BARK`, `OAK_STONE`, `BLUEBELL`, `TRAIL`).

| Oak Grove | This pack |
|---|---|
| Leaf sprays: wavy-edged leaves in flat faceted bands, pale midribs, pale olive twigs, `Oak \| Leaf 1-6` greens | Same leaf style and palette across the bush, ivy, fern, grass and flower cards (`T_Foliage_Atlas.png`) |
| Trunks: flat-shaded low-poly, `Oak \| Bark 1-5` per face | Logs and stump: flat-shaded low-poly, the same bark tones in long bands, faceted tan cut ends |
| Stones: flat-shaded, one `Grove \| Stone 1-4` colour each | Rocks: flat-shaded low-poly, one grove stone colour per rock, mossier tops |
| Bluebells, trail browns | Flower patch and leaf litter |

The ground plants (grass, flowers, mushrooms, leaf litter) are sized so they still read as 8 px ASCII cells at gameplay zoom.

**Rebuild straight into the project** (from the project root, with Unity closed or not yet importing):

```bash
blender -b --python ArtSource/OblivionFoliage/build_foliage.py -- ArtSource/OblivionFoliage Assets/_Project/Art/Environment/OblivionFoliage
```

Then run **Tools > Oblivion Foliage > Set Up Materials and Prefabs** in Unity.

**Side-by-side check with a real oak:** set `OAK_BLEND` to `ArtSource/OakGrove/Aelid-Oak-Grove.blend` and run
`render_previews.py`. Oak_Broadcrown joins the bushes row and Oak_Windlean joins the logs row.

| Category | Assets |
|---|---|
| Bushes | `SM_Bush_Broadleaf_Round`, `SM_Bush_SmallLeaf_Tall`, `SM_Bush_Flowering`, `SM_Bush_Bramble_Dry` |
| Logs | `SM_Log_Fallen_Long` (4 m, broken end), `SM_Log_Hollow`, `SM_Log_Short`, `SM_Stump` |
| Rocks | `SM_Rock_Boulder_Large`, `SM_Rock_Medium`, `SM_Rock_Small`, `SM_Rock_Slab`, `SM_Rock_Cluster`, `SM_Rock_Pebbles` |
| Vines | `SM_Vine_Hanging` (drapes over a ledge), `SM_Vine_Ivy_Wall`, `SM_Vine_Ground_Creeper` |
| Plants / ground | `SM_Fern`, `SM_Grass_Clump`, `SM_Flower_Patch`, `SM_Mushroom_Cluster`, `SM_Ground_Debris` |

## Files
- **`Exports/OblivionFoliage.unitypackage`:** the whole Unity kit in one file: models, textures, materials, prefabs, wind shaders and scripts. Import it with **Assets > Import Package > Custom Package…**.
- **Unity kit:** lives in the project at `Assets/_Project/Art/Environment/OblivionFoliage/`. It holds Models (FBX with `_LOD0`, `_LOD1` and `_Collider`), Textures, the wind shaders and the setup script. See its `README_Unity.md`.
- `Exports/GLB/*.glb`: one file per asset, textures embedded. Godot, Unreal and Unity (with glTFast) import these directly.
- `Exports/FBX/*.fbx`: one file per asset, textures embedded, scale applied (1 unit = 1 m).
- `Textures/`: `T_Foliage_Atlas.png` (RGBA, shared by every leaf, grass, ivy and flower card) plus baked albedo maps for rocks, logs and the stump.
- `FoliagePack.blend`: all assets marked as **Blender assets**, so you can drag them from the Asset Browser. The `Preview_Scene` collection only holds the ground, ledge and wall used for the preview renders.
- `Previews/`: renders of each category.
- `build_foliage.py` / `render_previews.py`: the generators. Change sizes, seeds or colors and run again:
  `blender -b --python build_foliage.py -- <out_dir>`

## Modular conventions
- Units are metres, +Z is up, all transforms are applied, and names use the `SM_` prefix.
- **Pivot** sits on the ground contact point, centred, so assets drop straight onto terrain.
  - `SM_Vine_Hanging`: the pivot is the **top front edge of a ledge**. The vine drapes over +Y and hangs down toward -Y.
  - `SM_Vine_Ivy_Wall`: the pivot is bottom-centre. It lies on the XZ plane and faces -Y. Place it flush against a wall.
- Rocks and logs are sunk slightly below their pivot so they sit naturally on uneven ground.
- All foliage cards share **one material and one atlas**. That keeps draw calls low when you scatter a lot of them.

## Material setup in your engine
- `M_Foliage`: alpha **cutout/mask** with a cutoff of 0.5, **double-sided**. The glTF files already carry this setting. For FBX, set the material to Alpha Clip / Masked and Two-Sided yourself.
- Foliage cards have **custom normals pointing outward from the bush centre**. That gives the soft, rounded shading Oblivion's shrubs have instead of flat cards. Keep imported normals on and don't recalculate them.
- Rocks, logs and stump are flat-shaded and use one baked albedo map each, holding their per-face colours. There are no normal maps: the facets carry the form, as in Oak Grove. `bake(obj, size, normal=True)` brings them back if needed.
- Poly counts are low: bushes 130–280 tris, rocks 300–1900, logs 260–600.
- The Unity FBX files add a LOD1 at roughly 40–70% of LOD0 and, for rocks, logs and the stump, a convex collision hull of 70–250 tris.

- Foliage meshes carry wind weights in a `Wind` vertex color (R = bend, G = phase, B = flutter). The Unity package's shaders use them for wind sway.

## Not included yet (good next steps)
- Wind on HDRP
- Trees
