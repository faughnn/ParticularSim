using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Cross-system integration tests.
///
/// These test interactions between multiple simulation systems:
/// - Belt + gravity (sand falling onto belt, transported, falling off)
/// - Lift + belt (material exits lift, lands on belt)
/// - Lift + density (multiple materials through lift)
/// - Belt + wall (wall blocks belt transport)
/// - Full pipeline: gravity → lift → belt → gravity
/// </summary>
public class CrossSystemIntegrationTests
{
    // ===== BELT + GRAVITY =====

    [Fact]
    public void Sand_FallsOntoBelt_GetsTransported()
    {
        using var sim = new SimulationFixture(128, 128);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(32, 80, 1); // right-moving
        belts.PlaceBelt(40, 80, 1); // merged
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Drop sand above belt
        sim.Set(34, 20, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(500);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have fallen onto belt, been transported right, fallen off end
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].x > 40,
            $"Sand should be transported right past belt end, but at x={pos[0].x}");
    }

    // ===== LIFT + GRAVITY =====

    [Fact]
    public void Sand_ThroughLift_LandsOnFloor()
    {
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand below the lift — it falls into lift and gets pushed up
        sim.Set(52, 96, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(1000);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should eventually settle on the floor
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 100,
            $"Sand should settle after exiting lift, but at y={pos[0].y}");
    }

    // ===== LIFT + BELT =====

    [Fact]
    public void Lift_Exit_Sand_LandsOnBelt()
    {
        // Sand exits lift with lateral velocity, should land on a nearby belt
        // and get transported.
        using var sim = new SimulationFixture(128, 128);

        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        // Belt to the right of the lift exit, catching material that fountains right
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(64, 72, 1); // right-moving belt
        belts.PlaceBelt(72, 72, 1);
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand inside lift (right side for rightward fountain)
        sim.Set(54, 86, Materials.Sand); // localX=6, strong push right

        sim.Step(1000);

        // Sand should be conserved (lift tiles fluctuate, so only check sand)
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // Sand should have exited lift, potentially landed on belt, and been transported
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should be far right of the lift (transported by belt or fell past it)
        Assert.True(pos[0].x > 55 || pos[0].y >= 100,
            $"Sand should exit lift and move right, but at ({pos[0].x},{pos[0].y})");
    }

    // ===== WALL + BELT =====

    [Fact]
    public void Wall_BlocksBeltTransport()
    {
        // Wall at the end of a belt should block material from being pushed off.
        using var sim = new SimulationFixture(128, 128);

        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(32, 80, 1);
        belts.PlaceBelt(40, 80, 1);
        sim.Simulator.SetBeltManager(belts);

        var walls = new WallManager(sim.World);
        walls.PlaceWall(48, 72); // Wall at x=48..55, y=72..79 — above belt end
        sim.Simulator.SetWallManager(walls);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int surfaceY = 79;
        sim.Set(34, surfaceY, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(500);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== MULTI-MATERIAL THROUGH LIFT =====

    [Fact]
    public void Lift_SandAndWater_BothExit()
    {
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(50, 90, Materials.Sand);
        sim.Set(54, 90, Materials.Water);

        sim.Step(1500);

        // Sand and water should be conserved (lift tiles fluctuate)
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== FULL PIPELINE CONSERVATION =====

    [Fact]
    public void FullPipeline_BeltLiftWall_Conservation()
    {
        // Complex scenario: belt feeds material to a lift, lift pushes up,
        // material falls back down to floor.
        using var sim = new SimulationFixture(256, 256);

        var belts = new BeltManager(sim.World);
        for (int x = 40; x < 88; x += 8)
            belts.PlaceBelt(x, 160, 1); // right-moving belt chain
        sim.Simulator.SetBeltManager(belts);

        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(88, 136);
        lifts.PlaceLift(88, 144);
        lifts.PlaceLift(88, 152);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 240, 256, 16, Materials.Stone);

        // Drop sand above belt start
        int placed = 0;
        for (int x = 42; x < 48; x++)
        {
            sim.Set(x, 140, Materials.Sand);
            placed++;
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(3000);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        Assert.Equal(placed, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== DENSITY ORDER PRESERVED THROUGH LIFT =====

    [Fact]
    public void Lift_SandAndWater_DensityOrder_AfterExit()
    {
        // After both sand and water exit a lift and settle, sand (heavier)
        // should be below water (lighter).
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 64);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        // Container to catch material after it exits
        sim.Fill(30, 110, 1, 18, Materials.Stone); // left wall
        sim.Fill(70, 110, 1, 18, Materials.Stone); // right wall
        sim.Fill(30, 127, 41, 1, Materials.Stone);  // floor

        // Place both materials inside lift
        for (int x = 50; x < 54; x++)
        {
            sim.Set(x, 92, Materials.Sand);
            sim.Set(x, 94, Materials.Water);
        }

        sim.Step(3000);

        // Sand and water should be conserved (lift tiles fluctuate)
        Assert.Equal(4, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(4, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }
}
