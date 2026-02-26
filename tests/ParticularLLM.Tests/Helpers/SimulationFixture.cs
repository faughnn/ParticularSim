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

    public byte Get(int x, int y) => World.GetCell(x, y);

    public Cell GetCell(int x, int y)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height) return default;
        return World.cells[y * World.width + x];
    }

    public void Dispose() { }
}
