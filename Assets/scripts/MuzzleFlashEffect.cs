using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reusable low-poly flame solids, angular embers and a brief shadowless light.</summary>
[DefaultExecutionOrder(400)]
public sealed class MuzzleFlashEffect : MonoBehaviour
{
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock properties;
    private Light flashLight;
    private Transform follow;
    private Vector3 origin;
    private Quaternion orientation, followRotation;
    private float age, duration, brightness, variation;
    private int lastSequence, shotFrame;
    private static Mesh sharedMesh;
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    public void Play(Transform muzzle, Vector3 position, Vector3 direction, int sequence, float scale, float intensity, float seconds)
    {
        if (sequence <= lastSequence) return;
        if (meshRenderer == null && !Initialize()) return;
        lastSequence = sequence;
        follow = muzzle; origin = position;
        // Stable per-shot variation does not change Unity's global recoil/spread random state.
        variation = ((uint)sequence * 2654435761u & 65535u) / 65535f;
        Vector3 flashDirection = muzzle != null ? muzzle.forward : direction;
        orientation = Quaternion.LookRotation(flashDirection.sqrMagnitude > .0001f ? flashDirection : Vector3.forward) * Quaternion.Euler(0, 0, variation * 360f);
        if (follow != null) followRotation = Quaternion.Inverse(follow.rotation) * orientation;
        duration = Mathf.Clamp(seconds, .015f, .1f);
        brightness = Mathf.Max(0f, intensity);
        age = 0f; shotFrame = Time.frameCount;
        transform.localScale = new Vector3(.055f, .055f, .22f) * Mathf.Max(.01f, scale) * Mathf.Lerp(.85f, 1.15f, variation);
        gameObject.SetActive(true);
        Present();
    }
    private bool Initialize()
    {
        var material = Resources.Load<Material>("VFX/MuzzleFlash");
        if (material == null) { Debug.LogError("Missing VFX/MuzzleFlash material.", this); return false; }
        sharedMesh = sharedMesh != null ? sharedMesh : BuildMesh();
        gameObject.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        properties = new MaterialPropertyBlock();
        var lightObject = new GameObject("Flash Light");
        lightObject.transform.SetParent(transform, false);
        flashLight = lightObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, .54f, .18f);
        flashLight.range = 2.2f;
        flashLight.shadows = LightShadows.None;
        return true;
    }
    private void LateUpdate()
    {
        if (meshRenderer == null) return;
        if (shotFrame != Time.frameCount) age += Time.deltaTime;
        if (age >= .14f) { gameObject.SetActive(false); return; }
        Present();
    }
    private void Present()
    {
        transform.SetPositionAndRotation(follow != null ? follow.position : origin, follow != null ? follow.rotation * followRotation : orientation);
        properties.SetFloat(AgeId, age);
        properties.SetFloat(DurationId, duration);
        properties.SetFloat(IntensityId, brightness);
        properties.SetFloat(SeedId, variation);
        meshRenderer.SetPropertyBlock(properties);
        flashLight.intensity = 2.5f * brightness * Mathf.Pow(Mathf.Clamp01(1f - age / duration), 2);
        flashLight.enabled = flashLight.intensity > .01f;
    }
    private static Mesh BuildMesh()
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var kinds = new List<Vector2>();
        var triangles = new List<int>();
        var palette = new[] {
            new Color(1f, .72f, .12f), new Color(1f, .43f, .035f),
            new Color(1f, .87f, .32f), new Color(.8f, .25f, .015f), new Color(1f, .59f, .06f)
        };
        void Face(Vector3 a, Vector3 b, Vector3 c, Color color, float kind = 0, float index = 0)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int i = 0; i < 3; i++) { colors.Add(color); kinds.Add(new Vector2(kind, index)); triangles.Add(first + i); }
        }
        void Crystal(Vector3 center, Vector3 back, Vector3 tip, float radius, int sides, bool core, float kind = 0, float index = 0)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                float b = (i + 1) * Mathf.PI * 2 / sides;
                var p = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                var q = center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * radius;
                Color color = core ? Color.Lerp(new Color(1f, .95f, .68f), Color.white, i / (float)sides) : palette[i % palette.Length];
                Face(back, q, p, color, kind, index);
                Face(p, q, tip, color * (core ? 1f : .9f), kind, index);
            }
        }
        // Closed solids with a single flat color per triangle, rather than transparent flame cards.
        Crystal(new Vector3(0, 0, .2f), Vector3.zero, new Vector3(0, 0, 1.1f), .43f, 5, false);
        Crystal(new Vector3(0, 0, .075f), new Vector3(0, 0, -.015f), new Vector3(0, 0, .65f), .23f, 5, true);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.PI * 2 / 5;
            var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
            var tangent = new Vector3(-radial.y, radial.x, 0);
            var a = radial * .12f + tangent * .2f + Vector3.forward * .07f;
            var b = radial * .12f - tangent * .2f + Vector3.forward * .07f;
            var c = radial * .32f + Vector3.forward * .4f;
            var tip = radial * (i % 2 == 0 ? 1.05f : .8f) + Vector3.forward * (.4f + i * .045f);
            Face(a, b, tip, palette[i]);
            Face(b, c, tip, palette[(i + 1) % 5]);
            Face(c, a, tip, palette[(i + 2) % 5]);
            Face(a, c, b, palette[i]);
        }
        for (int i = 0; i < 5; i++)
            Crystal(new Vector3(0, 0, .12f), new Vector3(0, 0, .065f), new Vector3(0, 0, .175f), .065f, 3, false, 2, i);
        var mesh = new Mesh { name = "Low-poly muzzle flash" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(1, kinds); mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(new Vector3(0, 0, .5f), new Vector3(10, 10, 5));
        mesh.UploadMeshData(true);
        return mesh;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedMesh()
    {
        if (sharedMesh != null) Destroy(sharedMesh);
        sharedMesh = null;
    }
}
