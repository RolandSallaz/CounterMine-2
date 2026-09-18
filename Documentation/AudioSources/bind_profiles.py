from pathlib import Path
import re
profile=re.search('guid: (\\w+)',Path('Assets/Resources/Audio/Weapons/ak74.asset.meta').read_text()).group(1)
for name in ['Player','Bot']:
 p=Path('Assets/Resources/'+name+'.prefab');s=p.read_text();s=s.replace('  - id: ak74\n', '  - id: ak74\n    audioProfile: {fileID: 11400000, guid: '+profile+', type: 2}\n');p.write_text(s)
