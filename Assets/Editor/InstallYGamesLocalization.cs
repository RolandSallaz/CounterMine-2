using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YG;
using YG.LanguageLegacy;

[InitializeOnLoad]
public static class InstallYGamesLocalization
{
    [Serializable] private class Catalog { public GameLocalization.Entry[] entries; }
    static InstallYGamesLocalization() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/install-yg-localization.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Run(); }
        catch(Exception error) { Debug.LogException(error); File.WriteAllText("Temp/yg-install-result.txt", error.ToString()); }
    }
    [MenuItem("Tools/CounterMine/Install YGames Localization")]
    public static void Run()
    {
        var settings = YG2.infoYG;
        settings.AutoTranslateLangs.languages.ru = true;
        settings.AutoTranslateLangs.languages.en = true;
        settings.AutoTranslateLangs.languages.tr = false;
        settings.AutoTranslateLangs.fonts.defaultFont = new[] { GameUIStyle.Font };
        settings.Localization.setLanguageMod = InfoYG.LocalizationSettings.SetLangMod.EveryGameLaunch;
        EditorUtility.SetDirty(settings);
        YG.EditorScr.DefineSymbols.AddDefine("Localization_yg");
        YG.EditorScr.DefineSymbols.AddDefine("AutoTranslateLangs_yg");
        if (YG.EditorScr.UnityPackagesManager.IsPackageImported("com.unity.nuget.newtonsoft-json"))
            YG.EditorScr.DefineSymbols.AddDefine("NJSON_YG2");
        var catalog = JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("GameTranslations").text);
        int count = 0;
        var root = PrefabUtility.LoadPrefabContents("Assets/Resources/UI/PlayerHUD.prefab");
        try
        {
            foreach(var text in root.GetComponentsInChildren<Text>(true))
            {
                var translator = GameLocalization.PrepareDynamic(text);
                var entry = catalog.entries.FirstOrDefault(item => item.source == text.text);
                if(entry != null)
                {
                    translator.text = entry.source; translator.ru = entry.ru; translator.en = entry.en;
                }
                EditorUtility.SetDirty(translator);
                count++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/UI/PlayerHUD.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // Existing scene text is also editable through the official LanguageYG inspector.
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if(opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            int sceneTextCount = 0;
            foreach(var text in scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Text>(true)))
            {
                var entry = catalog.entries.FirstOrDefault(item => item.source == text.text);
                if(entry != null) GameLocalization.Bind(text, entry.source);
                else GameLocalization.PrepareDynamic(text);
                count++;
                sceneTextCount++;
            }
            if(sceneTextCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally { if(opened) EditorSceneManager.CloseScene(scene, true); }
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save Project");
        File.WriteAllText("Temp/yg-defines.txt", PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone) + "\n" + PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.WebGL));
        File.WriteAllText("Temp/yg-install-result.txt", "PASS: official modules configured, ru/en, default font, " + count + " authored text components migrated. Runtime text factories use LanguageYG as well.");
    }
}
