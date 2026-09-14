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

    private void LateUpdate()
    {
        if (animationSource == null || !animationSource.isActiveAndEnabled) return;
        Solve();
    }

    private void Solve()
    {
        if (leftHand == null || rightHand == null || leftGrip == null || rightGrip == null) return;
        leftSolver ??= CreateSolver(leftHand, leftGrip, AvatarIKGoal.LeftHand);
        rightSolver ??= CreateSolver(rightHand, rightGrip, AvatarIKGoal.RightHand);
        UpdateSolver(leftSolver, leftHandWeight);
        UpdateSolver(rightSolver, rightHandWeight);
    }

    private IKSolverLimb CreateSolver(Transform hand, Transform target, AvatarIKGoal goal)
    {
        if (hand.parent == null || hand.parent.parent == null) return null;
        var solver = new IKSolverLimb(goal) { target = target, bendModifier = IKSolverLimb.BendModifier.Animation };
        return solver.SetChain(hand.parent.parent, hand.parent, hand, transform) ? solver : null;
    }

    private static void UpdateSolver(IKSolverLimb solver, float weight)
    {
        if (solver == null) return;
        solver.IKPositionWeight = weight;
        solver.IKRotationWeight = weight;
        solver.Update();
    }

    private void OnDisable() { leftSolver = rightSolver = null; }
}
