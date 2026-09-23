using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class InstallDeathShop
{
    static InstallDeathShop() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists("Temp/install-death-shop.request") || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete("Temp/install-death-shop.request");
        try { Run(); } catch (Exception e) { Directory.CreateDirectory("Documentation/Shop"); File.WriteAllText("Documentation/Shop/validation.txt", "FAIL " + e); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install and Validate Death Shop")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/Shop"); Directory.CreateDirectory("Assets/Resources/UI/Shop");
        AssetDatabase.Refresh();
        if (AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/ShopCatalog.asset") == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ShopCatalog>(), "Assets/Resources/ShopCatalog.asset");
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var catalog = new SerializedObject(sync).FindProperty("weapons");
            for (int i = 0; i < catalog.arraySize; i++)
            {
                var item = catalog.GetArrayElementAtIndex(i);
                string id = item.FindPropertyRelative("id").stringValue;
                var animator = (Animancer.AnimancerComponent)item.FindPropertyRelative("animator").objectReferenceValue;
                var clip = (AnimationClip)item.FindPropertyRelative("weaponIdle").objectReferenceValue;
                RenderIcon(animator.gameObject, clip, id);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        Validate();
    }
    private static void RenderIcon(GameObject original, AnimationClip clip, string id)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var copy = UnityEngine.Object.Instantiate(original); copy.SetActive(true);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, scene);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(copy, 0);
            var renderers = copy.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var go = new GameObject("Item camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = size * .42f;
            camera.transform.position = bounds.center + new Vector3(1.3f,.35f,-.65f).normalized * size * 3;
            camera.transform.LookAt(bounds.center); camera.nearClipPlane = .001f; camera.farClipPlane = size * 10;
            var light = new GameObject("Key light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, scene);
            light.AddComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 2.5f;
            light.transform.rotation = Quaternion.Euler(40,-40,0);
            string path = "Assets/Resources/UI/Shop/" + id + ".png";
            Render(camera, path, 640, 360, Color.clear);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Validate()
    {
        var report = new System.Text.StringBuilder();
        try
        {
            var profile = YandexPlayerData.Sanitize(JsonUtility.FromJson<YandexPlayerData>("{\"money\":600,\"ownedWeapons\":[\"ak74\"],\"equippedWeapon\":\"ak74\"}"));
            foreach (var candidate in new[] { profile, YandexPlayerData.CreateDefault() })
                foreach (string id in new[] { "ak74", "ucp", "radar", "milkor" })
                    Check(candidate.Owns(id) && candidate.IsEquipped(id), "Starter ownership/equipment: " + id);
            Check(profile.money == 600, "Migration changed wallet");
            profile.equippedSkills.Remove("radar");
            var migrated = YandexPlayerData.Sanitize(JsonUtility.FromJson<YandexPlayerData>(JsonUtility.ToJson(profile)));
            Check(!migrated.IsEquipped("radar") && migrated.Owns("radar"), "Migration repeated after removing a skill");
            report.AppendLine("PASS default and legacy starter loadout, unchanged wallet, one-time migration preserves later selections.");
            // Synthetic unowned inventory keeps purchase regression checks meaningful.
            profile.ownedWeapons = new System.Collections.Generic.List<string> { "ak74" };
            profile.equippedPistol = "";
            profile.equippedSkills.Clear();
            int pistolPrice = ShopCatalog.Find("ucp").price;
            profile.money = pistolPrice;
            Check(profile.TryPurchaseAndEquip("ucp", out _, false) && profile.money == 0 && profile.equippedPistol == "ucp", "Purchase/equip pistol");
            Check(profile.TryPurchaseAndEquip("ucp", out _, false) && profile.money == 0, "Double charge");
            Check(!profile.TryPurchaseAndEquip("milkor", out _, false) && !profile.Owns("milkor"), "Insufficient balance");
            Check(!profile.TryPurchaseAndEquip("invalid", out _, false), "Unknown item purchase");
            profile.money = ShopCatalog.Find("milkor").price + ShopCatalog.Find("radar").price;
            Check(profile.TryPurchaseAndEquip("milkor", out _, false) && profile.TryPurchaseAndEquip("radar", out _, false), "Skill purchase");
            Check(profile.money == 0 && profile.equippedSkills.Count == 2 && profile.equippedWeapon == "ak74", "Category separation");
            var saved = YandexPlayerData.Sanitize(JsonUtility.FromJson<YandexPlayerData>(JsonUtility.ToJson(profile)));
            Check(saved.Owns("milkor") && saved.equippedPistol == "ucp" && saved.equippedSkills.Count == 2, "Purchase persistence");
            Check(ShopCatalog.Find("milkor").killsRequired == 10 && ShopCatalog.Find("radar").killsRequired == 5, "Killstreak thresholds");
            report.AppendLine("PASS profile migration, permanent purchases, no duplicate charge, insufficient funds, invalid item, category selection and save/load round trip.");
            Preview();
            report.AppendLine("PASS shop UI and all three category previews; real wallet was not modified.");
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); Debug.LogException(e); }
        File.WriteAllText("Documentation/Shop/validation.txt", report.ToString());
    }
    private static void Preview()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var go = new GameObject("Shop preview camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.scene = scene;
            var root = new GameObject("Shop preview canvas", typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = .5f;
            var shop = DeathShopUI.Create(root.transform, null, null);
            var data = YandexPlayerData.CreateDefault(); data.money = 750;
            foreach (ShopCategory tab in Enum.GetValues(typeof(ShopCategory)))
            {
                shop.ShowPreview(data, tab); Canvas.ForceUpdateCanvases();
                Render(camera, "Documentation/Shop/" + tab + ".png", 1600,900, new Color(.015f,.025f,.035f));
                Check(shop.GetComponentsInChildren<Button>().Length >= 5, "Missing tabs or item button");
            }
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Render(Camera camera, string path, int width, int height, Color background)
    {
        var rt = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); var old = RenderTexture.active;
        var pixels = new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = background;
            camera.targetTexture = rt; camera.aspect = (float)width/height;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(pixels); }
    }
}
