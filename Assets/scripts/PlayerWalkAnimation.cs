using Photon.Pun;
using UnityEngine;

/// <summary>Distance-driven, in-place lower-body gait. Runs after FPS pose copying and before hand IK.</summary>
[DefaultExecutionOrder(275)]
[DisallowMultipleComponent]
public sealed class PlayerWalkAnimation : MonoBehaviourPun
{
    [SerializeField, Min(.2f)] private float cycleDistance = 1.65f;
    [SerializeField, Range(0f, .2f)] private float footLift = .095f;
    [SerializeField, Min(1f)] private float blendSpeed = 10f;
    [Header("Run")]
    [SerializeField, Min(.5f)] private float runCycleDistance = 2.15f;
    [SerializeField, Range(.1f, .3f)] private float runFootLift = .19f;
    private float runBlend;
    private float crouchBlend, slideBlend;
    private PlayerAnimancerController networkMovement;
    private PlayerRagdollController ragdoll;
    private PlayerDeathController death;
    private PlayerController movement;
    private CharacterController capsule;
    private WeaponIdleSynchronizer animationSource;
    private Leg left, right;
    private Transform hips, spine;
    private Vector3 previousPosition, velocity;
    private float phase, weight;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    public float Weight => weight;

    private sealed class Leg
    {
        public Transform thigh, shin, foot;
        public Vector3 standingFoot;
    }

    private void Start()
    {
        ragdoll = GetComponent<PlayerRagdollController>();
        death = GetComponent<PlayerDeathController>();
        movement = GetComponent<PlayerController>();
        networkMovement = GetComponent<PlayerAnimancerController>();
        capsule = GetComponent<CharacterController>();
        animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        // Use the original TPS skeleton, never the camera's duplicated FPS bones.
        var root = ragdoll != null ? ragdoll.SkeletonRoot : null;
        if (root == null) { enabled = false; return; }
        left = FindLeg(root, "L"); right = FindLeg(root, "R");
        if (left == null || right == null) { enabled = false; return; }
        hips = left.thigh.parent;
        spine = hips.Find("Spine");
        previousPosition = transform.position;
    }

    private Leg FindLeg(Transform root, string side)
    {
        Transform thigh = null, shin = null, foot = null;
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "UpperLeg_" + side) thigh = bone;
            if (bone.name == "LowerLeg_" + side) shin = bone;
            if (bone.name == "Foot_" + side) foot = bone;
        }
        if (!thigh || !shin || !foot) return null;
        return new Leg { thigh = thigh, shin = shin, foot = foot,
            standingFoot = transform.InverseTransformPoint(foot.position) };
    }

    private bool Grounded(bool local)
    {
        if (local && capsule != null && capsule.enabled) return capsule.isGrounded;
        int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .2f,
            Vector3.down, groundHits, .5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!groundHits[i].transform.IsChildOf(transform) && groundHits[i].normal.y > .5f) return true;
        return false;
    }

    private void LateUpdate()
    {
        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        if (left == null || right == null) return;
        if ((ragdoll != null && ragdoll.IsRagdoll) || (death != null && death.IsDead))
        { weight = crouchBlend = slideBlend = 0; velocity = Vector3.zero; return; }
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        // Spawn/teleport corrections must not become a huge stride.
        if (delta.sqrMagnitude > 4f) { velocity = Vector3.zero; weight = 0; return; }
        bool local = !PhotonNetwork.InRoom || photonView.IsMine;
        Vector3 targetVelocity = local && capsule != null ? capsule.velocity : delta / dt;
        targetVelocity.y = 0;
        velocity = Vector3.Lerp(velocity, Vector3.ClampMagnitude(targetVelocity, 7f), 1f - Mathf.Exp(-12f * dt));
        float speed = velocity.magnitude;
        bool crouching = local ? movement != null && movement.IsCrouching : networkMovement != null && networkMovement.IsCrouching;
        bool sliding = local ? movement != null && movement.IsSliding : networkMovement != null && networkMovement.IsSliding;
        float crouchTarget = local && movement != null ? movement.CrouchAmount : crouching ? 1f : 0f;
        crouchBlend = Mathf.MoveTowards(crouchBlend, crouchTarget, dt * 7f);
        slideBlend = Mathf.MoveTowards(slideBlend, sliding ? 1f : 0f, dt * 9f);
        float targetWeight = Grounded(local) ? Mathf.InverseLerp(.05f, .65f, speed) * (1f - slideBlend) : 0f;
        weight = Mathf.MoveTowards(weight, targetWeight, blendSpeed * dt);
        if ((weight <= 0 && crouchBlend <= 0 && slideBlend <= 0) || animationSource == null || animationSource.LastEvaluatedFrame != Time.frameCount) return;
        float runTarget = Mathf.InverseLerp(3.3f, 4.8f, speed) * (1f - crouchBlend);
        runBlend = Mathf.MoveTowards(runBlend, runTarget, dt * 6f);
        phase = Mathf.Repeat(phase + speed * dt / Mathf.Lerp(cycleDistance, runCycleDistance, runBlend), 1f);
        Vector3 direction = speed > .01f ? velocity / speed : transform.forward;
        ApplyPose(speed, direction);
    }

    private void ApplyPose(float speed, Vector3 direction)
    {
        // Shorten the stride at low speed instead of shuffling a full-size step.
        float stride = Mathf.Lerp(.12f, .34f, Mathf.InverseLerp(.1f, 3.2f, speed));
        float lift = footLift * Mathf.Lerp(.45f, 1f, Mathf.Clamp01(speed / 3.2f));
        stride = Mathf.Lerp(stride, .4f, runBlend);
        lift = Mathf.Lerp(lift, runFootLift, runBlend);
        stride *= Mathf.Lerp(1f, .55f, crouchBlend) * weight;
        lift *= Mathf.Lerp(1f, .4f, crouchBlend) * weight;
        // Bend the knees enough to reach the planted feet. Compensate at the spine
        // so upper-body weapon actions keep their authored world-space position.
        Vector3 spinePosition = spine != null ? spine.position : Vector3.zero;
        hips.position += transform.up * ((Mathf.Lerp(-.075f, -.1f, runBlend) + Mathf.Lerp(.012f, .025f, runBlend) * Mathf.Cos(phase * Mathf.PI * 4f)) * weight);
        if (spine != null) spine.position = spinePosition;
        // Lower the entire torso for crouching (including the shoulders). The
        // camera/weapon lowers with the capsule, and hand IK preserves the grip.
        hips.position -= transform.up * (.49f * crouchBlend + .14f * slideBlend);
        hips.position -= transform.forward * (.06f * crouchBlend + .07f * slideBlend);
        if (spine != null && animationSource.IsIdlePlaying)
            spine.rotation = Quaternion.AngleAxis(7f * runBlend * weight + 12f * crouchBlend - 30f * slideBlend, transform.right) * spine.rotation;
        ApplyLeg(left, phase, direction, stride, lift);
        ApplyLeg(right, Mathf.Repeat(phase + .5f, 1f), direction, stride, lift);
    }

    private void ApplyLeg(Leg leg, float cycle, Vector3 direction, float stride, float lift)
    {
        Quaternion thighPose = leg.thigh.localRotation, shinPose = leg.shin.localRotation;
        Quaternion footPose = leg.foot.rotation;
        // Stance: foot travels backwards. Swing: smooth return with toe clearance.
        float stance = Mathf.Lerp(.5f, .36f, runBlend);
        bool swing = cycle >= stance;
        float t = swing ? (cycle - stance) / (1f - stance) : cycle / stance;
        float travel = swing ? Mathf.Lerp(-stride, stride, Mathf.SmoothStep(0, 1, t)) : Mathf.Lerp(stride, -stride, t);
        float height = swing ? Mathf.Sin(t * Mathf.PI) * lift : 0f;
        Vector3 target = transform.TransformPoint(leg.standingFoot) + direction * travel + transform.up * height;
        Vector3 slideFoot = transform.TransformPoint(leg.standingFoot) + direction * (leg == left ? .47f : .17f);
        target = Vector3.Lerp(target, slideFoot, slideBlend);
        SolveLeg(leg.thigh, leg.shin, leg.foot, target, transform.forward);
        float legWeight = Mathf.Max(weight, crouchBlend, slideBlend);
        leg.thigh.localRotation = Quaternion.Slerp(thighPose, leg.thigh.localRotation, legWeight);
        leg.shin.localRotation = Quaternion.Slerp(shinPose, leg.shin.localRotation, legWeight);
        leg.foot.rotation = footPose; // Keep the sole level; do not inherit knee rotation.
    }

    public static void SolveLeg(Transform thigh, Transform shin, Transform foot, Vector3 target, Vector3 kneeForward)
    {
        Vector3 origin = thigh.position;
        float upper = Vector3.Distance(origin, shin.position), lower = Vector3.Distance(shin.position, foot.position);
        if (upper < .0001f || lower < .0001f) return;
        Vector3 axis = target - origin;
        float distance = Mathf.Clamp(axis.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
        axis = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.down;
        Vector3 bend = Vector3.ProjectOnPlane(kneeForward, axis).normalized;
        if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
        Vector3 knee = origin + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        thigh.rotation = Quaternion.FromToRotation(shin.position - origin, knee - origin) * thigh.rotation;
        Vector3 reachable = origin + axis * distance;
        shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, reachable - shin.position) * shin.rotation;
    }
}
