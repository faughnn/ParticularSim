# Sandy -- Brainstorm

Everything below is raw ideation. Quantity over polish. Cherry-pick what resonates, ignore the rest. Nothing here duplicates the existing roadmap (Fan, Magnet, Plate, Hinge, Motor, Weight, Spring, Grate, Trigger/Release, bucket types, sorting machines, chute/slide, hopper, gate/valve, airlock, buffer tank, splitter, waterwheel, windmill, pendulum, bucket elevator, catapult, trapdoor, wedge tile). If something echoes a roadmap item, it extends it in a direction not yet described.

---

## 1. New Materials and Material Properties

### Gravel
A coarse powder (density ~160, stability ~30) that forms steeper piles than sand and resists fan blowing (low airDrag). Interesting because it creates a second powder "tier" -- heavier and less mobile than sand, lighter than iron ore. Players could use density settling to separate gravel from sand in a water column. Also useful as cheap aggregate filler for building terrain bridges.

### Clay
A very high-stability powder (stability ~200) that barely slides at all -- almost static when dry. When adjacent to water cells it absorbs moisture and becomes Mud (liquid-like, low spread). When heated to ~150 it fires into Ceramic, a lightweight static material. This gives a three-stage processing chain (Clay + Water -> Mud -> heat -> Ceramic) and makes Clay a natural sealant for containing liquids before the player has walls.

### Mud
Liquid behavior but extremely high viscosity (spread 1, stability ~40). Denser than water so it sinks through it. Freezes into Clay at low temps. Interesting because it is the slowest-moving liquid -- players need to design wider channels and steeper drops to get it flowing, and it naturally clogs narrow passages, creating a terrain-sculpting tool.

### Saltwater
Liquid similar to water but density ~72 (slightly heavier). Boils at a higher temperature (110). When boiled, produces Steam and leaves behind Salt (powder, density ~90). This creates a desalination/evaporation puzzle where you separate salt from water using heat. Also, fresh water floats on saltwater, creating natural stratification the player can exploit for density-based sorting.

### Salt
Light powder (density ~90, stability ~5) that dissolves back into Saltwater when touching Water cells. Very low airDrag, so fans push it easily. Interesting because it is a material that cycles between states depending on context -- solid in dry areas, dissolved in wet areas. Creates emergent challenge: how do you transport salt past water without losing it?

### Acid
Liquid (density ~55) that corrodes materials with the Corrodes flag -- it slowly eats through soft metals and stone, converting them to a Sludge byproduct. Limited supply means it is a consumable puzzle resource. Interesting because it introduces irreversible material destruction as a mechanic -- the only way to remove stone barriers. Also creates tension: acid eats things you might not want it to, so containment and routing matter.

### Sludge
Waste byproduct from acid reactions. Very dense liquid (density ~180, spread 2) that is essentially useless. Interesting because it punishes sloppy acid use -- you have to deal with the growing volume of waste, route it away from your machines, and maybe store it somewhere. Creates a pollution/waste management minigame.

### Copper Ore
Powder (density ~170) with Magnetic flag false. Melts at ~160 (lower than iron). Smelts into Molten Copper, which freezes into Copper (static, Conductive flag). Interesting because it gives a second metal with different processing requirements -- lower melt point means simpler furnaces, but it cannot be magnetically separated from gangue, so the player needs a different sorting strategy (density settling or fan classification).

### Copper / Molten Copper
Copper is a static conductive material. Molten Copper is a liquid that freezes into Copper at ~120. Having two metals with different melt/freeze points means furnace design matters more -- a furnace hot enough for iron will also melt copper, so mixed-metal processing requires temperature control or pre-sorting.

### Sulfur
Powder (density ~80, stability ~10). Flammable at low ignition temp (~50). Burns into Smoke and releases a lot of heat while burning (exothermic flag, see Section 2). Interesting as a fuel that is easier to ignite than coal but burns faster. Could be mixed with coal in a furnace for faster ramp-up. Also relevant for explosive interactions (see gunpowder in Section 9).

### Charcoal
Powder produced when Wood burns slowly (see Wood below). Density ~60, Flammable, higher ignition temp than wood (~150) but burns longer. Interesting as a processed fuel -- the player could build a charcoal kiln (sealed furnace with wood) to produce better fuel for iron smelting. Processing chain: Wood -> (slow burn) -> Charcoal -> (burn in furnace) -> Ash + heat.

### Wood
Static material (density ~120) with Diggable and Flammable flags. When burned with abundant air, produces Ash and Smoke quickly. When burned in a low-oxygen enclosure, slowly converts to Charcoal instead. This makes wood a terrain material the player can dig AND a fuel source, and the oxygen-dependent burn behavior creates interesting furnace design challenges.

### Glass
Static material produced by melting Sand at ~250. Transparent (rendering flag, not physics). Cannot be dug. Very brittle -- when part of a cluster, fractures with minimal crush pressure. Interesting because sand is abundant and free, so the player can mass-produce glass as a building material, but it shatters easily under piston or cluster impact. Also, transparency means you can see through glass walls to monitor enclosed processes.

###Ite (Ite Ore -> Crystal)
A rare material found in specific level locations. IteOre is a heavy powder (density ~220) that is not magnetic and not flammable, making it hard to sort from rock. When heated to very high temps (~240), it transforms into Crystal -- a static material with unique visual properties (high emission, color-shifting). Crystal is the prestige material -- buckets that require Crystal are endgame challenges.

###Ite Dust
Produced when Crystal clusters fracture. A very light powder (density ~10, airDrag ~60) that drifts like ash. The only material lighter than ash, making it trivially fan-sortable. Could be used as an ingredient in advanced recipes. Interesting because it means Crystal is a non-renewable resource -- if you break it, you get dust that cannot be re-crystallized.

###Ite Liquid
When Crystal melts (at extreme temps, ~250), it becomes Ite Liquid -- a glowing, very dense liquid (density ~240) that can be cast into molds. Freezes back into Crystal. Interesting because it gives a path to reshape crystal, but requires the hottest possible furnace to achieve.

---

## 2. New Material Interactions and Chemistry

### Exothermic Reactions
Some burning materials (Sulfur, Coal, Oil) should emit heat into neighboring cells when burning, not just produce a burn product. Coal burning at its ignition point would heat adjacent cells by +2 per frame. This turns fuel into a self-sustaining heat source -- a furnace loaded with coal eventually ignites itself once the coal reaches ignition temp, and then the burning coal supplements the furnace heat, allowing faster smelting. Creates a natural positive feedback loop the player must manage (too much fuel = runaway temps that boil your water lines).

### Water + Molten Metal = Steam Explosion
When water contacts molten iron (or any molten metal), it should instantly vaporize into steam with a velocity burst in all directions. This is physically realistic (real foundries have catastrophic steam explosions from water-metal contact) and creates a genuine hazard in the game. Players must design furnace outputs that keep water and molten metal separated. Also opens up steam explosion as a deliberate tool -- water-dropping onto lava for impulse-based material launching.

### Acid Corrosion Mechanic
Acid liquid eats through materials flagged with Corrodes over time. Each frame of contact has a small probability of converting the adjacent corrode-able cell into Sludge and consuming one acid cell in the process. Rate depends on material density (lighter materials corrode faster). Interesting because it is a slow, probabilistic process -- the player needs to hold acid in contact with stone long enough to eat through it, which requires containment engineering.

### Alloy Mixing
When Molten Copper and Molten Iron are adjacent, they gradually merge into Molten Bronze (a new liquid). When Bronze freezes, it becomes a static material that is harder than either parent metal (higher fracture resistance in clusters, heavier). This creates an incentive to mix metals deliberately, and the player needs to figure out how to get both metals molten in the same container at the same time, which requires careful temperature management since they have different melt points.

### CalciumIte + Water =Ite Cement
If calcium-type materials exist, mixing powdered calcium with water could produce cement paste (very slow-moving liquid) that eventually hardens into a static solid. This gives the player a way to manufacture their own terrain -- build a mold out of walls, pour in cement, wait for it to set. Interesting because it inverts the normal puzzle dynamic: instead of removing terrain to create paths, you are building terrain to create barriers.

### Combustion Oxygen Model (Simplified)
Instead of tracking actual oxygen, use a "sealed enclosure" heuristic: if a burning material is fully surrounded by non-air cells (within a small radius), its burn rate drops dramatically. This makes charcoal production emergent -- put wood in a sealed box with minimal air, apply heat, and it chars slowly instead of burning away. No new materials needed, just a proximity check during burn simulation.

### Electrolysis (Far Future)
If an electricity system exists, passing current through Water could decompose it into Hydrogen (ultra-light gas) and Oxygen (heavier gas). Hydrogen is flammable and rises fast. Oxygen accelerates burning. This would be very late-game but creates an entire chemistry layer. Even without full electricity, a simple "electrode" structure could do this.

### Material Erosion from Flow
Fast-moving water (velocity above a threshold) should have a small per-frame chance of converting adjacent Ground or Stone cells into the corresponding loose material (Dirt or Sand). This makes rivers and waterfalls slowly erode terrain, which is both realistic and creates interesting long-term level dynamics. A player who routes a strong water flow past a stone wall will eventually wear through it.

###Ite Crystallization Seeds
Molten Ite freezing adjacent to existing Crystal cells should freeze into Crystal immediately rather than requiring cooling to the full freeze point. This is crystal seeding -- small Crystal fragments act as nucleation sites. Interesting because it means the player can control crystal growth direction by placing seed crystals strategically, almost like 2D crystal farming.

---

## 3. New Structure Primitives

### Pipe (Enclosed Channel)
An 8x8 structure block that is hollow inside (6x6 air interior with 1-cell walls). Materials flow through the interior but cannot escape sideways. Unlike open belts, pipes contain liquid and gas. Directional variants (horizontal, vertical, elbow, T-junction). Interesting because currently there is no way to transport liquids without them sloshing everywhere -- pipes make liquid routing predictable and contained.

### Sieve / Screen
An 8x8 block with a permeable surface: small particles (density below a threshold or a specific flag) pass through, large ones do not. Configurable by the mesh density at placement. Interesting because it creates a passive size-based sorting mechanism -- drop mixed powder onto a sieve, and light/fine material falls through while heavy/coarse material slides off. Combines with belts to create continuous screening operations.

### Nozzle / Funnel
An 8x8 block that narrows a flow from 8 cells wide to 2 cells wide (or 1). Useful for concentrating material into a single-cell stream for precise placement into buckets or other structures. Without this, players struggle to get material into narrow openings. The nozzle is passive -- gravity and pressure do the work -- but its shape constrains the flow.

### One-Way Valve
An 8x8 block that allows material to pass in one direction but not the reverse. Powder and liquid can fall through downward but not be pushed back up. Useful for preventing backflow in gravity-fed systems, ensuring material only moves forward through a processing pipeline. Simple concept, huge utility for machine reliability.

### Mixer / Agitator
An 8x8 block with a rotating internal element (driven by the same motor cycle as pistons). It stirs materials inside it, actively breaking up density stratification and forcing mixing. Without this, dense materials always sink to the bottom of a liquid column. The mixer fights that, keeping materials in suspension. Interesting for alloy production (keeping two molten metals mixed) and slurry transport.

### Heater Plate
A flat 8x1 structure (1 cell tall, 8 cells wide) that emits heat upward at a lower rate than furnace blocks but in a focused line. Cheaper and simpler than building a full furnace enclosure. Interesting for gentle heating tasks -- boiling water on a hot plate, keeping molten metal from freezing in a channel. Cannot reach iron-melting temps alone, so it does not replace the furnace for serious smelting.

### Drain
An 8x8 block placed in the ground that acts as a material sink -- but instead of destroying material, it teleports it to a linked output drain somewhere else. This is the game's first "wormhole" mechanic. Material conservation is preserved (cells move, not duplicated). Interesting because it solves the problem of waste disposal and long-distance transport in a way that is simple to use but expensive to unlock.

### Clamp / Gripper
An 8x8 structure that holds clusters in place when activated, preventing them from falling. Works with the cluster sleep system -- clamped clusters never enter physics, sitting motionless until released. Pairs with triggers for timed release mechanisms. Interesting for holding a weight at height and then dropping it onto a pile (stamp mill variant).

### Rail / Track
A 1-cell-wide static surface that constrains cluster movement to one axis (horizontal or vertical). Clusters touching a rail can only slide along it, not fall off sideways. Interesting because it turns clusters from unpredictable physics objects into reliable machine components -- a weight on a rail becomes a reliable linear actuator.

### Scaffold
A climbable static structure (like a ladder). The player character can ascend scaffolds to reach higher areas without building terrain ramps. Transparent to materials (powder falls through it). Interesting because currently the only way to reach high areas is to build dirt ramps, which is slow and uses up material.

---

## 4. Environmental and World Systems

### Wind
Ambient directional wind that varies by zone or time. Applies a gentle horizontal force to all cells with airDrag > 0, similar to a weak omnipresent fan. Some zones are calm, others have steady crosswinds. Interesting because it makes outdoor material transport harder -- sand blows off open belts, ash drifts sideways, smoke trails lean. Encourages players to enclose their machinery or align belt directions with the wind.

### Rain
Periodic water generation from the top of the world. Rain cells spawn as Water with downward velocity at random positions along the top edge during rain events. Interesting because it creates a natural water supply without requiring pre-placed water sources, but also means open-top machines can flood. Players must design weather-resistant enclosures or take advantage of rain collection for water-based processes.

### Underground Water Table
Below a certain depth, digging into Ground has a chance of hitting a water pocket that floods the excavation. Modeled as Water cells pre-placed deep in the terrain that are revealed by digging. Interesting because it creates a risk/reward tension for deep excavation -- you might find ore, or you might flood your mine and need to pump it out.

### Day/Night Cycle with Temperature Variation
Ambient temperature shifts with a day/night cycle. Daytime ambient is higher, nighttime ambient is lower. This affects proportional cooling -- furnaces are slightly more efficient at night (less cooling loss), and molten metal freezes faster outdoors at night. Creates a subtle time management layer without being punishing.

### Geological Layers
Different terrain materials at different depths: topsoil (Ground), rock (Stone), ore veins (IronOre, CopperOre). Veins are randomly placed during level generation and invisible until the player digs near them (revealed within 2-3 cells). This makes exploration meaningful -- the player must dig exploratory shafts to find resources. Mining becomes a real activity, not just digging through uniform dirt.

### Earthquakes / Seismic Events
Periodic events that apply a random velocity impulse to all non-static cells in a region. Piles of powder topple, liquid sloshes, unstable clusters fracture. Interesting because it stress-tests the player's machine design -- machines that rely on precise material placement will fail during an earthquake, encouraging robust designs with walls and containment.

### Erosion and Weathering
Over long time periods (thousands of frames), exposed Ground at the surface slowly converts to Dirt, and exposed Stone slowly converts to Sand. This is a background geological process. Interesting because it means terrain gradually becomes more malleable -- stone walls that the player cannot dig through will eventually weather into sand. Very slow, so it does not interfere with normal gameplay, but rewards patient players.

### Lava Pockets
Deep underground, pre-placed pockets of MoltenIron (or a new Lava material) that are under pressure. Digging into one causes a volcanic eruption -- molten material surges upward through the excavation. Creates a dramatic hazard and also a natural heat source the player could exploit by building a furnace around a lava vent instead of using furnace blocks.

### Biomes / Zone Types
Different world regions with different terrain composition, ambient temperature, and available materials. A desert zone has abundant sand but no water. A volcanic zone has lava and iron but extreme ambient heat. A swamp zone has water and mud everywhere. Interesting because it forces different puzzle-solving strategies per zone and encourages players to transport materials between biomes.

---

## 5. Puzzle Mechanics and Level Design Ideas

### Rube Goldberg Scoring
Levels award bonus points for machine complexity -- more structures used, more processing steps, more material transformations. The minimum solution scores low; elaborate solutions score high. Interesting because it inverts the normal puzzle optimization incentive and encourages creative over-engineering, which is where the game's emergent magic shines.

### Material Transmutation Chains
Puzzles that require multi-step material processing: Dig Ground -> Dirt falls onto belt -> Belt carries Dirt to water -> Dirt sinks (density sorting from sand?) -> Furnace heats something -> Product fills bucket. Each step uses a different system. Interesting because it tests the player's understanding of multiple systems working together.

### Contamination Puzzles
Bucket requires pure material, but the source is a mixture. Player must build a full sorting pipeline before delivering. Levels could introduce mixtures of increasing complexity -- two-material mixes early, four-material mixes late. Interesting because sorting is the game's unique mechanical challenge that no other puzzle game focuses on.

### Timed Delivery
Bucket has a timer -- must be filled before it expires. Encourages throughput optimization rather than just correctness. Players who build slow but correct machines need to parallelize or speed up their designs. Interesting because it adds an engineering dimension (efficiency) beyond just "does it work."

### Material Budget Levels
Limited supply of materials -- the source pile has exactly enough to fill the bucket, with no margin for waste. Any material lost (spilled off belt, burned accidentally, stuck in corners) means failure. Interesting because it forces precision and material conservation awareness, which reinforces the game's core physics philosophy.

### Reverse Engineering Levels
Player is given a pre-built machine that is broken (missing one structure) and must figure out what to add. Teaches through observation -- the player sees how existing machines work and fills in the gap. Interesting as a tutorial mechanic for advanced structures.

### Cascading Failure Levels
Multiple machines connected in series. If machine 1 fails, machine 2 starves, machine 3 overflows. Player must debug the whole pipeline, not just one machine. Interesting because it teaches systems thinking -- the player must understand how machines interact.

### Environmental Obstacle Levels
Natural terrain features that interfere with machines: a river cutting through the work area (floods belts), a cliff edge (material falls off), a stone column blocking the belt path. Player must work around or through these obstacles. Interesting because it makes level geometry a puzzle element, not just backdrop.

### "No Structures" Challenge Levels
Puzzles solvable using only terrain modification (digging channels), gravity, and density physics. No belts, no lifts, no walls. Forces the player to deeply understand the simulation physics. Interesting as a minimalist challenge mode for advanced players.

### Bucket Relay
Multiple buckets in sequence -- filling bucket A opens a gate that releases material toward bucket B, which opens a gate for bucket C. The player must build a chain of machines, each feeding the next. Interesting because it creates a Factorio-like automation challenge where the whole pipeline must work simultaneously.

### Pressure Puzzles
Enclosed liquid under pressure (liquid column above) must be routed through small openings. Higher liquid columns create more pressure (faster flow through gaps). Player must manage liquid levels and routing to achieve target flow rates. Interesting because it leverages the simulation's density and liquid spread mechanics in a novel way.

### Recycling Loops
The bucket output feeds back into the input with some waste. Player must build a closed-loop system that continuously processes material, removing waste at each cycle. Interesting because it introduces the concept of steady-state machines rather than one-shot deliveries.

---

## 6. Player Tools and Abilities

### Measuring Tool
Shows material counts, temperatures, and flow rates in a selected region. Click and drag a box, and a HUD panel shows: total cells by material, average temperature, net material flow in/out per second. Interesting because currently the player has no quantitative feedback -- they watch and guess. Measurement enables optimization and debugging of machines.

### Blueprint Tool
Select a group of placed structures and save them as a blueprint. Blueprints can be stamped down elsewhere, placing all structures at once (if space allows). Interesting because it eliminates tedious repetition when the player needs multiple copies of the same sub-machine (e.g., three identical sorting stations).

### Terrain Chisel
A precision digging tool that removes one cell at a time (vs. the shovel's area dig). Useful for carving exact channels, slots, and passages. Interesting because the shovel is too coarse for delicate work near machines -- one wrong dig can flood a furnace or collapse a support wall.

### Material Pipette
Click on any cell to identify its material type, temperature, velocity, and density. Like an eyedropper tool for the simulation. Interesting for debugging and learning -- new players can click on mysterious materials to understand what they are and what state they are in.

### Bomb / Explosive Charge
Placeable item that detonates after a delay, converting a radius of cells into Air and applying velocity impulse to surrounding materials. Useful for clearing large rock formations that are too dense to dig. Interesting because it is destructive and imprecise -- using it near machines is risky. Creates a trade-off between fast excavation and careful preservation.

### Bucket / Pail (Carried Liquid Container)
A carried item that holds a fixed volume of liquid, allowing the player to scoop water or molten metal and carry it somewhere. Different from the grab/drop system because it contains liquid without spilling. Interesting because currently there is no way for the player to manually transport liquids -- they always need pipes or channels.

### Torch
Placeable heat source that ignites Flammable materials on contact. Simpler than a furnace for starting fires. The player can place torches to light coal, ignite oil, or warm a small area. Interesting as a cheap, disposable heat source for early game before furnaces are unlocked.

### Spray / Hose Tool
Emits a stream of a specific material (Water, Sand, etc.) from the player's position in a targeted direction. Limited by material reserves in inventory. Interesting because it gives the player a way to precisely place materials at range, which is currently difficult -- you can only drop from directly above.

### Undo Tool
Reverts the last N player actions (structure placement, digging, etc.) but does NOT revert simulation physics. So you can undo placing a belt, but you cannot undo sand that has already fallen. Interesting because it reduces frustration from placement mistakes without breaking the simulation's physicality.

---

## 7. Visual and Aesthetic Systems

### Temperature Glow
Cells above a temperature threshold emit visible light, with color shifting from red (warm) to orange (hot) to white (extreme). MoltenIron already has a warm color, but this would make ALL heated materials glow -- hot sand glows red, hot water glows orange before boiling. Interesting because it gives immediate visual feedback about temperature distribution, making furnace design intuitive rather than requiring a debug overlay.

### Flow Visualization (Velocity Streamlines)
Toggle-able overlay that shows material velocity as tiny directional arrows or streaklines. Powder shows individual velocity vectors, liquids show flow direction. Interesting as a debug/learning tool -- players can see why material is pooling, where flow is blocked, and how fast things are moving.

### Particle Effects for Phase Changes
When material changes phase (melting, freezing, boiling, burning), emit small decorative particles at the transition point. Steam puffs when water boils, sparks when metal melts, wisps when material burns. Purely visual, no physics impact. Interesting because phase changes are currently invisible -- the cell just swaps material type. Visual effects make these moments feel dramatic and rewarding.

### Dust Clouds on Impact
When a cluster hits the ground or a powder pile absorbs a falling mass, emit a brief dust cloud particle effect. The bigger the impact, the larger the cloud. Interesting because it makes impacts feel weighty and physical, and gives visual feedback about collision intensity that is otherwise hard to judge.

### Material Shimmer / Animation
Liquids have subtle animated surface movement (shifting cell colors). Lava/molten metal has pulsing glow. Gases have drifting transparency. Interesting because static cell grids look artificial -- even small animation makes the world feel alive and materials feel distinct from each other.

### Machine Satisfaction Feedback
When a bucket is filled, play a satisfying visual+audio effect. When a long pipeline processes material successfully end-to-end, show a "flow" highlight along the path. Interesting because the emotional payoff of building a working machine needs to be amplified -- the moment everything clicks should feel triumphant.

### Underground Darkness
Areas the player has not yet dug into are rendered darker, with illumination spreading from dug-out areas and light sources (torches, lava, heated materials). Interesting because it creates atmosphere and a sense of discovery -- digging into dark rock and finding a glowing ore vein or a lava pocket is more dramatic when you cannot see what is ahead.

### Camera Zoom and Rotation
Let the player zoom in to watch individual cells move and zoom out to see the entire machine at once. At close zoom, show cell-level detail (velocity indicators, temperature colors). At far zoom, materials blend into colored regions. Interesting because the scale gap between cell-level physics and machine-level design is large, and players need both perspectives.

### Time-Lapse Replay
After completing a level, the player can watch a sped-up replay of their entire solution from the first structure placed to the final bucket fill. Interesting because it makes the player's achievement feel epic in retrospect -- watching a complex machine assemble and operate at 16x speed is inherently satisfying.

---

## 8. Progression and Unlock Ideas

### Material Discovery System
New materials start as "Unknown" until the player encounters them (digs into ore, produces a phase change product). Discovered materials go into an encyclopedia that shows their properties and known interactions. Interesting because it creates an exploration incentive and makes each new material feel like a discovery, not just another game mechanic.

### Structure Tech Tree
Structures unlock in a tree rather than a chain. After belts, the player can choose to unlock lifts OR walls next. Different branches enable different puzzle-solving strategies. Interesting because it gives the player agency in their progression path and encourages replay with different unlock orders.

### Mastery Challenges per Structure
Each structure has optional mastery challenges: "Transport 1000 cells with belts," "Lift material 50 cells high," "Melt 100 iron ore." Completing mastery unlocks an upgraded version of the structure (faster belts, higher-force lifts, hotter furnaces). Interesting because it rewards deep engagement with each structure rather than just using it once and moving on.

### Sandbox Unlock
After completing the main puzzle campaign, unlock a freeform sandbox mode with infinite materials, all structures, and no objectives. Interesting because the sandbox is where emergent creativity thrives -- players build crazy machines for fun after learning the mechanics through structured puzzles.

### Achievement System for Emergent Discoveries
Achievements for doing things the designer may not have planned: "Launch a cluster 100 cells high," "Create a 500-degree cell," "Build a machine with 20+ structures," "Sort four materials simultaneously." Interesting because it acknowledges and rewards emergent gameplay, which is the game's core identity.

### New Game Plus
Replay levels with harder constraints: smaller build area, fewer structures allowed, time limits, material budgets. Interesting because the same puzzles become entirely different challenges with tighter constraints, extending content without designing new levels.

### Material Compendium with Recipes
An in-game book that records every material transformation the player has caused. "Sand + 250 heat = Glass." "Coal + 180 heat = Ash + heat." Over time, this becomes a recipe book the player references when solving new puzzles. Interesting because it externalizes knowledge that players otherwise must memorize.

### Zone Progression (Overworld Map)
Instead of a linear level sequence, an overworld map with branching paths. Each zone has unique terrain, resources, and puzzle types. Completing a zone unlocks adjacent zones. Some zones are optional but contain rare materials or structures. Interesting because it gives the player exploration agency and allows non-linear difficulty progression.

### Prestige Materials
Endgame materials (Crystal, Bronze) that are difficult to produce but serve as ultimate bucket objectives. Producing them requires mastery of multiple systems (mining, sorting, smelting, alloying). Interesting because they create long-term goals that unify all the game's mechanics into a single challenge.

### Daily/Weekly Puzzle Challenges
Procedurally generated or curated challenge levels that rotate on a schedule. Leaderboards track completion time and machine efficiency. Interesting because it adds replayability and community engagement beyond the main campaign.

---

## 9. Weird and Experimental Mechanics That Might Be Fun

### Gunpowder (Coal + Sulfur Mixture)
When Coal and Sulfur powder are mixed and heated to a low ignition point, they explode -- converting a radius of cells to Air and applying massive velocity impulse. Not a new material per se, but an emergent interaction between existing materials. Interesting because it is discoverable (player might accidentally mix them near a furnace) and has both destructive and constructive uses (blasting rock, launching clusters).

### Ant Farm Mode
A zoomed-in side-scrolling view where the player IS a cell-sized character walking through the simulation. Sand grains are boulders. Water is a river. Interesting as a perspective shift that makes the physics feel grand-scale and emphasizes the simulation's detail.

### Living Materials (Self-Replicating)
A "Moss" material that slowly converts adjacent Water cells into more Moss. Grows in green patches, limited by water supply. Interesting because it is the only self-replicating material -- it will fill any water container if not managed. Creates a biological dimension: growing moss to plug leaks, or fighting moss overgrowth that clogs pipes.

### Gravity Inverter Zone
A structure that creates a local zone where gravity is reversed -- materials fall upward. Placed below a pile, materials float up. Interesting because it replaces lifts for bulk transport but is harder to control (everything rises, not just what you want). Creates chaotic fun when combined with normal gravity zones.

### Time Dilation Zones
Structures that speed up or slow down simulation within their area. A "Fast Zone" makes materials fall and react 4x faster. A "Slow Zone" makes them move at 0.25x speed. Interesting for controlling timing in complex machines -- slow down a section to synchronize it with another, or speed up a furnace to reduce smelting time.

### Pneumatic Tube
A sealed tube structure that sucks materials from one end and deposits them at the other, regardless of gravity. Material enters the input, disappears, and exits the output after a fixed delay. Different from pipes because it is active transport (vacuum suction) rather than passive gravity flow. Interesting as a long-distance transport mechanism that bypasses terrain obstacles.

### Material Resonance
Certain material combinations vibrate when adjacent (e.g., Iron + Crystal). The vibration is a small periodic velocity applied to both materials. If contained, this creates a natural mixing action. If unconstrained, materials slowly shake apart. Interesting because it is emergent from material properties rather than structure mechanics.

### Cluster Welding
When two metal clusters are adjacent at high temperature (both above their respective melt temps minus 20), they fuse into a single larger cluster. Interesting because it lets the player manufacture large metal objects by welding smaller pieces together. Combined with molds (wall-enclosed shapes filled with molten metal), this enables custom cluster fabrication.

### Magnetic Field Lines (Visual + Physical)
If magnets are implemented, their field lines could be visible as faint colored curves. Non-magnetic materials are not affected, but the visual presence of field lines helps the player understand the force distribution. Going further: two magnets facing each other could create a field that suspends magnetic material in mid-air between them. Interesting because magnetic levitation is physically real and visually spectacular.

### Sound-Based Mechanics
Materials produce sound when they collide (cluster impacts, powder pile impacts). Sound is not just aesthetic -- a "Microphone" structure could detect sound levels and trigger mechanisms. A pile of sand falling on a plate generates a sound event; if a microphone hears it, a gate opens. Interesting because it adds an invisible signal layer (sound) alongside the visible material layer.

### Perpetual Motion Detector
A debug/educational tool that highlights any system in the player's machine that violates energy conservation (e.g., a cluster that oscillates forever without losing energy). Marks it with a warning icon. Interesting as both a debugging aid and a subtle physics education tool.

### Material Memory / Patina
Materials that have been heated retain a tint showing their maximum reached temperature, even after cooling. Iron that was once white-hot has a blue temper tint. Sand that was almost glass-temp has a reddish hue. Purely visual but interesting because it tells a story -- you can look at materials and see what they have been through.

### Cluster Chains
Multiple clusters connected by a "chain" constraint (distance spring). When one cluster moves, it pulls the next, which pulls the next. Interesting because chains are fundamental mechanical components -- pulleys, bucket chains, conveyor links. A chain of clusters draped over two wheels becomes a bucket elevator.

### Freeze Ray / Cooling Structure
A structure that emits cold (negative heat) in a zone, counterpart to the furnace. Cools materials toward 0 degrees, freezing liquids. Interesting for controlled solidification -- freeze molten metal at a specific location to create a solid plug, or freeze water into a temporary dam.

### Material Pressure
Track how many cells of the same liquid are stacked above a given cell. Higher pressure (deeper liquid) could increase boiling temperature (real physics: pressure raises boiling point) and increase flow speed through narrow gaps. Interesting because it makes liquid depth a meaningful variable -- a tall water column flows faster through a gap than a shallow puddle, and you need higher temps to boil deep water.

### Dimensional Rift (Endgame)
Two paired portals that connect distant parts of the map. Material entering one exits the other with preserved velocity. Unlike drains (which are gravity-fed), rifts preserve momentum -- material shot through a rift exits at the same speed and direction. Interesting as an endgame transport tool that enables truly creative machine designs spanning the entire map.

---

## 10. Quality of Life and Meta-Game Features

### Simulation Speed Control
Global simulation speed slider: 0.25x, 0.5x, 1x, 2x, 4x, 8x. Slow motion for watching and debugging complex interactions. Fast forward for waiting on slow processes (furnace heating, long belt transport). Interesting because currently the player must wait at real-time speed for everything, which is frustrating during slow operations.

### Ghost Preview for All Structures
Before placing any structure, show a translucent preview of its effect zone. Fans show their wind zone. Magnets show their field zone. Furnaces show their heat emission direction. Interesting because currently the player must guess at effect ranges, leading to trial-and-error placement.

### Structure Rotation After Placement
Allow rotating directional structures (furnaces, fans) after placement without removing and re-placing them. Costs nothing, just changes the direction. Interesting because the current remove-and-replace workflow is tedious, especially when tuning a furnace orientation by trial and error.

### Auto-Save and Level Restart
Save the player's machine state at any time. Restart the simulation from the saved state without losing the machine. Interesting because currently, if a machine fails, the player must manually clean up before trying again. Saving the machine state and restarting the simulation from scratch preserves the build but resets the physics.

### Material Statistics Dashboard
A persistent HUD showing real-time counts of each material in the world, temperature histograms, and throughput graphs. How much sand has been processed? What is the furnace temperature trend? Interesting because it turns qualitative observation into quantitative monitoring, enabling systematic optimization.

### Machine Sharing (Level Codes or Files)
Export a machine design (structure positions, terrain modifications) as a compact code or file. Other players can import it into the same level and see it work. Interesting because sharing clever solutions is a huge part of the puzzle game community, and it extends content life through player-created solutions.

### Keybinding Customization
Let the player rebind all controls. Especially important for structure placement hotkeys as the structure count grows. Interesting as basic accessibility and comfort -- not glamorous, but its absence is noticed.

### Undo Stack for Structure Placement
Multiple levels of undo specifically for structure placement and terrain modification. Does not affect simulation state (materials that have already moved stay moved). Interesting because structure placement mistakes are common and currently require manual teardown.

### Material Color Accessibility Mode
Alternative color palettes that are distinguishable for colorblind players. Materials that look similar (Sand/Dirt, Iron/Stone) get distinct patterns or shapes in accessibility mode. Interesting because some material pairs are hard to distinguish even for non-colorblind players, and the game relies heavily on visual material identification.

### Tooltip System
Hovering over any structure or material shows a tooltip with its name, properties, and a one-line description of what it does. "Furnace Block (facing right): Emits heat to the right. Adjacent cells heat up toward equilibrium." Interesting because the game has many materials and structures with non-obvious properties, and in-game reference reduces the need to consult external documentation.

### Challenge Rating / Difficulty Indicators
Each level shows an estimated difficulty rating and which structures/systems it requires. Players can choose levels appropriate to their skill and unlocks. Interesting because it reduces frustration from attempting a level that requires a structure the player has not learned yet.

### Photo Mode
Pause the simulation and enter a free camera mode for screenshots. Apply filters, adjust zoom, frame the shot. Interesting because players build genuinely impressive machines and want to show them off -- giving them good screenshot tools encourages community sharing and marketing.

### Performance Monitor
An in-game overlay showing simulation FPS, active chunk count, total cell count, and cluster count. Interesting because large machines can slow down the simulation, and knowing the performance cost helps players build efficiently.

### Tutorial Replays
After completing a tutorial level, the player can watch a "developer solution" replay showing one way to solve it. Only available after completion so it does not spoil the puzzle. Interesting because seeing an expert solution teaches techniques the player might not have discovered.

---

## 11. Bonus: Meta-Design Principles for Feature Selection

When deciding which of these ideas to pursue, a few filters:

**Does it create new verbs?** The best additions give the player a new action (dissolve, freeze, pressurize, measure) that combines with existing verbs. Acid dissolving stone is a new verb. A differently-colored sand is not.

**Does it create emergent combinations?** The idea should interact with 3+ existing systems in non-obvious ways. Clay is interesting because it interacts with water (mud), heat (ceramic), digging (terrain), and fans (drying). A material that only has one interaction is not worth the complexity.

**Does it fail interestingly?** The best mechanics create entertaining failure modes. Steam explosions from water on molten metal are dangerous but spectacular. A structure that just does not work when used wrong is boring.

**Does it teach physics?** Since the game's north star is real physics, features that make players think "oh, that is how that works in real life" are ideal. Pressure affecting boiling point, erosion from flowing water, crystal seeding -- these are real phenomena that become game mechanics.

**Is it simple to implement, rich to combine?** One-way valves are trivially simple (check direction, allow/block) but transform machine design. Elaborate multi-state structures with complex logic are the opposite of the game's philosophy.
