using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ValidateReleaseUI
{
    static ValidateReleaseUI() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/validate-release-ui.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Run(); }
        catch (Exception e) { Directory.CreateDirectory("Documentation/ReleaseChecks"); File.WriteAllText("Documentation/ReleaseChecks/validation.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/CounterMine/Validate Release UI")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/ReleaseChecks");
        string previous = GameLocalization.Language;
        var events = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject lobbyObject = null;
        DeploymentScreen screen = null;
        try
        {
            Check(YG.YG2.infoYG.AutoTranslateLangs.languages.ru && YG.YG2.infoYG.AutoTranslateLangs.languages.en &&
                !YG.YG2.infoYG.AutoTranslateLangs.languages.tr, "YGames language configuration");
            Check(YG.LanguageLegacy.LanguageYG.ParseTranslationResponse != null &&
                YG.LanguageLegacy.LanguageYG.ParseTranslationResponse("[[[\"Hello\",\"Привет\",null,null],[\" world\",\" мир\",null,null]],null,\"ru\"]") == "Hello world",
                "YGames auto-translation JSON adapter");
            foreach(var target in new[] { UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.Build.NamedBuildTarget.WebGL })
                Check(PlayerSettings.GetScriptingDefineSymbols(target).Contains("Localization_yg") &&
                    PlayerSettings.GetScriptingDefineSymbols(target).Contains("NJSON_YG2"), "YGames modules must stay enabled for builds");
            Check(PlayerSettings.WebGL.template == "PROJECT:YandexGames", "WebGL must use YandexGames HTML");
            Check(File.ReadAllText("Assets/WebGLTemplates/YandexGames/index.html").Contains("/sdk.js"), "SDK script missing");
            Check(GameLocalization.Normalize("ru-RU") == "ru" && GameLocalization.Normalize("RU") == "ru" && GameLocalization.Normalize("tr") == "en", "Locale normalization");
            GameLocalization.SetLanguage("en");
            Check(GameLocalization.T("МАГАЗИН") == "SHOP", "English shop");
            Check(GameLocalization.Format("РАУНД {0}   ·   {1:00}:{2:00}", 2, 1, 5) == "ROUND 2   ·   01:05", "Round format");
            Check(GameLocalization.T("player-name-123") == "player-name-123", "Unknown text must remain unchanged");
            foreach (var item in ShopCatalog.Items)
                foreach (string value in new[] { item.title, item.description, item.detail })
                    Check(!GameLocalization.T(value).Any(c => c >= '\u0400' && c <= '\u04ff'), "Untranslated catalog text: " + value);

            lobbyObject = new GameObject("Release UI Test Lobby");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lobbyObject, scene);
            var lobby = lobbyObject.AddComponent<LobbyManager>();
            Check(!lobby.CanDeploy, "Cannot deploy before joining a room");
            screen = DeploymentScreen.Create(lobby);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(screen.gameObject, scene);
            Check(screen.GetComponentsInChildren<PlayerHealth>(true).Length == 0, "Preparation must not create a combat body");
            var shop = screen.GetComponentInChildren<DeathShopUI>(true);
            shop.Open(); Check(shop.IsOpen, "Pre-deployment shop must open without a dead player"); shop.Close();
            var forbidden = DeathShopUI.Create(screen.transform, null, null);
            forbidden.Open(); Check(!forbidden.IsOpen, "Ownerless shop must not bypass its access guard");
            UnityEngine.Object.DestroyImmediate(forbidden.gameObject);

            var canvas = screen.GetComponent<Canvas>();
            var cameraObject = new GameObject("Release UI Preview Camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.03f,.045f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            foreach (string language in new[] { "ru", "en" })
            {
                GameLocalization.SetLanguage(language);
                // Preview scenes do not run MonoBehaviour enable/update callbacks.
                foreach (var label in screen.GetComponentsInChildren<LocalizedGameText>(true))
                    typeof(LocalizedGameText).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(label, null);
                Check(screen.GetComponentsInChildren<Text>(true).Any(t => t.text == (language == "ru" ? "Подготовка к бою" : "Prepare for battle")), "Preparation title must refresh");
                Render(camera, "Documentation/ReleaseChecks/start-" + language + ".png");
                shop.ShowPreview(YandexPlayerData.CreateDefault(), ShopCategory.Primary);
                Check(shop.transform.Find("Shop Panel/Title").GetComponent<Text>().text == GameLocalization.T("МАГАЗИН / СНАРЯЖЕНИЕ"), "Shop labels must refresh while inactive");
                Render(camera, "Documentation/ReleaseChecks/shop-" + language + ".png");
                shop.Close();
            }
            File.WriteAllText("Documentation/ReleaseChecks/validation.txt", "PASS: Yandex HTML and SDK reference; ru/en/fallback; formatted time; all catalog text; explicit Edit Mode label refresh; deployment screen without player; guarded pre-deployment shop.\nPreview images: start-ru/en.png, shop-ru/en.png.\nBrowser SDK, actual WebGL build, and live network deployment require separate checks.\n");
        }
        finally
        {
            if (screen != null) UnityEngine.Object.DestroyImmediate(screen.gameObject);
            if (lobbyObject != null) UnityEngine.Object.DestroyImmediate(lobbyObject);
            foreach (var item in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                if (!events.Contains(item)) UnityEngine.Object.DestroyImmediate(item.gameObject);
            EditorSceneManager.ClosePreviewScene(scene);
            GameLocalization.SetLanguage(previous);
        }
    }

    private static void Render(Camera camera, string path)
    {
        var target = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.aspect = 1280f / 720;
            foreach (var group in camera.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CanvasGroup>(true)))
                group.alpha = 1;
            Canvas.ForceUpdateCanvases();
            foreach (var preview in camera.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopWeaponPreview>()))
                typeof(ShopWeaponPreview).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(preview, null);
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); }
    }
}
