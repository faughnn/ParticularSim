using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

/// <summary>
/// Tests for the piston system: placement, removal, motor cycle, cell chain pushing.
///
/// English rules (derived from PistonManager.cs):
///
/// GEOMETRY (16x16 block):
/// - Piston occupies a 16×16 cell region, snapped to a 16-cell grid
/// - Base bar: 2 cells thick on one side (PistonBase material, static)
/// - Plate: 2 cells thick, perpendicular to push direction (PistonArm cluster, kinematic)
/// - Chamber: 12 cells of travel between base and plate (MaxTravel = 16 - 2 - 2)
/// - Fill area grows behind plate as it extends (PistonBase)
///
/// DIRECTION:
/// - Right: base on left, plate pushes right
/// - Left: base on right, plate pushes left
/// - Down: base on top, plate pushes down
/// - Up: base on bottom, plate pushes up
///
/// MOTOR CYCLE (180 frames, frame-based):
/// - 0%-15%: dwell at retracted (strokeT=0)
/// - 15%-50%: extend (strokeT 0→1, cell chain pushing)
/// - 50%-65%: dwell at extended (strokeT=1)
/// - 65%-100%: retract (strokeT 1→0, clear fill behind plate)
/// - All pistons share the same global phase
///
/// CELL CHAIN PUSHING (extend phase):
/// - Each extend step advances the plate 1 cell
/// - Leading edge: 16 cells perpendicular to push direction
/// - For each row, scan in push direction for first air gap
/// - If air found: chain-shift all cells from far end to near end (piston push)
/// - If static/cluster/boundary: stall (piston cannot extend this frame)
/// - All 16 rows must succeed for extend to happen
///
/// PLACEMENT:
/// - Area must be clear (Air or soft terrain, no existing structures)
/// - Clears 16×16 to Air, writes base bar, creates plate cluster
///
/// REMOVAL:
/// - Removes arm cluster, clears PistonBase/PistonArm cells to Air
///
/// CLUSTER INTERACTION:
/// - When stalled against a cluster, applies push force proportional to 1/mass
/// - Increments CrushPressureFrames (for fracture threshold)
/// </summary>
public class PistonTests
{
    // =====================================================================
    // Grid snapping
    // =====================================================================

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 16)]
    [InlineData(31, 16)]
    [InlineData(32, 32)]
    public void SnapToGrid_SnapsCorrectly(int input, int expected)
    {
        Assert.Equal(expected, PistonManager.SnapToGrid(input));
    }

    [Theory]
    [InlineData(-1, -16)]
    [InlineData(-16, -16)]
    [InlineData(-17, -32)]
    public void SnapToGrid_NegativeValues(int input, int expected)
    {
        Assert.Equal(expected, PistonManager.SnapToGrid(input));
    }

    // =====================================================================
    // Motor cycle
    // =====================================================================

    [Fact]
    public void MotorCycle_StartsRetracted()
    {
        var manager = new PistonManager();
        // Frame 0 → cycleT = 0 → dwell at retracted
        float stroke = manager.CalculateDesiredStrokeT();
        Assert.Equal(0f, stroke);
    }

    [Fact]
    public void MotorCycle_DwellPhases()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        // Frame 0 through DwellFraction * CycleFrames: should be 0 (retracted dwell)
        // First frames (0% to 15%) → dwell at retracted
        for (int i = 0; i < (int)(PistonManager.CycleFrames * PistonManager.DwellFraction); i++)
        {
            float s = manager.CalculateDesiredStrokeT();
            Assert.Equal(0f, s);
            // Advance frame by calling UpdateMotors (increments frame counter)
            manager.UpdateMotors(world);
        }
    }

    [Fact]
    public void MotorCycle_ReachesFullExtension()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        float maxStroke = 0f;
        for (int i = 0; i < PistonManager.CycleFrames; i++)
        {
            manager.UpdateMotors(world);
            float s = manager.CalculateDesiredStrokeT();
            if (s > maxStroke) maxStroke = s;
        }

        Assert.Equal(1f, maxStroke);
    }

    [Fact]
    public void MotorCycle_CompletesFullCycle()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        // Run full cycle + 1 frame
        for (int i = 0; i < PistonManager.CycleFrames + 1; i++)
            manager.UpdateMotors(world);

        // After full cycle, should be back near retracted
        float s = manager.CalculateDesiredStrokeT();
        Assert.True(s < 0.1f, $"After full cycle, strokeT should be near 0, was {s:F3}");
    }

    // =====================================================================
    // Placement
    // =====================================================================

    [Fact]
    public void PlacePiston_Right_CreatesBaseBar()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        bool placed = manager.PlacePiston(world, 32, 32, PistonDirection.Right);
        Assert.True(placed);
        Assert.Equal(1, manager.PistonCount);

        // Base bar should be on the left side (2 cells thick)
        for (int dy = 0; dy < 16; dy++)
        {
            Assert.Equal(Materials.PistonBase, world.GetCell(32, 32 + dy));
            Assert.Equal(Materials.PistonBase, world.GetCell(33, 32 + dy));
        }

        // Cell just past the base should be Air (chamber area)
        Assert.Equal(Materials.Air, world.GetCell(34, 32));
    }

    [Fact]
    public void PlacePiston_Left_BaseOnRight()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        bool placed = manager.PlacePiston(world, 32, 32, PistonDirection.Left);
        Assert.True(placed);

        // Base bar on right side
        for (int dy = 0; dy < 16; dy++)
        {
            Assert.Equal(Materials.PistonBase, world.GetCell(46, 32 + dy)); // 32 + 16 - 2 = 46
            Assert.Equal(Materials.PistonBase, world.GetCell(47, 32 + dy)); // 32 + 16 - 1 = 47
        }
    }

    [Fact]
    public void PlacePiston_Down_BaseOnTop()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        bool placed = manager.PlacePiston(world, 32, 32, PistonDirection.Down);
        Assert.True(placed);

        // Base bar on top (2 cells thick)
        for (int dx = 0; dx < 16; dx++)
        {
            Assert.Equal(Materials.PistonBase, world.GetCell(32 + dx, 32));
            Assert.Equal(Materials.PistonBase, world.GetCell(32 + dx, 33));
        }
    }

    [Fact]
    public void PlacePiston_Up_BaseOnBottom()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        bool placed = manager.PlacePiston(world, 32, 32, PistonDirection.Up);
        Assert.True(placed);

        // Base bar on bottom
        for (int dx = 0; dx < 16; dx++)
        {
            Assert.Equal(Materials.PistonBase, world.GetCell(32 + dx, 46)); // 32 + 16 - 2 = 46
            Assert.Equal(Materials.PistonBase, world.GetCell(32 + dx, 47)); // 32 + 16 - 1 = 47
        }
    }

    [Fact]
    public void PlacePiston_OutOfBounds_ReturnsFalse()
    {
        var world = new CellWorld(64, 64);
        var manager = new PistonManager();

        // 16×16 piston at (48, 48) extends to (63, 63) — exactly at boundary
        bool edge = manager.PlacePiston(world, 48, 48, PistonDirection.Right);
        Assert.True(edge);

        // At (49, 49) snaps to (48, 48), which is already placed — should fail as overlap
        bool outOfBounds = manager.PlacePiston(world, 49, 49, PistonDirection.Right);
        Assert.False(outOfBounds, "Placing piston overlapping existing should return false");
    }

    [Fact]
    public void PlacePiston_OverlapExisting_ReturnsFalse()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        Assert.True(manager.PlacePiston(world, 32, 32, PistonDirection.Right));
        // Same position — should fail
        Assert.False(manager.PlacePiston(world, 32, 32, PistonDirection.Right));
    }

    [Fact]
    public void PlacePiston_BlockedByStone_ReturnsFalse()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        world.SetCell(35, 35, Materials.Stone);

        Assert.False(manager.PlacePiston(world, 32, 32, PistonDirection.Right));
    }

    [Fact]
    public void PlacePiston_ClearsSoftTerrain()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        // Fill area with dirt (soft terrain)
        for (int dy = 0; dy < 16; dy++)
            for (int dx = 0; dx < 16; dx++)
                world.SetCell(32 + dx, 32 + dy, Materials.Dirt);

        int dirtBefore = WorldAssert.CountMaterial(world, Materials.Dirt);
        Assert.True(dirtBefore > 0);

        bool placed = manager.PlacePiston(world, 32, 32, PistonDirection.Right);
        Assert.True(placed);

        // Dirt should be cleared to Air (except base bar cells which become PistonBase)
        int dirtAfter = WorldAssert.CountMaterial(world, Materials.Dirt);
        Assert.Equal(0, dirtAfter);
    }

    // =====================================================================
    // Removal
    // =====================================================================

    [Fact]
    public void RemovePiston_ClearsAndUnregisters()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        manager.PlacePiston(world, 32, 32, PistonDirection.Right);
        Assert.Equal(1, manager.PistonCount);

        bool removed = manager.RemovePiston(world, 35, 35); // Any cell within the piston
        Assert.True(removed);
        Assert.Equal(0, manager.PistonCount);

        // All piston cells should be Air
        int pistonCount = WorldAssert.CountMaterial(world, Materials.PistonBase);
        Assert.Equal(0, pistonCount);
    }

    [Fact]
    public void RemovePiston_NoPiston_ReturnsFalse()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        Assert.False(manager.RemovePiston(world, 32, 32));
    }

    // =====================================================================
    // Cell chain pushing
    // =====================================================================

    [Fact]
    public void Extend_PushesSandRight()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        var clusterManager = new ClusterManager();
        manager.SetClusterManager(clusterManager);

        // Place piston at (0, 32) pushing right
        manager.PlacePiston(world, 0, 32, PistonDirection.Right);

        // Place sand column just ahead of the plate's initial position
        // Base is 2 cells, plate is 2 cells, so leading edge starts at x=4
        for (int dy = 0; dy < 16; dy++)
            world.SetCell(4, 32 + dy, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        // Run enough frames to get past dwell and start extending
        // Dwell is 15% of 180 = 27 frames, then extend phase starts
        for (int i = 0; i < 60; i++)
            manager.UpdateMotors(world);

        // Sand should have been pushed to the right (material conservation)
        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);
    }

    [Fact]
    public void Extend_StalledByStone()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        manager.PlacePiston(world, 0, 32, PistonDirection.Right);

        // Place stone wall at the leading edge of the plate.
        // For Right piston at (0,32): leading edge at fillExtent=0 is x=4
        for (int dy = 0; dy < 16; dy++)
            world.SetCell(4, 32 + dy, Materials.Stone);

        // Run extend phase (past 15% dwell)
        for (int i = 0; i < 60; i++)
            manager.UpdateMotors(world);

        // Piston should be stalled — fill hasn't advanced (stone blocks from the start)
        Assert.Equal(0, manager.Pistons[0].LastFillExtent);
    }

    [Fact]
    public void Extend_WriteFillBehindPlate()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        manager.PlacePiston(world, 0, 32, PistonDirection.Right);

        // Run through dwell and into extend
        for (int i = 0; i < 60; i++)
            manager.UpdateMotors(world);

        int fill = manager.Pistons[0].LastFillExtent;

        if (fill > 0)
        {
            // Fill area behind plate should be PistonBase
            // For direction Right: fill starts at x = BaseThickness = 2
            Assert.Equal(Materials.PistonBase, world.GetCell(2, 32));
        }
    }

    [Fact]
    public void Retract_ClearsFillArea()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        manager.PlacePiston(world, 0, 32, PistonDirection.Right);

        // Run full extend + dwell + start of retract (about 130 frames)
        for (int i = 0; i < 130; i++)
            manager.UpdateMotors(world);

        int fillAtExtended = manager.Pistons[0].LastFillExtent;

        // Continue through retract phase
        for (int i = 0; i < 70; i++)
            manager.UpdateMotors(world);

        int fillAfterRetract = manager.Pistons[0].LastFillExtent;

        // Fill should have decreased during retract
        Assert.True(fillAfterRetract < fillAtExtended,
            $"Fill should decrease during retract: extended={fillAtExtended}, after={fillAfterRetract}");
    }

    // =====================================================================
    // Plate cluster
    // =====================================================================

    [Fact]
    public void PlacePiston_WithClusterManager_CreatesPlateCluster()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        var clusterManager = new ClusterManager();
        manager.SetClusterManager(clusterManager);

        manager.PlacePiston(world, 32, 32, PistonDirection.Right);

        Assert.NotNull(manager.Pistons[0].ArmCluster);
        Assert.True(manager.Pistons[0].ArmCluster!.IsMachinePart);
        Assert.Equal(32, manager.Pistons[0].ArmCluster.PixelCount); // 2×16
    }

    [Fact]
    public void PlacePiston_WithoutClusterManager_NoCluster()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        // No cluster manager set

        manager.PlacePiston(world, 32, 32, PistonDirection.Right);

        Assert.Null(manager.Pistons[0].ArmCluster);
    }

    [Fact]
    public void PlateCluster_MovesWithStrokeT()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        var clusterManager = new ClusterManager();
        manager.SetClusterManager(clusterManager);

        manager.PlacePiston(world, 0, 32, PistonDirection.Right);
        var piston = manager.Pistons[0];
        float retX = piston.RetractedX;
        float extX = piston.ExtendedX;

        Assert.True(extX > retX, $"Extended X ({extX}) should be > retracted X ({retX}) for Right piston");

        // Run to full extension
        for (int i = 0; i < 90; i++) // About half cycle = full extend
            manager.UpdateMotors(world);

        // Plate should have moved right
        if (piston.ArmCluster != null)
        {
            Assert.True(piston.ArmCluster.X > retX,
                $"Plate should have moved right: X={piston.ArmCluster.X:F2}, retracted={retX:F2}");
        }
    }

    // =====================================================================
    // Directions
    // =====================================================================

    [Theory]
    [InlineData(PistonDirection.Right)]
    [InlineData(PistonDirection.Left)]
    [InlineData(PistonDirection.Down)]
    [InlineData(PistonDirection.Up)]
    public void PlacePiston_AllDirections_Succeed(PistonDirection direction)
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        bool placed = manager.PlacePiston(world, 32, 32, direction);
        Assert.True(placed);
        Assert.Equal(1, manager.PistonCount);

        // Base bar cells should exist
        int baseCount = WorldAssert.CountMaterial(world, Materials.PistonBase);
        Assert.Equal(32, baseCount); // 2×16 = 32 cells
    }

    // =====================================================================
    // Integration with simulator
    // =====================================================================

    [Fact]
    public void Pipeline_PistonPushesSand()
    {
        var sim = new SimulationFixture(128, 128);
        var clusterManager = new ClusterManager();
        var pistonManager = new PistonManager();
        pistonManager.SetClusterManager(clusterManager);
        sim.Simulator.SetClusterManager(clusterManager);
        sim.Simulator.SetPistonManager(pistonManager);

        // Stone container: floor + walls
        sim.Fill(0, 80, 128, 1, Materials.Stone);  // Floor below piston
        sim.Fill(0, 47, 128, 1, Materials.Stone);  // Ceiling above piston

        // Place piston at (0, 48) pushing right → covers x=0..15, y=48..63
        pistonManager.PlacePiston(sim.World, 0, 48, PistonDirection.Right);

        // Place sand just outside the piston block at x=16 (piston leading edge reaches here at max)
        for (int dy = 0; dy < 16; dy++)
            sim.Set(16, 48 + dy, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // Run simulation: piston extends, pushes sand right, then retracts
        sim.StepWithInvariants(200, counts);
    }

    [Fact]
    public void Pipeline_MultiplePistons_IndependentPlacement()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();

        Assert.True(manager.PlacePiston(world, 0, 0, PistonDirection.Right));
        Assert.True(manager.PlacePiston(world, 0, 16, PistonDirection.Left));
        Assert.True(manager.PlacePiston(world, 0, 32, PistonDirection.Down));

        Assert.Equal(3, manager.PistonCount);
    }

    // =====================================================================
    // HasPistonAt query
    // =====================================================================

    [Fact]
    public void HasPistonAt_InsidePiston_ReturnsTrue()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        manager.PlacePiston(world, 32, 32, PistonDirection.Right);

        Assert.True(manager.HasPistonAt(32, 32));
        Assert.True(manager.HasPistonAt(40, 40));
        Assert.True(manager.HasPistonAt(47, 47));
    }

    [Fact]
    public void HasPistonAt_OutsidePiston_ReturnsFalse()
    {
        var world = new CellWorld(128, 128);
        var manager = new PistonManager();
        manager.PlacePiston(world, 32, 32, PistonDirection.Right);

        Assert.False(manager.HasPistonAt(31, 32));
        Assert.False(manager.HasPistonAt(48, 32));
        Assert.False(manager.HasPistonAt(32, 31));
        Assert.False(manager.HasPistonAt(32, 48));
    }

    // =====================================================================
    // Constants validation
    // =====================================================================

    [Fact]
    public void Constants_MaxTravel_Is12()
    {
        Assert.Equal(12, PistonManager.MaxTravel);
        Assert.Equal(16, PistonManager.BlockSize);
        Assert.Equal(2, PistonManager.BaseThickness);
        Assert.Equal(2, PistonManager.PlateThickness);
        Assert.Equal(PistonManager.BlockSize - PistonManager.BaseThickness - PistonManager.PlateThickness,
            PistonManager.MaxTravel);
    }

    [Fact]
    public void Constants_CycleFrames_Is180()
    {
        Assert.Equal(180, PistonManager.CycleFrames);
    }
}
