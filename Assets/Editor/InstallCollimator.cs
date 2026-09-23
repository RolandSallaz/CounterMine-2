using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class InstallCollimator
{
    private const string Folder = "Assets/Resources/Milkor/";
    static InstallCollimator() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists("Temp/install-collimator.request") || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete("Temp/install-collimator.request");
        try { Run(); }
        catch (Exception e) { File.WriteAllText("Documentation/Milkor/collimator-validation.txt", "FAIL " + e); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install and Validate Milkor Collimator")]
    public static void Run()
    {
        AssetDatabase.Refresh();
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var weapon = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "Milkor_Weapon");
            Configure(weapon, weapon.Find("Model"), weapon.GetComponentInChildren<WeaponSight>(true));
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Resources/Player.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        Validate();
    }
    public static void Configure(Transform weapon, Transform model, WeaponSight sight)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "Collimator.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Collimator shader is missing or failed compilation.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Collimator.mat");
        if (material == null)
        {
            material = new Material(shader) { name = "Milkor Collimator" };
            AssetDatabase.CreateAsset(material, Folder + "Collimator.mat");
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "Collimator Lens.asset");
        if (mesh == null)
        {
            // The FBX eyepiece is an eight-sided open tube. Match its measured aperture.
            mesh = new Mesh { name = "Milkor Collimator Lens" };
            var vertices = new Vector3[9]; var triangles = new int[24];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .0094f;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % 8 + 1; triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Folder + "Collimator Lens.asset");
        }
        var lens = weapon.Find("Collimator Lens");
        if (lens == null) { lens = new GameObject("Collimator Lens", typeof(MeshFilter), typeof(MeshRenderer)).transform; lens.SetParent(weapon, false); }
        lens.localRotation = Quaternion.identity; lens.localScale = Vector3.one;
        lens.position = model.TransformPoint(new Vector3(-.59f, 2.087386f, 0));
        lens.gameObject.layer = weapon.gameObject.layer;
        lens.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = lens.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        sight.transform.position = lens.position;
        sight.transform.localRotation = Quaternion.identity;
        EditorUtility.SetDirty(sight);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Validate()
    {
        Directory.CreateDirectory("Documentation/Milkor");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        var report = new System.Text.StringBuilder();
        try
        {
            var source = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "Milkor_Weapon");
            var copy = UnityEngine.Object.Instantiate(source.gameObject); copy.SetActive(true);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, scene);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lens = copy.transform.Find("Collimator Lens");
            var shader = lens.GetComponent<Renderer>().sharedMaterial.shader;
            Check(!ShaderUtil.ShaderHasError(shader) && shader.isSupported, "Shader compile/support");
            Check(Vector3.Distance(lens.position, copy.GetComponentInChildren<WeaponSight>().AimPoint.position) < .0001f, "ADS axis misaligned");
            var go = new GameObject("Optic validation camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.scene = scene; camera.nearClipPlane = .001f; camera.farClipPlane = 10; camera.fieldOfView = 35;
            camera.transform.rotation = lens.rotation;
            var light = new GameObject("Optic key"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, scene);
            light.AddComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 2;
            light.transform.rotation = Quaternion.Euler(25,-30,0);
            camera.transform.position = lens.position - lens.forward * .08f;
            int center = Render(camera, "collimator-centered.png"); Check(center > 8, "Reticle missing at centered eye: " + center);
            camera.transform.position += lens.right * .003f;
            int shifted = Render(camera, "collimator-offset.png"); Check(shifted > 8, "Reticle missing after small eye shift");
            camera.transform.position = lens.position - lens.forward * .08f + lens.right * .03f;
            Check(Render(camera, "collimator-outside.png") == 0, "Reticle escaped lens aperture");
            camera.transform.position = lens.position + lens.forward * .12f; camera.transform.rotation = Quaternion.LookRotation(-lens.forward, lens.up);
            Check(Render(camera, "collimator-front.png") == 0, "Reticle visible from muzzle side");
            camera.transform.position = lens.position - lens.forward * .08f; camera.transform.rotation = lens.rotation;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(block, scene);
            block.transform.position = lens.position - lens.forward * .035f; block.transform.localScale = new Vector3(.08f,.08f,.005f);
            Check(Render(camera, "collimator-occluded.png") == 0, "Reticle draws through opaque geometry");
            RenderADS(player);
            report.AppendLine("PASS URP shader compilation, lens/ADS alignment and player-camera ADS preview, centered and shifted eye, aperture clipping, front-side hiding and depth occlusion.");
            report.AppendLine("Reticle pixels: centered " + center + ", shifted " + shifted + ". Editor rendering; WebGL runtime was not run.");
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(player); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllText("Documentation/Milkor/collimator-validation.txt", report.ToString());
    }
    private static void RenderADS(GameObject player)
    {
        void Call(object component, string method, params object[] args) => component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, args);
        var aim = player.GetComponentInChildren<WeaponAimController>(true);
        var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
        Call(player.GetComponent<PlayerHealth>(), "Awake");
        Call(player.GetComponent<WeaponAmmo>(), "Awake");
        Call(aim, "Awake");
        Check(sync.EquipWeapon("milkor"), "Player Milkor equip"); sync.RestartIdle();
        player.GetComponent<PlayerModelPresentation>().ConfigureView(true);
        Call(aim, "Step", true, 1f); Call(aim, "ApplyPose", 1f);
        foreach (var ik in player.GetComponentsInChildren<WeaponHandIK>(true)) Call(ik, "LateUpdate");
        var camera = player.GetComponentInChildren<Camera>(true); camera.scene = player.scene;
        var lens = sync.WeaponRoot.Find("Collimator Lens");
        var screen = camera.WorldToViewportPoint(lens.position);
        Check(Mathf.Abs(screen.x - .5f) < .001f && Mathf.Abs(screen.y - .5f) < .001f, "Player ADS lens not centered");
        Check(Render(camera, "collimator-ads.png") > 4, "Player ADS reticle missing");
    }
    private static int Render(Camera camera, string filename)
    {
        var rt = new RenderTexture(960,720,24); var previous = RenderTexture.active;
        var image = new Texture2D(960,720,TextureFormat.RGB24,false);
        try
        {
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.22f,.28f,.34f);
            camera.targetTexture = rt; camera.aspect = 960f/720; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply();
            File.WriteAllBytes("Documentation/Milkor/" + filename, image.EncodeToPNG());
            return image.GetPixels32().Count(c => c.r > 150 && c.r > c.g * 2 && c.r > c.b * 2);
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); }
    }
}
