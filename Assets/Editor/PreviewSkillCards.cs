using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreviewSkillCards
{
    [MenuItem("Tools/CounterMine/Preview Skill Cards")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.NewPreviewScene();
        var previous = RenderTexture.active;
        RenderTexture target = null; Texture2D image = null;
        try
        {
            var hud = Object.Instantiate(Resources.Load<GameObject>("UI/PlayerHUD"));
            SceneManager.MoveGameObjectToScene(hud, scene);
            // Preview uses explicit state without connecting to a room or modifying player progress.
            for (int i = 0; i < 3; i++)
            {
                var card = SkillSlotUI.Create(hud.GetComponent<PlayerHUD>().ContentRoot, i);
                card.SetState(i == 0, i == 0 ? 2 : 0, 0, false);
                typeof(SkillSlotUI).GetField("displayed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(card, i == 0 ? .4f : 0);
                typeof(SkillSlotUI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, null);
            }
            var go = new GameObject("UI preview camera"); SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .19f);
            var canvas = hud.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            target = new RenderTexture(1600, 900, 24); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            Directory.CreateDirectory("Documentation/Character");
            File.WriteAllBytes("Documentation/Character/skill_cards_preview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) Object.DestroyImmediate(target);
            if (image != null) Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
