using UnityEngine;

//decoded 0x14 traj payload from the board: echoed pixel id + trajectory points
public sealed class TrajectoryMessage
{
    public uint pixelId;
    public Vector2[] points;
}
