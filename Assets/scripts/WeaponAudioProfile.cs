using UnityEngine;

[CreateAssetMenu(menuName = "CounterMine/Weapon Audio", fileName = "WeaponAudio")]
public sealed class WeaponAudioProfile : ScriptableObject
{
    public string displayName = "Weapon";
    public AudioClip[] shots;
    public AudioClip equip, reload, dryFire;
    [Range(0f, 1f)] public float shotVolume = .8f;
    [Min(1f)] public float shotRange = 90f;
    [Range(0f, .1f)] public float pitchVariation = .025f;
}
