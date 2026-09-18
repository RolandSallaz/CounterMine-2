using System;
using Photon.Pun;
using UnityEngine;

/// <summary>Switches the single character skeleton between animation and physics.</summary>
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PhotonView))]
public sealed class PlayerRagdollController : MonoBehaviourPun
{
    [SerializeField] private Transform skeletonRoot;
    [SerializeField] private Rigidbody hips;
    [SerializeField] private Transform head;
    [SerializeField] private Rigidbody[] bodies = Array.Empty<Rigidbody>();
    [SerializeField] private Collider[] ragdollColliders = Array.Empty<Collider>();
    [SerializeField] private Behaviour[] suspendWhileRagdoll = Array.Empty<Behaviour>();
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerController movement;
    [SerializeField] private PlayerDeathController death;
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject weaponPresentation;
    [Header("Directional fall")]
    [SerializeField, Min(0f)] private float fallSpeed = .85f;
    [SerializeField, Min(0f)] private float impactMultiplier = 2f;
    public Transform SkeletonRoot => skeletonRoot;
    public bool IsRagdoll { get; private set; }
    private Transform[] poseBones;
    private Vector3[] posePositions;
    private Quaternion[] poseRotations;
    private bool[] suspendedStates;
    private bool capsuleWasEnabled;
    private Transform heldWeapon, weaponParent;
    private Vector3 weaponLocalPosition, weaponLocalScale;
    private Quaternion weaponLocalRotation;
    private Transform[] weaponBones;
    private Vector3[] weaponBonePositions;
    private Quaternion[] weaponBoneRotations;
    private Vector3 cameraPosition, cameraHeadOffset, entryPosition;
    private Quaternion cameraRotation, cameraHeadRotation;
    private bool initialized;
    private bool IsLocal => !BotController.IsBot(this) && (!PhotonNetwork.InRoom || photonView.IsMine);

    private void Awake() => Initialize();

    private bool Initialize()
    {
        if (initialized) return true;
        if (skeletonRoot == null || hips == null || head == null || bodies.Length == 0) return false;
        poseBones = skeletonRoot.GetComponentsInChildren<Transform>(true);
        posePositions = new Vector3[poseBones.Length];
        poseRotations = new Quaternion[poseBones.Length];
        suspendedStates = new bool[suspendWhileRagdoll.Length];
        foreach (var body in bodies)
        {
            if (body == null) continue;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            body.maxAngularVelocity = 20f;
            body.maxDepenetrationVelocity = 3f;
        }
        SetPhysics(false);
        // The simple low-poly body has intersecting joints in its bind pose.
        // Disable internal contacts while retaining collisions with the world.
        for (int i = 0; i < ragdollColliders.Length; i++)
            for (int j = i + 1; j < ragdollColliders.Length; j++)
                if (ragdollColliders[i] && ragdollColliders[j])
                    Physics.IgnoreCollision(ragdollColliders[i], ragdollColliders[j], true);
        initialized = true;
        return true;
    }

    private void LateUpdate()
    {
        if (!IsRagdoll || !IsLocal || playerCamera == null || head == null) return;
        playerCamera.transform.SetPositionAndRotation(head.TransformPoint(cameraHeadOffset), head.rotation * cameraHeadRotation);
    }

    public void EnterRagdoll(Vector3 impulse, Vector3 impactPoint)
    {
        if (IsRagdoll || !Initialize()) return;
        GetComponent<NetworkWeaponPresentation>()?.PrepareForRagdoll();
        GetComponent<PlayerModelPresentation>()?.BeginRagdoll();
        entryPosition = transform.position;
        Vector3 velocity = characterController != null && characterController.enabled ? characterController.velocity : Vector3.zero;
        for (int i = 0; i < poseBones.Length; i++)
        { posePositions[i] = poseBones[i].localPosition; poseRotations[i] = poseBones[i].localRotation; }
        if (playerCamera != null)
        {
            cameraPosition = playerCamera.transform.localPosition;
            cameraRotation = playerCamera.transform.localRotation;
            cameraHeadOffset = head.InverseTransformPoint(playerCamera.transform.position);
            cameraHeadRotation = Quaternion.Inverse(head.rotation) * playerCamera.transform.rotation;
        }
        heldWeapon = weaponAnimation != null ? weaponAnimation.WeaponRoot : null;
        Vector3 weaponWorldPosition = heldWeapon ? heldWeapon.position : Vector3.zero;
        Quaternion weaponWorldRotation = heldWeapon ? heldWeapon.rotation : Quaternion.identity;
        if (heldWeapon != null)
        {
            weaponParent = heldWeapon.parent; weaponLocalPosition = heldWeapon.localPosition;
            weaponLocalRotation = heldWeapon.localRotation; weaponLocalScale = heldWeapon.localScale;
            weaponBones = heldWeapon.GetComponentsInChildren<Transform>(true);
            weaponBonePositions = new Vector3[weaponBones.Length]; weaponBoneRotations = new Quaternion[weaponBones.Length];
            for (int i = 0; i < weaponBones.Length; i++)
            { weaponBonePositions[i] = weaponBones[i].localPosition; weaponBoneRotations[i] = weaponBones[i].localRotation; }
        }
        IsRagdoll = true;
        capsuleWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        for (int i = 0; i < suspendWhileRagdoll.Length; i++)
        {
            var behaviour = suspendWhileRagdoll[i];
            if (behaviour == null || behaviour == this) continue;
            suspendedStates[i] = behaviour.enabled;
            behaviour.enabled = false;
        }
        RestorePose(); // Animator.OnDisable must not change the entry pose.
        if (heldWeapon != null)
        {
            for (int i = 0; i < weaponBones.Length; i++)
            { weaponBones[i].localPosition = weaponBonePositions[i]; weaponBones[i].localRotation = weaponBoneRotations[i]; }
            var hand = Array.Find(poseBones, bone => bone.name == "hand_R");
            if (hand != null) heldWeapon.SetParent(hand, true);
            heldWeapon.SetPositionAndRotation(weaponWorldPosition, weaponWorldRotation);
        }
        SetPhysics(true);
        Physics.SyncTransforms();
        foreach (var body in bodies) if (body != null) body.linearVelocity = velocity;
        if (IsFinite(impulse) && IsFinite(impactPoint))
        {
            Rigidbody closest = hips;
            foreach (var body in bodies)
                if (body != null && (body.worldCenterOfMass - impactPoint).sqrMagnitude < (closest.worldCenterOfMass - impactPoint).sqrMagnitude)
                    closest = body;
            Vector3 awayFromShot = Vector3.ProjectOnPlane(impulse, Vector3.up).normalized;
            // Blast-strength launch for every body: bullet impulses stay under the
            // threshold and behave exactly as before, explosions throw the whole corpse.
            float blastPush = Mathf.Max(0f, impulse.magnitude - 8f) * .25f;
            Vector3 launch = awayFromShot * (fallSpeed + blastPush) + Vector3.up * blastPush * .6f;
            if (awayFromShot.sqrMagnitude > .01f || blastPush > .01f)
                foreach (var body in bodies)
                    if (body != null) body.AddForce(launch, ForceMode.VelocityChange);
            // Clamp the lever arm: capsule hits may be outside the actual limb collider.
            Vector3 contact = closest.worldCenterOfMass + Vector3.ClampMagnitude(impactPoint - closest.worldCenterOfMass, .3f);
            closest.AddForceAtPosition(Vector3.ClampMagnitude(impulse * impactMultiplier, 60f), contact, ForceMode.Impulse);
        }
    }

    public void ExitRagdoll()
    {
        if (!IsRagdoll || (death != null && death.IsDead)) return;
        SetPhysics(false);
        if (heldWeapon != null)
        {
            heldWeapon.SetParent(weaponParent, false);
            heldWeapon.localPosition = weaponLocalPosition; heldWeapon.localRotation = weaponLocalRotation;
            heldWeapon.localScale = weaponLocalScale;
        }
        // Pose restoration is deterministic on every client and avoids standing up inside walls.
        transform.position = entryPosition;
        RestorePose();
        if (playerCamera != null)
        { playerCamera.transform.localPosition = cameraPosition; playerCamera.transform.localRotation = cameraRotation; }
        IsRagdoll = false;
        for (int i = suspendWhileRagdoll.Length - 1; i >= 0; i--)
            if (suspendWhileRagdoll[i] != null && suspendWhileRagdoll[i] != this)
                suspendWhileRagdoll[i].enabled = suspendedStates[i];
        GetComponent<PlayerModelPresentation>()?.EndRagdoll();
        movement?.ResetMotion();
        if (characterController != null) characterController.enabled = capsuleWasEnabled;
        Physics.SyncTransforms();
        if (weaponAnimation != null && weaponAnimation.isActiveAndEnabled) weaponAnimation.RestartIdle();
    }

    private void RestorePose()
    {
        for (int i = 0; i < poseBones.Length; i++)
        { poseBones[i].localPosition = posePositions[i]; poseBones[i].localRotation = poseRotations[i]; }
    }

    private void SetPhysics(bool enabled)
    {
        foreach (var body in bodies)
        {
            if (body == null) continue;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.interpolation = enabled ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            body.isKinematic = !enabled;
            body.detectCollisions = enabled;
            // Speculative CCD covers angular motion of limbs, unlike linear sweep CCD.
            if (enabled) { body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; body.WakeUp(); }
        }
        foreach (var collider in ragdollColliders) if (collider != null) collider.enabled = enabled;
        if (enabled)
            for (int i = 0; i < ragdollColliders.Length; i++)
                for (int j = i + 1; j < ragdollColliders.Length; j++)
                    if (ragdollColliders[i] && ragdollColliders[j])
                        Physics.IgnoreCollision(ragdollColliders[i], ragdollColliders[j], true);
    }

    private static bool IsFinite(Vector3 v) =>
        !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

}
