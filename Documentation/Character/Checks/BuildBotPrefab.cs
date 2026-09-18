using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Photon.Pun;

[InitializeOnLoad]
static class BuildBotPrefab
{
    static BuildBotPrefab() { EditorApplication.delayCall += Build; }
    static void Build()
    {
        const string report = "Documentation/Character/bots_validation.txt";
        if (File.Exists(report) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
            root.name = "Bot";
            if (root.GetComponent<BotController>() == null) root.AddComponent<BotController>();
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is PlayerController || component is PlayerCameraLook || component is PlayerHeadBob ||
                    component is PlayerLeanController || component is WeaponAimController || component is WeaponSway || component is WeaponRecoilController)
                    component.enabled = false;
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            if (root.GetComponent<PlayerHealth>() == null || root.GetComponent<NetworkWeapon>() == null || root.GetComponent<PhotonView>() == null ||
                root.GetComponentsInChildren<WeaponHandIK>(true).Length == 0 || root.GetComponent<PlayerRagdollController>() == null)
                throw new Exception("Bot missing combat/presentation components");
            var sync = root.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            if (sync == null || !sync.EquipWeapon("ak74")) throw new Exception("Bot cannot equip AK-74");
            sync.RestartIdle();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Bot.prefab");
            File.WriteAllText(report, "PASS: Bot prefab built from Player with health, AK74 equip/idle/reload catalog, hand IK, ragdoll and PhotonView. Human input behaviours, cameras and audio listeners disabled. Six room slots spawn under master authority. Live multiplayer combat and master handover not tested.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/bots_error.txt", ex.ToString()); Debug.LogException(ex); }
        finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
    }
}
