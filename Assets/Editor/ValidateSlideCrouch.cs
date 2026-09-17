using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
static class ValidateSlideCrouch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Documentation/Character/slide_crouch_validation.txt";
    static ValidateSlideCrouch() { EditorApplication.delayCall += Run; }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Run()
    {
        if (File.Exists(Report) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var previous = SceneManager.GetActiveScene(); Scene scene = default;
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab"), scene);
            player.transform.position = new Vector3(10000, .05f, 10000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(10000, -.5f, 10000); floor.transform.localScale = new Vector3(20, 1, 20);
            var move = player.GetComponent<PlayerController>(); Call(move, "Awake");
            var capsule = player.GetComponent<CharacterController>();
            Physics.SyncTransforms(); for (int i = 0; i < 4; i++) capsule.Move(Vector3.down * .1f);
            Check(capsule.isGrounded, "Physics fixture is not grounded");
            Set(move, "<IsSprinting>k__BackingField", true);
            Set(move, "horizontalVelocity", Vector3.forward * 3f);
            Check(!(bool)Call(move, "TryStartSlide"), "Slow movement can trigger slide");
            Set(move, "horizontalVelocity", Vector3.forward * 5.5f);
            Check((bool)Call(move, "TryStartSlide"), "Sprint does not trigger slide");
            Check(Mathf.Abs(move.StaminaNormalized - .88f) < .001f, "Slide stamina cost wrong");
            Check(!(bool)Call(move, "TryStartSlide"), "Slide retriggers while active");
            Call(move, "EndSlide");
            Check(!(bool)Call(move, "TryStartSlide"), "Slide cooldown not enforced");
            capsule.height = 1.15f; capsule.center = Vector3.up * .575f;
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube); roof.transform.position = player.transform.position + Vector3.up * 1.5f; roof.transform.localScale = new Vector3(3, .2f, 3);
            Physics.SyncTransforms(); Check(!(bool)Call(move, "CanStand"), "Standing allowed inside ceiling");
            UnityEngine.Object.DestroyImmediate(roof); Physics.SyncTransforms(); Check((bool)Call(move, "CanStand"), "Cannot stand in clear space");
            var sync = player.GetComponentInChildren<WeaponIdleSynchronizer>(true); sync.EquipWeapon("ak74"); sync.RestartIdle();
            var walk = player.GetComponent<PlayerWalkAnimation>(); Call(walk, "Start");
            var camera = player.GetComponentInChildren<Camera>(true); Vector3 cameraRest = camera.transform.localPosition;
            float worstGripError = 0;
            string details = "";
            foreach (var bone in sync.CharacterAnimator.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Hips" || bone.name == "Spine" || bone.name == "Chest") details += bone.name + " parent=" + bone.parent.name + "\n";
            foreach (bool slide in new[] { false, true })
            {
                float poseError = 0;
                for (int sample = 0; sample < 24; sample++)
                {
                    Call(sync, "Update");
                    Set(walk, "crouchBlend", 1f); Set(walk, "slideBlend", slide ? 1f : 0f);
                    Set(walk, "weight", slide ? 0f : 1f); Set(walk, "phase", sample / 24f);
                    camera.transform.localPosition = cameraRest - Vector3.up * (slide ? .87f : .596f);
                    Call(walk, "ApplyPose", slide ? 4f : 1.7f, player.transform.forward);
                    foreach (var ik in player.GetComponentsInChildren<WeaponHandIK>(true))
                    {
                        Call(ik, "Solve");
                        foreach (string side in new[] { "left", "right" })
                        {
                            var hand = (Transform)typeof(WeaponHandIK).GetField(side + "Hand", Flags).GetValue(ik);
                            var grip = (Transform)typeof(WeaponHandIK).GetField(side + "Grip", Flags).GetValue(ik);
                            worstGripError = Mathf.Max(worstGripError, Vector3.Distance(hand.position, grip.position));
                            poseError = Mathf.Max(poseError, Vector3.Distance(hand.position, grip.position));
                        }
                    }
                }
                RenderPose(scene, player, slide ? "slide" : "crouch");
                details += (slide ? "slide" : "crouch") + " error=" + poseError + "\n";
            }
            File.WriteAllText("Temp/stance_details.txt", details);
            Check(worstGripError < .03f, "Crouch/slide grip out of reach: " + worstGripError);
            File.WriteAllText(Report, "PASS: grounded sprint slide gate, low-speed rejection, 12 stamina cost, no retrigger, cooldown, low-ceiling stand prevention, clear-space stand. 48 crouch/slide poses; maximum hand/grip error " + worstGripError.ToString("F6") + " m. Live two-client gameplay not tested.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/slide_crouch_error.txt", ex.ToString()); Debug.LogException(ex); }
        finally { if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true); if (previous.IsValid()) SceneManager.SetActiveScene(previous); }
    }

    static void RenderPose(Scene scene, GameObject player, string name)
    {
        var camera = new GameObject("Pose preview").AddComponent<Camera>();
        camera.scene = scene;
        camera.transform.position = player.transform.position + new Vector3(2.5f, 1.5f, 3.5f);
        camera.transform.LookAt(player.transform.position + Vector3.up * .65f);
        camera.orthographic = true; camera.orthographicSize = 1.2f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .1f);
        var light = new GameObject("Pose light").AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(35, -30, 0);
        var target = new RenderTexture(900, 700, 24); var image = new Texture2D(900, 700, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); image.Apply();
            File.WriteAllBytes("Documentation/Character/" + name + "_preview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
