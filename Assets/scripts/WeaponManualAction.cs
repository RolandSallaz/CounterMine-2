using UnityEngine;

/// <summary>Local mechanical motion driven by the synchronized weapon action clock.</summary>
[DefaultExecutionOrder(190)]
public sealed class WeaponManualAction : MonoBehaviour
{
    public WeaponIdleSynchronizer source;
    public bool revolver;
    public Transform cylinder, cylinderArm, pump, leftGrip;
    private Vector3 pumpRest, gripRest;
    private Quaternion cylinderRest, armRest;
    private bool captured;
    private int chamber;
    private void OnEnable()
    {
        if (captured) return;
        if (pump != null) pumpRest = pump.localPosition;
        if (leftGrip != null) gripRest = leftGrip.localPosition;
        if (cylinder != null) cylinderRest = cylinder.localRotation;
        if (cylinderArm != null) armRest = cylinderArm.localRotation;
        captured = true;
    }
    public void Shot() { chamber = (chamber + 1) % 5; }
    private void LateUpdate()
    {
        if (source == null || source.WeaponRoot != transform) return;
        OnEnable();
        float t = source.ProceduralReload ? source.ActionProgress : 0f;
        if (revolver)
        {
            float open = WeaponIdleSynchronizer.SmoothWindow(t, .1f, .27f, .77f, .95f);
            float load = WeaponIdleSynchronizer.SmoothWindow(t, .31f, .4f, .65f, .75f);
            float eject = WeaponIdleSynchronizer.SmoothWindow(t, .3f, .35f, .39f, .45f);
            float insert = Mathf.Sin(Mathf.PI * 3f * Mathf.InverseLerp(.45f, .74f, t)) * load;
            if (cylinder != null) cylinder.localRotation = cylinderRest * Quaternion.AngleAxis(chamber * 72f + (90f * load), Vector3.up);
            if (cylinderArm != null) cylinderArm.localRotation = armRest * Quaternion.AngleAxis(-65f * open, Vector3.right);
            if (leftGrip != null) leftGrip.localPosition = gripRest + new Vector3(-.075f, -.035f, -.035f) * open +
                new Vector3(-.015f, -.025f, -.018f) * eject + Vector3.up * (.014f * insert);
        }
        else
        {
            float cycle = source.ProceduralReload
                ? WeaponIdleSynchronizer.SmoothWindow(t, .12f, .19f, .25f, .32f) +
                  WeaponIdleSynchronizer.SmoothWindow(t, .84f, .89f, .93f, .97f)
                : WeaponIdleSynchronizer.BoltCycle(source.ShotAge - .10f, .48f);
            cycle = Mathf.Clamp01(cycle);
            if (pump != null) pump.localPosition = pumpRest + pump.parent.InverseTransformVector(-transform.forward * (.085f * cycle));
            if (leftGrip != null)
            {
                float load = WeaponIdleSynchronizer.SmoothWindow(t, .31f, .39f, .76f, .86f);
                float pulse = Mathf.Sin(Mathf.PI * 6f * Mathf.InverseLerp(.37f, .78f, t));
                leftGrip.localPosition = gripRest + Vector3.back * (.085f * cycle) +
                    new Vector3(.02f, -.055f, -.13f) * load + Vector3.up * (.017f * pulse * load);
            }
        }
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (pump != null) pump.localPosition = pumpRest;
        if (leftGrip != null) leftGrip.localPosition = gripRest;
        if (cylinder != null) cylinder.localRotation = cylinderRest;
        if (cylinderArm != null) cylinderArm.localRotation = armRest;
        captured = false;
    }
}
