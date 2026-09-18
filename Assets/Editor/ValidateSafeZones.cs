using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ValidateSafeZones
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    [MenuItem("Tools/CounterMine/Validate Safe Zones")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.CreateScene("Safe zone checks");
        TeamSafeZone zone = null;
        try
        {
            var go = new GameObject("Test Zone");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(10000, 10000, 10000);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(8, 4, 8);
            zone = go.AddComponent<TeamSafeZone>();
            Call(zone, "OnEnable");
            Vector3 center = go.transform.position;
            Check(zone.Contains(center), "Zone center must be inside");
            Check(!zone.Contains(center + Vector3.right * 5), "Outside point marked inside");
            go.transform.rotation = Quaternion.Euler(0, 35, 0);
            Check(zone.Contains(go.transform.TransformPoint(new Vector3(3, 1, 3))), "Rotated volume containment");
            go.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            Check(BulletHitUtility.CastCover(center - Vector3.right * 6, Vector3.right, 12, null, ~0, sourceTeam: 2).didHit,
                "Enemy bullet must stop at shield");
            Check(!BulletHitUtility.CastCover(center - Vector3.right * 6, Vector3.right, 12, null, ~0, sourceTeam: 1).didHit,
                "Own team's incoming projectile must pass");
            Check(GrenadeProjectile.IsBlastBlocked(center - Vector3.right * 6, center, 2), "Enemy explosion must be blocked");
            Check(!GrenadeProjectile.IsBlastBlocked(center - Vector3.right * 6, center, 1), "Friendly blast should not hit own shield");
            Check(TeamSafeZone.ContainsAny(center), "Weapons must be blocked inside");
            Check(TeamSafeZone.IsEnemyArea(center, 2) && !TeamSafeZone.IsEnemyArea(center, 1), "Team access rule");
            var actor = new GameObject("Test Controller");
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = center - Vector3.right * 6;
            var controller = actor.AddComponent<CharacterController>();
            controller.radius = .3f;
            controller.height = 1.8f;
            Call(zone, "SetPassage", controller, false);
            Physics.SyncTransforms();
            controller.Move(Vector3.right * 5);
            Check(actor.transform.position.x < center.x - 3.9f, "Enemy controller crossed shield");
            controller.enabled = false;
            actor.transform.position = center - Vector3.right * 6;
            controller.enabled = true;
            Call(zone, "SetPassage", controller, true);
            Physics.SyncTransforms();
            controller.Move(Vector3.right * 5);
            Check(actor.transform.position.x > center.x - 2, "Friendly controller could not enter");
            var material = Resources.Load<Material>("VFX/SafeZoneBarrier");
            Check(material != null && material.shader != null && !ShaderUtil.ShaderHasError(material.shader), "Barrier shader failed");
            Call(zone, "OnDisable");
            Check(!TeamSafeZone.ContainsAny(center), "Disabled zone remains active");
            Directory.CreateDirectory("Documentation/SafeZones");
            File.WriteAllText("Documentation/SafeZones/validation.txt", "PASS: rotated containment; team projectile filtering; blast blocking; weapon-zone detection; enemy movement blocked; friendly passage; disable cleanup; shader compile. Not a two-client gameplay test.\n");
            Debug.Log("Safe zone validation passed.");
        }
        finally
        {
            if (zone != null) Call(zone, "OnDisable");
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
