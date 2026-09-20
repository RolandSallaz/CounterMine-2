using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Magazine ammo for the mounted weapon. Reserve is infinite: reloading refills the mag.
/// Owner-authoritative: dry shots are blocked on the shooter, so the master never sees them.</summary>
[DisallowMultipleComponent]
public sealed class WeaponAmmo : MonoBehaviour
{
    [SerializeField, Min(1)] private int magazineSize = 30;
    [SerializeField, Min(.1f)] private float fallbackReloadSeconds = 2.6f;
    [SerializeField] private bool autoReloadOnEmpty = true;
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    [SerializeField] private PlayerHealth health;

    public int MagazineSize => magazineSize;
    public int MagAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public bool CanShoot => !IsReloading && MagAmmo > 0;

    private PhotonView photonView;
    private bool animDriven;
    private float reloadEndsAt;
    private float nextDrySound;
    private string equippedWeapon;
    private readonly System.Collections.Generic.Dictionary<string,int> magazines = new System.Collections.Generic.Dictionary<string,int>();
    public void SelectWeapon(string id, int capacity)
    {
        if (equippedWeapon == id) return;
        if (!string.IsNullOrEmpty(equippedWeapon)) magazines[equippedWeapon] = MagAmmo;
        equippedWeapon = id;
        magazineSize = Mathf.Max(1,capacity);
        MagAmmo = magazines.TryGetValue(id,out int saved) ? Mathf.Clamp(saved,0,magazineSize) : magazineSize;
        // Switching interrupts a reload; it must not refill either magazine.
        IsReloading = false; animDriven = false;
    }
    private bool IsOwner => !PhotonNetwork.InRoom || (photonView != null && photonView.IsMine);

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        weaponAnimation ??= GetComponentInChildren<WeaponIdleSynchronizer>(true);
        health ??= GetComponent<PlayerHealth>();
        MagAmmo = magazineSize;
    }

    public void Consume()
    {
        if (MagAmmo <= 0) return;
        MagAmmo--;
        if (MagAmmo == 0 && autoReloadOnEmpty) TryStartReload();
    }

    public void HandleDryFire()
    {
        if (!IsReloading && Time.time >= nextDrySound && weaponAnimation != null)
        {
            var profile = weaponAnimation.AudioProfile;
            if (profile != null) GameAudio.Play(profile.dryFire, transform.position + Vector3.up, .35f, 10f, !BotController.IsBot(this) && IsOwner);
            nextDrySound = Time.time + .25f;
        }
        if (!IsReloading && autoReloadOnEmpty && MagAmmo <= 0) TryStartReload();
    }

    public bool TryStartReload()
    {
        if (IsReloading || MagAmmo >= magazineSize || weaponAnimation == null) return false;
        if (health != null && health.IsDead) return false;
        if (weaponAnimation.PlayWeaponAction("reload"))
        {
            IsReloading = true; animDriven = true; return true;
        }
        // No reload clip in the weapon catalog: timed fallback. CanFire stays true, IsReloading gates fire.
        IsReloading = true; animDriven = false; reloadEndsAt = Time.time + fallbackReloadSeconds; return true;
    }

    private void Update()
    {
        if (!IsOwner || weaponAnimation == null) return;
        // Pick up reloads started through the shared weapon action API.
        if (!IsReloading && weaponAnimation.ActionId == "reload" && MagAmmo < magazineSize)
        {
            IsReloading = true; animDriven = true;
        }
        if (IsReloading)
        {
            if (animDriven)
            {
                if (weaponAnimation.ActionId != "reload") FinishReload();
            }
            else if (Time.time >= reloadEndsAt) FinishReload();
            return;
        }
        if (health != null && health.IsDead) return;
        if (!BotController.IsBot(this) && weaponAnimation.CanFire && MagAmmo < magazineSize &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            Keyboard.current?.rKey.wasPressedThisFrame == true)
            TryStartReload();
    }

    private void FinishReload()
    {
        MagAmmo = magazineSize;
        IsReloading = false;
    }
}
