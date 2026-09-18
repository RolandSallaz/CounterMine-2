using UnityEditor;
using UnityEngine;

/// <summary>Keep this authored prop's FBX materials compatible with the project's URP renderer.</summary>
public sealed class GrenadeModelImporter : AssetPostprocessor
{
    private bool IsGrenade => assetPath == "Assets/Resources/Grenade/grenade.fbx";
    private void OnPreprocessModel()
    {
        if (!IsGrenade) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1;
        importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
    private void OnPostprocessMaterial(Material material)
    {
        if (!IsGrenade) return;
        Color color = material.color;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return;
        material.shader = shader;
        material.SetColor("_BaseColor", color);
        bool metal = material.name.Contains("Steel") || material.name.Contains("Lever");
        material.SetFloat("_Metallic", metal ? .7f : 0f);
        material.SetFloat("_Smoothness", metal ? .55f : .3f);
    }
}
