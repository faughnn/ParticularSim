using ParticularLLM;

namespace ParticularLLM.Tests.Helpers;

public class SimulationFixture : IDisposable
{
    public CellWorld World { get; }
    public CellSimulator Simulator { get; }

    public SimulationFixture(int width = 64, int height = 64)
    {
        World = new CellWorld(width, height);
        Simulator = new CellSimulator();
    }

    public void Step(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            Simulator.Simulate(World);
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

    public Cell GetCell(int x, int y)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height) return default;
        return World.cells[y * World.width + x];
    }

    /// <summary>
    /// Runs simulation until the grid stops changing between frames, or maxFrames is reached.
    /// Returns the number of frames simulated.
    /// </summary>
    public int StepUntilSettled(int maxFrames = 5000)
    {
        byte[] previous = new byte[World.cells.Length];

        for (int frame = 0; frame < maxFrames; frame++)
        {
            // Snapshot current state
            for (int i = 0; i < World.cells.Length; i++)
                previous[i] = World.cells[i].materialId;

            Simulator.Simulate(World);

            // Check if anything changed
            bool changed = false;
            for (int i = 0; i < World.cells.Length; i++)
            {
                if (World.cells[i].materialId != previous[i])
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
                return frame + 1;
        }

        return maxFrames;
    }

    /// <summary>
    /// Takes a snapshot of all non-air material counts in the world.
    /// </summary>
    public Dictionary<byte, int> SnapshotMaterialCounts()
    {
        return InvariantChecker.SnapshotMaterialCounts(World);
    }

    /// <summary>
    /// Steps the simulation for N frames, checking material conservation each frame.
    /// </summary>
    public void StepWithInvariants(int frames, Dictionary<byte, int> expectedCounts)
    {
        for (int i = 0; i < frames; i++)
        {
            Simulator.Simulate(World);
            InvariantChecker.AssertMaterialConservation(World, expectedCounts);
        }
    }

    /// <summary>
    /// Finds all positions of a given material in the world.
    /// </summary>
    public List<(int x, int y)> FindMaterial(byte materialId)
    {
        var positions = new List<(int x, int y)>();
        for (int i = 0; i < World.cells.Length; i++)
        {
            if (World.cells[i].materialId == materialId)
            {
                int x = i % World.width;
                int y = i / World.width;
                positions.Add((x, y));
            }
        }
        return positions;
    }

    /// <summary>
    /// Computes the center of mass for a given material.
    /// Returns (NaN, NaN) if no cells of that material exist.
    /// </summary>
    public (double x, double y) CenterOfMass(byte materialId)
    {
        double sumX = 0, sumY = 0;
        int count = 0;

        for (int i = 0; i < World.cells.Length; i++)
        {
            if (World.cells[i].materialId == materialId)
            {
                sumX += i % World.width;
                sumY += i / World.width;
                count++;
            }
        }

        if (count == 0) return (double.NaN, double.NaN);
        return (sumX / count, sumY / count);
    }

    public void Dispose() { }
}
