using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InstallProceduralWeaponMotion
{
    static InstallProceduralWeaponMotion() => EditorApplication.update += Poll;
    static void Poll()
    {
        if(!EditorApplication.isCompiling&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/install-reload-props.request"))
        {File.Delete("Temp/install-reload-props.request");InstallReloadProps();return;}
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/install-procedural-motion.request")) return;
        File.Delete("Temp/install-procedural-motion.request");
        try { Run(); File.WriteAllText("Documentation/NewWeapons/procedural-motion-install.txt","PASS reference and both prefab catalogs installed"); }
        catch(Exception e) { File.WriteAllText("Documentation/NewWeapons/procedural-motion-install.txt",e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install Procedural Weapon Motion")]
    public static void Run()
    {
        AnalyzeAk74Motion.Run();
        const string directory = "Assets/Resources/WeaponMotion";
        if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets/Resources","WeaponMotion");
        const string path = directory + "/AK74 Motion Reference.asset";
        var reference = AssetDatabase.LoadAssetAtPath<WeaponMotionReference>(path);
        if(reference == null) { reference=ScriptableObject.CreateInstance<WeaponMotionReference>(); AssetDatabase.CreateAsset(reference,path); }
        reference.equip=ReadFrames("equip",out reference.equipDuration);
        reference.reload=ReadFrames("reload",out reference.reloadDuration);
        EditorUtility.SetDirty(reference);
        foreach(string prefabPath in new[]{"Assets/Resources/Player.prefab","Assets/Resources/Bot.prefab"})
        {
            var player=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                var entry=InstallNewWeapons.Entries(sync).Single(e=>e.id=="ucp");
                entry.proceduralEquipSeconds=.5f; entry.proceduralReloadSeconds=2.1f; entry.cameraActionScale=.7f;
                var motion=entry.animator.GetComponent<WeaponMagazineMotion>();
                if(motion==null)motion=entry.animator.gameObject.AddComponent<WeaponMagazineMotion>();
                motion.source=sync;motion.leftGrip=entry.leftGrip;
                motion.magazine=entry.animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="mag");
                motion.bolt=entry.animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="slide");
                motion.boltAction=true;
                foreach(var weapon in InstallNewWeapons.Entries(sync))
                {
                    var mechanism=weapon.animator.GetComponent<WeaponMagazineMotion>();
                    if(mechanism!=null)mechanism.rightGrip=weapon.rightGrip;
                    var manual=weapon.animator.GetComponent<WeaponManualAction>();
                    if(manual!=null)
                    {
                        var old=weapon.animator.transform.Find("ReloadRound");
                        if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                        manual.reloadRound=BuildRound(weapon.animator.transform,weapon.id=="winchester1897");
                    }
                }
                EditorUtility.SetDirty(sync);
                PrefabUtility.SaveAsPrefabAsset(player,prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        CopyMissingBotWeapons();
        AssetDatabase.SaveAssets();
    }
    static Transform BuildRound(Transform parent,bool shell)
    {
        var root=new GameObject("ReloadRound").transform;root.SetParent(parent,false);
        foreach(bool cap in new[]{false,true})
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cylinder);part.name=cap?"Brass rim":"Cartridge body";
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());part.transform.SetParent(root,false);
            float radius=shell?.0185f:.0127f,length=shell?.065f:.060f;
            part.transform.localRotation=Quaternion.Euler(90,0,0);part.transform.localScale=new Vector3(radius,cap?.0015f:length*.5f,radius);
            part.transform.localPosition=Vector3.back*(cap?length*.5f:0);
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/NewWeapons/Brass.mat");
            if(shell&&!cap)
            {
                const string path="Assets/Resources/NewWeapons/Reload Shell Polymer.mat";
                material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.SetColor("_BaseColor",new Color(.65f,.035f,.018f));material.SetFloat("_Smoothness",.35f);AssetDatabase.CreateAsset(material,path);}
            }
            part.GetComponent<Renderer>().sharedMaterial=material;
            part.layer=parent.gameObject.layer;
        }
        root.gameObject.layer=parent.gameObject.layer;root.gameObject.SetActive(false);return root;
    }
    [MenuItem("Tools/CounterMine/Install Reload Cartridge Props")]
    public static void InstallReloadProps()
    {
        foreach(string path in new[]{"Assets/Resources/Player.prefab","Assets/Resources/Bot.prefab"})
        {
            var player=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var manual in player.GetComponentsInChildren<WeaponManualAction>(true))
                {
                    var old=manual.transform.Find("ReloadRound");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                    manual.reloadRound=BuildRound(manual.transform,!manual.revolver);
                }
                PrefabUtility.SaveAsPrefabAsset(player,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(player);}
        }
        AssetDatabase.SaveAssets();
    }
    static void CopyMissingBotWeapons()
    {
        var source=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        var bot=PrefabUtility.LoadPrefabContents("Assets/Resources/Bot.prefab");
        try
        {
            var sourceSync=source.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var botSync=bot.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var entries=InstallNewWeapons.Entries(botSync).ToList();
            var parent=entries.First(e=>e.id=="ak74").animator.transform.parent;
            foreach(var entry in InstallNewWeapons.Entries(sourceSync).Where(e=>!entries.Any(b=>b.id==e.id)))
            {
                var root=entry.animator.transform;
                var clone=UnityEngine.Object.Instantiate(root.gameObject,parent,false);
                clone.name=root.name;clone.transform.SetLocalPositionAndRotation(root.localPosition,root.localRotation);clone.transform.localScale=root.localScale;
                UnityEngine.Object Map(UnityEngine.Object value)
                {
                    if(value==sourceSync)return botSync;
                    var transform=value as Transform ?? (value as Component)?.transform;
                    if(transform==null||!transform.IsChildOf(root))return value;
                    string relative=AnimationUtility.CalculateTransformPath(transform,root);
                    var copy=relative.Length==0?clone.transform:clone.transform.Find(relative);
                    return value is Transform ? (UnityEngine.Object)copy : copy.GetComponent(value.GetType());
                }
                foreach(var component in clone.GetComponentsInChildren<Component>(true))
                {
                    if(component==null)continue;
                    var so=new SerializedObject(component);var property=so.GetIterator();
                    while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue==sourceSync)property.objectReferenceValue=botSync;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var copyEntry=(WeaponIdleSynchronizer.WeaponEntry)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(entry,null);
                foreach(var field in typeof(WeaponIdleSynchronizer.WeaponEntry).GetFields())
                    if(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))field.SetValue(copyEntry,Map((UnityEngine.Object)field.GetValue(entry)));
                entries.Add(copyEntry);clone.SetActive(false);
            }
            typeof(WeaponIdleSynchronizer).GetField("weapons",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(botSync,entries.ToArray());
            EditorUtility.SetDirty(botSync);PrefabUtility.SaveAsPrefabAsset(bot,"Assets/Resources/Bot.prefab");
        }
        finally{PrefabUtility.UnloadPrefabContents(bot);PrefabUtility.UnloadPrefabContents(source);}
    }
    static WeaponMotionReference.Frame[] ReadFrames(string action,out float duration)
    {
        var rows=File.ReadAllLines("Documentation/NewWeapons/ak74-"+action+"-frames.csv").Skip(1).Where(s=>s.Length>0)
            .Select(s=>s.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        duration=rows[rows.Length-1][1];
        return rows.Select(r=>new WeaponMotionReference.Frame {position=new Vector3(r[3],r[4],r[5]),rotation=new Vector3(r[6],r[7],r[8]),camera=new Vector3(r[9],r[10],r[11])}).ToArray();
    }
}
