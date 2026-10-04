using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TuneWinchesterPresentation
{
    static TuneWinchesterPresentation() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/tune-winchester.request")) return;
        File.Delete("Temp/tune-winchester.request");
        Run();
    }
    [MenuItem("Tools/CounterMine/Tune Winchester Sight")]
    public static void Run()
    {
        foreach(string prefab in new[]{"Player","Bot"})
        {
            string path="Assets/Resources/"+prefab+".prefab";
            var player=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var weapon=player.GetComponentsInChildren<Transform>(true).First(t=>t.name=="winchester1897_Weapon");
                InstallRshWinchester.ConfigureWinchesterSight(weapon.Find("Model"),weapon.Find("Sight").GetComponent<WeaponSight>());
                PrefabUtility.SaveAsPrefabAsset(player,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(player);}
        }
        Inspect();
    }
    static void Inspect()
    {
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var weapon = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "winchester1897_Weapon");
            var model = weapon.Find("Model");
            weapon.gameObject.SetActive(true);
            foreach(var renderer in player.GetComponentsInChildren<Renderer>(true))
                renderer.enabled=renderer.transform.IsChildOf(model);
            var report = new StringBuilder();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh(); skin.BakeMesh(mesh);
                var vertices = mesh.vertices.Select(v => model.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                report.AppendLine(skin.name + " " + mesh.vertexCount);
                var raw=skin.sharedMesh.vertices.Select(v=>model.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                report.AppendLine("Baked bead "+vertices.Where(v=>v.x< -4.1f&&Mathf.Abs(v.z)<.12f).OrderByDescending(v=>v.y).First());
                report.AppendLine("Shared bead "+raw.Where(v=>v.x< -4.1f&&Mathf.Abs(v.z)<.12f).OrderByDescending(v=>v.y).FirstOrDefault());
                foreach (var group in vertices.GroupBy(v=>Mathf.Round(v.x*10)/10).OrderBy(g=>g.Key))
                    report.AppendLine("x="+group.Key+" top="+group.Max(v=>v.y));
                Object.DestroyImmediate(mesh);
            }
            File.WriteAllText("Documentation/NewWeapons/winchester-geometry.txt",report.ToString());
            var camera = new GameObject("Winchester side camera").AddComponent<Camera>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,player.scene);
            camera.scene=player.scene; camera.orthographic=true;camera.orthographicSize=.25f;
            camera.transform.position=model.TransformPoint(new Vector3(-1,1,9));
            camera.transform.LookAt(model.TransformPoint(new Vector3(-1,.5f,0)),model.up);
            var light=new GameObject("Geometry light").AddComponent<Light>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject,player.scene);
            light.type=LightType.Directional;light.intensity=3;light.transform.eulerAngles=new Vector3(30,-30,0);
            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/winchester-side.png");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }
}
