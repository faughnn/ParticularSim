namespace ParticularLLM.Viewer;

/// <summary>
/// World setup and stepping for the viewer. Mirrors SimulationFixture from the test project
/// but adds an OnFrameStep callback fired after each simulation frame.
/// </summary>
public class ViewerFixture : IDisposable
{
    public CellWorld World { get; }
    public CellSimulator Simulator { get; }
    public BeltManager? BeltManager { get; private set; }
    public LiftManager? LiftManager { get; private set; }
    public WallManager? WallManager { get; private set; }
    public FurnaceBlockManager? FurnaceManager { get; private set; }
    public ClusterManager? ClusterManager { get; private set; }
    public PistonManager? PistonManager { get; private set; }

    public Action<CellWorld, int>? OnFrameStep;
    public int FrameCount { get; private set; }

    public ViewerFixture(int width = 64, int height = 64)
    {
        World = new CellWorld(width, height);
        Simulator = new CellSimulator();
    }

    public BeltManager EnableBelts()
    {
        BeltManager = new BeltManager(World);
        Simulator.SetBeltManager(BeltManager);
        return BeltManager;
    }

    public LiftManager EnableLifts()
    {
        LiftManager = new LiftManager(World);
        Simulator.SetLiftManager(LiftManager);
        return LiftManager;
    }

    public WallManager EnableWalls()
    {
        WallManager = new WallManager(World);
        Simulator.SetWallManager(WallManager);
        return WallManager;
    }

    public FurnaceBlockManager EnableFurnaces()
    {
        FurnaceManager = new FurnaceBlockManager(World);
        Simulator.SetFurnaceManager(FurnaceManager);
        Simulator.EnableHeatTransfer = true;
        return FurnaceManager;
    }

    public ClusterManager EnableClusters()
    {
        ClusterManager = new ClusterManager();
        Simulator.SetClusterManager(ClusterManager);
        return ClusterManager;
    }

    public PistonManager EnablePistons()
    {
        PistonManager = new PistonManager();
        Simulator.SetPistonManager(PistonManager);
        return PistonManager;
    }

    public void Step(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            Simulator.Simulate(World);
            FrameCount++;
            OnFrameStep?.Invoke(World, FrameCount);
        }
    }

    public void Set(int x, int y, byte materialId) => World.SetCell(x, y, materialId);

    public void Fill(int x, int y, int w, int h, byte materialId)
    {
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                World.SetCell(x + dx, y + dy, materialId);
    }

    public void SetWithVelocity(int x, int y, byte materialId, sbyte vx, sbyte vy)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height) return;
        int index = y * World.width + x;
        World.cells[index] = new Cell
        {
            materialId = materialId,
            velocityX = vx,
            velocityY = vy,
            temperature = 20,
        };
        World.MarkDirty(x, y);
    }

    public byte Get(int x, int y) => World.GetCell(x, y);

    public void Dispose()
    {
        World.Dispose();
    }
}
