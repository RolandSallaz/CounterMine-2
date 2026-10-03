using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ValidateHK416Placement
{
    static ValidateHK416Placement() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/validate-hk-placement.request";
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        var report = new StringBuilder();
        try
        {
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var entry = InstallNewWeapons.Entries(sync).Single(e => e.id == "hk416");
            var aim = player.GetComponentInChildren<WeaponAimController>(true);
            void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
            Call(aim, "Awake");
            sync.EquipWeapon("hk416"); sync.RestartIdle();
            player.GetComponent<PlayerModelPresentation>().ConfigureView(false);
            foreach (var ik in player.GetComponentsInChildren<WeaponHandIK>(true))
            {
                var hand = ik.LeftHand;
                var length = Vector3.Distance(hand.position, hand.parent.position) + Vector3.Distance(hand.parent.position, hand.parent.parent.position);
                report.AppendLine(ik.name + " arm length=" + length + " target distance=" + Vector3.Distance(hand.parent.parent.position, entry.leftGrip.position));
                ik.Solve();
                report.AppendLine("Left grip error=" + Vector3.Distance(hand.position, entry.leftGrip.position));
                if (Vector3.Distance(hand.position, entry.leftGrip.position) > .001f) throw new System.Exception("Left grip unreachable");
            }
            var camera = player.GetComponentInChildren<Camera>(true);
            Vector3 MeasureADS()
            {
                Call(aim, "Step", true, 1f); Call(aim, "ApplyPose", 1f);
                var point = camera.WorldToViewportPoint(entry.aimRig.ActiveSight.AimPoint.position);
                if (Mathf.Abs(point.x - .5f) > .001f || Mathf.Abs(point.y - .5f) > .001f) throw new System.Exception("ADS off center");
                return camera.WorldToViewportPoint(entry.animator.transform.Find("Model").TransformPoint(new Vector3(-2.029f,.88583f,0)));
            }
            var shifted = MeasureADS();
            Call(aim, "Step", false, 1f); Call(aim, "ApplyPose", 1f);
            entry.animator.transform.position -= player.transform.forward * .04f;
            var original = MeasureADS();
            report.AppendLine("ADS front post original=" + original.ToString("F6") + " shifted=" + shifted.ToString("F6"));
            if (Vector3.Distance(original, shifted) > .0001f) throw new System.Exception("Forward placement changed ADS");
            report.AppendLine("PASS: third-person hand reach and unchanged ADS alignment. No player build.");
        }
        catch (System.Exception e) { report.AppendLine(e.ToString()); }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        File.WriteAllText("Documentation/NewWeapons/hk416-placement.txt", report.ToString());
    }
}
