"""Run in Blender background mode. Reads the source without saving over it."""
import bpy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Anims/UCP/Exported'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Assets/Anims/UCP_Actions.blend'))
if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
character, weapon = bpy.data.objects['Root'], bpy.data.objects['UCP']
scene = bpy.context.scene
metadata = {'fps': scene.render.fps, 'actions': {}}
for kind in ['Idle','Equip','Reload']:
    frames=[]
    for rig, suffix in [(character,'Character'),(weapon,'Weapon')]:
        action=bpy.data.actions['UCP_'+kind+'_'+suffix]
        rig.animation_data.action=action
        rig.animation_data.action_slot=action.slots[0]
        frames += [k.co.x for layer in action.layers for strip in layer.strips
                   for bag in strip.channelbags for curve in bag.fcurves for k in curve.keyframe_points]
    assert frames, 'No authored keys: '+kind
    start,end=int(min(frames)),int(max(frames))
    if start==end:end=start+1
    scene.frame_start=start;scene.frame_end=end;scene.frame_set(start)
    bpy.context.view_layer.update()
    metadata['actions'][kind]={'start':start,'end':end,'seconds':(end-start)/scene.render.fps}
    for rig,suffix in [(character,'Character'),(weapon,'Weapon')]:
        bpy.ops.object.select_all(action='DESELECT')
        rig.hide_set(False);rig.select_set(True)
        for child in rig.children_recursive:
            if child.type=='MESH':child.hide_set(False);child.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(OUT/('UCP_'+kind+'_'+suffix+'.fbx')),
            use_selection=True,object_types={'ARMATURE','MESH'},global_scale=1,
            apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
            axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
            use_armature_deform_only=False,armature_nodetype='NULL',
            bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=False,bake_anim_force_startend_keying=True,
            bake_anim_step=1,bake_anim_simplify_factor=0,path_mode='AUTO')
        print('EXPORTED',kind,suffix,start,end)
(OUT/'export.json').write_text(json.dumps(metadata,indent=2))
