using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

/// <summary>Analytical TPS hitboxes with pose history; does not enable ragdoll collision during animation.</summary>
[DefaultExecutionOrder(600)]
public sealed class PlayerHitboxes : MonoBehaviour
{
    public enum Zone { Torso, Head, Arm, Leg }
    [SerializeField] private float headMultiplier = 3f, torsoMultiplier = 1f, armMultiplier = .65f, legMultiplier = .75f;
    [SerializeField] private bool drawHitboxes;
    private Collider[] shapes;
    private Zone[] zones;
    private Pose[][] history;
    private readonly double[] times = new double[40];
    private int count, next;
    private struct Pose { public Vector3 center, end, size; public Quaternion rotation; public float radius; public Zone zone; }
    public bool Ready { get { Initialize(); return shapes != null && shapes.Length > 0; } }
    private static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;

    private void Initialize()
    {
        if (shapes != null) return;
        var root = GetComponent<PlayerRagdollController>()?.SkeletonRoot;
        if (root == null) return;
        var found = new List<Collider>();
        foreach (var shape in root.GetComponentsInChildren<Collider>(true))
            if (shape is BoxCollider || shape is CapsuleCollider) found.Add(shape);
        shapes = found.ToArray(); history = new Pose[times.Length][];
        zones = Array.ConvertAll(shapes, shape => Classify(shape.name));
        for (int i = 0; i < history.Length; i++) history[i] = new Pose[shapes.Length];
    }
    private void LateUpdate() => Record(Now);
    public void Record(double time)
    {
        if (!Ready) return;
        if (count > 0 && time - times[(next + times.Length - 1) % times.Length] < 1d / 90d) return;
        for (int i = 0; i < shapes.Length; i++) history[next][i] = Capture(shapes[i], zones[i]);
        times[next] = time; next = (next + 1) % times.Length; count = Mathf.Min(count + 1, times.Length);
    }
    private static Pose Capture(Collider shape, Zone zone)
    {
        var t = shape.transform; Vector3 scale = t.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        var pose = new Pose { rotation = t.rotation, zone = zone };
        if (shape is BoxCollider box) { pose.center = t.TransformPoint(box.center); pose.size = Vector3.Scale(box.size, scale) * .5f; }
        else if (shape is CapsuleCollider capsule)
        {
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            float axial = scale[capsule.direction]; float radial = Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
            pose.radius = capsule.radius * radial;
            Vector3 offset = t.TransformDirection(axis) * Mathf.Max(0, capsule.height * axial * .5f - pose.radius);
            var center = t.TransformPoint(capsule.center); pose.center = center - offset; pose.end = center + offset;
        }
        return pose;
    }
    private static Zone Classify(string bone)
    {
        if (bone == "Head") return Zone.Head;
        if (bone.Contains("arm") || bone.Contains("hand")) return Zone.Arm;
        if (bone.Contains("Leg") || bone.Contains("Foot")) return Zone.Leg;
        return Zone.Torso;
    }
    public float Multiplier(Zone zone) => zone == Zone.Head ? headMultiplier : zone == Zone.Arm ? armMultiplier : zone == Zone.Leg ? legMultiplier : torsoMultiplier;

    public bool Raycast(Vector3 origin, Vector3 direction, float range, double time, out float distance, out Vector3 normal, out Zone zone)
    {
        distance = range; normal = -direction; zone = Zone.Torso;
        if (!Ready) return false;
        int lower = -1, upper = -1; float blend = 0;
        if (count > 0)
        {
            lower = upper = (next - count + times.Length) % times.Length;
            for (int j = 1; j < count; j++)
            {
                int index = (next - count + j + times.Length) % times.Length;
                if (times[index] >= time) { upper = index; blend = Mathf.Clamp01((float)((time - times[lower]) / Math.Max(.000001, times[upper] - times[lower]))); break; }
                lower = upper = index;
            }
        }
        bool found = false;
        for (int i = 0; i < shapes.Length; i++)
        {
            Pose p = lower < 0 ? Capture(shapes[i], zones[i]) : history[lower][i];
            if (upper != lower)
            {
                Pose q = history[upper][i]; p.center = Vector3.Lerp(p.center, q.center, blend); p.end = Vector3.Lerp(p.end, q.end, blend);
                p.rotation = Quaternion.Slerp(p.rotation, q.rotation, blend); p.size = Vector3.Lerp(p.size, q.size, blend); p.radius = Mathf.Lerp(p.radius, q.radius, blend);
            }
            float hit; Vector3 n;
            if (p.radius > 0)
            {
                if (!BulletHitUtility.RayCapsule(origin, direction, p.center, p.end, p.radius, out hit)) continue;
                Vector3 point = origin + direction * hit;
                Vector3 axis = p.end - p.center;
                n = (point - (p.center + axis * Mathf.Clamp01(Vector3.Dot(point - p.center, axis) / Mathf.Max(.000001f, axis.sqrMagnitude)))).normalized;
            }
            else if (!RayBox(origin, direction, p.center, p.rotation, p.size, out hit, out n)) continue;
            if (hit > distance) continue;
            distance = hit; normal = n; zone = p.zone; found = true;
        }
        return found;
    }
    public static bool RayBox(Vector3 origin, Vector3 direction, Vector3 center, Quaternion rotation, Vector3 halfSize, out float distance, out Vector3 normal)
    {
        var inverse = Quaternion.Inverse(rotation); Vector3 o = inverse * (origin - center), d = inverse * direction;
        float near = 0, far = float.PositiveInfinity; Vector3 n = -direction;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(d[axis]) < .000001f) { if (Mathf.Abs(o[axis]) > halfSize[axis]) { distance = 0; normal = n; return false; } continue; }
            float a = (-halfSize[axis] - o[axis]) / d[axis], b = (halfSize[axis] - o[axis]) / d[axis];
            if (a > b) { float swap = a; a = b; b = swap; }
            if (a > near) { near = a; var localNormal = Vector3.zero; localNormal[axis] = -Mathf.Sign(d[axis]); n = rotation * localNormal; }
            far = Mathf.Min(far, b);
            if (near > far) { distance = 0; normal = n; return false; }
        }
        distance = near; normal = n; return far >= 0;
    }
    private void OnDrawGizmosSelected()
    {
        if (!drawHitboxes || !Ready) return;
        foreach (var shape in shapes)
        {
            Pose p = Capture(shape, Classify(shape.name)); Gizmos.color = p.zone == Zone.Head ? Color.red : p.zone == Zone.Torso ? Color.yellow : Color.cyan;
            if (p.radius > 0) { Gizmos.DrawWireSphere(p.center,p.radius); Gizmos.DrawWireSphere(p.end,p.radius); Gizmos.DrawLine(p.center,p.end); }
            else { Gizmos.matrix = Matrix4x4.TRS(p.center,p.rotation,Vector3.one); Gizmos.DrawWireCube(Vector3.zero,p.size*2); Gizmos.matrix=Matrix4x4.identity; }
        }
    }
}
