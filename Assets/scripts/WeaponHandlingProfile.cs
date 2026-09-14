using UnityEngine;

[CreateAssetMenu(menuName = "CounterMine/Weapon Handling", fileName = "WeaponHandling")]
public sealed class WeaponHandlingProfile : ScriptableObject
{
    [Range(0f, 100f)] public float ergonomics = 55f;
    [Min(.01f)] public float lowErgonomicsAimTime = .5f;
    [Min(.01f)] public float highErgonomicsAimTime = .16f;
    [Range(.1f, 1f)] public float aimOutTimeMultiplier = .8f;
    [Range(.1f, 1f)] public float aimedSensitivity = .7f;
    [Range(.1f, 1f)] public float aimedMovementSpeed = .65f;
    [Min(.01f)] public float sightSwitchTime = .16f;
}
