from pathlib import Path
p=Path('Assets/scripts/PlayerHUD.cs');s=p.read_text(encoding='utf-8-sig').replace('private Text radarLabel;', 'private Text radarLabel;\n    private readonly Text[] skillLabels = new Text[RadarSkill.SlotCount];').replace('radarProgress = -1;', 'radarProgress = -1, radarCharges = -1;');a=s.index('        if (radarLabel == null)',s.index('private void RefreshRadar'));b=s.index('    private void UpdateRespawn',a)
s=s[:a]+'''        if (radarLabel == null)
        {
            for (int slot = 0; slot < RadarSkill.SlotCount; slot++)
            {
                var panel = new GameObject("Skill Slot " + (slot + 1), typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(safeArea != null ? safeArea : transform, false);
                var rect = (RectTransform)panel.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
                rect.pivot = new Vector2(.5f, 1f); rect.anchoredPosition = new Vector2((slot - 1) * 210, -104);
                rect.sizeDelta = new Vector2(200, 52);
                var background = panel.GetComponent<Image>(); background.color = new Color(.025f, .035f, .045f, .9f); background.raycastTarget = false;
                var label = new GameObject("Status", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(panel.transform, false);
                var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 16; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                text.text = "[" + (slot + 3) + "]  EMPTY"; text.color = new Color(.45f, .5f, .55f);
                var labelRect = text.rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                skillLabels[slot] = text;
            }
            radarLabel = skillLabels[0];
        }
        int seconds = Mathf.CeilToInt(radar.Remaining);
        if (seconds == radarSeconds && radarProgress == radar.Progress && radarCharges == radar.Charges) return;
        radarSeconds = seconds; radarProgress = radar.Progress; radarCharges = radar.Charges;
        radarLabel.text = seconds > 0 ? $"[3] RADAR  {seconds}s\\nCHARGES {radar.Charges}  |  {radar.Progress}/5"
            : radar.Charges > 0 ? $"[3] RADAR  READY x{radar.Charges}\\nNEXT {radar.Progress}/5 KILLS"
            : $"[3] RADAR\\n{radar.Progress}/5 KILLS";
        radarLabel.color = seconds > 0 ? new Color(1f, .7f, .3f)
            : radar.Charges > 0 ? new Color(.45f, 1f, .7f) : new Color(.65f, .78f, .82f);
    }
''' + s[b:];p.write_text(s,encoding='utf-8')
p=Path('Assets/Editor/ValidateRadarSkill.cs');s=p.read_text();a=s.index('            Check(progress.Record(Kill(1, -1001)');b=s.index('            var shader',a)
s=s[:a]+'''            Check(!progress.TryActivate(9), "Activated without a charge");
            Check(progress.Record(Kill(1, -1001), 1, 10), "Fifth bot kill did not grant charge");
            Check(progress.Kills == 0 && progress.Charges == 1 && progress.Remaining(10) == 0, "Charge auto-activated");
            Check(progress.TryActivate(12) && progress.Charges == 0 && progress.Remaining(12) == 15, "Manual activation failed");
            for (int i = 0; i < 10; i++) progress.Record(Kill(), 1, 20);
            Check(progress.Charges == 2 && !progress.TryActivate(20) && progress.Charges == 2, "Active radar consumed another charge");
            Check(progress.Remaining(27) == 0 && progress.TryActivate(27) && progress.Charges == 1, "Queued charge failed after expiry");
            progress.Reset(); Check(progress.Kills == 0 && progress.Charges == 0 && progress.Remaining(30) == 0, "Room reset failed");
''' + s[b:];s=s.replace('radar_validation.txt','radar_manual_validation.txt').replace('duration 15 seconds; repeat refresh; room reset.', 'manual activation; no automatic activation; charges accumulate; no consumption while active; duration 15 seconds; room reset.');p.write_text(s)
