using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LocalizedGameText : MonoBehaviour
{
    private Func<string> value;
    private Text label;
    private YG.LanguageLegacy.LanguageYG translator;
    [SerializeField] private string textSource;
    public void SetValue(Func<string> next) { value = next; translator = null; textSource = null; Refresh(); }
    public void SetSource(string source)
    {
        label = GetComponent<Text>();
        textSource = source;
        value = null;
        translator = GameLocalization.Configure(label, source);
        Refresh();
    }
    private void OnEnable()
    {
        GameLocalization.Changed += Refresh;
        if (translator == null && !string.IsNullOrEmpty(textSource)) SetSource(textSource);
        Refresh();
    }
    private void OnDisable() => GameLocalization.Changed -= Refresh;
    private void Refresh()
    {
        if (label == null) label = GetComponent<Text>();
        if (label != null && value != null) label.text = value();
        else if (translator != null) translator.AssignTranslate();
    }
}
