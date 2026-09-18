import soundfile as sf,numpy as np
from pathlib import Path
from scipy.signal import find_peaks
for p in Path('Documentation/AudioSources').rglob('*.wav'):
 a,sr=sf.read(p,always_2d=True);a=a.mean(axis=1); hop=int(sr*.01);env=np.array([np.max(np.abs(a[i:i+hop])) for i in range(0,len(a),hop)]);peaks,_=find_peaks(env,height=env.max()*.35,distance=30)
 print(p.name, 'max',max(abs(a)), 'peaks', [(round(i*.01,2),round(env[i],3)) for i in peaks])
