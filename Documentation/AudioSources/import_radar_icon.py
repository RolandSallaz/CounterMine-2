from pathlib import Path
import uuid
p=Path('Assets/Resources/UI/Icons/Radar.png.meta')
if not p.exists():
 s=Path('Assets/TutorialInfo/Icons/URP.png.meta').read_text();s=s.replace('727a75301c3d24613a3ebcec4a24c2c8',uuid.uuid4().hex).replace('spriteMode: 0','spriteMode: 1').replace('textureType: 2','textureType: 8').replace('maxTextureSize: 2048','maxTextureSize: 256').replace('filterMode: 0','filterMode: 1').replace('spriteGenerateFallbackPhysicsShape: 1','spriteGenerateFallbackPhysicsShape: 0');p.write_text(s)
