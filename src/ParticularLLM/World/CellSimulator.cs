namespace ParticularLLM;

/// <summary>
/// Orchestrates cell simulation by collecting active chunks and running them
/// through SimulateChunksLogic sequentially.
/// </summary>
public class CellSimulator
{
    private readonly List<int> activeChunks = new();
    private readonly List<int> groupA = new();
    private readonly List<int> groupB = new();
    private readonly List<int> groupC = new();
    private readonly List<int> groupD = new();

    private BeltManager? _beltManager;
    private LiftManager? _liftManager;
    private WallManager? _wallManager;

    /// <summary>
    /// When true, uses 4-pass checkerboard group ordering (matching Unity's parallel execution order).
    /// When false, processes all active chunks in flat index order.
    /// Both are single-threaded here, but 4-pass mode validates the grouping logic.
    /// </summary>
    public bool UseFourPassGrouping { get; set; }

    public void SetBeltManager(BeltManager manager) => _beltManager = manager;
    public void SetLiftManager(LiftManager manager) => _liftManager = manager;
    public void SetWallManager(WallManager manager) => _wallManager = manager;

    public void Simulate(CellWorld world)
    {
        world.currentFrame++;

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

        if (UseFourPassGrouping)
        {
            // 4-pass checkerboard: groups A, B, C, D processed sequentially
            // Matches Unity's parallel execution order (each group completes before next starts)
            world.CollectChunkGroups(groupA, groupB, groupC, groupD);

            foreach (int chunkIndex in groupA)
                logic.SimulateChunk(chunkIndex);
            foreach (int chunkIndex in groupB)
                logic.SimulateChunk(chunkIndex);
            foreach (int chunkIndex in groupC)
                logic.SimulateChunk(chunkIndex);
            foreach (int chunkIndex in groupD)
                logic.SimulateChunk(chunkIndex);
        }
        else
        {
            // Flat sequential: all active chunks in index order
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

            foreach (int chunkIndex in activeChunks)
                logic.SimulateChunk(chunkIndex);
        }

        // Simulate belts after cell simulation
        if (_beltManager != null)
            _beltManager.SimulateBelts(world, world.currentFrame);

        // Reset dirty state
        world.ResetDirtyState();
    }
}
