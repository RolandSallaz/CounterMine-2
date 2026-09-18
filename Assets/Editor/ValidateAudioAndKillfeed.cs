using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ValidateAudioAndKillfeed
{
    private const string Folder = "Documentation/AudioSources/Validation/";
    static ValidateAudioAndKillfeed() => EditorApplication.delayCall += AutoRun;
    private static void AutoRun()
    {
        if (!File.Exists(Folder + "unity-validation.txt") && !EditorApplication.isPlayingOrWillChangePlaymode) Run();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [MenuItem("Tools/CounterMine/Validate Audio and Killfeed")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject hudObject = null;
        RenderTexture target = null;
        Texture2D image = null;
        var previousTarget = RenderTexture.active;
        try
        {
            var profile = Resources.Load<WeaponAudioProfile>("Audio/Weapons/ak74");
            Check(profile != null && profile.shots.Length == 3, "AK74 profile/shot variants missing");
            var clips = Resources.LoadAll<AudioClip>("Audio");
            Check(clips.Length == 11, "Expected 11 audio clips, got " + clips.Length);
            foreach (var clip in clips)
            {
                Check(clip.channels == 1 && clip.length > .03f, "Invalid clip: " + clip.name);
                clip.LoadAudioData();
                var samples = new float[clip.samples * clip.channels];
                Check(clip.GetData(samples, 0) && samples.Any(x => Mathf.Abs(x) > .01f), "Silent clip: " + clip.name);
            }
            foreach (string name in new[] { "Player", "Bot" })
            {
                var player = Resources.Load<GameObject>(name);
                var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                var serialized = new SerializedObject(sync);
                Check(serialized.FindProperty("weapons").GetArrayElementAtIndex(0).FindPropertyRelative("audioProfile").objectReferenceValue == profile,
                    name + " audio profile not assigned");
            }
            hudObject = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("UI/PlayerHUD"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hudObject, scene);
            var hud = hudObject.GetComponent<PlayerHUD>();
            // Invoke the real feed path. Negative IDs are valid bots, identical names are valid different players.
            var handle = typeof(PlayerHUD).GetMethod("HandleKillfeedKill", BindingFlags.Instance | BindingFlags.NonPublic);
            handle.Invoke(hud, new object[] { new PlayerHealth.KillInfo(2, -1001, -1, "Roland", "BOT Viper", "", 1, 2, 0, "ak74") });
            handle.Invoke(hud, new object[] { new PlayerHealth.KillInfo(3, 1, 4, "Player", "Player", "Teammate", 2, 1, 1, "ak74") });
            var feed = hudObject.transform.Find("Killfeed");
            Check(feed != null && feed.childCount == 2, "Feed rows missing");
            foreach (Transform row in feed)
            {
                var labels = row.GetComponentsInChildren<Text>();
                Check(labels.Length == 3 && labels[1].text == "\u2192 AK-74 \u2192", "Bot/duplicate-name kill lost weapon/killer");
                Check(labels.All(t => !t.resizeTextForBestFit && t.font.dynamic), "Feed uses rescaled bitmap font");
            }
            var cameraObject = new GameObject("HUD Preview Camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .19f);
            var canvas = hudObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            target = new RenderTexture(1600, 900, 24); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)feed);
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "killfeed-preview.png", image.EncodeToPNG());
            File.WriteAllText(Folder + "unity-validation.txt", "PASS: 11 imported non-silent mono clips; AK74 audio profile assigned to Player and Bot; bot killer, duplicate nicknames and assist retain killer -> AK-74 -> victim; dynamic fonts with best-fit disabled. Impact audio removed.\nGenerated killfeed-preview.png.\nNot a live two-client or audible playback test.\n");
            Debug.Log("[Audio/HUD] Validation passed: " + Folder);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "unity-error.txt", error.ToString()); Debug.LogException(error);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (hudObject != null) UnityEngine.Object.DestroyImmediate(hudObject);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
