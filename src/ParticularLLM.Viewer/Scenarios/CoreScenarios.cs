namespace ParticularLLM.Viewer.Scenarios;

public class CoreScenarios : IScenarioProvider
{
    public IEnumerable<ScenarioDef> GetScenarios()
    {
        yield return new ScenarioDef(
            "Sand Grain Falls",
            "Core Physics",
            "A single sand grain falls under gravity and comes to rest on the floor.",
            sim =>
            {
                // Stone floor
                sim.Fill(0, 60, 64, 4, Materials.Sand);
                sim.Fill(0, 60, 64, 4, Materials.Stone);
                // Single grain
                sim.Set(32, 5, Materials.Sand);
            },
            Tags: ["powder"],
            SuggestedFrames: 100
        );

        yield return new ScenarioDef(
            "Sand Pile",
            "Core Physics",
            "Sand falls from a narrow opening and forms a natural pile with diagonal spreading.",
            sim =>
            {
                // Stone floor
                sim.Fill(0, 60, 64, 4, Materials.Stone);
                // Sand source block at top center
                sim.Fill(28, 2, 8, 6, Materials.Sand);
            },
            Tags: ["powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Dirt Pile (Steep)",
            "Core Physics",
            "Dirt has high stability (50) so it piles steeper than sand.",
            sim =>
            {
                sim.Fill(0, 60, 64, 4, Materials.Stone);
                sim.Fill(28, 2, 8, 6, Materials.Dirt);
            },
            Tags: ["powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Hourglass",
            "Core Physics",
            "Sand flows through a narrow opening between two chambers.",
            sim =>
            {
                // Bottom chamber
                sim.Fill(10, 50, 44, 2, Materials.Stone); // floor
                sim.Fill(10, 20, 2, 30, Materials.Stone);  // left wall
                sim.Fill(52, 20, 2, 30, Materials.Stone);  // right wall
                // Narrow funnel
                sim.Fill(10, 20, 22, 2, Materials.Stone); // left shelf
                sim.Fill(34, 20, 20, 2, Materials.Stone); // right shelf (1-cell gap at 32-33)
                // Fill upper chamber with sand
                sim.Fill(12, 5, 40, 15, Materials.Sand);
            },
            Tags: ["powder"],
            SuggestedFrames: 600
        );

        yield return new ScenarioDef(
            "Water Drop",
            "Core Physics",
            "Water falls and spreads laterally along the floor.",
            sim =>
            {
                sim.Fill(0, 60, 64, 4, Materials.Stone);
                sim.Fill(29, 5, 6, 4, Materials.Water);
            },
            Tags: ["liquid"],
            SuggestedFrames: 300
        );

        yield return new ScenarioDef(
            "Water Fills Container",
            "Core Physics",
            "Water pours into a stone container and levels out.",
            sim =>
            {
                // Container
                sim.Fill(15, 55, 34, 2, Materials.Stone); // floor
                sim.Fill(15, 30, 2, 25, Materials.Stone);  // left wall
                sim.Fill(47, 30, 2, 25, Materials.Stone);  // right wall
                // Water poured from top
                sim.Fill(29, 5, 6, 8, Materials.Water);
            },
            Tags: ["liquid"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Water Overflow",
            "Core Physics",
            "Water overfills a shallow container and spills over the sides.",
            sim =>
            {
                sim.Fill(0, 58, 64, 6, Materials.Stone);  // floor
                sim.Fill(15, 48, 2, 10, Materials.Stone);  // left wall
                sim.Fill(47, 48, 2, 10, Materials.Stone);  // right wall
                // Lots of water
                sim.Fill(20, 20, 24, 20, Materials.Water);
            },
            Tags: ["liquid"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Oil Floats on Water",
            "Core Physics",
            "Oil (density 48) floats on top of water (density 64) via density displacement.",
            sim =>
            {
                // Container
                sim.Fill(10, 55, 44, 2, Materials.Stone);
                sim.Fill(10, 35, 2, 20, Materials.Stone);
                sim.Fill(52, 35, 2, 20, Materials.Stone);
                // Water first, then oil on top
                sim.Fill(12, 45, 40, 10, Materials.Water);
                sim.Fill(12, 38, 40, 7, Materials.Oil);
            },
            Tags: ["liquid", "density"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Sand Sinks Through Water",
            "Core Physics",
            "Sand (density 128) sinks through water (density 64) displacing water upward.",
            sim =>
            {
                // Container
                sim.Fill(10, 55, 44, 2, Materials.Stone);
                sim.Fill(10, 25, 2, 30, Materials.Stone);
                sim.Fill(52, 25, 2, 30, Materials.Stone);
                // Water pool
                sim.Fill(12, 35, 40, 20, Materials.Water);
                // Sand dropped on top
                sim.Fill(25, 28, 14, 6, Materials.Sand);
            },
            Tags: ["powder", "liquid", "density"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Steam Rises",
            "Core Physics",
            "Steam rises upward as a gas, spreading laterally as it goes.",
            sim =>
            {
                // Stone ceiling
                sim.Fill(0, 0, 64, 3, Materials.Stone);
                // Steam at the bottom
                sim.Fill(25, 55, 14, 6, Materials.Steam);
            },
            Tags: ["gas"],
            SuggestedFrames: 300
        );
    }
}
