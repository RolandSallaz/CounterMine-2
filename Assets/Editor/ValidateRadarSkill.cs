using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ValidateRadarSkill
{
    private const string Folder = "Documentation/Character/";
    static ValidateRadarSkill() => EditorApplication.delayCall += AutoRun;
    private static void AutoRun()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists(Folder + "radar_manual_validation.txt")) Run();
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    private static PlayerHealth.KillInfo Kill(int killer = 1, int victim = 2, int team = 2) =>
        new PlayerHealth.KillInfo(victim, killer, -1, "Victim", "Killer", "", team, 1, 0, "ak74");
    [MenuItem("Tools/CounterMine/Validate Radar")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.NewPreviewScene();
        Material radar = null, wallMaterial = null;
        RenderTexture target = null; Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            var progress = new RadarChargeProgress();
            Check(!progress.Record(Kill(3), 1, 1), "Other player's kill counted");
            Check(!progress.Record(Kill(1, 1), 1, 1), "Suicide counted");
            Check(!progress.Record(Kill(1, 2, 1), 1, 1), "Team kill counted");
            Check(progress.Kills == 0, "Invalid kills changed progress");
            for (int i = 0; i < 4; i++) Check(!progress.Record(Kill(), 1, i + 2), "Activated before five kills");
            // The local player dies, then continues killing in another life.
            progress.Record(Kill(3, 1), 1, 6);
            Check(progress.Kills == 4, "Death reset accumulated kills");
            Check(!progress.TryActivate(9), "Activated without a charge");
            Check(progress.Record(Kill(1, -1001), 1, 10), "Fifth bot kill did not grant charge");
            Check(progress.Kills == 0 && progress.Charges == 1 && progress.Remaining(10) == 0, "Charge auto-activated");
            Check(progress.TryActivate(12) && progress.Charges == 0 && progress.Remaining(12) == 15, "Manual activation failed");
            for (int i = 0; i < 10; i++) progress.Record(Kill(), 1, 20);
            Check(progress.Charges == 2 && !progress.TryActivate(20) && progress.Charges == 2, "Active radar consumed another charge");
            Check(progress.Remaining(27) == 0 && progress.TryActivate(27) && progress.Charges == 1, "Queued charge failed after expiry");
            progress.Reset(); Check(progress.Kills == 0 && progress.Charges == 0 && progress.Remaining(30) == 0, "Room reset failed");
            var shader = Resources.Load<Shader>("VFX/RadarHighlight");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Radar shader missing/invalid");
            radar = new Material(shader);
            wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wallMaterial.SetColor("_BaseColor", new Color(.15f, .2f, .25f));
            var cameraObject = new GameObject("Radar test camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(0, 0, -5); camera.orthographic = true; camera.orthographicSize = 2;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0, 0, -1); wall.transform.localScale = new Vector3(4, 4, .3f);
            wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule); SceneManager.MoveGameObjectToScene(enemy, scene);
            enemy.GetComponent<Renderer>().sharedMaterial = radar;
            target = new RenderTexture(512, 512, 24); camera.targetTexture = target;
            pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
            Color highlighted = pixels.GetPixel(256, 256);
            Check(highlighted.r > highlighted.b + .1f, "Silhouette hidden by opaque wall");
            File.WriteAllBytes(Folder + "radar_preview.png", pixels.EncodeToPNG());
            enemy.GetComponent<Renderer>().enabled = false;
            camera.Render(); pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
            Check(pixels.GetPixel(256, 256).r < highlighted.r - .1f, "Disabled overlay still visible");
            File.WriteAllText(Folder + "radar_manual_validation.txt", "PASS: threshold five; enemy bot kills count; other players/suicide/team kills excluded; death retains progress; manual activation; no automatic activation; charges accumulate; no consumption while active; duration 15 seconds; room reset. URP shader compiles and renders through opaque wall; disabling removes effect.\nLive skinned player/camera isolation and two-client behavior still require Play Mode verification.\n");
            Debug.Log("[Radar] Validation passed.");
        }
        catch (Exception error) { File.WriteAllText(Folder + "radar_validation_error.txt", error.ToString()); Debug.LogException(error); }
        finally
        {
            RenderTexture.active = previous;
            if (radar != null) UnityEngine.Object.DestroyImmediate(radar);
            if (wallMaterial != null) UnityEngine.Object.DestroyImmediate(wallMaterial);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
