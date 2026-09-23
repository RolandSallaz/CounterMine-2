using System;
using System.IO;
using System.Linq;
using UnityEditor;
using Animancer;
using System.Reflection;
using UnityEngine;

[InitializeOnLoad]
public static class InstallNewWeapons
{
    static InstallNewWeapons() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists("Temp/install-new-weapons.request") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/install-new-weapons.request");
        try { Run(); } catch (Exception e) { Directory.CreateDirectory("Documentation/NewWeapons"); File.WriteAllText("Documentation/NewWeapons/error.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install New Weapons")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/NewWeapons");
        AssetDatabase.Refresh();
        foreach (string name in new[] { "HK416", "L115A3" }) PrepareModel(name);
        Install("Assets/Resources/Player.prefab");
        var catalog=AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/ShopCatalog.asset");
        var items=catalog.items.ToList();
        foreach(var item in ShopCatalog.Defaults().Where(i=>i.id=="hk416" || i.id=="l115a3"))
            if(!items.Any(i=>i.id==item.id))items.Add(item);
        catalog.items=items.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        InstallDeathShop.Run();ValidateNewWeapons.Run();ValidateMilkor.Run();
    }
    const string Folder="Assets/Resources/NewWeapons/";
    static Material Paint(string name,Color color,float metal,float smooth)
    {
        Directory.CreateDirectory(Folder);var path=Folder+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",smooth);EditorUtility.SetDirty(m);return m;
    }
    static void PrepareModel(string name)
    {
        var steel=Paint("Gunmetal",new Color(.12f,.14f,.15f),.72f,.38f);
        var dark=Paint("Polymer",new Color(.055f,.06f,.064f),.02f,.23f);
        var tan=Paint("Sand",new Color(.42f,.36f,.23f),.15f,.28f);
        var alloy=Paint("Machined Steel",new Color(.28f,.3f,.32f),.8f,.48f);
        var brass=Paint("Brass",new Color(.58f,.39f,.14f),.8f,.45f);
        var copper=Paint("Copper",new Color(.46f,.22f,.12f),.65f,.35f);
        var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/models/"+name+".fbx");
        importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.optimizeGameObjects=false;
        // Use original FBX material identifiers so repeated installation stays deterministic.
        int[] indices=name=="HK416"?new[]{0,1,2,3,4,5,6,7,8,9,10,11}:new[]{4,5,6,7,8,9,27,28,29,30,38,39,40,41,42,43,44,46};
        foreach(int n in indices)
        {
            Material target=steel;
            if(name=="HK416")
            {if(n==3)target=tan;else if(n==4||n==5)target=dark;else if(n==7||n==8||n==10)target=alloy;else if(n==11)target=brass;else if(n==9)target=copper;}
            else
            {
                if(n==4)target=tan;else if(n==30||n==38||n==46)target=dark;else if(n==6||n==7||n==9||n==28||n==44)target=alloy;else if(n==29)target=brass;else if(n==27)target=copper;
                else if(n==39)
                {
                    var path=Folder+"Scope Glass.mat";target=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(target==null){target=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Milkor/Collimator.shader"));target.SetFloat("_Brightness",0);target.SetColor("_GlassColor",new Color(.12f,.25f,.28f,.04f));AssetDatabase.CreateAsset(target,path);}
                }
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),n==0?"Material":"Material."+n.ToString("000")),target);
        }
        importer.SaveAndReimport();
    }
    static void Set(UnityEngine.Object target,string name,UnityEngine.Object value)
    {var so=new SerializedObject(target);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
    public static WeaponIdleSynchronizer.WeaponEntry[] Entries(WeaponIdleSynchronizer sync) =>
        (WeaponIdleSynchronizer.WeaponEntry[])typeof(WeaponIdleSynchronizer).GetField("weapons",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sync);
    static void Install(string path)
    {
        var player=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);var entries=Entries(sync).ToList();var ak=entries.First(e=>e.id=="ak74");var parent=ak.animator.transform.parent;
            var character=sync.CharacterAnimator.gameObject;var bones=character.GetComponentsInChildren<Transform>(true);
            var positions=bones.Select(t=>t.localPosition).ToArray();var rotations=bones.Select(t=>t.localRotation).ToArray();var scales=bones.Select(t=>t.localScale).ToArray();
            ak.characterIdle.SampleAnimation(character,0);
            var ik=new SerializedObject(player.GetComponentInChildren<WeaponHandIK>(true));var rightHand=(Transform)ik.FindProperty("rightHand").objectReferenceValue;var leftHand=(Transform)ik.FindProperty("leftHand").objectReferenceValue;
            foreach(bool sniper in new[]{false,true})
            {
                string id=sniper?"l115a3":"hk416",name=sniper?"L115A3":"HK416";
                var old=parent.Find(name+"_Weapon");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                var weapon=new GameObject(name+"_Weapon");weapon.transform.SetParent(parent,false);weapon.transform.rotation=player.transform.rotation;
                var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/models/"+name+".fbx"),weapon.transform,false);
                model.name="Model";model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.Euler(0,90,0);model.transform.localScale=Vector3.one*(sniper?.262f:.15f);
                new GameObject("IdleClock").transform.SetParent(weapon.transform,false);
                var animator=weapon.AddComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var animancer=weapon.AddComponent<AnimancerComponent>();animancer.Animator=animator;
                var handlingPath=Folder+name+" Handling.asset";var handling=AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>(handlingPath);
                if(handling==null){handling=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/Ak74/AK74_Handling.asset"));handling.ergonomics=sniper?25:65;handling.aimedSensitivity=sniper?.24f:.65f;handling.aimedMovementSpeed=sniper?.42f:.65f;AssetDatabase.CreateAsset(handling,handlingPath);}
                Transform Marker(string label,Vector3 raw,Quaternion rotation)
                {var t=new GameObject(label).transform;t.SetParent(weapon.transform,false);t.SetPositionAndRotation(model.transform.TransformPoint(raw),rotation);return t;}
                var right=Marker("RightGrip",sniper?new Vector3(.99f,.10f,0):new Vector3(.76f,-.20f,0),rightHand.rotation);
                var left=Marker("LeftGrip",sniper?new Vector3(-.47f,.13f,0):new Vector3(-1.35f,.19f,0),leftHand.rotation);
                var muzzle=Marker("Muzzle",sniper?new Vector3(-2.59f,.3573f,0):new Vector3(-3.10f,.314f,0),player.transform.rotation);
                var sight=Marker("Sight",sniper?new Vector3(1.215f,.57454f,0):new Vector3(.65f,.886f,0),player.transform.rotation).gameObject.AddComponent<WeaponSight>();
                var sightSO=new SerializedObject(sight);sightSO.FindProperty("eyeRelief").floatValue=sniper?.08f:.10f;sightSO.FindProperty("aimedFieldOfView").floatValue=sniper?20:60;sightSO.ApplyModifiedPropertiesWithoutUndo();
                var rig=weapon.AddComponent<WeaponAimRig>();Set(rig,"handling",handling);Set(rig,"defaultSight",sight);
                weapon.transform.position+=rightHand.position-right.position;
                var magazine=weapon.AddComponent<WeaponMagazineMotion>();magazine.source=sync;magazine.magazine=model.GetComponentsInChildren<Transform>(true).First(t=>t.name=="mag");magazine.leftGrip=left;
                if(sniper)
                {
                    InstallPiPScope.Configure(weapon,model.transform,sight);
                }
                var audioPath="Assets/Resources/Audio/Weapons/"+id+".asset";var audio=AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>(audioPath);
                if(audio==null){audio=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/ak74.asset"));audio.displayName=name;audio.shotVolume=sniper?1:.72f;audio.shotRange=sniper?140:95;AssetDatabase.CreateAsset(audio,audioPath);}
                var shot=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+name+" Shot.wav");if(shot!=null){audio.shots=new[]{shot};EditorUtility.SetDirty(audio);}
                var idlePath=Folder+name+" Idle.anim";var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(idlePath);
                if(idle==null){idle=new AnimationClip{name=name+" Idle"};idle.SetCurve("IdleClock",typeof(Transform),"localPosition.x",AnimationCurve.Constant(0,1,0));AssetDatabase.CreateAsset(idle,idlePath);}
                var recoilAnimation=ak.recoil?.animation;
                if(recoilAnimation==null)recoilAnimation=(Kinemation.Recoilly.RecoilAnimData)new SerializedObject(player.GetComponentInChildren<WeaponRecoilController>(true)).FindProperty("recoilProfile").objectReferenceValue;
                var entry=new WeaponIdleSynchronizer.WeaponEntry {
                    id=id,animator=animancer,aimRig=rig,audioProfile=audio,muzzle=muzzle,leftGrip=left,rightGrip=right,maximumMuzzleReach=sniper?1.6f:1f,
                    characterIdle=ak.characterIdle,weaponIdle=idle,magazineSize=sniper?5:30,roundsPerMinute=sniper?45:800,automatic=!sniper,damage=sniper?100:30,
                    proceduralEquipSeconds=sniper?.72f:.48f,proceduralReloadSeconds=sniper?3.2f:2.45f,recoilKick=sniper?2.3f:.8f,
                    boltTravel=sniper?.1f:.035f,boltCycleSeconds=sniper?1.2f:.075f,boltBoneName=sniper?"boltmove":"bolt",
                    recoil=new WeaponRecoilController.Tuning{animation=recoilAnimation,cameraPitch=sniper?new Vector2(1.3f,1.7f):new Vector2(.24f,.38f),cameraYaw=.16f,heatPerShot=.1f,heatRecovery=.9f,sustainedFireMultiplier=1.3f,hipSpread=sniper?3.5f:1.1f,heatSpread=2,moveSpread=sniper?5:3,airSpread=4,crouchSpreadMultiplier=.6f}
                };
                int index=entries.FindIndex(e=>e.id==id);if(index<0)entries.Add(entry);else entries[index]=entry;
                foreach(var t in weapon.GetComponentsInChildren<Transform>(true))t.gameObject.layer=parent.gameObject.layer;
                weapon.SetActive(false);
            }
            for(int i=0;i<bones.Length;i++){bones[i].SetLocalPositionAndRotation(positions[i],rotations[i]);bones[i].localScale=scales[i];}
            typeof(WeaponIdleSynchronizer).GetField("weapons",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sync,entries.ToArray());EditorUtility.SetDirty(sync);
            PrefabUtility.SaveAsPrefabAsset(player,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(player);}
    }
    public static void Render(Camera camera,string path)
    {
        var rt=new RenderTexture(1280,720,24);var old=RenderTexture.active;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.19f);camera.aspect=1280f/720;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
    }
}
