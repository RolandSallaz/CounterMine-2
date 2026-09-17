using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>Two shared palettes for the same character mesh.</summary>
public sealed class CharacterTeamColors : MonoBehaviourPunCallbacks
{
    [Serializable]
    public struct MaterialPair
    {
        public Material team1;
        public Material team2;
    }

    [SerializeField, Range(1, 2)] private int previewTeam = 1;
    [SerializeField] private MaterialPair[] palette = Array.Empty<MaterialPair>();
    private PhotonView ownerView;

    public override void OnEnable()
    {
        base.OnEnable();
        ownerView = GetComponentInParent<PhotonView>();
        RefreshTeam();
    }

    public override void OnPlayerPropertiesUpdate(Player player, Hashtable changedProps)
    {
        if (ownerView != null && player == ownerView.Owner && changedProps.ContainsKey("team"))
            RefreshTeam();
    }

    private void RefreshTeam()
    {
        int team = previewTeam;
        if (ownerView != null && ownerView.Owner != null &&
            ownerView.Owner.CustomProperties.TryGetValue("team", out object value) && value is int selected)
            team = selected;
        ApplyTeam(team);
    }

    public void ApplyTeam(int team)
    {
        team = team == 2 ? 2 : 1;
        foreach (Renderer target in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = target.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                foreach (MaterialPair pair in palette)
                {
                    if (pair.team1 == null || pair.team2 == null) continue;
                    if (materials[i] != pair.team1 && materials[i] != pair.team2) continue;
                    Material desired = team == 2 ? pair.team2 : pair.team1;
                    if (materials[i] != desired) { materials[i] = desired; changed = true; }
                    break;
                }
            }
            if (changed) target.sharedMaterials = materials;
        }
    }

    [ContextMenu("Preview Team 1")]
    private void PreviewTeam1() { previewTeam = 1; ApplyTeam(1); }
    [ContextMenu("Preview Team 2")]
    private void PreviewTeam2() { previewTeam = 2; ApplyTeam(2); }
}
