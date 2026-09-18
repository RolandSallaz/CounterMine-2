using UnityEngine;

/// <summary>Swept flight queries against physical cover and historical player capsules.</summary>
public static class BulletHitUtility
{
    private static readonly RaycastHit[] CoverHits = new RaycastHit[64];
    public static Hit CastCover(Vector3 origin, Vector3 direction, float range, Transform shooter, LayerMask mask, bool includeCharacters = false, int sourceTeam = 0)
    {
        var shooterHealth = shooter != null ? shooter.GetComponentInParent<PlayerHealth>() : null;
        if (shooterHealth != null) sourceTeam = BotController.TeamOf(shooterHealth);
        direction.Normalize();
        var result = new Hit { point = origin + direction * range, normal = -direction };
        int count = Physics.RaycastNonAlloc(origin, direction, CoverHits, range, mask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = CoverHits;
        // A full buffer may omit the nearest wall. Preserve correctness in dense scenes.
        if (count == CoverHits.Length)
        {
            hits = Physics.RaycastAll(origin, direction, range, mask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        float nearest = range;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            var zone = hit.collider.GetComponent<TeamSafeZone>();
            if (zone != null && (!zone.isActiveAndEnabled || zone.Team == sourceTeam)) continue;
            if (hit.distance >= nearest || (shooter != null && hit.transform.IsChildOf(shooter)) || (!includeCharacters && hit.collider.GetComponentInParent<PlayerHealth>() != null)) continue;
            nearest = hit.distance;
            result = new Hit { point = hit.point, normal = hit.normal, didHit = true };
        }
        return result;
    }
    public static Vector3 FlightPosition(Vector3 origin, Vector3 velocity, float gravity, float time) =>
        origin + velocity * time + Vector3.down * (.5f * gravity * time * time);
    public struct Hit
    {
        public Vector3 point;
        public Vector3 normal;
        public PlayerHealth player;
        public float damageMultiplier;
        public bool didHit;
    }

    public static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
        float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    public static Hit Cast(Vector3 origin, Vector3 direction, float range, Transform shooter, double time, LayerMask mask)
    {
        direction.Normalize();
        var result = CastCover(origin, direction, range, shooter, mask);
        float nearest = result.didHit ? Vector3.Distance(origin, result.point) : range;
        var source = shooter != null ? shooter.GetComponentInParent<PlayerHealth>() : null;
        int sourceTeam = source != null ? BotController.TeamOf(source) : 0;
        foreach (var player in PlayerHealth.ActivePlayers)
        {
            if (player == null || player.transform == shooter || player.IsDead || !player.isActiveAndEnabled) continue;
            if ((mask.value & (1 << player.gameObject.layer)) == 0) continue;
            if (TeamSafeZone.Protects(player, sourceTeam)) continue;
            var boxes = player.Hitboxes;
            if (boxes != null && boxes.Ready)
            {
                if (boxes.Raycast(origin, direction, nearest, time, out float boxDistance, out var boxNormal, out var zone))
                {
                    nearest = boxDistance;
                    result = new Hit { point = origin + direction * boxDistance, normal = boxNormal, player = player,
                        damageMultiplier = boxes.Multiplier(zone), didHit = true };
                }
                continue;
            }
            player.GetCapsule(time, out Vector3 bottom, out Vector3 top, out float radius);
            if (!RayCapsule(origin, direction, bottom, top, radius, out float distance) || distance >= nearest) continue;
            nearest = distance;
            Vector3 point = origin + direction * distance;
            Vector3 axis = Vector3.Lerp(bottom, top, Mathf.Clamp01(Vector3.Dot(point - bottom, top - bottom) / Mathf.Max((top - bottom).sqrMagnitude, .000001f)));
            result = new Hit { point = point, normal = (point - axis).normalized, player = player, damageMultiplier = 1f, didHit = true };
        }
        return result;
    }

    public static bool RayCapsule(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, float radius, out float distance)
    {
        Vector3 axis = b - a;
        float length = axis.magnitude;
        if (length < .00001f) return RaySphere(origin, direction, a, radius, out distance);
        Vector3 up = axis / length;
        Vector3 offset = origin - a;
        float originAxis = Vector3.Dot(offset, up);
        float directionAxis = Vector3.Dot(direction, up);
        Vector3 radialOrigin = offset - up * originAxis;
        Vector3 radialDirection = direction - up * directionAxis;
        Vector3 closest = a + up * Mathf.Clamp(originAxis, 0f, length);
        if ((origin - closest).sqrMagnitude <= radius * radius) { distance = 0f; return true; }
        distance = float.PositiveInfinity;
        float aa = radialDirection.sqrMagnitude;
        float bb = Vector3.Dot(radialOrigin, radialDirection);
        float cc = radialOrigin.sqrMagnitude - radius * radius;
        float discriminant = bb * bb - aa * cc;
        if (aa > .000001f && discriminant >= 0f)
        {
            float t = (-bb - Mathf.Sqrt(discriminant)) / aa;
            float along = originAxis + t * directionAxis;
            if (t >= 0f && along >= 0f && along <= length) distance = t;
        }
        if (RaySphere(origin, direction, a, radius, out float cap)) distance = Mathf.Min(distance, cap);
        if (RaySphere(origin, direction, b, radius, out cap)) distance = Mathf.Min(distance, cap);
        return !float.IsInfinity(distance);
    }

    private static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float distance)
    {
        Vector3 offset = origin - center;
        float c = offset.sqrMagnitude - radius * radius;
        if (c <= 0f) { distance = 0f; return true; }
        float b = Vector3.Dot(offset, direction);
        float d = b * b - c;
        distance = d >= 0f ? -b - Mathf.Sqrt(d) : -1f;
        return distance >= 0f;
    }
}
