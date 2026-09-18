from pathlib import Path
p=Path('Assets/scripts/PlayerHUD.cs');s=p.read_text(encoding='utf-8-sig');a=s.index('    private Text radarLabel;');b=s.index('    private readonly System.Collections.Generic.List<GameObject>',a);s=s[:a]+'''    private readonly SkillSlotUI[] skillCards = new SkillSlotUI[RadarSkill.SlotCount];
    private int radarProgress = -1, radarCharges = -1;
    private bool radarWasActive;
'''+s[b:];a=s.index('    private void RefreshRadar()');b=s.index('    private void UpdateRespawn',a);s=s[:a]+'''    private void RefreshRadar()
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
'''+s[b:];p.write_text(s,encoding='utf-8')
p=Path('Assets/scripts/PlayerController.cs');s=p.read_text();s=s.replace('private float slideDuration = .85f;', 'private float slideDuration = .95f;\n    [SerializeField, Min(0f)] private float slideSpeedBoost = .35f;');s=s.replace('slideRemaining = slideDuration;', 'slideRemaining = slideDuration;\n        horizontalVelocity += horizontalVelocity.normalized * slideSpeedBoost;');p.write_text(s)
p=Path('Assets/Resources/Bot.prefab');s=p.read_text().replace('  slideDuration: 0.85','  slideDuration: 0.95');p.write_text(s)
p=Path('Assets/scripts/SkillSlotUI.cs');s=p.read_text().replace('new Vector2(0, 5), new Vector2(68, 68)', 'new Vector2(0, 12), new Vector2(56, 56)').replace('new Vector2(0, 5), new Vector2(30, 30)', 'new Vector2(0, 12), new Vector2(26, 26)');p.write_text(s)
p=Path('Documentation/AudioSources/Validation/compile.rsp');s=p.read_text(encoding='utf-8-sig');s+='\n"Assets/scripts/SkillSlotUI.cs"\n';p.write_text(s,encoding='utf-8')
