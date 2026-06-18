using UnityEngine;
using TMPro;

public class ResolutionDropdown : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private string param = "Resolution";

    public string ParamLabel => param;
    public int ResolutionX { get; private set; } = 120;
    public int ResolutionY { get; private set; } = 120;
    public int Resolution => ResolutionX;

    void Awake()
    {
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>() ?? GetComponentInChildren<TMP_Dropdown>(true);

        if (dropdown != null)
            ParseOption(dropdown.value);
    }

    void Start()
    {
        if (dropdown == null)
            return;

        dropdown.onValueChanged.RemoveListener(OnChanged);
        dropdown.onValueChanged.AddListener(OnChanged);
        ParseOption(dropdown.value);
    }

    public void OnChanged(int index)
    {
        ParseOption(index);
        Debug.Log($"[ResolutionDropdown] index {index} -> {ResolutionX}x{ResolutionY}");
        PynqParamController.NotifySliderReleased();
    }

    private void ParseOption(int index)
    {
        if (dropdown == null || index < 0 || index >= dropdown.options.Count)
            return;

        string text = dropdown.options[index].text;
        if (TryParseResolution(text, out int w, out int h))
        {
            ResolutionX = w;
            ResolutionY = h;
        }
    }

    // parses the leading "WxH" from a label like "1920x1080(Full HD)"
    private static bool TryParseResolution(string text, out int w, out int h)
    {
        w = h = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        int xPos = text.IndexOfAny(new[] { 'x', 'X' });
        if (xPos <= 0)
            return false;

        string left = ExtractLeadingInt(text.Substring(0, xPos));
        string right = ExtractLeadingInt(text.Substring(xPos + 1));

        return int.TryParse(left, out w) & int.TryParse(right, out h) && w > 0 && h > 0;
    }

    private static string ExtractLeadingInt(string s)
    {
        s = s.Trim();
        int end = 0;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return s.Substring(0, end);
    }
}
