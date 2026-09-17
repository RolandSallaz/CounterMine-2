using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded reusable surface marks and deterministic low-poly sparks.</summary>
public sealed class BulletImpactEffect : MonoBehaviour
{
    private static readonly List<BulletImpactEffect> pool = new List<BulletImpactEffect>();
    private static int cursor;
    private static Mesh disk, sparkMesh;
    private static Material material;
    private Transform[] sparks;
    private Vector3[] velocities;
    private Vector3 origin;
    private float age, lifetime;

    public static void Spawn(Vector3 point, Vector3 normal, int seed, float duration)
    {
        if (normal.sqrMagnitude < .001f) return;
        material ??= Resources.Load<Material>("VFX/BulletImpact");
        if (material == null) return;
        pool.RemoveAll(effect => effect == null);
        var instance = pool.Find(effect => !effect.gameObject.activeSelf);
        if (instance == null && pool.Count < 64)
        {
            instance = new GameObject("Bullet Impact").AddComponent<BulletImpactEffect>();
            instance.Build(); pool.Add(instance);
        }
        if (instance == null) instance = pool[cursor++ % pool.Count];
        instance.transform.SetParent(null, false);
        instance.origin = point + normal.normalized * .002f;
        var random = new System.Random(seed);
        instance.transform.SetPositionAndRotation(instance.origin, Quaternion.LookRotation(normal) * Quaternion.Euler(0,0,(float)random.NextDouble()*360));
        instance.transform.localScale = Vector3.one;
        // Keep marks on moving cover, but never attach them to characters.
        foreach (var hit in Physics.RaycastAll(point + normal*.025f, -normal, .06f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.GetComponentInParent<PlayerHealth>() == null) { instance.transform.SetParent(hit.collider.transform, true); break; }
        instance.age = 0; instance.lifetime = Mathf.Max(1, duration);
        for (int i=0;i<instance.sparks.Length;i++)
        {
            float angle=(float)random.NextDouble()*Mathf.PI*2;
            var local = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), .4f+(float)random.NextDouble());
            instance.velocities[i] = Quaternion.LookRotation(normal) * local.normalized * (1f+(float)random.NextDouble()*2);
            instance.sparks[i].gameObject.SetActive(true);
            instance.sparks[i].position=instance.origin;
            instance.sparks[i].localScale=Vector3.one*.012f;
        }
        instance.gameObject.SetActive(true);
    }
    private void Build()
    {
        if (disk == null)
        {
            disk = new Mesh { name="Impact mark" };var vertices=new Vector3[10];var triangles=new int[27];
            for(int i=0;i<9;i++) {float angle=i*Mathf.PI*2/9;float radius=.032f*(i%2==0?1:.78f);vertices[i+1]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%9+1;}
            disk.vertices=vertices;disk.triangles=triangles;disk.RecalculateBounds();
            sparkMesh=new Mesh {name="Impact spark"};sparkMesh.vertices=new[]{Vector3.up*2,Vector3.down*2,Vector3.left,Vector3.right,Vector3.forward};
            sparkMesh.triangles=new[]{0,2,4,0,4,3,1,4,2,1,3,4,0,3,2,1,2,3};sparkMesh.RecalculateBounds();
        }
        AddRenderer(transform,disk,new Color(.012f,.01f,.008f));
        sparks=new Transform[7];velocities=new Vector3[7];
        for(int i=0;i<sparks.Length;i++){sparks[i]=new GameObject("Spark").transform;sparks[i].SetParent(transform,false);AddRenderer(sparks[i],sparkMesh,new Color(3f,1.5f,.15f));}
    }
    private static void AddRenderer(Transform target,Mesh mesh,Color color)
    {
        target.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=target.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var block=new MaterialPropertyBlock();block.SetColor("_Color",color);renderer.SetPropertyBlock(block);
    }
    private void Update()
    {
        age+=Time.deltaTime;
        if(age>=lifetime){gameObject.SetActive(false);return;}
        for(int i=0;i<sparks.Length;i++)
        {
            if(age>.28f){sparks[i].gameObject.SetActive(false);continue;}
            sparks[i].position=origin+velocities[i]*age+Vector3.down*(4.9f*age*age);
            sparks[i].rotation=Quaternion.LookRotation(velocities[i]);
            sparks[i].localScale=Vector3.one*(.012f*(1-age/.28f));
        }
    }
}
