using System;
using System.IO;
using System.Linq;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in play-mode check; leaves the authored scene unchanged.</summary>
[InitializeOnLoad]
public static class ValidateStartMenu
{
    private const string Request = "Temp/validate-start-menu.request";
    private const string UIRequest = "Temp/validate-menu-ui.request";
    private const string Key = "CounterMine.StartMenuValidation";
    private const string Folder = "Documentation/StartMenu";
    private static double next, deadline;
    private static int step;
    static ValidateStartMenu() => EditorApplication.update += Poll;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Poll()
    {
        if (EditorApplication.isCompiling) return;
        if ((File.Exists(Request) || File.Exists(UIRequest)) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            bool uiOnly = File.Exists(UIRequest);
            File.Delete(uiOnly ? UIRequest : Request);
            SessionState.SetBool(Key + ".UIOnly", uiOnly);
            SessionState.SetBool(Key, true);
            Directory.CreateDirectory(Folder);
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        try
        {
            var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyManager>();
            if (step == 0)
            {
                deadline = EditorApplication.timeSinceStartup + 45;
                step = 1; return;
            }
            Check(EditorApplication.timeSinceStartup < deadline, "Timed out at step " + step);
            if (step == 1)
            {
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                if (menu == null) return;
                Check(!PhotonNetwork.InRoom && !PhotonNetwork.IsConnected, "Menu connected before mode selection");
                Check(PlayerHealth.ActivePlayers.Count == 0, "Preview registered as a combat player");
                Check(menu.GetComponentsInChildren<Button>().Length == 3, "Expected two mode buttons and the shop");
                GameLocalization.SetLanguage("ru");
                Check(menu.GetComponentsInChildren<Text>().Any(t => t.text == "Одиночная игра"), "Russian solo label missing");
                Check(menu.GetComponentsInChildren<Text>().Any(t => t.text == "Мультиплеер"), "Russian multiplayer label missing");
                var actor = GameObject.Find("Display Player");
                Check(actor != null && actor.GetComponentsInChildren<Renderer>().Length > 0, "Display model missing");
                Check(actor.GetComponentsInChildren<MonoBehaviour>().All(b => b is WeaponHandIK && !b.enabled), "Display model has gameplay scripts");
                var displayIK = actor.GetComponentInChildren<WeaponHandIK>();
                var chest = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Chest");
                var samplePose = typeof(StartMenuScreen).GetMethod("SamplePose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                displayIK.ApplyBreathing(0, 1, true);
                float minChest = float.PositiveInfinity, maxChest = float.NegativeInfinity;
                for (int i = 0; i < 32; i++)
                {
                    samplePose.Invoke(menu, new object[] { i * 4.2f / 32 });
                    minChest = Mathf.Min(minChest, chest.position.y);
                    maxChest = Mathf.Max(maxChest, chest.position.y);
                }
                Check(maxChest - minChest > .005f && maxChest - minChest < .025f,
                    "Menu breathing missing or excessive: chest travel " + (maxChest - minChest));
                menu.ShowRoomBrowser(true);
                var browser = menu.GetComponentInChildren<RoomBrowserScreen>();
                Check(browser != null && browser.gameObject.activeSelf, "Room browser did not open");
                Check(browser.GetComponentsInChildren<InputField>().Length == 2, "Room search or name field missing");
                Check(browser.GetComponentsInChildren<Button>().Any(b => b.name == "БЫСТРЫЙ ВХОД"), "Quick play button missing");
                Check(browser.GetComponentsInChildren<Button>().Any(b => b.name == "ОБНОВИТЬ"), "Refresh button missing");
                Check(browser.GetComponentsInChildren<Button>().Any(b => b.name == "СОЗДАТЬ"), "Create room button missing");
                step = 10; return;
            }
            if (step == 10)
            {
                ScreenCapture.CaptureScreenshot(Folder + "/room-browser.png");
                step = 11; return;
            }
            if (step == 11)
            {
                Check(File.Exists(Folder + "/room-browser.png"), "Room browser screenshot missing");
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                menu.ShowRoomBrowser(false);
                menu.GetComponentsInChildren<Button>().Single(b => b.name == "МАГАЗИН").onClick.Invoke();
                var shop = menu.GetComponentInChildren<DeathShopUI>();
                Check(shop != null && shop.IsOpen, "Main-menu shop did not open");
                Check(PlayerHealth.ActivePlayers.Count == 0 && !PhotonNetwork.IsConnected, "Shop started combat or networking");
                shop.Close();
                var shader = Resources.Load<Shader>("UI/MenuBackgroundBlur");
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Blur shader failed");
                ScreenCapture.CaptureScreenshot(Folder + "/start-menu.png");
                step = 2; return;
            }
            if (step == 2)
            {
                Check(File.Exists(Folder + "/start-menu.png"), "Menu screenshot missing");
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                if (SessionState.GetBool(Key + ".UIOnly", false))
                {
                    menu.GetComponentsInChildren<Button>().Single(b => b.name == "МАГАЗИН").onClick.Invoke();
                    step = 20; return;
                }
                menu.GetComponentsInChildren<Button>().Single(b => b.name == "Single player").onClick.Invoke();
                step = 3; return;
            }
            if (step == 20)
            {
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                var shop = menu.GetComponentInChildren<DeathShopUI>();
                Check(shop != null && shop.IsOpen, "Shop did not remain open");
                Canvas.ForceUpdateCanvases();
                var items = shop.GetComponentsInChildren<LayoutElement>();
                Check(items.Length > 0 && items.All(item => ((RectTransform)item.transform).rect.width > 160), "Weapon list too narrow");
                ScreenCapture.CaptureScreenshot(Folder + "/shop-menu.png");
                step = 21; return;
            }
            if (step == 21)
            {
                var inspectedShop = UnityEngine.Object.FindFirstObjectByType<DeathShopUI>();
                string savedLoadout = JsonUtility.ToJson(YandexPlayerData.Current);
                var inspect = typeof(DeathShopUI).GetMethod("Inspect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (var item in ShopCatalog.Items)
                {
                    inspect.Invoke(inspectedShop, new object[] { item });
                    var model = inspectedShop.GetComponentInChildren<ShopWeaponPreview>();
                    if (item.id == "radar") { Check(!model.HasModel, "Radar should use its icon"); continue; }
                    Check(model.HasModel && model.Entry.id == item.id, "Missing preview for " + item.id);
                    model.SendMessage("LateUpdate");
                    Check(model.GetComponent<RawImage>().texture != null, "Missing preview texture for " + item.id);
                    var turntable = GameObject.Find("Shop preview stage").GetComponentsInChildren<Transform>().Single(t => t.name == "Weapon turntable");
                    Quaternion before = turntable.localRotation;
                    var previewCamera = GameObject.Find("Shop preview stage").GetComponentInChildren<Camera>();
                    float originalSize = previewCamera.orthographicSize;
                    Vector3 originalPosition = previewCamera.transform.localPosition;
                    model.OnDrag(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                        button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, delta = new Vector2(80,25) });
                    model.SendMessage("LateUpdate");
                    Check(Quaternion.Angle(before, turntable.localRotation) > 5, "Drag did not rotate " + item.id);
                    Check(Quaternion.Angle(turntable.localRotation, Quaternion.Euler(-4.5f,83f,0)) < .1f,
                        "Unexpected drag direction for " + item.id);
                    for (int rotation = 0; rotation < 12; rotation++)
                    {
                        model.OnDrag(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, delta = new Vector2(75,20) });
                        model.SendMessage("LateUpdate");
                        Check(Mathf.Approximately(originalSize, previewCamera.orthographicSize), "Rotation changed zoom for " + item.id);
                        Check(Vector3.Distance(originalPosition, previewCamera.transform.localPosition) < .0001f,
                            "Rotation moved the camera for " + item.id);
                    }
                    model.OnScroll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                        scrollDelta = Vector2.up });
                    model.SendMessage("LateUpdate");
                    Check(previewCamera.orthographicSize < originalSize, "Scroll did not zoom for " + item.id);
                    model.ResetView(); model.SendMessage("LateUpdate");
                    Check(Mathf.Approximately(originalSize, previewCamera.orthographicSize), "Reset did not restore zoom for " + item.id);
                }
                Check(savedLoadout == JsonUtility.ToJson(YandexPlayerData.Current), "Browsing changed wallet/loadout");
                Check(PlayerHealth.ActivePlayers.Count == 0 && !PhotonNetwork.IsConnected, "Preview initialized gameplay/networking");
                Check(File.Exists(Folder + "/shop-menu.png"), "Shop screenshot missing");
                Check(!PhotonNetwork.IsConnected && PlayerHealth.ActivePlayers.Count == 0, "UI validation started networking or combat");
                File.WriteAllText(Folder + "/ui-validation.txt", "PASS: localized menu, main-menu shop open/close, weapon list wider than 160 canvas units, all weapon previews, drag direction, fixed camera through rotation, scroll zoom and reset verified, browsing preserves wallet/loadout, display-only character, breathing chest travel between 5 and 25 mm across 32 samples of the actual menu pose pipeline, no combat/networking. Captured start-menu.png and shop-menu.png. No purchases and no player build.\n");
                Finish(); return;
            }
            if (step == 3)
            {
                Check(PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient, "Solo room not active");
                Check(UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>() == null, "Menu did not close");
                Check(GameObject.Find("Menu Presentation") == null, "Preview stage leaked into match");
                if (!lobby.CanDeploy || !PlayerHealth.ActivePlayers.Any(p => p != null && BotController.IsBot(p))) return;
                lobby.Deploy(); lobby.Deploy();
                step = 4; return;
            }
            if (step == 4)
            {
                var players = PlayerHealth.ActivePlayers.Where(p => p != null && !BotController.IsBot(p)).ToArray();
                Check(players.Length == 1, "Deploy created duplicate players");
                Check(UnityEngine.Object.FindFirstObjectByType<DeploymentScreen>() == null, "Deployment remained open");
                players[0].GetComponent<PlayerDeathController>().ApplyNetworkDeath(Vector3.zero, players[0].transform.position);
                step = 5; return;
            }
            if (step == 5)
            {
                Check(UnityEngine.Object.FindFirstObjectByType<DeploymentScreen>() != null, "Death did not reopen deployment");
                if (!lobby.CanDeploy) return;
                lobby.Deploy(); step = 6; return;
            }
            Check(PlayerHealth.ActivePlayers.Count(p => p != null && !BotController.IsBot(p) && !p.IsDead) == 1, "Solo respawn failed");
            File.WriteAllText(Folder + "/validation.txt", "PASS: disconnected startup, localized room browser with search, refresh, create and quick play controls, room-browser screenshot, mode/shop buttons, main-menu shop open/close, display-only rig with manual hand IK, blur shader, screenshot, offline room, bot spawning, duplicate-deploy guard, death screen and respawn.\nOnline matchmaking and WebGL need separate live checks.\n");
            Finish();
        }
        catch (Exception e)
        {
            File.WriteAllText(Folder + "/validation.txt", "FAIL at step " + step + "\n" + e);
            Debug.LogException(e); Finish();
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(Key, false); step = 0;
        EditorApplication.ExitPlaymode();
    }
}
