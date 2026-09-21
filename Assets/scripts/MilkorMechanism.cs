using UnityEngine;

/// <summary>Mechanical cylinder indexing; no reload animation or ammunition regeneration.</summary>
public sealed class MilkorMechanism : MonoBehaviour
{
    public Transform cylinder;
    private Quaternion rest;
    private float angle, target;
    private void Awake() { if (cylinder != null) rest = cylinder.localRotation; }
    public void Shot() => target += 60f;
    private void LateUpdate()
    {
        angle = Mathf.MoveTowards(angle, target, Time.deltaTime * 420f);
        if (cylinder != null) cylinder.localRotation = rest * Quaternion.AngleAxis(angle, Vector3.up);
    }
}
