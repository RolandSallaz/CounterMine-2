using UnityEngine;

/// <summary>Aim point: +Z along the sight axis, +Y up. Works for irons and mounted optics.</summary>
[DisallowMultipleComponent]
public sealed class WeaponSight : MonoBehaviour
{
    [SerializeField] private Transform aimPoint;
    [SerializeField, Min(.01f)] private float eyeRelief = .08f;
    [SerializeField, Range(5f, 100f)] private float aimedFieldOfView = 60f;
    [Tooltip("Higher priority optics are selected automatically unless a sight was explicitly selected.")]
    [SerializeField] private int selectionPriority;
    public Transform AimPoint => aimPoint != null ? aimPoint : transform;
    public float EyeRelief => eyeRelief;
    public float AimedFieldOfView => aimedFieldOfView;
    public int SelectionPriority => selectionPriority;
    private WeaponAimRig rig;
    private void OnEnable() => RefreshOwner();
    private void OnDisable() { if (rig != null) rig.RefreshAttachments(); }
    private void OnTransformParentChanged() => RefreshOwner();
    private void OnValidate() => RefreshOwner();
    private void RefreshOwner()
    {
        if (rig != null) rig.RefreshAttachments();
        rig = GetComponentInParent<WeaponAimRig>();
        if (rig != null) rig.RefreshAttachments();
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(AimPoint.position, AimPoint.forward * .2f);
        Gizmos.DrawWireSphere(AimPoint.position, .004f);
    }
}
