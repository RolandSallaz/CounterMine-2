using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Animancer;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InstallRshWinchester
{
    const string Folder="Assets/Resources/NewWeapons/";
    static InstallRshWinchester()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/install-rsh-winchester.request"))return;
        File.Delete("Temp/install-rsh-winchester.request");
        try{Run();File.WriteAllText("Documentation/NewWeapons/rsh-winchester-install.txt","PASS installation");}
        catch(Exception e){File.WriteAllText("Documentation/NewWeapons/rsh-winchester-install.txt",e.ToString());Debug.LogException(e);}
    }
    static void Set(UnityEngine.Object o,string key,UnityEngine.Object value){var so=new SerializedObject(o);so.FindProperty(key).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    static Material Paint(string name,Color color,float metal)
    {
        string path=Folder+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(m);return m;
    }
    [MenuItem("Tools/CounterMine/Install RSH-12 and Winchester")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/NewWeapons");
        var steel=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Gunmetal.mat");
        var wood=Paint("Walnut",new Color(.20f,.075f,.026f),0);
        foreach(string name in new[]{"RSH 12","Winchester Model 1897"})
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/models/"+name+".fbx");
            importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.optimizeGameObjects=false;
            foreach(int index in (name=="RSH 12"?new[]{0,1,2,3,4,5,6,7,8,9}:new[]{0,2,5,6,7,8,9}))
            {
                Material material=steel;
                if(name=="Winchester Model 1897" && index==0)material=wood;
                if(name=="RSH 12" && index==5)material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Polymer.mat");
                if((name=="Winchester Model 1897" && index==7)||(name=="RSH 12" && index==1))material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Machined Steel.mat");
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),index==0?"Material":"Material."+index.ToString("000")),material);
            }
            importer.SaveAndReimport();
        }
        Install("Assets/Resources/Player.prefab");Install("Assets/Resources/Bot.prefab");
        var catalog=AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/ShopCatalog.asset");var items=catalog.items.ToList();
        foreach(var item in ShopCatalog.Defaults().Where(i=>i.id=="rsh12"||i.id=="winchester1897")){int index=items.FindIndex(i=>i.id==item.id);if(index<0)items.Add(item);else items[index]=item;}
        catalog.items=items.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
    }
    static void Install(string path)
    {
        var player=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);var entries=InstallNewWeapons.Entries(sync).ToList();var ak=entries.First(e=>e.id=="ak74");
            var parent=ak.animator.transform.parent;var character=sync.CharacterAnimator.gameObject;
            var bones=character.GetComponentsInChildren<Transform>(true);var positions=bones.Select(t=>t.localPosition).ToArray();var rotations=bones.Select(t=>t.localRotation).ToArray();var scales=bones.Select(t=>t.localScale).ToArray();
            foreach(bool revolver in new[]{true,false})
            {
                string id=revolver?"rsh12":"winchester1897",name=revolver?"RSH 12":"Winchester Model 1897";
                for(int i=0;i<bones.Length;i++){if(bones[i]==null)continue;bones[i].SetLocalPositionAndRotation(positions[i],rotations[i]);bones[i].localScale=scales[i];}
                var template=entries.First(e=>e.id==(revolver?"ucp":"ak74"));template.characterIdle.SampleAnimation(character,0);
                var ik=new SerializedObject(player.GetComponentInChildren<WeaponHandIK>(true));var rh=(Transform)ik.FindProperty("rightHand").objectReferenceValue;var lh=(Transform)ik.FindProperty("leftHand").objectReferenceValue;
                var old=parent.Find(id+"_Weapon");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                var weapon=new GameObject(id+"_Weapon");weapon.transform.SetParent(parent,false);weapon.transform.rotation=player.transform.rotation;
                var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/models/"+name+".fbx"),weapon.transform,false);model.name="Model";
                model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.Euler(0,90,0);model.transform.localScale=Vector3.one*(revolver?1f:.105f);
                foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())r.updateWhenOffscreen=true;
                new GameObject("IdleClock").transform.SetParent(weapon.transform,false);
                var animator=weapon.AddComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var animancer=weapon.AddComponent<AnimancerComponent>();animancer.Animator=animator;
                Transform Marker(string label,Vector3 raw,Quaternion rotation){var t=new GameObject(label).transform;t.SetParent(weapon.transform,false);t.SetPositionAndRotation(model.transform.TransformPoint(raw),rotation);return t;}
                var right=Marker("RightGrip",revolver?new Vector3(.12f,-.026f,0):new Vector3(1.12f,.15f,0),rh.rotation);
                var left=Marker("LeftGrip",revolver?new Vector3(.10f,-.043f,.025f):new Vector3(-1.95f,-.16f,0),lh.rotation);
                var muzzle=Marker("Muzzle",revolver?new Vector3(-.223f,.0447f,0):new Vector3(-4.87f,.55f,0),player.transform.rotation);
                var sight=Marker("Sight",revolver?new Vector3(.10f,.095f,0):new Vector3(.90f,.90f,0),player.transform.rotation*Quaternion.Euler(revolver?0:1.72f,0,0)).gameObject.AddComponent<WeaponSight>();
                var sightSO=new SerializedObject(sight);sightSO.FindProperty("eyeRelief").floatValue=revolver?.24f:.13f;sightSO.FindProperty("aimedFieldOfView").floatValue=60;sightSO.ApplyModifiedPropertiesWithoutUndo();
                string hp=Folder+id+" Handling.asset";var handling=AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>(hp);if(handling==null){handling=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/Ak74/AK74_Handling.asset"));handling.ergonomics=revolver?55:40;handling.aimedSensitivity=.65f;AssetDatabase.CreateAsset(handling,hp);}
                if(revolver)
                {
                    var pistolHandling=AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/UCP/UCP_Handling.asset");
                    handling.sprintPosition=pistolHandling.sprintPosition;
                    handling.sprintRotation=pistolHandling.sprintRotation;
                    handling.sprintBlendTime=pistolHandling.sprintBlendTime;
                    handling.sprintBob=pistolHandling.sprintBob;
                    handling.sprintBobFrequency=pistolHandling.sprintBobFrequency;
                    EditorUtility.SetDirty(handling);
                }
                var rig=weapon.AddComponent<WeaponAimRig>();Set(rig,"handling",handling);Set(rig,"defaultSight",sight);weapon.transform.position+=rh.position-right.position+player.transform.TransformDirection(revolver?new Vector3(.05f,-.02f,.025f):new Vector3(.10f,-.015f,.10f));
                var motion=weapon.AddComponent<WeaponManualAction>();motion.source=sync;motion.revolver=revolver;motion.leftGrip=left;
                Transform Bone(string n)=>model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==n);
                motion.cylinder=Bone("cylinder");motion.cylinderArm=Bone("cylinderarm");motion.pump=revolver?null:Bone("bolt");
                string ap="Assets/Resources/Audio/Weapons/"+id+".asset";var audio=AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>(ap);if(audio==null){audio=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/ak74.asset"));audio.displayName=revolver?"RSH-12":"Winchester 1897";audio.shotVolume=.95f;audio.shotRange=100;AssetDatabase.CreateAsset(audio,ap);}
                var reload=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+id+" Reload.wav");if(reload!=null){audio.reload=reload;EditorUtility.SetDirty(audio);}
                var shot=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+id+" Shot.wav");if(shot!=null){audio.shots=new[]{shot};EditorUtility.SetDirty(audio);}
                string cp=Folder+id+" Idle.anim";var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(cp);if(idle==null){idle=new AnimationClip{name=id+" Idle"};idle.SetCurve("IdleClock",typeof(Transform),"localPosition.x",AnimationCurve.Constant(0,1,0));AssetDatabase.CreateAsset(idle,cp);}
                var recoilAnimation=template.recoil?.animation;if(recoilAnimation==null)recoilAnimation=(Kinemation.Recoilly.RecoilAnimData)new SerializedObject(player.GetComponentInChildren<WeaponRecoilController>(true)).FindProperty("recoilProfile").objectReferenceValue;
                var entry=new WeaponIdleSynchronizer.WeaponEntry{id=id,animator=animancer,aimRig=rig,audioProfile=audio,muzzle=muzzle,leftGrip=left,rightGrip=right,maximumMuzzleReach=1.6f,
                    characterIdle=template.characterIdle,weaponIdle=idle,magazineSize=5,roundsPerMinute=revolver?150:80,automatic=false,damage=revolver?65:13,
                    pelletCount=revolver?1:8,pelletSpreadDegrees=revolver?0:3.2f,muzzleVelocity=revolver?300:380,bulletGravity=9.81f,fullDamageRange=revolver?30:10,maximumRange=revolver?150:70,minimumDamageFraction=revolver?.5f:.15f,
                    proceduralEquipSeconds=revolver?.55f:.65f,proceduralReloadSeconds=revolver?3.4f:4.2f,recoilKick=revolver?2f:1.8f,boltBoneName="__manual_action__",boltTravel=0,
                    recoil=new WeaponRecoilController.Tuning{animation=recoilAnimation,rotateAroundGrip=revolver,cameraPitch=revolver?new Vector2(1.1f,1.5f):new Vector2(.9f,1.3f),cameraYaw=.2f,heatPerShot=.2f,heatRecovery=.9f,sustainedFireMultiplier=1.3f,hipSpread=revolver?1.5f:1,heatSpread=1,moveSpread=2,airSpread=3,crouchSpreadMultiplier=.7f}};
                TuneWeaponRecoil.Apply(entry);
                int index=entries.FindIndex(e=>e.id==id);if(index<0)entries.Add(entry);else entries[index]=entry;
                foreach(var t in weapon.GetComponentsInChildren<Transform>(true))t.gameObject.layer=parent.gameObject.layer;
                weapon.SetActive(false);
                if(path.Contains("Player"))typeof(InstallDeathShop).GetMethod("RenderIcon",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{weapon,idle,id});
            }
            for(int i=0;i<bones.Length;i++){if(bones[i]==null)continue;bones[i].SetLocalPositionAndRotation(positions[i],rotations[i]);bones[i].localScale=scales[i];}
            typeof(WeaponIdleSynchronizer).GetField("weapons",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sync,entries.ToArray());EditorUtility.SetDirty(sync);PrefabUtility.SaveAsPrefabAsset(player,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
    }
}
