using UnityEngine;

/// <summary>Embedded typography shared by prefab and runtime UI in every player.</summary>
public static class GameUIStyle
{
    private static Font font;
    public static Font Font => font != null ? font : font = Resources.Load<Font>("UI/Fonts/Jura-Medium");
}
