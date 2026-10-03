using UnityEngine;
using UnityEngine.UI;

/// <summary>Embedded typography shared by prefab and runtime UI in every player.</summary>
public static class GameUIStyle
{
    private static Font font;
    public static Font Font => font != null ? font : font = Resources.Load<Font>("UI/Fonts/Jura-Medium");
    public static readonly Color Panel = new Color(.035f, .049f, .060f, .97f);
    public static readonly Color Card = new Color(.075f, .096f, .112f, .98f);
    public static readonly Color Accent = new Color(.90f, .79f, .56f);
    public static readonly Color Text = new Color(.94f, .95f, .92f);
    public static readonly Color Muted = new Color(.66f, .72f, .75f);
    private static Sprite rounded;

    // A small sliced UI shape; generated once, shared by all runtime panels.
    public static void Surface(Image image, Color color)
    {
        if (rounded == null)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "UI rounded surface"; texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - 15.5f) - 7.5f, 0);
                    float dy = Mathf.Max(Mathf.Abs(y - 15.5f) - 7.5f, 0);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(8f - Mathf.Sqrt(dx * dx + dy * dy))));
                }
            texture.Apply(false, true);
            rounded = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 100, 0,
                SpriteMeshType.FullRect, Vector4.one * 10);
        }
        image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color;
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        Surface((Image)button.targetGraphic, Color.white);
        var colors = button.colors;
        colors.normalColor = primary ? Accent : new Color(.105f, .135f, .16f);
        colors.highlightedColor = primary ? new Color(1, .9f, .69f) : new Color(.15f, .20f, .23f);
        colors.pressedColor = primary ? new Color(.72f, .60f, .39f) : new Color(.045f, .065f, .08f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(.14f, .17f, .19f, .85f);
        colors.fadeDuration = .14f; button.colors = colors;
    }
}
