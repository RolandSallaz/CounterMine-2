using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
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
    [SerializeField, Min(0f)] private float respawnDelay = 3f;
    private float deathAt = -1f;
    private GameObject respawnPanel;
    private Button respawnButton;
    private Text respawnLabel;
    private RectTransform killfeedRoot;
    private Font killfeedFont;
    [SerializeField] private HitDirectionIndicator hitDirection;
    [SerializeField] private ThreatArrow threatArrow;
    private readonly System.Collections.Generic.List<GrenadeProjectile> threatScratch = new System.Collections.Generic.List<GrenadeProjectile>();
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
    private int lastHealth = -1;
    private float damageFlash, refreshAt;
    private Rect previousSafeArea;
    private Camera playerCam;
    private Canvas hudCanvas;
    private Vector2[] baseArmSizes;
    private float lastStaminaValue = -1f;
    private static readonly Vector2[] Directions = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
    public void Bind(PlayerHealth player)
    {
        health=player;animationSource=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
        aim=player.GetComponentInChildren<WeaponAimController>(true);recoil=player.GetComponentInChildren<WeaponRecoilController>(true);
        movement=player.GetComponent<PlayerController>();ragdoll=player.GetComponent<PlayerRagdollController>();
        ammo=player.GetComponent<WeaponAmmo>();
        grenades=player.GetComponent<GrenadeThrower>();
        playerCam=player.GetComponentInChildren<Camera>(true);hudCanvas=GetComponent<Canvas>();
        if (hitDirection == null && hudCanvas != null) hitDirection = HitDirectionIndicator.Create(hudCanvas.transform);
        if (threatArrow == null && hudCanvas != null) threatArrow = ThreatArrow.Create(hudCanvas.transform, new Color(1f, .6f, .1f));
        if(crosshairArms!=null&&crosshairArms.Length>0)
        {
            baseArmSizes=new Vector2[crosshairArms.Length];
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
    }
    private void OnDisable()
    {
        PlayerHealth.OnKilled -= HandleKillfeedKill;
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
        if (health == null || info.victim == null || info.victim != health || hitDirection == null) return;
        if (!TryAttackerPosition(info, out Vector3 attackerPos)) return;
        if (TryScreenAngle(attackerPos, out float angle)) hitDirection.Show(angle);
    }

    /// <summary>Screen angle to a world point. 0 = ahead, positive = right.</summary>
    private bool TryScreenAngle(Vector3 worldPos, out float angle)
    {
        angle = 0f;
        if (health == null) return false;
        Vector3 toTarget = worldPos - health.transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < .01f) return false;
        Transform view = playerCam != null ? playerCam.transform : health.transform;
        Vector3 forward = view.forward; forward.y = 0f;
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
        // Fallback: bullet force points from the shooter to the victim.
        Vector3 back = -info.force; back.y = 0f;
        if (back.sqrMagnitude > .000001f) { attackerPos = health.transform.position + back.normalized * 10f; return true; }
        return false;
    }

    private bool EnsureKillfeed()
    {
        if (killfeedRoot != null) return true;
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas == null) return false;
        killfeedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudCanvas.pixelPerfect = true;
        var rootGo = new GameObject("Killfeed");
        rootGo.transform.SetParent(hudCanvas.transform, false);
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
            AddKillfeedLabel(entry.transform, "died", new Color(1f, 1f, 1f, .65f), TextAnchor.MiddleRight);
        }
        else
        {
            AddKillfeedLabel(entry.transform, killerName, killerColor, TextAnchor.MiddleRight);
            AddKillfeedLabel(entry.transform, "\u2192 " + weapon + " \u2192", Color.white, TextAnchor.MiddleCenter);
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
        if(health==null)return;
        if(PhotonNetwork.InRoom&&!health.photonView.IsMine){gameObject.SetActive(false);return;}
        if(lastHealth>=0&&health.CurrentHealth<lastHealth){damageFlash=.65f;ShowHitDirection(health.LastDamage);}
        lastHealth=health.CurrentHealth;damageFlash=Mathf.MoveTowards(damageFlash,0,Time.unscaledDeltaTime*1.4f);damageOverlay.alpha=damageFlash;
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
        if(Screen.safeArea!=previousSafeArea&&Screen.width>0&&Screen.height>0){previousSafeArea=Screen.safeArea;safeArea.anchorMin=new Vector2(previousSafeArea.xMin/Screen.width,previousSafeArea.yMin/Screen.height);safeArea.anchorMax=new Vector2(previousSafeArea.xMax/Screen.width,previousSafeArea.yMax/Screen.height);}
    }
    private void RefreshRadar()
    {
        var radar = RadarSkill.Instance;
        if (radar == null) return;
        bool created = skillCards[0] == null;
        if (created)
            for (int i = 0; i < skillCards.Length; i++)
                skillCards[i] = SkillSlotUI.Create(safeArea != null ? safeArea : transform, i);
        if (!created && radarProgress == radar.Progress && radarCharges == radar.Charges && radarWasActive == radar.Active) return;
        radarProgress = radar.Progress; radarCharges = radar.Charges; radarWasActive = radar.Active;
        for (int i = 0; i < skillCards.Length; i++)
            skillCards[i].SetState(radar.SkillAt(i) != RadarSkill.SkillKind.None,
                i == 0 ? radar.Progress : 0, i == 0 ? radar.Charges : 0, i == 0 && radar.Active);
    }
    private void UpdateRespawn(bool dead)
    {
        if (!dead)
        {
            deathAt = -1f;
            if (respawnPanel != null) respawnPanel.SetActive(false);
            return;
        }
        if (deathAt < 0f)
        {
            deathAt = Time.unscaledTime;
            EnsureRespawnPanel();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (respawnPanel != null) respawnPanel.SetActive(true);
        float remaining = Mathf.Max(0f, respawnDelay - (Time.unscaledTime - deathAt));
        if (respawnButton != null) respawnButton.interactable = remaining <= 0f;
        if (respawnLabel != null) respawnLabel.text = remaining > 0f ? $"RESPAWN IN {Mathf.CeilToInt(remaining)}" : "RESPAWN";
    }

    private void EnsureRespawnPanel()
    {
        if (respawnPanel != null) return;
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas == null) return;
        if (hudCanvas.GetComponent<GraphicRaycaster>() == null) hudCanvas.gameObject.AddComponent<GraphicRaycaster>();
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Font font = killfeedFont ?? (weaponName != null ? weaponName.font : null) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        respawnPanel = new GameObject("Respawn Panel");
        respawnPanel.transform.SetParent(hudCanvas.transform, false);
        var panelRect = respawnPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(.5f, .5f);
        panelRect.anchorMax = new Vector2(.5f, .5f);
        panelRect.pivot = new Vector2(.5f, .5f);
        panelRect.anchoredPosition = new Vector2(0f, -120f);
        panelRect.sizeDelta = new Vector2(320f, 150f);
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
        respawnButton.interactable = false;
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(260f, 56f);
        var layoutElement = buttonGo.AddComponent<LayoutElement>();
        layoutElement.minHeight = 56f;
        layoutElement.preferredHeight = 56f;

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
        respawnLabel.text = "RESPAWN";
        respawnButton.onClick.AddListener(OnRespawnClicked);
    }

    private void OnRespawnClicked()
    {
        GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);
        if (respawnButton != null) respawnButton.interactable = false;
        // Inside the click handler, so pointer-lock requests stay browser-legal.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        var lobby = Object.FindFirstObjectByType<LobbyManager>();
        if (lobby != null && health != null) lobby.RespawnPlayer(health.gameObject);
        else Debug.LogWarning("[PlayerHUD] Respawn unavailable: lobby or player missing.", this);
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
        teamLabel.text=team==2?"BRAVO / 02":"ALPHA / 01";teamLabel.color=color;
        playerName.text=string.IsNullOrWhiteSpace(health.photonView.Owner?.NickName)?"OPERATOR":health.photonView.Owner.NickName.ToUpperInvariant();
        healthValue.text=health.CurrentHealth.ToString("000");healthValue.color=health.CurrentHealth<=25?danger:Color.white;
        healthFill.color=health.CurrentHealth<=25?danger:color;healthFill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01((float)health.CurrentHealth/health.MaximumHealth),1);
        var id=animationSource!=null?animationSource.WeaponId:null;weaponName.text=id=="ak74"?"AK-74":string.IsNullOrEmpty(id)?"UNARMED":id.Replace('_',' ').ToUpperInvariant();
        if(ammoLabel!=null)
        {
            if(ammo==null){ammoLabel.text="--";ammoLabel.color=Color.white;}
            else{ammoLabel.text=ammo.MagAmmo+" / INF";ammoLabel.color=ammo.MagAmmo<=0?danger:Color.white;}
        }
        if(grenadeLabel!=null)
        {
            if(grenades==null){grenadeLabel.text="G --";grenadeLabel.color=Color.white;}
            else{grenadeLabel.text="G x"+grenades.Grenades;grenadeLabel.color=grenades.Grenades<=0?danger:Color.white;}
        }
        bool dead=health.IsDead;bool rag=ragdoll!=null&&ragdoll.IsRagdoll;bool action=animationSource!=null&&animationSource.IsPlayingAction;
        weaponState.text=dead?"OFFLINE":rag?"RAGDOLL":action?"BUSY":"READY";
        actionLabel.text=animationSource!=null&&animationSource.ActionId=="reload"?"RELOADING":"EQUIPPING";
        deathLabel.gameObject.SetActive(dead);deathLabel.text="ELIMINATED";
        connection.text=PhotonNetwork.InRoom?"LIVE  /  "+PhotonNetwork.GetPing()+" MS":"LOCAL";
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
