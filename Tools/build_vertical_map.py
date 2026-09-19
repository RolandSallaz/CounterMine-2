"""Author the static, editable bridge prefab. No runtime map generation."""
from pathlib import Path
import json
import math
import re
import uuid

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets/maps/map1'
OUT = ASSETS / 'VerticalArena'
OUT.mkdir(exist_ok=True)

def meta(path, folder=False):
    target = Path(str(path) + '.meta')
    if target.exists():
        return re.search(r'guid: (\w+)', target.read_text())[1]
    guid = uuid.uuid4().hex
    target.write_text('fileFormatVersion: 2\nguid: ' + guid + ('\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n' if folder else '\n'))
    return guid

meta(OUT, True)
palette = {'Steel': (.15,.23,.27), 'Deck': (.29,.36,.38),
           'Concrete': (.62,.58,.48), 'Dark': (.085,.12,.14),
           'Alpha': (.17,.66,.79), 'Bravo': (.95,.49,.16), 'Marking': (.92,.79,.44)}
materials = {}
template = (ASSETS / 'Map1Base.mat').read_text()
for name, color in palette.items():
    path = OUT / (name + '.mat')
    text = template.replace('m_Name: Map1Base', 'm_Name: ' + name)
    rgba = '{r: %s, g: %s, b: %s, a: 1}' % color
    text = re.sub(r'(- _(?:BaseColor|Color): )\{[^}]+\}', lambda m: m[1]+rgba, text)
    text = text.replace('m_EnableInstancingVariants: 0', 'm_EnableInstancingVariants: 1')
    path.write_text(text)
    materials[name] = meta(path)

objects = []
def box(name, pos, size, material='Steel', angle=0, solid=True):
    assert min(size) > 0
    objects.append(dict(name=name, pos=pos, size=size, material=material, angle=angle, solid=solid))

# World-space floor is y=0; roofs are 3.5 m high. Deck clearance is 5.65 m.
HEIGHT = 6.0
RAMP_RUN = 26.0
for side in [-1,1]:
    box('Bridge deck', (side*8.6,HEIGHT-.175,0), (9.2,.35,3.8), 'Deck')
box('Central overlook', (0,HEIGHT-.175,0), (8,.35,8), 'Deck')
for side in [-1,1]:
    team = 'Alpha' if side < 0 else 'Bravo'
    slope = -side * math.degrees(math.atan2(HEIGHT,RAMP_RUN))
    length = math.hypot(RAMP_RUN,HEIGHT)
    box(team+' approach ramp', (side*26,HEIGHT/2-.15/math.cos(math.radians(slope)),0), (length+.18,.3,2.35), 'Deck', slope)
    # Side beams follow the slope; they do not obstruct the central walking strip.
    for z in [-1.26,1.26]:
        box(team+' ramp edge', (side*26,HEIGHT/2+.05,z), (length,.15,.13), team, slope)
    box(team+' ramp threshold', (side*39.25,.012,0), (.65,.024,2.5), team, solid=False)
    # Segmented parapets keep long sightlines from dominating the entire map.
    for x in [6.2,10.4]:
        for z in [-1.83,1.83]:
            box(team+' bridge cover', (side*x,HEIGHT+.58,z), (2.5,1.16,.24), 'Steel')
            box(team+' cover accent', (side*x,HEIGHT+1.09,z), (2.3,.09,.26), team, solid=False)
    for x in [5,11.6]:
        for z in [-2.05,2.05]:
            box('Bridge support', (side*x,(HEIGHT-.4)/2,z), (.38,HEIGHT-.4,.38), 'Dark')
            box('Support footing', (side*x,.18,z), (.8,.36,.8), 'Concrete')
    # Ground-level flanks: staggered cover leaves the central underpass open.
    for x,z in [(10,8.5),(17,-9.5),(28,10.5)]:
        box(team+' ground barricade', (side*x,.68,z*side), (3.4,1.36,.65), 'Concrete')
        box(team+' barricade stripe', (side*x,1.2,z*side), (3.42,.15,.67), team, solid=False)
    # Deck side fascia and directional lane markings.
    for x in [5,8,11]:
        box(team+' deck stripe', (side*x,HEIGHT+.01,0), (.8,.02,.15), team, solid=False)
    for z in [-1,1]:
        if z == side: continue  # Open the diagonal roof-exit corner.
        box('Hub corner cover', (side*3.55,HEIGHT+.58,z*3.05), (.36,1.16,1.8), 'Concrete')
        box('Hub corner cap', (side*3.55,HEIGHT+1.18,z*3.05), (.4,.08,1.9), 'Marking', solid=False)

# Diagonal roof exits: turn onto the landing, then descend along the roof.
for side in [-1,1]:
    rise = HEIGHT-3.5
    angle = side*math.degrees(math.atan2(rise,4))
    box('Roof access '+str(side), (side*2,(HEIGHT+3.5)/2-.12/math.cos(math.radians(angle)),side*5), (math.hypot(4,rise)+.12,.24,1.8), 'Deck', angle)
    box('Roof exit landing', (side*4.45,HEIGHT-.175,side*4.6), (.9,.35,2.6), 'Deck')
    box('Roof exit outer rail', (side*4.85,HEIGHT+.5,side*4.6), (.1,1,2.6), 'Steel')
    box('Roof landing support', (side*4.45,(HEIGHT-.35)/2,side*5.6), (.25,HEIGHT-.35,.25), 'Dark')
    box('Roof perimeter cover', (-side*1.8,4.08,side*5.95), (1.1,1.16,.28), 'Concrete')
    box('Hub route marking', (side*2.8,HEIGHT+.01,side*3.3), (.8,.02,.18), 'Marking', solid=False)

# Close-range cargo courts, rotated symmetrically for both teams. Main lanes at
# z=+/-11.8 and the middle passage stay open; each court has multiple entrances.
for side in [-1,1]:
    team = 'Alpha' if side < 0 else 'Bravo'
    for x,z,sx,sz,h in [(10,-7.5,6,2.4,2.8),(16,-4.8,2.6,5,3.0)]:
        box(team+' cargo container', (side*x,h/2,side*z), (sx,h,sz), 'Steel')
        box('Container roof lip', (side*x,h+.04,side*z), (sx+.12,.08,sz+.12), 'Dark', solid=False)
        # Shallow corrugated ribs add silhouette detail without extra colliders.
        for offset in [-.35,0,.35]:
            box('Container reinforcement', (side*(x+offset*sx),h/2,side*(z-sz/2-.025)), (.07,h,.06), team, solid=False)
        box('Container ID stripe', (side*x,h*.72,side*(z+sz/2+.015)), (sx*.75,.2,.035), team, solid=False)
    for x,z,h in [(8,-3,1.15),(12,-3,1.75),(15,-9,1.15),(27,-7,1.6),(29,6,1.15)]:
        box(team+' supply crate', (side*x,h/2,side*z), (1.5,h,1.5), 'Concrete')
        for dx in [-.55,.55]:
            box('Crate band', (side*x+dx,h/2,side*z), (.1,h+.02,1.52), 'Dark', solid=False)
        box('Crate cap', (side*x,h+.025,side*z), (1.54,.05,1.54), team, solid=False)
    # L-shaped positions offer a protected reload spot with two ways around.
    box(team+' flank screen', (side*7,1.2,side*9.5), (.35,2.4,3.4), 'Concrete')
    box(team+' flank low return', (side*5.7,.65,side*8), (2.6,1.3,.35), 'Concrete')
    # Mid-height cover in the underpass, leaving a 3 m central walking corridor.
    box(team+' underpass barricade', (side*7,.65,side*2.8), (2.4,1.3,.45), 'Concrete')
    box('Underpass route stripe', (side*7,.015,0), (.8,.03,.18), team, solid=False)

# Mesh details have no collision; the deck and walls carry the gameplay geometry.
for x in [-10,-6,6,10]:
    box('Deck underside rib', (x,HEIGHT-.43,0), (.16,.16,3.8), 'Dark', solid=False)

def vec(v): return '{x: %.7g, y: %.7g, z: %.7g}' % tuple(v)
def header(kind, ident, label):
    return f'--- !u!{kind} &{ident}\n{label}:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n'

root_go, root_tf = 100000, 100001
result = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
result += header(1,root_go,'GameObject') + f'  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {root_tf}}}\n  m_Layer: 0\n  m_Name: Vertical Arena\n  m_TagString: Untagged\n  m_IsActive: 1\n'
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
prefab=OUT/'VerticalArena.prefab'; prefab.write_text(result); guid=meta(prefab)
scene=ROOT/'Assets/Scenes/SampleScene.unity'; text=scene.read_text()
# Reuse the two old entrance walls as flank cover; they otherwise block ramps.
wall_guid=re.search(r'guid: (\w+)', (ROOT/'Assets/LowPolyFPSLite/Prefabs/Wall_04.prefab.meta').read_text())[1]
blocks=re.split(r'(?=--- !u!)',text)
for i,block in enumerate(blocks):
    if block.startswith('--- !u!1001') and f'm_SourcePrefab: {{fileID: 100100000, guid: {wall_guid},' in block:
        x=float(re.search(r'propertyPath: m_LocalPosition.x\n\s+value: ([^\n]+)',block)[1])
        world_z=-7.5 if x<0 else 7.5
        local_z=(world_z+2.30192)/25
        blocks[i]=re.sub(r'(propertyPath: m_LocalPosition.z\n\s+value: )[^\n]+',lambda m:m[1]+str(local_z),block)
text=''.join(blocks)
if guid not in text:
    instance,transform=930200000,930200001
    addition=header(1001,instance,'PrefabInstance')+f'  serializedVersion: 2\n  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {{fileID: 0}}\n    m_Modifications: []\n    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n'
    addition+=f'--- !u!4 &{transform} stripped\nTransform:\n  m_CorrespondingSourceObject: {{fileID: {root_tf}, guid: {guid}, type: 3}}\n  m_PrefabInstance: {{fileID: {instance}}}\n  m_PrefabAsset: {{fileID: 0}}\n'
    text=text.replace('--- !u!1660057539',addition+'--- !u!1660057539')
    text+=f'  - {{fileID: {transform}}}\n'
scene.write_text(text)
report=ROOT/'Documentation/Map'; report.mkdir(exist_ok=True)
(report/'geometry.json').write_text(json.dumps(objects,indent=2))
print(f'Authored {len(objects)} pieces, {sum(o["solid"] for o in objects)} colliders. Ramp slope: {math.degrees(math.atan2(HEIGHT,RAMP_RUN)):.2f} degrees.')
