namespace ParticularLLM.Viewer.Scenarios;

public class StructureScenarios : IScenarioProvider
{
    public IEnumerable<ScenarioDef> GetScenarios()
    {
        yield return new ScenarioDef(
            "Belt Carries Sand",
            "Structures",
            "A rightward belt transports sand horizontally across its surface.",
            sim =>
            {
                var belts = sim.EnableBelts();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Place 4 belt segments going right (8x8 each, on row 48)
                for (int bx = 8; bx < 48; bx += 8)
                    belts.PlaceBelt(bx, 48, 1); // direction=1 is right
                // Sand pile on left end of belt
                sim.Fill(8, 42, 6, 6, Materials.Sand);
            },
            Tags: ["belt", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Belt Drops Off Edge",
            "Structures",
            "Sand rides a belt to the edge and falls off into a pit.",
            sim =>
            {
                var belts = sim.EnableBelts();
                // Floor with a gap
                sim.Fill(0, 56, 40, 8, Materials.Stone);  // left platform
                sim.Fill(48, 56, 16, 8, Materials.Stone);  // right platform lower
                // Belt on left platform going right
                for (int bx = 8; bx < 40; bx += 8)
                    belts.PlaceBelt(bx, 48, 1);
                // Sand
                sim.Fill(10, 42, 8, 6, Materials.Sand);
            },
            Tags: ["belt", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Lift Pushes Sand Up",
            "Structures",
            "A lift column applies upward force to sand, pushing it above the lift. Lift net force is -3/frame (gravity 17 minus lift 20).",
            sim =>
            {
                var lifts = sim.EnableLifts();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Tall lift column (6 segments, y=8 to y=55)
                for (int ly = 8; ly < 56; ly += 8)
                    lifts.PlaceLift(24, ly);
                // Sand at the bottom of the lift
                sim.Fill(24, 48, 8, 8, Materials.Sand);
            },
            Tags: ["lift", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Lift Fountain",
            "Structures",
            "A tall lift column shoots sand upward; sand falls back down on the sides.",
            sim =>
            {
                var lifts = sim.EnableLifts();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Tall lift column
                for (int ly = 8; ly < 56; ly += 8)
                    lifts.PlaceLift(28, ly);
                // Sand at the bottom of the lift
                sim.Fill(28, 48, 8, 8, Materials.Sand);
            },
            Tags: ["lift", "powder"],
            SuggestedFrames: 500
        );

        yield return new ScenarioDef(
            "Wall Blocks Sand",
            "Structures",
            "Sand falls from above but is blocked by a wall segment.",
            sim =>
            {
                var walls = sim.EnableWalls();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Wall in the middle
                walls.PlaceWall(28, 40);
                // Sand falling from above
                sim.Fill(24, 5, 16, 8, Materials.Sand);
            },
            Tags: ["wall", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Wall Container",
            "Structures",
            "Walls form a container that holds sand.",
            sim =>
            {
                var walls = sim.EnableWalls();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Wall container: floor and sides
                walls.PlaceWall(16, 48);
                walls.PlaceWall(16, 40);
                walls.PlaceWall(40, 48);
                walls.PlaceWall(40, 40);
                walls.PlaceWall(24, 48); // bottom of container
                walls.PlaceWall(32, 48);
                // Sand from above
                sim.Fill(22, 5, 20, 10, Materials.Sand);
            },
            Tags: ["wall", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Furnace Melts Iron Ore",
            "Structures",
            "A furnace block emits heat rightward into a stone-walled chamber containing iron ore. The ore heats up and melts into molten iron.",
            sim =>
            {
                var furnace = sim.EnableFurnaces();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Stone chamber to the right of furnace block
                sim.Fill(16, 40, 1, 16, Materials.Stone); // left wall (behind furnace)
                sim.Fill(33, 40, 1, 16, Materials.Stone); // right wall
                sim.Fill(17, 40, 16, 1, Materials.Stone); // bottom wall
                sim.Fill(17, 55, 16, 1, Materials.Stone); // top wall
                // Furnace block at (16,40) emitting right into the chamber
                furnace.PlaceFurnace(16, 40, FurnaceDirection.Right);
                // Iron ore inside the chamber
                sim.Fill(25, 42, 6, 6, Materials.IronOre);
            },
            Tags: ["furnace", "heat", "powder"],
            Width: 64, Height: 64,
            SuggestedFrames: 600
        );

        yield return new ScenarioDef(
            "Furnace Boils Water",
            "Structures",
            "A furnace block emits heat rightward into a stone-walled chamber containing water. The water heats up and boils into steam, which rises inside the sealed chamber.",
            sim =>
            {
                var furnace = sim.EnableFurnaces();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Stone chamber to the right of furnace block
                sim.Fill(16, 36, 1, 20, Materials.Stone); // left wall (behind furnace)
                sim.Fill(33, 36, 1, 20, Materials.Stone); // right wall
                sim.Fill(17, 36, 16, 1, Materials.Stone); // bottom wall
                sim.Fill(17, 55, 16, 1, Materials.Stone); // top wall
                // Furnace block at (16,40) emitting right into the chamber
                furnace.PlaceFurnace(16, 40, FurnaceDirection.Right);
                // Water inside the chamber
                sim.Fill(25, 48, 6, 6, Materials.Water);
            },
            Tags: ["furnace", "heat", "liquid", "gas"],
            SuggestedFrames: 600
        );

        yield return new ScenarioDef(
            "Piston Pushes Sand",
            "Structures",
            "A rightward piston extends (max stroke 12 cells, plate at x=14-15 fully extended) and pushes sand across the floor.",
            sim =>
            {
                var pistons = sim.EnablePistons();
                sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                // Piston facing right: occupies x=0..15, y=32..47
                pistons.PlacePiston(sim.World, 0, 32, PistonDirection.Right);
                // Sand right at the piston's reach (plate pushes at x=16)
                sim.Fill(16, 40, 6, 16, Materials.Sand);
            },
            Tags: ["piston", "cluster", "powder"],
            SuggestedFrames: 400
        );

        yield return new ScenarioDef(
            "Piston Cycle",
            "Structures",
            "A piston extends and retracts over a 180-frame cycle. Sand placed at the push point gets shoved forward each extension.",
            sim =>
            {
                var pistons = sim.EnablePistons();
                sim.EnableClusters();
                sim.Fill(0, 56, 64, 8, Materials.Stone);
                pistons.PlacePiston(sim.World, 0, 32, PistonDirection.Right);
                // Sand at push point
                sim.Fill(16, 44, 4, 12, Materials.Sand);
            },
            Tags: ["piston", "cluster", "powder"],
            SuggestedFrames: 600
        );
    }
}
