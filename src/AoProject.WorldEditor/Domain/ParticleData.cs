namespace AoProject.WorldEditor.Domain;

internal sealed class ParticleData
{
    public string Name { get; set; } = string.Empty;
    public int NumOfParticles { get; set; }
    public int NumGrhs { get; set; }
    public int Id { get; set; }
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Angle { get; set; }
    public int VecX1 { get; set; }
    public int VecX2 { get; set; }
    public int VecY1 { get; set; }
    public int VecY2 { get; set; }
    public int Life1 { get; set; }
    public int Life2 { get; set; }
    public int Friction { get; set; }
    public byte Spin { get; set; }
    public float SpinSpeedLow { get; set; }
    public float SpinSpeedHigh { get; set; }
    public byte AlphaBlend { get; set; }
    public byte Gravity { get; set; }
    public int GravityStrength { get; set; }
    public int BounceStrength { get; set; }
    public byte XMove { get; set; }
    public byte YMove { get; set; }
    public int MoveX1 { get; set; }
    public int MoveX2 { get; set; }
    public int MoveY1 { get; set; }
    public int MoveY2 { get; set; }
    public int[] Grhs { get; set; } = [];
    public int[] ColorTint { get; set; } = new int[4];
    public float Speed { get; set; }
    public int LifeCounter { get; set; }
    public bool GrhResize { get; set; }
    public short GrhResizeX { get; set; }
    public short GrhResizeY { get; set; }
}
