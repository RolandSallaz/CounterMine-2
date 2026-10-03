using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] public static class ValidateRshWinchester
{
    static double nextMenuCheck;
    static int menuStep;
    static ValidateRshWinchester(){EditorApplication.update+=Poll;EditorApplication.update+=MenuPoll;}
    static void MenuPoll()
    {
        const string key="ValidateRshWinchester.Menu";
        if(EditorApplication.isCompiling)return;
        if(File.Exists("Temp/validate-rsh-winchester-menu.request")&&!EditorApplication.isPlayingOrWillChangePlaymode)
        {File.Delete("Temp/validate-rsh-winchester-menu.request");SessionState.SetBool(key,true);SessionState.SetFloat(key+".Started",(float)EditorApplication.timeSinceStartup);EditorApplication.EnterPlaymode();return;}
        if(!SessionState.GetBool(key,false)||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<nextMenuCheck)return;
        nextMenuCheck=EditorApplication.timeSinceStartup+1;
        try
        {
            Check(EditorApplication.timeSinceStartup-SessionState.GetFloat(key+".Started",0)<60,"Menu preview timeout");
            var menu=UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();if(menu==null)return;
            if(menuStep==0){Call(menu,"ShowEquippedItem","rsh12");menuStep++;return;}
            if(menuStep==1){ScreenCapture.CaptureScreenshot("Documentation/NewWeapons/rsh12-menu.png");menuStep++;return;}
            if(menuStep==2){Call(menu,"ShowEquippedItem","winchester1897");menuStep++;return;}
            if(menuStep==3){ScreenCapture.CaptureScreenshot("Documentation/NewWeapons/winchester1897-menu.png");menuStep++;return;}
            Check(!Photon.Pun.PhotonNetwork.IsConnected&&PlayerHealth.ActivePlayers.Count==0,"Preview started combat/networking");
            File.WriteAllText("Documentation/NewWeapons/rsh-winchester-menu-validation.txt","PASS both menu previews in Play Mode; no purchases, combat, networking or player build.");
        }
        catch(Exception e){File.WriteAllText("Documentation/NewWeapons/rsh-winchester-menu-validation.txt",e.ToString());Debug.LogException(e);}
        SessionState.SetBool(key,false);menuStep=0;EditorApplication.ExitPlaymode();
    }
    static void Poll(){
        if(!EditorApplication.isCompiling&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/refresh-rsh.request")){File.Delete("Temp/refresh-rsh.request");AssetDatabase.Refresh();return;}
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/validate-rsh-winchester.request"))return;File.Delete("Temp/validate-rsh-winchester.request");Run();}
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);
    static void Field(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    [MenuItem("Tools/CounterMine/Validate RSH-12 and Winchester")]
    public static void Run()
    {
        var report=new StringBuilder();
        var previousTrails=UnityEngine.Object.FindObjectsByType<BulletTrail>(FindObjectsInactive.Include,FindObjectsSortMode.None).ToHashSet();
        try
        {
            foreach(string path in new[]{"Assets/Resources/Player.prefab","Assets/Resources/Bot.prefab"})
            {
                var player=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);var ammo=player.GetComponent<WeaponAmmo>();var aim=player.GetComponentInChildren<WeaponAimController>(true);
                    Call(player.GetComponent<PlayerHealth>(),"Awake");Call(ammo,"Awake");Call(aim,"Awake");
                    var cameraLook=player.GetComponent<PlayerCameraLook>();if(cameraLook!=null)Call(cameraLook,"Awake");
                    var camera=player.GetComponentInChildren<Camera>(true);if(camera!=null)camera.scene=player.scene;
                    var presentation=player.GetComponent<PlayerModelPresentation>();presentation?.ConfigureView(true);
                    var light=new GameObject("Validation light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light,player.scene);light.AddComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=3;light.transform.eulerAngles=new Vector3(35,-30,0);
                    
                    foreach(string id in new[]{"rsh12","winchester1897"})
                    {
                        bool revolver=id=="rsh12";var e=sync.FindPreviewWeapon(id);Check(e!=null,id+" catalog");
                        Check(sync.EquipWeapon(id)&&!sync.CanFire,id+" equip gate");
                        if(camera!=null&&path.Contains("Player"))
                        {
                            double equipTime=e.proceduralEquipSeconds*.2f;
                            Field(sync,"elapsedSeconds",equipTime);Call(sync,"EvaluatePair",equipTime);
                            if(cameraLook!=null)Call(cameraLook,"ApplyRotation");
                            var equipSight=camera.WorldToViewportPoint(e.aimRig.ActiveSight.AimPoint.position);
                            var equipMuzzle=camera.WorldToViewportPoint(e.muzzle.position);
                            bool InFrame(Vector3 point)=>point.z>camera.nearClipPlane&&point.x>.05f&&point.x<.95f&&point.y>.05f&&point.y<.95f;
                            Check(InFrame(equipSight)||InFrame(equipMuzzle),id+" disappears under the camera while drawing");
                            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-equip.png");
                        }
                        e.characterIdle.SampleAnimation(sync.CharacterAnimator.gameObject,0);sync.RestartIdle();
                        if(cameraLook!=null)Call(cameraLook,"ApplyRotation");
                        Check(ammo.MagAmmo==5&&!e.automatic&&e.pelletCount==(revolver?1:8),id+" ammo/fire mode");
                        var mechanism=e.animator.GetComponent<WeaponManualAction>();Call(mechanism,"OnEnable");
                        if(presentation!=null)Call(presentation,"LateUpdate");
                        foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                        if(camera!=null&&path.Contains("Player"))
                        {
                            
                            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-hip.png");
                            Check(Vector3.Distance(camera.transform.position,e.muzzle.position)<=e.maximumMuzzleReach,id+" muzzle reach");
                            Call(aim,"Step",true,1f);Call(aim,"ApplyPose",1f);
                            foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                            var point=camera.WorldToViewportPoint(e.aimRig.ActiveSight.AimPoint.position);Check(Mathf.Abs(point.x-.5f)<.001f&&Mathf.Abs(point.y-.5f)<.001f,id+" ADS axis");
                            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-ads.png");
                            Call(aim,"Step",false,1f);Call(aim,"ApplyPose",1f);
                        }
                        var pumpRest=mechanism.pump!=null?mechanism.pump.localPosition:Vector3.zero;
                        var cylinderRest=mechanism.cylinder!=null?mechanism.cylinder.localRotation:Quaternion.identity;
                        var armRest=mechanism.cylinderArm!=null?mechanism.cylinderArm.localRotation:Quaternion.identity;
                        sync.PlayShotBolt();Field(sync,"boltShotAt",Time.timeAsDouble-.2);Call(mechanism,"LateUpdate");
                        Check(revolver?Quaternion.Angle(cylinderRest,mechanism.cylinder.localRotation)>60:Vector3.Distance(pumpRest,mechanism.pump.localPosition)>.0001f,id+" mechanical shot motion");
                        var grip=e.leftGrip.localPosition;
                        ammo.Consume();Check(ammo.MagAmmo==4,id+" one round per shot");Check(ammo.TryStartReload()&&!sync.CanFire,id+" reload gate");
                        Field(sync,"elapsedSeconds",(double)(e.proceduralReloadSeconds*.45f));Call(sync,"EvaluatePair",(double)(e.proceduralReloadSeconds*.45f));Call(mechanism,"LateUpdate");
                        if(cameraLook!=null)Call(cameraLook,"ApplyRotation");
                        Check(Vector3.Distance(grip,e.leftGrip.localPosition)>.005f,id+" reload hand motion");
                        if(revolver)Check(Quaternion.Angle(mechanism.cylinderArm.localRotation,armRest)>30f,id+" cylinder does not open during reload");
                        if(presentation!=null)Call(presentation,"LateUpdate");
                        foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                        if(camera!=null&&path.Contains("Player"))
                        {
                            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-reload.png");
                            var reloadSight=camera.WorldToViewportPoint(e.aimRig.ActiveSight.AimPoint.position);
                            var reloadMuzzle=camera.WorldToViewportPoint(e.muzzle.position);
                            bool InFrame(Vector3 point)=>point.z>camera.nearClipPlane&&point.x>.05f&&point.x<.95f&&point.y>.05f&&point.y<.95f;
                            Check(InFrame(reloadSight)||InFrame(reloadMuzzle),id+" disappears below the camera during reload: "+reloadSight+" / "+reloadMuzzle);
                        }
                        sync.EquipWeapon("ak74");sync.RestartIdle();sync.EquipWeapon(id);sync.RestartIdle();Check(ammo.MagAmmo==4,id+" interruption must preserve rounds");
                        Check(ammo.TryStartReload(),id+" reload again");sync.RestartIdle();Call(ammo,"Update");Check(ammo.MagAmmo==5&&!ammo.IsReloading,id+" reload completion");
                        Check(sync.ApplyNetworkState(id,"reload",0,0),id+" remote action");sync.RestartIdle();
                        Check(e.animator.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader.isSupported)),id+" materials");
                        var network=player.GetComponent<NetworkWeapon>();var so=new SerializedObject(network);so.FindProperty("muzzleFlashEnabled").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
                        var flights=(IDictionary)typeof(NetworkWeapon).GetField("flights",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(network);flights.Clear();
                        int seq=revolver?101:102;Field(network,"lastSoundSequence",seq);Call(network,"PresentConfirmedShot",seq,Time.timeAsDouble,Vector3.zero,Vector3.forward*e.muzzleVelocity,e.bulletGravity,e.maximumRange,e.damage,e.fullDamageRange,1,id);
                        Check(flights.Count==e.pelletCount,id+" projectile count");
                        Call(network,"PresentConfirmedShot",seq,Time.timeAsDouble,Vector3.zero,Vector3.forward*e.muzzleVelocity,e.bulletGravity,e.maximumRange,e.damage,e.fullDamageRange,1,id);Check(flights.Count==e.pelletCount,id+" duplicate confirm");
                        foreach(DictionaryEntry f in flights){var velocity=(Vector3)f.Value.GetType().GetField("velocity").GetValue(f.Value);Check(Mathf.Abs(velocity.magnitude-e.muzzleVelocity)<.01f,id+" projectile speed");Check(Vector3.Angle(velocity,Vector3.forward)<=e.pelletSpreadDegrees+.01f,id+" pellet cone");}
                        flights.Clear();report.AppendLine("PASS "+path+" "+id+": equip, ADS, materials, ammo, reload interruption/completion, network action, projectile count/cone/speed, duplicate confirmation.");
                    }
                }
                finally{PrefabUtility.UnloadPrefabContents(player);}
            }
            CheckPelletCover();report.AppendLine("PASS deterministic pellet cone, vertical aiming, separate impacts against partial cover, close/distant per-pellet falloff.");
            var profile=YandexPlayerData.CreateDefault();profile.money=1550;
            foreach(string id in new[]{"rsh12","winchester1897"})Check(profile.TryPurchaseAndEquip(id,out _,false),id+" purchase");
            Check(profile.money==0&&profile.equippedPistol=="rsh12"&&profile.equippedWeapon=="winchester1897","shop slots");
            var saved=YandexPlayerData.Sanitize(JsonUtility.FromJson<YandexPlayerData>(JsonUtility.ToJson(profile)));Check(saved.Owns("rsh12")&&saved.Owns("winchester1897"),"save persistence");
            report.AppendLine("PASS isolated shop purchase/save; real wallet unchanged. No player build or two-client network test.");
        }
        catch(Exception e){report.AppendLine("FAIL "+e);Debug.LogException(e);}
        foreach(var t in UnityEngine.Object.FindObjectsByType<BulletTrail>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(!previousTrails.Contains(t))UnityEngine.Object.DestroyImmediate(t.gameObject);
        File.WriteAllText("Documentation/NewWeapons/rsh-winchester-validation.txt",report.ToString());
    }
    static void CheckPelletCover()
    {
        var wall=new GameObject("Weapon validation cover");var backing=new GameObject("Weapon validation backstop");
        try
        {
            Vector3 origin=new Vector3(1234,1234,1234);
            wall.layer=backing.layer=29;
            wall.transform.position=origin+Vector3.forward*5;wall.AddComponent<BoxCollider>().size=new Vector3(.15f,4,.02f);
            backing.transform.position=origin+Vector3.forward*10;backing.AddComponent<BoxCollider>().size=new Vector3(4,4,.02f);
            Physics.SyncTransforms();int near=0,far=0;
            for(int i=0;i<8;i++)
            {
                var v=NetworkWeapon.PelletVelocity(Vector3.forward*380,123,i,8,3.2f);
                Check(v==NetworkWeapon.PelletVelocity(Vector3.forward*380,123,i,8,3.2f),"pellets differ for same shot");
                Check(v!=NetworkWeapon.PelletVelocity(Vector3.forward*380,124,i,8,3.2f),"successive shot patterns identical");
                var vertical=NetworkWeapon.PelletVelocity(Vector3.up*380,123,i,8,3.2f);Check(BulletHitUtility.IsFinite(vertical)&&Mathf.Abs(vertical.magnitude-380)<.01f,"vertical pellet basis");
                var hit=BulletHitUtility.Cast(origin,v.normalized,20,null,0,1<<29);Check(hit.didHit,"pellet passed cover");
                float distance=hit.point.z-origin.z;if(distance<6)near++;else far++;
            }
            Check(near>0&&far>0&&near+far==8,"partial cover did not split pellet impacts");
            Check(NetworkWeapon.DamageAtDistance(13,.15f,10,70,5)==13&&NetworkWeapon.DamageAtDistance(13,.15f,10,70,70)==2,"shotgun falloff");
        }
        finally{UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(backing);}
    }

}
