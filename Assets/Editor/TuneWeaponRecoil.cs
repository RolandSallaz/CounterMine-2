using System;
using System.IO;
using System.Linq;
using Kinemation.Recoilly;
using UnityEditor;
using UnityEngine;

/// <summary>Independent authored recoil profiles. Never edits AK74/UCP or their entries.</summary>
[InitializeOnLoad]
public static class TuneWeaponRecoil
{
    const string Folder = "Assets/Resources/Recoil";
    static TuneWeaponRecoil() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/tune-weapon-recoil.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Run(); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/recoil-tuning-result.txt", "FAIL: " + e); }
    }

    // A sharp impulse, followed by a slower, monotonic recovery: no spring bounce through the rest pose.
    static AnimationCurve Curve(float peak, float settle, float end) => new AnimationCurve(
        new Keyframe(0, 0, 0, 0), new Keyframe(peak, 1, 0, 0),
        new Keyframe(settle, .24f, 0, 0), new Keyframe(end, 0, 0, 0));
    static VectorCurve Curves(float peak, float settle, float end) => new VectorCurve {
        x = Curve(peak, settle, end), y = Curve(peak, settle, end), z = Curve(peak, settle, end) };

    public static void Apply(WeaponIdleSynchronizer.WeaponEntry entry)
    {
        if (entry == null || entry.id == "ak74" || entry.id == "ucp") return;
        float rise, backward, recovery, camera, side;
        switch (entry.id)
        {
            case "hk416": rise = 1.45f; backward = .0055f; recovery = .19f; camera = .30f; side = .10f; break;
            case "l115a3": rise = 3.4f; backward = .011f; recovery = .39f; camera = 1.05f; side = .14f; break;
            case "rsh12": rise = 6.1f; backward = .008f; recovery = .34f; camera = 1.30f; side = .16f; break;
            case "winchester1897": rise = 4.0f; backward = .012f; recovery = .35f; camera = 1.10f; side = .13f; break;
            case "milkor": rise = 3.0f; backward = .011f; recovery = .42f; camera = .80f; side = .12f; break;
            default: return;
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Recoil");
        string path = Folder + "/" + entry.id + ".asset";
        var profile = AssetDatabase.LoadAssetAtPath<RecoilAnimData>(path);
        if (profile == null) { profile = ScriptableObject.CreateInstance<RecoilAnimData>(); AssetDatabase.CreateAsset(profile, path); }
        profile.pitch = new Vector2(-rise * 1.06f, -rise * .94f);
        profile.yaw = new Vector4(-side, -side * .2f, side * .2f, side);
        profile.roll = profile.yaw * .65f;
        profile.kickback = new Vector2(-backward, -backward * .85f);
        profile.kickUp = new Vector2(0, .0006f);
        profile.kickRight = new Vector2(-.00015f, .00015f);
        profile.aimRot = new Vector3(.72f, .65f, .6f);
        profile.aimLoc = new Vector3(.7f, .7f, .85f);
        profile.smoothRot = profile.smoothLoc = Vector3.one * 25f;
        profile.extraRot = profile.extraLoc = Vector3.one;
        profile.noiseX = profile.noiseY = Vector2.zero;
        profile.noiseAccel = profile.noiseDamp = Vector2.one * 20f;
        profile.noiseScalar = 1; profile.pushAmount = 0;
        profile.pushAccel = profile.pushDamp = 20;
        profile.smoothRoll = true; profile.playRate = 1;
        float peak = entry.id == "hk416" ? .018f : .027f;
        profile.recoilCurves = new RecoilCurves {
            semiRotCurve = Curves(peak, recovery * .42f, recovery),
            semiLocCurve = Curves(.012f, recovery * .25f, recovery * .78f),
            autoRotCurve = Curves(peak, recovery * .42f, recovery),
            autoLocCurve = Curves(.012f, recovery * .25f, recovery * .78f) };
        entry.recoil ??= new WeaponRecoilController.Tuning();
        entry.recoil.animation = profile;
        entry.recoil.rotateAroundGrip = true;
        entry.recoil.cameraPitch = new Vector2(camera * .9f, camera * 1.1f);
        entry.recoil.cameraYaw = side;
        entry.recoil.heatPerShot = entry.id == "hk416" ? .11f : .12f;
        entry.recoil.heatRecovery = entry.id == "hk416" ? .65f : 1.1f;
        entry.recoil.sustainedFireMultiplier = entry.id == "hk416" ? 1.3f : 1.08f;
        entry.recoilKick = 1f;
        EditorUtility.SetDirty(profile);
    }

    [MenuItem("CounterMine/Tune weapon recoil (except AK74 and UCP)")]
    static void Run()
    {
        foreach (string path in new[] { "Assets/Resources/Player.prefab", "Assets/Resources/Bot.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var sync = root.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                var entries = InstallNewWeapons.Entries(sync);
                var protectedEntries = entries.Where(e => e.id == "ak74" || e.id == "ucp").ToArray();
                var before = protectedEntries.Select(e => JsonUtility.ToJson(e)).ToArray();
                foreach (var entry in entries) Apply(entry);
                for (int i = 0; i < before.Length; i++)
                    if (before[i] != JsonUtility.ToJson(protectedEntries[i])) throw new Exception("Protected weapon changed");
                EditorUtility.SetDirty(sync); PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/recoil-tuning-result.txt", "PASS: five independent recoil profiles assigned to all matching Player/Bot entries; AK74 and UCP entries unchanged.");
    }
}
