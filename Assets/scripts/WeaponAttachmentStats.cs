using UnityEngine;

/// <summary>Add to any attachment. Only enabled, mounted attachments affect handling.</summary>
[DisallowMultipleComponent]
public sealed class WeaponAttachmentStats : MonoBehaviour
{
    [SerializeField, Range(-100f, 100f)] private float ergonomicsModifier;
    public float ErgonomicsModifier => ergonomicsModifier;
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
}
