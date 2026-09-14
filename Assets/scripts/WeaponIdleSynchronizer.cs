using Animancer;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Samples first-person arms and weapon from one clock, including clips of different lengths.</summary>
[DisallowMultipleComponent]
public sealed class WeaponIdleSynchronizer : MonoBehaviour
{
    [SerializeField] private AnimancerComponent armsAnimancer;
    [SerializeField] private AnimancerComponent weaponAnimancer;
    [SerializeField] private AnimationClip armsIdleClip;
    [SerializeField] private AnimationClip weaponIdleClip;
    [SerializeField, Min(0f)] private float playbackSpeed = 1f;

    private AnimancerState armsState;
    private AnimancerState weaponState;
    private double elapsedSeconds;

    public double NormalizedTime => armsState != null ? armsState.NormalizedTimeD : 0;

    private void OnEnable() => RestartIdle();

    public void RestartIdle()
    {
        if (armsAnimancer == null || weaponAnimancer == null ||
            armsAnimancer == weaponAnimancer || armsIdleClip == null || weaponIdleClip == null ||
            armsAnimancer.Animator == null || weaponAnimancer.Animator == null ||
            armsIdleClip.length <= 0f || weaponIdleClip.length <= 0f)
        {
            Debug.LogError("Assign separate arms/weapon Animancers and non-empty idle clips.", this);
            return;
        }

        armsState = armsAnimancer.Play(armsIdleClip);
        weaponState = weaponAnimancer.Play(weaponIdleClip);
        // Neither graph advances independently: both poses are evaluated at the exact same phase.
        armsAnimancer.Playable.UpdateMode = DirectorUpdateMode.Manual;
        weaponAnimancer.Playable.UpdateMode = DirectorUpdateMode.Manual;
        elapsedSeconds = 0;
        EvaluateIdle(elapsedSeconds);
    }

    private void Update()
    {
        if (armsState == null || weaponState == null) return;
        elapsedSeconds += Time.deltaTime * playbackSpeed;
        EvaluateIdle(elapsedSeconds);
    }

    private void EvaluateIdle(double seconds)
    {
        double phase = seconds / armsIdleClip.length;
        phase -= System.Math.Floor(phase);
        armsState.NormalizedTimeD = phase;
        weaponState.NormalizedTimeD = phase;
        armsAnimancer.Evaluate(0f);
        weaponAnimancer.Evaluate(0f);
    }

    private void OnDisable()
    {
        if (armsAnimancer != null && armsAnimancer.IsPlayableInitialized)
            armsAnimancer.Playable.PauseGraph();
        if (weaponAnimancer != null && weaponAnimancer.IsPlayableInitialized)
            weaponAnimancer.Playable.PauseGraph();
        armsState = null;
        weaponState = null;
    }
}
