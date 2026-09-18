using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ValidateRagdollGround
{
    static ValidateRagdollGround() => EditorApplication.delayCall += Auto;
    private static void Auto()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists("Documentation/Character/ragdoll_ground_validation.txt")) Run();
    }
    [MenuItem("Tools/CounterMine/Validate Ragdoll Ground")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var report = new StringBuilder();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            if (scene.GetPhysicsScene() == Physics.defaultPhysicsScene)
            {
                File.WriteAllText("Documentation/Character/ragdoll_ground_skipped.txt", "This editor version does not give preview scenes isolated physics. Simulation skipped to avoid moving objects in the user's scene. A Play Mode physics fixture is required.");
                return;
            }
            var floor = new GameObject("Thin Floor"); SceneManager.MoveGameObjectToScene(floor, scene);
            var ground = floor.AddComponent<BoxCollider>(); ground.size = new Vector3(40, .04f, 40);
            ground.center = new Vector3(0, -.02f, 0);
            foreach (string prefab in new[] { "Player", "Bot" })
            foreach (Vector3 force in new[] { Vector3.forward * 4, Vector3.right * 4, Vector3.back * 10 })
            {
                var player = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(prefab));
                SceneManager.MoveGameObjectToScene(player, scene);
                try
                {
                    player.transform.position = Vector3.up * .12f;
                    var rag = player.GetComponent<PlayerRagdollController>();
                    rag.EnterRagdoll(force, player.transform.position + Vector3.up * 1.4f);
                    var bodies = rag.SkeletonRoot.GetComponentsInChildren<Rigidbody>();
                    foreach (var body in bodies)
                    {
                        if (body.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative || body.solverIterations < 12)
                            throw new Exception("Ragdoll stability settings missing: " + body.name);
                    }
                    var physics = scene.GetPhysicsScene();
                    float minimum = float.PositiveInfinity;
                    for (int frame = 0; frame < 400; frame++)
                    {
                        physics.Simulate(.02f);
                        foreach (var body in bodies)
                        {
                            minimum = Mathf.Min(minimum, body.worldCenterOfMass.y);
                            if (!BulletHitUtility.IsFinite(body.position) || body.worldCenterOfMass.y < -.04f)
                                throw new Exception(prefab + " body crossed thin floor: " + body.name + " at frame " + frame + " y=" + body.worldCenterOfMass.y);
                        }
                    }
                    report.AppendLine(prefab + " force=" + force + " minimum body center=" + minimum.ToString("F4"));
                }
                finally { UnityEngine.Object.DestroyImmediate(player); }
            }
            if (Resources.Load<Sprite>("UI/Icons/Radar") == null) throw new Exception("Radar icon not imported as Sprite");
            PreviewSkillCards.Run();
            report.AppendLine("PASS: six 8-second falls on a 4cm floor; no body center below -4cm; icon imported. Does not verify the live map or network corpses.");
            File.WriteAllText("Documentation/Character/ragdoll_ground_validation.txt", report.ToString());
        }
        catch (Exception error)
        {
            File.WriteAllText("Documentation/Character/ragdoll_ground_error.txt", report + error.ToString()); Debug.LogException(error);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
