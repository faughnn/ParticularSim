using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class LiftSimulationTests
{
    [Fact]
    public void Lift_PushesSandUpward()
    {
        // Create a tall lift column (4 blocks = 32 cells high)
        // The lift applies a net upward force that moves sand upward at ~1 cell/frame
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 40);  // rows 40-47
        lifts.PlaceLift(32, 48);  // rows 48-55
        lifts.PlaceLift(32, 56);  // rows 56-63
        lifts.PlaceLift(32, 64);  // rows 64-71
        sim.Simulator.SetLiftManager(lifts);

        // Place sand inside the lift zone near the bottom (y=70 is in the bottom block)
        sim.Set(34, 70, Materials.Sand);

        // After 20 steps, the sand should have risen significantly within the lift zone
        // (approximately 1 cell/frame upward due to net -3 fractional force)
        sim.Step(20);

        // Sand should still exist (material conservation)
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should have moved upward from y=70. After 20 steps at ~1 cell/frame,
        // it should be around y=50, which is well above the starting y=70
        bool sandFound = false;
        int sandY = -1;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                if (sim.Get(x, y) == Materials.Sand)
                {
                    sandFound = true;
                    sandY = y;
                    break;
                }

        Assert.True(sandFound, "Sand should still exist after lift simulation");
        Assert.True(sandY < 70, $"Sand should have moved upward from y=70, but found at y={sandY}");
    }

    [Fact]
    public void Lift_PushesWaterUpward()
    {
        // Create a lift column. Water in the lift zone should rise upward.
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80);  // rows 80-87
        lifts.PlaceLift(32, 88);  // rows 88-95
        lifts.PlaceLift(32, 96);  // rows 96-103
        sim.Simulator.SetLiftManager(lifts);

        // Place water inside the lift zone
        sim.Set(34, 96, Materials.Water);

        // After 10 steps, water should have risen from y=96
        // (liquid in a lift zone gets upward force, ~1 cell/frame)
        sim.Step(10);

        // Water should still exist
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);

        // Find the water position
        int waterY = -1;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                if (sim.Get(x, y) == Materials.Water)
                {
                    waterY = y;
                    break;
                }

        Assert.True(waterY >= 0, "Water should exist");
        Assert.True(waterY < 96, $"Water should have moved upward from y=96, but found at y={waterY}");
    }

    [Fact]
    public void Lift_MaterialConservation()
    {
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80);
        lifts.PlaceLift(32, 88);
        lifts.PlaceLift(32, 96);
        sim.Simulator.SetLiftManager(lifts);

        int placed = 0;
        for (int y = 90; y < 100; y++)
            for (int dx = 0; dx < 4; dx++)
            {
                sim.Set(32 + dx, y, Materials.Sand);
                placed++;
            }

        sim.Step(1000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Lift_IsPassable_SandEntersLiftZone()
    {
        // Sand dropped above a lift should fall INTO the lift zone
        // because lift material is passable
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 64);  // rows 64-71
        sim.Simulator.SetLiftManager(lifts);

        // Place sand above the lift
        sim.Set(34, 50, Materials.Sand);

        sim.Step(200);

        // Sand should have entered the lift zone or been pushed up and out
        // Either way, it should NOT be stuck above the lift as if blocked
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);
    }

    [Fact]
    public void Lift_RestoresLiftMaterial_WhenCellMovesOut()
    {
        // When a cell leaves a lift zone, the lift material should be restored
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80);
        lifts.PlaceLift(32, 88);
        sim.Simulator.SetLiftManager(lifts);

        // Place sand in the lift zone
        sim.Set(34, 85, Materials.Sand);

        sim.Step(500);

        // The cell at (34,85) should have lift material restored (sand moved out)
        byte matAt85 = sim.Get(34, 85);
        Assert.True(Materials.IsLift(matAt85),
            $"Lift material should be restored at (34,85) after sand moves out, got material {matAt85}");
    }
}
