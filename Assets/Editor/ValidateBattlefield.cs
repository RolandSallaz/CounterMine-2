using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ValidateBattlefield
{
    static ValidateBattlefield()=>EditorApplication.delayCall+=()=>
    {
        if(!File.Exists("Temp/validate-battlefield.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete("Temp/validate-battlefield.request");Run();
    };
    [MenuItem("Tools/CounterMine/Validate Battlefield Map")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        Directory.CreateDirectory("Documentation/Battlefield");
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        NavMeshData data=null;NavMeshDataInstance handle=default;
        try
        {
            var sources=new List<NavMeshBuildSource>();var bounds=BotNavigation.Collect(scene,sources);
            var settings=BotNavigation.Settings();data=NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("Navigation build failed");handle=NavMesh.AddNavMeshData(data);
            int checks=0;
            var goals=new List<Vector3>{new Vector3(0,0,36),new Vector3(0,0,-36),new Vector3(-24,0,0),new Vector3(24,0,0)};
            for(int level=1;level<=5;level++)goals.Add(new Vector3(6,level*4,0));
            foreach(int side in new[]{-1,1}){goals.Add(new Vector3(side*34+5,8,23));goals.Add(new Vector3(side*34+5,4,-23));}
            foreach(var route in scene.GetRootGameObjects().Where(r=>r.activeInHierarchy).SelectMany(r=>r.GetComponentsInChildren<BotPatrolRoute>()))goals.AddRange(route.WorldPoints);
            goals=goals.Distinct().ToList();
            foreach(var spawn in scene.GetRootGameObjects().Where(r=>r.activeInHierarchy).SelectMany(r=>r.GetComponentsInChildren<TeamSpawnPoint>()))
            {
                var filter=new NavMeshQueryFilter{agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas&~(1<<(spawn.Team==1?4:3))};
                if(!NavMesh.SamplePosition(spawn.transform.position,out var start,3,filter))throw new Exception("Missing base "+spawn.Team);
                foreach(var goal in goals)
                {
                    if(!NavMesh.SamplePosition(goal,out var end,.8f,filter))throw new Exception("Missing floor "+goal);
                    var path=new NavMeshPath();
                    if(!NavMesh.CalculatePath(start.position,end.position,filter,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Unreachable floor team "+spawn.Team+" -> "+goal+" "+path.status);
                    checks++;
                }
            }
            Capture(scene);
            File.WriteAllText("Documentation/Battlefield/validation.txt",$"PASS: {checks} complete paths from both team bases to all 5 central floors, four side roofs, both outer flanks and avenue routes. {sources.Count} navigation sources.\nSaved-scene Unity navigation and renders; live PvP match not run.\n");
        }
        catch(Exception e){File.WriteAllText("Documentation/Battlefield/validation.txt","FAIL "+e);Debug.LogException(e);Capture(scene);}
        finally{if(handle.valid)handle.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Capture(Scene scene)
    {
        var go=new GameObject("Battlefield preview camera");SceneManager.MoveGameObjectToScene(go,scene);
        var camera=go.AddComponent<Camera>();camera.scene=scene;camera.farClipPlane=400;camera.fieldOfView=52;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.20f,.24f);
        var target=new RenderTexture(1600,1000,24);camera.targetTexture=target;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            var views=new[]{new Vector3(95,85,-100),new Vector3(-49,1.7f,-3),new Vector3(20,12,-24)};
            var focus=new[]{new Vector3(0,4,0),new Vector3(0,11,0),new Vector3(0,9,0)};
            for(int i=0;i<views.Length;i++)
            {
                camera.transform.position=views[i];camera.transform.LookAt(focus[i]);camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes("Documentation/Battlefield/view-"+i+".png",image.EncodeToPNG());
            }
        }
        finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);}
    }
}
