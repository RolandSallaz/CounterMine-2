using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class ValidateConquest
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    [MenuItem("Tools/CounterMine/Validate Conquest and Bolts")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var report=new StringBuilder();
        foreach(var name in new[]{"Player","Bot"})
        {
            var root=PrefabUtility.LoadPrefabContents("Assets/Resources/"+name+".prefab");
            try
            {
                typeof(PlayerHealth).GetMethod("Awake",Private).Invoke(root.GetComponent<PlayerHealth>(),null);
                typeof(WeaponAmmo).GetMethod("Awake",Private).Invoke(root.GetComponent<WeaponAmmo>(),null);
                var sync=root.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                foreach(string id in new[]{"ak74","ucp","ak74","ucp"})
                {
                    Check(sync.EquipWeapon(id),"Cannot equip "+id);sync.RestartIdle();
                    var type=typeof(WeaponIdleSynchronizer);
                    var bone=(Transform)type.GetField("bolt",Private).GetValue(sync);
                    Check(bone!=null,"Missing bolt/slide: "+id);
                    Vector3 rest=bone.position;
                    sync.PlayShotBolt();
                    type.GetField("boltShotAt",Private).SetValue(sync,Time.timeAsDouble-.022);
                    type.GetMethod("EvaluatePair",Private).Invoke(sync,new object[]{0d});
                    Vector3 movement=bone.position-rest;
                    var entry=(WeaponIdleSynchronizer.WeaponEntry)type.GetField("currentWeapon",Private).GetValue(sync);
                    Check(movement.magnitude>entry.boltTravel*.8f&&movement.magnitude<entry.boltTravel*1.02f,"Incorrect bolt travel: "+id+" "+movement.magnitude);
                    Check(Vector3.Dot(movement.normalized,-entry.muzzle.forward)>.99f,"Bolt moves off the barrel axis: "+id);
                    type.GetField("boltShotAt",Private).SetValue(sync,Time.timeAsDouble-1);
                    type.GetMethod("EvaluatePair",Private).Invoke(sync,new object[]{0d});
                    Check(Vector3.Distance(bone.position,rest)<.0001f,"Bolt drift: "+id);
                    sync.PlayShotBolt();Check(sync.PlayWeaponAction("reload"),"Missing reload");
                    Check(double.IsNegativeInfinity((double)type.GetField("boltShotAt",Private).GetValue(sync)),"Reload does not cancel firing bolt");
                    sync.RestartIdle();
                    report.AppendLine("PASS "+name+" "+id+": native bone, world-space backward travel, return, reload cancellation.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var scene=EditorSceneManager.NewPreviewScene();NavMeshData data=null;NavMeshDataInstance handle=default;
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/maps/map1/BattlefieldArena/BattlefieldArena.prefab");
            PrefabUtility.InstantiatePrefab(prefab,scene);
            var sites=new[]{new Vector3(-28,0,23),new Vector3(4,4,0),new Vector3(28,0,-23)};
            var sources=new List<NavMeshBuildSource>();var bounds=BotNavigation.Collect(scene,sources);
            var settings=BotNavigation.Settings();data=NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
            Check(data!=null,"Cannot build navigation");handle=NavMesh.AddNavMeshData(data);
            foreach(int side in new[]{-1,1})
            {
                Check(NavMesh.SamplePosition(new Vector3(side*56,0,0),out var start,3,NavMesh.AllAreas),"Base missing from navigation");
                foreach(var point in sites)
                {
                    Check(NavMesh.SamplePosition(point,out var end,.8f,NavMesh.AllAreas),"Capture floor missing: "+point);
                    var path=new NavMeshPath();
                    Check(NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"Capture point unreachable: "+point);
                }
            }
            var site=new ConquestMatch.Site { position=sites[1] };
            Check(site.Contains(sites[1])&&!site.Contains(sites[1]+Vector3.up*4)&&!site.Contains(sites[1]-Vector3.up*4),"Capture crosses floors");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/VFX/CaptureRing.shader");
            Check(shader!=null&&!ShaderUtil.ShaderHasError(shader),"Capture ring shader import failed");
            var variants=new ShaderVariantCollection();variants.Add(new ShaderVariantCollection.ShaderVariant(shader,UnityEngine.Rendering.PassType.Normal));variants.WarmUp();
            Check(!ShaderUtil.ShaderHasError(shader),"Capture ring compilation failed");
            report.AppendLine("PASS: six navigation paths from both bases to A/B/C, floor isolation, ring shader warmup.");
        }
        finally { if(handle.valid)handle.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);EditorSceneManager.ClosePreviewScene(scene); }
        report.AppendLine("Native prefab/navigation validation. Live multi-client Photon match not exercised.");
        Directory.CreateDirectory("Documentation/Conquest");File.WriteAllText("Documentation/Conquest/native-validation.txt",report.ToString());
        Debug.Log(report.ToString());
    }
}
