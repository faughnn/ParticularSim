namespace ParticularLLM;

/// <summary>
/// Data for a single 16x16 piston instance.
/// The base bar is a permanent strip of PistonBase cells.
/// The plate is a kinematic cluster that moves within the block.
/// Fill area grows behind the plate to seal the chamber.
/// </summary>
public class PistonData
{
    public int BaseCellX;             // Grid-snapped origin of 16x16 block
    public int BaseCellY;
    public PistonDirection Direction;

    public ClusterData? ArmCluster;   // The plate cluster (kinematic, 2x16 or 16x2)

    public float RetractedX;         // Plate cell-space pos when fully retracted (strokeT=0)
    public float RetractedY;
    public float ExtendedX;          // Plate cell-space pos when fully extended (strokeT=1)
    public float ExtendedY;

    public float CurrentStrokeT;     // Actual 0..1 position, may lag if stalled
    public int LastFillExtent;       // Cells of fill behind plate (for delta updates)
}
