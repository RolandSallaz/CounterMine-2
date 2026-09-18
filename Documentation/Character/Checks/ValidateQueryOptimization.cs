using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
static class ValidateQueryOptimization
{
    const string Report = "Documentation/Character/query_optimization_validation.txt";
    static ValidateQueryOptimization() { EditorApplication.delayCall += Run; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static BulletHitUtility.Hit ReferenceCover(Vector3 origin, Vector3 direction, float range, Transform shooter)
    {
        var result = new BulletHitUtility.Hit { point = origin + direction * range, normal = -direction };
        float nearest = range;
        foreach (var hit in Physics.RaycastAll(origin, direction, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(shooter) || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            result = new BulletHitUtility.Hit { point = hit.point, normal = hit.normal, didHit = true };
        }
        return result;
    }
    public static void BatchRun() { Run(); ValidateSlideCrouch.Run(); }
    public static void Run()
    {
        if (File.Exists(Report) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene previous = SceneManager.GetActiveScene(), scene = default;
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            Vector3 origin = new Vector3(12000, 100, 12000);
            var shooter = new GameObject("Query fixture shooter").transform; shooter.position = origin;
            var walls = new GameObject[80];
            for (int i = 0; i < walls.Length; i++)
            {
                walls[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                walls[i].transform.position = origin + Vector3.forward * (i + 2);
                walls[i].transform.localScale = new Vector3(2, 2, .2f);
                if (i < 4) walls[i].transform.SetParent(shooter, true);
            }
            Physics.SyncTransforms();
            var dense = BulletHitUtility.CastCover(origin, Vector3.forward, 100, shooter, ~0);
            Check(dense.didHit && Mathf.Abs(dense.point.z - origin.z - 5.9f) < .003f, "Full query buffer drops nearest valid wall");
            for (int i = 5; i < walls.Length; i++) walls[i].SetActive(false);
            Physics.SyncTransforms();
            var sparse = BulletHitUtility.CastCover(origin, Vector3.forward, 100, shooter, ~0);
            Check(Vector3.Distance(sparse.point, dense.point) < .001f, "Sparse query differs from dense fallback");
            Check(!BulletHitUtility.CastCover(origin, Vector3.back, 10, shooter, ~0).didHit, "Empty ray reports hit");
            for (int i = 0; i < 100; i++) BulletHitUtility.CastCover(origin, Vector3.forward, 100, shooter, ~0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) BulletHitUtility.CastCover(origin, Vector3.forward, 100, shooter, ~0);
            long optimized = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) ReferenceCover(origin, Vector3.forward, 100, shooter);
            long original = GC.GetAllocatedBytesForCurrentThread() - before;
            File.WriteAllText("Documentation/Character/query_allocations.txt", "optimized=" + optimized + ", original=" + original);
            // Some Unity Mono versions return zero for this counter even for RaycastAll.
            // Do not misreport that as a measured zero-allocation result.
            bool counterAvailable = original > 0;
            if (counterAvailable) Check(optimized < original, "Reusable query does not reduce allocations: " + optimized + " vs " + original);
            for (int i = 0; i < 45; i++) DamageNumber.Spawn(origin, i + 1, i % 3 == 0);
            var numbers = UnityEngine.Object.FindObjectsByType<DamageNumber>(FindObjectsSortMode.None);
            Check(numbers.Length == 30, "Damage number pool is not bounded");
            var ids = new System.Collections.Generic.HashSet<int>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var number in numbers)
            {
                ids.Add(number.GetInstanceID());
                typeof(DamageNumber).GetField("age", flags).SetValue(number, 2f);
                typeof(DamageNumber).GetMethod("Update", flags).Invoke(number, null);
            }
            for (int i = 0; i < 30; i++) DamageNumber.Spawn(origin, 10, false);
            foreach (var number in UnityEngine.Object.FindObjectsByType<DamageNumber>(FindObjectsSortMode.None))
                Check(ids.Contains(number.GetInstanceID()), "Damage numbers not reused");
            File.WriteAllText(Report, "PASS: 80-collider saturated fallback, self-collider filtering, nearest cover and empty ray. Damage numbers bounded at 30 and all 30 reused after expiry. " + (counterAvailable ? "Allocated bytes for 1000 sparse queries: optimized=" + optimized + ", original=" + original : "Allocation counter unavailable: this Unity runtime reports zero for both the optimized and allocating reference implementation") + ". This is a query fixture, not a game FPS benchmark.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/query_optimization_error.txt", ex.ToString()); Debug.LogException(ex); }
        finally { if (scene.IsValid() && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }
}
