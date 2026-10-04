using RootMotion.FinalIK;
using UnityEngine;

/// <summary>Solves the two arms after animation, ADS, recoil and sway have positioned the gun.</summary>
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class WeaponHandIK : MonoBehaviour
{
    [SerializeField] private WeaponIdleSynchronizer animationSource;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftGrip;
    [SerializeField] private Transform rightGrip;
    [SerializeField, Range(0f, 1f)] private float leftHandWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float rightHandWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float thirdPersonElbowWeight = .9f;
    private Transform thirdPersonFrame;
    [SerializeField, Range(0f, 2f)] private float breathingAmount = 1f;
    private Transform breathingChest;
    private bool searchedChest, hasBreathingPose, hasPreviousPosition;
    private Vector3 chestPosition, posedChestPosition, previousPosition;
    private Quaternion chestRotation, posedChestRotation;
    private float breathingWeight;
    private IKSolverLimb leftSolver;
    private IKSolverLimb rightSolver;
    private GrenadeThrowIK grenadeThrow;
    private Transform adjustedShoulder;
    private Vector3 shoulderRestPosition, shoulderPosedPosition;
    private Transform adjustedRightShoulder;
    private Vector3 rightShoulderRestPosition, rightShoulderPosedPosition;
    private Transform fittedWeapon;
    private Vector3 weaponRestPosition, weaponPosedPosition;
    private void RestoreWeaponReach()
    {
        if (fittedWeapon != null && fittedWeapon.localPosition.Equals(weaponPosedPosition))
            fittedWeapon.localPosition = weaponRestPosition;
        fittedWeapon = null;
    }

    private bool IsWinchester => leftGrip != null && leftGrip.parent != null &&
        leftGrip.parent.name == "winchester1897_Weapon";

    private void FitWinchesterReach()
    {
        // This rig has no clavicles. Moving an upper arm detaches the shoulder
        // from the chest. Bring the gun towards the body along the view axis
        // instead, retaining the sight line and both physical grip locations.
        var weapon = leftGrip.parent;
        Vector3 forward = animationSource != null ? animationSource.transform.forward : transform.forward;
        float retreat = GetWeaponRetreat(forward);
        if (retreat <= 0f) return;
        fittedWeapon = weapon;
        weaponRestPosition = weapon.localPosition;
        weapon.position -= forward * Mathf.Min(retreat, .35f);
        weaponPosedPosition = weapon.localPosition;
    }

    public float GetWeaponRetreat(Vector3 forward) => IsWinchester && leftHand != null && rightHand != null && rightGrip != null
        ? Mathf.Max(RequiredRetreat(leftHand, leftGrip, forward), RequiredRetreat(rightHand, rightGrip, forward)) : 0f;

    private static float RequiredRetreat(Transform hand, Transform grip, Vector3 forward)
    {
        if (hand.parent == null || hand.parent.parent == null) return 0f;
        var shoulder = hand.parent.parent;
        float reach = (Vector3.Distance(shoulder.position, hand.parent.position) +
            Vector3.Distance(hand.parent.position, hand.position)) * .96f;
        Vector3 delta = grip.position - shoulder.position;
        if (delta.sqrMagnitude <= reach * reach) return 0f;
        float along = Vector3.Dot(delta, forward);
        return Mathf.Max(0f, along - Mathf.Sqrt(Mathf.Max(0f,
            reach * reach - (delta.sqrMagnitude - along * along))));
    }
    private void RestoreShoulder()
    {
        if (adjustedShoulder != null && adjustedShoulder.localPosition.Equals(shoulderPosedPosition))
            adjustedShoulder.localPosition = shoulderRestPosition;
        adjustedShoulder = null;
        if (adjustedRightShoulder != null && adjustedRightShoulder.localPosition.Equals(rightShoulderPosedPosition))
            adjustedRightShoulder.localPosition = rightShoulderRestPosition;
        adjustedRightShoulder = null;
    }

    private void FitProceduralArm(Transform hand, Transform grip, bool right)
    {
        if (hand.parent == null || hand.parent.parent == null) return;
        var shoulder = hand.parent.parent;
        float length = Vector3.Distance(shoulder.position,hand.parent.position)+Vector3.Distance(hand.parent.position,hand.position);
        float bend = right ? .98f : thirdPersonFrame != null ? .86f : .94f;
        Vector3 delta = grip.position-shoulder.position;
        float advance = Mathf.Clamp(delta.magnitude-length*bend,0f,right?.12f:.20f);
        if (advance <= 0f) return;
        Vector3 rest=shoulder.localPosition;
        shoulder.position+=delta.normalized*advance;
        if(right){adjustedRightShoulder=shoulder;rightShoulderRestPosition=rest;rightShoulderPosedPosition=shoulder.localPosition;}
        else {adjustedShoulder=shoulder;shoulderRestPosition=rest;shoulderPosedPosition=shoulder.localPosition;}
    }

    private void FitLongGunLeftArm()
    {
        if (leftGrip.parent == null) return;
        string weaponName = leftGrip.parent.name;
        if (thirdPersonFrame == null) return;
        if (weaponName != "HK416_Weapon" && weaponName != "L115A3_Weapon") return;
        var upperArm = leftHand.parent.parent;
        float reach = (Vector3.Distance(upperArm.position, leftHand.parent.position) +
            Vector3.Distance(leftHand.parent.position, leftHand.position)) * .99f;
        Vector3 delta = leftGrip.position - upperArm.position;
        Vector3 forward = thirdPersonFrame.forward;
        float along = Vector3.Dot(delta, forward);
        float lateralSquared = delta.sqrMagnitude - along * along;
        if (delta.sqrMagnitude <= reach * reach || along <= 0f) return;
        float shift = Mathf.Clamp(along - Mathf.Sqrt(Mathf.Max(0f, reach * reach - lateralSquared)), 0f, .25f);
        adjustedShoulder = upperArm;
        shoulderRestPosition = upperArm.localPosition;
        upperArm.position += forward * shift;
        shoulderPosedPosition = upperArm.localPosition;
    }
    public Transform LeftHand => leftHand;
    public void SetThirdPersonFrame(Transform frame) => thirdPersonFrame = frame;

    public void CopyConfigurationTo(WeaponHandIK target, System.Collections.Generic.Dictionary<Transform, Transform> bones)
    {
        target.animationSource = animationSource;
        target.leftHand = bones[leftHand]; target.rightHand = bones[rightHand];
        target.leftGrip = leftGrip; target.rightGrip = rightGrip;
        target.leftHandWeight = leftHandWeight; target.rightHandWeight = rightHandWeight;
    }

    public void SetGrips(Transform left, Transform right)
    {
        RestoreWeaponReach();
        RestoreShoulder();
        leftGrip = left; rightGrip = right; leftSolver = rightSolver = null;
    }

    private void LateUpdate()
    {
        if (animationSource == null || !animationSource.isActiveAndEnabled ||
            animationSource.LastEvaluatedFrame != Time.frameCount || !animationSource.CanPoseHands) return;
        ApplyBreathing(Time.time, Time.deltaTime, animationSource.IsIdlePlaying);
        Solve();
    }

    /// <summary>Small additive chest motion, after sampling and before hand IK. Never runs on FPS arms.</summary>
    public void ApplyBreathing(float time, float deltaTime, bool idle)
    {
        if (thirdPersonFrame == null) return;
        RestoreBreathing();
        if (!searchedChest)
        {
            searchedChest = true;
            breathingChest = transform.Find("Root/root/Hips/Spine/Chest");
            // Current exported character uses Chest; older rigs used Spine.002.
            foreach (var bone in GetComponentsInChildren<Transform>(true))
            {
                if (breathingChest != null) break;
                if (bone.name == "Chest") breathingChest = bone;
            }
            if (breathingChest == null)
                foreach (var bone in GetComponentsInChildren<Transform>(true))
                    if (bone.name == "Spine.002") { breathingChest = bone; break; }
        }
        if (breathingChest == null) return;
        float speed = hasPreviousPosition && deltaTime > 0f
            ? Vector3.Distance(thirdPersonFrame.position, previousPosition) / deltaTime : 0f;
        previousPosition = thirdPersonFrame.position; hasPreviousPosition = true;
        float target = idle ? 1f - Mathf.InverseLerp(.1f, 1.2f, speed) : 0f;
        breathingWeight = Mathf.Lerp(breathingWeight, target, 1f - Mathf.Exp(-5f * deltaTime));
        float phase = time * (2f * Mathf.PI / 4.2f) + (thirdPersonFrame.GetInstanceID() & 255) * .024f;
        float breath = Mathf.Sin(phase) * breathingWeight * breathingAmount;
        chestPosition = breathingChest.localPosition;
        chestRotation = breathingChest.localRotation;
        breathingChest.position += thirdPersonFrame.up * (.006f * breath) + thirdPersonFrame.forward * (.003f * breath);
        breathingChest.rotation = Quaternion.AngleAxis(.65f * breath, thirdPersonFrame.right) * breathingChest.rotation;
        posedChestPosition = breathingChest.localPosition;
        posedChestRotation = breathingChest.localRotation;
        hasBreathingPose = true;
    }

    private void RestoreBreathing()
    {
        if (!hasBreathingPose || breathingChest == null) return;
        // Sparse clips may not rewrite the chest. Remove our previous addition
        // only if animation hasn't already replaced that transform this frame.
        if (breathingChest.localPosition.Equals(posedChestPosition)) breathingChest.localPosition = chestPosition;
        if (breathingChest.localRotation.Equals(posedChestRotation)) breathingChest.localRotation = chestRotation;
        hasBreathingPose = false;
    }

    // Menu display rigs sample clips manually and solve only their copied bones.
    public void Solve()
    {
        RestoreShoulder();
        RestoreWeaponReach();
        if (leftHand == null || rightHand == null || leftGrip == null || rightGrip == null) return;
        bool winchester = IsWinchester;
        if (winchester && GetComponentInParent<PlayerModelPresentation>() == null) FitWinchesterReach();
        leftSolver ??= CreateSolver(leftHand, leftGrip, AvatarIKGoal.LeftHand);
        rightSolver ??= CreateSolver(rightHand, rightGrip, AvatarIKGoal.RightHand);
        grenadeThrow ??= GetComponentInParent<GrenadeThrowIK>();
        if (leftSolver != null && grenadeThrow != null &&
            grenadeThrow.Sample(leftHand, leftGrip, out var position, out var rotation, out var elbow))
        {
            leftSolver.target = null;
            leftSolver.IKPosition = position;
            leftSolver.IKRotation = rotation;
            leftSolver.SetBendGoalPosition(elbow, 1f);
            leftSolver.IKPositionWeight = leftHandWeight;
            leftSolver.IKRotationWeight = leftHandWeight;
            leftSolver.Update();
        }
        else
        {
            if (!winchester && animationSource != null && WeaponProceduralMotion.Supports(animationSource.WeaponId))
                FitProceduralArm(leftHand,leftGrip,false);
            else if (!winchester) FitLongGunLeftArm();
            UpdateSolver(leftSolver, leftGrip, leftHandWeight, -1f);
        }
        if (!winchester && animationSource != null && WeaponProceduralMotion.Supports(animationSource.WeaponId))
            FitProceduralArm(rightHand,rightGrip,true);
        UpdateSolver(rightSolver, rightGrip, rightHandWeight, 1f);
    }

    private IKSolverLimb CreateSolver(Transform hand, Transform target, AvatarIKGoal goal)
    {
        if (hand.parent == null || hand.parent.parent == null) return null;
        // Animation mode stores a bend axis at initialization. Instead, derive the
        // pole from the freshly sampled elbow each frame, including one-frame poses.
        var solver = new IKSolverLimb(goal) { target = target, bendModifierWeight = 0f };
        return solver.SetChain(hand.parent.parent, hand.parent, hand, transform) ? solver : null;
    }

    private void UpdateSolver(IKSolverLimb solver, Transform grip, float weight, float side)
    {
        if (solver == null) return;
        solver.target = grip;
        solver.IKPosition = grip.position;
        Vector3 elbow = solver.bone2.transform.position;
        if (thirdPersonFrame != null)
        {
            // FPS clips spread the elbows for the camera. A body-relative pole
            // brings them down without moving the hands off the weapon grips.
            // Scale by the actual arm length instead of assuming model units.
            Vector3 shoulder = solver.bone1.transform.position;
            float length = Vector3.Distance(shoulder, elbow) +
                Vector3.Distance(elbow, solver.bone3.transform.position);
            Vector3 relaxed = shoulder + thirdPersonFrame.TransformDirection(new Vector3(side * .35f, -1f, -.1f)) * length;
            elbow = Vector3.Lerp(elbow, relaxed, thirdPersonElbowWeight);
        }
        solver.SetBendGoalPosition(elbow, 1f);
        solver.IKPositionWeight = weight;
        solver.IKRotationWeight = weight;
        solver.Update();
    }

    private void OnDisable()
    {
        RestoreWeaponReach();
        RestoreBreathing();
        RestoreShoulder();
        breathingWeight = 0f; hasPreviousPosition = false;
        leftSolver = rightSolver = null;
    }
}
