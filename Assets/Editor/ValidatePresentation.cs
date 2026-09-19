using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A request file is explicit opt-in for this check; no scene or asset is saved.
[InitializeOnLoad]
public static class ValidatePresentation
{
    static ValidatePresentation()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists("Temp/validate-presentation.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/validate-presentation.request");
            Run();
        };
    }

    [MenuItem("Tools/CounterMine/Validate UI and Vertical Map")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject arena = null, hudRoot = null, player = null;
        const string report = "Documentation/Map/unity-validation.txt";
        Directory.CreateDirectory("Documentation/Map");
        try
        {
            arena = PrefabUtility.LoadPrefabContents("Assets/maps/map1/VerticalArena/VerticalArena.prefab");
            Check(arena.GetComponentsInChildren<MeshRenderer>().All(r => r.sharedMaterial != null && r.sharedMaterial.shader != null), "Missing map material/shader");
            var colliders = arena.GetComponentsInChildren<BoxCollider>();
            Check(colliders.Length == 71, "Expected 71 map colliders");
            Physics.SyncTransforms();
            float previous = 0;
            for (int i = 0; i <= 790; i++)
            {
                float x = -39.5f + i * .1f;
                float height = 0;
                foreach (var collider in colliders)
                    if (collider.Raycast(new Ray(new Vector3(x, 20, 0), Vector3.down), out var hit, 21)) height = Mathf.Max(height, hit.point.y);
                Check(Mathf.Abs(height - previous) < .3f, "Unwalkable bridge seam at x=" + x);
                if (Mathf.Abs(x) < 13) Check(Mathf.Abs(height - 6f) < .04f, "Missing bridge deck at x=" + x);
                previous = height;
            }
            hudRoot = PrefabUtility.LoadPrefabContents("Assets/Resources/UI/PlayerHUD.prefab");
            var font = GameUIStyle.Font;
            Check(font != null, "Embedded UI font missing");
            Check(hudRoot.GetComponentsInChildren<Text>(true).All(t => t.font == font), "Mixed HUD fonts");
            var scaler = hudRoot.GetComponent<CanvasScaler>();
            Check(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && scaler.referenceResolution == new Vector2(1600,900), "HUD scaler mismatch");
            player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
            var hud = hudRoot.GetComponent<PlayerHUD>();
            var field = typeof(PlayerHUD).GetField("crosshairArms", BindingFlags.NonPublic | BindingFlags.Instance);
            var arms = (RectTransform[])field.GetValue(hud);
            hud.Bind(player.GetComponent<PlayerHealth>());
            var sizes = arms.Select(a => a.sizeDelta).ToArray();
            hud.Bind(player.GetComponent<PlayerHealth>());
            Check(arms.Select((a,i) => a.sizeDelta == sizes[i]).All(v => v), "Repeated binding scales crosshair");
            Check(hudRoot.GetComponents<MatchScoreUI>().Length == 1, "Duplicate scoreboard");
            CaptureMap();
            File.WriteAllText(report, "PASS: Unity imports arena meshes/materials and 71 colliders.\nPASS: 791 physics samples across both ramps/deck; seams below 0.3 m.\nPASS: full-scene route, both roof exits, player-width cargo aisles and 60 m flank sightlines.\nPASS: embedded HUD font, reference scaler, repeated Bind and single scoreboard.\nThis is an Editor validation, not a player build or a live bot match.\n");
        }
        catch (Exception e)
        {
            File.WriteAllText(report, "FAIL: " + e);
            Debug.LogException(e);
        }
        finally
        {
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
            if (hudRoot != null) PrefabUtility.UnloadPrefabContents(hudRoot);
            if (arena != null) PrefabUtility.UnloadPrefabContents(arena);
        }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    private static void CaptureMap()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        RenderTexture target = null;
        Texture2D image = null;
        Camera camera = null;
        var previous = RenderTexture.active;
        try
        {
            var go = new GameObject("Map validation camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            camera = go.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.14f,.19f,.23f);
            camera.farClipPlane = 250;
            camera.fieldOfView = 55;
            target = new RenderTexture(1400,850,24);
            camera.targetTexture = target;
            image = new Texture2D(1400,850,TextureFormat.RGB24,false);
            Physics.SyncTransforms();
            var colliders = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Collider>())
                .Where(c => c.enabled && !c.isTrigger && c.GetComponent<TeamSafeZone>() == null).ToArray();
            foreach (float z in new[] { -.3f, 0f, .3f })
            {
                float last = 0;
                for (int i = 0; i <= 790; i++)
                {
                    float x = -39.5f + i*.1f;
                    float y = 0;
                    foreach (var collider in colliders)
                        if (collider.Raycast(new Ray(new Vector3(x,20,z), Vector3.down),out var hit,21)) y = Mathf.Max(y,hit.point.y);
                    Check(Mathf.Abs(y-last)<.3f, "Scene obstacle on upper route at " + x + ", " + z);
                    last=y;
                }
            }
            // Long flanks remain unobstructed at standing eye height.
            foreach (float z in new[] { -11.8f, 11.8f })
                Check(!colliders.Any(c => c.Raycast(new Ray(new Vector3(-30,1.6f,z),Vector3.right),out _,60)), "Blocked long sightline at z="+z);
            foreach (int side in new[] { -1,1 })
            {
                // A player-width aisle through each cargo court stays open.
                foreach (float offset in new[] { -.25f,0f,.25f })
                foreach (float height in new[] { .35f,1.6f })
                    Check(!colliders.Any(c => c.Raycast(new Ray(new Vector3(side*13.9f+offset,height,-side*10),Vector3.forward*side),out _,10)), "Blocked cargo-court aisle");
                Check(colliders.Any(c => c.Raycast(new Ray(new Vector3(side*10,1.6f,-side*10),Vector3.forward*side),out _,5)), "Cargo court lacks tall cover");
                float lastRoof = 6;
                for (int j=0;j<=90;j++)
                {
                    float x=side*(4.5f-j*.05f);
                    float y=0;
                    foreach (var collider in colliders)
                        if (collider.Raycast(new Ray(new Vector3(x,20,side*5),Vector3.down),out var hit,21)) y=Mathf.Max(y,hit.point.y);
                    Check(Mathf.Abs(y-lastRoof)<.3f, "Broken roof exit");
                    lastRoof=y;
                }
                Check(Mathf.Abs(lastRoof-3.5f)<.04f, "Roof exit misses existing roof");
            }
            for (int i=0;i<3;i++)
            {
                camera.transform.position = i==0 ? new Vector3(44,34,-38) : i==1 ? new Vector3(-38,1.7f,0) : new Vector3(13.9f,1.7f,-11);
                camera.transform.LookAt(i==0 ? new Vector3(0,1.5f,0) : i==1 ? new Vector3(-8,6,0) : new Vector3(13.9f,1.7f,-2));
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,1400,850),0,0); image.Apply();
                File.WriteAllBytes("Documentation/Map/"+(i==0?"unity-overview.png":i==1?"unity-approach.png":"unity-cargo-court.png"), image.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
