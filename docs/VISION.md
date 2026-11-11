# Project Aelid: Vision

*Creative brief from the creative director. Technical work must not silently change this vision.*

## 1. Game vision

An ambitious, open-world, 3D fantasy MMORPG built with Unity 6 and C#. It combines:

- **Dark Souls / Elden Ring**: deliberate, skill-based melee combat, stamina management, dodging, parrying, challenging enemies, and meaningful boss encounters.
- **The Elder Scrolls (Oblivion / Skyrim)**: open-world exploration, immersive NPCs, factions, dynamic settlements, dungeons, player freedom, and environmental storytelling.
- **Asciilands**: a visually distinctive world rendered through colorful ASCII characters, with persistent online gameplay and player interaction.
- **Berserk**: dark fantasy themes, terrifying monsters, supernatural events, and an unsettling atmosphere.

The goal is a beautiful, dangerous, living fantasy world that players can freely explore, inhabit, and influence. Aelid is separate from any other game in development.

## 2. Graphics and art direction

The game uses real low-poly 3D models: characters, environments, buildings, monsters, weapons, and equipment. The final image passes through a custom **hybrid ASCII shader**. The shader turns much of the 3D world's appearance into dense, colorful ASCII characters while keeping the underlying geometry, animation, lighting, and depth.

- Dense ASCII glyphs form terrain, architecture, vegetation, and environmental details.
- Player characters and important enemies keep readable silhouettes.
- Dynamic lighting affects the 3D scene before ASCII conversion.
- Water supports reflections and animated surface effects.
- Spells, fire, and important combat indicators may use reduced ASCII for clarity.
- ASCII density, intensity, color, and character readability should eventually be configurable.
- Glyph flickering and instability during camera movement should be minimal.

This is **not** a 2D ASCII game. It is a fully 3D game rendered with an ASCII visual effect.

### Camera

A fixed-angle, top-down, isometric-style camera like Asciilands. It shows the player's character from above and behind at an elevated angle, **not** a traditional over-the-shoulder third-person camera. It follows the player smoothly at a consistent angle and orientation. Limited zoom may come later; player-controlled rotation is not a priority. The view should show the depth of the 3D environment while keeping combat clearly visible.

## 3. Lighting, weather, and atmosphere

**Daytime: Oblivion-inspired fantasy.** Bright, magical sunlight; colorful forests and lush green fields; beautiful rivers, mountains, and towns; golden morning light; dynamic shadows; vibrant environments. A feeling of freedom, wonder, and exploration.

**Nighttime: Berserk-inspired dark fantasy.** Cold moonlight and deep shadows; dangerous forests and ominous ruins; supernatural creatures and events; dense fog; firelight and magical illumination. A feeling of danger and isolation.

**Dynamic weather:** clear skies, cloudy/overcast, light and heavy rain, thunderstorms and lightning, light snow and blizzards, fog and mist, dynamic wind. Weather should change the environment visually, and may later affect gameplay, visibility, NPC behavior, and encounters. Snow should build up on terrain and objects where technically feasible. All of this uses URP with a centralized lighting and weather controller.

## 4. Open-world design

A large, connected, freely explorable world: towns, cities, villages, and wilderness; forests, plains, swamps, deserts, and snowy mountains; caves, ruins, crypts, castles, and dungeons; hidden locations and secrets; roads, rivers, lakes, and oceans; regions that get harder as you go; distinct cultures, factions, and settlements. Exploration should reward players without depending only on quest markers. The long-term aim is a persistent world that player actions and simulated events can change.

## 5. Combat

Real-time, deliberate, and skill-based: light and heavy attacks, directional movement and aiming, stamina management, blocking and parrying, dodging with invulnerability frames, weapon-specific movesets, poise and stagger, enemy attack telegraphs, challenging bosses, and melee, ranged, and magic combat. Combat is designed for the fixed-angle isometric view, not copied from third-person Souls games. Animations, hitboxes, movement, stamina, and combat states must stay in sync for multiplayer.

## 6. Dynamic world simulation

A believable, living world: NPC schedules and routines; merchants traveling between settlements; regional trade and supply chains; factions competing for influence; wildlife populations and territories; NPC relationships and reputation; dynamic encounters and world events; settlements that grow and regions that change; regional economies driven by supply and demand.

*Example:* bandits attack a caravan, its supplies never arrive, local prices move, and players get new opportunities.

These systems should combine into emergent gameplay rather than rely on scripted events. Distant regions use lightweight simulation; expensive real-time AI is reserved for areas near players.

## 7. MMORPG features

Persistent online characters, cooperative exploration, parties and guilds, player trading, equipment and loot, crafting and gathering, a player-driven economy, shared world events, group dungeons and world bosses, and a persistent world economy. The design is **server-authoritative**: the client handles input, presentation, animation, and rendering, and the server controls combat validation, inventories, transactions, and persistent world state. Development starts with small multiplayer tests, not massive player counts.

## 8. Technical direction

| | |
|---|---|
| Engine | Unity 6 |
| Language | C# |
| Rendering | Universal Render Pipeline (URP) |
| Visual system | Custom hybrid ASCII shader |
| World | Fully 3D, low-poly assets |
| Camera | Fixed-angle top-down / isometric |
| Multiplayer | Authoritative networking |
| Development | Modular, incremental, AI-assisted |

Major systems are independent, maintainable components. Performance and scalability are priorities. World simulation, rendering, combat, and persistent data stay loosely coupled.

## 9. Development philosophy

The creative director sets art direction, design decisions, gameplay ideas, and testing feedback. Claude handles technical architecture, coding, debugging, implementation, and iteration. Build working systems in steps with clear milestones and testable results; do not build the whole MMORPG at once.

**Initial vertical slice:**

1. A low-poly 3D environment
2. A fixed-angle isometric player camera
3. A controllable, animated character
4. A working hybrid ASCII rendering shader
5. Dynamic daylight and nighttime lighting
6. Rain and snow weather systems
7. Basic combat with one enemy
8. A small, explorable area
9. Eventually, two-player multiplayer

The first priority is the visual foundation, the camera, player controls, and the rendering system.

## 10. Working agreement for the technical lead

- Prefer maintainable, modular C# architecture.
- Explain technical decisions and trade-offs.
- Consider performance, especially for the ASCII shader.
- Avoid unnecessary complexity early on.
- Build usable, testable implementations, not speculative systems.
- Keep the established art direction and camera perspective.
- Clearly separate working features from proposed ones.
- Never silently change the project's overall vision.
