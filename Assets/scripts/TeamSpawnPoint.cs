using UnityEngine;

public class TeamSpawnPoint : MonoBehaviour
{
    [SerializeField, Range(1, 2)] private int team = 1;

    public int Team => team;

    private void OnDrawGizmos()
    {
        Gizmos.color = team == 1 ? Color.blue : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.75f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward);
    }
}
