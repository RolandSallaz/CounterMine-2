using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ValidateUCP
{
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);}
    [MenuItem("Tools/CounterMine/Validate UCP")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        Directory.CreateDirectory("Documentation/UCP");
        var report=new StringBuilder();
        try
        {
            foreach(var name in new[]{"Player","Bot"})
            {
                var root=PrefabUtility.LoadPrefabContents("Assets/Resources/"+name+".prefab");
                try
                {
                    var sync=root.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                    var ammo=root.GetComponent<WeaponAmmo>();
                    Check(ammo!=null,"Missing magazine component");
                    typeof(PlayerHealth).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root.GetComponent<PlayerHealth>(),null);
                    typeof(WeaponAmmo).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ammo,null);
                    Check(sync.EquipWeapon("ak74"),"AK selection failed");sync.RestartIdle();
                    ammo.Consume();ammo.Consume();Check(ammo.MagAmmo==28,"AK magazine");
                    Check(sync.EquipWeapon("ucp"),"UCP selection failed");
                    Check(sync.IsPlayingAction&&!sync.CanFire,"Equip fire gate");sync.RestartIdle();
                    Check(ammo.MagAmmo==20&&ammo.MagazineSize==20,"UCP magazine");
                    Check(sync.CanFire&&sync.WeaponRoot.name=="UCP_Weapon","UCP idle/fire");
                    var recoil=root.GetComponentInChildren<WeaponRecoilController>(true);
                    Check(Mathf.Approximately(recoil.RoundsPerMinute,400),"UCP cadence");
                    var catalog=new SerializedObject(sync).FindProperty("weapons");
                    var ucp=catalog.GetArrayElementAtIndex(1);
                    Check(!ucp.FindPropertyRelative("automatic").boolValue,"UCP must be semi-auto");
                    foreach(var action in new[]{"Idle","Equip","Reload"})
                    foreach(var part in new[]{"Character","Weapon"})
                    {
                        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Anims/UCP/UCP_"+action+"_"+part+".anim");
                        var target=part=="Character"?sync.CharacterAnimator.transform:sync.WeaponRoot;
                        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                            Check(binding.path==""||target.Find(binding.path)!=null,"Missing animation path: "+binding.path);
                        clip.SampleAnimation(target.gameObject,clip.length*.5f);
                    }
                    sync.RestartIdle();
                    var ik=new SerializedObject(root.GetComponentInChildren<WeaponHandIK>(true));
                    foreach(var side in new[]{"left","right"})
                    {
                        var hand=(Transform)ik.FindProperty(side+"Hand").objectReferenceValue;
                        var grip=(Transform)ucp.FindPropertyRelative(side+"Grip").objectReferenceValue;
                        Check(Vector3.Distance(hand.position,grip.position)<.002f,"Idle grip offset: "+side+" "+Vector3.Distance(hand.position,grip.position));
                    }
                    ammo.Consume();Check(ammo.TryStartReload(),"UCP reload did not start");
                    Check(sync.ActionId=="reload"&&ammo.IsReloading,"Reload binding");
                    Check(sync.EquipWeapon("ak74"),"Return to AK failed");
                    Check(ammo.MagAmmo==28&&!ammo.IsReloading,"Switch incorrectly refills magazine");
                    Check(sync.EquipWeapon("ucp"),"Return to UCP failed");
                    Check(ammo.MagAmmo==19,"Interrupted reload incorrectly refills UCP");sync.RestartIdle();
                    Check(ammo.TryStartReload(),"Second reload failed");sync.RestartIdle();
                    typeof(WeaponAmmo).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ammo,null);
                    Check(ammo.MagAmmo==20&&!ammo.IsReloading,"Reload completion failed");
                    Check(sync.ApplyNetworkState("ak74","idle",0,1),"Remote AK state");
                    Check(sync.ApplyNetworkState("ucp","reload",0,1),"Remote UCP state");
                    sync.RestartIdle();
                    Check(sync.WeaponRoot.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null)),"Missing materials");
                    if(name=="Player") Render(root,sync);
                    report.AppendLine("PASS "+name+": animation paths, idle grips, equip/fire gate, semi-auto stats, independent magazines, reload interruption/completion and network state application.");
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            report.AppendLine("Editor prefab/animation validation; live Photon match and packaged build not run.");
        }
        catch(Exception e) {report.AppendLine("FAIL "+e);Debug.LogException(e);}
        File.WriteAllText("Documentation/UCP/validation.txt",report.ToString());
    }
    static void Render(GameObject root,WeaponIdleSynchronizer sync)
    {
        var presentation=root.GetComponent<PlayerModelPresentation>();
        if(presentation==null)presentation=root.AddComponent<PlayerModelPresentation>();
        presentation.ConfigureView(true);sync.RestartIdle();
        var camera=root.GetComponentInChildren<Camera>(true);
        var light=new GameObject("UCP preview light");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light,root.scene);
        var source=light.AddComponent<Light>();source.type=LightType.Directional;source.intensity=2;
        light.transform.rotation=Quaternion.Euler(35,-30,0);
        var target=new RenderTexture(1280,720,24);var previous=RenderTexture.active;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.enabled=false;camera.scene=root.scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.11f,.14f,.18f);
            camera.targetTexture=target;camera.aspect=1280f/720;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("Documentation/UCP/first-person.png",image.EncodeToPNG());
        }
        finally {camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(light);}
    }
}
