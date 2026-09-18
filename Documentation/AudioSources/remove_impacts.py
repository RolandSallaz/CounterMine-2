from pathlib import Path
import json
root=Path.cwd().resolve();folder=root/'Assets/Resources/Audio/Impacts'
for p in folder.glob('*'):
 assert p.resolve().is_relative_to(root/'Assets/Resources/Audio/Impacts')
 if p.name in [f'impact{i}.wav'+suffix for i in range(3) for suffix in ['', '.meta']]:p.unlink()
if folder.exists() and not any(folder.iterdir()):folder.rmdir()
meta=Path(str(folder)+'.meta')
if meta.exists():meta.unlink()
p=Path('Documentation/AudioSources/prepare.py');s=p.read_text();start=s.index(' for i in range(3):\n  name=f\'Audio/impactMetal');end=s.index(' license1=',start);s=s[:start]+s[end:];p.write_text(s)
p=Path('Documentation/AudioSources/selected-clips.json');entries=[e for e in json.loads(p.read_text()) if 'Impacts' not in e['asset']];p.write_text(json.dumps(entries,indent=2))
p=Path('Documentation/AudioSources/Audio.md');s=p.read_text(encoding='utf-8');s=s.replace('оружие других игроков и импакты — в 3D', 'оружие других игроков — в 3D');s=s.replace('14 моно WAV (около 550 КиБ PCM)', '11 моно WAV');s=s.replace('14 непустых моно', '11 непустых моно');s+='\nЗвуки попаданий отключены по запросу; их клипы удалены из Assets. Искры и следы пуль сохранены.\n';p.write_text(s,encoding='utf-8')
p=Path('Assets/StreamingAssets/AudioCredits.txt');s=p.read_text(encoding='utf-8').replace('Steps/impacts:', 'Steps:');p.write_text(s,encoding='utf-8')
p=Path('Documentation/AudioSources/Validation/unity-validation.txt')
if p.exists():p.rename(p.with_name('unity-validation-before-impact-removal.txt'))

