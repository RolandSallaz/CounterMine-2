using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Persistent wallet, permanent purchases and the loadout for the next life.
/// Cloud storage on Yandex WebGL, PlayerPrefs everywhere else (see <see cref="YandexCloudSave"/>).</summary>
[Serializable]
public sealed class YandexPlayerData
{
    public const string SaveKey = "countermine_save_v1";
    public const string DefaultWeapon = "ak74";

    public int money;
    public List<string> ownedWeapons = new List<string> { DefaultWeapon, "ucp", "radar", "milkor" };
    public string equippedWeapon = DefaultWeapon;
    public string equippedPistol = "ucp";
    public List<string> equippedSkills = new List<string> { "radar", "milkor" };
    public int starterLoadoutVersion;

    public static YandexPlayerData Current { get; private set; } = CreateDefault();
    public static bool IsLoaded { get; private set; }

    public static YandexPlayerData CreateDefault() => new YandexPlayerData { starterLoadoutVersion = 1 };

    public static void Load(Action onDone = null)
    {
        IsLoaded = false;
        YandexCloudSave.LoadObject<YandexPlayerData>(SaveKey, data =>
        {
            bool migrated = data == null || data.starterLoadoutVersion < 1;
            Current = Sanitize(data);
            IsLoaded = true;
            if (migrated) Save();
            onDone?.Invoke();
        });
    }

    public static void Save() => YandexCloudSave.SaveObject(SaveKey, Current);

    public static YandexPlayerData Sanitize(YandexPlayerData data)
    {
        if (data == null) return CreateDefault();
        data.ownedWeapons ??= new List<string>();
        data.equippedSkills ??= new List<string>();
        if (data.starterLoadoutVersion < 1)
        {
            foreach (string id in new[] { DefaultWeapon, "ucp", "radar", "milkor" })
                if (!data.ownedWeapons.Contains(id)) data.ownedWeapons.Add(id);
            data.equippedWeapon = DefaultWeapon;
            data.equippedPistol = "ucp";
            data.equippedSkills = new List<string> { "radar", "milkor" };
            data.starterLoadoutVersion = 1;
        }
        if (!data.ownedWeapons.Contains(DefaultWeapon)) data.ownedWeapons.Add(DefaultWeapon);
        data.ownedWeapons = new List<string>(new HashSet<string>(data.ownedWeapons));
        if (!data.Owns(data.equippedWeapon) || ShopCatalog.Find(data.equippedWeapon)?.category != ShopCategory.Primary)
            data.equippedWeapon = DefaultWeapon;
        if (!data.Owns(data.equippedPistol) || ShopCatalog.Find(data.equippedPistol)?.category != ShopCategory.Pistol)
            data.equippedPistol = "";
        data.equippedSkills ??= new List<string>();
        data.equippedSkills = new List<string>(new HashSet<string>(data.equippedSkills));
        data.equippedSkills.RemoveAll(id => !data.Owns(id) || ShopCatalog.Find(id)?.category != ShopCategory.Skill);
        data.money = Mathf.Max(0, data.money);
        return data;
    }

    public bool Owns(string weaponId) => !string.IsNullOrEmpty(weaponId) && ownedWeapons.Contains(weaponId);

    public void AddMoney(int amount)
    {
        if (amount == 0) return;
        money = Mathf.Max(0, money + amount);
        Save();
    }

    public bool TryBuy(string weaponId, int price)
    {
        if (Owns(weaponId) || price < 0 || money < price) return false;
        money -= price;
        ownedWeapons.Add(weaponId);
        Save();
        return true;
    }

    public bool TryEquip(string weaponId)
    {
        if (!Owns(weaponId) || ShopCatalog.Find(weaponId)?.category != ShopCategory.Primary) return false;
        equippedWeapon = weaponId;
        Save();
        return true;
    }

    public bool IsEquipped(string id)
    {
        var item = ShopCatalog.Find(id);
        return item != null && (item.category == ShopCategory.Primary ? equippedWeapon == id :
            item.category == ShopCategory.Pistol ? equippedPistol == id : equippedSkills.Contains(id));
    }
    public bool TryPurchaseAndEquip(string id, out string message, bool persist = true)
    {
        message = "";
        if (ReferenceEquals(this, Current) && !IsLoaded) { message = GameLocalization.T("Профиль ещё загружается."); return false; }
        var item = ShopCatalog.Find(id);
        if (item == null || item.price < 0) { message = GameLocalization.T("Предмет недоступен."); return false; }
        bool owned = Owns(id);
        if (!owned && money < item.price) { message = GameLocalization.T("Недостаточно денег."); return false; }
        if (item.category == ShopCategory.Skill && !equippedSkills.Contains(id) && equippedSkills.Count >= RadarSkill.SlotCount)
        { message = GameLocalization.T("Все слоты навыков заняты."); return false; }
        if (!owned) { money -= item.price; ownedWeapons.Add(id); }
        if (item.category == ShopCategory.Primary) equippedWeapon = id;
        else if (item.category == ShopCategory.Pistol) equippedPistol = id;
        else if (!equippedSkills.Contains(id)) equippedSkills.Add(id);
        if (persist && ReferenceEquals(this, Current)) Save();
        message = owned ? GameLocalization.T("Выбрано для следующего возрождения.") : GameLocalization.T("Куплено навсегда и выбрано.");
        return true;
    }
    public bool UnequipSkill(string id)
    {
        if (!IsLoaded || !equippedSkills.Remove(id)) return false;
        Save(); return true;
    }
}
