namespace Freefrag;

public static class BotFacing
{
    /// <summary>
    /// Pitch/yaw in degrees (Source convention: yaw = atan2(dy, dx), pitch positive = looking
    /// down) from eye position `from` to `to`. Returns (0, 0) when the points coincide.
    /// </summary>
    public static (float Pitch, float Yaw) AnglesTo(float fromX, float fromY, float fromZ,
                                                   float toX, float toY, float toZ)
    {
        float dx = toX - fromX;
        float dy = toY - fromY;
        float dz = toZ - fromZ;
        if (dx == 0f && dy == 0f && dz == 0f)
            return (0f, 0f);

        double yaw = 180.0 * Math.Atan2(dy, dx) / Math.PI;
        double horizontal = Math.Sqrt((double)dx * dx + (double)dy * dy);
        double pitch = 180.0 * Math.Atan2(-dz, horizontal) / Math.PI;
        return ((float)pitch, (float)yaw);
    }
}
