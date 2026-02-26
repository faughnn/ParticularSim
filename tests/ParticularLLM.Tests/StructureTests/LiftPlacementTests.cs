using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class LiftPlacementTests
{
    [Fact]
    public void PlaceLift_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        Assert.True(lifts.PlaceLift(10, 10));
        Assert.True(lifts.HasLiftAt(8, 8));
    }

    [Fact]
    public void PlaceLift_WritesPassableMaterialToCells()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.True(Materials.IsLift(world.GetCell(8 + dx, 8 + dy)),
                    $"Cell ({8+dx},{8+dy}) should be lift material");
    }

    [Fact]
    public void AdjacentLifts_Vertically_Merge()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        lifts.PlaceLift(8, 16);
        Assert.Equal(1, lifts.LiftCount);
    }

    [Fact]
    public void AdjacentLifts_Horizontally_DontMerge()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        lifts.PlaceLift(16, 8);
        Assert.Equal(2, lifts.LiftCount);
    }

    [Fact]
    public void RemoveLift_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        Assert.True(lifts.RemoveLift(8, 8));
        Assert.False(lifts.HasLiftAt(8, 8));
    }

    [Fact]
    public void RemoveLift_MiddleOfMerged_SplitsInTwo()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        lifts.PlaceLift(8, 16);
        lifts.PlaceLift(8, 24);
        Assert.Equal(1, lifts.LiftCount);
        lifts.RemoveLift(8, 16);
        Assert.Equal(2, lifts.LiftCount);
    }
}
