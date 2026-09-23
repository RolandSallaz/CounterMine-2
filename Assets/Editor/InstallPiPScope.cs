using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class InstallPiPScope
{
    const string Folder = "Assets/Resources/NewWeapons/";
    static InstallPiPScope() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists("Temp/install-pip-scope.request") || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/install-pip-scope.request");
        try { Run(); } catch (Exception e) { File.WriteAllText("Documentation/NewWeapons/pip-validation.txt", "FAIL " + e); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install and Validate PiP Scope")]
    public static void Run()
    {
        AssetDatabase.Refresh();
        var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var weapon = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "L115A3_Weapon");
            Configure(weapon.gameObject, weapon.Find("Model"), weapon.GetComponentInChildren<WeaponSight>(true));
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Resources/Player.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        ValidatePiPScope.Run();
    }
    public static void Configure(GameObject weapon, Transform model, WeaponSight sight)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/VFX/ScopePictureInPicture.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("PiP shader compilation failed.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Scope PiP.mat");
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, Folder + "Scope PiP.mat"); }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "Scope PiP Lens.asset");
        if (mesh == null)
        {
            mesh = new Mesh { name = "L115A3 Eyepiece" };
            var vertices = new Vector3[9]; var uv = new Vector2[9]; var triangles = new int[24]; uv[0] = Vector2.one * .5f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .0161f;
                uv[i + 1] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .5f + Vector2.one * .5f;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % 8 + 1; triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Folder + "Scope PiP Lens.asset");
        }
        var lens = weapon.transform.Find("Scope PiP Lens");
        if (lens == null) { lens = new GameObject("Scope PiP Lens", typeof(MeshFilter), typeof(MeshRenderer)).transform; lens.SetParent(weapon.transform, false); }
        lens.localRotation = Quaternion.identity; lens.localScale = Vector3.one;
        lens.position = model.TransformPoint(new Vector3(1.201f, .57454f, 0));
        lens.gameObject.layer = weapon.layer; lens.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = lens.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off; renderer.enabled = false;
        var scope = weapon.GetComponent<ScopedSightView>() ?? weapon.AddComponent<ScopedSightView>(); scope.lensRenderer = renderer; scope.lensRadius = .0161f;
        var settings = new SerializedObject(sight);
        settings.FindProperty("preservePeripheralFieldOfView").boolValue = true;
        settings.FindProperty("eyeRelief").floatValue = .05f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        sight.transform.position = lens.position;
        EditorUtility.SetDirty(scope); EditorUtility.SetDirty(sight);
    }
}
