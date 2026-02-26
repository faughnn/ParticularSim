namespace ParticularLLM;

/// <summary>
/// Orchestrates cell simulation by collecting active chunks and running them
/// through SimulateChunksLogic sequentially.
/// </summary>
public class CellSimulator
{
    private readonly List<int> activeChunks = new();

    private BeltManager? _beltManager;
    private LiftManager? _liftManager;
    private WallManager? _wallManager;

    public void SetBeltManager(BeltManager manager) => _beltManager = manager;
    public void SetLiftManager(LiftManager manager) => _liftManager = manager;
    public void SetWallManager(WallManager manager) => _wallManager = manager;

    public void Simulate(CellWorld world)
    {
        world.currentFrame++;

        // Collect active chunks
        activeChunks.Clear();
        for (int i = 0; i < world.chunks.Length; i++)
        {
            var chunk = world.chunks[i];
            bool isActive = (chunk.flags & ChunkFlags.IsDirty) != 0
                         || chunk.activeLastFrame != 0
                         || (chunk.flags & ChunkFlags.HasStructure) != 0;
            if (isActive)
                activeChunks.Add(i);
        }

        // Create simulation logic
        var logic = new SimulateChunksLogic
        {
            cells = world.cells,
            chunks = world.chunks,
            materials = world.materials,
            liftTiles = _liftManager?.LiftTiles,
            beltTiles = _beltManager?.GetBeltTiles(),
            wallTiles = _wallManager?.WallTiles,
            width = world.width,
            height = world.height,
            chunksX = world.chunksX,
            chunksY = world.chunksY,
            currentFrame = world.currentFrame,
            fractionalGravity = PhysicsSettings.FractionalGravity,
            gravity = PhysicsSettings.CellGravityAccel,
            maxVelocity = PhysicsSettings.MaxVelocity,
            liftForce = PhysicsSettings.LiftForce,
            liftExitLateralForce = PhysicsSettings.LiftExitLateralForce,
        };

        // Simulate all active chunks sequentially
        foreach (int chunkIndex in activeChunks)
            logic.SimulateChunk(chunkIndex);

        // Simulate belts after cell simulation
        if (_beltManager != null)
            _beltManager.SimulateBelts(world, world.currentFrame);

        // Reset dirty state
        world.ResetDirtyState();
    }
}
