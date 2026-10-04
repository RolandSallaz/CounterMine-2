using UnityEngine;

/// <summary>Frame-sampled AK74 timing and camera response, retargeted to each gun's grip.</summary>
[CreateAssetMenu(menuName = "CounterMine/Weapon Motion Reference")]
public sealed class WeaponMotionReference : ScriptableObject
{
    [System.Serializable] public struct Frame
    {
        public Vector3 position, rotation, camera;
    }
    public Frame[] equip, reload;
    public float equipDuration, reloadDuration;
    public Frame Sample(string action, float phase)
    {
        var frames = action == "equip" ? equip : reload;
        if (frames == null || frames.Length < 2) return default;
        float cursor = Mathf.Clamp01(phase) * (frames.Length - 1);
        int index = Mathf.Min(Mathf.FloorToInt(cursor), frames.Length - 2);
        float t = cursor - index;
        var a = frames[Mathf.Max(0,index-1)]; var b = frames[index];
        var c = frames[index+1]; var d = frames[Mathf.Min(frames.Length-1,index+2)];
        return new Frame { position = Cubic(a.position,b.position,c.position,d.position,t),
            rotation = Cubic(a.rotation,b.rotation,c.rotation,d.rotation,t), camera = Cubic(a.camera,b.camera,c.camera,d.camera,t) };
    }
    static Vector3 Cubic(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float t) =>
        .5f * ((2f*b) + (-a+c)*t + (2f*a-5f*b+4f*c-d)*t*t + (-a+3f*b-3f*c+d)*t*t*t);
}

public static class WeaponProceduralMotion
{
    static WeaponMotionReference reference;
    public static WeaponMotionReference Reference => reference != null ? reference : reference = Resources.Load<WeaponMotionReference>("WeaponMotion/AK74 Motion Reference");
    public static bool Supports(string id) => id != null && id != "ak74" && Reference != null;
    public static float Window(float t,float start,float peak,float release,float end) =>
        Ease(Mathf.InverseLerp(start,peak,t)) * (1f-Ease(Mathf.InverseLerp(release,end,t)));
    public static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6f-15f)+10f); }
    public static WeaponMotionReference.Frame EvaluateIndividualReload(string id,float hold,float phase,bool inserting,float cameraScale)
    {
        float seat=inserting?Window(phase,.45f,.60f,.64f,.82f):0f;
        float fetch=inserting?Window(phase,.02f,.18f,.32f,.54f):0f;
        return new WeaponMotionReference.Frame {
            position=(id=="rsh12"?new Vector3(-.035f,.035f,.012f):new Vector3(-.070f,.080f,.020f))*hold+new Vector3(-.003f,0f,-.004f)*fetch+Vector3.up*(.003f*seat),
            rotation=(id=="rsh12"?new Vector3(-10f,25f,-18f):new Vector3(-8f,14f,-24f))*hold+new Vector3(-.7f,.8f,.6f)*seat+new Vector3(.5f,-.8f,0)*fetch,
            camera=(new Vector3(.13f,-.06f,.08f)*seat+new Vector3(-.06f,.04f,0)*fetch)*(cameraScale>0?cameraScale:1f)
        };
    }
    public static WeaponMotionReference.Frame Evaluate(string id,string action,float t,float cameraScale)
    {
        if (!Supports(id) || (action != "equip" && action != "reload")) return default;
        var frame = Reference.Sample(action,t);
        bool pistol = id == "ucp" || id == "rsh12", heavy = id == "milkor", shell = id == "winchester1897" || id == "rsh12";
        if (action == "equip")
        {
            frame.position = Vector3.Scale(frame.position,new Vector3(pistol?.38f:.52f,heavy?.25f:.30f,.45f));
            frame.rotation = Vector3.Scale(frame.rotation,new Vector3(heavy?.26f:.40f,pistol?.42f:.48f,.45f));
        }
        else
        {
            float amplitude = shell ? .25f : id == "l115a3" ? .40f : pistol ? .48f : .52f;
            frame.position *= amplitude;
            frame.rotation *= amplitude;
            if (shell)
            {
                float hold = Window(t,.02f,.16f,.78f,1f);
                frame.position += new Vector3(id == "rsh12" ? -.018f : -.025f,.016f,.012f)*hold;
                frame.rotation += new Vector3(-3f,6f,id == "rsh12" ? -12f : -8f)*hold;
            }
        }
        // The camera follows the authored anticipation and delayed impacts,
        // with less amplitude for short, repeated shell-insertion actions.
        frame.camera *= (cameraScale > 0f ? cameraScale : .7f) * (action == "equip" ? .65f : shell ? .16f : .40f);
        // Imported endpoints have small numerical residuals. Remove them smoothly.
        float returnWeight = 1f-Ease(Mathf.InverseLerp(.94f,1f,t));
        frame.position *= returnWeight; frame.rotation *= returnWeight; frame.camera *= returnWeight;
        return frame;
    }
}
