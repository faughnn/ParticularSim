namespace ParticularLLM;

public static class PhysicsSettings
{
    public const byte FractionalGravity = 17;
    public const int CellGravityAccel = 1;
    public const int MaxVelocity = 16;
    public const byte LiftForce = 20;
    // Must be >= 256 so even center cells (lateralSign=±1) get velocityX > 0.
    // Edge cells (sign=±7) get velocityX ≈ ±7, center cells get ≈ ±1.
    public const short LiftExitLateralForce = 260;
}
