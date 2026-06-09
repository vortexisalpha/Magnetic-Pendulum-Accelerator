using UnityEngine;

//maps an fss word to a display color. fss word layout (matches the float
//reference): bits[5:2]=base step category, bit[1]=timeout_any, bit[0]=sensitive.
//two-colour map: sensitive vs everything else (stable/timeout).
public static class FssColorizer
{
    private static readonly Color32 Stable = new Color32(20, 26, 41, 255);
    private static readonly Color32 Sensitive = new Color32(255, 191, 31, 255);

    public static Color32 Colorize(int pixel)
    {
        return (pixel & 0x1) != 0 ? Sensitive : Stable;
    }
}
