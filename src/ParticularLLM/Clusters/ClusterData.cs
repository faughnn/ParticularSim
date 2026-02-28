namespace ParticularLLM;

/// <summary>
/// Rigid body cluster: a group of pixels that move together as a unit.
/// In Unity this is a MonoBehaviour with Rigidbody2D; here it's a plain class
/// with float-precision position/velocity and a pixel lookup grid for O(1) sync.
///
/// Coordinate convention: all positions use cell space (Y+ = down).
/// Pixel local coordinates are offsets from the cluster center in cell space.
/// </summary>
public class ClusterData
{
    // Identity
    public ushort Id { get; }

    // Position in cell-space (float for sub-cell precision)
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }  // Radians, counterclockwise positive

    // Velocity
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float AngularVelocity { get; set; }

    // Physics properties (calculated from pixels)
    public float Mass { get; set; } = 1f;
    public float MomentOfInertia { get; set; } = 1f;
    public float Restitution { get; set; } = 0.2f;
    public float Friction { get; set; } = 0.3f;

    // State
    public bool IsSleeping { get; set; }
    public bool IsMachinePart { get; set; }
    public int LowVelocityFrames { get; set; }
    public int CrushPressureFrames { get; set; }
    public int ActiveForceCount { get; set; }
    public bool IsOnGround { get; set; }
    public int DisplacedCellsLastSync { get; set; }

    // Sync state (for sleep optimization: skip clear/sync if position unchanged)
    public bool IsPixelsSynced { get; set; }
    public float LastSyncedX { get; set; }
    public float LastSyncedY { get; set; }
    public float LastSyncedRotation { get; set; }

    // Pixel data
    private readonly List<ClusterPixel> _pixels = new();
    public IReadOnlyList<ClusterPixel> Pixels => _pixels;
    public int PixelCount => _pixels.Count;

    // Pixel lookup grid (built lazily for O(1) inverse mapping)
    private byte[]? _pixelLookup;
    private int _lookupMinX, _lookupMinY;
    private int _lookupWidth, _lookupHeight;
    private bool _lookupBuilt;

    // Local bounding box (valid after BuildPixelLookup)
    public int LocalMinX => _lookupMinX;
    public int LocalMaxX => _lookupMinX + _lookupWidth - 1;
    public int LocalMinY => _lookupMinY;
    public int LocalMaxY => _lookupMinY + _lookupHeight - 1;

    // Tolerance for skip-sync position comparison
    private const float PositionTolerance = 0.01f;
    private const float RotationTolerance = 0.01f;

    public ClusterData(ushort id) { Id = id; }

    public void AddPixel(ClusterPixel pixel)
    {
        _pixels.Add(pixel);
        _lookupBuilt = false;
    }

    public void SetPixels(IEnumerable<ClusterPixel> pixels)
    {
        _pixels.Clear();
        _pixels.AddRange(pixels);
        _lookupBuilt = false;
    }

    /// <summary>
    /// Build pixel lookup grid for O(1) material queries at local coordinates.
    /// Called lazily and cached until pixels change.
    /// </summary>
    public void BuildPixelLookup()
    {
        if (_lookupBuilt) return;

        if (_pixels.Count == 0)
        {
            _lookupMinX = 0;
            _lookupMinY = 0;
            _lookupWidth = 0;
            _lookupHeight = 0;
            _lookupBuilt = true;
            return;
        }

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var p in _pixels)
        {
            if (p.localX < minX) minX = p.localX;
            if (p.localX > maxX) maxX = p.localX;
            if (p.localY < minY) minY = p.localY;
            if (p.localY > maxY) maxY = p.localY;
        }

        _lookupMinX = minX;
        _lookupMinY = minY;
        _lookupWidth = maxX - minX + 1;
        _lookupHeight = maxY - minY + 1;

        _pixelLookup = new byte[_lookupWidth * _lookupHeight];

        foreach (var p in _pixels)
        {
            int idx = (p.localY - _lookupMinY) * _lookupWidth + (p.localX - _lookupMinX);
            _pixelLookup[idx] = p.materialId;
        }

        _lookupBuilt = true;
    }

    /// <summary>
    /// Get the material at a local pixel position. Returns Air if no pixel there.
    /// </summary>
    public byte GetPixelMaterialAt(int localX, int localY)
    {
        if (!_lookupBuilt) BuildPixelLookup();
        if (_pixelLookup == null) return Materials.Air;

        int gx = localX - _lookupMinX;
        int gy = localY - _lookupMinY;
        if (gx < 0 || gx >= _lookupWidth || gy < 0 || gy >= _lookupHeight)
            return Materials.Air;
        return _pixelLookup[gy * _lookupWidth + gx];
    }

    /// <summary>
    /// Compute world-space AABB for this cluster at its current position and rotation.
    /// Returns the min/max cell coordinates that could contain cluster pixels.
    /// </summary>
    public void GetWorldAABB(out float minX, out float maxX, out float minY, out float maxY)
    {
        BuildPixelLookup();

        if (_pixels.Count == 0)
        {
            minX = maxX = X;
            minY = maxY = Y;
            return;
        }

        float hx = MathF.Max(MathF.Abs(_lookupMinX), MathF.Abs(_lookupMinX + _lookupWidth - 1)) + 1f;
        float hy = MathF.Max(MathF.Abs(_lookupMinY), MathF.Abs(_lookupMinY + _lookupHeight - 1)) + 1f;
        float absCos = MathF.Abs(MathF.Cos(Rotation));
        float absSin = MathF.Abs(MathF.Sin(Rotation));
        float extentX = hx * absCos + hy * absSin;
        float extentY = hx * absSin + hy * absCos;

        minX = X - extentX;
        maxX = X + extentX;
        minY = Y - extentY;
        maxY = Y + extentY;
    }

    /// <summary>
    /// Iterate all world cells covered by this cluster using inverse mapping.
    /// For each occupied cell, calls action(cellX, cellY, materialId).
    /// Uses the rotated bounding box approach to guarantee no gaps.
    /// </summary>
    public void ForEachWorldCell(int worldWidth, int worldHeight, Action<int, int, byte> action)
    {
        ForEachWorldCell(worldWidth, worldHeight, (cx, cy, mat) => { action(cx, cy, mat); return false; });
    }

    /// <summary>
    /// Iterate world cells with early-out support. Returns true if action returned true (early exit).
    /// </summary>
    public bool ForEachWorldCell(int worldWidth, int worldHeight, Func<int, int, byte, bool> action)
    {
        if (_pixels.Count == 0) return false;

        GetWorldAABB(out float aabbMinX, out float aabbMaxX, out float aabbMinY, out float aabbMaxY);

        float cos = MathF.Cos(Rotation);
        float sin = MathF.Sin(Rotation);

        int cellMinX = Math.Max(0, (int)MathF.Floor(aabbMinX));
        int cellMaxX = Math.Min(worldWidth - 1, (int)MathF.Ceiling(aabbMaxX));
        int cellMinY = Math.Max(0, (int)MathF.Floor(aabbMinY));
        int cellMaxY = Math.Min(worldHeight - 1, (int)MathF.Ceiling(aabbMaxY));

        for (int cy = cellMinY; cy <= cellMaxY; cy++)
        {
            for (int cx = cellMinX; cx <= cellMaxX; cx++)
            {
                float dx = cx - X;
                float dy = cy - Y;

                // Inverse rotation: world → local
                float localXf = dx * cos + dy * sin;
                float localYf = -dx * sin + dy * cos;

                int localX = (int)MathF.Round(localXf);
                int localY = (int)MathF.Round(localYf);

                byte materialId = GetPixelMaterialAt(localX, localY);
                if (materialId == Materials.Air) continue;

                if (action(cx, cy, materialId)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if the cluster should skip sync because it's sleeping at the same position.
    /// </summary>
    public bool ShouldSkipSync()
    {
        if (IsMachinePart) return false;
        if (!IsSleeping) return false;
        if (!IsPixelsSynced) return false;

        float dx = X - LastSyncedX;
        float dy = Y - LastSyncedY;
        if (dx * dx + dy * dy > PositionTolerance * PositionTolerance) return false;

        float dr = MathF.Abs(Rotation - LastSyncedRotation);
        if (dr > RotationTolerance) return false;

        return true;
    }

    /// <summary>
    /// Wake the cluster from sleep (e.g., when external force is applied).
    /// </summary>
    public void Wake()
    {
        IsSleeping = false;
        LowVelocityFrames = 0;
    }
}
