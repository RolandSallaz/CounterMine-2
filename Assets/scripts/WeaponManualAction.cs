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
        float reload = source.ProceduralReload ? Mathf.Sin(source.ActionProgress * Mathf.PI) : 0;
        if (revolver)
        {
            if (cylinder != null) cylinder.localRotation = cylinderRest * Quaternion.AngleAxis(chamber * 72f, Vector3.up);
            if (cylinderArm != null) cylinderArm.localRotation = armRest * Quaternion.AngleAxis(-65f * reload, Vector3.right);
            if (leftGrip != null) leftGrip.localPosition = gripRest + new Vector3(-.065f, -.04f, -.02f) * reload;
        }
        else
        {
            float cycle = WeaponIdleSynchronizer.BoltCycle(source.ShotAge - .10f, .48f);
            if (pump != null) pump.localPosition = pumpRest + pump.parent.InverseTransformVector(-transform.forward * (.085f * cycle));
            if (leftGrip != null) leftGrip.localPosition = gripRest + Vector3.back * (.085f * cycle) +
                new Vector3(.015f, -.055f, -.16f) * reload;
        }
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (pump != null) pump.localPosition = pumpRest;
        if (leftGrip != null) leftGrip.localPosition = gripRest;
        if (cylinder != null) cylinder.localRotation = cylinderRest;
        if (cylinderArm != null) cylinderArm.localRotation = armRest;
    }
}
