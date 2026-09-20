using System;
using System.IO;
using System.Linq;
using System.Text;
using Animancer;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InstallUCP
{
    const string Folder = "Assets/Anims/UCP/";
    static InstallUCP() => EditorApplication.delayCall += () =>
    {
        if (!File.Exists("Temp/install-ucp.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/install-ucp.request");
        try { Run(); } catch(Exception e) { File.WriteAllText("Temp/ucp-install-error.txt",e.ToString()); Debug.LogException(e); }
    };
    [MenuItem("Tools/CounterMine/Install UCP")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var report = new StringBuilder();
        foreach (var action in new[]{"Idle","Equip","Reload"})
        foreach (var part in new[]{"Character","Weapon"})
        {
            var name = "UCP_"+action+"_"+part;
            var path = Folder+"Exported/"+name+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.animationCompression=ModelImporterAnimationCompression.Off;
            importer.importAnimation=true;
            importer.optimizeGameObjects=false;
            importer.SaveAndReimport();
            var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
            var clip=UnityEngine.Object.Instantiate(source); clip.name=name;
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=action=="Idle";
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            var dest=Folder+name+".anim";
            var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(dest);
            if(old==null)AssetDatabase.CreateAsset(clip,dest); else {EditorUtility.CopySerialized(clip,old);UnityEngine.Object.DestroyImmediate(clip);clip=old;}
            report.AppendLine(name+" duration="+clip.length+" bindings="+AnimationUtility.GetCurveBindings(clip).Length);
        }
        Install("Assets/Resources/Player.prefab",report);
        Install("Assets/Resources/Bot.prefab",report);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Documentation/UCP");
        File.WriteAllText("Documentation/UCP/import.txt",report.ToString());
        if(File.Exists("Temp/ucp-install-error.txt"))File.Delete("Temp/ucp-install-error.txt");
        ValidateUCP.Run();
    }
    static void Set(UnityEngine.Object obj,string name,UnityEngine.Object value)
    {
        var so=new SerializedObject(obj);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Install(string path,StringBuilder report)
    {
        var audio=AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/ucp.asset");
        if(audio==null)
        {
            audio=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/ak74.asset"));
            audio.name="ucp";audio.displayName="HK UCP";audio.shotVolume=.65f;
            AssetDatabase.CreateAsset(audio,"Assets/Resources/Audio/Weapons/ucp.asset");
        }
        var handling=AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>(Folder+"UCP_Handling.asset");
        if(handling==null)
        {
            handling=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/Ak74/AK74_Handling.asset"));
            handling.name="UCP_Handling";handling.ergonomics=80;
            handling.sprintPosition=new Vector3(.035f,.005f,-.025f);
            handling.sprintRotation=new Vector3(-58f,6f,-8f);
            handling.sprintBob=.008f;
            AssetDatabase.CreateAsset(handling,Folder+"UCP_Handling.asset");
        }
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            if(root.GetComponent<WeaponAmmo>()==null)root.AddComponent<WeaponAmmo>();
            var sync=root.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            Set(root.GetComponent<WeaponAmmo>(),"weaponAnimation",sync);
            Set(root.GetComponent<WeaponAmmo>(),"health",root.GetComponent<PlayerHealth>());
            var so=new SerializedObject(sync);var catalog=so.FindProperty("weapons");
            var ak=catalog.GetArrayElementAtIndex(0);
            ak.FindPropertyRelative("magazineSize").intValue=30;
            ak.FindPropertyRelative("roundsPerMinute").floatValue=600;
            ak.FindPropertyRelative("automatic").boolValue=true;
            ak.FindPropertyRelative("damage").intValue=34;
            var akKick=ak.FindPropertyRelative("recoilKick"); if(akKick!=null) akKick.floatValue=1f;
            var parent=((AnimancerComponent)ak.FindPropertyRelative("animator").objectReferenceValue).transform.parent;
            var previous=parent.Find("UCP_Weapon");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var weapon=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Exported/UCP_Idle_Weapon.fbx"),parent,false);
            weapon.name="UCP_Weapon";
            var animator=weapon.GetComponent<Animator>();
            if(animator==null)animator=weapon.AddComponent<Animator>();
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var animancer=weapon.AddComponent<AnimancerComponent>();animancer.Animator=animator;
            Clip("Idle","Weapon").SampleAnimation(weapon,0);
            var body=weapon.transform.Find("UCP/body");
            var barrel=body.Find("barrel");var end=barrel.Find("barrel_end");
            var forward=(end.position-barrel.position).normalized;
            var up=Vector3.ProjectOnPlane(parent.up,forward).normalized;
            Transform Marker(string name,Vector3 position,Quaternion rotation)
            {
                var t=new GameObject(name).transform;t.SetParent(body,false);t.SetPositionAndRotation(position,rotation);return t;
            }
            var muzzle=Marker("Muzzle",end.position,Quaternion.LookRotation(forward,up));
            var sight=Marker("IronSight",barrel.position-forward*.10f+up*.015f,muzzle.rotation).gameObject.AddComponent<WeaponSight>();
            var rig=weapon.AddComponent<WeaponAimRig>();Set(rig,"handling",handling);Set(rig,"defaultSight",sight);
            var character=sync.CharacterAnimator.gameObject;
            var transforms=character.GetComponentsInChildren<Transform>(true);
            var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
            Clip("Idle","Character").SampleAnimation(character,0);
            var ik=new SerializedObject(root.GetComponentInChildren<WeaponHandIK>(true));
            var left=(Transform)ik.FindProperty("leftHand").objectReferenceValue;var right=(Transform)ik.FindProperty("rightHand").objectReferenceValue;
            var leftGrip=Marker("LeftGrip",left.position,left.rotation);var rightGrip=Marker("RightGrip",right.position,right.rotation);
            for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}
            foreach(var renderer in weapon.GetComponentsInChildren<Renderer>(true))
            {
                report.AppendLine("Material "+string.Join(",",renderer.sharedMaterials.Select(m=>m==null?"NULL":m.name+" / "+m.shader.name)));
                renderer.gameObject.layer=parent.gameObject.layer;
            }
            catalog.arraySize=2;var entry=catalog.GetArrayElementAtIndex(1);
            entry.FindPropertyRelative("id").stringValue="ucp";
            entry.FindPropertyRelative("audioProfile").objectReferenceValue=audio;
            entry.FindPropertyRelative("animator").objectReferenceValue=animancer;
            entry.FindPropertyRelative("aimRig").objectReferenceValue=rig;
            entry.FindPropertyRelative("muzzle").objectReferenceValue=muzzle;
            entry.FindPropertyRelative("leftGrip").objectReferenceValue=leftGrip;
            entry.FindPropertyRelative("rightGrip").objectReferenceValue=rightGrip;
            entry.FindPropertyRelative("magazineSize").intValue=20;
            entry.FindPropertyRelative("roundsPerMinute").floatValue=400;
            entry.FindPropertyRelative("automatic").boolValue=false;
            entry.FindPropertyRelative("damage").intValue=28;
            var kickProp=entry.FindPropertyRelative("recoilKick"); if(kickProp!=null) kickProp.floatValue=2.2f;
            entry.FindPropertyRelative("characterIdle").objectReferenceValue=Clip("Idle","Character");
            entry.FindPropertyRelative("weaponIdle").objectReferenceValue=Clip("Idle","Weapon");
            entry.FindPropertyRelative("characterEquip").objectReferenceValue=Clip("Equip","Character");
            entry.FindPropertyRelative("weaponEquip").objectReferenceValue=Clip("Equip","Weapon");
            var actions=entry.FindPropertyRelative("actions");actions.arraySize=1;var reload=actions.GetArrayElementAtIndex(0);
            reload.FindPropertyRelative("id").stringValue="reload";
            reload.FindPropertyRelative("characterClip").objectReferenceValue=Clip("Reload","Character");
            reload.FindPropertyRelative("weaponClip").objectReferenceValue=Clip("Reload","Weapon");
            so.ApplyModifiedPropertiesWithoutUndo();weapon.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,path);
            report.AppendLine("INSTALLED "+path);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }
    static AnimationClip Clip(string action,string part)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"UCP_"+action+"_"+part+".anim");
}
