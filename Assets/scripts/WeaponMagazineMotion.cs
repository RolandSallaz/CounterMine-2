using UnityEngine;

/// <summary>Magazine exchange and supporting-hand reach during synchronized procedural reloads.</summary>
[DefaultExecutionOrder(200)]
public sealed class WeaponMagazineMotion : MonoBehaviour
{
    public WeaponIdleSynchronizer source;
    public Transform magazine, leftGrip;
    private Vector3 magazineRest, gripRest;
    private Quaternion gripRotation;
    private bool captured;
    private void OnEnable()
    {
        if (magazine == null || leftGrip == null) return;
        magazineRest = magazine.localPosition; gripRest = leftGrip.localPosition;
        gripRotation = leftGrip.localRotation; captured = true;
    }
    private void LateUpdate()
    {
        if (!captured || source == null) return;
        magazine.localPosition = magazineRest; leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation;
        if (!source.ProceduralReload || source.WeaponRoot != transform) return;
        float t = source.ActionProgress;
        float pull = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, .43f, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.58f, .8f, t)));
        magazine.localPosition += magazine.parent.InverseTransformVector(transform.TransformDirection(new Vector3(-.025f, -.16f, .015f)) * pull);
        float reach = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.02f, .18f, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.83f, .98f, t)));
        var target = magazine.position + transform.TransformDirection(new Vector3(-.025f, -.025f, 0));
        leftGrip.position = Vector3.Lerp(leftGrip.position, target, reach);
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (magazine != null) magazine.localPosition = magazineRest;
        if (leftGrip != null) { leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation; }
        captured = false;
    }
}
