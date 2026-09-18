using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual for a single killfeed row. Lives on the entry prefab:
/// killer label, separator ("->" or "died") and victim label + CanvasGroup for fading.
/// </summary>
public sealed class KillfeedEntry : MonoBehaviour
{
    [SerializeField] private Text killerLabel;
    [SerializeField] private Text separatorLabel;
    [SerializeField] private Text victimLabel;
    [SerializeField] private CanvasGroup canvasGroup;

    public CanvasGroup Group => canvasGroup;

    public void Configure(string killerName, string victimName, Color killerColor, Color victimColor, bool suicide, string weaponName = "Unknown")
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        if (suicide)
        {
            if (killerLabel != null) killerLabel.gameObject.SetActive(false);
            if (separatorLabel != null) separatorLabel.text = "died";
            if (victimLabel != null)
            {
                victimLabel.gameObject.SetActive(true);
                victimLabel.text = string.IsNullOrEmpty(victimName) ? "?" : victimName;
                victimLabel.color = victimColor;
            }
            return;
        }

        if (killerLabel != null)
        {
            killerLabel.gameObject.SetActive(true);
            killerLabel.text = string.IsNullOrEmpty(killerName) ? "?" : killerName;
            killerLabel.color = killerColor;
        }
        if (separatorLabel != null) separatorLabel.text = "\u2192 " + weaponName + " \u2192";
        if (victimLabel != null)
        {
            victimLabel.gameObject.SetActive(true);
            victimLabel.text = string.IsNullOrEmpty(victimName) ? "?" : victimName;
            victimLabel.color = victimColor;
        }
    }
}
