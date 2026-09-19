"""Offline geometry checks; Unity physics validation lives in ValidatePresentation."""
from pathlib import Path
import json
import math
import re
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
objects = json.loads((ROOT/'Documentation/Map/geometry.json').read_text())

def rotation(angle=0, yaw=0):
    a,b = map(math.radians,(angle,yaw))
    rz=np.array([[math.cos(a),-math.sin(a),0],[math.sin(a),math.cos(a),0],[0,0,1]])
    ry=np.array([[math.cos(b),0,math.sin(b)],[0,1,0],[-math.sin(b),0,math.cos(b)]])
    return ry@rz

boxes=[(o['name'],np.array(o['pos']),np.array(o['size'])/2,rotation(o['angle'],o.get('yaw',0))) for o in objects if o['solid']]
# Include the existing low-poly building/wall/crate box colliders from the scene.
prefabs={re.search(r'guid: (\w+)',p.read_text())[1]:Path(str(p)[:-5]) for p in (ROOT/'Assets/LowPolyFPSLite/Prefabs').glob('*.prefab.meta')}
scene=(ROOT/'Assets/Scenes/SampleScene.unity').read_text()
for block in re.split(r'(?=--- !u!)',scene):
    if not block.startswith('--- !u!1001'): continue
    match=re.search(r'm_SourcePrefab: .*guid: (\w+)',block)
    if not match or match[1] not in prefabs: continue
    src=prefabs[match[1]].read_text()
    def xyz(text,key):
        m=re.search(key+r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',text)
        return np.array([float(v) for v in m.groups()]) if m else None
    size=xyz(src,'m_Size'); center=xyz(src,'m_Center')
    if size is None or center is None: continue
    values=dict(re.findall(r'propertyPath: (.*?)\n\s+value: (.*?)\n',block))
    pos=np.array([float(values.get('m_LocalPosition.'+axis,0)) for axis in 'xyz'])*25+[-1.36319,0,-2.30192]
    scale=np.array([float(values.get('m_LocalScale.'+axis,1)) for axis in 'xyz'])*25
    rot=rotation(0,float(values.get('m_LocalEulerAnglesHint.y',0)))
    boxes.append((prefabs[match[1]].stem,pos+rot@(center*scale),size*scale/2,rot))

def hits(origin,direction,distance):
    result=[]
    for name,pos,half,rot in boxes:
        start=rot.T@(np.array(origin)-pos); delta=rot.T@np.array(direction)
        low,high=0.,distance
        for s,d,h in zip(start,delta,half):
            if abs(d)<1e-9:
                if abs(s)>h: high=-1;break
            else:
                a,b=(-h-s)/d,(h-s)/d
                low=max(low,min(a,b));high=min(high,max(a,b))
        if low<=high: result.append((low,name))
    return result

def top(x,z):
    result=hits((x,20,z),(0,-1,0),21)
    return max([0]+[20-t for t,_ in result])

for z in [-.3,0,.3]:
    previous=0
    for x in np.linspace(-39.5,39.5,791):
        y=top(x,z)
        assert abs(y-previous)<.3,(x,z,y,previous)
        if abs(x)<13: assert abs(y-6)<.04
        previous=y
for z in [-11.8,11.8]:
    assert not hits((-30,1.6,z),(1,0,0),60),('long lane',z,hits((-30,1.6,z),(1,0,0),60))
for side in [-1,1]:
    for offset in [-.25,0,.25]:
        for y in [.35,1.6]:
            found=hits((side*13.9+offset,y,-side*10),(0,0,side),10)
            assert not found,('cargo aisle',found)
    assert hits((side*10,1.6,-side*10),(0,0,side),5)
    last=6
    for x in np.linspace(4.5,0,91)*side:
        y=top(x,side*5)
        assert abs(y-last)<.3,('roof exit',x,y,last)
        last=y
    assert abs(last-3.5)<.04
message='PASS: analytical oriented-box checks: 2373 upper-route samples; two 60 m sightlines; both cargo aisles with 0.5 m width; both 6 m to 3.5 m roof exits.\nNot a live Unity physics or player movement test.\n'
(ROOT/'Documentation/Map/geometry-validation.txt').write_text(message)
print(message)
