using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ValidateWeaponActionOffsets
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ValidateWeaponActionOffsets() => EditorApplication.delayCall += () =>
    {
        if(!File.Exists("Temp/validate-weapon-offsets.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete("Temp/validate-weapon-offsets.request");Run();
    };
    static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    [MenuItem("Tools/CounterMine/Validate Weapon Action Offsets")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        GameObject player=null;
        Directory.CreateDirectory("Documentation/UCP");
        try
        {
            player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
            var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var sway=player.GetComponentInChildren<WeaponSway>(true);
            var recoil=player.GetComponentInChildren<WeaponRecoilController>(true);
            var aim=player.GetComponentInChildren<WeaponAimController>(true);
            Call(sway,"Awake");Call(recoil,"Awake");Call(recoil,"OnEnable");Call(aim,"Awake");
            Vector3 aimRest=aim.transform.localPosition;Quaternion aimRotation=aim.transform.localRotation;
            foreach(var weapon in new[]{"ak74","ucp"})
            foreach(var action in new[]{"equip","reload"})
            {
                Check(sync.EquipWeapon(weapon),"Weapon unavailable");sync.RestartIdle();
                Call(sway,"Step",new Vector2(240,100),.1f);
                Check(Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition)>.001f,"Idle sway missing");
                Check(sync.PlayWeaponAction(action),"Action unavailable");
                recoil.transform.localPosition+=Vector3.one*.01f;
                aim.transform.localPosition+=Vector3.one*.1f;
                typeof(WeaponAimController).GetField("progress",Flags).SetValue(aim,1f);
                typeof(WeaponAimController).GetField("<AimAmount>k__BackingField",Flags).SetValue(aim,1f);
                for(int i=0;i<5;i++)
                {
                    Call(sway,"Step",new Vector2(500,-500),1f/60);
                    Call(sway,"StepWalk",4.8f,true,false,false,false,1f/60);
                    Call(sway,"ApplySprint",1f/60);Call(recoil,"LateUpdate");Call(aim,"ApplyPose",1f/60);
                    Check(Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition)<.00001f&&Quaternion.Angle(sway.transform.localRotation,sway.NeutralLocalRotation)<.001f,"Sway moves authored action");
                    Check(Vector3.Distance(recoil.transform.localPosition,recoil.NeutralLocalPosition)<.00001f&&Quaternion.Angle(recoil.transform.localRotation,recoil.NeutralLocalRotation)<.001f,"Recoil moves authored action");
                    Check(Vector3.Distance(aim.transform.localPosition,aimRest)<.00001f&&Quaternion.Angle(aim.transform.localRotation,aimRotation)<.001f&&aim.AimAmount==0,"ADS moves authored action");
                }
                sync.RestartIdle();Call(sway,"Step",Vector2.zero,1f/60);
                Check(Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition)<.00001f,"Stale sway after action");
                Call(sway,"Step",new Vector2(240,0),1f/60);
                float offset=Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition);
                Check(offset>0&&offset<.01f,"Sway does not return smoothly");
            }
            sync.RestartIdle();
            for(int i=0;i<120;i++){Call(sway,"Step",Vector2.zero,1f/60);Call(sway,"StepWalk",4.8f,true,false,false,false,1f/60);}
            Check(Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition)>.001f,"Walking bob missing");
            for(int i=0;i<120;i++){Call(sway,"Step",Vector2.zero,1f/60);Call(sway,"StepWalk",0f,true,false,false,false,1f/60);}
            Check(Vector3.Distance(sway.transform.localPosition,sway.NeutralLocalPosition)<.0001f,"Bob persists at rest");
            var handling=AssetDatabase.LoadAssetAtPath<WeaponHandlingProfile>("Assets/Anims/UCP/UCP_Handling.asset");
            var direction=Quaternion.Euler(handling.sprintRotation)*Vector3.forward;
            Check(direction.y>.7f&&Mathf.Abs(direction.x)<.2f,"Pistol sprint is not pointing upward");
            File.WriteAllText("Documentation/UCP/action-offset-validation.txt","PASS: AK74/UCP equip and reload suppress sway, walking bob, sprint carry, residual recoil and ADS; idle sway resumes smoothly without stored look input. Walking bob appears with movement and fades at rest. UCP sprint points upward. Real prefab editor checks; live input/network match not run.\n");
        }
        catch(Exception e){File.WriteAllText("Documentation/UCP/action-offset-validation.txt","FAIL: "+e);Debug.LogException(e);}
        finally {if(player!=null)PrefabUtility.UnloadPrefabContents(player);}
    }
}
