using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ValidateRadarSpawn
{
    static ValidateRadarSpawn() => EditorApplication.delayCall += Auto;
    private static void Auto()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists("Documentation/Character/radar_spawn_validation.txt")) Run();
    }
    [MenuItem("Tools/CounterMine/Validate Radar Spawn")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject player = null, proxy = null;
        Mesh baked = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents("Assets/Resources/Bot.prefab");
            var model = player.GetComponent<PlayerModelPresentation>(); model.ConfigureView(false);
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var sources = player.GetComponent<PlayerRagdollController>().SkeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (sources.Length == 0) throw new Exception("Bot has no skinned geometry");
            proxy = new GameObject("Radar Test Proxy");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(proxy, player.scene);
            baked = new Mesh();
            var bake = typeof(RadarSkill).GetMethod("BakeSilhouette", BindingFlags.Static | BindingFlags.NonPublic);
            int checks = 0; float worst = 0;
            foreach (Vector3 spawn in new[] { Vector3.zero, new Vector3(90, 3, -70), new Vector3(-120, 0, 140) })
            foreach (string action in new[] { "idle", "equip", "reload" })
            {
                player.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0, 73, 0));
                sync.ApplyNetworkState("ak74", action, Time.timeAsDouble - .1, 1);
                typeof(WeaponIdleSynchronizer).GetMethod("Update", flags).Invoke(sync, null);
                foreach (var source in sources)
                {
                    bake.Invoke(null, new object[] { source, baked, proxy.transform });
                    foreach (var vertex in baked.vertices)
                    {
                        Vector3 world = proxy.transform.TransformPoint(vertex);
                        float error = Vector3.Distance(world, source.transform.TransformPoint(vertex));
                        worst = Mathf.Max(worst, error);
                        if (!BulletHitUtility.IsFinite(world) || Vector3.Distance(world, spawn) > 4 || error > .002f)
                            throw new Exception("Stretched radar pose: " + source.name + " action=" + action + " spawn=" + spawn + " vertex=" + world);
                    }
                    checks++;
                }
            }
            PreviewSkillCards.Run();
            File.WriteAllText("Documentation/Character/radar_spawn_validation.txt", "PASS: " + checks + " baked mesh poses across idle/equip/reload at three spawn points including far from origin; all vertices within 4m; maximum world transform error " + worst + ".\nLive two-client spawn still needs verification.\n");
        }
        catch (Exception e) { File.WriteAllText("Documentation/Character/radar_spawn_error.txt", e.ToString()); Debug.LogException(e); }
        finally
        {
            if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            if (proxy != null) UnityEngine.Object.DestroyImmediate(proxy);
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
        }
    }
}
