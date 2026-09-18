from pathlib import Path
p=Path('Assets/scripts/WeaponAmmo.cs');s=p.read_text();s=s.replace('private float reloadEndsAt;', 'private float reloadEndsAt;\n    private float nextDrySound;');s=s.replace('public void HandleDryFire()\n    {','public void HandleDryFire()\n    {\n        if (!IsReloading && Time.time >= nextDrySound && weaponAnimation != null)\n        {\n            var profile = weaponAnimation.AudioProfile;\n            if (profile != null) GameAudio.Play(profile.dryFire, transform.position + Vector3.up, .35f, 10f, !BotController.IsBot(this) && IsOwner);\n            nextDrySound = Time.time + .25f;\n        }');p.write_text(s)
p=Path('Assets/scripts/LobbyManager.cs');s=p.read_text(encoding='utf-8-sig');s=s.replace('private void SelectTeam(int team)\n    {', 'private void SelectTeam(int team)\n    {\n        GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);');p.write_text(s,encoding='utf-8')
# Build against Unity's actual assembly references, without touching Bee outputs.
import re
folder=Path('Documentation/AudioSources/Validation');folder.mkdir(exist_ok=True)
p=Path('Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.rsp');s=p.read_text(encoding='utf-8-sig')
s=re.sub(r'^-out:.*$', '-out:"'+str(folder/'Assembly-CSharp.dll').replace('\\','/')+'"',s,flags=re.M)
s=re.sub(r'^-refout:.*$', '-refout:"'+str(folder/'Assembly-CSharp.ref.dll').replace('\\','/')+'"',s,flags=re.M)
for file in Path('Assets/scripts').glob('*.cs'):
 name=str(file).replace('\\','/')
 if '"'+name+'"' not in s:s+='\n"'+name+'"'
(folder/'compile.rsp').write_text(s,encoding='utf-8')
