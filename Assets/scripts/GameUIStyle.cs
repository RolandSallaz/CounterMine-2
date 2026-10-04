using UnityEngine;
using UnityEngine.UI;

/// <summary>Embedded typography shared by prefab and runtime UI in every player.</summary>
public static class GameUIStyle
{
    private static Font font;
    public static Font Font => font != null ? font : font = Resources.Load<Font>("UI/Fonts/Jura-Medium");
    public static readonly Color Panel = new Color(.025f, .038f, .057f, .97f);
    public static readonly Color Card = new Color(.065f, .090f, .122f, .98f);
    public static readonly Color Accent = new Color(.98f, .74f, .37f);
    public static readonly Color Text = new Color(.94f, .97f, 1f);
    public static readonly Color Muted = new Color(.65f, .73f, .82f);
    public static readonly Color Border = new Color(.48f,.64f,.78f,.18f);
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
        colors.normalColor = primary ? Accent : Card;
        colors.highlightedColor = primary ? new Color(1, .84f, .53f) : new Color(.12f, .18f, .24f);
        colors.pressedColor = primary ? new Color(.76f, .52f, .23f) : new Color(.045f, .075f, .11f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(.14f, .17f, .19f, .85f);
        colors.fadeDuration = .12f; button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        var feedback=button.GetComponent<GameUIButtonFeedback>();
        if(feedback==null)feedback=button.gameObject.AddComponent<GameUIButtonFeedback>();
        feedback.Configure(primary);
    }

    public static void PanelSurface(Image image,bool shadow=false)
    {
        Surface(image,Panel);
        var outline=image.GetComponent<Outline>()??image.gameObject.AddComponent<Outline>();
        outline.effectColor=Border;outline.effectDistance=new Vector2(1,-1);
        if(shadow)
        {
            Shadow depth=null;
            foreach(var effect in image.GetComponents<Shadow>())if(!(effect is Outline)){depth=effect;break;}
            if(depth==null)depth=image.gameObject.AddComponent<Shadow>();
            depth.effectColor=new Color(0,0,0,.30f);depth.effectDistance=new Vector2(0,-6);
        }
    }

    public static void StyleInput(InputField field)
    {
        Surface(field.GetComponent<Image>(),Card);
        var outline=field.GetComponent<Outline>()??field.gameObject.AddComponent<Outline>();
        outline.effectColor=Border;outline.effectDistance=new Vector2(1,-1);
        field.customCaretColor=true;field.caretColor=Accent;field.selectionColor=new Color(Accent.r,Accent.g,Accent.b,.28f);
        field.navigation=new Navigation {mode=Navigation.Mode.Automatic};
        var colors=field.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.88f,.94f,1f);
        colors.selectedColor=new Color(.80f,.90f,1f);field.colors=colors;
    }
}
