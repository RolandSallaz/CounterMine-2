using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit local developer request only; never included in a player build.</summary>
[InitializeOnLoad]
public static class LocalWalletRequest
{
    private const string Request = "Temp/set-local-wallet.request";
    static LocalWalletRequest() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || !File.Exists(Request)) return;
        string value = File.ReadAllText(Request); File.Delete(Request);
        try
        {
            if (!int.TryParse(value.Trim(), out int amount) || amount < 0) throw new Exception("Invalid wallet amount");
            string previous = PlayerPrefs.GetString(YandexPlayerData.SaveKey, "");
            var data = EditorApplication.isPlaying && YandexPlayerData.IsLoaded ? YandexPlayerData.Current :
                string.IsNullOrEmpty(previous) ? YandexPlayerData.CreateDefault() : JsonUtility.FromJson<YandexPlayerData>(previous);
            if (data == null) throw new Exception("Local save could not be read");
            File.WriteAllText("Temp/local-wallet-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", JsonUtility.ToJson(data));
            data.money = amount;
            PlayerPrefs.SetString(YandexPlayerData.SaveKey, JsonUtility.ToJson(data)); PlayerPrefs.Save();
            var check = JsonUtility.FromJson<YandexPlayerData>(PlayerPrefs.GetString(YandexPlayerData.SaveKey));
            if (check.money != amount) throw new Exception("Wallet verification failed");
            File.WriteAllText("Temp/local-wallet-result.txt", "PASS: " + Application.productName + " local wallet = " + check.money);
        }
        catch (Exception e) { File.WriteAllText("Temp/local-wallet-result.txt", "FAIL: " + e.Message); }
    }
}
