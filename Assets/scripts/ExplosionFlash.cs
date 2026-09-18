using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Seeded, pooled grenade burst: hot lobes cool into smoke, with ballistic embers and a ground wave.</summary>
public sealed class ExplosionFlash : MonoBehaviour
{
    private const float Lifetime = 1.65f;
    private const int PoolLimit = 12;
    private static readonly List<ExplosionFlash> Pool = new List<ExplosionFlash>();
    private static readonly RaycastHit[] GroundHits = new RaycastHit[24];
    private static Material material;
    private static AudioClip boomClip;
    private static int cursor;
    private readonly List<Vector3> vertices = new List<Vector3>(6000);
    private readonly List<Color> colors = new List<Color>(6000);
    private readonly List<int> indices = new List<int>(6000);
    private readonly Vector3[] lobes = new Vector3[13];
    private readonly float[] sizes = new float[13];
    private readonly Vector3[] sparks = new Vector3[28];
    private Mesh mesh;
    private Light flash;
    private float age;
    private bool grounded;
    private Vector3 ground;
    private Quaternion groundRotation;

    public static void Spawn(Vector3 point, int seed)
    {
        // Audio and camera feedback do not depend on the visual material being available.
        ShakeLocalCamera(point);
        if (boomClip == null) boomClip = Resources.Load<AudioClip>("Audio/Explosion/explosion");
        GameAudio.Play(boomClip, point, .55f, 70f, false, 1f);
        if (material == null) material = Resources.Load<Material>("VFX/StylizedExplosion");
        if (material == null) return;
        Pool.RemoveAll(item => item == null);
        var effect = Pool.Find(item => !item.gameObject.activeSelf);
        if (effect == null && Pool.Count < PoolLimit)
        {
            effect = new GameObject("Grenade Explosion").AddComponent<ExplosionFlash>();
            effect.Build();
            Pool.Add(effect);
        }
        if (effect == null) effect = Pool[cursor++ % Pool.Count];
        effect.transform.position = point + Vector3.up * .25f;
        effect.age = 0f;
        effect.Seed(seed);
        effect.FindGround(point);
        effect.gameObject.SetActive(true);
        effect.Present(0f);
    }

    private void Build()
    {
        mesh = new Mesh { name = "Stylized grenade burst" };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        flash = gameObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, .57f, .22f);
        flash.range = 19f;
        flash.shadows = LightShadows.None;
    }

    private void Seed(int seed)
    {
        var random = new System.Random(seed);
        float Next() => (float)random.NextDouble();
        for (int i = 0; i < lobes.Length; i++)
        {
            float angle = i * 2.399963f + Next() * .5f;
            lobes[i] = i == 0 ? Vector3.up * .15f :
                new Vector3(Mathf.Cos(angle), .15f + Next() * .85f, Mathf.Sin(angle)).normalized;
            sizes[i] = .65f + Next() * .55f;
        }
        for (int i = 0; i < sparks.Length; i++)
        {
            float angle = Next() * Mathf.PI * 2f;
            sparks[i] = new Vector3(Mathf.Cos(angle), .15f + Next() * 1.5f, Mathf.Sin(angle)).normalized * (7f + Next() * 13f);
        }
    }

    private void FindGround(Vector3 point)
    {
        grounded = false;
        float nearest = 2.5f;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * .3f, Vector3.down, GroundHits, nearest, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = GroundHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null || hit.distance >= nearest) continue;
            nearest = hit.distance;
            grounded = true;
            ground = hit.point + hit.normal * .055f - transform.position;
            groundRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Lifetime) { gameObject.SetActive(false); return; }
        Present(age);
    }

    // Explicit time also allows deterministic editor previews of each phase.
    public void Present(float time)
    {
        vertices.Clear(); colors.Clear(); indices.Clear();
        float growth = 1f - Mathf.Exp(-time * 13f);
        for (int i = 0; i < lobes.Length; i++)
        {
            float cooling = Mathf.Clamp01((time - .12f - i * .009f) / .42f);
            Vector3 center = lobes[i] * (growth * (i == 0 ? .3f : 2.2f));
            center.y += time * (1.1f + sizes[i] * .4f);
            float radius = sizes[i] * (.22f + growth * 1.05f + time * .3f);
            Color fire = Color.Lerp(new Color(3.2f, 2.5f, 1.1f), new Color(1.5f, .24f, .025f), Mathf.Clamp01(time * 4f + i * .035f));
            Color smoke = Color.Lerp(new Color(.095f, .105f, .12f), new Color(.24f, .225f, .20f), i / 12f);
            Color tint = Color.Lerp(fire, smoke, cooling);
            tint.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.85f, Lifetime - i * .012f, time));
            Blob(center, radius, tint, i);
        }
        // Brief spear-shaped jets give the initial detonation a sharp, asymmetric silhouette.
        if (time < .22f)
        {
            float t = time / .22f;
            for (int i = 1; i < 10; i++)
            {
                Vector3 direction = lobes[i];
                Vector3 tip = direction * (1.1f + 4f * Mathf.Sqrt(t));
                Vector3 center = direction * growth;
                Vector3 side = Vector3.Cross(direction, Vector3.up).normalized * (.35f * (1f - t));
                Vector3 other = Vector3.Cross(direction, side).normalized * (.28f * (1f - t));
                Color hot = new Color(3.5f, 1.8f, .35f, 1f - t);
                Triangle(tip, center + side, center + other, hot);
                Triangle(tip, center + other, center - side, hot);
                Triangle(tip, center - side, center - other, hot);
                Triangle(tip, center - other, center + side, hot);
            }
        }
        if (grounded && time < .48f) Wave(time);
        for (int i = 0; i < sparks.Length; i++)
        {
            float life = .48f + (i % 7) * .085f;
            if (time >= life) continue;
            Vector3 velocity = sparks[i];
            Vector3 tip = velocity * time + Vector3.down * (5f * time * time);
            Vector3 direction = (velocity + Vector3.down * (10f * time)).normalized;
            Vector3 tail = tip - direction * Mathf.Lerp(.65f, .12f, time / life);
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized * (.045f * (1f - time / life));
            Color tint = new Color(3f, 1.35f, .16f, 1f - time / life);
            Triangle(tip, tail + side, tail - side, tint);
            side = Vector3.Cross(direction, side).normalized * .02f * (1f - time / life);
            Triangle(tip, tail + side, tail - side, tint);
        }
        mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        flash.intensity = 9f * Mathf.Pow(Mathf.Clamp01(1f - time / .24f), 2f);
        flash.enabled = time < .24f;
    }

    private void Blob(Vector3 center, float radius, Color tint, int seed)
    {
        const int rings = 5, sides = 8;
        Vector3 Point(int lat, int lon)
        {
            float p = lat * Mathf.PI / rings;
            float a = lon * Mathf.PI * 2f / sides + seed * .73f;
            float rough = 1f + .12f * Mathf.Sin(p) * Mathf.Sin(lat * 7.3f + (lon % sides) * 3.1f + seed);
            return center + new Vector3(Mathf.Sin(p) * Mathf.Cos(a), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Sin(a)) * radius * rough;
        }
        for (int lat = 0; lat < rings; lat++)
            for (int lon = 0; lon < sides; lon++)
            {
                Vector3 a = Point(lat, lon), b = Point(lat + 1, lon);
                Vector3 c = Point(lat + 1, lon + 1), d = Point(lat, lon + 1);
                Color face = tint * (.72f + .28f * (1f - lat / (float)rings));
                face.a = tint.a;
                if (lat > 0) Triangle(a, b, d, face);
                if (lat < rings - 1) Triangle(b, c, d, face);
            }
    }

    private void Wave(float time)
    {
        float t = time / .48f;
        float radius = .4f + 8f * (1f - Mathf.Pow(1f - t, 3f));
        float width = Mathf.Lerp(.4f, .08f, t);
        Color tint = Color.Lerp(new Color(2f, 1.2f, .45f), new Color(.45f, .32f, .18f), t);
        tint.a = (1f - t) * .8f;
        for (int i = 0; i < 64; i++)
        {
            if (i % 9 == 0) continue;
            float a = i * Mathf.PI * 2f / 64, b = (i + 1) * Mathf.PI * 2f / 64;
            Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 v = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
            Vector3 p = ground + groundRotation * (u * radius), q = ground + groundRotation * (v * radius);
            Vector3 r = ground + groundRotation * (u * (radius - width)), s = ground + groundRotation * (v * (radius - width));
            Triangle(p, q, r, tint); Triangle(q, s, r, tint);
        }
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color tint)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        colors.Add(tint); colors.Add(tint); colors.Add(tint);
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
    }

    private static void ShakeLocalCamera(Vector3 point)
    {
        foreach (var player in PlayerHealth.ActivePlayers)
        {
            if (player == null || BotController.IsBot(player)) continue;
            if (PhotonNetwork.InRoom && (player.photonView == null || !player.photonView.IsMine)) continue;
            float distance = Vector3.Distance(player.transform.position + Vector3.up * 1.5f, point);
            if (distance >= 42f) continue;
            float strength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 42f, distance));
            player.GetComponent<PlayerCameraLook>()?.AddExplosionShake(point, strength);
        }
    }

    private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared() { Pool.Clear(); cursor = 0; material = null; boomClip = null; }
}
