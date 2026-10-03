using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent expand/restore corners, without a font glyph or texture.</summary>
public sealed class FullscreenIcon : MaskableGraphic
{
    private bool restore;
    public void SetRestore(bool value) { if (restore == value) return; restore = value; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        float size = Mathf.Min(rect.width, rect.height), edge = size * .43f;
        float arm = size * .30f, thickness = Mathf.Max(1.5f, size * .085f);
        foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
        {
            var corner = rect.center + new Vector2(x, y) * (restore ? edge - arm : edge);
            float direction = restore ? 1 : -1;
            Bar(mesh, corner, corner + Vector2.right * (x * direction * arm), thickness);
            Bar(mesh, corner, corner + Vector2.up * (y * direction * arm), thickness);
        }
    }
    private void Bar(VertexHelper mesh, Vector2 a, Vector2 b, float thickness)
    {
        var normal = new Vector2(-(b-a).y, (b-a).x).normalized * (thickness * .5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(a-normal, color, Vector2.zero); mesh.AddVert(a+normal, color, Vector2.zero);
        mesh.AddVert(b+normal, color, Vector2.zero); mesh.AddVert(b-normal, color, Vector2.zero);
        mesh.AddTriangle(start, start+1, start+2); mesh.AddTriangle(start, start+2, start+3);
    }
}
