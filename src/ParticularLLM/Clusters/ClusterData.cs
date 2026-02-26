namespace ParticularLLM;

/// <summary>
/// Stub cluster data. In Unity, this is a MonoBehaviour with Rigidbody2D.
/// Here it's a plain class holding pixel data and position for grid sync testing.
/// </summary>
public class ClusterData
{
    public ushort Id { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }  // Radians
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }

    private readonly List<ClusterPixel> _pixels = new();
    public IReadOnlyList<ClusterPixel> Pixels => _pixels;
    public int PixelCount => _pixels.Count;

    public ClusterData(ushort id) { Id = id; }

    public void AddPixel(ClusterPixel pixel) => _pixels.Add(pixel);
}
