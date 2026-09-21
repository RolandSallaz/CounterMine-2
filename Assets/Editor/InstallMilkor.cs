using System;
using System.IO;
using System.Linq;
using Animancer;
using UnityEditor;
using UnityEngine;

/// <summary>Repeatable asset installation; source FBX is kept intact and remapped to URP materials.</summary>
[InitializeOnLoad]
public static class InstallMilkor
{
    public const string Folder = "Assets/Resources/Milkor/";
    public const string ModelPath = "Assets/models/Milkor MGL Mk 1s.fbx";
    static InstallMilkor() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (!File.Exists("Temp/install-milkor.request") || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete("Temp/install-milkor.request");
        try { Run(); }
        catch (Exception e) { File.WriteAllText("Documentation/Milkor/install-error.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/CounterMine/Install Milkor MGL")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory("Documentation/Milkor");
        AssetDatabase.Refresh();
        var steel = Material("Parkerized Steel", new Color(.105f,.12f,.13f), .72f, .36f);
        var olive = Material("Olive Cerakote", new Color(.26f,.29f,.18f), .25f, .3f);
        var edge = Material("Olive Frame", new Color(.32f,.34f,.23f), .35f, .38f);
        var rubber = Material("Black Polymer", new Color(.045f,.052f,.055f), .04f, .24f);
        var brass = Material("40mm Brass", new Color(.55f,.36f,.12f), .78f, .5f);
        var glass = Material("Optic Glass", new Color(.035f,.16f,.19f), .7f, .85f);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
        string[] names = { "Material", "Material.002", "Material.006", "Material.008", "Material.005", "Material.007",
            "Material.004", "Material.001", "Material.003", "Material.014", "Material.013", "Material.012", "Material.011" };
        Material[] mapped = { steel, rubber, steel, edge, olive, steel, brass, brass, brass, rubber, steel, glass, brass };
        for (int i = 0; i < names.Length; i++) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), names[i]), mapped[i]);
        importer.SaveAndReimport();
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Milkor_Idle.anim");
        if (idle == null)
        {
            idle = new AnimationClip { name = "Milkor_Idle" };
            idle.SetCurve("IdleClock", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, 0));
            AssetDatabase.CreateAsset(idle, Folder + "Milkor_Idle.anim");
        }
        var audio = AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/milkor.asset");
        if (audio == null) { audio = ScriptableObject.CreateInstance<WeaponAudioProfile>(); AssetDatabase.CreateAsset(audio, "Assets/Resources/Audio/Weapons/milkor.asset"); }
        var baseAudio = AssetDatabase.LoadAssetAtPath<WeaponAudioProfile>("Assets/Resources/Audio/Weapons/ak74.asset");
        audio.displayName = "Milkor MGL Mk 1S";
        audio.shots = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "Launch.wav") };
        audio.equip = baseAudio.equip; audio.dryFire = baseAudio.dryFire; audio.reload = null; audio.shotVolume = .9f;
        EditorUtility.SetDirty(audio);
        var handling = AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>(Folder + "Handling.asset");
        if (handling == null)
        {
            handling = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/Ak74/AK74_Handling.asset"));
            handling.name = "Milkor Handling"; handling.ergonomics = 35;
            AssetDatabase.CreateAsset(handling, Folder + "Handling.asset");
        }
        InstallPlayer("Assets/Resources/Player.prefab", idle, handling, audio);
        BuildRound(brass, olive);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Documentation/Milkor/install.txt", "Installed Milkor MGL Mk 1S: remapped URP materials, player catalog, six-round finite magazine, procedural equip, projectile and audio.\n");
        EditorApplication.delayCall += ValidateMilkor.Run;
    }
    private static Material Material(string name, Color color, float metal, float smooth)
    {
        var path = Folder + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
        result.name = name; result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", metal); result.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(result); return result;
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void InstallPlayer(string path, AnimationClip idle, WeaponHandlingProfile handling, WeaponAudioProfile audio)
    {
        var player = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (player.GetComponent<MilkorSkill>() == null) player.AddComponent<MilkorSkill>();
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var so = new SerializedObject(sync); var catalog = so.FindProperty("weapons");
            var ak = catalog.GetArrayElementAtIndex(0);
            var akAnim = (AnimancerComponent)ak.FindPropertyRelative("animator").objectReferenceValue;
            var parent = akAnim.transform.parent;
            var old = parent.Find("Milkor_Weapon"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var weapon = new GameObject("Milkor_Weapon"); weapon.transform.SetParent(parent, false);
            weapon.transform.rotation = player.transform.rotation;
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), weapon.transform, false);
            model.name = "Model"; model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, 90, 0); model.transform.localScale = Vector3.one * .07f;
            new GameObject("IdleClock").transform.SetParent(weapon.transform, false);
            var animator = weapon.AddComponent<Animator>(); animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var animancer = weapon.AddComponent<AnimancerComponent>(); animancer.Animator = animator;
            var mechanism = weapon.AddComponent<MilkorMechanism>(); mechanism.cylinder = model.transform.Find("Armature/body/cylinder");
            var rig = weapon.AddComponent<WeaponAimRig>(); Set(rig, "handling", handling);
            var characterClip = (AnimationClip)ak.FindPropertyRelative("characterIdle").objectReferenceValue;
            var character = sync.CharacterAnimator.gameObject;
            var bones = character.GetComponentsInChildren<Transform>(true);
            var positions = bones.Select(t => t.localPosition).ToArray(); var rotations = bones.Select(t => t.localRotation).ToArray();
            var scales = bones.Select(t => t.localScale).ToArray();
            characterClip.SampleAnimation(character, 0);
            var ik = new SerializedObject(player.GetComponentInChildren<WeaponHandIK>(true));
            var leftHand = (Transform)ik.FindProperty("leftHand").objectReferenceValue;
            var rightHand = (Transform)ik.FindProperty("rightHand").objectReferenceValue;
            Transform Marker(string name, Vector3 raw, Quaternion rotation)
            {
                var marker = new GameObject(name).transform; marker.SetParent(weapon.transform, false);
                marker.position = model.transform.TransformPoint(raw); marker.rotation = rotation; return marker;
            }
            var right = Marker("RightGrip", new Vector3(1.7f, -.65f, 0), rightHand.rotation);
            var left = Marker("LeftGrip", new Vector3(-3.4f, -1.1f, 0), leftHand.rotation);
            var muzzle = Marker("Muzzle", new Vector3(-6f, .18f, 0), player.transform.rotation);
            var sight = Marker("Sight", new Vector3(-.75f, 2.07f, 0), player.transform.rotation).gameObject.AddComponent<WeaponSight>();
            Set(rig, "defaultSight", sight);
            weapon.transform.position += rightHand.position - right.position;
            for (int i = 0; i < bones.Length; i++) { bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]); bones[i].localScale = scales[i]; }
            foreach (var t in weapon.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            int index = -1;
            for (int i = 0; i < catalog.arraySize; i++) if (catalog.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == MilkorSkill.WeaponId) index = i;
            if (index < 0) { index = catalog.arraySize; catalog.arraySize++; }
            var entry = catalog.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = MilkorSkill.WeaponId;
            entry.FindPropertyRelative("audioProfile").objectReferenceValue = audio;
            entry.FindPropertyRelative("animator").objectReferenceValue = animancer;
            entry.FindPropertyRelative("aimRig").objectReferenceValue = rig;
            entry.FindPropertyRelative("muzzle").objectReferenceValue = muzzle;
            entry.FindPropertyRelative("leftGrip").objectReferenceValue = left;
            entry.FindPropertyRelative("rightGrip").objectReferenceValue = right;
            entry.FindPropertyRelative("magazineSize").intValue = 6;
            entry.FindPropertyRelative("finiteReserve").boolValue = true;
            entry.FindPropertyRelative("proceduralEquipSeconds").floatValue = .72f;
            entry.FindPropertyRelative("roundsPerMinute").floatValue = 60f / MilkorSkill.ShotInterval;
            entry.FindPropertyRelative("automatic").boolValue = false;
            entry.FindPropertyRelative("damage").intValue = 110;
            entry.FindPropertyRelative("recoilKick").floatValue = 2.2f;
            entry.FindPropertyRelative("recoil").FindPropertyRelative("animation").objectReferenceValue = null;
            entry.FindPropertyRelative("characterIdle").objectReferenceValue = characterClip;
            entry.FindPropertyRelative("weaponIdle").objectReferenceValue = idle;
            entry.FindPropertyRelative("characterEquip").objectReferenceValue = null;
            entry.FindPropertyRelative("weaponEquip").objectReferenceValue = null;
            entry.FindPropertyRelative("actions").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            weapon.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(player, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }
    private static void BuildRound(Material brass, Material olive)
    {
        var root = new GameObject("40mm Round");
        foreach (bool cap in new[] { false, true })
        {
            var part = GameObject.CreatePrimitive(cap ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
            part.name = cap ? "Olive projectile" : "Brass body"; part.transform.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.localPosition = new Vector3(0, 0, cap ? .045f : 0);
            part.transform.localRotation = Quaternion.Euler(90, 0, 0);
            part.transform.localScale = cap ? new Vector3(.042f,.05f,.042f) : new Vector3(.04f,.025f,.04f);
            part.GetComponent<Renderer>().sharedMaterial = cap ? olive : brass;
        }
        PrefabUtility.SaveAsPrefabAsset(root, Folder + "Round.prefab"); UnityEngine.Object.DestroyImmediate(root);
    }
}
