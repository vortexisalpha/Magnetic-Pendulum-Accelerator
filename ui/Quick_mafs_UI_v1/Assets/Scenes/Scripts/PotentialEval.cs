
using UnityEngine;

public static class PotentialEvaluator
{
    public static float Evaluate(float x, float y, Vector2[] magnets, float omega, float mu, float h)
    {
        float v = 0.5f * omega * omega * ( x * x + y * y); 
        foreach ( var m in magnets)
        {
            float d_x = m.x - x, d_y = m.y - y; 
            v -= mu / Mathf.Sqrt(d_x * d_x + d_y * d_y + h * h); 
        }
        return v; 
    }
}