using UnityEngine;

/// <summary>Local mechanical motion driven by the synchronized weapon action clock.</summary>
[DefaultExecutionOrder(190)]
public sealed class WeaponManualAction : MonoBehaviour
{
    public WeaponIdleSynchronizer source;
    public bool revolver;
    public Transform cylinder, cylinderArm, pump, leftGrip, reloadRound;
    private Vector3 pumpRest, gripRest;
    private Quaternion cylinderRest, armRest;
    private Quaternion gripRotation;
    private bool captured;
    private int chamber;
    private float reloadCylinderIndex, reloadCylinderStartIndex;
    private double observedReloadStart=double.NaN;
    private Transform model;
    private static Vector3 LoadingHandPath(Vector3 ready,Vector3 fetch,Vector3 entry,float phase)
    {
        if(phase<.18f)return Vector3.Lerp(ready,fetch,WeaponProceduralMotion.Ease(phase/.18f));
        if(phase<.32f)return fetch;
        Vector3 approach=entry+Vector3.back*.035f;
        if(phase<.54f)
        {
            float t=WeaponProceduralMotion.Ease(Mathf.InverseLerp(.32f,.54f,phase)),u=1f-t;
            return u*u*u*fetch+3f*u*u*t*(fetch+Vector3.up*.09f)+3f*u*t*t*(approach+Vector3.left*.035f)+t*t*t*approach;
        }
        if(phase<WeaponIdleSynchronizer.ReloadInsertPhase)return Vector3.Lerp(approach,entry,
            WeaponProceduralMotion.Ease(Mathf.InverseLerp(.54f,WeaponIdleSynchronizer.ReloadInsertPhase,phase)));
        return Vector3.Lerp(entry,ready,WeaponProceduralMotion.Ease(Mathf.InverseLerp(.68f,.94f,phase)));
    }
    private void OnEnable()
    {
        if (captured) return;
        if (pump != null) pumpRest = pump.localPosition;
        if (leftGrip != null) { gripRest = leftGrip.localPosition; gripRotation = leftGrip.localRotation; }
        if (cylinder != null) cylinderRest = cylinder.localRotation;
        if (cylinderArm != null) armRest = cylinderArm.localRotation;
        captured = true;
        model=transform.Find("Model");
    }
    public void Shot() { chamber = (chamber + 1) % 5; }
    private void LateUpdate()
    {
        if (source == null || source.WeaponRoot != transform) return;
        OnEnable();
        bool individual=source.ProceduralReload&&source.IndividualReload;
        float t = individual ? source.ReloadInsertionProgress : source.ProceduralReload ? source.ActionProgress : 0f;
        float hold=individual?source.ReloadHoldWeight:0f;
        if(reloadRound!=null)
        {
            reloadRound.gameObject.SetActive(source.ProceduralReload && (!individual||source.IsInsertingRound) && t>.18f && t<WeaponIdleSynchronizer.ReloadInsertPhase);
            if(leftGrip!=null)reloadRound.SetPositionAndRotation(leftGrip.position+transform.TransformDirection(new Vector3(.01f,.012f,.035f)),transform.rotation);
        }
        if (revolver)
        {
            float open = individual?hold:WeaponProceduralMotion.Window(t, .06f, .20f, .80f, .98f);
            float load = individual?(source.IsInsertingRound?WeaponProceduralMotion.Window(t,.02f,.20f,.52f,.94f):0f):WeaponProceduralMotion.Window(t, .24f, .36f, .66f, .78f);
            float eject = individual?0f:WeaponProceduralMotion.Window(t, .21f, .26f, .29f, .34f);
            float insert = WeaponProceduralMotion.Window(t, .52f, .60f, .64f, .70f);
            if(individual)
            {
                if(observedReloadStart!=source.StartedAt){observedReloadStart=source.StartedAt;reloadCylinderStartIndex=reloadCylinderIndex;}
                reloadCylinderIndex=reloadCylinderStartIndex+(source.IsInsertingRound?
                    source.ReloadInsertionIndex+WeaponProceduralMotion.Ease(Mathf.InverseLerp(.78f,1f,t)):source.CompletedReloadRounds);
            }
            else reloadCylinderIndex=Mathf.Round(reloadCylinderIndex);
            if (cylinder != null) cylinder.localRotation = cylinderRest * Quaternion.AngleAxis(chamber * 72f + 72f*reloadCylinderIndex + (individual?0f:90f*load), Vector3.up);
            if (cylinderArm != null) cylinderArm.localRotation = armRest * Quaternion.AngleAxis(-65f * open, Vector3.right);
            if (leftGrip != null)
            {
                leftGrip.localPosition = gripRest + new Vector3(-.075f, -.035f, -.035f) * open +
                    new Vector3(-.02f, -.04f, -.025f) * load + new Vector3(-.015f, -.025f, -.018f) * eject + Vector3.up * (.014f * insert);
                leftGrip.localRotation = gripRotation * Quaternion.Euler(-12f*load,8f*insert,-18f*open);
            }
        }
        else
        {
            float cycle = individual ? WeaponProceduralMotion.Window(source.ReloadClosingProgress,.48f,.65f,.76f,1f) : source.ProceduralReload
                ? WeaponIdleSynchronizer.SmoothWindow(t, .12f, .19f, .25f, .32f) +
                  WeaponIdleSynchronizer.SmoothWindow(t, .84f, .89f, .93f, .97f)
                : WeaponProceduralMotion.Window(source.ShotAge, .10f, .24f, .29f, .58f);
            cycle = Mathf.Clamp01(cycle);
            if (pump != null) pump.localPosition = pumpRest + pump.parent.InverseTransformVector(-transform.forward * (.085f * cycle));
            if (leftGrip != null)
            {
                float load = individual?hold:WeaponProceduralMotion.Window(t, .23f, .38f, .68f, .87f);
                float pulse = WeaponProceduralMotion.Window(t, .52f, .61f, .64f, .72f);
                float fetch=individual&&source.IsInsertingRound?WeaponProceduralMotion.Window(t,.02f,.20f,.44f,.94f):0f;
                leftGrip.localPosition = gripRest + Vector3.back * (.085f * cycle) +
                    new Vector3(.02f, -.055f, -.13f) * load + new Vector3(-.025f,-.035f,-.07f)*fetch + Vector3.up * (.017f * pulse * load);
                leftGrip.localRotation = gripRotation * Quaternion.Euler(-10f*load,6f*pulse,-16f*load);
            }
        }
        if(individual&&leftGrip!=null)
        {
            Vector3 entry;
            if(revolver&&cylinder!=null)
                entry=transform.InverseTransformPoint(cylinder.position)-new Vector3(.025f,.025f,.047f);
            else entry=model!=null?transform.InverseTransformPoint(model.TransformPoint(new Vector3(.30f,-.25f,0f)))-new Vector3(.050f,.050f,.055f):new Vector3(-.060f,-.078f,-.08f);
            Vector3 ready=entry+new Vector3(-.035f,-.03f,-.03f);
            Vector3 fetch=entry+new Vector3(-.11f,-.13f,-.045f);
            Vector3 hand=source.IsInsertingRound?LoadingHandPath(ready,fetch,entry,t):ready;
            float handHold=hold*(1f-WeaponProceduralMotion.Ease(Mathf.InverseLerp(0f,.45f,source.ReloadClosingProgress)));
            float closingPump=!revolver?WeaponProceduralMotion.Window(source.ReloadClosingProgress,.48f,.65f,.76f,1f):0f;
            leftGrip.localPosition=Vector3.Lerp(gripRest,hand,handHold)+Vector3.back*(.085f*closingPump);
            float retrieve=source.IsInsertingRound?WeaponProceduralMotion.Window(t,0f,.18f,.34f,.54f):0f;
            leftGrip.localRotation=Quaternion.Slerp(gripRotation,gripRotation*Quaternion.Euler(-18f+12f*retrieve,10f,-28f+18f*retrieve),handHold);
            if(reloadRound!=null)reloadRound.SetPositionAndRotation(leftGrip.position+transform.TransformDirection(new Vector3(revolver?.025f:.050f,revolver?.025f:.050f,.028f)),transform.rotation);
        }
    }
    private void OnDisable()
    {
        if (!captured) return;
        if (pump != null) pump.localPosition = pumpRest;
        if (leftGrip != null) { leftGrip.localPosition = gripRest; leftGrip.localRotation = gripRotation; }
        if (cylinder != null) cylinder.localRotation = cylinderRest;
        if (cylinderArm != null) cylinderArm.localRotation = armRest;
        captured = false;
        if(reloadRound!=null)reloadRound.gameObject.SetActive(false);
    }
}
