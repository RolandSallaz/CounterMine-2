using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LocalizedGameText : MonoBehaviour
{
    private Func<string> value;
    private Text label;
    public void SetValue(Func<string> next) { value = next; Refresh(); }
    private void OnEnable() { GameLocalization.Changed += Refresh; Refresh(); }
    private void OnDisable() => GameLocalization.Changed -= Refresh;
    private void Refresh()
    {
        if (label == null) label = GetComponent<Text>();
        if (label != null && value != null) label.text = value();
    }
}
