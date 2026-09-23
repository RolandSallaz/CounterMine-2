using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ValidateMilkor
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    [MenuItem("Tools/CounterMine/Validate Milkor MGL")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/Milkor");
        var report = new StringBuilder();
        try
        {
            var state = new MilkorRewardState(1);
            Check(!state.Activate(100), "Un-earned activation");
            for (int i = 0; i < 9; i++) state.RecordKill();
            Check(state.Kills == 9 && state.Charges == 0 && !state.Activate(100), "Reward before ten kills");
            state.RecordKill(); Check(state.Charges == 1 && state.Kills == 0, "Tenth kill reward");
            Check(state.Activate(100) && state.Ammo == 6 && state.Charges == 0, "Initial activation");
            Check(state.Spend(100) && state.Activate(100) && state.Ammo == 5, "Recall refilled magazine");
            Check(!state.Spend(101), "Another life spent ammunition");
            for (int i = 0; i < 5; i++) Check(state.Spend(100), "Missing projectile");
            Check(!state.Spend(100) && !state.Activate(100), "Seventh projectile/charge duplication");
            for (int i = 0; i < 20; i++) state.RecordKill();
            Check(state.Charges == 2 && state.Activate(101) && state.Ammo == 6 && state.Charges == 1, "Stored rewards across lives");
            var restored = MilkorRewardState.Read(state.Pack(), 1);
            Check(restored.Ammo == 6 && restored.Charges == 1 && restored.ViewId == 101, "Migration snapshot");
            Check(MilkorRewardState.Read(state.Pack(), 2).Charges == 0, "Round reset");
            report.AppendLine("PASS reward ledger: ten kills, queued charges, six shots, recall, life identity, migration snapshot, round reset.");
            var player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
            try
            {
                var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                var ammo = player.GetComponent<WeaponAmmo>();
                Call(player.GetComponent<PlayerHealth>(), "Awake"); Call(ammo, "Awake");
                Check(player.GetComponent<MilkorSkill>() != null, "Missing skill component");
                Check(sync.EquipWeapon("ak74"), "AK unavailable"); sync.RestartIdle(); ammo.Consume();
                Check(sync.EquipWeapon("milkor"), "Milkor catalog");
                Check(ammo.MagAmmo == 0 && ammo.FiniteReserve, "Free initial ammunition");
                ammo.SetFiniteAmmo("milkor", 6);
                Check(sync.ProceduralEquip && !sync.CanFire && sync.CanPoseHands, "Procedural equip gate/IK");
                sync.RestartIdle();
                Check(sync.CanFire && ammo.MagAmmo == 6 && !ammo.TryStartReload(), "Idle/reload gate");
                var rest = sync.WeaponRoot.localPosition;
                sync.PlayEquip();
                Check(sync.WeaponRoot.localPosition.y < rest.y - .25f && !sync.CanFire, "Missing procedural draw offset");
                typeof(WeaponIdleSynchronizer).GetMethod("EvaluatePair", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sync, new object[] { .36d });
                Check(sync.WeaponRoot.localPosition.y > rest.y - .25f && sync.ProceduralEquip, "Draw does not advance");
                sync.EquipWeapon("ak74"); sync.EquipWeapon("milkor"); sync.RestartIdle();
                Check(Vector3.Distance(rest, sync.WeaponRoot.localPosition) < .0001f, "Interrupted draw accumulates offsets");
                ammo.Consume(); ammo.Consume();
                Check(!ammo.TryStartReload() && !ammo.IsReloading, "Manual reload created ammo");
                Check(sync.EquipWeapon("ak74"), "Return to AK");
                Check(ammo.MagAmmo == 29 && !ammo.FiniteReserve, "AK magazine/regeneration settings");
                Check(sync.EquipWeapon("milkor") && ammo.MagAmmo == 4, "Switch refilled Milkor");
                sync.RestartIdle();
                for (int i = 0; i < 4; i++) ammo.Consume();
                Check(ammo.MagAmmo == 0 && !ammo.CanShoot && !ammo.IsReloading, "Empty auto-reload");
                Check(sync.ApplyNetworkState("milkor", "equip", 0, 1), "Remote equip binding");
                Check(!sync.PlayWeaponAction("reload"), "Unexpected reload animation");
                Check(sync.WeaponRoot.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && (m.shader.name == "Universal Render Pipeline/Lit" || m.shader.name == "CounterMine/Collimator"))), "Invalid materials");
                var catalog = new SerializedObject(sync).FindProperty("weapons");
                var entry = catalog.GetArrayElementAtIndex(2);
                Check(entry.FindPropertyRelative("magazineSize").intValue == 6 && !entry.FindPropertyRelative("automatic").boolValue, "Magazine/cadence config");
                ammo.SetFiniteAmmo("milkor", 6); sync.RestartIdle();
                Measure(sync.WeaponRoot);
                RenderPlayer(player, sync);
                report.AppendLine("PASS player: finite magazine, no reload, weapon switching, procedural fire gate/hand IK, remote state, URP materials.");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            RenderModel();
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(InstallMilkor.Folder + "Round.prefab") != null, "Missing projectile");
            Check(AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/milkor.asset").shots[0] != null, "Missing launch sound");
            report.AppendLine("PASS model and first-person render previews, projectile asset, launch sound.");
            ValidateUCP.Run();
            Check(!File.ReadAllText("Documentation/UCP/validation.txt").Contains("FAIL"), "Existing AK/UCP regression checks failed");
            ValidateWeaponActionOffsets.Run();
            Check(!File.ReadAllText("Documentation/UCP/action-offset-validation.txt").Contains("FAIL"), "Existing weapon action offset checks failed");
            report.AppendLine("PASS existing AK/UCP magazine, reload, animation, network state and recoil regression checks.");
            report.AppendLine("Editor checks only; live multiplayer and WebGL performance require an actual match.");
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); Debug.LogException(e); }
        File.WriteAllText("Documentation/Milkor/validation.txt", report.ToString());
    }
    private static void Measure(Transform root)
    {
        Bounds bounds = new Bounds(); bool first = true;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var skinned = renderer as SkinnedMeshRenderer;
            Mesh mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var matrices = skinned != null ? skinned.bones.Select((bone, i) => bone.localToWorldMatrix * mesh.bindposes[i]).ToArray() : null;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                Vector3 world = renderer.transform.TransformPoint(vertex);
                if (matrices != null && matrices.Length > 0 && weights.Length == vertices.Length)
                {
                    var w = weights[i];
                    world = matrices[w.boneIndex0].MultiplyPoint3x4(vertex) * w.weight0;
                    if (w.weight1 > 0) world += matrices[w.boneIndex1].MultiplyPoint3x4(vertex) * w.weight1;
                    if (w.weight2 > 0) world += matrices[w.boneIndex2].MultiplyPoint3x4(vertex) * w.weight2;
                    if (w.weight3 > 0) world += matrices[w.boneIndex3].MultiplyPoint3x4(vertex) * w.weight3;
                }
                Vector3 point = root.InverseTransformPoint(world);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
            }
        }
        File.WriteAllText("Documentation/Milkor/dimensions.txt", "Measured deformed mesh vertices in weapon-local metres (unit root scale).\n" +
            "Width: " + bounds.size.x.ToString("F4") + " m\nHeight with optic and foregrip: " + bounds.size.y.ToString("F4") +
            " m\nOverall length: " + bounds.size.z.ToString("F4") + " m\nRoot scale: " + root.lossyScale +
            "\nManufacturer Mk 1S nominal overall length: 754 mm stock retracted / 832 mm stock extended; width 163 mm; height 207 mm (accessories vary).\n" +
            "Source: https://milkor.ae/wp-content/uploads/2025/02/Milkor-Weapons-Division.pdf (PDF p. 5).\n");
    }
    private static void RenderPlayer(GameObject player, WeaponIdleSynchronizer sync)
    {
        var presentation = player.GetComponent<PlayerModelPresentation>();
        if (presentation == null) presentation = player.AddComponent<PlayerModelPresentation>();
        presentation.ConfigureView(true); sync.RestartIdle();
        foreach (var ik in player.GetComponentsInChildren<WeaponHandIK>(true)) Call(ik, "LateUpdate");
        var camera = player.GetComponentInChildren<Camera>(true); camera.scene = player.scene;
        Render(camera, "first-person.png");
    }
    private static void RenderModel()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(InstallMilkor.ModelPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, scene);
            var go = new GameObject("Model preview"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(-7, 4, -13); camera.transform.LookAt(new Vector3(-.6f,.3f,0));
            camera.fieldOfView = 42;
            Render(camera, "model.png");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Render(Camera camera, string file)
    {
        var light = new GameObject("Milkor preview key"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, camera.gameObject.scene);
        light.AddComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 2.5f;
        light.transform.rotation = Quaternion.Euler(35,-30,0);
        var rt = new RenderTexture(1280,720,24); var old = RenderTexture.active;
        var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f,.14f,.18f);
            camera.targetTexture = rt; camera.aspect = 1280f/720; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes("Documentation/Milkor/"+file, image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(light); }
    }
}
