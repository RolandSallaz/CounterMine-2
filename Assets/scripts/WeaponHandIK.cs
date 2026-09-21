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
    private IKSolverLimb leftSolver;
    private IKSolverLimb rightSolver;
    private GrenadeThrowIK grenadeThrow;
    public Transform LeftHand => leftHand;

    public void CopyConfigurationTo(WeaponHandIK target, System.Collections.Generic.Dictionary<Transform, Transform> bones)
    {
        target.animationSource = animationSource;
        target.leftHand = bones[leftHand]; target.rightHand = bones[rightHand];
        target.leftGrip = leftGrip; target.rightGrip = rightGrip;
        target.leftHandWeight = leftHandWeight; target.rightHandWeight = rightHandWeight;
    }

    public void SetGrips(Transform left, Transform right)
    {
        leftGrip = left; rightGrip = right; leftSolver = rightSolver = null;
    }

    private void LateUpdate()
    {
        if (animationSource == null || !animationSource.isActiveAndEnabled ||
            animationSource.LastEvaluatedFrame != Time.frameCount || !animationSource.CanPoseHands) return;
        Solve();
    }

    private void Solve()
    {
        if (leftHand == null || rightHand == null || leftGrip == null || rightGrip == null) return;
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
        else UpdateSolver(leftSolver, leftGrip, leftHandWeight);
        UpdateSolver(rightSolver, rightGrip, rightHandWeight);
    }

    private IKSolverLimb CreateSolver(Transform hand, Transform target, AvatarIKGoal goal)
    {
        if (hand.parent == null || hand.parent.parent == null) return null;
        // Animation mode stores a bend axis at initialization. Instead, derive the
        // pole from the freshly sampled elbow each frame, including one-frame poses.
        var solver = new IKSolverLimb(goal) { target = target, bendModifierWeight = 0f };
        return solver.SetChain(hand.parent.parent, hand.parent, hand, transform) ? solver : null;
    }

    private static void UpdateSolver(IKSolverLimb solver, Transform grip, float weight)
    {
        if (solver == null) return;
        solver.target = grip;
        solver.IKPosition = grip.position;
        // This is still the animated elbow, before IK modifies the chain.
        solver.SetBendGoalPosition(solver.bone2.transform.position, 1f);
        solver.IKPositionWeight = weight;
        solver.IKRotationWeight = weight;
        solver.Update();
    }

    private void OnDisable() { leftSolver = rightSolver = null; }
}
