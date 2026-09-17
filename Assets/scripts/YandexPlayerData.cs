using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Persistent player profile: money + owned/equipped weapons. Ready for a future shop.
/// Cloud storage on Yandex WebGL, PlayerPrefs everywhere else (see <see cref="YandexCloudSave"/>).</summary>
[Serializable]
public sealed class YandexPlayerData
{
    public const string SaveKey = "countermine_save_v1";
    public const string DefaultWeapon = "ak74";

    public int money;
    public List<string> ownedWeapons = new List<string> { DefaultWeapon };
    public string equippedWeapon = DefaultWeapon;

    public static YandexPlayerData Current { get; private set; } = CreateDefault();
    public static bool IsLoaded { get; private set; }

    public static YandexPlayerData CreateDefault() => new YandexPlayerData();

    public static void Load(Action onDone = null)
    {
        IsLoaded = false;
        YandexCloudSave.LoadObject<YandexPlayerData>(SaveKey, data =>
        {
            Current = Sanitize(data);
            IsLoaded = true;
            onDone?.Invoke();
        });
    }

    public static void Save() => YandexCloudSave.SaveObject(SaveKey, Current);

    private static YandexPlayerData Sanitize(YandexPlayerData data)
    {
        if (data == null) return CreateDefault();
        data.ownedWeapons ??= new List<string>();
        if (data.ownedWeapons.Count == 0) data.ownedWeapons.Add(DefaultWeapon);
        if (string.IsNullOrEmpty(data.equippedWeapon) || !data.ownedWeapons.Contains(data.equippedWeapon))
            data.equippedWeapon = data.ownedWeapons[0];
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
        if (!Owns(weaponId)) return false;
        equippedWeapon = weaponId;
        Save();
        return true;
    }
}
