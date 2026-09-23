using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ValidatePiPScope
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);
    [MenuItem("Tools/CounterMine/Validate PiP Scope")]
    public static void Run()
    {
        Directory.CreateDirectory("Documentation/NewWeapons");var report=new StringBuilder();
        var player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");ScopedSightView scope=null;
        var materials=new System.Collections.Generic.List<Material>();
        try
        {
            var health=player.GetComponent<PlayerHealth>();var ammo=player.GetComponent<WeaponAmmo>();var sync=player.GetComponentInChildren<WeaponIdleSynchronizer>(true);
            var aim=player.GetComponentInChildren<WeaponAimController>(true);var camera=player.GetComponentInChildren<Camera>(true);camera.scene=player.scene;camera.aspect=1280f/720;
            Call(health,"Awake");Call(ammo,"Awake");Call(aim,"Awake");float peripheralFov=camera.fieldOfView;
            Check(sync.EquipWeapon("l115a3"),"L115A3 not installed");sync.RestartIdle();player.GetComponent<PlayerModelPresentation>().ConfigureView(true);
            Call(aim,"Step",true,1f);Call(aim,"ApplyPose",1f);
            foreach(var ik in player.GetComponentsInChildren<WeaponHandIK>(true))Call(ik,"LateUpdate");
            Check(Mathf.Abs(camera.fieldOfView-peripheralFov)<.001f,"PiP zoom changed peripheral FOV");
            scope=sync.WeaponRoot.GetComponent<ScopedSightView>();Call(scope,"OnEnable");Check(scope.Visible,"Scope should be visible in ADS");
            Check(scope.Magnification==6,"Default magnification");
            var shader=scope.lensRenderer.sharedMaterial.shader;Check(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"PiP shader failed compilation");
            void Cube(string name,Vector3 position,Vector3 size,Color color)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,player.scene);
                go.transform.SetPositionAndRotation(camera.transform.TransformPoint(position),camera.transform.rotation);go.transform.localScale=size;
                var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",color);materials.Add(material);go.GetComponent<Renderer>().sharedMaterial=material;
            }
            Cube("Target backboard",new Vector3(0,0,42),new Vector3(25,15,.1f),new Color(.65f,.69f,.66f));
            for(int y=-4;y<=4;y++)for(int x=-6;x<=6;x++)
                if((x+y)%2==0)Cube("Grid",new Vector3(x*.8f,y*.8f,41.8f),new Vector3(.79f,.79f,.1f),new Color(.22f,.29f,.31f));
            Cube("Red measurement target",new Vector3(.35f,.25f,40),new Vector3(.35f,.35f,.08f),new Color(.95f,.025f,.012f));
            Cube("Blue peripheral landmark",new Vector3(-6,2,38),new Vector3(1,1,.2f),new Color(.05f,.2f,.95f));
            var light=new GameObject("PiP preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light,player.scene);light.AddComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=2.5f;light.transform.rotation=Quaternion.Euler(30,-30,0);
            camera.backgroundColor=new Color(.22f,.28f,.34f);
            Call(scope,"LateUpdate");
            Check(scope.ViewTexture!=null&&scope.ViewTexture.IsCreated()&&scope.ScopeCamera!=null&&!scope.ScopeCamera.enabled,"PiP camera/texture not initialized or renders automatically");
            float fov6=scope.ScopeCamera.fieldOfView;int width6=SaveTexture(scope.ViewTexture,"scope-texture-6x.png");
            Check(width6>8,"Scope render does not contain the scene target");
            Check(scope.GetComponentInChildren<Text>(true).text=="6?","Zoom label at 6x");
            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/pip-6x.png");
            scope.SetMagnification(99);Check(scope.Magnification==16,"Upper zoom clamp");Call(scope,"LateUpdate");
            float fov16=scope.ScopeCamera.fieldOfView;int width16=SaveTexture(scope.ViewTexture,"scope-texture-16x.png");
            Check(Mathf.Abs((float)width16/width6-16f/6)<.12f,"Actual rendered zoom ratio incorrect: "+width6+" / "+width16);
            Check(scope.GetComponentInChildren<Text>(true).text=="16?","Zoom label at 16x");
            Check(fov16<fov6&&Mathf.Abs(camera.fieldOfView-peripheralFov)<.001f,"Zoom camera separation");
            InstallNewWeapons.Render(camera,"Documentation/NewWeapons/pip-16x.png");
            scope.SetMagnification(5);Check(scope.Magnification==6,"Lower zoom clamp");scope.SetMagnification(7);Check(scope.Magnification==7,"One-times zoom step");
            Check(player.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),"Player renderers were left hidden");
            Check(scope.lensRenderer.enabled,"PiP lens not visible");
            Call(aim,"Step",false,1f);Call(aim,"ApplyPose",1f);Call(scope,"LateUpdate");
            Check(!scope.Visible&&!scope.lensRenderer.enabled&&!scope.GetComponentInChildren<Canvas>(true).gameObject.activeSelf,"Scope persists outside ADS");
            Call(aim,"Step",true,1f);Call(aim,"ApplyPose",1f);ammo.Consume();Check(ammo.TryStartReload(),"Reload setup");Call(aim,"ApplyPose",1f);Call(scope,"LateUpdate");
            Check(!scope.Visible&&!scope.lensRenderer.enabled,"Scope persists during reload");
            sync.RestartIdle();sync.EquipWeapon("ak74");Check(!scope.Visible,"Scope persists after weapon switch");
            Call(scope,"OnDisable");Check(!scope.ScopeCamera.enabled,"Inactive scope camera costs a render");Call(scope,"OnDestroy");Check(scope.ViewTexture==null,"RenderTexture not released");
            report.AppendLine("PASS PiP scene rendering, shader, independent peripheral FOV, 6x/16x limits and 1x steps, zoom labels, render cleanup, ADS/reload/switch gates and texture release.");
            report.AppendLine("Rendered red target widths: "+width6+" px at 6x; "+width16+" px at 16x. Camera FOVs: "+fov6.ToString("F3")+" / "+fov16.ToString("F3")+" degrees; peripheral "+peripheralFov+" degrees.");
            report.AppendLine("Editor URP rendering verified; WebGL performance and live mouse-wheel interaction were not exercised.");
        }
        catch(Exception e){report.AppendLine("FAIL "+e);Debug.LogException(e);}
        finally
        {
            if(scope!=null){Call(scope,"OnDisable");Call(scope,"OnDestroy");}
            PrefabUtility.UnloadPrefabContents(player);foreach(var material in materials)UnityEngine.Object.DestroyImmediate(material);
        }
        File.WriteAllText("Documentation/NewWeapons/pip-validation.txt",report.ToString());
    }
    static int SaveTexture(RenderTexture rt,string filename)
    {
        var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes("Documentation/NewWeapons/"+filename,image.EncodeToPNG());
            var pixels=image.GetPixels32();int min=rt.width,max=-1;
            for(int y=0;y<rt.height;y++)for(int x=0;x<rt.width;x++)
            {var p=pixels[y*rt.width+x];if(p.r>190&&p.g<100&&p.b<100){min=Mathf.Min(min,x);max=Mathf.Max(max,x);}}
            return max>=min?max-min+1:0;
        }
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
    }
}
