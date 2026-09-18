from pathlib import Path
import numpy as np,soundfile as sf,zipfile,io,uuid,json
from scipy.signal import resample_poly
root=Path('Assets/Resources/Audio');src=Path('Documentation/AudioSources');manifest=[]
def write(name,a,sr,source):
 a=np.asarray(a)
 if a.ndim>1:a=a.mean(axis=1)
 if sr!=44100:
  import math
  g=math.gcd(sr,44100);a=resample_poly(a,44100//g,sr//g)
 if max(abs(a))>0:a=a/max(abs(a))*.89
 n=min(len(a)//2,220);a[:44]*=np.linspace(0,1,44);a[-n:]*=np.linspace(1,0,n)
 p=root/(name+'.wav');p.parent.mkdir(parents=True,exist_ok=True);sf.write(p,a,44100,subtype='PCM_16')
 manifest.append(dict(asset=str(p),source=source,duration=len(a)/44100,peak=float(max(abs(a)))))
 return p
p=src/'Extracted/Prepared SFX Library/AK-47/C_28P.wav';a,sr=sf.read(p)
for i,t in enumerate([.61,3.26,6.02]):
 # Single-shot recording; include attack and natural decay, excluding the next shot.
 lo=int((t-.08)*sr);hi=int((t+.95)*sr);b=a[lo:hi];threshold=np.max(abs(b))*.035
 onset=np.where(np.max(abs(b),axis=1)>threshold)[0][0];b=b[max(0,onset-int(sr*.002)):]
 write('AK74/shot'+str(i),b,sr,f'{p.name}, shot near {t}s (AK-47 recording used as AK-74 placeholder)')
a,sr=sf.read(src/'assaultriflereload1_0.wav');write('AK74/reload',a,sr,'SpringySpringo: assaultriflereload1_0.wav')
a,sr=sf.read(src/'equipment_clicks3.wav');write('AK74/equip',a[int(.78*sr):int(1.13*sr)],sr,'LFA: equipment_clicks3.wav 0.78-1.13s');write('AK74/dry',a[int(.16*sr):int(.39*sr)],sr,'LFA: equipment_clicks3.wav 0.16-0.39s')
with zipfile.ZipFile(src/'kenney_impact-sounds.zip') as z:
 for i in range(4):
  name=f'Audio/footstep_concrete_{i:03}.ogg';a,sr=sf.read(io.BytesIO(z.read(name)));write('Steps/step'+str(i),a,sr,'Kenney Impact Sounds: '+name)
 license1=z.read('License.txt').decode('utf-8-sig')
with zipfile.ZipFile(src/'kenney_interface-sounds.zip') as z:
 a,sr=sf.read(io.BytesIO(z.read('Audio/click_001.ogg')));write('UI/click',a,sr,'Kenney Interface Sounds: click_001.ogg');license2=z.read('License.txt').decode('utf-8-sig')
credits=Path('Assets/StreamingAssets/AudioCredits.txt');credits.parent.mkdir(exist_ok=True)
credits.write_text('CounterMine audio credits\n\nAll currently shipped clips use CC0 1.0: https://creativecommons.org/publicdomain/zero/1.0/\n\nGunshots: Ben Jaszczak et al., The Free Firearm Sound Library\nhttps://opengameart.org/content/the-free-firearm-sound-library\nSource: AK-47/C_28P.wav. Three shots trimmed, mixed to mono, resampled, normalized and faded. Used as temporary AK-74 sound; not an authentic AK-74 recording.\n\nReload: SpringySpringo, Gun Reload Sounds\nhttps://opengameart.org/content/gun-reload-sounds\nMono conversion, resampling, normalization, fades.\n\nEquip/dry click: LFA, Equipment Clicks III\nhttps://opengameart.org/content/equipment-clicks-iii\nTrimmed, normalized and faded.\n\nSteps/impacts: Kenney Impact Sounds\nhttps://kenney.nl/assets/impact-sounds\nUI: Kenney Interface Sounds\nhttps://kenney.nl/assets/interface-sounds\nConverted to mono WAV, normalized and faded.\n\nOriginal Kenney license notices:\n'+license1+'\n'+license2,encoding='utf-8')
(src/'selected-clips.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
# Stable references before Unity import.
def guid(p):
 meta=Path(str(p)+'.meta')
 if meta.exists():
  import re
  return re.search(r'guid: (\w+)',meta.read_text()).group(1)
 g=uuid.uuid4().hex
 if p.suffix=='.cs':body='MonoImporter:\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
 elif p.suffix=='.wav':body='AudioImporter:\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 0.85\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 1\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n'
 else:body='NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n'
 meta.write_text('fileFormatVersion: 2\nguid: '+g+'\n'+body);return g
script=guid(Path('Assets/scripts/WeaponAudioProfile.cs'))
for path in root.rglob('*.wav'):guid(path)
for name in ['GameAudio','PlayerAudio']:guid(Path('Assets/scripts/'+name+'.cs'))
def ref(name):return '{fileID: 8300000, guid: '+guid(root/(name+'.wav'))+', type: 3}'
profile=root/'Weapons/ak74.asset';profile.parent.mkdir(exist_ok=True)
profile.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: '+script+', type: 3}\n  m_Name: ak74\n  m_EditorClassIdentifier: Assembly-CSharp::WeaponAudioProfile\n  displayName: AK-74\n  shots:\n'+''.join('  - '+ref('AK74/shot'+str(i))+'\n' for i in range(3))+'  equip: '+ref('AK74/equip')+'\n  reload: '+ref('AK74/reload')+'\n  dryFire: '+ref('AK74/dry')+'\n  shotVolume: 0.75\n  shotRange: 90\n  pitchVariation: 0.02\n');guid(profile)
print('Prepared',len(manifest),'clips; PCM bytes',sum(p.stat().st_size for p in root.rglob('*.wav')))
