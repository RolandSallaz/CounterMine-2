import bpy, math, random, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceArt/Grenade'
ASSETS = ROOT / 'Assets/Resources/Grenade'
DOC = ROOT / 'Documentation/Grenade'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
random.seed(23)

def material(name, color, metallic=0, roughness=.65):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        bsdf = m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        output = next((n for n in m.node_tree.nodes if n.type == 'OUTPUT_MATERIAL'), None) or m.node_tree.nodes.new('ShaderNodeOutputMaterial')
        m.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = roughness
    return m

olive = material('Grenade_Olive', (.19, .235, .075))
light = material('Grenade_Olive_Light', (.235, .275, .10))
dark = material('Grenade_Olive_Dark', (.13, .17, .045))
groove = material('Grenade_Recess', (.055, .073, .025))
steel = material('Grenade_Steel', (.23, .27, .28), .8, .36)
levermat = material('Grenade_Lever', (.105, .135, .12), .65, .4)
bandmat = material('Grenade_Marking', (.61, .51, .19), .1)
parts = []

def mesh(name, verts, faces, mats):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces); data.update()
    ob = bpy.data.objects.new(name, data); scene.collection.objects.link(ob)
    for mat in mats: data.materials.append(mat)
    parts.append(ob)
    return ob

def lathe(name, rings, segments, mat):
    verts = [(r*math.cos(2*math.pi*i/segments), r*math.sin(2*math.pi*i/segments), z)
             for z, r in rings for i in range(segments)]
    faces = []
    for j in range(len(rings)-1):
        for i in range(segments):
            a=j*segments+i; b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    faces += [tuple(reversed(range(segments))), tuple((len(rings)-1)*segments+i for i in range(segments))]
    return mesh(name, verts, faces, [mat])

# A stylized visual prop, without internal or functional mechanisms.
rings = [(-.04,.014),(-.034,.024),(-.021,.031),(-.005,.034),(.012,.032),(.026,.026),(.036,.016)]
core = lathe('Body_Core', [(z,r*.965) for z,r in rings], 16, groove)
verts=[]; faces=[]
for row in range(len(rings)-1):
    z0,r0=rings[row]; z1,r1=rings[row+1]
    gap=.001
    t=gap/(z1-z0)
    za,zb=z0+gap*.5,z1-gap*.5
    ra,rb=r0+(r1-r0)*t*.5,r1+(r0-r1)*t*.5
    for col in range(16):
        a=2*math.pi*(col+.045)/16; b=2*math.pi*(col+.955)/16
        i=len(verts)
        verts += [(ra*math.cos(a),ra*math.sin(a),za),(ra*math.cos(b),ra*math.sin(b),za),
                  (rb*math.cos(b),rb*math.sin(b),zb),(rb*math.cos(a),rb*math.sin(a),zb),
                  ((ra-.0018)*math.cos(a),(ra-.0018)*math.sin(a),za),
                  ((ra-.0018)*math.cos(b),(ra-.0018)*math.sin(b),za),
                  ((rb-.0018)*math.cos(b),(rb-.0018)*math.sin(b),zb),
                  ((rb-.0018)*math.cos(a),(rb-.0018)*math.sin(a),zb)]
        faces += [tuple(i+k for k in f) for f in [(0,1,2,3),(4,0,3,7),(1,5,6,2),(4,5,1,0),(3,2,6,7),(7,6,5,4)]]
body=mesh('Body_Segments',verts,faces,[olive,light,dark])
for poly in body.data.polygons:
    poly.material_index=random.choices([0,1,2],[.75,.13,.12])[0]
lathe('Neck',[(.034,.015),(.039,.016),(.049,.014),(.054,.009)],12,levermat)
lathe('Marking_Band',[(.038,.0162),(.0405,.0162)],16,bandmat)

# One solid bent lever, separate from body for animation.
profile=[(-.013,.058),(.019,.058),(.038,.036),(.040,-.026),(.035,-.034),
         (.031,-.031),(.035,-.025),(.033,.034),(.017,.053),(-.013,.053)]
verts=[(x,y,z) for y in [-.005,.005] for x,z in profile]
n=len(profile)
faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
lever=mesh('Safety_Lever',verts,faces,[levermat])
bevel=lever.modifiers.new('Edge highlights','BEVEL'); bevel.width=.0006; bevel.segments=1
bpy.context.view_layer.objects.active=lever
bpy.ops.object.modifier_apply(modifier=bevel.name)

bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.0028, depth=.018, location=(-.006,0,.051), rotation=(math.pi/2,0,0))
pin=bpy.context.object; pin.name='Safety_Pin'; pin.data.materials.append(steel); parts.append(pin)
bpy.ops.mesh.primitive_torus_add(major_segments=20, minor_segments=6, location=(-.023,-.01,.05),
    rotation=(math.pi/2,0,0), major_radius=.013, minor_radius=.00125)
ring=bpy.context.object; ring.name='Pull_Ring'; ring.data.materials.append(steel); parts.append(ring)

root=bpy.data.objects.new('Grenade',None); scene.collection.objects.link(root)
for ob in parts: ob.parent=root
grip=bpy.data.objects.new('Grip_R',None); scene.collection.objects.link(grip); grip.parent=root
grip.location=(0,-.024,0); grip.empty_display_type='ARROWS'; grip.empty_display_size=.012
parts += [root,grip]

# Keep game meshes flat shaded and apply scale before exporting.
bpy.ops.object.select_all(action='DESELECT')
for ob in parts: ob.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
for ob in parts:
    if ob.type != 'MESH': continue
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025)
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
for ob in parts: ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(ASSETS/'grenade.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,
    use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,path_mode='AUTO')

# Presentation objects belong only to the .blend, never the FBX.
studio=bpy.data.collections.new('Preview_Studio'); scene.collection.children.link(studio)
def to_studio(ob):
    for collection in list(ob.users_collection): collection.objects.unlink(ob)
    studio.objects.link(ob)
def aim(ob,point): ob.rotation_euler=(Vector(point)-ob.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.041))
plane=bpy.context.object; plane.name='Preview_Ground'; plane.data.materials.append(material('Studio',(.027,.038,.046))); to_studio(plane)
bpy.ops.object.camera_add(location=(.19,-.28,.155))
camera=bpy.context.object; camera.name='Preview_Camera'; camera.data.type='ORTHO'; camera.data.ortho_scale=.155
camera.data.lens=55; camera.data.clip_start=.001; aim(camera,(0,0,.006)); scene.camera=camera; to_studio(camera)
for name,loc,power,size,color in [('Key',(-.13,-.15,.25),4,.18,(1,.9,.72)),('Fill',(.15,-.08,.09),1.5,.15,(.62,.8,1)),('Rim',(.04,.15,.18),5,.12,(1,.94,.8))]:
    bpy.ops.object.light_add(type='AREA',location=loc); ob=bpy.context.object; ob.name=name
    ob.data.energy=power; ob.data.shape='DISK'; ob.data.size=size; ob.data.color=color; aim(ob,(0,0,.01)); to_studio(ob)
scene.render.engine='CYCLES'; scene.cycles.samples=48
scene.render.resolution_x=1100; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
scene.world.color=(.16,.16,.16)
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(DOC/'grenade_preview.png')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=.25
            area.spaces.active.region_3d.view_location=(0,0,.008)
            area.spaces.active.clip_start=.001
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active=body
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'grenade.blend'))
triangles=sum(sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in parts if ob.type=='MESH')
(DOC/'model_info.json').write_text(json.dumps({'triangles':triangles,'mesh_objects':len([ob for ob in parts if ob.type=='MESH']),
    'units':'meters','height_about_m':.105,'source':'SourceArt/Grenade/grenade.blend','export':'Assets/Resources/Grenade/grenade.fbx'},indent=2))
bpy.ops.render.render(write_still=True)
print('GRENADE_COMPLETE',triangles)
