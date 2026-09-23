using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ValidateNewWeapons
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    [MenuItem("Tools/CounterMine/Validate New Weapons")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/NewWeapons");var report=new StringBuilder();
        var player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
        try
        {
            var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);var ammo=player.GetComponent<WeaponAmmo>();var aim=player.GetComponentInChildren<WeaponAimController>(true);
            Call(player.GetComponent<PlayerHealth>(),"Awake");Call(ammo,"Awake");Call(aim,"Awake");
            var camera=player.GetComponentInChildren<Camera>(true);camera.scene=player.scene;
            player.GetComponent<PlayerModelPresentation>().ConfigureView(true);
            var light=new GameObject("Preview key");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light,player.scene);light.AddComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=2.5f;light.transform.rotation=Quaternion.Euler(35,-30,0);
            foreach(string id in new[]{"hk416","l115a3"})
            {
                var entry=InstallNewWeapons.Entries(sync).Single(e=>e.id==id);
                Check(sync.EquipWeapon(id),id+" equip");Check(sync.ProceduralEquip&&!sync.CanFire,id+" draw gate");sync.RestartIdle();
                Check(ammo.MagAmmo==entry.magazineSize&&!ammo.FiniteReserve,id+" magazine");
                Check(sync.WeaponRoot.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader.isSupported)),id+" materials");
                var motion=sync.WeaponRoot.GetComponent<WeaponMagazineMotion>();Call(motion,"OnEnable");
                foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-hip.png");
                Check(Vector3.Distance(camera.transform.position,entry.muzzle.position)<=entry.maximumMuzzleReach,id+" muzzle exceeds server validation reach");
                ammo.Consume();Check(ammo.TryStartReload()&&sync.ProceduralReload&&!sync.CanFire,id+" reload gate");
                var rest=motion.magazine.localPosition;Call(sync,"EvaluatePair",(double)(entry.proceduralReloadSeconds*.45f));
                typeof(WeaponIdleSynchronizer).GetField("elapsedSeconds",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sync,(double)(entry.proceduralReloadSeconds*.45f));
                Call(motion,"LateUpdate");Check(Vector3.Distance(rest,motion.magazine.localPosition)>.001f,id+" magazine did not move");
                foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-reload.png");
                sync.EquipWeapon("ak74");sync.RestartIdle();Call(motion,"OnDisable");
                Check(Vector3.Distance(rest,motion.magazine.localPosition)<.0001f,id+" interrupted reload offset");
                sync.EquipWeapon(id);sync.RestartIdle();Check(ammo.MagAmmo==entry.magazineSize-1,id+" switch refilled ammo");
                Check(ammo.TryStartReload(),id+" reload after interruption");sync.RestartIdle();Call(ammo,"Update");Check(ammo.MagAmmo==entry.magazineSize&&!ammo.IsReloading,id+" completed reload");
                Check(sync.ApplyNetworkState(id,"reload",0,0),id+" network reload binding");sync.RestartIdle();
                Call(aim,"Step",true,1f);Call(aim,"ApplyPose",1f);
                foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
                var point=camera.WorldToViewportPoint(entry.aimRig.ActiveSight.AimPoint.position);Check(Mathf.Abs(point.x-.5f)<.001f&&Mathf.Abs(point.y-.5f)<.001f,id+" ADS alignment");
                if(id=="hk416")
                {
                    var model=sync.WeaponRoot.Find("Model");
                    var post=camera.WorldToViewportPoint(model.TransformPoint(new Vector3(-2.029f,.88583f,0)));
                    Check(Mathf.Abs(post.x-.5f)<.001f&&Mathf.Abs(post.y-.5f)<.001f,"HK front sight is not on the aim axis");
                }
                if(id=="l115a3")
                {
                    var scope=sync.WeaponRoot.GetComponent<ScopedSightView>();Call(scope,"OnEnable");Call(scope,"LateUpdate");Check(scope.Visible,"Scope visibility");
                    Check(scope.lensRenderer.sharedMaterial.shader.isSupported&&!ShaderUtil.ShaderHasError(scope.lensRenderer.sharedMaterial.shader),"Scope shader compilation");
                    InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-ads.png");
                    Check(player.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),"Scope did not restore renderers");
                    Call(scope,"OnDisable");Call(scope,"OnDestroy");
                }
                else InstallNewWeapons.Render(camera,"Documentation/NewWeapons/"+id+"-ads.png");
                Call(aim,"Step",false,1f);Call(aim,"ApplyPose",1f);
                report.AppendLine("PASS "+id+": equipment, ammo, procedural draw/reload, interrupted reload, network reload state, materials and ADS preview.");
            }
            var profile=YandexPlayerData.CreateDefault();int prices=ShopCatalog.Find("hk416").price+ShopCatalog.Find("l115a3").price;profile.money=prices;
            foreach(string id in new[]{"hk416","l115a3"})Check(!profile.Owns(id)&&profile.TryPurchaseAndEquip(id,out _,false),id+" shop purchase");
            Check(profile.money==0&&profile.equippedWeapon=="l115a3"&&profile.equippedPistol=="ucp"&&profile.equippedSkills.Count==2,"Loadout category separation");
            Check(profile.TryPurchaseAndEquip("hk416",out _,false)&&profile.money==0,"Purchased weapon selection costs money");
            var restored=YandexPlayerData.Sanitize(JsonUtility.FromJson<YandexPlayerData>(JsonUtility.ToJson(profile)));
            Check(restored.Owns("l115a3")&&restored.equippedWeapon=="hk416","Purchase/save round trip");
            report.AppendLine("PASS permanent purchases, free reselection, primary slot, preserved starter loadout and save round trip. Real wallet unchanged.");
            report.AppendLine("Editor checks only; live multiplayer and WebGL were not run.");
        }
        catch(Exception e){report.AppendLine("FAIL "+e);Debug.LogException(e);}
        finally{PrefabUtility.UnloadPrefabContents(player);}
        File.WriteAllText("Documentation/NewWeapons/validation.txt",report.ToString());
    }
}
