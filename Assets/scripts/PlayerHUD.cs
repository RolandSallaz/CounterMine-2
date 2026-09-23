using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Owner-only HUD bound to live player state; visuals are authored in PlayerHUD.prefab.</summary>
public sealed class PlayerHUD : MonoBehaviour
{
    [SerializeField] private RectTransform safeArea;
    [SerializeField] private Text healthValue, playerName, teamLabel, weaponName, weaponState, connection, actionLabel, deathLabel;
    [SerializeField] private Text ammoLabel, grenadeLabel;
    [SerializeField] private Image healthFill, accent, actionFill;
    [SerializeField] private Image staminaFill;
    [SerializeField] private Text staminaLabel;
    [SerializeField] private CanvasGroup crosshair, actionGroup, damageOverlay;
    [SerializeField] private RectTransform[] crosshairArms;
    [SerializeField] private Color teamOne = new Color(.38f,.76f,.95f), teamTwo = new Color(1f,.7f,.32f), danger = new Color(1f,.3f,.25f);
    [SerializeField, Min(1f)] private float crosshairScale = 3f;
    [SerializeField, Min(.1f)] private float crosshairThicknessScale = 1f;
    [Header("Killfeed (top-right, built in code)")]
    [SerializeField, Min(1)] private int killfeedMaxEntries = 5;
    [SerializeField, Min(1f)] private float killfeedLifetime = 6f;
    [SerializeField, Min(0f)] private float killfeedFade = .8f;
    [SerializeField, Min(8)] private int killfeedFontSize = 20;
    [Header("Respawn")]
    [SerializeField, Min(0f)] private float respawnDelay = 0f;
    [SerializeField] private KeyCode respawnHoldKey = KeyCode.F;
    [SerializeField, Min(0.2f)] private float respawnHoldDuration = 1f;
    [SerializeField] private Color respawnFillColor = new Color(.95f, .72f, .2f, .9f);
    private float deathAt = -1f;
    private bool respawning;
    private float respawnRequestedAt;
    private float respawnHoldProgress;
    private GameObject respawnPanel;
    private DeathShopUI deathShop;
    private Button respawnButton;
    private Text respawnLabel;
    private Image respawnFill;
    private RectTransform killfeedRoot;
    private Font killfeedFont;
    [SerializeField] private HitDirectionIndicator hitDirection;
    [SerializeField] private ThreatArrow threatArrow;
    private readonly System.Collections.Generic.List<GrenadeProjectile> threatScratch = new System.Collections.Generic.List<GrenadeProjectile>();
    private int milkorProgress = -1, milkorCharges = -1, milkorRemaining = -1;
    private bool milkorEquipped;
    private int previousSkillLayout = -1;
    private readonly SkillSlotUI[] skillCards = new SkillSlotUI[RadarSkill.SlotCount];
    private int radarProgress = -1, radarCharges = -1;
    private bool radarWasActive;
    private readonly System.Collections.Generic.List<GameObject> killfeedEntries = new System.Collections.Generic.List<GameObject>();
    private PlayerHealth health;
    private WeaponIdleSynchronizer animationSource;
    private WeaponAimController aim;
    private WeaponRecoilController recoil;
    private PlayerController movement;
    private PlayerRagdollController ragdoll;
    private WeaponAmmo ammo;
    private GrenadeThrower grenades;
    private float refreshAt;
    private HealthScreenEffect healthScreenEffect;
    private Rect previousSafeArea;
    private Camera playerCam;
    private Canvas hudCanvas;
    private Vector2[] baseArmSizes;
    private float lastStaminaValue = -1f;
    private static readonly Vector2[] Directions = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
    public Transform ContentRoot => safeArea != null ? safeArea : transform;

    private void Awake()
    {
        // Prefab root is stored with scale 0 — unhide immediately so the HUD
        // (and respawn button) works even before Bind() in all builds.
        if (transform.localScale.x == 0f) transform.localScale = Vector3.one;
        GameLocalization.BindHUD(transform);
        hudCanvas = GetComponent<Canvas>();
        UpdateSafeArea();
    }

    private void UpdateSafeArea()
    {
        if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
        previousSafeArea = Screen.safeArea;
        safeArea.anchorMin = new Vector2(previousSafeArea.xMin / Screen.width, previousSafeArea.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(previousSafeArea.xMax / Screen.width, previousSafeArea.yMax / Screen.height);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
    }
    public void Bind(PlayerHealth player)
    {
        if (health != null) health.Damaged -= ShowHitDirection;
        respawning = false;
        respawnHoldProgress = 0f;
        // FIX (build): HUD prefab root is stored with scale 0 — force scale 1 or the whole
        // canvas (including the respawn button) stays invisible / unclickable.
        transform.localScale = Vector3.one;
        EnsureEventSystem();
        deathShop?.Bind(player);
        health=player;animationSource=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
        if (isActiveAndEnabled) health.Damaged += ShowHitDirection;
        aim=player.GetComponentInChildren<WeaponAimController>(true);recoil=player.GetComponentInChildren<WeaponRecoilController>(true);
        movement=player.GetComponent<PlayerController>();ragdoll=player.GetComponent<PlayerRagdollController>();
        ammo=player.GetComponent<WeaponAmmo>();
        grenades=player.GetComponent<GrenadeThrower>();
        playerCam=player.GetComponentInChildren<Camera>(true);hudCanvas=GetComponent<Canvas>();
        if (GetComponent<MatchScoreUI>() == null) gameObject.AddComponent<MatchScoreUI>();
        if (healthScreenEffect == null && hudCanvas != null) healthScreenEffect = HealthScreenEffect.Create(hudCanvas.transform);
        if (healthScreenEffect != null) healthScreenEffect.Clear();
        if (damageOverlay != null) damageOverlay.gameObject.SetActive(false);
        if (hitDirection == null && hudCanvas != null) hitDirection = HitDirectionIndicator.Create(hudCanvas.transform);
        if (threatArrow == null && hudCanvas != null) threatArrow = ThreatArrow.Create(hudCanvas.transform, new Color(1f, .6f, .1f));
        if(crosshairArms!=null&&crosshairArms.Length>0)
        {
            if (baseArmSizes == null) baseArmSizes=new Vector2[crosshairArms.Length];
            for(int i=0;i<crosshairArms.Length;i++)
            {
                if(crosshairArms[i]==null)continue;
                // Arms come from the prefab at 1x size; scale once here so no prefab rebuild is needed.
                // Length grows with crosshairScale, thickness with crosshairThicknessScale.
                if(baseArmSizes[i]==Vector2.zero)baseArmSizes[i]=crosshairArms[i].sizeDelta;
                Vector2 b=baseArmSizes[i];
                crosshairArms[i].sizeDelta=b.x>=b.y
                    ? new Vector2(b.x*crosshairScale,b.y*crosshairThicknessScale)
                    : new Vector2(b.x*crosshairThicknessScale,b.y*crosshairScale);
            }
        }
        Refresh();
    }

    private void OnEnable()
    {
        PlayerHealth.OnKilled += HandleKillfeedKill;
        if (health != null) health.Damaged += ShowHitDirection;
    }
    private void OnDisable()
    {
        PlayerHealth.OnKilled -= HandleKillfeedKill;
        if (health != null) health.Damaged -= ShowHitDirection;
        if (hitDirection != null) hitDirection.Clear();
        if (threatArrow != null) threatArrow.Hide();
        if (healthScreenEffect != null) healthScreenEffect.Clear();
        foreach (var entry in killfeedEntries) if (entry != null) Destroy(entry);
        killfeedEntries.Clear();
    }

    private void HandleKillfeedKill(PlayerHealth.KillInfo info)
    {
        if (killfeedRoot == null && !EnsureKillfeed()) return;
        bool suicide = string.IsNullOrEmpty(info.killerName) || info.killerActorNr == -1 || info.killerActorNr == 0 ||
                       info.killerActorNr == info.victimActorNr;
        string killerText = !suicide && !string.IsNullOrEmpty(info.assisterName)
            ? info.killerName + " + " + info.assisterName : info.killerName;
        AddKillfeedEntry(killerText, info.victimName, KillfeedTeamColor(info.killerTeam), KillfeedTeamColor(info.victimTeam), suicide, GameAudio.WeaponName(info.weaponId));
    }

    /// <summary>Hit direction wheel: a red arrow around the crosshair points at the attacker. Purely local.</summary>
    private void ShowHitDirection(PlayerHealth.DamageInfo info)
    {
        if (health == null || info.victim == null || info.victim != health) return;
        if (PhotonNetwork.InRoom && !health.photonView.IsMine) return;
        if (healthScreenEffect != null) healthScreenEffect.Wound((float)info.amount / Mathf.Max(1, health.MaximumHealth));
        if (hitDirection == null) return;
        if (!TryAttackerPosition(info, out Vector3 attackerPos)) return;
        hitDirection.Show(attackerPos, health.transform, playerCam != null ? playerCam.transform : health.transform);
    }

    /// <summary>Screen angle to a world point. 0 = ahead, positive = right.</summary>
    private bool TryScreenAngle(Vector3 worldPos, out float angle)
    {
        angle = 0f;
        if (health == null) return false;
        Vector3 toTarget = worldPos - health.transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < .01f) return false;
        Transform view = playerCam != null ? playerCam.transform : health.transform;
        Vector3 forward = Vector3.Cross(view.right, Vector3.up); forward.y = 0f;
        if (forward.sqrMagnitude < .000001f) { forward = health.transform.forward; forward.y = 0f; }
        if (forward.sqrMagnitude < .000001f) return false;
        angle = Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up);
        return true;
    }

    /// <summary>Orange arrow tracking the nearest live grenade. Purely local.</summary>
    private void UpdateThreatArrow()
    {
        if (threatArrow == null || health == null) return;
        if (health.IsDead) { threatArrow.Hide(); return; }
        const float maxRange = 25f;
        threatScratch.Clear();
        threatScratch.AddRange(GrenadeProjectile.Active);
        GrenadeProjectile nearest = null;
        float best = maxRange * maxRange;
        Vector3 self = health.transform.position;
        foreach (var grenade in threatScratch)
        {
            if (grenade == null) continue;
            float distance = (grenade.Position - self).sqrMagnitude;
            if (distance < best) { best = distance; nearest = grenade; }
        }
        if (nearest == null) { threatArrow.Hide(); return; }
        if (TryScreenAngle(nearest.Position, out float angle)) threatArrow.PointAt(angle);
        else threatArrow.Hide();
    }

    private bool TryAttackerPosition(PlayerHealth.DamageInfo info, out Vector3 attackerPos)
    {
        attackerPos = Vector3.zero;
        // The impulse describes the actual incoming hit, including a grenade blast.
        Vector3 incoming = -info.force; incoming.y = 0f;
        if (incoming.sqrMagnitude > .000001f)
        {
            attackerPos = health.transform.position + incoming.normalized * 10f;
            return true;
        }
        if (info.killerBotViewId > 0)
        {
            var botView = PhotonView.Find(info.killerBotViewId);
            if (botView != null) { attackerPos = botView.transform.position; return true; }
        }
        if (info.killerActorNr > 0)
        {
            foreach (var candidate in PlayerHealth.ActivePlayers)
            {
                if (candidate == null || candidate == health) continue;
                var view = candidate.photonView;
                if (view == null || view.OwnerActorNr != info.killerActorNr || BotController.IsBot(candidate)) continue;
                attackerPos = candidate.transform.position;
                return true;
            }
        }
        return false;
    }

    private bool EnsureKillfeed()
    {
        if (killfeedRoot != null) return true;
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas == null) return false;
        killfeedFont = GameUIStyle.Font;
        var rootGo = new GameObject("Killfeed");
        rootGo.transform.SetParent(ContentRoot, false);
        killfeedRoot = rootGo.AddComponent<RectTransform>();
        killfeedRoot.anchorMin = new Vector2(1f, 1f);
        killfeedRoot.anchorMax = new Vector2(1f, 1f);
        killfeedRoot.pivot = new Vector2(1f, 1f);
        killfeedRoot.anchoredPosition = new Vector2(-16f, -16f);
        killfeedRoot.sizeDelta = new Vector2(660f, 0f);
        var layout = rootGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = rootGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return true;
    }

    private Color KillfeedTeamColor(int team) => team == 1 ? teamOne : team == 2 ? teamTwo : Color.white;

    private void AddKillfeedEntry(string killerName, string victimName, Color killerColor, Color victimColor, bool suicide, string weapon)
    {
        var entry = new GameObject("Kill Entry");
        entry.transform.SetParent(killfeedRoot, false);
        entry.transform.SetAsLastSibling();
        var bg = entry.AddComponent<Image>();
        bg.color = new Color(.025f, .035f, .045f, .9f);
        bg.raycastTarget = false;
        var group = entry.AddComponent<HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleRight;
        group.spacing = 8f;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        group.padding = new RectOffset(12, 12, 7, 7);
        var layoutElement = entry.AddComponent<LayoutElement>();
        layoutElement.minHeight = killfeedFontSize + 16f;
        if (suicide)
        {
            AddKillfeedLabel(entry.transform, victimName, victimColor, TextAnchor.MiddleRight);
            AddKillfeedLabel(entry.transform, GameLocalization.T("died"), new Color(1f, 1f, 1f, .65f), TextAnchor.MiddleRight);
        }
        else
        {
            AddKillfeedLabel(entry.transform, killerName, killerColor, TextAnchor.MiddleRight);
            AddKillfeedLabel(entry.transform, "> " + weapon + " >", Color.white, TextAnchor.MiddleCenter);
            AddKillfeedLabel(entry.transform, victimName, victimColor, TextAnchor.MiddleRight);
        }
        killfeedEntries.Add(entry);
        while (killfeedEntries.Count > Mathf.Max(1, killfeedMaxEntries))
        {
            var oldest = killfeedEntries[0];
            killfeedEntries.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }
        StartCoroutine(ExpireKillfeedEntry(entry, killfeedLifetime));
    }

    private void AddKillfeedLabel(Transform parent, string text, Color color, TextAnchor alignment)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<Text>();
        label.font = killfeedFont;
        label.fontSize = killfeedFontSize;
        label.fontStyle = FontStyle.Normal;
        label.resizeTextForBestFit = false;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.supportRichText = false;
        label.color = color;
        label.text = string.IsNullOrEmpty(text) ? "?" : text;
    }

    private System.Collections.IEnumerator ExpireKillfeedEntry(GameObject entry, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (entry == null) yield break;
        var group = entry.GetComponent<CanvasGroup>();
        if (group == null) group = entry.AddComponent<CanvasGroup>();
        if (killfeedFade > 0f)
        {
            float t = 0f;
            while (entry != null && t < killfeedFade)
            {
                t += Time.deltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / killfeedFade);
                yield return null;
            }
        }
        killfeedEntries.Remove(entry);
        if (entry != null) Destroy(entry);
    }
    private void Update()
    {
        if(health==null){if(healthScreenEffect!=null)healthScreenEffect.Clear();return;}
        if(PhotonNetwork.InRoom&&!health.photonView.IsMine){gameObject.SetActive(false);return;}
        if (health.IsDead)
        {
            var lobby = Object.FindFirstObjectByType<LobbyManager>();
            if (lobby != null)
            {
                healthScreenEffect?.Clear();
                deathShop?.Close();
                lobby.ShowAfterDeath(health, respawnDelay);
                gameObject.SetActive(false);
                return;
            }
        }
        if (healthScreenEffect != null) healthScreenEffect.Tick((float)health.CurrentHealth / Mathf.Max(1, health.MaximumHealth), health.IsDead, Time.unscaledDeltaTime);
        if(Time.unscaledTime>=refreshAt){refreshAt=Time.unscaledTime+.1f;Refresh();}
        RefreshStamina();
        RefreshRadar();
        bool dead=health.IsDead;bool action=animationSource!=null&&animationSource.IsPlayingAction;
        bool canAim=!dead&&(ragdoll==null||!ragdoll.IsRagdoll)&&animationSource!=null&&animationSource.CanFire&&(movement==null||!movement.IsSprinting);
        crosshair.alpha=canAim?1f-(aim!=null?aim.AimAmount:0):0;
        if (crosshair.alpha > .001f)
        {
            float gap=CrosshairHalfGap(recoil!=null?recoil.CurrentSpreadDegrees:0f);
            for(int i=0;i<crosshairArms.Length;i++)crosshairArms[i].anchoredPosition=Directions[i]*gap;
        }
        actionGroup.alpha=action&&!dead?1:0;
        actionFill.rectTransform.anchorMax=new Vector2(animationSource!=null?animationSource.ActionProgress:0,1);
        if (dead && hitDirection != null) hitDirection.Clear();
        UpdateThreatArrow();
        UpdateRespawn(dead);
        if(Screen.safeArea!=previousSafeArea) UpdateSafeArea();
    }
    private void RefreshRadar()
    {
        var radar = RadarSkill.Instance;
        if (radar == null) return;
        bool created = skillCards[0] == null;
        if (created)
            for (int i = 0; i < skillCards.Length; i++)
                skillCards[i] = SkillSlotUI.Create(safeArea != null ? safeArea : transform, i);
        var mgl = health.GetComponent<MilkorSkill>();
        var reward = MilkorRewards.Read(health.photonView.OwnerActorNr);
        int remaining = mgl != null ? mgl.Remaining : 0;
        bool equipped = mgl != null && mgl.Equipped;
        int layout = (int)radar.SkillAt(0) + (int)radar.SkillAt(1) * 4 + (int)radar.SkillAt(2) * 16;
        if (!created && layout == previousSkillLayout && radarProgress == radar.Progress && radarCharges == radar.Charges && radarWasActive == radar.Active &&
            milkorProgress == reward.Kills && milkorCharges == reward.Charges && milkorRemaining == remaining && milkorEquipped == equipped) return;
        previousSkillLayout = layout;
        milkorProgress = reward.Kills; milkorCharges = reward.Charges; milkorRemaining = remaining; milkorEquipped = equipped;
        radarProgress = radar.Progress; radarCharges = radar.Charges; radarWasActive = radar.Active;
        for (int i = 0; i < skillCards.Length; i++)
        {
            var kind = radar.SkillAt(i);
            bool launcher = kind == RadarSkill.SkillKind.Milkor;
            skillCards[i].SetState(kind != RadarSkill.SkillKind.None,
                launcher ? reward.Kills : radar.Progress, launcher ? reward.Charges : radar.Charges,
                launcher ? equipped : kind == RadarSkill.SkillKind.Radar && radar.Active,
                launcher ? MilkorRewards.RequiredKills : RadarSkill.RequiredKills,
                launcher ? "MILKOR MGL" : "RADAR", i + 3, launcher ? remaining : -1);
        }
    }
    private void UpdateRespawn(bool dead)
    {
        if (!dead)
        {
            deathAt = -1f;
            respawning = false;
            respawnHoldProgress = 0f;
            if (respawnFill != null) respawnFill.fillAmount = 0f;
            if (respawnPanel != null) respawnPanel.SetActive(false);
            deathShop?.Close();
            return;
        }
        if (deathAt < 0f)
        {
            deathAt = Time.unscaledTime;
            respawning = false;
            respawnHoldProgress = 0f;
            EnsureRespawnPanel();
            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            catch { }
        }
        if (respawnPanel != null)
        {
            if (!respawnPanel.activeSelf) respawnPanel.SetActive(true);
            respawnPanel.transform.SetAsLastSibling();
        }
        // Parent CanvasGroup (Safe Area) is non-interactable — our panel lives outside it,
        // but keep the button itself forced interactable.
        if (respawnButton != null && !respawning) respawnButton.interactable = true;
        // If a previous click didn't produce a new life within a few seconds, allow retrying.
        if (respawning && Time.unscaledTime - respawnRequestedAt > 5f) respawning = false;

        if (deathShop != null && deathShop.IsOpen)
        {
            deathShop.transform.SetAsLastSibling(); respawnHoldProgress = 0;
            if (respawnFill != null) respawnFill.fillAmount = 0;
            return;
        }
        // Gate by respawnDelay (min time since death before input counts).
        float sinceDeath = Time.unscaledTime - deathAt;
        bool gated = sinceDeath < respawnDelay;

        if (!respawning && !gated)
        {
            // Quick press: Space (legacy behavior).
            if (WasRespawnQuickPressed())
            {
                OnRespawnClicked();
            }
            else
            {
                // Hold F (or hold key): fills the button, then respawns.
                if (IsRespawnHoldHeld())
                {
                    respawnHoldProgress += Time.unscaledDeltaTime / Mathf.Max(.01f, respawnHoldDuration);
                    if (respawnHoldProgress >= 1f)
                    {
                        respawnHoldProgress = 1f;
                        if (respawnFill != null) respawnFill.fillAmount = 1f;
                        OnRespawnClicked();
                    }
                }
                else if (respawnHoldProgress > 0f)
                {
                    respawnHoldProgress = Mathf.Max(0f, respawnHoldProgress - Time.unscaledDeltaTime * 3f);
                }
            }
        }
        else if (gated)
        {
            respawnHoldProgress = 0f;
        }

        if (respawnFill != null) respawnFill.fillAmount = Mathf.Clamp01(respawnHoldProgress);
        if (respawnLabel != null)
        {
            if (respawning) respawnLabel.text = GameLocalization.T("SPAWNING...");
            else if (gated) respawnLabel.text = GameLocalization.Format("ГОТОВНОСТЬ ЧЕРЕЗ {0}...", Mathf.CeilToInt(respawnDelay - sinceDeath));
            else if (respawnHoldProgress > 0f) respawnLabel.text = GameLocalization.Format("УДЕРЖИВАЙТЕ [{0}] {1}%", respawnHoldKey, Mathf.RoundToInt(respawnHoldProgress * 100f));
            else respawnLabel.text = GameLocalization.Format("ВОЗРОДИТЬСЯ [SPACE] / УДЕРЖИВАЙТЕ [{0}]", respawnHoldKey);
        }
    }

    private bool IsRespawnHoldHeld()
    {
        // New Input System path (project uses activeInputHandler = Input System).
        try
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                // Generic KeyCode -> Input System Key mapping (F, G, R, Space, etc.).
                if (System.Enum.TryParse<Key>(respawnHoldKey.ToString(), out var inputKey))
                {
                    try { if (kb[inputKey].isPressed) return true; } catch { }
                }
                else if (respawnHoldKey == KeyCode.F && kb.fKey.isPressed) return true;
            }
        }
        catch { }
        // Legacy Input path — harmless if the old system is disabled (returns false).
        try { if (Input.GetKey(respawnHoldKey)) return true; } catch { }
        return false;
    }

    private bool WasRespawnQuickPressed()
    {
        try
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
        }
        catch { }
        try { if (Input.GetKeyDown(KeyCode.Space)) return true; } catch { }
        return false;
    }

    private void EnsureEventSystem()
    {
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas != null)
        {
            var raycaster = hudCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = hudCanvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
        }
        // FIX (build): the scene may already contain an EventSystem with only the OLD
        // StandaloneInputModule (or none). A Button needs an active EventSystem +
        // InputSystemUIInputModule to receive clicks in the Input-System-only project.
        var es = Object.FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            es = new GameObject("EventSystem").AddComponent<EventSystem>();
            es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
        else if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
        if (!es.gameObject.activeSelf) es.gameObject.SetActive(true);
        if (!es.enabled) es.enabled = true;
    }

    private void EnsureRespawnPanel()
    {
        if (respawnPanel != null) return;
        EnsureEventSystem();
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas == null) return;
        Font font = GameUIStyle.Font;

        // FIX (build): parent to the Canvas itself, NOT to ContentRoot/SafeArea.
        // SafeArea has a CanvasGroup with interactable=false which silently disables
        // every child Button (Button.IsInteractable checks parent groups).
        respawnPanel = new GameObject("Respawn Panel");
        respawnPanel.transform.SetParent(hudCanvas.transform, false);
        var panelGroup = respawnPanel.AddComponent<CanvasGroup>();
        panelGroup.interactable = true;
        panelGroup.blocksRaycasts = true;
        panelGroup.ignoreParentGroups = true;
        var panelRect = respawnPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(.5f, .5f);
        panelRect.anchorMax = new Vector2(.5f, .5f);
        panelRect.pivot = new Vector2(.5f, .5f);
        panelRect.anchoredPosition = new Vector2(0f, -120f);
        panelRect.sizeDelta = new Vector2(420f, 184f);
        var bg = respawnPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, .6f);
        bg.raycastTarget = false;
        var layout = respawnPanel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 10f;
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var buttonGo = new GameObject("Respawn Button");
        buttonGo.transform.SetParent(respawnPanel.transform, false);
        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = new Color(.16f, .32f, .42f, .95f);
        respawnButton = buttonGo.AddComponent<Button>();
        respawnButton.interactable = true;
        // Avoid Space triggering the focused button AND our Space handler (double respawn).
        respawnButton.navigation = new Navigation { mode = Navigation.Mode.None };
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(260f, 56f);
        var layoutElement = buttonGo.AddComponent<LayoutElement>();
        layoutElement.minHeight = 56f;
        layoutElement.preferredHeight = 56f;

        // Hold-progress fill: horizontal bar behind the label, driven by F-hold (0..1).
        var fillGo = new GameObject("Respawn Fill");
        fillGo.transform.SetParent(buttonGo.transform, false);
        var fillRect = fillGo.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, .5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        respawnFill = fillGo.AddComponent<Image>();
        respawnFill.color = respawnFillColor;
        respawnFill.raycastTarget = false;
        respawnFill.type = Image.Type.Filled;
        respawnFill.fillMethod = Image.FillMethod.Horizontal;
        respawnFill.fillOrigin = 0;
        respawnFill.fillAmount = 0f;
        // Keep fill behind the label.
        fillGo.transform.SetAsFirstSibling();

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, false);
        var labelRect = labelGo.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(.5f, .5f);
        labelRect.sizeDelta = Vector2.zero;
        respawnLabel = labelGo.AddComponent<Text>();
        respawnLabel.font = font;
        respawnLabel.fontSize = 22;
        respawnLabel.fontStyle = FontStyle.Bold;
        respawnLabel.alignment = TextAnchor.MiddleCenter;
        respawnLabel.raycastTarget = false;
        respawnLabel.color = Color.white;
        respawnLabel.text = GameLocalization.T("RESPAWN");
        respawnButton.onClick.AddListener(OnRespawnClicked);
        deathShop = DeathShopUI.Create(hudCanvas.transform, health, OnRespawnClicked);
        var shopGo = new GameObject("Shop Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        shopGo.transform.SetParent(respawnPanel.transform, false); shopGo.transform.SetAsFirstSibling();
        shopGo.GetComponent<Image>().color = new Color(.12f,.25f,.3f,.98f);
        shopGo.GetComponent<LayoutElement>().preferredHeight = 56;
        var shopButton = shopGo.GetComponent<Button>(); shopButton.navigation = new Navigation { mode = Navigation.Mode.None };
        shopButton.onClick.AddListener(() => { if (!respawning) deathShop.Open(); });
        var shopLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text)); shopLabelGo.transform.SetParent(shopGo.transform, false);
        var shopText = shopLabelGo.GetComponent<Text>(); shopText.font = font; shopText.fontSize = 20;
        GameLocalization.Bind(shopText, "МАГАЗИН / СНАРЯЖЕНИЕ"); shopText.alignment = TextAnchor.MiddleCenter; shopText.raycastTarget = false;
        shopText.rectTransform.anchorMin = Vector2.zero; shopText.rectTransform.anchorMax = Vector2.one;
        shopText.rectTransform.offsetMin = shopText.rectTransform.offsetMax = Vector2.zero;
    }

    private void OnRespawnClicked()
    {
        // Guard against double clicks: each extra call would spawn an additional player.
        if (respawning) return;
        if (health == null || !health.IsDead) return;
        deathShop?.Close();
        respawning = true;
        respawnRequestedAt = Time.unscaledTime;
        if (respawnButton != null) respawnButton.interactable = false;
        if (respawnLabel != null) respawnLabel.text = GameLocalization.T("SPAWNING...");
        try { GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true); } catch { }
        // Inside the click handler, so pointer-lock requests stay browser-legal (WebGL).
        // Must not throw — otherwise the spawn below never runs (classic build-only bug).
        try
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        catch { }
        try
        {
            var lobby = Object.FindFirstObjectByType<LobbyManager>();
            if (lobby != null && health != null) lobby.RespawnPlayer(health.gameObject);
            else Debug.LogWarning("[PlayerHUD] Respawn unavailable: lobby or player missing.", this);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            // If the spawn failed, allow another attempt instead of staying stuck.
            if (health != null && health.IsDead)
            {
                respawning = false;
                respawnHoldProgress = 0f;
                if (respawnFill != null) respawnFill.fillAmount = 0f;
                if (respawnButton != null) respawnButton.interactable = true;
            }
        }
    }

    /// <summary>Crosshair half-gap in canvas units matching the spread cone angle on screen. Falls back to the old heat-based gap if no camera is cached.</summary>
    private float CrosshairHalfGap(float spreadDegrees)
    {
        float floor=6f*crosshairScale;
        if(playerCam==null||hudCanvas==null||Screen.height<=0)
            return floor+(recoil!=null?Mathf.Clamp01(recoil.Heat)*10f*crosshairScale:0f);
        float fov=Mathf.Clamp(playerCam.fieldOfView,1f,179f);
        float pxPerTan=(Screen.height*.5f)/Mathf.Tan(fov*.5f*Mathf.Deg2Rad);
        float halfGap=Mathf.Tan(Mathf.Max(0f,spreadDegrees)*Mathf.Deg2Rad)*pxPerTan/Mathf.Max(.01f,hudCanvas.scaleFactor);
        return Mathf.Max(floor,halfGap);
    }
    private void Refresh()
    {
        if(health==null)return;
        int team=health.photonView.Owner?.CustomProperties["team"] is int t?t:1;
        Color color=team==2?teamTwo:teamOne;accent.color=color;
        teamLabel.text=team==2?GameLocalization.T("BRAVO / 02"):GameLocalization.T("ALPHA / 01");teamLabel.color=color;
        playerName.text=string.IsNullOrWhiteSpace(health.photonView.Owner?.NickName)?GameLocalization.T("OPERATOR"):health.photonView.Owner.NickName.ToUpperInvariant();
        healthValue.text=health.CurrentHealth.ToString("000");healthValue.color=health.CurrentHealth<=25?danger:Color.white;
        healthFill.color=health.CurrentHealth<=25?danger:color;healthFill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01((float)health.CurrentHealth/health.MaximumHealth),1);
        var id=animationSource!=null?animationSource.WeaponId:null;weaponName.text=string.IsNullOrEmpty(id)?GameLocalization.T("UNARMED"):GameAudio.WeaponName(id);
        if(ammoLabel!=null)
        {
            if(ammo==null){ammoLabel.text="--";ammoLabel.color=Color.white;}
            else{ammoLabel.text=ammo.MagAmmo+(ammo.FiniteReserve ? " / 6" : GameLocalization.T(" / INF"));ammoLabel.color=ammo.MagAmmo<=0?danger:Color.white;}
        }
        if(grenadeLabel!=null)
        {
            if(grenades==null){grenadeLabel.text="G --";grenadeLabel.color=Color.white;}
            else{grenadeLabel.text="G x"+grenades.Grenades;grenadeLabel.color=grenades.Grenades<=0?danger:Color.white;}
        }
        bool dead=health.IsDead;bool rag=ragdoll!=null&&ragdoll.IsRagdoll;bool action=animationSource!=null&&animationSource.IsPlayingAction;
        weaponState.text=dead?GameLocalization.T("OFFLINE"):TeamSafeZone.BlocksWeapons(health)?GameLocalization.T("SAFE ZONE"):rag?GameLocalization.T("RAGDOLL"):action?GameLocalization.T("BUSY"):GameLocalization.T("READY");
        actionLabel.text=animationSource!=null&&animationSource.ActionId=="reload"?GameLocalization.T("RELOADING"):GameLocalization.T("EQUIPPING");
        deathLabel.gameObject.SetActive(dead);deathLabel.text=GameLocalization.T("ELIMINATED");
        connection.text=PhotonNetwork.InRoom?GameLocalization.T("LIVE  /  ")+PhotonNetwork.GetPing()+GameLocalization.T(" MS"):GameLocalization.T("LOCAL");
        RefreshStamina();
    }
    private void RefreshStamina()
    {
        float value = movement != null ? Mathf.Clamp01(movement.StaminaNormalized) : 0f;
        if (Mathf.Approximately(value, lastStaminaValue)) return;
        lastStaminaValue = value;
        Color color = Color.Lerp(danger, new Color(.72f, .83f, .64f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, .4f, value)));
        if (staminaFill != null)
        {
            staminaFill.rectTransform.anchorMax = new Vector2(value, 1f);
            staminaFill.color = color;
        }
        if (staminaLabel != null) staminaLabel.color = color;
    }
}
