# ParticularLLM

Standalone C# test harness for the falling sand simulation from the Sandy Unity game (`G:\Sandy`).

## Purpose

The Unity project can't be tested by Claude Code — it requires the Unity editor, GPU, and manual playtesting. This project extracts the core simulation logic (cell physics, materials, structures, chunking) into a plain .NET solution that `dotnet test` can run headlessly. This lets Claude Code validate simulation correctness, catch regressions, and verify tricky behaviors (processing order, material conservation, chunk boundaries) without a GUI.

## What's Ported

- Cell world, materials, and physics (gravity, velocity, fractional accumulation)
- Powder, liquid, gas, and static material behaviors
- Density displacement between materials
- 4-pass checkerboard chunk grouping (mirrors Unity's parallel execution)
- Bottom-to-top processing with alternating X direction per row
- Structure managers: Belt, Lift, Wall (placement, removal, ghost activation)
- Belt simulation (horizontal material transport)
- Lift simulation (upward force on materials)
- Cluster data stub
- Extended region cross-chunk cell movement

## What's NOT Here

- Rendering, shaders, GPU compute
- Unity MonoBehaviours, Rigidbody2D, Physics2D
- Player controls, game logic, UI
- Burst/Jobs parallelism (simulation runs single-threaded; 4-pass mode validates grouping logic)

## Key Invariant

**Material conservation**: total material count must never change unless explicitly intended. Many tests assert this.

## Architecture Philosophy

**Systems, Not Patches**
- Build unified systems that handle all cases, not individual fixes for individual problems
- One source of truth — if logic exists, it lives in ONE place
- No special-case rules for specific scenarios
- If a "fix" only addresses one situation, step back and design a system that handles ALL similar situations
- When something doesn't work, ask: "What system is missing?" not "What patch can I add?"

**Material Conservation**
- Materials must NEVER silently vanish. Total material quantity must be preserved.
- Materials can change shape, move, or be redistributed, but must not disappear unless explicitly intended (e.g., burning, dissolving, or an explicit destroy action).
- If an operation can't place all materials (congested area, out of bounds), retain the unplaced materials and give the player a way to retry — never discard them.

**Questions to ask before implementing:**
1. Does this logic already exist somewhere? (Don't duplicate)
2. Where should this logic live? (Single responsibility)
3. Will other systems need this? (Design for reuse)
4. Am I adding a special case or extending a system? (Prefer the latter)

## Testing & Development Workflow

### Three Test Layers

Tests are organized in three layers, from most automated to most exploratory:

**Layer 1 — Global Invariants**
Physics rules that must always hold. These are checked automatically and never require LLM judgment. Two categories:

*Per-step invariants* — valid even mid-simulation:
- Material conservation (total count unchanged)
- No two materials occupying the same cell (duplication)

*Settled-state invariants* — only valid after the sim has reached rest (grid stops changing between frames, or after a generous number of frames like 500+):
- No powder cell with air directly below it unless it has upward velocity or is inside a lift
- No liquid floating with air below and air on both sides

Global invariants are implemented as reusable assertion helpers (like `WorldAssert`) that can be called after any simulation run. Clearly distinguish per-step vs settled-state invariants so they aren't applied at the wrong time.

**Layer 2 — Scenario Assertions**
Specific test setups with hard, deterministic checks. Place materials, run N frames, assert measurable outcomes. These are the bulk of the test suite. Examples:
- "Place 50 sand above a stone floor, run 200 frames, count is still 50"
- "Sand at chunk boundary falls into adjacent chunk"
- "Water in a sealed container spreads to both sides"

**Layer 3 — LLM Review (English Rules + ASCII Dumps)**
For qualitative behaviors that are hard to express as deterministic assertions. Each scenario has:
- A minimal world setup (prefer small worlds, 64x64 or 128x128)
- English rules describing what correct behavior looks like
- An ASCII dump of the final state (using `WorldAssert.DumpRegion` or a full-world variant)

The LLM reads the dump and evaluates whether the English rules hold. This is inherently fuzzy but catches issues that counting alone cannot — wrong shapes, implausible resting states, asymmetric spread.

### The Pipeline: Layer 3 Feeds Layers 1 and 2

LLM review is the exploration tool. When reviewing an ASCII dump, observations about *why* it looks correct (or wrong) should be turned into concrete assertions:

1. LLM reviews dump, notes "sand formed a symmetric pile, wider at base"
2. This becomes a Layer 2 assertion: "pile width at bottom row >= pile width at top row"
3. If the rule is universal (not scenario-specific), promote it to Layer 1

Over time, the hard test coverage grows and LLM review is mainly needed for new features or significant code changes.

### Test Authoring Process

When adding tests for an existing system:

1. **Read the simulation code** for the behavior being tested (e.g., `SimulatePowder` in `SimulateChunksLogic.cs`)
2. **Derive English rules** from what the code does — what should the result look like in the grid?
3. **Note known tradeoffs** that are not bugs (e.g., upward-moving sand travels slower than downward due to bottom-to-top scan order)
4. **Design minimal scenarios** that isolate each rule — one behavior per test, small worlds
5. **Write the test** with Layer 2 hard assertions where possible
6. **Run and dump** the ASCII state
7. **LLM review** the dump against the English rules
8. **Harden** — turn review observations into new Layer 1 invariants or Layer 2 assertions
9. **Repeat** — drill into edge cases, boundary conditions, interactions between systems

### New Feature Development (Roadmap Items)

When implementing a new feature from the roadmap:

1. **Read the roadmap item** and any linked design docs
2. **Write English rules first** — what should the feature look like when working? (TDD in natural language)
3. **Write failing tests** from those rules — Layer 2 assertions and Layer 3 scenarios
4. **Implement the feature** in the simulation code
5. **Run all tests** — new tests should now pass, existing tests should not regress
6. **Dump and review** — LLM evaluates ASCII output against English rules
7. **Harden** — turn review observations into permanent assertions
8. **Re-derive English rules from the actual code** — the implementation may handle edge cases differently than the initial rules assumed. Update rules to match, or fix code if the behavior is wrong
9. **Move to next roadmap item**

### Tests Drive the Simulation

Tests define what correct behavior looks like. When a test fails:
- If the test expectation is **physically wrong** (e.g., expecting sand to fall up), fix the test.
- If the simulation is **not doing what it should** (e.g., liquids at rest never sorting by density, materials escaping containers, conservation violations), **fix the simulation code**. Don't weaken the test to accommodate broken behavior.
- When in doubt, ask: "What would a player expect to see?" If the sim doesn't match player expectations, the sim is wrong.

### Known Tradeoffs (Not Bugs)

Document these so tests don't flag expected behavior:
- **Upward movement is slower than downward**: Bottom-to-top scan order means falling sand cascades (multiple grains move per frame) but rising sand moves one-at-a-time. This is a fundamental tradeoff of the processing order, shared with the Unity version.
- **Processing order affects results**: Flat vs 4-pass chunk ordering produces different (but both valid) final states. Both must conserve materials and be individually deterministic.

### Conventions

- Test files live in `tests/ParticularLLM.Tests/` organized by category: `CoreTests/`, `SimulationTests/`, `StructureTests/`, `IntegrationTests/`
- Shared helpers live in `Helpers/` — `SimulationFixture.cs` for setup, `WorldAssert.cs` for assertions
- Prefer small focused worlds (64x64 or 128x128) over large ones for readability and LLM review
- Each test should assert material conservation unless the test specifically involves material creation/destruction
- English rules for Layer 3 tests should be written as comments in the test class or method
