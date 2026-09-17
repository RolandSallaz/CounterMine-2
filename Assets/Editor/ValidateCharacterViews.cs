using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEngine.Rendering;
[InitializeOnLoad] static class ValidateCharacterViews
{
 static ValidateCharacterViews(){EditorApplication.delayCall+=Run;}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Run(){
 const string report="Documentation/Character/character_views_validation.txt";
 if(File.Exists(report)||EditorApplication.isPlayingOrWillChangePlaymode)return;
 GameObject player=null;
 try{
 player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
 var view=player.GetComponent<PlayerModelPresentation>();var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
 sync.RestartIdle();var body=sync.CharacterAnimator.transform;var bodyStart=body.localPosition;
 view.ConfigureView(true);
 var fps=player.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="FPSArms");var camera=player.GetComponentInChildren<Camera>(true);
 Check(fps.parent==camera.transform,"FPS is not attached to camera");
 Check(body.parent==player.transform&&Mathf.Abs(body.localPosition.z-bodyStart.z+.15f)<.001f,"TPS origin/offset wrong");
 var fpsMeshes=fps.GetComponentsInChildren<SkinnedMeshRenderer>(true);Check(fpsMeshes.Length>0&&fpsMeshes.All(r=>r.name=="arms"&&r.shadowCastingMode==ShadowCastingMode.Off),"FPS contains body/shadows or no arms");
 Check(fps.GetComponentsInChildren<Rigidbody>(true).Length==0,"Duplicated ragdoll physics in FPS");
 Check(body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name=="arms").All(r=>r.shadowCastingMode==ShadowCastingMode.ShadowsOnly),"Local TPS arms visible");
 Check(sync.CharacterAnimator.transform==fps&&sync.CanFire,"FPS animation not bound");
 var bodyPosition=body.position;camera.transform.localRotation=Quaternion.Euler(25,0,0);
 Check(Vector3.Distance(body.position,bodyPosition)<.0001f,"TPS follows camera");camera.transform.localRotation=Quaternion.identity;
 view.ConfigureView(false);
 Check(!fps.gameObject.activeSelf&&sync.CharacterAnimator.transform==body,"Remote uses FPS");
 Check(body.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r=>r.enabled&&r.gameObject.activeInHierarchy&&!r.forceRenderingOff&&r.shadowCastingMode==ShadowCastingMode.On),"Remote model hidden");
 view.ConfigureView(true);sync.RestartIdle();
 var rag=player.GetComponent<PlayerRagdollController>();var gun=sync.WeaponRoot;var originalParent=gun.parent;
 rag.EnterRagdoll(Vector3.forward*4,body.position+Vector3.up);
 Check(!fps.gameObject.activeSelf&&gun.parent.name=="hand_R"&&gun.gameObject.activeInHierarchy,"Ragdoll presentation failed");
 rag.ExitDebugRagdoll();Check(fps.gameObject.activeSelf&&sync.CharacterAnimator.transform==fps&&gun.parent==originalParent&&sync.CanFire,"F8 reset failed");
 File.WriteAllText(report,"PASS: real prefab local/remote presentation, FPS only arms and no shadows/rigidbodies, TPS world root 15cm back, remote TPS renderers active including arms, animation rebound to correct skeleton, ragdoll keeps weapon and hides FPS, debug reset restores FPS and idle/fire.\nEditor validation only; two-client transport/rendering and physical fall direction not simulated.\n");
 }catch(Exception ex){File.WriteAllText("Temp/character_views_validation_error.txt",ex.ToString());Debug.LogException(ex);}
 finally{if(player)PrefabUtility.UnloadPrefabContents(player);}
 }
}
