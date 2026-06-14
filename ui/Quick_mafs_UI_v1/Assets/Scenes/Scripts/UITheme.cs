using UnityEngine;
using TMPro;

// Applies one consistent TMP font and a small size hierarchy across all text in
// the UI so labels look uniform. Put this on a root object (e.g. the top UI
// GameObject), assign a font, and press Apply (or it runs on Awake).
public class UITheme : MonoBehaviour
{
    [Header("Font")]
    [Tooltip("Font applied to every TMP text under this object.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Size tiers (relative)")]
    [Tooltip("Texts larger than this keep title sizing.")]
    [SerializeField] private float titleThreshold = 26f;
    [SerializeField] private float titleSize = 28f;
    [SerializeField] private float labelSize = 18f;

    [Header("Colour")]
    [SerializeField] private bool overrideColors = false;
    [SerializeField] private Color labelColor = new Color(0.9f, 0.92f, 0.95f);

    void Awake() => Apply();

    [ContextMenu("Apply")]
    public void Apply()
    {
        var texts = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts)
        {
            if (font != null) t.font = font;

            // keep big headings big, normalise everything else to label size
            t.fontSize = t.fontSize >= titleThreshold ? titleSize : labelSize;

            if (overrideColors) t.color = labelColor;
        }
    }
}
