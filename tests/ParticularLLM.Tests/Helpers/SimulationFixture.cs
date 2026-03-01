using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using ParticularLLM;

namespace ParticularLLM.Tests.Helpers;

public class SimulationFixture : IDisposable
{
    private const int MaxCaptureFrames = 2000;

    // Frame capture support — triggered by PARTICULARLLM_CAPTURE env var
    private static readonly string? CaptureDir;
    private static readonly ConcurrentDictionary<string, int> NameCounter = new();

    static SimulationFixture()
    {
        CaptureDir = Environment.GetEnvironmentVariable("PARTICULARLLM_CAPTURE");
    }

    public CellWorld World { get; }
    public CellSimulator Simulator { get; }

    /// <summary>
    /// Optional description of what this test scenario is verifying.
    /// Shown in the HTML viewer to help reviewers understand the expected behavior.
    /// </summary>
    public string? Description { get; set; }

    // Capture state (only active when CaptureDir is set and test method found)
    private readonly bool _capturing;
    private readonly string? _captureKey;
    private readonly MemoryStream? _captureStream;
    private int _capturedFrameCount;

    public SimulationFixture(int width = 64, int height = 64)
    {
        World = new CellWorld(width, height);
        Simulator = new CellSimulator();

        if (CaptureDir != null)
        {
            var (className, methodName) = FindTestMethod();
            if (className != null && methodName != null)
            {
                // Deduplicate: multiple fixtures per test or Theory variants
                string baseKey = $"{className}.{methodName}";
                int seq = NameCounter.AddOrUpdate(baseKey, 1, (_, v) => v + 1);
                _captureKey = seq == 1 ? baseKey : $"{baseKey}_{seq}";

                _capturing = true;
                _captureStream = new MemoryStream();
                CaptureCurrentFrame();
            }
        }
    }

    private static (string? className, string? methodName) FindTestMethod()
    {
        var trace = new StackTrace();
        for (int i = 0; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            if (method == null) continue;

            var attrs = method.GetCustomAttributes(false);
            foreach (var attr in attrs)
            {
                var typeName = attr.GetType().Name;
                if (typeName == "FactAttribute" || typeName == "TheoryAttribute")
                {
                    var className = method.DeclaringType?.Name ?? "Unknown";
                    return (className, method.Name);
                }
            }
        }
        return (null, null);
    }

    private void CaptureCurrentFrame()
    {
        if (!_capturing || _capturedFrameCount >= MaxCaptureFrames) return;

        int cellCount = World.width * World.height;
        var buffer = new byte[cellCount * 2];
        for (int j = 0; j < cellCount; j++)
        {
            buffer[j * 2] = World.cells[j].materialId;
            buffer[j * 2 + 1] = World.cells[j].temperature;
        }

        _captureStream!.Write(buffer, 0, buffer.Length);
        _capturedFrameCount++;
    }

    public void Step(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            Simulator.Simulate(World);
            CaptureCurrentFrame();
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

    public void SetTemperature(int x, int y, byte temp)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height) return;
        int index = y * World.width + x;
        World.cells[index].temperature = temp;
    }

    public byte GetTemperature(int x, int y)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height) return 0;
        return World.cells[y * World.width + x].temperature;
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
            CaptureCurrentFrame();

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
    /// Runs simulation until the grid stops changing between frames, or maxFrames is reached,
    /// checking material conservation every frame.
    /// Returns the number of frames simulated.
    /// </summary>
    public int StepUntilSettledWithInvariants(Dictionary<byte, int> expectedCounts, int maxFrames = 5000)
    {
        byte[] previous = new byte[World.cells.Length];

        for (int frame = 0; frame < maxFrames; frame++)
        {
            for (int i = 0; i < World.cells.Length; i++)
                previous[i] = World.cells[i].materialId;

            Simulator.Simulate(World);
            CaptureCurrentFrame();
            InvariantChecker.AssertMaterialConservation(World, expectedCounts);

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
            CaptureCurrentFrame();
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

    public void Dispose()
    {
        if (_capturing && _captureStream != null && _capturedFrameCount > 0)
        {
            WriteCaptureFile();
            _captureStream.Dispose();
        }
    }

    private void WriteCaptureFile()
    {
        try
        {
            Directory.CreateDirectory(CaptureDir!);

            int cellCount = World.width * World.height;
            int bytesPerFrame = cellCount * 2; // v2: 2 bytes per cell (materialId + temperature)
            byte[] rawFrames = _captureStream!.ToArray();
            int totalFrames = _capturedFrameCount;

            // Subsample if over limit
            byte[] outputFrames;
            int outputFrameCount;
            if (totalFrames > MaxCaptureFrames)
            {
                outputFrameCount = MaxCaptureFrames;
                outputFrames = new byte[outputFrameCount * bytesPerFrame];

                // Always keep frame 0, evenly space the rest
                for (int i = 0; i < outputFrameCount; i++)
                {
                    int srcFrame = i == 0 ? 0 : (int)((long)i * (totalFrames - 1) / (outputFrameCount - 1));
                    Buffer.BlockCopy(rawFrames, srcFrame * bytesPerFrame, outputFrames, i * bytesPerFrame, bytesPerFrame);
                }
            }
            else
            {
                outputFrameCount = totalFrames;
                outputFrames = rawFrames;
            }

            // Write v2 binary format
            string filePath = Path.Combine(CaptureDir!, $"{_captureKey}.bin");
            using var fs = File.Create(filePath);

            // Magic marker: "PLv2"
            fs.WriteByte(0x50); // 'P'
            fs.WriteByte(0x4C); // 'L'
            fs.WriteByte(0x76); // 'v'
            fs.WriteByte(0x32); // '2'

            // Header: width (int32), height (int32), frameCount (int32), little-endian
            fs.Write(BitConverter.GetBytes(World.width));
            fs.Write(BitConverter.GetBytes(World.height));
            fs.Write(BitConverter.GetBytes(outputFrameCount));

            // Description: length-prefixed UTF-8 string
            var descBytes = System.Text.Encoding.UTF8.GetBytes(Description ?? "");
            fs.Write(BitConverter.GetBytes(descBytes.Length));
            if (descBytes.Length > 0)
                fs.Write(descBytes);

            // Furnace block metadata
            WriteFurnaceMetadata(fs);

            // Gzip body: 2 bytes per cell per frame
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(outputFrames, 0, outputFrameCount * bytesPerFrame);
            }
        }
        catch
        {
            // Capture failures must never break tests
        }
    }

    private void WriteFurnaceMetadata(FileStream fs)
    {
        var furnaceManager = Simulator.FurnaceManager;
        if (furnaceManager == null)
        {
            // No furnace manager — write 0 blocks
            fs.Write(BitConverter.GetBytes(0));
            return;
        }

        var blocks = furnaceManager.GetPlacedBlocks();
        fs.Write(BitConverter.GetBytes(blocks.Count));
        foreach (var (gridX, gridY, direction) in blocks)
        {
            fs.Write(BitConverter.GetBytes((short)gridX));
            fs.Write(BitConverter.GetBytes((short)gridY));
            fs.WriteByte((byte)direction);
        }
    }
}
