using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent tactical HUD marks, drawn without textures or font glyphs.</summary>
public sealed class HitDirectionArrow : MaskableGraphic
{
    public enum Mark { Damage, Chevron, Grenade }
    [SerializeField] private Mark style;
    public Mark Style { get => style; set { style = value; SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (rectTransform.rect.width <= 0f || rectTransform.rect.height <= 0f) return;
        if (style == Mark.Grenade) { Grenade(mesh); return; }
        if (style == Mark.Chevron)
        {
            Polygon(mesh, color, new Vector2(-.46f, -.2f), new Vector2(0, .42f),
                new Vector2(0, .16f), new Vector2(-.28f, -.2f));
            Polygon(mesh, color, new Vector2(0, .42f), new Vector2(.46f, -.2f),
                new Vector2(.28f, -.2f), new Vector2(0, .16f));
            return;
        }
        // Tapered crescent with a thin bright rim and a sharp central bearing mark.
        const int steps = 32;
        for (int i = 0; i < steps; i++)
        {
            float a = Mathf.Lerp(-1f, 1f, i / (float)steps);
            float b = Mathf.Lerp(-1f, 1f, (i + 1f) / steps);
            ArcStrip(mesh, a, b, false);
            ArcStrip(mesh, a, b, true);
        }
        Polygon(mesh, Color.Lerp(color, Color.white, .35f),
            new Vector2(-.065f, .31f), new Vector2(0, .49f), new Vector2(.065f, .31f));
    }

    private void ArcStrip(VertexHelper mesh, float a, float b, bool rim)
    {
        float ya = .32f - .57f * a * a;
        float yb = .32f - .57f * b * b;
        float ta = .025f + .29f * (1f - a * a);
        float tb = .025f + .29f * (1f - b * b);
        Color tint = rim ? Color.Lerp(color, Color.white, .3f) : color;
        tint.a *= Mathf.Lerp(.25f, 1f, 1f - Mathf.Abs((a + b) * .5f));
        Polygon(mesh, tint, new Vector2(a * .49f, ya), new Vector2(b * .49f, yb),
            new Vector2(b * .49f, yb - (rim ? .035f : tb)),
            new Vector2(a * .49f, ya - (rim ? .035f : ta)));
    }

    private void Grenade(VertexHelper mesh)
    {
        Color plate = new Color(.035f, .045f, .055f, .94f);
        Polygon(mesh, plate, new Vector2(-.5f,-.3f), new Vector2(-.3f,-.5f),
            new Vector2(.3f,-.5f), new Vector2(.5f,-.3f), new Vector2(.5f,.3f),
            new Vector2(.3f,.5f), new Vector2(-.3f,.5f), new Vector2(-.5f,.3f));
        // Familiar fragmentation-grenade silhouette: shoulder, body, fuse and bent lever.
        Polygon(mesh, color, new Vector2(-.2f,-.34f), new Vector2(.18f,-.34f),
            new Vector2(.27f,-.23f), new Vector2(.24f,.09f), new Vector2(.12f,.22f),
            new Vector2(-.14f,.22f), new Vector2(-.26f,.07f), new Vector2(-.28f,-.22f));
        Box(mesh, color, -.12f, .24f, .08f, .34f);
        Polygon(mesh, color, new Vector2(-.09f,.39f), new Vector2(.23f,.36f),
            new Vector2(.36f,.05f), new Vector2(.29f,.02f), new Vector2(.17f,.29f), new Vector2(-.09f,.31f));
        Box(mesh, plate, -.25f, -.12f, .25f, -.075f);
        Box(mesh, plate, -.22f, .045f, .21f, .085f);
        Box(mesh, plate, -.035f, -.32f, .01f, .2f);
    }

    private void Box(VertexHelper mesh, Color tint, float x0, float y0, float x1, float y1)
        => Polygon(mesh, tint, new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x1,y1), new Vector2(x0,y1));

    private void Polygon(VertexHelper mesh, Color tint, params Vector2[] points)
    {
        Rect rect = rectTransform.rect;
        int first = mesh.currentVertCount;
        foreach (var point in points)
            mesh.AddVert(rect.center + Vector2.Scale(point, rect.size), tint, Vector2.zero);
        for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
    }
}
