using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class ValidateBotNavigation
{
    static ValidateBotNavigation() => EditorApplication.delayCall += () =>
    {
        if (!File.Exists("Temp/validate-bots.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/validate-bots.request"); Run();
    };
    [MenuItem("Tools/CounterMine/Validate Bot Routes")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        NavMeshData data = null; NavMeshDataInstance handle = default;
        Directory.CreateDirectory("Documentation/Bots");
        try
        {
            var sources = new List<NavMeshBuildSource>();
            var bounds = BotNavigation.Collect(scene,sources);
            var settings = BotNavigation.Settings();
            data = NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
            if (data == null) throw new Exception("No navigation data built");
            handle = NavMesh.AddNavMeshData(data);
            var goals = new[] {new Vector3(0,6,0),new Vector3(0,3.5f,5),new Vector3(0,3.5f,-5),
                new Vector3(13.9f,0,-4),new Vector3(-13.9f,0,4),new Vector3(-30,0,-11.8f),new Vector3(30,0,11.8f)};
            var route=scene.GetRootGameObjects().Where(r=>r.activeInHierarchy).SelectMany(r=>r.GetComponentsInChildren<BotPatrolRoute>()).FirstOrDefault();
            if(route!=null)goals=route.WorldPoints;
            var spawns=scene.GetRootGameObjects().Where(r=>r.activeInHierarchy).SelectMany(r=>r.GetComponentsInChildren<TeamSpawnPoint>()).ToArray();
            int checks = 0;
            foreach (int team in new[] {1,2})
            {
                var filter=new NavMeshQueryFilter {agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas & ~(1<<(team==1?4:3))};
                Vector3 origin=spawns.First(s=>s.Team==team).transform.position;
                if (!NavMesh.SamplePosition(origin,out var start,3,filter)) throw new Exception("Own spawn absent from navigation: "+team);
                foreach (var goal in goals)
                {
                    if (!NavMesh.SamplePosition(goal,out var end,.8f,filter)) throw new Exception("Missing goal: "+goal);
                    var path=new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position,end.position,filter,path) || path.status!=NavMeshPathStatus.PathComplete)
                        throw new Exception("Unreachable goal: team "+team+" -> "+goal+" status="+path.status);
                    checks++;
                }
                var enemy=spawns.First(s=>s.Team!=team).transform.position;
                if (NavMesh.SamplePosition(enemy,out _,3,filter)) throw new Exception("Enemy sanctuary remains walkable: "+team);
                checks++;
            }
            ValidateController(settings.agentTypeID,scene);
            File.WriteAllText("Documentation/Bots/navigation-validation.txt",$"PASS: {checks} Unity NavMesh checks, {sources.Count} geometry sources.\nBoth actual elevated scene spawn markers project onto navigation; all six slots have connected candidates for each team (12 slot checks).\nBoth team spawns reach bridge, both roofs, cargo courts and flanks; enemy safe zones excluded.\nReal Bot prefab loads, initializes native path in Awake, calculates patrol route and recreates path after simulated script reload.\nEditor validation; full physics spawn clearance and live Photon match not run.\n");
        }
        catch (Exception e) {File.WriteAllText("Documentation/Bots/navigation-validation.txt","FAIL: "+e);Debug.LogException(e);}
        finally
        {
            if(handle.valid)handle.Remove();
            if(data!=null)UnityEngine.Object.DestroyImmediate(data);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void ValidateController(int agentType,UnityEngine.SceneManagement.Scene scene)
    {
        GameObject bot=null, navigationObject=null;
        string nativeError=null;
        void Log(string message,string trace,LogType type)
        {
            if((type==LogType.Error||type==LogType.Exception) && message.Contains("InitializeNavMeshPath"))nativeError=message;
        }
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        Application.logMessageReceived+=Log;
        try
        {
            bot=PrefabUtility.LoadPrefabContents("Assets/Resources/Bot.prefab");
            if(nativeError!=null)throw new Exception(nativeError);
            var controller=bot.GetComponent<BotController>();
            var serialized=new SerializedObject(controller);
            foreach(var name in new[]{"patrolSpeed","combatSpeed","sprintSpeed","fireRange","memorySeconds"})
                if(serialized.FindProperty(name).floatValue<=0)throw new Exception("Bot prefab has zero "+name);
            typeof(BotController).GetMethod("Awake",flags).Invoke(controller,null);
            var field=typeof(BotController).GetField("path",flags);
            if(field.GetValue(controller)==null)throw new Exception("Awake did not create path");
            navigationObject=new GameObject("Navigation regression check");
            var navigation=navigationObject.AddComponent<BotNavigation>();
            typeof(BotNavigation).GetProperty("Ready").SetValue(navigation,true);
            typeof(BotNavigation).GetProperty("AgentType").SetValue(navigation,agentType);
            foreach(var root in scene.GetRootGameObjects())
            foreach(var spawn in root.GetComponentsInChildren<TeamSpawnPoint>())
            {
                if(!navigation.TryGetSpawnAnchor(spawn.transform.position,spawn.Team,out var anchor))
                    throw new Exception("Actual elevated scene spawn cannot reach ground: "+spawn.transform.position);
                int candidates=0;
                for(int slot=0;slot<6;slot++)
                {
                    bool found=false;
                    for(int attempt=0;attempt<24;attempt++)
                    {
                        float angle=(slot*60f+attempt*37f)*Mathf.Deg2Rad;
                        Vector3 probe=anchor+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(5f+attempt*.4f);
                        if(navigation.Sample(probe,spawn.Team,.6f,out var point)&&navigation.CanReach(anchor,point,spawn.Team)) {found=true;break;}
                    }
                    if(!found)throw new Exception("No connected candidate for team "+spawn.Team+" slot "+slot);
                    candidates++;
                }
                if(candidates!=6)throw new Exception("Missing spawn slots");
            }
            foreach(int team in new[]{1,2})
            {
                var spawns=scene.GetRootGameObjects().Where(r=>r.activeInHierarchy).SelectMany(r=>r.GetComponentsInChildren<TeamSpawnPoint>()).ToArray();
                if(!navigation.TryGetSpawnAnchor(spawns.First(s=>s.Team==team).transform.position,team,out var basePoint))throw new Exception("Missing base anchor");
                if(!navigation.CanReach(basePoint,new Vector3(6,20,0),team))throw new Exception("Spawn connectivity rejects tower");
                if(navigation.Sample(spawns.First(s=>s.Team!=team).transform.position,team,3,out _))throw new Exception("Enemy sanctuary accessible");
            }
            typeof(BotController).GetField("navigation",flags).SetValue(controller,navigation);
            // An uninstantiated room bot defaults to team 2.
            if(!navigation.Sample(new Vector3(43,0,0),2,1,out var origin))throw new Exception("Missing bot spawn");
            bot.transform.position=origin;
            var setDestination=typeof(BotController).GetMethod("SetDestination",flags);
            if(!(bool)setDestination.Invoke(controller,new object[]{new Vector3(6,20,0)}))throw new Exception("Bot controller cannot route to tower");
            field.SetValue(controller,null);
            if(!(bool)setDestination.Invoke(controller,new object[]{new Vector3(-24,0,0)}))throw new Exception("Bot cannot recreate path after script reload");
            typeof(PlayerRagdollController).GetMethod("Awake",flags).Invoke(bot.GetComponent<PlayerRagdollController>(),null);
            var capsule=bot.GetComponent<CharacterController>();
            var move=typeof(BotController).GetMethod("MoveAlongPath",flags);
            foreach(var mode in new[]{"patrol","combat","sprint"})
            {
                capsule.enabled=false;bot.transform.position=origin+Vector3.up*.08f;capsule.enabled=true;
                typeof(BotController).GetField("vertical",flags).SetValue(controller,0f);
                typeof(BotController).GetField("visible",flags).SetValue(controller,mode=="combat");
                var goal=mode=="sprint"?new Vector3(6,20,0):origin+Vector3.left*3;
                if(!(bool)setDestination.Invoke(controller,new object[]{goal}))throw new Exception("Missing movement test route: "+mode);
                var before=bot.transform.position;
                for(int step=0;step<10;step++)move.Invoke(controller,new object[]{1f/60f});
                var distance=Vector3.ProjectOnPlane(bot.transform.position-before,Vector3.up).magnitude;
                if(distance<.15f)throw new Exception("CharacterController is stationary in "+mode+": "+distance);
            }
            if(nativeError!=null)throw new Exception(nativeError);
        }
        finally
        {
            Application.logMessageReceived-=Log;
            if(bot!=null)PrefabUtility.UnloadPrefabContents(bot);
            if(navigationObject!=null)UnityEngine.Object.DestroyImmediate(navigationObject);
        }
    }
}
