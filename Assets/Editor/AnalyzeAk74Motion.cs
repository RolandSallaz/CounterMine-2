using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AnalyzeAk74Motion
{
    static AnalyzeAk74Motion() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/analyze-ak74-motion.request")) return;
        File.Delete("Temp/analyze-ak74-motion.request");
        try { Run(); } catch (Exception e) { File.WriteAllText("Documentation/NewWeapons/ak74-motion-analysis.txt", e.ToString()); Debug.LogException(e); }
    }
    public static void Run()
    {
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var ak = InstallNewWeapons.Entries(sync).Single(e => e.id == "ak74");
            var character = sync.CharacterAnimator.gameObject;
            var body = ak.animator.transform.Find("Armature/body");
            var camera = character.transform.Find("Root/root/camera");
            var ik = new SerializedObject(character.GetComponent<WeaponHandIK>());
            var left = (Transform)ik.FindProperty("leftHand").objectReferenceValue;
            var right = (Transform)ik.FindProperty("rightHand").objectReferenceValue;
            var report = new StringBuilder();
            foreach (string action in new[] { "equip", "reload" })
            {
                var armsClip = action == "equip" ? ak.characterEquip : ak.actions.Single(a => a.id == action).characterClip;
                var weaponClip = action == "equip" ? ak.weaponEquip : ak.actions.Single(a => a.id == action).weaponClip;
                ak.characterIdle.SampleAnimation(character, 0); ak.weaponIdle.SampleAnimation(ak.animator.gameObject, 0);
                Vector3 rest = player.transform.InverseTransformPoint(body.position);
                Quaternion rotation = Quaternion.Inverse(player.transform.rotation) * body.rotation;
                Quaternion cameraRest = Quaternion.Inverse(character.transform.rotation) * camera.rotation;
                var csv = new StringBuilder("frame,time,phase,x,y,z,pitch,yaw,roll,camPitch,camYaw,camRoll,leftX,leftY,leftZ,rightX,rightY,rightZ\n");
                Vector3 maxPosition = Vector3.zero, maxRotation = Vector3.zero, maxCamera = Vector3.zero;
                int frames = Mathf.RoundToInt(armsClip.length * armsClip.frameRate);
                for (int frame = 0; frame <= frames; frame++)
                {
                    float phase = (float)frame / frames;
                    armsClip.SampleAnimation(character, phase * armsClip.length);
                    weaponClip.SampleAnimation(ak.animator.gameObject, phase * weaponClip.length);
                    Vector3 position = player.transform.InverseTransformPoint(body.position) - rest;
                    Vector3 euler = Angles(Quaternion.Inverse(player.transform.rotation) * body.rotation * Quaternion.Inverse(rotation));
                    Vector3 cam = Angles(Quaternion.Inverse(cameraRest) * Quaternion.Inverse(character.transform.rotation) * camera.rotation);
                    Vector3 lh = body.InverseTransformPoint(left.position), rh = body.InverseTransformPoint(right.position);
                    maxPosition = MaxAbs(maxPosition, position); maxRotation = MaxAbs(maxRotation, euler); maxCamera = MaxAbs(maxCamera, cam);
                    csv.AppendLine(string.Join(",", new float[] {frame,phase*armsClip.length,phase,position.x,position.y,position.z,euler.x,euler.y,euler.z,cam.x,cam.y,cam.z,lh.x,lh.y,lh.z,rh.x,rh.y,rh.z}.Select(v => v.ToString("F6", CultureInfo.InvariantCulture))));
                }
                File.WriteAllText("Documentation/NewWeapons/ak74-" + action + "-frames.csv", csv.ToString());
                report.AppendLine(action + ": " + frames + " frames, " + armsClip.frameRate + " fps, " + armsClip.length + " seconds; weapon position peak=" + maxPosition + "; rotation peak=" + maxRotation + "; camera peak=" + maxCamera);
            }
            report.AppendLine("Catalog: " + string.Join(", ", InstallNewWeapons.Entries(sync).Select(e => e.id)));
            File.WriteAllText("Documentation/NewWeapons/ak74-motion-analysis.txt", report.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }
    static Vector3 MaxAbs(Vector3 a, Vector3 b) => new Vector3(Mathf.Max(a.x,Mathf.Abs(b.x)),Mathf.Max(a.y,Mathf.Abs(b.y)),Mathf.Max(a.z,Mathf.Abs(b.z)));
    public static Vector3 Angles(Quaternion q) { var a=q.eulerAngles; return new Vector3(Mathf.DeltaAngle(0,a.x),Mathf.DeltaAngle(0,a.y),Mathf.DeltaAngle(0,a.z)); }
}
