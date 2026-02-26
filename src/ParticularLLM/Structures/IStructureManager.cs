using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Common interface for structure managers.
/// Enables registration-based iteration in SimulationManager and ghost rendering.
/// </summary>
public interface IStructureManager
{
    /// <summary>
    /// Update ghost block states (activate blocks where terrain has cleared).
    /// </summary>
    void UpdateGhostStates();

    /// <summary>
    /// Populate the list with cell positions of ghost blocks for rendering.
    /// </summary>
    void GetGhostBlockPositions(List<(int x, int y)> positions);

    /// <summary>
    /// Check if this manager has a structure tile at the given cell position.
    /// </summary>
    bool HasStructureAt(int x, int y);
}
