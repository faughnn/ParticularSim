namespace ParticularLLM.Viewer.Scenarios;

public class InteractionScenarios : IScenarioProvider
{
    public IEnumerable<ScenarioDef> GetScenarios()
    {
        yield return new ScenarioDef(
            "Belt-to-Lift Pipeline",
            "Interactions",
            "Sand rides a belt into a lift column, gets pushed upward, then falls back.",
            sim =>
            {
                var belts = sim.EnableBelts();
                var lifts = sim.EnableLifts();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Belt going right toward the lift
                for (int bx = 0; bx < 24; bx += 8)
                    belts.PlaceBelt(bx, 48, 1);
                // Lift column to the right of the belt
                for (int ly = 8; ly < 56; ly += 8)
                    lifts.PlaceLift(24, ly);
                // Sand on the belt
                sim.Fill(2, 42, 8, 6, Materials.Sand);
            },
            Tags: ["belt", "lift", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Piston onto Belt",
            "Interactions",
            "A piston pushes sand onto a belt which carries it away.",
            sim =>
            {
                var belts = sim.EnableBelts();
                var pistons = sim.EnablePistons();
                sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Piston at left: occupies x=0..15
                pistons.PlacePiston(sim.World, 0, 32, PistonDirection.Right);
                // Sand right at push point
                sim.Fill(16, 44, 4, 12, Materials.Sand);
                // Belt starting just past the sand, going right
                for (int bx = 24; bx < 56; bx += 8)
                    belts.PlaceBelt(bx, 48, 1);
            },
            Tags: ["piston", "belt", "cluster", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Piston Blocked by Wall",
            "Interactions",
            "A piston tries to push sand but a wall stops the chain.",
            sim =>
            {
                var walls = sim.EnableWalls();
                var pistons = sim.EnablePistons();
                sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Piston at left
                pistons.PlacePiston(sim.World, 0, 32, PistonDirection.Right);
                // Sand right at push point
                sim.Fill(16, 40, 6, 16, Materials.Sand);
                // Wall barrier close enough to block
                walls.PlaceWall(32, 32);
                walls.PlaceWall(32, 40);
                walls.PlaceWall(32, 48);
            },
            Tags: ["piston", "wall", "cluster", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Lift Blocked by Ceiling",
            "Interactions",
            "A lift pushes sand upward but a stone ceiling blocks it. Sand accumulates under the ceiling.",
            sim =>
            {
                sim.EnableLifts();
                var lifts = sim.LiftManager!;
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                sim.Fill(10, 3, 20, 3, Materials.Stone); // ceiling directly above lift
                // Tall lift column from near the ceiling down to the floor
                for (int ly = 8; ly < 56; ly += 8)
                    lifts.PlaceLift(16, ly);
                // Sand inside the lift
                sim.Fill(16, 48, 8, 8, Materials.Sand);
            },
            Tags: ["lift", "powder"],
            SuggestedFrames: 600
        );

        yield return new ScenarioDef(
            "Cluster Falls onto Floor",
            "Interactions",
            "A cluster (rigid body) falls under gravity and lands on the stone floor.",
            sim =>
            {
                var clusters = sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Create a small cluster from stone pixels
                var pixels = new List<ClusterPixel>();
                for (int dx = 0; dx < 6; dx++)
                    for (int dy = 0; dy < 4; dy++)
                        pixels.Add(new ClusterPixel((short)dx, (short)dy, Materials.Stone));
                ClusterFactory.CreateCluster(pixels, 28, 10, clusters);
            },
            Tags: ["cluster"],
            SuggestedFrames: 300
        );

        yield return new ScenarioDef(
            "Cluster Displaces Sand",
            "Interactions",
            "A stone cluster (rigid body) falls into a sand pile and pushes sand out of the way via push-based displacement.",
            sim =>
            {
                var clusters = sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Sand pile
                sim.Fill(20, 40, 24, 16, Materials.Sand);
                // Cluster dropping from above
                var pixels = new List<ClusterPixel>();
                for (int dx = 0; dx < 8; dx++)
                    for (int dy = 0; dy < 4; dy++)
                        pixels.Add(new ClusterPixel((short)dx, (short)dy, Materials.Stone));
                ClusterFactory.CreateCluster(pixels, 28, 5, clusters);
            },
            Tags: ["cluster", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Rain on Pile",
            "Interactions",
            "Water falls onto a sand pile, flowing over and through it.",
            sim =>
            {
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Sand pile
                sim.Fill(20, 45, 24, 11, Materials.Sand);
                // Water drops spread across the top
                for (int x = 15; x < 50; x += 3)
                    sim.Fill(x, 3, 2, 2, Materials.Water);
            },
            Tags: ["liquid", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Coal Burns to Ash",
            "Interactions",
            "Coal at high temperature ignites and turns to ash.",
            sim =>
            {
                sim.Simulator.EnableHeatTransfer = true;
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Coal pile
                sim.Fill(20, 46, 24, 10, Materials.Coal);
                // Heat the bottom row to ignition temperature
                for (int x = 20; x < 44; x++)
                {
                    int idx = 55 * sim.World.width + x;
                    sim.World.cells[idx].temperature = 200;
                    sim.World.MarkDirty(x, 55);
                }
            },
            Tags: ["heat", "powder"],
            SuggestedFrames: 600
        );

        yield return new ScenarioDef(
            "Density Sorting",
            "Interactions",
            "Mixed oil, water, and sand in a container sort by density: sand sinks, oil floats.",
            sim =>
            {
                // Container
                sim.Fill(10, 55, 44, 2, Materials.Stone);
                sim.Fill(10, 20, 2, 35, Materials.Stone);
                sim.Fill(52, 20, 2, 35, Materials.Stone);
                // Mix of materials (interleaved layers)
                sim.Fill(12, 22, 40, 5, Materials.Oil);
                sim.Fill(12, 27, 40, 5, Materials.Sand);
                sim.Fill(12, 32, 40, 5, Materials.Water);
                sim.Fill(12, 37, 40, 5, Materials.Oil);
                sim.Fill(12, 42, 40, 5, Materials.Sand);
                sim.Fill(12, 47, 40, 8, Materials.Water);
            },
            Tags: ["liquid", "powder", "density"],
            SuggestedFrames: 800
        );

        yield return new ScenarioDef(
            "Water Dam Break",
            "Interactions",
            "Water held behind a thin stone dam flows through a gap at the bottom, flooding the right side.",
            sim =>
            {
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Left wall
                sim.Fill(0, 15, 2, 41, Materials.Stone);
                // Dam wall with gap at bottom (y=52..55 is open)
                sim.Fill(30, 15, 2, 37, Materials.Stone); // dam: y=15..51
                // Water filling left reservoir
                sim.Fill(2, 20, 28, 36, Materials.Water);
            },
            Tags: ["liquid"],
            SuggestedFrames: 600
        );
    }
}
