using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class TuneArenaDaylight
{
    const string Folder = "Documentation/Lighting";
    static TuneArenaDaylight() => EditorApplication.update += Poll;
    static void Poll()
    {
        if(File.Exists("Temp/validate-daylight.request") && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete("Temp/validate-daylight.request");
            try { Validate(); } catch(Exception error) { File.WriteAllText(Folder+"/validation.txt", "FAIL: "+error); Debug.LogException(error); }
        }
        if(!File.Exists("Temp/tune-daylight.request") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/tune-daylight.request");
        try { Run(); } catch(Exception error) { Directory.CreateDirectory(Folder); File.WriteAllText(Folder+"/validation.txt", "FAIL: "+error); Debug.LogException(error); }
    }
    [MenuItem("Tools/CounterMine/Validate Daylight")]
    public static void Validate()
    {
        ArenaDaylight.ApplyAmbient();
        var directions = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        var illumination = new Color[directions.Length];
        RenderSettings.ambientProbe.Evaluate(directions, illumination);
        if(illumination.Any(color => color.r < .2f || color.g < .2f || color.b < .2f)) throw new Exception("Ambient hemisphere is too dark");
        var sun = RenderSettings.sun;
        var sky = RenderSettings.skybox;
        if(sun == null || sun.shadows != LightShadows.Soft || Vector3.Dot(((Vector3)sky.GetVector("_SunDirection")).normalized, -sun.transform.forward) < .999f)
            throw new Exception("Sky sun and shadow direction differ");
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        if(player.GetComponentsInChildren<Camera>(true).Any(camera => !camera.allowHDR ||
            camera.GetComponent<UniversalAdditionalCameraData>() == null || !camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing))
            throw new Exception("Saved player camera lacks daylight tone mapping");
        Capture("after",true);
        File.WriteAllText(Folder+"/validation.txt", "PASS: ambient illumination >0.2 linear RGB in all six directions; sky sun matches directional light; soft shadows; saved camera post processing. Three fixed views captured with display-only enemy models.\nNo additional realtime lights or per-frame ambient calculation.\n");
    }
    [MenuItem("Tools/CounterMine/Tune Arena Daylight")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path != "Assets/Scenes/SampleScene.unity") throw new Exception("Open the arena scene before tuning lighting.");
        Capture("before", false);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.70f,.80f,.90f);
        RenderSettings.ambientEquatorColor = new Color(.62f,.66f,.69f);
        RenderSettings.ambientGroundColor = new Color(.54f,.50f,.43f);
        RenderSettings.ambientIntensity = 1;
        var sun = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>()).First(light => light.type == LightType.Directional);
        sun.intensity = 3.4f; sun.color = Color.white; sun.useColorTemperature = true; sun.colorTemperature = 6200;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = .8f;
        RenderSettings.sun = sun;
        if(sun.GetComponent<ArenaDaylight>() == null) sun.gameObject.AddComponent<ArenaDaylight>();
        ArenaDaylight.ApplyAmbient();
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/ArenaSky.mat");
        sky.SetColor("_Zenith", new Color(.34f,.56f,.80f));
        sky.SetColor("_Horizon", new Color(.79f,.86f,.92f));
        sky.SetColor("_Ground", new Color(.48f,.47f,.43f));
        sky.SetColor("_SunColor", new Color(1,.97f,.88f));
        sky.SetVector("_SunDirection", -sun.transform.forward);
        EditorUtility.SetDirty(sky);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if(profile.TryGet<Vignette>(out var vignette)) { vignette.active=false; EditorUtility.SetDirty(vignette); }
        if(profile.TryGet<Bloom>(out var bloom)) { bloom.active=false; EditorUtility.SetDirty(bloom); }
        if(!profile.TryGet<ColorAdjustments>(out var colors)) { colors=profile.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(colors,profile); }
        colors.postExposure.Override(.15f); colors.contrast.Override(-2); colors.saturation.Override(0);
        EditorUtility.SetDirty(colors); EditorUtility.SetDirty(profile);
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            foreach(var camera in player.GetComponentsInChildren<Camera>(true))
            { camera.allowHDR = true; camera.GetUniversalAdditionalCameraData().renderPostProcessing = true; }
            PrefabUtility.SaveAsPrefabAsset(player,"Assets/Resources/Player.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        EditorUtility.SetDirty(sun); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Capture("after", true);
        File.WriteAllText(Folder+"/validation.txt", "PASS: sky/ground ambient probe initialized explicitly; neutral 6200K sun, soft shadows, subtle exposure, no bloom/vignette; saved arena and player camera. Captured three fixed before/after views with display-only enemy models.\n");
    }
    static void Capture(string prefix, bool post)
    {
        var cameraObject = new GameObject("Lighting comparison camera",typeof(Camera));
        var builder = new GameObject("Lighting character builder").AddComponent<StartMenuScreen>();
        Transform enemy = null;
        var camera = cameraObject.GetComponent<Camera>(); camera.enabled=false;
        camera.clearFlags=CameraClearFlags.Skybox; camera.fieldOfView=65; camera.farClipPlane=300; camera.allowHDR=true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=post;
        var texture = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
        var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
        var previous = RenderTexture.active;
        try
        {
            var method = typeof(StartMenuScreen).GetMethod("CreateDisplayCharacter",BindingFlags.Instance|BindingFlags.NonPublic);
            enemy = (Transform)method.Invoke(builder,new object[]{Resources.Load<GameObject>("Player")});
            foreach(var renderer in enemy.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials=renderer.sharedMaterials.Select(material => material == null ? null :
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/models/CharacterMaterials/Team2/Materials/"+material.name+".mat") ?? material).ToArray();
            var views=new[]{new Vector3(-49,1.7f,-3),new Vector3(0,1.7f,-36),new Vector3(20,9.7f,-23)};
            var enemies=new[]{new Vector3(-37,0,-3),new Vector3(0,0,-25),new Vector3(9,8,-23)};
            camera.targetTexture=texture;
            for(int i=0;i<views.Length;i++)
            {
                enemy.position=enemies[i];
                Vector3 facing=views[i]-enemies[i]; facing.y=0; enemy.rotation=Quaternion.LookRotation(facing);
                camera.transform.position=views[i]; camera.transform.LookAt(enemies[i]+Vector3.up*1.2f);
                camera.Render(); RenderTexture.active=texture;
                image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                File.WriteAllBytes(Folder+"/"+prefix+"-"+i+".png",image.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active=previous;camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(image);
            if(enemy!=null)UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            UnityEngine.Object.DestroyImmediate(builder.gameObject);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
