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
    [Header("Sprint carry (relative to the right grip)")]
    public Vector3 sprintPosition = new Vector3(-.035f, -.015f, -.035f);
    public Vector3 sprintRotation = new Vector3(10f, -32f, -18f);
    [Min(.01f)] public float sprintBlendTime = .22f;
    [Range(0f, .05f)] public float sprintBob = .012f;
    [Range(.1f, 2f)] public float sprintBobFrequency = .5f;
}
