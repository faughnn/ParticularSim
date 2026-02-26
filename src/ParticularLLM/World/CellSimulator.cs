namespace ParticularLLM;

/// <summary>
/// Orchestrates cell simulation by collecting active chunks and running them
/// through SimulateChunksLogic sequentially.
/// </summary>
public class CellSimulator
{
    private readonly List<int> activeChunks = new();

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
            liftTiles = null,
            beltTiles = null,
            wallTiles = null,
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

        // Reset dirty state
        world.ResetDirtyState();
    }
}
