using System.Collections.Generic;
using UnityEngine;

/// <summary>Pooled ballistic bullet mesh with a short, bounded tracer. Never applies damage.</summary>
public sealed class BulletTrail : MonoBehaviour
{
    private static readonly Stack<BulletTrail> Pool = new Stack<BulletTrail>();
    private LineRenderer tracer;
    private MeshRenderer core;
    private Vector3 origin, initialVelocity;
    private float age, lifetime, gravity, tracerLength, fadeAge;
    private bool stopped;
    private int spawnFrame;
    public long ShotKey { get; private set; }

    public static BulletTrail Spawn(Vector3 start, Vector3 velocity, float gravity, float range, Material material,
        float length, float width, float size, long shotKey, float delay = 0f)
    {
        BulletTrail bullet = null;
        while (Pool.Count > 0 && bullet == null) bullet = Pool.Pop();
        if (bullet == null)
        {
            var obj = new GameObject("Bullet");
            bullet = obj.AddComponent<BulletTrail>();
            bullet.tracer = obj.AddComponent<LineRenderer>();
            bullet.tracer.useWorldSpace = true;
            bullet.tracer.positionCount = 3;
            bullet.tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bullet.tracer.receiveShadows = false;
            bullet.tracer.numCapVertices = 2;
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "Bullet Core";
            mesh.transform.SetParent(obj.transform, false);
            var collider = mesh.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            bullet.core = mesh.GetComponent<MeshRenderer>();
            bullet.core.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bullet.core.receiveShadows = false;
        }
        bullet.origin = start; bullet.initialVelocity = velocity; bullet.ShotKey = shotKey;
        bullet.spawnFrame = Time.frameCount;
        bullet.gravity = gravity;
        bullet.tracerLength = length;
        bullet.lifetime = Mathf.Min(10f, range / Mathf.Max(1f, velocity.magnitude));
        bullet.age = Mathf.Clamp(delay, 0f, bullet.lifetime);
        bullet.fadeAge = 0f; bullet.stopped = false;
        bullet.tracer.sharedMaterial = material;
        bullet.core.sharedMaterial = material;
        bullet.core.transform.localScale = new Vector3(size, size, size * 3f);
        bullet.tracer.startWidth = 0f;
        bullet.tracer.endWidth = width;
        bullet.tracer.startColor = new Color(1f, .3f, .05f, 0f);
        bullet.tracer.endColor = new Color(1f, .8f, .35f, 1f);
        bullet.core.enabled = true;
        bullet.RenderFlight();
        bullet.gameObject.SetActive(true);
        return bullet;
    }
    private Vector3 Position(float t) => BulletHitUtility.FlightPosition(origin, initialVelocity, gravity, t);
    private void RenderFlight()
    {
        var velocity = initialVelocity + Vector3.down * (gravity * age);
        transform.SetPositionAndRotation(Position(age), velocity.sqrMagnitude > .00001f ? Quaternion.LookRotation(velocity) : Quaternion.identity);
        float tailTime = Mathf.Min(age, tracerLength / Mathf.Max(1f, velocity.magnitude));
        for (int i = 0; i < 3; i++) tracer.SetPosition(i, Position(age - tailTime * (1f - i * .5f)));
    }
    public void ConfirmLaunch(Vector3 start, Vector3 velocity, float elapsed, long key)
    {
        if (ShotKey != key || !gameObject.activeSelf || stopped) return;
        origin = start; initialVelocity = velocity; age = Mathf.Clamp(elapsed, 0f, lifetime);
        RenderFlight();
    }
    public void Impact(Vector3 point, long key)
    {
        if (ShotKey != key || !gameObject.activeSelf || stopped) return;
        var velocity = initialVelocity + Vector3.down * (gravity * age);
        transform.position = point;
        // A late confirmation cannot create a long line from an overshot visual position.
        for (int i = 0; i < 3; i++) tracer.SetPosition(i, point - velocity.normalized * tracerLength * (1f - i * .5f));
        stopped = true; fadeAge = 0f; core.enabled = false;
    }
    private void Update()
    {
        // The weapon spawns us before Update. Do not skip the first visible flight frame.
        if (spawnFrame == Time.frameCount) return;
        Advance(Time.deltaTime);
    }
    private void Advance(float deltaTime)
    {
        if (!stopped)
        {
            age = Mathf.Min(lifetime, age + deltaTime);
            RenderFlight();
            if (age >= lifetime) Impact(transform.position, ShotKey);
            return;
        }
        fadeAge += deltaTime;
        tracer.endColor = new Color(1f, .8f, .35f, Mathf.Clamp01(1f - fadeAge / .04f));
        if (fadeAge < .04f) return;
        gameObject.SetActive(false);
        if (Pool.Count < 128) Pool.Push(this); else Destroy(gameObject);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPool() => Pool.Clear();
}
