using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ValidateIndividualReload
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ValidateIndividualReload()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/validate-individual-reload.request"))return;
        File.Delete("Temp/validate-individual-reload.request");Run();
    }
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags).Invoke(o,args);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Sample(WeaponIdleSynchronizer sync,WeaponAmmo ammo,WeaponManualAction motion,double start,double seconds)
    {
        typeof(WeaponIdleSynchronizer).GetField("elapsedSeconds",Flags).SetValue(sync,seconds);
        Call(sync,"EvaluatePair",seconds);Call(motion,"LateUpdate");Call(ammo,"Update");
        Check(sync.StartedAt==start,"Insertion restarts the action clock");
    }
    [MenuItem("Tools/CounterMine/Validate Individual Reload")]
    public static void Run()
    {
        var report=new StringBuilder();
        var previousTrails=UnityEngine.Object.FindObjectsByType<BulletTrail>(FindObjectsInactive.Include,FindObjectsSortMode.None).ToHashSet();
        try
        {
            foreach(string prefab in new[]{"Player","Bot"})
            {
                var player=PrefabUtility.LoadPrefabContents("Assets/Resources/"+prefab+".prefab");
                try
                {
                    var ammo=player.GetComponent<WeaponAmmo>();var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                    Call(player.GetComponent<PlayerHealth>(),"Awake");Call(ammo,"Awake");
                    var aim=player.GetComponentInChildren<WeaponAimController>(true);Call(aim,"Awake");
                    var look=player.GetComponent<PlayerCameraLook>();if(look!=null)Call(look,"Awake");
                    var presentation=player.GetComponent<PlayerModelPresentation>();presentation.ConfigureView(prefab=="Player");
                    var camera=player.GetComponentInChildren<Camera>(true);camera.scene=player.scene;
                    var light=new GameObject("Reload preview light").AddComponent<Light>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject,player.scene);light.type=LightType.Directional;light.intensity=3;light.transform.eulerAngles=new Vector3(35,-30,0);
                    typeof(WeaponAmmo).GetField("autoReloadOnEmpty",Flags).SetValue(ammo,false);
                    foreach(string id in new[]{"rsh12","winchester1897"})
                    {
                        typeof(WeaponIdleSynchronizer).GetField("playbackSpeed",Flags).SetValue(sync,1f);
                        typeof(WeaponAmmo).GetField("autoReloadOnEmpty",Flags).SetValue(ammo,false);
                        sync.EquipWeapon(id);sync.RestartIdle();while(ammo.MagAmmo>0)ammo.Consume();
                        Check(ammo.TryStartReload(),id+" empty reload start");
                        Check(sync.ReloadRoundCount==5,"Missing round count");
                        double start=sync.StartedAt;float cycle=sync.ReloadRoundSeconds;
                        var motion=sync.WeaponRoot.GetComponent<WeaponManualAction>();Call(motion,"OnEnable");
                        Check(!ammo.TryInterruptReloadForShot(),"Empty gun interrupts before insertion");
                        for(int round=0;round<5;round++)
                        {
                            double insert=WeaponIdleSynchronizer.ReloadOpenSeconds+(round+WeaponIdleSynchronizer.ReloadInsertPhase)*cycle;
                            Sample(sync,ammo,motion,start,insert-.001);
                            Check(ammo.MagAmmo==round,"Round credited before seating");
                            Sample(sync,ammo,motion,start,insert+.001);
                            Check(ammo.MagAmmo==round+1,"Round not credited at seating");
                            Check(ammo.IsReloading&&!sync.CanFire,"Reload stops between rounds");
                            Check(motion.reloadRound==null||!motion.reloadRound.gameObject.activeSelf,"Inserted prop stays in hand");
                            if(id=="rsh12")Check(Quaternion.Angle(motion.cylinderArm.localRotation,Quaternion.identity)>20,"Cylinder closes between insertions");
                            // A repeated sample cannot grant the same cartridge twice.
                            Sample(sync,ammo,motion,start,insert+.001);Check(ammo.MagAmmo==round+1,"Duplicate cartridge credit");
                        }
                        double duration=(float)typeof(WeaponIdleSynchronizer).GetProperty("ActionDuration",Flags).GetValue(sync);
                        typeof(WeaponIdleSynchronizer).GetProperty("StartedAt").SetValue(sync,Time.timeAsDouble-duration-.01);
                        Call(sync,"Update");Call(ammo,"Update");Call(motion,"LateUpdate");
                        Check(ammo.MagAmmo==5&&!ammo.IsReloading&&sync.CanFire,"Full reload does not close and finish");
                        ammo.Consume();ammo.Consume();ammo.Consume();
                        Check(ammo.TryStartReload()&&sync.ReloadRoundCount==3,"Partial reload does not use missing count");
                        start=sync.StartedAt;
                        Sample(sync,ammo,motion,start,WeaponIdleSynchronizer.ReloadOpenSeconds+WeaponIdleSynchronizer.ReloadInsertPhase*cycle+.001);
                        Check(ammo.MagAmmo==3&&ammo.TryInterruptReloadForShot(),"Loaded gun cannot cancel reload");
                        Check(!ammo.IsReloading&&sync.CanFire,"Cancellation leaves fire gated");ammo.Consume();Call(ammo,"Update");
                        Check(ammo.MagAmmo==2&&!ammo.IsReloading,"Cancellation loses rounds or restarts reload");
                        Call(motion,"LateUpdate");Check(motion.reloadRound==null||!motion.reloadRound.gameObject.activeSelf,"Cancelled reload leaves cartridge visible");
                        Check(ammo.TryStartReload(),"Cannot resume interrupted reload");
                        sync.EquipWeapon("ak74");sync.RestartIdle();sync.EquipWeapon(id);sync.RestartIdle();
                        Check(ammo.MagAmmo==2&&!ammo.IsReloading,"Weapon switch refills or continues reload");
                        if(prefab=="Bot")
                        {
                            var network=player.GetComponent<NetworkWeapon>();Call(network,"Awake");
                            // Each weapon case starts with a fresh rate-limit burst.
                            typeof(NetworkWeapon).GetField("tokens",Flags).SetValue(network,2f);
                            typeof(NetworkWeapon).GetField("lastSoundSequence",Flags).SetValue(network,int.MaxValue);
                            typeof(NetworkWeapon).GetField("muzzleFlashEnabled",Flags).SetValue(network,false);
                            Check(ammo.TryStartReload(),"Bot shot interrupt start failed");
                            Check(network.FireBotShot(camera.transform.position,Vector3.forward,0),"Firing pipeline blocks a loaded individual reload");
                            Check(ammo.MagAmmo==1&&!ammo.IsReloading&&sync.CanFire,"Actual shot does not consume one round and cancel reload");
                            typeof(WeaponAmmo).GetField("autoReloadOnEmpty",Flags).SetValue(ammo,true);
                            Check(ammo.TryStartReload()&&network.FireBotShot(camera.transform.position,Vector3.forward,0),"Cannot fire the only loaded round during reload");
                            Check(ammo.MagAmmo==0&&!ammo.IsReloading&&sync.CanFire,"Firing the last round immediately restarts the cancelled reload");
                            typeof(WeaponAmmo).GetField("autoReloadOnEmpty",Flags).SetValue(ammo,false);
                        }
                        Check(sync.ApplyNetworkState(id,"reload",0,0,3)&&sync.ReloadRoundCount==3,"Network missing-round count not restored");sync.RestartIdle();
                        if(prefab=="Player")
                        {
                            typeof(WeaponIdleSynchronizer).GetField("playbackSpeed",Flags).SetValue(sync,1f);
                            while(ammo.MagAmmo>0)ammo.Consume();ammo.TryStartReload();
                            RenderCycle(player,sync,presentation,look,aim,camera,id);
                            sync.RestartIdle();Call(ammo,"Update");
                        }
                        report.AppendLine("PASS "+prefab+" "+id+": one action clock, five individual credits at seating, no duplicate credit, partial reload, shot cancellation, preserved ammo, switch interruption, network round count."+(prefab=="Player"?" Full reload sampled at 60 FPS with grip and frame-step checks.":" Actual bot firing pipeline cancels reload; last-round shot does not auto-restart it."));
                    }
                    sync.EquipWeapon("ak74");sync.RestartIdle();ammo.Consume();
                    Check(ammo.TryStartReload()&&!ammo.CanInterruptReload&&!ammo.TryInterruptReloadForShot(),"Magazine reload became interruptible");
                }
                finally {PrefabUtility.UnloadPrefabContents(player);}
            }
        }
        catch(Exception e){report.AppendLine("FAIL "+e);Debug.LogException(e);}
        foreach(var trail in UnityEngine.Object.FindObjectsByType<BulletTrail>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(!previousTrails.Contains(trail))UnityEngine.Object.DestroyImmediate(trail.gameObject);
        File.WriteAllText("Documentation/NewWeapons/individual-reload-validation.txt",report.ToString());
    }
    static void RenderCycle(GameObject player,WeaponIdleSynchronizer sync,PlayerModelPresentation presentation,PlayerCameraLook look,WeaponAimController aim,Camera camera,string id)
    {
        var sheet=new Texture2D(1280,540,TextureFormat.RGB24,false);
        try
        {
            // Opening, then a complete first cartridge, then subsequent inserts and closing.
            double cycle=sync.ReloadRoundSeconds,duration=(float)typeof(WeaponIdleSynchronizer).GetProperty("ActionDuration",Flags).GetValue(sync);
            var times=new[]{0d,.14d,.22+cycle*.18,.22+cycle*.32,.22+cycle*.48,.22+cycle*.59,
                .22+cycle*.64,.22+cycle*.85,.22+cycle*1.32,.22+cycle*1.59,.22+cycle*4.59,duration-.12};
            float worstGap=0,largestStep=0;Quaternion previous=sync.WeaponRoot.localRotation;
            for(int frame=0;frame<=Mathf.CeilToInt((float)duration*60);frame++)
            {
                double seconds=Math.Min(duration,frame/60d);
                typeof(ValidateProceduralWeaponMotion).GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{player,sync,presentation,look,aim,seconds,false});
                var ik=player.GetComponentsInChildren<WeaponHandIK>(true).Single(h=>h.name=="FPSArms");
                var right=(Transform)typeof(WeaponHandIK).GetField("rightHand",Flags).GetValue(ik);
                var entry=sync.FindPreviewWeapon(id);
                worstGap=Mathf.Max(worstGap,Vector3.Distance(ik.LeftHand.position,entry.leftGrip.position),Vector3.Distance(right.position,entry.rightGrip.position));
                if(frame>0)largestStep=Mathf.Max(largestStep,Quaternion.Angle(previous,sync.WeaponRoot.localRotation));previous=sync.WeaponRoot.localRotation;
            }
            Check(worstGap<.025f&&largestStep<12f,"Full individual reload: unreachable hand or abrupt rotation: "+worstGap+" / "+largestStep);
            for(int tile=0;tile<times.Length;tile++)
            {
                typeof(ValidateProceduralWeaponMotion).GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{player,sync,presentation,look,aim,times[tile],false});
                const string path="Temp/individual-reload-frame.png";
                typeof(ValidateRshWinchester).GetMethod("RenderPose",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{camera,path});
                var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes(path));
                var pixels=new Color[320*180];for(int y=0;y<180;y++)for(int x=0;x<320;x++)pixels[y*320+x]=source.GetPixelBilinear((x+.5f)/320,(y+.5f)/180);
                sheet.SetPixels((tile%4)*320,(2-tile/4)*180,320,180,pixels);UnityEngine.Object.DestroyImmediate(source);
            }
            sheet.Apply();File.WriteAllBytes("Documentation/NewWeapons/"+id+"-individual-reload.png",sheet.EncodeToPNG());
        }
        finally {UnityEngine.Object.DestroyImmediate(sheet);}
    }
}
