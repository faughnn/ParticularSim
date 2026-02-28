using System.Runtime.CompilerServices;

namespace ParticularLLM;

/// <summary>
/// Plain C# port of SimulateChunksJob (Burst/Jobs removed).
/// Each call to SimulateChunk processes one chunk's core 64x64 region.
/// Cells can read/write within a 128x128 extended region (32px buffer around core).
///
/// Movement system: Bresenham ray-march traces the full velocity vector (vx, vy)
/// in a single pass, handling collisions with per-material restitution.
/// At-rest behaviors (powder slide, liquid spread) run when velocity is zero.
/// </summary>
public class SimulateChunksLogic
{
    // Read/Write access to world data
    public Cell[] cells = null!;
    public ChunkState[] chunks = null!;

    // Read-only material definitions
    public MaterialDef[] materials = null!;

    // Lift zone tiles for O(1) lookup (parallel array to cells)
    public LiftTile[]? liftTiles;

    // Belt tiles for ghost tile blocking (Dictionary: position key -> tile data)
    public Dictionary<int, BeltTile>? beltTiles;

    // Wall tiles for ghost tile blocking (parallel array to cells)
    public WallTile[]? wallTiles;

    // World dimensions
    public int width;
    public int height;
    public int chunksX;
    public int chunksY;

    // Frame counter for double-processing prevention
    public ushort currentFrame;

    // Fractional gravity: added to accumulator each frame; overflow triggers velocity increment
    // Value of 17 gives ~15 frames between increments (256/17 ~ 15)
    public byte fractionalGravity;

    // Physics constants from PhysicsSettings
    public int gravity;      // Gravity applied when accumulator overflows (usually 1)
    public int maxVelocity;  // Maximum velocity in cells/frame (usually 16)
    public byte liftForce;   // Lift force (default 20, gravity is 17 so net is -3 upward)
    public short liftExitLateralForce; // Lateral force at lift exit row (fountain effect)

    // When true, temperature-triggered reactions (melting, freezing, boiling, burning) are active.
    // Should be enabled alongside heat transfer to avoid instant phase changes at ambient temperature.
    public bool enableReactions;

    private const int ChunkSize = 64;

    // Extended region bounds for current chunk (128x128 area with 32px buffer)
    private int extendedMinX;
    private int extendedMinY;
    private int extendedMaxX;
    private int extendedMaxY;

    // Index of the cell currently being simulated (y * width + x).
    // Used by CanMoveTo for source-aware ghost blocking.
    private int currentCellIdx;

    public void SimulateChunk(int chunkIndex)
    {
        int chunkX = chunkIndex % chunksX;
        int chunkY = chunkIndex / chunksX;

        // Core chunk bounds (clamped to world bounds)
        int coreMinX = chunkX * ChunkSize;
        int coreMinY = chunkY * ChunkSize;
        int coreMaxX = Math.Min(width, coreMinX + ChunkSize);
        int coreMaxY = Math.Min(height, coreMinY + ChunkSize);

        // Extended region bounds (128x128 with 32px buffer around core)
        // Cells can only read/write within this region
        extendedMinX = Math.Max(0, coreMinX - 32);
        extendedMinY = Math.Max(0, coreMinY - 32);
        extendedMaxX = Math.Min(width, coreMaxX + 32);
        extendedMaxY = Math.Min(height, coreMaxY + 32);

        // Process bottom-to-top (critical for falling), alternating X direction
        // Only simulate cells in core region - buffer zone is for cells to LAND in, not be simulated
        for (int y = coreMaxY - 1; y >= coreMinY; y--)
        {
            bool leftToRight = (y & 1) == 0;

            int startX = leftToRight ? coreMinX : coreMaxX - 1;
            int endX = leftToRight ? coreMaxX : coreMinX - 1;
            int stepX = leftToRight ? 1 : -1;

            for (int x = startX; x != endX; x += stepX)
            {
                SimulateCell(x, y);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SimulateCell(int x, int y)
    {
        int index = y * width + x;
        currentCellIdx = index;
        Cell cell = cells[index];

        // Skip air
        if (cell.materialId == Materials.Air)
            return;

        // Skip cells owned by clusters (rigid bodies) - they move as a unit
        if (cell.ownerId != 0)
            return;

        MaterialDef mat = materials[cell.materialId];

        // Skip if already processed this frame (prevents double-processing across chunks)
        byte frameModulo = (byte)(currentFrame & 0xFF);
        if (cell.frameUpdated == frameModulo)
            return;
        cell.frameUpdated = frameModulo;
        cells[index] = cell;

        // Phase 0: Check phase changes and burning (only when reactions are enabled)
        if (enableReactions)
        {
            if (CheckPhaseChange(x, y, index, ref cell, mat))
                return;

            if ((cell.flags & CellFlags.Burning) != 0)
            {
                SimulateBurning(x, y, index, ref cell, mat);
                if (cell.materialId == Materials.Air)
                    return;
            }
        }

        // Skip static materials for movement simulation
        if (mat.behaviour == BehaviourType.Static)
            return;

        // Simulate based on behaviour type
        switch (mat.behaviour)
        {
            case BehaviourType.Powder:
                SimulatePowder(x, y, cell, mat);
                break;
            case BehaviourType.Liquid:
                SimulateLiquid(x, y, cell, mat);
                break;
            case BehaviourType.Gas:
                SimulateGas(x, y, cell, mat);
                break;
        }
    }

    // ===== PHASE CHANGES & BURNING =====

    /// <summary>
    /// Check if the cell's temperature triggers a phase change (melt, freeze, boil, ignite).
    /// Returns true if the material was transformed (caller should return).
    /// </summary>
    private bool CheckPhaseChange(int x, int y, int index, ref Cell cell, MaterialDef mat)
    {
        byte temp = cell.temperature;

        // Melting: solid/powder → liquid
        if (mat.meltTemp > 0 && temp >= mat.meltTemp && mat.materialOnMelt != 0)
        {
            cell.materialId = mat.materialOnMelt;
            cell.velocityX = 0;
            cell.velocityY = 0;
            cell.flags = (byte)(cell.flags & ~CellFlags.Burning);
            cells[index] = cell;
            MarkDirtyInternal(x, y);
            return true;
        }

        // Freezing: liquid → solid
        if (mat.freezeTemp > 0 && temp <= mat.freezeTemp && mat.materialOnFreeze != 0)
        {
            cell.materialId = mat.materialOnFreeze;
            cell.velocityX = 0;
            cell.velocityY = 0;
            cells[index] = cell;
            MarkDirtyInternal(x, y);
            return true;
        }

        // Boiling: liquid → gas
        if (mat.boilTemp > 0 && temp >= mat.boilTemp && mat.materialOnBoil != 0)
        {
            cell.materialId = mat.materialOnBoil;
            cell.velocityX = 0;
            cell.velocityY = -1; // Gas rises
            cells[index] = cell;
            MarkDirtyInternal(x, y);
            return true;
        }

        // Ignition: flammable material catches fire
        if (mat.ignitionTemp > 0 && temp >= mat.ignitionTemp &&
            (mat.flags & MaterialFlags.Flammable) != 0 &&
            (cell.flags & CellFlags.Burning) == 0)
        {
            cell.flags |= CellFlags.Burning;
            cells[index] = cell;
        }

        return false;
    }

    /// <summary>
    /// Simulate a burning cell: emit heat, spread fire to neighbors, consume fuel.
    /// </summary>
    private void SimulateBurning(int x, int y, int index, ref Cell cell, MaterialDef mat)
    {
        // Emit heat: +5 degrees per frame
        cell.temperature = (byte)Math.Min(255, cell.temperature + 5);

        // Spread fire to flammable neighbors (~10% chance per neighbor per frame)
        SpreadFire(x - 1, y, 26);
        SpreadFire(x + 1, y, 26);
        SpreadFire(x, y - 1, 26);
        SpreadFire(x, y + 1, 26);

        // Consume fuel (~2% chance per frame)
        uint burnHash = HashPosition(x, y, currentFrame);
        if ((burnHash & 255) < 5) // ~2% chance
        {
            // Transform to burn product (ash or smoke)
            cell.materialId = mat.materialOnBurn;
            cell.flags = (byte)(cell.flags & ~CellFlags.Burning);
            cell.velocityX = 0;
            cell.velocityY = 0;
            cell.temperature = HeatSettings.AmbientTemperature;
            cells[index] = cell;
            MarkDirtyInternal(x, y);
            return;
        }

        cells[index] = cell;
    }

    private void SpreadFire(int nx, int ny, int chance)
    {
        if (!IsInBounds(nx, ny)) return;
        int ni = ny * width + nx;
        Cell neighbor = cells[ni];
        if (neighbor.materialId == Materials.Air) return;

        MaterialDef nMat = materials[neighbor.materialId];
        if ((nMat.flags & MaterialFlags.Flammable) == 0) return;

        // Heat neighbor
        neighbor.temperature = (byte)Math.Min(255, neighbor.temperature + 10);

        // Random chance to ignite
        uint hash = HashPosition(nx, ny, currentFrame);
        if ((hash & 255) < chance)
            neighbor.flags |= CellFlags.Burning;

        cells[ni] = neighbor;
        MarkDirtyInternal(nx, ny);
    }

    // ===== POWDER SIMULATION =====

    private void SimulatePowder(int x, int y, Cell cell, MaterialDef mat)
    {
        // Check if in lift zone (only if lift tiles exist and tile is not ghost)
        bool inLift = false;
        if (liftTiles != null)
        {
            var lt = liftTiles[y * width + x];
            inLift = lt.liftId != 0 && !lt.isGhost;
        }

        // Apply gravity with lift force opposition using fractional accumulation
        int netForce = fractionalGravity;
        if (inLift)
            netForce -= liftForce;
        ApplyFractionalForceY(ref cell, netForce);

        // Apply lateral exit force at top of lift (fountain effect)
        if (inLift)
            ApplyLiftExitForce(ref cell, x, y);

        // Air drag: probabilistic per-frame horizontal velocity decay.
        // Each frame, airDrag/256 chance of losing 1 unit of velocityX.
        // Subtractive (linear) avoids integer-truncation issues with small velocities.
        if (cell.velocityX != 0 && mat.airDrag > 0)
        {
            uint dragHash = HashPosition(x, y, currentFrame);
            if ((dragHash & 255) < mat.airDrag)
                cell.velocityX = (sbyte)(cell.velocityX - Math.Sign(cell.velocityX));
        }

        // Compute effective velocity for Bresenham trace
        int vx = cell.velocityX;
        int vy = cell.velocityY;

        // Zero-velocity gravity pull: always try to fall at least 1 cell.
        // This ensures continuous falling before the fractional accumulator overflows,
        // and enables density displacement at rest (sand sinking through water).
        if (vy == 0 && CanMoveTo(x, y + 1, mat.density))
            vy = 1;

        // If no movement possible, try at-rest behavior
        if (vx == 0 && vy == 0)
        {
            TryPowderSlide(x, y, ref cell, mat);
            return;
        }

        // Bresenham trace: follow the velocity vector through the grid
        var (fx, fy, collided, vertColl) = TraceMovement(x, y, vx, vy, mat.density);

        if (fx != x || fy != y)
        {
            if (collided)
            {
                HandlePowderCollision(ref cell, vx, vy, vertColl, mat.restitution, fx, fy);

                // Same-frame cascade: trace again with post-collision velocity.
                // This matches the old Phase 2 behavior where diagonal movement
                // happened in the same frame as the vertical collision.
                int postVx = cell.velocityX;
                int postVy = cell.velocityY;
                if (postVx != 0 || postVy != 0)
                {
                    var (fx2, fy2, col2, vert2) = TraceMovement(fx, fy, postVx, postVy, mat.density);
                    if (fx2 != fx || fy2 != fy)
                    {
                        if (col2)
                            HandlePowderCollision(ref cell, postVx, postVy, vert2, mat.restitution, fx2, fy2);
                        fx = fx2;
                        fy = fy2;
                    }
                }
            }
            ApplyLiquidDrag(ref cell, fx, fy);
            MoveCell(x, y, fx, fy, cell);
            return;
        }

        // Couldn't move at all — collision response then slide fallback
        if (collided)
            HandlePowderCollision(ref cell, vx, vy, vertColl, mat.restitution, x, y);
        TryPowderSlide(x, y, ref cell, mat);
    }

    private void ApplyLiquidDrag(ref Cell cell, int atX, int atY)
    {
        int idx = atY * width + atX;
        byte targetMat = cells[idx].materialId;
        if (targetMat == Materials.Air) return;

        var def = materials[targetMat];
        if (def.behaviour != BehaviourType.Liquid) return;

        // Drag proportional to liquid density (proxy for viscosity)
        int factor = 256 - def.density;
        cell.velocityX = (sbyte)(cell.velocityX * factor / 256);
        cell.velocityY = (sbyte)(cell.velocityY * factor / 256);
    }

    private void HandlePowderCollision(ref Cell cell, int preVx, int preVy,
        bool verticalCollision, byte restitution, int atX, int atY)
    {
        if (verticalCollision)
        {
            int impactSpeed = Math.Abs(preVy);
            int scatterSpeed = impactSpeed * restitution / 255;

            if (scatterSpeed > 0)
            {
                // Scatter: convert impact energy to diagonal movement along surface
                cell.velocityY = (sbyte)scatterSpeed;
                if (cell.velocityX == 0)
                {
                    uint hash = HashPosition(atX, atY, currentFrame);
                    cell.velocityX = (sbyte)((hash & 1) == 0 ? -scatterSpeed : scatterSpeed);
                }
            }
            else
            {
                cell.velocityY = 0;
            }
        }
        else
        {
            // Horizontal collision — reverse direction with damping
            int impactSpeed = Math.Abs(preVx);
            int reflectedSpeed = impactSpeed * restitution / 255;
            cell.velocityX = (sbyte)(-Math.Sign(preVx) * reflectedSpeed);
        }
    }

    private void TryPowderSlide(int x, int y, ref Cell cell, MaterialDef mat)
    {
        // Check slide resistance: higher values = less likely to slide diagonally
        if (mat.stability > 0)
        {
            uint hash = HashPosition(x, y, 0);
            if ((hash & 255) < mat.stability)
            {
                cell.velocityX = 0;
                cell.velocityY = 0;
                cells[y * width + x] = cell;
                if (y + 1 < height && CanMoveTo(x, y + 1, mat.density))
                    MarkDirtyInternal(x, y);
                return;
            }
        }

        // Randomize direction to avoid bias
        bool tryLeftFirst = ((x + y + currentFrame) & 1) == 0;
        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        if (CanMoveTo(x + dx1, y + 1, mat.density))
        {
            MoveCell(x, y, x + dx1, y + 1, cell);
            return;
        }

        if (CanMoveTo(x + dx2, y + 1, mat.density))
        {
            MoveCell(x, y, x + dx2, y + 1, cell);
            return;
        }

        // Stuck - write back with zeroed velocity
        cell.velocityX = 0;
        cell.velocityY = 0;
        cells[y * width + x] = cell;
        if (y + 1 < height && CanMoveTo(x, y + 1, mat.density))
            MarkDirtyInternal(x, y);
    }

    // ===== LIQUID SIMULATION =====

    private void SimulateLiquid(int x, int y, Cell cell, MaterialDef mat)
    {
        bool wasFreeFalling = cell.velocityY > 2;

        // Check if in lift zone
        bool inLift = false;
        if (liftTiles != null)
        {
            var lt = liftTiles[y * width + x];
            inLift = lt.liftId != 0 && !lt.isGhost;
        }

        // Apply gravity with lift force opposition using fractional accumulation
        int netForce = fractionalGravity;
        if (inLift)
            netForce -= liftForce;
        ApplyFractionalForceY(ref cell, netForce);

        // Apply lateral exit force at top of lift (fountain effect)
        if (inLift)
            ApplyLiftExitForce(ref cell, x, y);

        // Compute effective velocity for Bresenham trace
        int vx = cell.velocityX;
        int vy = cell.velocityY;

        // Zero-velocity gravity pull
        if (vy == 0 && CanMoveTo(x, y + 1, mat.density))
            vy = 1;

        // If have velocity, do Bresenham trace
        if (vx != 0 || vy != 0)
        {
            var (fx, fy, collided, vertColl) = TraceMovement(x, y, vx, vy, mat.density);

            if (fx != x || fy != y)
            {
                if (collided)
                {
                    if (vertColl)
                    {
                        // Convert vertical momentum to horizontal spread for liquid.
                        // Scale boost by spread so viscous liquids spread less.
                        if (Math.Abs(vy) > 2 && cell.velocityX == 0)
                        {
                            int boost = mat.spread + Math.Abs(vy) * mat.spread / 15;
                            uint ch = HashPosition(fx, fy, currentFrame);
                            cell.velocityX = (sbyte)((ch & 1) == 0 ? -boost : boost);
                        }
                        cell.velocityY = 0;
                    }
                    else
                    {
                        cell.velocityX = 0;
                    }
                }
                // Damp horizontal velocity after horizontal movement.
                // Liquid: higher stability value = more viscous = faster velocity decay.
                if (fx != x)
                {
                    int dampNumerator = 224 - mat.stability;
                    cell.velocityX = (sbyte)(cell.velocityX * dampNumerator / 256);
                }
                MoveCell(x, y, fx, fy, cell);
                return;
            }

            // Trace failed to move — try diagonal fall/rise as fallback
            if (vy > 0)
            {
                if (TryDiagonalMove(x, y, cell, mat.density, 1))
                    return;
            }
            else if (vy < 0)
            {
                if (TryDiagonalMove(x, y, cell, mat.density, -1))
                    return;
            }
        }

        // At rest — spread horizontally

        // Viscosity/friction check: per-frame chance to stop spreading.
        // Uses frame-varying hash so liquid eventually flows but gradually settles.
        // Only applies when the cell has no directional momentum (vx already decayed to 0).
        if (mat.stability > 0 && !wasFreeFalling && cell.velocityX == 0)
        {
            uint resistHash = HashPosition(x, y, currentFrame);
            if ((resistHash & 255) < mat.stability)
            {
                cell.velocityY = 0;
                cells[y * width + x] = cell;
                return;
            }
        }

        int velocityBoost = wasFreeFalling ? Math.Abs(cell.velocityY) / 3 : 0;
        int spread = mat.spread + velocityBoost;

        uint hash = HashPosition(x, y, currentFrame);
        int randomOffset = (int)(hash % 3) - 1;
        spread = Math.Max(1, spread + randomOffset);

        // Convert falling velocity to horizontal velocity when landing
        if (wasFreeFalling && cell.velocityX == 0)
        {
            bool goLeft = (hash & 4) != 0;
            cell.velocityX = (sbyte)(goLeft ? -4 : 4);
        }

        // Determine primary direction
        bool tryLeftFirst;
        if (cell.velocityX < 0)
            tryLeftFirst = true;
        else if (cell.velocityX > 0)
            tryLeftFirst = false;
        else
            tryLeftFirst = ((x + y + currentFrame) & 1) == 0;

        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        int bestDist1 = FindSpreadDistance(x, y, dx1, spread, mat.density);
        int bestDist2 = FindSpreadDistance(x, y, dx2, spread, mat.density);

        if (bestDist1 > 0 && bestDist1 >= bestDist2)
        {
            cell.velocityX = (sbyte)(cell.velocityX * 7 / 8);
            cell.velocityY = 0;
            MoveCell(x, y, x + dx1 * bestDist1, y, cell);
            return;
        }
        else if (bestDist2 > 0)
        {
            cell.velocityX = (sbyte)(-cell.velocityX * 7 / 8);
            cell.velocityY = 0;
            MoveCell(x, y, x + dx2 * bestDist2, y, cell);
            return;
        }

        // Stuck
        cell.velocityX = (sbyte)(cell.velocityX / 2);
        cell.velocityY = 0;
        cells[y * width + x] = cell;
    }

    // Find how far liquid can spread in a direction
    private int FindSpreadDistance(int x, int y, int dx, int maxSpread, byte density)
    {
        int bestDist = 0;
        for (int dist = 1; dist <= maxSpread; dist++)
        {
            int targetX = x + dx * dist;
            if (!IsInBounds(targetX, y))
                break;

            if (CanMoveTo(targetX, y, density))
            {
                bestDist = dist;
            }
            else if (!IsEmpty(targetX, y))
            {
                break;
            }
        }
        return bestDist;
    }

    // ===== GAS SIMULATION =====

    private void SimulateGas(int x, int y, Cell cell, MaterialDef mat)
    {
        // Gases rise - negative gravity using fractional accumulation
        byte oldFracY = cell.velocityFracY;
        cell.velocityFracY += fractionalGravity;
        if (cell.velocityFracY < oldFracY) // Overflow detected
        {
            cell.velocityY = (sbyte)Math.Max(cell.velocityY - gravity, -maxVelocity);
        }

        int targetY = y + cell.velocityY;

        // Trace path upward
        for (int checkY = y - 1; checkY >= targetY; checkY--)
        {
            if (!CanMoveTo(x, checkY, mat.density))
            {
                targetY = checkY + 1;
                break;
            }
        }

        if (targetY < y)
        {
            MoveCell(x, y, x, targetY, cell);
            return;
        }

        // Try diagonal upward
        bool tryLeftFirst = ((x + y + currentFrame) & 1) == 0;
        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        if (CanMoveTo(x + dx1, y - 1, mat.density))
        {
            MoveCell(x, y, x + dx1, y - 1, cell);
            return;
        }

        if (CanMoveTo(x + dx2, y - 1, mat.density))
        {
            MoveCell(x, y, x + dx2, y - 1, cell);
            return;
        }

        // Spread horizontally (gases disperse)
        int spread = 4;
        for (int dist = 1; dist <= spread; dist++)
        {
            int targetX = x + dx1 * dist;
            if (CanMoveTo(targetX, y, mat.density))
            {
                MoveCell(x, y, targetX, y, cell);
                return;
            }
        }

        // Stuck
        cell.velocityY = 0;
        cells[y * width + x] = cell;
    }

    // ===== BRESENHAM TRACE =====

    /// <summary>
    /// Traces a Bresenham line from (startX, startY) along velocity vector (vx, vy).
    /// Stops at the first blocked cell, returning the last valid position.
    /// Prevents tunneling on diagonal steps by checking intermediate cells.
    /// </summary>
    private (int finalX, int finalY, bool collided, bool verticalCollision) TraceMovement(
        int startX, int startY, int vx, int vy, byte density)
    {
        if (vx == 0 && vy == 0)
            return (startX, startY, false, false);

        int absDx = Math.Abs(vx);
        int absDy = Math.Abs(vy);
        int sx = vx > 0 ? 1 : (vx < 0 ? -1 : 0);
        int sy = vy > 0 ? 1 : (vy < 0 ? -1 : 0);

        int cx = startX;
        int cy = startY;

        if (absDy >= absDx)
        {
            // Y is the major axis (most common: falling)
            int error = absDy / 2;

            for (int step = 0; step < absDy; step++)
            {
                int nx = cx;
                int ny = cy + sy;
                error -= absDx;
                bool xStep = false;

                if (error < 0)
                {
                    nx += sx;
                    error += absDy;
                    xStep = true;
                }

                if (xStep)
                {
                    // Diagonal step — check intermediates to prevent tunneling
                    bool canY = CanMoveTo(cx, ny, density);
                    bool canX = CanMoveTo(nx, cy, density);

                    if (CanMoveTo(nx, ny, density) && (canY || canX))
                    {
                        cx = nx;
                        cy = ny;
                    }
                    else
                    {
                        return (cx, cy, true, !canY);
                    }
                }
                else
                {
                    if (CanMoveTo(nx, ny, density))
                    {
                        cx = nx;
                        cy = ny;
                    }
                    else
                    {
                        return (cx, cy, true, true);
                    }
                }
            }
        }
        else
        {
            // X is the major axis (horizontal movement dominant)
            int error = absDx / 2;

            for (int step = 0; step < absDx; step++)
            {
                int nx = cx + sx;
                int ny = cy;
                error -= absDy;
                bool yStep = false;

                if (error < 0)
                {
                    ny += sy;
                    error += absDx;
                    yStep = true;
                }

                if (yStep)
                {
                    bool canY = CanMoveTo(cx, ny, density);
                    bool canX = CanMoveTo(nx, cy, density);

                    if (CanMoveTo(nx, ny, density) && (canY || canX))
                    {
                        cx = nx;
                        cy = ny;
                    }
                    else
                    {
                        return (cx, cy, true, !canY);
                    }
                }
                else
                {
                    if (CanMoveTo(nx, ny, density))
                    {
                        cx = nx;
                        cy = ny;
                    }
                    else
                    {
                        return (cx, cy, true, false);
                    }
                }
            }
        }

        return (cx, cy, false, false);
    }

    // ===== FRACTIONAL FORCE HELPERS =====

    /// <summary>
    /// Apply a vertical force using fractional accumulation. Shared by powder and liquid gravity.
    /// When the accumulator overflows (>= 256), velocity increments by gravity.
    /// When it underflows (&lt; 0), velocity decrements (e.g., lift pushing upward).
    /// </summary>
    private void ApplyFractionalForceY(ref Cell cell, int force)
    {
        int newFracY = cell.velocityFracY + force;

        if (newFracY >= 256)
        {
            cell.velocityFracY = (byte)(newFracY - 256);
            cell.velocityY = (sbyte)Math.Min(cell.velocityY + gravity, maxVelocity);
        }
        else if (newFracY < 0)
        {
            cell.velocityFracY = (byte)(newFracY + 256);
            cell.velocityY = (sbyte)Math.Max(cell.velocityY - gravity, -maxVelocity);
        }
        else
        {
            cell.velocityFracY = (byte)newFracY;
        }
    }

    /// <summary>
    /// Apply lateral exit force at the top of a lift (fountain effect).
    /// Pushes material outward based on its position within the lift column.
    /// Shared by powder and liquid simulation.
    /// </summary>
    private void ApplyLiftExitForce(ref Cell cell, int x, int y)
    {
        if (liftExitLateralForce <= 0)
            return;

        bool isExitRow = (y == 0) || liftTiles == null ||
                         liftTiles[(y - 1) * width + x].liftId == 0;
        if (isExitRow)
        {
            int localX = x & 7;
            int lateralSign = (2 * localX - 7);
            int lateralForceValue = lateralSign * liftExitLateralForce;
            ApplyFractionalForceX(ref cell, lateralForceValue);
        }
    }

    private void ApplyFractionalForceX(ref Cell cell, int force)
    {
        int newFracX = cell.velocityFracX + force;

        if (newFracX >= 256)
        {
            int overflows = newFracX / 256;
            cell.velocityFracX = (byte)(newFracX - overflows * 256);
            cell.velocityX = (sbyte)Math.Min(cell.velocityX + overflows, maxVelocity);
        }
        else if (newFracX < 0)
        {
            int absVal = -newFracX;
            int underflows = (absVal + 255) / 256;
            cell.velocityFracX = (byte)(newFracX + underflows * 256);
            cell.velocityX = (sbyte)Math.Max(cell.velocityX - underflows, -maxVelocity);
        }
        else
        {
            cell.velocityFracX = (byte)newFracX;
        }
    }

    // ===== LIQUID FALLBACK HELPERS =====

    /// <summary>
    /// Try to move diagonally in the given vertical direction (dy=1 for fall, dy=-1 for rise).
    /// Alternates left/right priority based on position and frame for symmetry.
    /// </summary>
    private bool TryDiagonalMove(int x, int y, Cell cell, byte density, int dy)
    {
        bool tryLeftFirst = ((x + y + currentFrame) & 1) == 0;
        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        if (CanMoveTo(x + dx1, y + dy, density))
        {
            MoveCell(x, y, x + dx1, y + dy, cell);
            return true;
        }

        if (CanMoveTo(x + dx2, y + dy, density))
        {
            MoveCell(x, y, x + dx2, y + dy, cell);
            return true;
        }

        return false;
    }

    // ===== UTILITY =====

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint HashPosition(int x, int y, ushort frame)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + frame * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        return h ^ (h >> 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEmpty(int x, int y)
    {
        return cells[y * width + x].materialId == Materials.Air;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsInExtendedRegion(int x, int y)
    {
        return x >= extendedMinX && x < extendedMaxX &&
               y >= extendedMinY && y < extendedMaxY;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanMoveTo(int x, int y, byte myDensity)
    {
        if (!IsInExtendedRegion(x, y))
            return false;

        int idx = y * width + x;
        Cell target = cells[idx];

        if (target.ownerId != 0)
            return false;

        if (target.materialId == Materials.Air)
        {
            if (beltTiles != null && beltTiles.TryGetValue(idx, out BeltTile bt) && bt.isGhost)
            {
                if (!beltTiles.TryGetValue(currentCellIdx, out BeltTile srcBt) || !srcBt.isGhost)
                    return false;
            }
            if (wallTiles != null && wallTiles[idx].exists && wallTiles[idx].isGhost)
            {
                if (!wallTiles[currentCellIdx].exists || !wallTiles[currentCellIdx].isGhost)
                    return false;
            }

            return true;
        }

        MaterialDef targetMat = materials[target.materialId];
        if (targetMat.behaviour == BehaviourType.Static)
            return (targetMat.flags & MaterialFlags.Passable) != 0;

        return myDensity > targetMat.density;
    }

    private void MoveCell(int fromX, int fromY, int toX, int toY, Cell cell)
    {
        int fromIndex = fromY * width + fromX;
        int toIndex = toY * width + toX;

        Cell targetCell = cells[toIndex];

        // Place moving cell at destination
        cells[toIndex] = cell;

        // Determine what to leave at source
        if (liftTiles != null && liftTiles[fromIndex].liftId != 0 && !liftTiles[fromIndex].isGhost)
        {
            cells[fromIndex] = new Cell { materialId = liftTiles[fromIndex].materialId };
        }
        else if (targetCell.materialId != Materials.Air &&
                 (materials[targetCell.materialId].flags & MaterialFlags.Passable) != 0)
        {
            cells[fromIndex] = default;
        }
        else
        {
            cells[fromIndex] = targetCell;
        }

        // Mark both positions dirty
        MarkDirtyInternal(fromX, fromY);
        MarkDirtyInternal(toX, toY);

        // Wake adjacent chunks when we vacate a boundary position
        int localX = fromX & 63;
        int localY = fromY & 63;
        if (localX == 0 && fromX > 0)           MarkDirtyInternal(fromX - 1, fromY);
        if (localX == 63 && fromX < width - 1)  MarkDirtyInternal(fromX + 1, fromY);
        if (localY == 0 && fromY > 0)           MarkDirtyInternal(fromX, fromY - 1);
        if (localY == 63 && fromY < height - 1) MarkDirtyInternal(fromX, fromY + 1);
    }

    private void MarkDirtyInternal(int x, int y)
    {
        int chunkX = x >> 6;
        int chunkY = y >> 6;

        if (chunkX < 0 || chunkX >= chunksX || chunkY < 0 || chunkY >= chunksY)
            return;

        int chunkIndex = chunkY * chunksX + chunkX;

        ChunkState chunk = chunks[chunkIndex];
        chunk.flags |= ChunkFlags.IsDirty;

        int localX = x & 63;
        int localY = y & 63;

        if (localX < chunk.minX) chunk.minX = (ushort)localX;
        if (localX > chunk.maxX) chunk.maxX = (ushort)localX;
        if (localY < chunk.minY) chunk.minY = (ushort)localY;
        if (localY > chunk.maxY) chunk.maxY = (ushort)localY;

        chunks[chunkIndex] = chunk;
    }
}
