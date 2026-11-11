# Oblivion Foliage: Unity setup

## Install from the package (recommended)
The kit ships as `OblivionFoliage.unitypackage` (in Aelid: `ArtSource/OblivionFoliage/Exports/`).
1. In Unity, choose **Assets > Import Package > Custom Package…** (or double-click the file) and import everything.
   It lands in `Assets/_Project/Art/Environment/OblivionFoliage/` with materials and prefabs ready for URP.
2. **Outside Project Aelid**, or in a Built-in or HDRP project: click **Tools > Oblivion Foliage > Set Up Materials and Prefabs** once.
   - This rebuilds the materials for your pipeline.
   - Until you do, the prefabs show one missing script: Aelid's ASCII surface tag. Setup removes it in projects without Aelid.
3. Use the prefabs in `OblivionFoliage/Prefabs/`.

To make a new package after changing the kit, use **Tools > Oblivion Foliage > Export Package**. It runs the setup first, so the materials and prefabs are current. From the command line:
`Unity -batchmode -projectPath <project> -executeMethod OblivionFoliageExport.ExportFromCommandLine [-exportPath <file>]`

## Install by copying the folder
1. Copy this whole `OblivionFoliage` folder into your project's `Assets/` folder.
2. Wait for Unity to finish importing.
3. Click **Tools > Oblivion Foliage > Set Up Materials and Prefabs**.
4. Use the prefabs in `OblivionFoliage/Prefabs/`.

Optional: **Tools > Oblivion Foliage > Create Demo Scene** lays out every prefab in a test scene.

The setup detects your render pipeline (URP, HDRP or Built-in) and creates matching materials. If you switch pipelines later, run it again; it updates the existing materials in place.

Tested in Unity 6000.6.8f1 with a URP project (the URP Blank template) and a Built-in project. HDRP is supported in the script but hasn't been tested.

Each prefab cross-fades between its LODs, so for about half a second after it switches you'll see a dithered pattern on the leaves. That's normal. To get a hard switch instead, set the LOD Group's Fade Mode to None.

## What the setup does
- **Textures:** the foliage atlas gets *Mip Maps Preserve Coverage* (so leaves don't thin out in the distance) and Clamp wrap. Any `_Normal` textures are flagged as normal maps. The current build has none, because the rocks and wood are flat-shaded to match Oak Grove.
- **Materials:**
  - `M_Foliage` is alpha-clip at 0.5, two-sided, and uses the AlphaTest queue.
  - Rocks, logs and the stump use their baked per-face colour texture. Re-running setup clears normal maps left over from older builds.
- **Models:** imports the authored normals (that's what gives the bushes their soft, rounded shading), generates Mikk tangents, and maps the FBX materials to the materials above.
- **Prefabs** (prefab variants of the models):
  - **LOD Group:** LOD0 → LOD1 → culled, with cross-fade.
  - **Collision:** rocks, logs and the stump get a convex `MeshCollider` on the hidden `_Collider` child. Bushes, vines and plants have no collision on purpose, so players walk through them like in Oblivion.

## Wind sway (URP and Built-in)
Bushes, vines, fern, grass and flowers sway in the wind. Stems stay anchored at the ground, tips bend the most, and leaves flutter. Each placed copy moves out of step with its neighbours, so a field of bushes doesn't sway in lockstep.

- **Per material:** select `M_Foliage` (leaves) or `M_Twig` (stems) and adjust *Wind Direction*, *Bend Strength* (metres of movement at the tips), *Bend Speed*, *Leaf Flutter* and *Flutter Speed*. Keep the two materials' bend settings matched so the stems stay inside their leaves.
- **Scene-wide:** add **Oblivion Foliage > Foliage Wind** to any GameObject (the demo scene has one). Its *Strength* multiplies every material, from 0 (calm) to 1 (default) to 3 (storm). Assign a Unity **WindZone** and it follows that zone's direction, *Main* and *Turbulence*, so you can animate weather in one place.
- The wind data is baked into the meshes' vertex colors:
  - R: how far each point bends.
  - G: its timing offset.
  - B: how much its leaves flutter.

  If you edit the meshes, keep the vertex colors.
- In URP the wind shader is installed by the setup step, from `Shaders/URPFoliageWind.shader.txt`. It includes a soft backlight, so leaves glow when the sun is behind them.
- HDRP: the foliage uses HDRP/Lit with no wind.

## Placement notes
- Pivots sit on the ground contact point, so prefabs snap to terrain (hold V for vertex snapping).
- **SM_Vine_Hanging:** the pivot is the ledge's top front edge. It drapes back over the ledge (-Z) and hangs down on the +Z side.
- **SM_Vine_Ivy_Wall:** flat ivy that faces +Z. Place it against a wall and rotate it to face outward.
- For terrain painting, the prefabs work with **Terrain > Paint Trees/Details** as detail meshes. Rocks and logs are better placed by hand or with a scatter tool.
- Static batching: tick **Static** on rocks and logs you won't move.

## Built-in pipeline only
The two-sided leaf shader with wind is `OblivionFoliage/Builtin Leaves Two-Sided`, in `Shaders/`. URP and HDRP projects ignore it.
