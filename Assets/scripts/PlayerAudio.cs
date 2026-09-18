using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAudio : MonoBehaviour
{
    private WeaponIdleSynchronizer animationSource;
    private PlayerHealth health;
    private PlayerController movement;
    private PlayerAnimancerController pose;
    private PhotonView view;
    private CharacterController capsule;
    private Vector3 previous;
    private float distance;
    private int step;
    private string action;
    private double started = double.NaN;
    private AudioSource actionVoice;
    private AudioClip actionClip;
    private int voiceLease;
    private bool Local => !BotController.IsBot(this) && (!PhotonNetwork.InRoom || view.IsMine);
    private void Start()
    {
        animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        health = GetComponent<PlayerHealth>(); movement = GetComponent<PlayerController>();
        pose = GetComponent<PlayerAnimancerController>(); view = GetComponent<PhotonView>();
        capsule = GetComponent<CharacterController>(); previous = transform.position;
    }
    private void Update()
    {
        Vector3 delta = transform.position - previous; previous = transform.position;
        if (health != null && health.IsDead) { StopAction(); return; }
        if (animationSource != null)
        {
            if (action != animationSource.ActionId || started != animationSource.StartedAt)
            {
                StopAction(); action = animationSource.ActionId; started = animationSource.StartedAt;
                var profile = animationSource.AudioProfile;
                actionClip = profile == null ? null : action == "reload" ? profile.reload : action == "equip" ? profile.equip : null;
                double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
                float elapsed = Mathf.Max(0f, (float)(now - started) * animationSource.PlaybackSpeed);
                actionVoice = GameAudio.Play(actionClip, transform.position + Vector3.up, .5f, 18f, Local,
                    animationSource.PlaybackSpeed, elapsed);
                voiceLease = GameAudio.Lease(actionVoice);
            }
            if (OwnsVoice()) actionVoice.transform.position = transform.position + Vector3.up;
        }
        bool sliding = Local ? movement != null && movement.IsSliding : pose != null && pose.IsSliding;
        bool grounded = capsule != null && capsule.enabled && capsule.isGrounded;
        if (!grounded) grounded = Physics.Raycast(transform.position + Vector3.up * .1f, Vector3.down, .25f, ~0, QueryTriggerInteraction.Ignore);
        delta.y = 0;
        if (!grounded || sliding || delta.magnitude > 1f || delta.sqrMagnitude < .000001f) { distance = 0; return; }
        bool crouch = Local ? movement != null && movement.IsCrouching : pose != null && pose.IsCrouching;
        distance += delta.magnitude;
        if (distance < (crouch ? .9f : 1.55f)) return;
        distance = 0;
        GameAudio.Effect("Steps/step" + (step++ % 4), transform.position, crouch ? .16f : .32f, crouch ? 9f : 20f, Local);
    }
    // A stolen pool voice must never be moved or stopped by its previous user.
    private bool OwnsVoice() => actionVoice != null && actionVoice.isPlaying && GameAudio.Lease(actionVoice) == voiceLease;
    private void StopAction() { if (OwnsVoice()) actionVoice.Stop(); actionVoice = null; }
    private void OnDisable() => StopAction();
}
