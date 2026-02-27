namespace ParticularLLM;

/// <summary>
/// Manages all piston instances. 16x16 block pistons with kinematic plate,
/// global phase sync, fill area behind plate, and cell chain pushing on extend.
///
/// Piston geometry (e.g., direction=Right):
///   [BB][.............][PP]
///   [BB] = base bar (2 cells thick, PistonBase material)
///   [PP] = plate (2 cells thick, PistonArm cluster)
///   [..] = chamber (up to 12 cells of travel)
///
/// Motor cycle (3-second period, frame-based):
///   0%–15%: dwell at retracted
///   15%–50%: extend (push materials ahead of plate, one cell at a time)
///   50%–65%: dwell at extended
///   65%–100%: retract (clear fill behind plate, plate moves back)
///
/// Cell chain pushing:
///   On each extend step, the plate advances one cell. The leading edge cells
///   are checked for obstructions. Non-static materials ahead of the plate are
///   pushed in the piston direction (chain-shift from far end to near end).
///   Static materials or world boundaries stall the piston.
/// </summary>
public class PistonManager
{
    // Piston geometry
    public const int BlockSize = 16;
    public const int BaseThickness = 2;
    public const int PlateThickness = 2;
    public const int MaxTravel = BlockSize - BaseThickness - PlateThickness; // 12

    // Timing: 3-second cycle at 60 fps = 180 frames per cycle
    public const int CycleFrames = 180;
    public const float DwellFraction = 0.15f;

    // Force applied to clusters blocking the piston
    public const float ClusterPushForce = 800f;

    private readonly List<PistonData> _pistons = new();
    private readonly int[] _pushChainBuffer = new int[BlockSize];
    private readonly HashSet<ushort> _detectedClusterIds = new();
    private int _frameCounter;

    private ClusterManager? _clusterManager;

    public int PistonCount => _pistons.Count;
    public IReadOnlyList<PistonData> Pistons => _pistons;

    public void SetClusterManager(ClusterManager manager) => _clusterManager = manager;

    /// <summary>Snaps a coordinate to the 16-cell grid.</summary>
    public static int SnapToGrid(int coord)
    {
        if (coord < 0)
            return ((coord - BlockSize + 1) / BlockSize) * BlockSize;
        return (coord / BlockSize) * BlockSize;
    }

    /// <summary>
    /// Global piston phase: 0 = retracted, 1 = extended.
    /// Uses frame counter instead of Time.time for determinism.
    /// </summary>
    public float CalculateDesiredStrokeT()
    {
        float cycleT = (_frameCounter % CycleFrames) / (float)CycleFrames;

        if (cycleT < DwellFraction)
            return 0f;
        else if (cycleT < 0.5f)
            return Math.Clamp((cycleT - DwellFraction) / (0.5f - DwellFraction), 0f, 1f);
        else if (cycleT < 0.5f + DwellFraction)
            return 1f;
        else
            return Math.Clamp(1f - (cycleT - 0.5f - DwellFraction) / (1f - 0.5f - DwellFraction), 0f, 1f);
    }

    // =====================================================================
    // Placement / Removal
    // =====================================================================

    /// <summary>
    /// Place a 16x16 piston at the given position.
    /// Position is snapped to 16-cell grid. Returns true if placed successfully.
    /// </summary>
    public bool PlacePiston(CellWorld world, int cellX, int cellY, PistonDirection direction)
    {
        int gridX = SnapToGrid(cellX);
        int gridY = SnapToGrid(cellY);

        // Bounds check
        if (!world.IsInBounds(gridX, gridY) ||
            !world.IsInBounds(gridX + BlockSize - 1, gridY + BlockSize - 1))
            return false;

        // Validate area is clear (air or soft terrain, no overlapping structures)
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                if (HasPistonAt(cx, cy)) return false;

                byte mat = world.GetCell(cx, cy);
                if (mat != Materials.Air && !Materials.IsSoftTerrain(mat))
                    return false;
            }
        }

        // Clear entire 16x16 to Air
        for (int dy = 0; dy < BlockSize; dy++)
            for (int dx = 0; dx < BlockSize; dx++)
                world.SetCell(gridX + dx, gridY + dy, Materials.Air);

        // Write PistonBase cells for base bar
        WriteBaseBar(world, gridX, gridY, direction);

        // Mark chunks as having structure
        StructureUtils.MarkChunksHasStructure(world, gridX, gridY, BlockSize, BlockSize);

        // Calculate plate positions (in cell space)
        CalculatePlatePositions(gridX, gridY, direction,
            out float retX, out float retY, out float extX, out float extY);

        // Create plate cluster (if cluster manager available)
        ClusterData? armCluster = null;
        if (_clusterManager != null)
        {
            var platePixels = CreatePlatePixels(direction);
            armCluster = ClusterFactory.CreateCluster(platePixels, retX, retY, _clusterManager);
            if (armCluster != null)
                armCluster.IsMachinePart = true;
        }

        _pistons.Add(new PistonData
        {
            BaseCellX = gridX,
            BaseCellY = gridY,
            Direction = direction,
            ArmCluster = armCluster,
            RetractedX = retX,
            RetractedY = retY,
            ExtendedX = extX,
            ExtendedY = extY,
            CurrentStrokeT = 0f,
            LastFillExtent = 0,
        });

        return true;
    }

    /// <summary>Remove the piston at the given position.</summary>
    public bool RemovePiston(CellWorld world, int cellX, int cellY)
    {
        int gridX = SnapToGrid(cellX);
        int gridY = SnapToGrid(cellY);

        for (int i = _pistons.Count - 1; i >= 0; i--)
        {
            var piston = _pistons[i];
            if (piston.BaseCellX != gridX || piston.BaseCellY != gridY)
                continue;

            // Remove arm cluster
            if (piston.ArmCluster != null && _clusterManager != null)
                _clusterManager.RemoveCluster(piston.ArmCluster, world);

            // Clear all piston cells in 16x16 area
            for (int dy = 0; dy < BlockSize; dy++)
            {
                for (int dx = 0; dx < BlockSize; dx++)
                {
                    int cx = piston.BaseCellX + dx;
                    int cy = piston.BaseCellY + dy;
                    byte mat = world.GetCell(cx, cy);
                    if (Materials.IsPiston(mat))
                    {
                        world.SetCell(cx, cy, Materials.Air);
                        world.MarkDirty(cx, cy);
                    }
                }
            }

            StructureUtils.UpdateChunksStructureFlag(world, gridX, gridY, BlockSize, BlockSize,
                world.width, world.height, HasPistonAt);
            _pistons.RemoveAt(i);
            return true;
        }
        return false;
    }

    // =====================================================================
    // Motor update
    // =====================================================================

    /// <summary>
    /// Update all piston motors. Call each simulation frame.
    /// </summary>
    public void UpdateMotors(CellWorld world)
    {
        _frameCounter++;
        float desiredStrokeT = CalculateDesiredStrokeT();
        int desiredFill = (int)MathF.Round(desiredStrokeT * MaxTravel);

        for (int i = 0; i < _pistons.Count; i++)
        {
            var piston = _pistons[i];
            int currentFill = piston.LastFillExtent;

            if (desiredFill > currentFill)
            {
                // Extending — push materials ahead and advance one cell
                _detectedClusterIds.Clear();
                if (TryPushAndExtend(world, piston))
                {
                    WriteFillSlice(world, piston, currentFill);
                    piston.LastFillExtent = currentFill + 1;
                    piston.CurrentStrokeT = (currentFill + 1f) / MaxTravel;
                }
                else if (_detectedClusterIds.Count > 0 && _clusterManager != null)
                {
                    // Stalled against clusters — apply force
                    var dir = DirectionInfo.Get(piston.Direction);
                    foreach (ushort clusterId in _detectedClusterIds)
                    {
                        var cluster = _clusterManager.GetCluster(clusterId);
                        if (cluster == null || cluster.IsMachinePart) continue;

                        cluster.Wake();
                        cluster.VelocityX += dir.PushDx * ClusterPushForce / Math.Max(cluster.Mass, 1f);
                        cluster.VelocityY += dir.PushDy * ClusterPushForce / Math.Max(cluster.Mass, 1f);
                        cluster.CrushPressureFrames++;
                    }
                }
            }
            else if (desiredFill < currentFill)
            {
                // Retracting — always succeeds
                ClearFillSlice(world, piston, currentFill - 1);
                piston.LastFillExtent = currentFill - 1;
                piston.CurrentStrokeT = (currentFill - 1f) / MaxTravel;
            }

            // Move plate cluster
            if (piston.ArmCluster != null)
            {
                float t = piston.CurrentStrokeT;
                piston.ArmCluster.X = piston.RetractedX + (piston.ExtendedX - piston.RetractedX) * t;
                piston.ArmCluster.Y = piston.RetractedY + (piston.ExtendedY - piston.RetractedY) * t;
                piston.ArmCluster.IsSleeping = false; // Force sync every frame
                piston.ArmCluster.IsPixelsSynced = false;
            }
        }
    }

    // =====================================================================
    // Query
    // =====================================================================

    public bool HasPistonAt(int cellX, int cellY)
    {
        for (int i = 0; i < _pistons.Count; i++)
        {
            var p = _pistons[i];
            if (cellX >= p.BaseCellX && cellX < p.BaseCellX + BlockSize &&
                cellY >= p.BaseCellY && cellY < p.BaseCellY + BlockSize)
                return true;
        }
        return false;
    }

    // =====================================================================
    // Cell chain pushing
    // =====================================================================

    /// <summary>
    /// Try to push materials ahead of the plate and extend one cell.
    /// Returns false if stalled (static material, cluster, or world edge blocks all rows).
    /// </summary>
    private bool TryPushAndExtend(CellWorld world, PistonData piston)
    {
        int n = piston.LastFillExtent;
        if (n >= MaxTravel) return false;

        GetLeadingEdgeCells(piston, n,
            out int startX, out int startY,
            out int iterDx, out int iterDy,
            out int pushDx, out int pushDy, out int count);

        // First pass: validate all rows can be pushed
        for (int i = 0; i < count; i++)
        {
            int cx = startX + iterDx * i;
            int cy = startY + iterDy * i;

            if (!world.IsInBounds(cx, cy)) return false;

            int idx = cy * world.width + cx;
            Cell leadCell = world.cells[idx];

            // Cluster cells block the piston
            if (leadCell.ownerId != 0)
            {
                _detectedClusterIds.Add(leadCell.ownerId);
                return false;
            }

            if (leadCell.materialId == Materials.Air)
            {
                _pushChainBuffer[i] = 0;
                continue;
            }

            // Static materials at the leading edge block the piston entirely
            if (world.materials[leadCell.materialId].behaviour == BehaviourType.Static)
                return false;

            // Walk in push direction to find air or blocker
            int chainLen = 1;
            bool foundAir = false;
            const int maxScan = 64;

            for (int s = 1; s <= maxScan; s++)
            {
                int sx = cx + pushDx * s;
                int sy = cy + pushDy * s;

                if (!world.IsInBounds(sx, sy)) break;

                int sidx = sy * world.width + sx;
                Cell scanCell = world.cells[sidx];

                if (scanCell.ownerId != 0)
                {
                    _detectedClusterIds.Add(scanCell.ownerId);
                    break;
                }

                if (scanCell.materialId == Materials.Air)
                {
                    foundAir = true;
                    break;
                }

                if (world.materials[scanCell.materialId].behaviour == BehaviourType.Static)
                    break;

                chainLen++;
            }

            if (!foundAir) return false;
            _pushChainBuffer[i] = chainLen;
        }

        // Second pass: push cells (shift from far end to near end)
        for (int i = 0; i < count; i++)
        {
            if (_pushChainBuffer[i] == 0) continue;

            int cx = startX + iterDx * i;
            int cy = startY + iterDy * i;

            for (int s = _pushChainBuffer[i]; s >= 1; s--)
            {
                int fromX = cx + pushDx * (s - 1);
                int fromY = cy + pushDy * (s - 1);
                int toX = cx + pushDx * s;
                int toY = cy + pushDy * s;

                int fromIdx = fromY * world.width + fromX;
                int toIdx = toY * world.width + toX;

                world.cells[toIdx] = world.cells[fromIdx];
                world.MarkDirty(toX, toY);
            }

            // Clear the leading edge cell
            world.SetCell(cx, cy, Materials.Air);
            world.MarkDirty(cx, cy);
        }

        return true;
    }

    // =====================================================================
    // Direction geometry (single source of truth for all direction-dependent logic)
    // =====================================================================

    /// <summary>
    /// Pre-computed direction-dependent geometry. One switch statement here
    /// replaces five separate switches in the geometry helpers.
    ///
    /// Coordinate system: pushAxis runs along the push direction, crossAxis is perpendicular.
    /// For horizontal push (Right/Left): pushAxis=X, crossAxis=Y.
    /// For vertical push (Down/Up): pushAxis=Y, crossAxis=X.
    /// </summary>
    private readonly struct DirectionInfo
    {
        public readonly int PushDx, PushDy;
        public readonly int CrossDx, CrossDy;
        public readonly int BaseStart;        // Base bar start along push axis
        public readonly int FillOrigin;       // First fill slice along push axis
        public readonly int FillStep;         // +1 or -1
        public readonly int LeadOrigin;       // First leading edge along push axis
        public readonly int PlateRetracted;   // Plate center along push axis (retracted)
        public readonly int PlateExtended;    // Plate center along push axis (extended)

        private DirectionInfo(int pushDx, int pushDy, int crossDx, int crossDy, bool positive)
        {
            PushDx = pushDx; PushDy = pushDy;
            CrossDx = crossDx; CrossDy = crossDy;
            BaseStart = positive ? 0 : BlockSize - BaseThickness;
            FillOrigin = positive ? BaseThickness : BlockSize - BaseThickness - 1;
            FillStep = positive ? 1 : -1;
            LeadOrigin = positive ? BaseThickness + PlateThickness
                                  : BlockSize - BaseThickness - PlateThickness - 1;
            PlateRetracted = positive ? BaseThickness : BlockSize - BaseThickness - PlateThickness;
            PlateExtended = positive ? BlockSize - PlateThickness : 0;
        }

        public static DirectionInfo Get(PistonDirection dir) => dir switch
        {
            PistonDirection.Right => new(1, 0, 0, 1, true),
            PistonDirection.Left  => new(-1, 0, 0, 1, false),
            PistonDirection.Down  => new(0, 1, 1, 0, true),
            PistonDirection.Up    => new(0, -1, 1, 0, false),
            _ => new(0, 0, 0, 0, true),
        };

        /// <summary>
        /// Convert (pushAxis, crossAxis) offsets to world cell coordinates.
        /// </summary>
        public void ToWorld(int gx, int gy, int pushPos, int crossPos, out int wx, out int wy)
        {
            int absPx = PushDx * PushDx; // 0 or 1
            int absPy = PushDy * PushDy; // 0 or 1
            wx = gx + pushPos * absPx + crossPos * CrossDx;
            wy = gy + pushPos * absPy + crossPos * CrossDy;
        }
    }

    // =====================================================================
    // Fill area operations
    // =====================================================================

    private void WriteFillSlice(CellWorld world, PistonData piston, int fillIndex)
    {
        var dir = DirectionInfo.Get(piston.Direction);
        int pushPos = dir.FillOrigin + fillIndex * dir.FillStep;

        for (int i = 0; i < BlockSize; i++)
        {
            dir.ToWorld(piston.BaseCellX, piston.BaseCellY, pushPos, i, out int cx, out int cy);
            if (world.IsInBounds(cx, cy))
            {
                world.SetCell(cx, cy, Materials.PistonBase);
                world.MarkDirty(cx, cy);
            }
        }
    }

    private void ClearFillSlice(CellWorld world, PistonData piston, int fillIndex)
    {
        var dir = DirectionInfo.Get(piston.Direction);
        int pushPos = dir.FillOrigin + fillIndex * dir.FillStep;

        for (int i = 0; i < BlockSize; i++)
        {
            dir.ToWorld(piston.BaseCellX, piston.BaseCellY, pushPos, i, out int cx, out int cy);
            if (world.IsInBounds(cx, cy))
            {
                world.SetCell(cx, cy, Materials.Air);
                world.MarkDirty(cx, cy);

                // Wake neighbors so materials fall into the gap
                if (world.IsInBounds(cx, cy - 1)) world.MarkDirty(cx, cy - 1);
                if (world.IsInBounds(cx, cy + 1)) world.MarkDirty(cx, cy + 1);
                if (world.IsInBounds(cx - 1, cy)) world.MarkDirty(cx - 1, cy);
                if (world.IsInBounds(cx + 1, cy)) world.MarkDirty(cx + 1, cy);
            }
        }
    }

    // =====================================================================
    // Leading edge and cell chain pushing helpers
    // =====================================================================

    private void GetLeadingEdgeCells(PistonData piston, int fillExtent,
        out int startX, out int startY,
        out int iterDx, out int iterDy,
        out int pushDx, out int pushDy, out int count)
    {
        var dir = DirectionInfo.Get(piston.Direction);
        int pushPos = dir.LeadOrigin + fillExtent * dir.FillStep;

        dir.ToWorld(piston.BaseCellX, piston.BaseCellY, pushPos, 0, out startX, out startY);
        iterDx = dir.CrossDx;
        iterDy = dir.CrossDy;
        pushDx = dir.PushDx;
        pushDy = dir.PushDy;
        count = BlockSize;
    }

    // =====================================================================
    // Plate and base bar helpers
    // =====================================================================

    private static List<ClusterPixel> CreatePlatePixels(PistonDirection direction)
    {
        var pixels = new List<ClusterPixel>();
        var dir = DirectionInfo.Get(direction);
        bool horizontal = dir.PushDx != 0;

        if (horizontal)
        {
            // PlateThickness wide × BlockSize tall
            for (int ly = -7; ly <= 8; ly++)
                for (int lx = 0; lx < PlateThickness; lx++)
                    pixels.Add(new ClusterPixel((short)lx, (short)ly, Materials.PistonArm));
        }
        else
        {
            // BlockSize wide × PlateThickness tall
            for (int ly = 0; ly >= -(PlateThickness - 1); ly--)
                for (int lx = -8; lx <= 7; lx++)
                    pixels.Add(new ClusterPixel((short)lx, (short)ly, Materials.PistonArm));
        }

        return pixels;
    }

    private static void CalculatePlatePositions(int gridX, int gridY, PistonDirection direction,
        out float retX, out float retY, out float extX, out float extY)
    {
        var dir = DirectionInfo.Get(direction);
        float half = BlockSize / 2f;

        dir.ToWorld(gridX, gridY, dir.PlateRetracted, (int)half, out int rwx, out int rwy);
        dir.ToWorld(gridX, gridY, dir.PlateExtended, (int)half, out int ewx, out int ewy);

        // Cross-axis position uses float half for centering
        bool horizontal = dir.PushDx != 0;
        retX = horizontal ? rwx : gridX + half;
        retY = horizontal ? gridY + half : rwy;
        extX = horizontal ? ewx : gridX + half;
        extY = horizontal ? gridY + half : ewy;
    }

    private static void WriteBaseBar(CellWorld world, int gridX, int gridY, PistonDirection direction)
    {
        var dir = DirectionInfo.Get(direction);

        for (int cross = 0; cross < BlockSize; cross++)
        {
            for (int push = dir.BaseStart; push < dir.BaseStart + BaseThickness; push++)
            {
                dir.ToWorld(gridX, gridY, push, cross, out int cx, out int cy);
                world.SetCell(cx, cy, Materials.PistonBase);
                world.MarkDirty(cx, cy);
            }
        }
    }
}
