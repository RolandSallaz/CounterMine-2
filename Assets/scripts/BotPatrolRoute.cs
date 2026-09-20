using UnityEngine;

/// <summary>Scene-authored navigation destinations, including exact floor heights.</summary>
public sealed class BotPatrolRoute : MonoBehaviour
{
    [SerializeField] private Vector3[] points = System.Array.Empty<Vector3>();
    public Vector3[] WorldPoints => System.Array.ConvertAll(points, transform.TransformPoint);
    private void OnDrawGizmosSelected()
    {
        Gizmos.color=Color.cyan;
        foreach(var point in points)Gizmos.DrawWireSphere(transform.TransformPoint(point),.5f);
    }
}
