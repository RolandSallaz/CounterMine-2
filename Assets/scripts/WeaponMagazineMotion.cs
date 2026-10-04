using UnityEngine;

/// <summary>Magazine exchange and supporting-hand reach during synchronized procedural reloads.</summary>
[DefaultExecutionOrder(200)]
public sealed class WeaponMagazineMotion : MonoBehaviour
{
    public WeaponIdleSynchronizer source;
    public Transform magazine, leftGrip, rightGrip, bolt;
    public bool boltAction;
    private Vector3 magazineRest, gripRest;
    private Vector3 boltRest;
    private Quaternion magazineRotation, gripRotation;
    private Vector3 rightRest;
    private Quaternion rightRotation;
    private bool captured;
    private void OnEnable()
    {
        if (captured) return;
        if (magazine == null || leftGrip == null) return;
        magazineRest = magazine.localPosition; magazineRotation = magazine.localRotation;
        gripRest = leftGrip.localPosition; gripRotation = leftGrip.localRotation;
        if (bolt != null) boltRest = bolt.localPosition;
        if (rightGrip != null) { rightRest=rightGrip.localPosition;rightRotation=rightGrip.localRotation; }
        captured = true;
    }
    private void LateUpdate()
    {
        if (!captured || source == null) return;
        magazine.localPosition = magazineRest; magazine.localRotation = magazineRotation;
        leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation;
        if(rightGrip!=null){rightGrip.localPosition=rightRest;rightGrip.localRotation=rightRotation;}
        if (bolt != null && source.ProceduralReload) bolt.localPosition = boltRest;
        if (source.WeaponId == "l115a3" && source.IsIdlePlaying && rightGrip != null && bolt != null)
            ReachBolt(WeaponProceduralMotion.Window(source.ShotAge,.12f,.32f,.80f,1.15f));
        if (!source.ProceduralReload || source.WeaponRoot != transform) return;
        float t = source.ActionProgress;
        float reach = WeaponProceduralMotion.Window(t, .04f, .16f, .78f, .98f);
        float pull = WeaponProceduralMotion.Window(t, .19f, .34f, .46f, .64f);
        float seat = WeaponProceduralMotion.Window(t, .63f, .66f, .68f, .72f);
        float fetch = WeaponProceduralMotion.Window(t, .34f, .43f, .46f, .57f);
        magazine.localPosition += magazine.parent.InverseTransformVector(transform.TransformDirection(
            new Vector3(-.045f, -.15f, -.045f) * pull + new Vector3(-.025f,-.035f,-.02f)*fetch + Vector3.up * (.009f * seat)));
        magazine.localRotation = magazineRotation * Quaternion.Euler(10f * pull, -8f*fetch, -16f * pull);
        Vector3 target = magazine.position + transform.TransformDirection(new Vector3(-.025f, -.025f, .008f));
        leftGrip.position = Vector3.Lerp(leftGrip.position, target, reach);
        leftGrip.localRotation = gripRotation * Quaternion.Euler(-8f*fetch, 5f*pull, -18f * reach);
        if (boltAction && bolt != null)
        {
            float cycle = WeaponProceduralMotion.Window(t, .77f, .82f, .86f, .93f);
            bolt.localPosition = boltRest + bolt.parent.InverseTransformVector(-transform.forward * (.085f * cycle));
            float boltReach = WeaponProceduralMotion.Window(t, .72f, .80f, .88f, .98f);
            if(source.WeaponId=="l115a3"&&rightGrip!=null)
            {
                leftGrip.localPosition=Vector3.Lerp(leftGrip.localPosition,gripRest,boltReach);
                ReachBolt(boltReach);
            }
            else leftGrip.position = Vector3.Lerp(leftGrip.position, bolt.position + transform.TransformDirection(new Vector3(-.025f, -.015f, .005f)), boltReach);
        }
    }
    private void ReachBolt(float weight)
    {
        Vector3 target=bolt.position+transform.TransformDirection(new Vector3(.025f,-.012f,.005f));
        rightGrip.position=Vector3.Lerp(rightGrip.position,target,weight);
        rightGrip.localRotation=rightRotation*Quaternion.Euler(0f,18f*weight,12f*weight);
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (magazine != null) { magazine.localPosition = magazineRest; magazine.localRotation = magazineRotation; }
        if (bolt != null) bolt.localPosition = boltRest;
        if (leftGrip != null) { leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation; }
        if (rightGrip != null) { rightGrip.localPosition=rightRest;rightGrip.localRotation=rightRotation; }
        captured = false;
    }
}
