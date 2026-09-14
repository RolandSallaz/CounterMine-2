using UnityEngine;

/// <summary>Per-weapon ADS binding. Put one on each weapon root under the common aim pivot.</summary>
[DisallowMultipleComponent]
public sealed class WeaponAimRig : MonoBehaviour
{
    [SerializeField] private WeaponHandlingProfile handling;
    [SerializeField] private WeaponSight defaultSight;
    [SerializeField] private WeaponSight selectedSight;
    private WeaponSight[] sights = System.Array.Empty<WeaponSight>();
    private WeaponAttachmentStats[] attachments = System.Array.Empty<WeaponAttachmentStats>();
    private bool dirty = true;
    private WeaponAimController controller;
    public WeaponHandlingProfile Handling => handling;
    public void RefreshAttachments() => dirty = true;
    private void Refresh()
    {
        if (!dirty) return;
        sights = GetComponentsInChildren<WeaponSight>(true);
        attachments = GetComponentsInChildren<WeaponAttachmentStats>(true);
        dirty = false;
    }
    private bool Mounted(Component item) => item != null && item.gameObject.activeInHierarchy &&
        item.transform.IsChildOf(transform) && item.GetComponentInParent<WeaponAimRig>() == this;
    public WeaponSight ActiveSight
    {
        get
        {
            Refresh();
            if (Mounted(selectedSight) && selectedSight.enabled) return selectedSight;
            var best = Mounted(defaultSight) && defaultSight.enabled ? defaultSight : null;
            foreach (var sight in sights)
                if (Mounted(sight) && sight.enabled && (best == null || sight.SelectionPriority > best.SelectionPriority)) best = sight;
            return best;
        }
    }
    public float Ergonomics
    {
        get
        {
            Refresh();
            float value = handling != null ? handling.ergonomics : 55f;
            foreach (var attachment in attachments)
                if (Mounted(attachment) && attachment.enabled) value += attachment.ErgonomicsModifier;
            return Mathf.Clamp(value, 0f, 100f);
        }
    }
    public bool SelectSight(WeaponSight sight)
    {
        if (sight != null && (!Mounted(sight) || !sight.enabled)) return false;
        selectedSight = sight; // null restores automatic priority selection.
        return true;
    }
    private void OnEnable()
    {
        dirty = true;
        controller = GetComponentInParent<WeaponAimController>();
        if (controller != null) controller.Equip(this);
    }
    private void OnDisable()
    {
        if (controller != null && controller.CurrentWeapon == this) controller.Equip(null);
    }
    private void OnTransformChildrenChanged() => RefreshAttachments();
    private void OnValidate() => RefreshAttachments();
}
