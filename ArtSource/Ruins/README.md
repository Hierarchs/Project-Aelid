# Aelid modular ruins

Open `Aelid-Modular-Ruin-Kit.blend` for **Gate Above the Ravine**, **Broken
Cloister**, and the complete **Modular Kit** catalog. The 27 module meshes are
available under **Asset Browser > Current File**.

## Place the kit in Unity

- Drag individual FBX models from `Assets/_Project/Art/Ruins/Models` into a scene.
  Import settings use the FBX file units. Most modules fit a 2 m placement grid.
  Arches and the sealed gate span 4 m bays. Stairs run 2 m and rise 1 m.
- The FBX material slots reference shared URP materials in
  `Assets/_Project/Art/Ruins/Materials`. The cyan seal uses an emissive material.
- Add **Aelid > Rendering > ASCII Surface** to placed pieces. Use **Stone** for
  masonry, **Foliage** for ivy, and **Emissive** for the cyan seal.
- Place the seal in front of the sealed gate slab at a height of 2.20 m.
  Add colliders as needed for your assembled route.

The Blender file includes example terrain, trees, and lighting for layout
reference. The Unity project contains the individual architectural modules.
