using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Master-authoritative grenade: every client simulates the same ballistic flight
/// locally, only the master's copy deals damage. Explosion is a procedural fireball.</summary>
public sealed class GrenadeProjectile : MonoBehaviour
{
    private const float Gravity = 9.81f;
    private const float Fuse = 2.4f;
    private const float BlastRadius = 13f;
    private const float FullDamageFraction = .5f;
    private const float MaxDamage = 100f;
    private const float MinDamage = 50f;
    private static readonly RaycastHit[] CastHits = new RaycastHit[16];
    private static GameObject grenadeModel;
    /// <summary>All live grenades on this client (visual copies included).</summary>
    public static readonly HashSet<GrenadeProjectile> Active = new HashSet<GrenadeProjectile>();

    private Vector3 position, velocity;
    private double throwTime;
    private Player killer;
    private int seed;
    private bool authority;
    private GameObject visual;
    /// <summary>Current simulated position (follows bounces, valid until the blast).</summary>
    public Vector3 Position => position;

    private void OnDestroy() => Active.Remove(this);

    public static void Launch(Vector3 start, Vector3 velocity, double throwTime, Player killer, int seed)
    {
        var go = new GameObject("Grenade");
        go.transform.position = start;
        var projectile = go.AddComponent<GrenadeProjectile>();
        projectile.position = start;
        projectile.velocity = velocity;
        projectile.throwTime = throwTime;
        projectile.killer = killer;
        projectile.seed = seed;
        projectile.authority = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
        Active.Add(projectile);
        if (grenadeModel == null) grenadeModel = Resources.Load<GameObject>("Grenade/grenade");
        if (grenadeModel != null)
        {
            projectile.visual = Instantiate(grenadeModel, start, Random.rotationUniform);
            projectile.visual.transform.SetParent(go.transform, true);
            foreach (var collider in projectile.visual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        }
    }

    private void Update()
    {
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        if (now >= throwTime + Fuse) { Explode(); return; }
        Simulate(Time.deltaTime);
    }

    private void Simulate(float dt)
    {
        if (velocity.sqrMagnitude > .000001f)
        {
            float remaining = dt;
            while (remaining > 0f)
            {
                float h = Mathf.Min(1f / 120f, remaining);
                remaining -= h;
                Vector3 next = position + velocity * h + Vector3.down * (.5f * Gravity * h * h);
                Vector3 segment = next - position;
                float distance = segment.magnitude;
                if (distance > .000001f && CastWorld(position, segment / distance, distance,
                    out Vector3 point, out Vector3 normal))
                {
                    position = point + normal * .02f;
                    Vector3 reflected = velocity;
                    float into = Vector3.Dot(reflected, normal);
                    if (into < 0f) reflected -= normal * (into * 1.42f);
                    reflected *= .7f;
                    velocity = reflected.magnitude < 1f ? Vector3.zero : reflected;
                }
                else position = next;
                velocity += Vector3.down * (Gravity * h);
                if (velocity.sqrMagnitude < .000001f) break;
            }
            transform.position = position;
            if (visual != null && velocity.sqrMagnitude > 1f)
                visual.transform.Rotate(new Vector3(7f, 3f, 5f) * (Time.deltaTime * velocity.magnitude * .2f), Space.Self);
        }
        else transform.position = position;
    }

    private static bool CastWorld(Vector3 origin, Vector3 direction, float distance,
        out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero; normal = Vector3.up;
        int count = Physics.RaycastNonAlloc(origin, direction, CastHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = CastHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                point = hit.point; normal = hit.normal;
                found = true;
            }
        }
        return found;
    }

    private void Explode()
    {
        ExplosionFlash.Spawn(position, seed);
        if (authority) DealDamage(position);
        Destroy(gameObject);
    }

    private void DealDamage(Vector3 at)
    {
        int throwerTeam = 0;
        if (killer != null && killer.CustomProperties["team"] is int team) throwerTeam = team;
        Vector3 blastOrigin = at + Vector3.up * .3f;
        // Snapshot to avoid mutation during iteration.
        var victims = new List<PlayerHealth>(PlayerHealth.ActivePlayers);
        foreach (var victim in victims)
        {
            if (victim == null || victim.IsDead) continue;
            Vector3 chest = victim.transform.position + Vector3.up * 1.2f;
            Vector3 toVictim = chest - blastOrigin;
            float distance = toVictim.magnitude;
            if (distance > BlastRadius) continue;
            int victimTeam = BotController.TeamOf(victim);
            bool isThrower = killer != null && victim.photonView != null &&
                !BotController.IsBot(victim) && victim.photonView.OwnerActorNr == killer.ActorNumber;
            // Own grenade always hurts its thrower; other teammates are still spared.
            if (!isThrower && throwerTeam != 0 && victimTeam != 0 && throwerTeam == victimTeam) continue;
            if (distance > .001f)
            {
                var block = BulletHitUtility.CastCover(blastOrigin, toVictim / distance, distance, null, ~0);
                if (block.didHit) continue;
            }
            float fullRadius = BlastRadius * FullDamageFraction;
            float fall = distance <= fullRadius ? 1f
                : 1f - (distance - fullRadius) / (BlastRadius - fullRadius);
            int damage = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(MinDamage, MaxDamage, fall)));
            // Heavy blast impulse: survivors ignore it, kills fling the ragdoll away from the blast.
            Vector3 flat = distance > .001f ? toVictim / distance : Vector3.zero;
            Vector3 force = (flat + Vector3.up * .7f).normalized * 45f;
            victim.ApplyMasterDamage(damage, force, chest, killer, 0, "grenade");
        }
    }
}

/// <summary>Short-lived procedural fireball using the shared muzzle-flash shader plus a point light.</summary>
public sealed class ExplosionFlash : MonoBehaviour
{
    private const float Duration = .5f;
    private static Mesh fireball;
    private static Material material;
    private static AudioClip boomClip;
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private MaterialPropertyBlock properties;
    private MeshRenderer meshRenderer;
    private Light blastLight;
    private float age;

    public static void Spawn(Vector3 point, int seed)
    {
        material ??= Resources.Load<Material>("VFX/MuzzleFlash");
        if (material == null) return;
        fireball ??= BuildFireball();
        var go = new GameObject("Explosion");
        go.transform.SetPositionAndRotation(point + Vector3.up * .3f, Random.rotationUniform);
        go.transform.localScale = Vector3.one * 5.2f;
        var flash = go.AddComponent<ExplosionFlash>();
        flash.meshRenderer = go.AddComponent<MeshRenderer>();
        flash.meshRenderer.sharedMaterial = material;
        flash.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        flash.meshRenderer.receiveShadows = false;
        flash.meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        flash.meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.AddComponent<MeshFilter>().sharedMesh = fireball;
        flash.blastLight = go.AddComponent<Light>();
        flash.blastLight.type = LightType.Point;
        flash.blastLight.color = new Color(1f, .55f, .2f);
        flash.blastLight.range = 28f;
        flash.blastLight.shadows = LightShadows.None;
        flash.blastLight.intensity = 6f;
        flash.properties = new MaterialPropertyBlock();
        flash.Present(seed);
        ShakeLocalCamera(point);
        BlastSphere.Spawn(point, seed);
        ShockwaveRing.Spawn(point, seed);
        BulletImpactEffect.Spawn(point, Vector3.up, seed, 8f);
        if (boomClip == null) boomClip = Resources.Load<AudioClip>("Audio/Explosion/explosion");
        GameAudio.Play(boomClip, point, .55f, 70f, false, 1f);
    }

    private void Present(int seed)
    {
        properties.SetFloat(AgeId, 0f);
        properties.SetFloat(DurationId, Duration);
        properties.SetFloat(IntensityId, 3.5f);
        properties.SetFloat(SeedId, (seed & 65535) / 65535f);
        meshRenderer.SetPropertyBlock(properties);
    }

    /// <summary>Trauma shake for the local player only, scaled by distance to the blast.</summary>
    private static void ShakeLocalCamera(Vector3 point)
    {
        const float innerRadius = 8f;
        const float outerRadius = 36f;
        foreach (var candidate in PlayerHealth.ActivePlayers)
        {
            if (candidate == null || candidate.transform == null || BotController.IsBot(candidate)) continue;
            if (PhotonNetwork.InRoom && (candidate.photonView == null || !candidate.photonView.IsMine)) continue;
            float distance = Vector3.Distance(candidate.transform.position + Vector3.up * 1.5f, point);
            if (distance >= outerRadius) continue;
            float strength = distance <= innerRadius ? 1f : 1f - (distance - innerRadius) / (outerRadius - innerRadius);
            var look = candidate.GetComponent<PlayerCameraLook>();
            if (look != null) look.AddShake(strength);
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Duration + .1f) { Destroy(gameObject); return; }
        properties.SetFloat(AgeId, age);
        meshRenderer.SetPropertyBlock(properties);
        if (blastLight != null) blastLight.intensity = 6f * Mathf.Pow(Mathf.Clamp01(1f - age / Duration), 2);
    }

    private static Mesh BuildFireball()
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var kinds = new List<Vector2>();
        var triangles = new List<int>();
        var palette = new[] {
            new Color(1f, .72f, .12f), new Color(1f, .43f, .035f),
            new Color(1f, .87f, .32f), new Color(.8f, .25f, .015f), new Color(1f, .59f, .06f)
        };
        void Face(Vector3 a, Vector3 b, Vector3 c, Color color, float index)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int i = 0; i < 3; i++) { colors.Add(color); kinds.Add(new Vector2(0, index)); triangles.Add(first + i); }
        }
        Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;
        Color core = new Color(1f, .95f, .7f);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Face(x * sx, y * sy, z * sz, core, 0);
        Vector3[] axes = { x, -x, y, -y, z, -z };
        for (int i = 0; i < axes.Length; i++)
        {
            Vector3 dir = axes[i];
            Vector3 u = Mathf.Abs(dir.y) > .5f ? x : y;
            Vector3 v = Vector3.Cross(dir, u);
            Vector3 p1 = u * .35f, p2 = (u * -.5f + v * .87f) * .35f, p3 = (u * -.5f - v * .87f) * .35f;
            Vector3 tip = dir * 1.6f;
            Color color = palette[i % palette.Length];
            Face(p1, p2, tip, color, i + 1);
            Face(p2, p3, tip, color, i + 1);
            Face(p3, p1, tip, color, i + 1);
        }
        var mesh = new Mesh { name = "Explosion fireball" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(1, kinds); mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(10, 10, 10));
        mesh.UploadMeshData(true);
        return mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared()
    {
        if (fireball != null) Destroy(fireball);
        fireball = null; material = null; boomClip = null;
    }
}

/// <summary>Expanding ground shockwave ring using the shared muzzle-flash shader.</summary>
public sealed class ShockwaveRing : MonoBehaviour
{
    private const float Duration = .55f;
    private const float MaxRadius = 15f;
    private static Mesh ring;
    private static Material material;
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private MaterialPropertyBlock properties;
    private MeshRenderer meshRenderer;
    private float age;

    public static void Spawn(Vector3 point, int seed)
    {
        material ??= Resources.Load<Material>("VFX/MuzzleFlash");
        if (material == null) return;
        ring ??= BuildRing();
        float baseY = point.y;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * .5f, Vector3.down, GroundHits, 4f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = 4f;
        for (int i = 0; i < count; i++)
        {
            var hit = GroundHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (hit.distance < nearest) nearest = hit.distance;
        }
        if (nearest < 4f) baseY = point.y + .5f - nearest + .06f;
        var go = new GameObject("Shockwave");
        go.transform.position = new Vector3(point.x, baseY, point.z);
        go.transform.localScale = Vector3.one * .5f;
        var wave = go.AddComponent<ShockwaveRing>();
        wave.meshRenderer = go.AddComponent<MeshRenderer>();
        wave.meshRenderer.sharedMaterial = material;
        wave.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        wave.meshRenderer.receiveShadows = false;
        wave.meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        wave.meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.AddComponent<MeshFilter>().sharedMesh = ring;
        wave.properties = new MaterialPropertyBlock();
        wave.properties.SetFloat(AgeId, 0f);
        wave.properties.SetFloat(DurationId, Duration);
        wave.properties.SetFloat(IntensityId, 2.5f);
        wave.properties.SetFloat(SeedId, (seed & 65535) / 65535f);
        wave.meshRenderer.SetPropertyBlock(wave.properties);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Duration + .1f) { Destroy(gameObject); return; }
        float t = Mathf.Clamp01(age / Duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.localScale = Vector3.one * Mathf.Lerp(.5f, MaxRadius, eased);
        properties.SetFloat(AgeId, age);
        meshRenderer.SetPropertyBlock(properties);
    }

    private static Mesh BuildRing()
    {
        const int segments = 64;
        var vertices = new List<Vector3>(segments * 4);
        var colors = new List<Color>(segments * 4);
        var kinds = new List<Vector2>(segments * 4);
        var triangles = new List<int>(segments * 6);
        var color = new Color(1f, .6f, .15f);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            int first = vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a) * .85f, 0f, Mathf.Sin(a) * .85f));
            vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            vertices.Add(new Vector3(Mathf.Cos(b) * .85f, 0f, Mathf.Sin(b) * .85f));
            vertices.Add(new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)));
            for (int k = 0; k < 4; k++) { colors.Add(color); kinds.Add(new Vector2(0, i)); }
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first + 1); triangles.Add(first + 3); triangles.Add(first + 2);
        }
        var mesh = new Mesh { name = "Explosion shockwave" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(1, kinds); mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(20, 1f, 20));
        mesh.UploadMeshData(true);
        return mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared()
    {
        if (ring != null) Destroy(ring);
        ring = null; material = null;
    }
}

/// <summary>Expanding spherical blast shell using the shared muzzle-flash shader.</summary>
public sealed class BlastSphere : MonoBehaviour
{
    private const float Duration = .45f;
    private const float MaxRadius = 9f;
    private static Mesh sphere;
    private static Material material;
    private static readonly int AgeId = Shader.PropertyToID("_Age");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private MaterialPropertyBlock properties;
    private MeshRenderer meshRenderer;
    private float age;

    public static void Spawn(Vector3 point, int seed)
    {
        material ??= Resources.Load<Material>("VFX/MuzzleFlash");
        if (material == null) return;
        sphere ??= BuildSphere();
        var go = new GameObject("Blast Sphere");
        go.transform.SetPositionAndRotation(point + Vector3.up * .3f, Random.rotationUniform);
        go.transform.localScale = Vector3.one;
        var blast = go.AddComponent<BlastSphere>();
        blast.meshRenderer = go.AddComponent<MeshRenderer>();
        blast.meshRenderer.sharedMaterial = material;
        blast.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        blast.meshRenderer.receiveShadows = false;
        blast.meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        blast.meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.AddComponent<MeshFilter>().sharedMesh = sphere;
        blast.properties = new MaterialPropertyBlock();
        blast.properties.SetFloat(AgeId, 0f);
        blast.properties.SetFloat(DurationId, Duration);
        blast.properties.SetFloat(IntensityId, 2f);
        blast.properties.SetFloat(SeedId, (seed & 65535) / 65535f);
        blast.meshRenderer.SetPropertyBlock(blast.properties);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Duration + .1f) { Destroy(gameObject); return; }
        float t = Mathf.Clamp01(age / Duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, MaxRadius, eased);
        properties.SetFloat(AgeId, age);
        meshRenderer.SetPropertyBlock(properties);
    }

    private static Mesh BuildSphere()
    {
        const int latSegments = 16;
        const int lonSegments = 24;
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var kinds = new List<Vector2>();
        var triangles = new List<int>();
        var bottom = new Color(.9f, .25f, .05f);
        var middle = new Color(1f, .6f, .15f);
        var top = new Color(1f, .95f, .7f);
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float polar = lat * Mathf.PI / latSegments;
            float y = Mathf.Cos(polar);
            float ring = Mathf.Sin(polar);
            Color color = y < 0f ? Color.Lerp(middle, bottom, -y) : Color.Lerp(middle, top, y);
            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float azimuth = lon * Mathf.PI * 2f / lonSegments;
                vertices.Add(new Vector3(ring * Mathf.Cos(azimuth), y, ring * Mathf.Sin(azimuth)));
                colors.Add(color);
                kinds.Add(new Vector2(0, lat + lon));
            }
        }
        for (int lat = 0; lat < latSegments; lat++)
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int a = lat * (lonSegments + 1) + lon;
                int b = a + lonSegments + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                triangles.Add(b); triangles.Add(b + 1); triangles.Add(a + 1);
            }
        var mesh = new Mesh { name = "Explosion blast sphere" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(1, kinds); mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(12, 12, 12));
        mesh.UploadMeshData(true);
        return mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShared()
    {
        if (sphere != null) Destroy(sphere);
        sphere = null; material = null;
    }
}
