using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>One scene-wide navigation mesh, built from static gameplay colliders.</summary>
public sealed class BotNavigation : MonoBehaviour
{
    private static BotNavigation instance;
    private NavMeshData data;
    private NavMeshDataInstance handle;
    public bool Ready { get; private set; }
    public int AgentType { get; private set; }
    private AsyncOperation build;
    private NavMeshPath connectivityPath;
    public static BotNavigation Ensure()
    {
        if (instance == null)
        {
            instance = new GameObject("Bot Navigation").AddComponent<BotNavigation>();
            instance.StartCoroutine(instance.Build());
        }
        return instance;
    }
    public static NavMeshBuildSettings Settings()
    {
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = .28f;
        settings.agentHeight = 1.8f;
        settings.agentClimb = .3f;
        settings.agentSlope = 40;
        settings.overrideVoxelSize = true;
        settings.voxelSize = .07f;
        return settings;
    }
    public static Bounds Collect(Scene scene, List<NavMeshBuildSource> sources)
    {
        sources.Clear();
        Vector3 center = Vector3.zero;
        int spawns = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!root.activeInHierarchy) continue;
            foreach (var spawn in root.GetComponentsInChildren<TeamSpawnPoint>()) { center += spawn.transform.position; spawns++; }
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<PlayerHealth>() != null ||
                    collider.GetComponentInParent<TeamSafeZone>() != null) continue;
                var source = new NavMeshBuildSource { area = 0, component = collider };
                if (collider is BoxCollider box)
                {
                    source.shape = NavMeshBuildSourceShape.Box;
                    source.transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center);
                    source.size = box.size;
                }
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
                {
                    source.shape = NavMeshBuildSourceShape.Mesh;
                    source.transform = mesh.transform.localToWorldMatrix;
                    source.sourceObject = mesh.sharedMesh;
                }
                else continue;
                sources.Add(source);
            }
        }
        // Area volumes allow own-team sanctuary routes while excluding the enemy's.
        foreach (var root in scene.GetRootGameObjects())
            if (root.activeInHierarchy)
            foreach (var zone in root.GetComponentsInChildren<TeamSafeZone>())
            {
                var box = zone.GetComponent<BoxCollider>();
                if (box == null || !box.enabled) continue;
                var scale = box.transform.lossyScale;
                sources.Add(new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(box.transform.TransformPoint(box.center), box.transform.rotation, Vector3.one),
                    size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))) + Vector3.one * .6f,
                    area = zone.Team == 1 ? 3 : 4
                });
            }
        if (spawns > 0) center /= spawns;
        center.y = 5;
        // XL-friendly: covers spawn lines at +/-120 with margin (was 108x24x50).
        return new Bounds(center, new Vector3(320, 60, 170));
    }
    private IEnumerator Build()
    {
        // Let scene actors initialize their colliders and safe zones first.
        yield return null;
        var sources = new List<NavMeshBuildSource>();
        var bounds = Collect(gameObject.scene, sources);
        var settings = Settings();
        AgentType = settings.agentTypeID;
        data = new NavMeshData(AgentType);
        build = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        yield return build;
        handle = NavMesh.AddNavMeshData(data);
        Ready = handle.valid;
        if (!Ready) Debug.LogError("[Bots] Navigation mesh could not be built.", this);
    }
    public NavMeshQueryFilter Filter(int team) => new NavMeshQueryFilter {
        agentTypeID = AgentType, areaMask = NavMesh.AllAreas & ~(1 << (team == 1 ? 4 : 3))
    };
    public bool Sample(Vector3 point, int team, float radius, out Vector3 result)
    {
        result = point;
        if (!Ready || !NavMesh.SamplePosition(point, out var hit, radius, Filter(team)) || TeamSafeZone.IsEnemyArea(hit.position, team)) return false;
        result = hit.position;
        return true;
    }
    public bool TryGetSpawnAnchor(Vector3 marker, int team, out Vector3 anchor) => Sample(marker,team,3f,out anchor);

    public bool CanReach(Vector3 from, Vector3 to, int team)
    {
        if(!Sample(from,team,1.2f,out var start) || !Sample(to,team,1.2f,out var end)) return false;
        connectivityPath ??= new NavMeshPath();
        return NavMesh.CalculatePath(start,end,Filter(team),connectivityPath) &&
            connectivityPath.status==NavMeshPathStatus.PathComplete;
    }
    private void OnDestroy()
    {
        Ready = false;
        if (data != null) NavMeshBuilder.Cancel(data);
        if (handle.valid) handle.Remove();
        if (data != null) Destroy(data);
        if (instance == this) instance = null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;
}
