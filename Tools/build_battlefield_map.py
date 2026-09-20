"""Author the static, editable Battlefield-style construction arena. No runtime map generation."""
from pathlib import Path
import json
import math
import re
import uuid

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets/maps/map1'
OUT = ASSETS / 'BattlefieldArena'
OUT.mkdir(exist_ok=True)

def meta(path, folder=False):
    target = Path(str(path) + '.meta')
    if target.exists():
        return re.search(r'guid: (\w+)', target.read_text())[1]
    guid = uuid.uuid4().hex
    target.write_text('fileFormatVersion: 2\nguid: ' + guid + ('\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n' if folder else '\n'))
    return guid

meta(OUT, True)
palette = {'Steel': (0.23, 0.29, 0.32), 'Deck': (0.34, 0.37, 0.38), 'Concrete': (0.6, 0.59, 0.55), 'Dark': (0.095, 0.115, 0.13), 'Alpha': (0.16, 0.54, 0.67), 'Bravo': (0.86, 0.39, 0.13), 'Marking': (0.88, 0.68, 0.29)}
# Metallic and smoothness stay within the existing Lit shader variant.
finish = {'Steel': (0.55, 0.32), 'Deck': (0.08, 0.2), 'Concrete': (0, 0.08), 'Dark': (0, 0.12), 'Alpha': (0.15, 0.3), 'Bravo': (0.15, 0.3), 'Marking': (0.1, 0.26)}
materials = {}
template = (ASSETS / 'Map1Base.mat').read_text()
for name, color in palette.items():
    path = OUT / (name + '.mat')
    text = template.replace('m_Name: Map1Base', 'm_Name: ' + name)
    rgba = '{r: %s, g: %s, b: %s, a: 1}' % color
    text = re.sub(r'(- _(?:BaseColor|Color): )\{[^}]+\}', lambda m: m[1]+rgba, text)
    text = text.replace('m_EnableInstancingVariants: 0', 'm_EnableInstancingVariants: 1')
    for prop, value in [('_Metallic', finish[name][0]), ('_Smoothness', finish[name][1]), ('_Glossiness', finish[name][1])]:
        text = re.sub(r'(- ' + prop + r': )[\d.]+', lambda m: m[1] + str(value), text)
    path.write_text(text)
    materials[name] = meta(path)

objects = []
def box(name, pos, size, material='Steel', angle=0, solid=True):
    assert min(size) > 0
    objects.append(dict(name=name, pos=pos, size=size, material=material, angle=angle, solid=solid))


"""Geometry section used by build_battlefield_map.py; dimensions are in metres."""
box('Foundation', (0,-.4,0), (132,.8,92), 'Concrete')
box('Central avenue', (0,.015,0), (124,.025,12), 'Dark', solid=False)
for z in [-29,29]:
    box('Cross street', (0,.02,z), (124,.03,6), 'Deck', solid=False)
for x in [-24,24]:
    box('Service avenue', (x,.02,0), (6,.03,84), 'Deck', solid=False)
for side in [-1,1]:
    box('Boundary retaining wall', (side*65,2,0), (1,4,92), 'Concrete')
    box('Boundary retaining wall', (0,2,side*45), (130,4,1), 'Concrete')
    team='Alpha' if side<0 else 'Bravo'
    # Dogleg screens protect bases without closing their three exits.
    for z in [-7,7]:
        box(team+' base screen', (side*51,1.8,z), (1,3.6,7), 'Concrete')
        box(team+' base identification', (side*50.48,2.7,z), (.04,.8,5), team, solid=False)
    for z in [-15,15]:
        box(team+' base flank cover', (side*57,.7,z), (5,1.4,.8), 'Steel')

def skeleton(name,cx,cz,width,depth,levels,accent):
    height=levels*4
    # Separate structural members make every building editable, with open walls.
    for x in [-width/2+.5,0,width/2-.5]:
        for z in [-depth/2+.5,0,depth/2-.5]:
            box(name+' concrete column',(cx+x,height/2,cz+z),(.65,height,.65),'Concrete')
            box(name+' column footing',(cx+x,.2,cz+z),(1.1,.4,1.1),'Dark')
    for level in range(1,levels+1):
        y=level*4
        box(name+' floor '+str(level),(cx,y-.18,cz),(width,.36,depth),'Deck')
        for z in [-depth/2+.25,depth/2-.25]:
            box(name+' edge beam',(cx,y-.5,cz+z),(width,.6,.45),'Concrete')
            # Gaps at each corner connect floor to the external stair landings.
            box(name+' parapet',(cx,y+.55,cz+z),(width-8,1.1,.3),'Concrete')
            box(name+' level stripe',(cx,y-.45,cz+z*1.014),(width-1,.18,.05),accent,solid=False)
        for x in [-width/2+.25,width/2-.25]:
            for z in [-depth*.27,depth*.27]:
                box(name+' side cover',(cx+x,y+.55,cz+z),(.3,1.1,depth*.32),'Concrete')
        # Interior screens break cross-map firing lines and provide reload pockets.
        if level<levels:
            box(name+' partial core wall',(cx+(-1 if level%2 else 1)*2.5,y+1.5,cz),(3,3,.5),'Concrete')
        box(name+' floor supplies',(cx+3,y+.65,cz+depth*.22),(2,1.3,1.3),'Steel')
    # Two independent access routes. Flights run parallel on successive levels
    # for full headroom; cross the floor to reach the next flight. Smooth ramps
    # small steps let both CharacterController and navigation traverse every level.
    run=width-4
    for side in [-1,1]:
        z=cz+side*(depth/2+1.55)
        for level in range(levels):
            direction=side
            angle=direction*math.degrees(math.atan2(4,run))
            length=math.hypot(run,4)
            box(name+' stair flight '+str(level+1),(cx,level*4+2-.16/math.cos(math.radians(angle)),z),(length+.14,.32,2.8),'Deck',angle)
            for edge in [-1,1]:
                box(name+' stair stringer',(cx,level*4+2+.06,z+edge*1.42),(length,.14,.12),accent,angle)
            for end in [-1,1]:
                y=(level+(1 if end==direction else 0))*4
                box(name+' stair landing',(cx+end*(run/2+.8),y-.18,cz+side*(depth/2+.6)),(2.2,.36,5),'Deck')
                # Outer landing parapet leaves the floor-side entrance clear.
                box(name+' landing guard',(cx+end*(run/2+.8),y+.55,cz+side*(depth/2+3)),(2.2,1.1,.16),'Steel')
    # Exposed beam crown gives the tallest structure an unfinished silhouette.
    for x in [-width/2+.5,width/2-.5]:
        for z in [-depth/2+.5,depth/2-.5]:
            box(name+' exposed crown column',(cx+x,height+1.7,cz+z),(.35,3.4,.35),'Steel')
    for z in [-depth/2+.5,depth/2-.5]:
        box(name+' exposed roof beam',(cx,height+3.2,cz+z),(width,.32,.35),'Steel')

skeleton('CITADEL',0,0,22,20,5,'Marking')
for side in [-1,1]:
    team='Alpha' if side<0 else 'Bravo'
    skeleton(team+' north frame',side*34,23,16,12,2,team)
    skeleton(team+' south frame',side*34,-23,16,12,1,team)
    # Open ground-floor garage bays create close fights near each approach.
    for z in [-5,5]:
        box(team+' garage pillar',(side*42,2,z),(.6,4,.6),'Concrete')
    box(team+' garage canopy',(side*42,4,0),(8,.35,11),'Deck')
    box(team+' garage rear wall',(side*46,1.5,3),(.4,3,4),'Concrete')
    for x,z in [(17,8),(17,-9),(27,3),(28,-7),(44,12),(46,-12),(13,32),(16,-34),(46,36),(50,-34)]:
        box(team+' concrete barricade',(side*x,.65,side*z),(3.8,1.3,.7),'Concrete')
        box(team+' cover stripe',(side*x,1.1,side*z),(3.82,.14,.72),team,solid=False)
    for x,z in [(21,36),(19,-23),(49,24)]:
        box(team+' container',(side*x,1.4,side*z),(7,2.8,2.6),'Steel')
        for dx in [-2.6,-1.3,0,1.3,2.6]:
            box('Container ribs',(side*x+dx,1.4,side*z-1.33),(.08,2.65,.09),team,solid=False)
        box('Container roof',(side*x,2.84,side*z),(7.15,.08,2.75),'Dark',solid=False)
    for x,z in [(15,3),(20,-5),(28,10),(45,-5),(9,25),(9,-27),(52,32),(31,38)]:
        box(team+' supply stack',(side*x,.85,side*z),(1.7,1.7,1.7),'Concrete')
        for dx in [-.6,.6]:box('Supply strapping',(side*x+dx,.85,side*z),(.12,1.72,1.74),'Dark',solid=False)
    for z in [-39,39]:
        for x in [8,20,42,55]:
            box(team+' peripheral cover',(side*x,.9,z),(3,1.8,.6),'Concrete')
    # Road markings define lanes without adding collision or bright visual noise.
    for x in [17,21,25,29,33,37,41,45]:
        box('Avenue dash',(side*x,.04,0),(1.7,.02,.12),'Marking',solid=False)
    # Construction rubble is placed in dead corners, away from stair entrances.
    for i in range(7):
        box('Concrete rubble',(side*(14+i*.75),.2+(i%2)*.12,side*41),(1.2,.4+(i%2)*.24,.8),'Concrete',i*7)

# Construction crane, outside the central playable floors.
box('Crane base',(14,.4,19),(3,.8,3),'Concrete')
for x in [13.4,14.6]:
    for z in [18.4,19.6]:box('Crane mast',(x,14,z),(.2,28,.2),'Marking')
for y in range(2,29,2):box('Crane mast tie',(14,y,19),(1.5,.13,1.5),'Steel',solid=False)
box('Crane boom',(4,28,19),(34,.5,.75),'Marking',solid=False)
box('Crane counterweight',(19,27.5,19),(3,1.5,2),'Dark',solid=False)
box('Crane hoist cable',(-10,23,19),(.045,10,.045),'Dark',solid=False)
box('Crane suspended hook',(-10,18,19),(.3,.5,.25),'Marking',solid=False)

def vec(v): return '{x: %.7g, y: %.7g, z: %.7g}' % tuple(v)
def header(kind, ident, label):
    return f'--- !u!{kind} &{ident}\n{label}:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n'

root_go, root_tf = 100000, 100001
result = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
result += header(1,root_go,'GameObject') + f'  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {root_tf}}}\n  m_Layer: 0\n  m_Name: Battlefield Construction\n  m_TagString: Untagged\n  m_IsActive: 1\n'
result += header(4,root_tf,'Transform') + f'  m_GameObject: {{fileID: {root_go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\n  m_LocalPosition: {{x: 0, y: 0, z: 0}}\n  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_Children:\n'
result += ''.join(f'  - {{fileID: {100011+i*10}}}\n' for i in range(len(objects)))
result += '  m_Father: {fileID: 0}\n'
for i,o in enumerate(objects):
    go = 100010+i*10; tf,mf,mr,bc=go+1,go+2,go+3,go+4
    result += header(1,go,'GameObject')+'  serializedVersion: 6\n  m_Component:\n'+''.join(f'  - component: {{fileID: {n}}}\n' for n in [tf,mf,mr]+([bc] if o['solid'] else []))
    result += f"  m_Layer: 0\n  m_Name: {o['name']}\n  m_TagString: Untagged\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n"
    a=math.radians(o['angle'])/2; y=math.radians(o.get('yaw',0))/2
    q=(math.sin(y)*math.sin(a),math.sin(y)*math.cos(a),math.cos(y)*math.sin(a),math.cos(y)*math.cos(a))
    result += header(4,tf,'Transform')+f'  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: {q[0]}, y: {q[1]}, z: {q[2]}, w: {q[3]}}}\n  m_LocalPosition: {vec(o["pos"])}\n  m_LocalScale: {vec(o["size"])}\n  m_Children: []\n  m_Father: {{fileID: {root_tf}}}\n'
    result += header(33,mf,'MeshFilter')+f'  m_GameObject: {{fileID: {go}}}\n  m_Mesh: {{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}}\n'
    result += header(23,mr,'MeshRenderer')+f'  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  m_CastShadows: 1\n  m_ReceiveShadows: 1\n  m_DynamicOccludee: 1\n  m_MotionVectors: 1\n  m_LightProbeUsage: 1\n  m_ReflectionProbeUsage: 1\n  m_RenderingLayerMask: 1\n  m_Materials:\n  - {{fileID: 2100000, guid: {materials[o["material"]]}, type: 2}}\n  m_SortingLayerID: 0\n  m_SortingOrder: 0\n'
    if o['solid']:
        result += header(65,bc,'BoxCollider')+f'  m_GameObject: {{fileID: {go}}}\n  m_Material: {{fileID: 0}}\n  m_IsTrigger: 0\n  m_Enabled: 1\n  serializedVersion: 3\n  m_Size: {{x: 1, y: 1, z: 1}}\n  m_Center: {{x: 0, y: 0, z: 0}}\n'
route_guid=meta(ROOT/'Assets/scripts/BotPatrolRoute.cs')
points=[(-25,0,-35),(6,4,0),(25,0,35),(39,8,23),(6,12,0),(-29,4,-23),
        (6,20,0),(0,0,-36),(-29,8,23),(6,8,0),(39,4,-23),(0,0,36),(6,16,0)]
result=result.replace('  - component: {fileID: 100001}\n','  - component: {fileID: 100001}\n  - component: {fileID: 100002}\n',1)
result+=header(114,100002,'MonoBehaviour')+f'  m_GameObject: {{fileID: 100000}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {route_guid}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::BotPatrolRoute\n  points:\n'
result+=''.join('  - '+vec(point)+'\n' for point in points)
prefab=OUT/'BattlefieldArena.prefab'; prefab.write_text(result); guid=meta(prefab)

report=ROOT/'Documentation/Battlefield'; report.mkdir(exist_ok=True)
(report/'geometry.json').write_text(json.dumps(objects,indent=2))
print(f'Created {len(objects)} meshes, {sum(o["solid"] for o in objects)} colliders')
