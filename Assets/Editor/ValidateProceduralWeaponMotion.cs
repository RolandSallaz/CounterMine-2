using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ValidateProceduralWeaponMotion
{
    const string Folder="Documentation/NewWeapons/Motion/";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ValidateProceduralWeaponMotion()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/validate-procedural-motion.request"))return;
        File.Delete("Temp/validate-procedural-motion.request");Run();
    }
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags).Invoke(o,args);
    static void Set(object o,string field,object value)=>o.GetType().GetField(field,Flags).SetValue(o,value);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Sample(GameObject player,WeaponIdleSynchronizer sync,PlayerModelPresentation presentation,PlayerCameraLook look,WeaponAimController aim,double seconds,bool ads=false)
    {
        Set(sync,"elapsedSeconds",seconds);Call(sync,"EvaluatePair",seconds);
        if(look!=null)Call(look,"ApplyRotation");
        Call(aim,"Step",ads,1f);Call(aim,"ApplyPose",1f);
        var magazine=sync.WeaponRoot.GetComponent<WeaponMagazineMotion>();if(magazine!=null){Call(magazine,"OnEnable");Call(magazine,"LateUpdate");}
        var manual=sync.WeaponRoot.GetComponent<WeaponManualAction>();if(manual!=null){Call(manual,"OnEnable");Call(manual,"LateUpdate");}
        Call(presentation,"LateUpdate");
        foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))
        {
            var right=(Transform)typeof(WeaponHandIK).GetField("rightHand",Flags).GetValue(ik);
            Vector3 leftShoulder=ik.LeftHand.parent.parent.localPosition,rightShoulder=right.parent.parent.localPosition;
            Call(ik,"LateUpdate");
            if(sync.WeaponId=="winchester1897")
            {
                Check(Vector3.Distance(leftShoulder,ik.LeftHand.parent.parent.localPosition)<.0001f&&
                    Vector3.Distance(rightShoulder,right.parent.parent.localPosition)<.0001f,"Winchester IK moves a shoulder attachment");
                Vector3 weaponPosition=sync.WeaponRoot.position;
                ik.Solve();
                Check(Vector3.Distance(weaponPosition,sync.WeaponRoot.position)<.001f,"Winchester reach correction accumulates");
            }
        }
    }
    [MenuItem("Tools/CounterMine/Validate Procedural Weapon Motion")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);var report=new StringBuilder();
        try
        {
            Check(WeaponProceduralMotion.Reference!=null,"AK74 motion reference missing");
            foreach(string prefab in new[]{"Player","Bot"})
            {
                var player=PrefabUtility.LoadPrefabContents("Assets/Resources/"+prefab+".prefab");
                try
                {
                    var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
                    var look=player.GetComponent<PlayerCameraLook>();var aim=player.GetComponentInChildren<WeaponAimController>(true);
                    Call(player.GetComponent<PlayerHealth>(),"Awake");Call(player.GetComponent<WeaponAmmo>(),"Awake");Call(aim,"Awake");if(look!=null)Call(look,"Awake");
                    var presentation=player.GetComponent<PlayerModelPresentation>();presentation.ConfigureView(prefab=="Player");
                    var camera=player.GetComponentInChildren<Camera>(true);camera.scene=player.scene;
                    var light=new GameObject("Motion preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light,player.scene);light.AddComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=3;light.transform.eulerAngles=new Vector3(35,-30,0);
                    foreach(var entry in InstallNewWeapons.Entries(sync))
                    foreach(string action in new[]{"equip","reload"})
                    {
                        if(action=="reload"&&entry.finiteReserve)continue;
                        Check(sync.EquipWeapon(entry.id),entry.id+" equip failed");sync.RestartIdle();Sample(player,sync,presentation,look,aim,0);
                        Vector3 restPosition=sync.WeaponRoot.localPosition;Quaternion restRotation=sync.WeaponRoot.localRotation;
                        if(action=="equip")sync.PlayEquip();else Check(sync.PlayWeaponAction(action),entry.id+" reload failed");
                        double duration=(float)typeof(WeaponIdleSynchronizer).GetProperty("ActionDuration",Flags).GetValue(sync);
                        int frames=Mathf.CeilToInt((float)duration*60);float worstGap=0,worstPhase=0,leftGap=0,rightGap=0,peakCamera=0,largestStep=0;Quaternion previous=sync.WeaponRoot.localRotation;
                        var sheet=prefab=="Player"?new Texture2D(1024,288,TextureFormat.RGB24,false):null;int nextTile=0;
                        try
                        {
                            for(int frame=0;frame<=frames;frame++)
                            {
                                float phase=(float)frame/frames;Sample(player,sync,presentation,look,aim,phase*duration);
                                Check(BulletHitUtility.IsFinite(sync.WeaponRoot.position),entry.id+" non-finite pose");
                                float step=Quaternion.Angle(previous,sync.WeaponRoot.localRotation);if(frame>0)largestStep=Mathf.Max(largestStep,step);previous=sync.WeaponRoot.localRotation;
                                peakCamera=Mathf.Max(peakCamera,Quaternion.Angle(Quaternion.identity,sync.CameraRotationOffset));
                                if(entry.id!="ak74")
                                {
                                    var handIK=player.GetComponentsInChildren<WeaponHandIK>(true).Single(ik=>prefab=="Player"?ik.name=="FPSArms":ik.name=="FpsChar");
                                    var right=(Transform)typeof(WeaponHandIK).GetField("rightHand",Flags).GetValue(handIK);
                                    float l=Vector3.Distance(handIK.LeftHand.position,entry.leftGrip.position),r=Vector3.Distance(right.position,entry.rightGrip.position);
                                    if(Mathf.Max(l,r)>worstGap){worstGap=Mathf.Max(l,r);worstPhase=phase;leftGap=l;rightGap=r;}
                                }
                                if(sheet!=null&&nextTile<8&&phase+1f/frames>=nextTile/7f)
                                { Capture(camera,sheet,nextTile++); }
                            }
                            if(entry.id!="ak74"&&worstGap>=.025f)report.AppendLine("FAIL "+prefab+" "+entry.id+" "+action+" left="+leftGap+" right="+rightGap+" phase="+worstPhase);
                            Check(largestStep<12f,prefab+" "+entry.id+" "+action+" sudden pose jump="+largestStep);
                            Sample(player,sync,presentation,look,aim,duration);Vector3 end=sync.WeaponRoot.localPosition;Quaternion endRotation=sync.WeaponRoot.localRotation;
                            sync.RestartIdle();Sample(player,sync,presentation,look,aim,0);
                            if(entry.id!="ak74")Check(Vector3.Distance(end,restPosition)<.001f&&Quaternion.Angle(endRotation,restRotation)<.05f,entry.id+" end pose does not return to idle");
                            if(sheet!=null){sheet.Apply();File.WriteAllBytes(Folder+entry.id+"-"+action+".png",sheet.EncodeToPNG());}
                            report.AppendLine("PASS "+prefab+" "+entry.id+" "+action+": "+frames+" frames, max grip gap="+worstGap.ToString("F4")+"m, camera peak="+peakCamera.ToString("F2")+"deg, maximum frame rotation="+largestStep.ToString("F2")+"deg.");
                        }
                        finally{if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);}
                    }
                    Check(sync.EquipWeapon("winchester1897"),"Winchester pump equip failed");
                    sync.RestartIdle();
                    var winchester=InstallNewWeapons.Entries(sync).Single(e=>e.id=="winchester1897");
                    foreach(bool ads in new[]{false,true})
                    {
                        float worst=0f;string worstDetails="";
                        for(int frame=0;frame<=60;frame++)
                        {
                            Set(sync,"boltShotAt",(double)typeof(WeaponIdleSynchronizer).GetProperty("Now",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)-frame/60.0);
                            Sample(player,sync,presentation,look,aim,0,ads);
                            var ik=player.GetComponentsInChildren<WeaponHandIK>(true).Single(h=>prefab=="Player"?h.name=="FPSArms":h.name=="FpsChar");
                            var right=(Transform)typeof(WeaponHandIK).GetField("rightHand",Flags).GetValue(ik);
                            float gap=Mathf.Max(Vector3.Distance(ik.LeftHand.position,winchester.leftGrip.position),Vector3.Distance(right.position,winchester.rightGrip.position));
                            if(gap>worst){worst=gap;worstDetails=" frame="+frame+" L="+winchester.leftGrip.position+" LS="+ik.LeftHand.parent.parent.position+" R="+winchester.rightGrip.position+" RS="+right.parent.parent.position+" retreat="+ik.GetWeaponRetreat(camera.transform.forward);}
                            if(ads&&prefab=="Player")
                            {
                                Vector3 sight=camera.WorldToViewportPoint(winchester.aimRig.ActiveSight.AimPoint.position);
                                Check(Mathf.Abs(sight.x-.5f)<.001f&&Mathf.Abs(sight.y-.5f)<.001f,"Winchester reach fitting moves the ADS axis");
                                Check(Mathf.Abs(sight.z-.32f)<.002f,"Winchester IK changes the configured eye relief");
                                var model=sync.WeaponRoot.Find("Model");
                                Vector3 bead=camera.WorldToViewportPoint(model.TransformPoint(InstallRshWinchester.WinchesterBead(model)));
                                Check(Mathf.Abs(bead.x-.5f)<.002f&&Mathf.Abs(bead.y-.5f)<.002f,"Winchester physical front bead is off the aim axis");
                            }
                        }
                        Check(worst<.025f,"Winchester pump grip misses: "+prefab+" ADS="+ads+" gap="+worst+worstDetails);
                        report.AppendLine("PASS "+prefab+" Winchester "+(ads?"ADS":"hip")+" pump: 61 frames, fixed shoulder attachments, repeatable solve, max grip gap="+worst.ToString("F4")+"m.");
                    }
                }
                finally{PrefabUtility.UnloadPrefabContents(player);}
            }
            Check(!report.ToString().Contains("FAIL"),"One or more sampled poses failed the grip threshold");
            report.AppendLine("PASS frame sampling, finite poses, grip reach, idle endpoints; AK74 authored clips unchanged. Milkor remains finite-ammo with no reload. No player build or two-client session.");
        }
        catch(Exception e){report.AppendLine("FAIL "+e);Debug.LogException(e);}
        File.WriteAllText(Folder+"validation.txt",report.ToString());
    }
    static void Capture(Camera camera,Texture2D sheet,int tile)
    {
        const string path="Temp/procedural-motion-frame.png";
        if(tile==7&&camera.GetComponent<WeaponIdleSynchronizer>().WeaponId=="ak74")InstallNewWeapons.Render(camera,Folder+"ak74-original-render.png");
        typeof(ValidateRshWinchester).GetMethod("RenderPose",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{camera,path});
        var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes(path));
        try
        {
            var pixels=new Color[256*144];for(int y=0;y<144;y++)for(int x=0;x<256;x++)pixels[y*256+x]=source.GetPixelBilinear((x+.5f)/256,(y+.5f)/144);
            sheet.SetPixels((tile%4)*256,(1-tile/4)*144,256,144,pixels);
        }
        finally{UnityEngine.Object.DestroyImmediate(source);}
    }
}
