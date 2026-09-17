using System;using System.IO;using System.Linq;using System.Reflection;using UnityEngine;using UnityEditor;
[InitializeOnLoad] static class ValidateCombatChanges
{
 static ValidateCombatChanges(){EditorApplication.delayCall+=Run;}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Call(object target,string method)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
 static void Run(){
 const string report="Documentation/Character/combat_validation.txt";
 if(File.Exists(report)||EditorApplication.isPlayingOrWillChangePlaymode)return;
 GameObject player=null;
 try{
 Check(PlayerHitboxes.RayBox(new Vector3(0,0,-2),Vector3.forward,Vector3.zero,Quaternion.identity,Vector3.one*.5f,out var d,out var n)&&Mathf.Abs(d-1.5f)<.001f&&n==Vector3.back,"Box front ray");
 Check(!PlayerHitboxes.RayBox(new Vector3(2,0,-2),Vector3.forward,Vector3.zero,Quaternion.identity,Vector3.one*.5f,out d,out n),"Box parallel miss");
 player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");player.transform.position=new Vector3(80,0,35);
 var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);var model=player.GetComponent<PlayerModelPresentation>();model.ConfigureView(false);
 Check(sync.ApplyNetworkState("ak74","idle",Time.timeAsDouble-120,1),"Late idle snapshot failed");
 var skeleton=player.GetComponent<PlayerRagdollController>().SkeletonRoot;
 var bones=skeleton.GetComponentsInChildren<Transform>();var baseline=bones.Select(b=>b.localPosition).ToArray();
 var ik=skeleton.GetComponent<WeaponHandIK>();
 for(int step=0;step<300;step++){
 player.transform.position+=Vector3.right*.01f;Call(sync,"Update");Call(ik,"LateUpdate");
 foreach(var bone in bones)Check(BulletHitUtility.IsFinite(bone.position)&&Vector3.Distance(bone.position,player.transform.position)<4,"Stretched late-join bone: "+bone.name);
 }
 Check(player.GetComponentsInChildren<Rigidbody>(true).All(b=>b.isKinematic&&b.interpolation==RigidbodyInterpolation.None),"Animated rigidbody interpolation active");
 var hitboxes=player.GetComponent<PlayerHitboxes>();Check(hitboxes.Ready,"Missing TPS hitboxes");
 var head=bones.Single(b=>b.name=="Head").GetComponent<BoxCollider>();var headCenter=head.transform.TransformPoint(head.center);
 hitboxes.Record(100);
 Check(hitboxes.Raycast(headCenter+Vector3.forward*2,Vector3.back,4,100,out d,out n,out var zone)&&zone==PlayerHitboxes.Zone.Head,"Head zone missed: "+zone);
 Check(hitboxes.Multiplier(zone)==3&&hitboxes.Multiplier(PlayerHitboxes.Zone.Arm)<1&&hitboxes.Multiplier(PlayerHitboxes.Zone.Leg)<1,"Damage modifiers invalid");
 player.transform.position+=Vector3.right*5;hitboxes.Record(101);
 Check(hitboxes.Raycast(headCenter+Vector3.forward*2,Vector3.back,4,100,out d,out n,out zone)&&zone==PlayerHitboxes.Zone.Head,"Rewind lost old head position");
 Check(!hitboxes.Raycast(headCenter+Vector3.forward*2,Vector3.back,4,101,out d,out n,out zone),"Current pose uses stale hitbox");
 var mat=Resources.Load<Material>("VFX/BulletImpact");Check(mat!=null&&mat.shader!=null&&!ShaderUtil.ShaderHasError(mat.shader),"Impact shader/material missing or invalid");
 File.WriteAllText(report,"PASS: OBB ray hit/miss; late idle snapshot at distant spawn + 300 animation/IK updates while translating player stays within 4m; animated ragdoll has no interpolation; TPS head detection and damage multipliers; historical head hit survives player displacement, current hit misses; impact material/shader exists without shader errors.\nEditor checks only; exact reported two-client stretching and VFX appearance still need live verification.");
 }catch(Exception ex){File.WriteAllText("Temp/combat_validation_error.txt",ex.ToString());Debug.LogException(ex);}
 finally{if(player)PrefabUtility.UnloadPrefabContents(player);}
 }
}
