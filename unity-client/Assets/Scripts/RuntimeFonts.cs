using UnityEngine;

/// <summary>
/// Uses an OS-provided CJK font so the demo does not need a bundled font asset.
/// </summary>
public static class RuntimeFonts
{
    private static Font chineseFont;
    private static bool fallbackWarningIssued;

    public static Font UiFont
    {
        get
        {
            if (chineseFont != null)
            {
                return chineseFont;
            }

            chineseFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Noto Sans SC", "SimHei", "Arial" },
                24);
            if (chineseFont == null)
            {
                chineseFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (!fallbackWarningIssued)
                {
                    fallbackWarningIssued = true;
                    Debug.LogWarning("No CJK system font was found; using Unity's fallback font.");
                }
            }
            return chineseFont;
        }
    }
}
