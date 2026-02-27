using System.Runtime.CompilerServices;

namespace ParticularLLM;

/// <summary>
/// Plain C# port of SimulateChunksJob (Burst/Jobs removed).
/// Each call to SimulateChunk processes one chunk's core 64x64 region.
/// Cells can read/write within a 128x128 extended region (32px buffer around core).
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

        // Skip static materials
        if (mat.behaviour == BehaviourType.Static)
            return;

        // Skip if moving upward and already processed this frame
        // (upward movement goes against scan direction, causing multi-processing)
        if (cell.velocityY < 0)
        {
            byte frameModulo = (byte)(currentFrame & 0xFF);
            if (cell.frameUpdated == frameModulo)
                return;
            cell.frameUpdated = frameModulo;
            cells[index] = cell;
        }

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
        // Gravity adds +17 per frame, lift subtracts 20, net is -3 (upward)
        int netForce = fractionalGravity;
        if (inLift)
            netForce -= liftForce;

        int newFracY = cell.velocityFracY + netForce;

        if (newFracY >= 256)  // Overflow -> accelerate down
        {
            cell.velocityFracY = (byte)(newFracY - 256);
            cell.velocityY = (sbyte)Math.Min(cell.velocityY + gravity, maxVelocity);
        }
        else if (newFracY < 0)  // Underflow -> accelerate up
        {
            cell.velocityFracY = (byte)(newFracY + 256);
            cell.velocityY = (sbyte)Math.Max(cell.velocityY - gravity, -maxVelocity);
        }
        else
        {
            cell.velocityFracY = (byte)newFracY;
        }

        // Apply lateral exit force at top of lift (fountain effect)
        if (inLift && liftExitLateralForce > 0)
        {
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

        // ===== PHASE 1: Vertical movement (down or up) =====
        int targetY = y + cell.velocityY;
        bool collided = false;

        if (cell.velocityY > 0)
        {
            // Falling - trace path downward
            for (int checkY = y + 1; checkY <= targetY; checkY++)
            {
                if (!CanMoveTo(x, checkY, mat.density))
                {
                    targetY = checkY - 1;
                    collided = true;
                    break;
                }
            }

            if (targetY > y)
            {
                int finalX = ApplyHorizontalVelocity(x, targetY, ref cell, mat.density);
                MoveCell(x, y, finalX, targetY, cell);
                return;
            }
        }
        else if (cell.velocityY == 0)
        {
            // No velocity yet — gravity still pulls. Try to fall 1 cell.
            // This ensures powder falls even before fractional accumulator overflows,
            // and enables density displacement at rest (e.g., sand sinking through water).
            if (CanMoveTo(x, y + 1, mat.density))
            {
                int finalX = ApplyHorizontalVelocity(x, y + 1, ref cell, mat.density);
                MoveCell(x, y, finalX, y + 1, cell);
                return;
            }
        }
        else // cell.velocityY < 0
        {
            // Rising (in lift) - trace path upward
            for (int checkY = y - 1; checkY >= targetY; checkY--)
            {
                if (!CanMoveTo(x, checkY, mat.density))
                {
                    targetY = checkY + 1;
                    collided = true;
                    break;
                }
            }

            if (targetY < y)
            {
                int finalX = ApplyHorizontalVelocity(x, targetY, ref cell, mat.density);
                MoveCell(x, y, finalX, targetY, cell);
                return;
            }
        }

        // On collision, transfer momentum to diagonal movement
        if (collided && cell.velocityY > 1)
        {
            // Retain 70% of velocity
            int remainingVelocity = cell.velocityY * 7 / 10;

            if (remainingVelocity > 0)
            {
                // Split into diagonal: ~71% for 45 degree decomposition (5/7 ~ 0.714)
                int diagonalSpeed = remainingVelocity * 5 / 7;
                diagonalSpeed = Math.Max(1, diagonalSpeed);

                // Determine direction based on available slide
                bool canLeft = CanMoveTo(x - 1, y + 1, mat.density);
                bool canRight = CanMoveTo(x + 1, y + 1, mat.density);

                if (canLeft && canRight)
                {
                    // Both available - use random direction
                    uint hash = HashPosition(x, y, currentFrame);
                    cell.velocityX = (sbyte)((hash & 1) == 0 ? -diagonalSpeed : diagonalSpeed);
                }
                else if (canLeft)
                {
                    cell.velocityX = (sbyte)(-diagonalSpeed);
                }
                else if (canRight)
                {
                    cell.velocityX = (sbyte)diagonalSpeed;
                }
                // else velocityX stays as-is (could be from previous frame)

                cell.velocityY = (sbyte)diagonalSpeed;
            }
        }

        // ===== PHASE 2: Diagonal movement using velocityX =====
        if (cell.velocityX != 0)
        {
            int dx = cell.velocityX > 0 ? 1 : -1;
            int maxDiagonalDist = Math.Abs(cell.velocityX);

            // Trace diagonal path
            int diagonalDist = TraceDiagonalPath(x, y, dx, maxDiagonalDist, mat.density);

            if (diagonalDist > 0)
            {
                // Apply friction: 87.5% retention
                cell.velocityX = (sbyte)(cell.velocityX * 7 / 8);
                cell.velocityY = (sbyte)(cell.velocityY * 7 / 8);

                MoveCell(x, y, x + dx * diagonalDist, y + diagonalDist, cell);
                return;
            }

            // Blocked - try opposite direction
            dx = -dx;
            diagonalDist = TraceDiagonalPath(x, y, dx, maxDiagonalDist, mat.density);

            if (diagonalDist > 0)
            {
                // Reverse direction and apply friction
                cell.velocityX = (sbyte)(-cell.velocityX * 7 / 8);
                cell.velocityY = (sbyte)(cell.velocityY * 7 / 8);

                MoveCell(x, y, x + dx * diagonalDist, y + diagonalDist, cell);
                return;
            }
        }

        // ===== PHASE 3: Fallback to simple slide =====
        // Check slide resistance: higher values = less likely to slide diagonally
        if (mat.slideResistance > 0)
        {
            // Use position-only hash (no currentFrame) so decision is consistent per-position
            uint hash = HashPosition(x, y, 0);
            if ((hash & 255) < mat.slideResistance)
            {
                // Too resistant to slide - stay put
                cell.velocityX = 0;
                cell.velocityY = 0;
                cells[y * width + x] = cell;
                // Keep chunk alive if cell is unsupported (airborne) so gravity can act
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
        // Keep chunk alive if cell is unsupported (airborne) so gravity can act
        if (y + 1 < height && CanMoveTo(x, y + 1, mat.density))
            MarkDirtyInternal(x, y);
    }

    private void SimulateLiquid(int x, int y, Cell cell, MaterialDef mat)
    {
        // Track if we were free-falling before this frame
        bool wasFreeFalling = cell.velocityY > 2;

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

        int newFracY = cell.velocityFracY + netForce;

        if (newFracY >= 256)  // Overflow -> accelerate down
        {
            cell.velocityFracY = (byte)(newFracY - 256);
            cell.velocityY = (sbyte)Math.Min(cell.velocityY + gravity, maxVelocity);
        }
        else if (newFracY < 0)  // Underflow -> accelerate up
        {
            cell.velocityFracY = (byte)(newFracY + 256);
            cell.velocityY = (sbyte)Math.Max(cell.velocityY - gravity, -maxVelocity);
        }
        else
        {
            cell.velocityFracY = (byte)newFracY;
        }

        // Apply lateral exit force at top of lift (fountain effect)
        if (inLift && liftExitLateralForce > 0)
        {
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

        // Try vertical movement based on velocity direction
        if (cell.velocityY > 0)
        {
            // Falling
            if (TryFall(x, y, cell, mat.density))
                return;

            if (TryDiagonalFall(x, y, cell, mat.density))
                return;
        }
        else if (cell.velocityY == 0)
        {
            // No velocity yet — gravity still pulls. Try to fall 1 cell.
            // Enables density displacement at rest (e.g., water on top of oil sorts itself).
            if (CanMoveTo(x, y + 1, mat.density))
            {
                int finalX = ApplyHorizontalVelocity(x, y + 1, ref cell, mat.density);
                MoveCell(x, y, finalX, y + 1, cell);
                return;
            }
            // Also try diagonal fall with no velocity
            if (TryDiagonalFall(x, y, cell, mat.density))
                return;
        }
        else if (cell.velocityY < 0)
        {
            // Rising (in lift)
            if (TryRise(x, y, cell, mat.density))
                return;

            if (TryDiagonalRise(x, y, cell, mat.density))
                return;
        }

        // Can't fall - convert vertical momentum to horizontal spread (Java-style)
        // Key insight: faster falling water should spread MORE, not less
        int velocityBoost = wasFreeFalling ? Math.Abs(cell.velocityY) / 3 : 0;
        int spread = mat.dispersionRate + velocityBoost;

        // Add randomization for natural look
        uint hash = HashPosition(x, y, currentFrame);
        int randomOffset = (int)(hash % 3) - 1;  // -1, 0, or +1
        spread = Math.Max(1, spread + randomOffset);

        // Convert falling velocity to horizontal velocity when landing
        if (wasFreeFalling && cell.velocityX == 0)
        {
            bool goLeft = (hash & 4) != 0;
            cell.velocityX = (sbyte)(goLeft ? -4 : 4);
        }

        // Determine primary direction: follow existing horizontal velocity, or randomize
        bool tryLeftFirst;
        if (cell.velocityX < 0)
            tryLeftFirst = true;
        else if (cell.velocityX > 0)
            tryLeftFirst = false;
        else
            tryLeftFirst = ((x + y + currentFrame) & 1) == 0;

        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        // Find furthest reachable position in primary direction
        int bestDist1 = FindSpreadDistance(x, y, dx1, spread, mat.density);

        // Find furthest reachable position in secondary direction
        int bestDist2 = FindSpreadDistance(x, y, dx2, spread, mat.density);

        // Move to furthest valid position (prefer primary direction on tie)
        if (bestDist1 > 0 && bestDist1 >= bestDist2)
        {
            // Dampen horizontal velocity over time
            cell.velocityX = (sbyte)(cell.velocityX * 7 / 8);
            cell.velocityY = 0;
            MoveCell(x, y, x + dx1 * bestDist1, y, cell);
            return;
        }
        else if (bestDist2 > 0)
        {
            // Reverse direction since we're going the other way
            cell.velocityX = (sbyte)(-cell.velocityX * 7 / 8);
            cell.velocityY = 0;
            MoveCell(x, y, x + dx2 * bestDist2, y, cell);
            return;
        }

        // Stuck - dampen velocities
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
                // Hit solid obstacle, stop searching
                break;
            }
        }
        return bestDist;
    }

    // Trace diagonal path downward (45 degrees) - used for powder momentum
    private int TraceDiagonalPath(int x, int y, int dx, int maxDistance, byte density)
    {
        int traveled = 0;
        for (int dist = 1; dist <= maxDistance; dist++)
        {
            int targetX = x + dx * dist;
            int targetY = y + dist;

            if (!CanMoveTo(targetX, targetY, density))
                break;

            traveled = dist;
        }
        return traveled;
    }

    // Apply a fractional force to the X-axis accumulator, handling multiple overflows/underflows.
    // Large forces (e.g., lift exit lateral force of ±840) can cause multiple 256-overflows
    // per frame, so we compute the number of velocity increments via division.
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

    // Apply horizontal velocity at a target Y position during vertical movement.
    // This ensures velocityX (e.g., from lift exit lateral force) is consumed
    // during free-fall/rise, not just during Phase 2 diagonal movement.
    private int ApplyHorizontalVelocity(int startX, int targetY, ref Cell cell, byte density)
    {
        if (cell.velocityX == 0)
            return startX;

        int dx = cell.velocityX > 0 ? 1 : -1;
        int maxDist = Math.Abs(cell.velocityX);
        int finalX = startX;

        for (int d = 1; d <= maxDist; d++)
        {
            if (!CanMoveTo(startX + dx * d, targetY, density))
                break;
            finalX = startX + dx * d;
        }

        if (finalX != startX)
            cell.velocityX = (sbyte)(cell.velocityX * 7 / 8);

        return finalX;
    }

    // Simple hash for randomization
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint HashPosition(int x, int y, ushort frame)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + frame * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        return h ^ (h >> 16);
    }

    private void SimulateGas(int x, int y, Cell cell, MaterialDef mat)
    {
        // Gases rise - negative gravity using fractional accumulation
        // When accumulator overflows 255, decrement velocity (rising)
        byte oldFracY = cell.velocityFracY;
        cell.velocityFracY += fractionalGravity;
        if (cell.velocityFracY < oldFracY) // Overflow detected
        {
            cell.velocityY = (sbyte)Math.Max(cell.velocityY - gravity, -maxVelocity);
        }

        int targetY = y + cell.velocityY; // velocityY is negative, so this goes up

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

        // Stuck - write back with zeroed velocity
        cell.velocityY = 0;
        cells[y * width + x] = cell;
    }

    private bool TryFall(int x, int y, Cell cell, byte density)
    {
        int targetY = y + cell.velocityY;

        for (int checkY = y + 1; checkY <= targetY; checkY++)
        {
            if (!CanMoveTo(x, checkY, density))
            {
                targetY = checkY - 1;
                break;
            }
        }

        if (targetY > y)
        {
            int finalX = ApplyHorizontalVelocity(x, targetY, ref cell, density);
            MoveCell(x, y, finalX, targetY, cell);
            return true;
        }
        return false;
    }

    private bool TryDiagonalFall(int x, int y, Cell cell, byte density)
    {
        bool tryLeftFirst = ((x + y + currentFrame) & 1) == 0;
        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        if (CanMoveTo(x + dx1, y + 1, density))
        {
            MoveCell(x, y, x + dx1, y + 1, cell);
            return true;
        }

        if (CanMoveTo(x + dx2, y + 1, density))
        {
            MoveCell(x, y, x + dx2, y + 1, cell);
            return true;
        }

        return false;
    }

    private bool TryRise(int x, int y, Cell cell, byte density)
    {
        int targetY = y + cell.velocityY;  // velocityY is negative, so this goes up

        for (int checkY = y - 1; checkY >= targetY; checkY--)
        {
            if (!CanMoveTo(x, checkY, density))
            {
                targetY = checkY + 1;
                break;
            }
        }

        if (targetY < y)
        {
            int finalX = ApplyHorizontalVelocity(x, targetY, ref cell, density);
            MoveCell(x, y, finalX, targetY, cell);
            return true;
        }
        return false;
    }

    private bool TryDiagonalRise(int x, int y, Cell cell, byte density)
    {
        bool tryLeftFirst = ((x + y + currentFrame) & 1) == 0;
        int dx1 = tryLeftFirst ? -1 : 1;
        int dx2 = tryLeftFirst ? 1 : -1;

        if (CanMoveTo(x + dx1, y - 1, density))
        {
            MoveCell(x, y, x + dx1, y - 1, cell);
            return true;
        }

        if (CanMoveTo(x + dx2, y - 1, density))
        {
            MoveCell(x, y, x + dx2, y - 1, cell);
            return true;
        }

        return false;
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
        // Must be within 128x128 extended region (32px buffer around home chunk)
        if (!IsInExtendedRegion(x, y))
            return false;

        int idx = y * width + x;
        Cell target = cells[idx];

        // Cannot move into cells owned by clusters (rigid bodies)
        if (target.ownerId != 0)
            return false;

        // Can move into air
        if (target.materialId == Materials.Air)
        {
            // Ghost belts and walls block external material from entering, but
            // allow material already inside the ghost area to move within/out.
            // currentCellIdx is the index of the cell being simulated.
            if (beltTiles != null && beltTiles.TryGetValue(idx, out BeltTile bt) && bt.isGhost)
            {
                // Only block if source is NOT also in a ghost belt
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

        // Can displace lighter materials (not static, unless passable)
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
            // Source is a lift tile -- restore lift material
            cells[fromIndex] = new Cell { materialId = liftTiles[fromIndex].materialId };
        }
        else if (targetCell.materialId != Materials.Air &&
                 (materials[targetCell.materialId].flags & MaterialFlags.Passable) != 0)
        {
            // Target was passable structure -- don't scatter it, leave Air
            cells[fromIndex] = default;
        }
        else
        {
            // Normal swap
            cells[fromIndex] = targetCell;
        }

        // Mark both positions dirty
        MarkDirtyInternal(fromX, fromY);
        MarkDirtyInternal(toX, toY);

        // Wake adjacent chunks when we vacate a boundary position
        // This allows sand in the adjacent chunk to fall into the newly empty space
        int localX = fromX & 63;
        int localY = fromY & 63;
        if (localX == 0 && fromX > 0)           MarkDirtyInternal(fromX - 1, fromY); // Wake left chunk
        if (localX == 63 && fromX < width - 1)  MarkDirtyInternal(fromX + 1, fromY); // Wake right chunk
        if (localY == 0 && fromY > 0)           MarkDirtyInternal(fromX, fromY - 1); // Wake chunk above
        if (localY == 63 && fromY < height - 1) MarkDirtyInternal(fromX, fromY + 1); // Wake chunk below
    }

    private void MarkDirtyInternal(int x, int y)
    {
        int chunkX = x >> 6; // x / 64
        int chunkY = y >> 6; // y / 64

        if (chunkX < 0 || chunkX >= chunksX || chunkY < 0 || chunkY >= chunksY)
            return;

        int chunkIndex = chunkY * chunksX + chunkX;

        ChunkState chunk = chunks[chunkIndex];
        chunk.flags |= ChunkFlags.IsDirty;

        int localX = x & 63; // x % 64
        int localY = y & 63; // y % 64

        // Update dirty bounds (race conditions on min/max are acceptable - worst case is extra work)
        if (localX < chunk.minX) chunk.minX = (ushort)localX;
        if (localX > chunk.maxX) chunk.maxX = (ushort)localX;
        if (localY < chunk.minY) chunk.minY = (ushort)localY;
        if (localY > chunk.maxY) chunk.maxY = (ushort)localY;

        chunks[chunkIndex] = chunk;
    }
}
