using UnityEngine;

/// <summary>Magazine exchange and supporting-hand reach during synchronized procedural reloads.</summary>
[DefaultExecutionOrder(200)]
public sealed class WeaponMagazineMotion : MonoBehaviour
{
    public WeaponIdleSynchronizer source;
    public Transform magazine, leftGrip, bolt;
    public bool boltAction;
    private Vector3 magazineRest, gripRest;
    private Vector3 boltRest;
    private Quaternion magazineRotation, gripRotation;
    private bool captured;
    private void OnEnable()
    {
        if (magazine == null || leftGrip == null) return;
        magazineRest = magazine.localPosition; magazineRotation = magazine.localRotation;
        gripRest = leftGrip.localPosition; gripRotation = leftGrip.localRotation;
        if (bolt != null) boltRest = bolt.localPosition;
        captured = true;
    }
    private void LateUpdate()
    {
        if (!captured || source == null) return;
        magazine.localPosition = magazineRest; magazine.localRotation = magazineRotation;
        leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation;
        if (bolt != null && source.ProceduralReload) bolt.localPosition = boltRest;
        if (!source.ProceduralReload || source.WeaponRoot != transform) return;
        float t = source.ActionProgress;
        float reach = WeaponIdleSynchronizer.SmoothWindow(t, .07f, .22f, .82f, .96f);
        float pull = WeaponIdleSynchronizer.SmoothWindow(t, .23f, .39f, .59f, .79f);
        float seat = WeaponIdleSynchronizer.SmoothWindow(t, .77f, .81f, .85f, .89f);
        magazine.localPosition += magazine.parent.InverseTransformVector(transform.TransformDirection(
            new Vector3(-.04f, -.14f, -.055f) * pull + Vector3.up * (.012f * seat)));
        magazine.localRotation = magazineRotation * Quaternion.Euler(8f * pull, 0f, -12f * pull);
        Vector3 target = magazine.position + transform.TransformDirection(new Vector3(-.025f, -.025f, .008f));
        leftGrip.position = Vector3.Lerp(leftGrip.position, target, reach);
        leftGrip.localRotation = gripRotation * Quaternion.Euler(0f, 0f, -18f * reach);
        if (boltAction && bolt != null)
        {
            float cycle = WeaponIdleSynchronizer.SmoothWindow(t, .83f, .87f, .91f, .96f);
            bolt.localPosition = boltRest + bolt.parent.InverseTransformVector(-transform.forward * (.085f * cycle));
            float boltReach = WeaponIdleSynchronizer.SmoothWindow(t, .79f, .86f, .91f, .99f);
            leftGrip.position = Vector3.Lerp(leftGrip.position, bolt.position + transform.TransformDirection(new Vector3(-.025f, -.015f, .005f)), boltReach);
        }
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (magazine != null) { magazine.localPosition = magazineRest; magazine.localRotation = magazineRotation; }
        if (bolt != null) bolt.localPosition = boltRest;
        if (leftGrip != null) { leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation; }
        captured = false;
    }
}
