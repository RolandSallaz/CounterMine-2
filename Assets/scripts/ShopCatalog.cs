using System;
using UnityEngine;

public enum ShopCategory { Primary, Pistol, Skill }

[CreateAssetMenu(menuName = "CounterMine/Shop Catalog", fileName = "ShopCatalog")]
public sealed class ShopCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Item
    {
        public string id, title;
        [TextArea] public string description;
        public ShopCategory category;
        [Min(0)] public int price;
        public string detail, icon;
        public int killsRequired;
        public int key;
    }
    public Item[] items = Defaults();
    private static ShopCatalog cached;
    private static readonly Item[] fallback = Defaults();
    public static Item[] Items
    {
        get { if (cached == null) cached = Resources.Load<ShopCatalog>("ShopCatalog"); return cached != null ? cached.items : fallback; }
    }
    public static Item Find(string id) => Array.Find(Items, item => item != null && item.id == id);
    public static Item[] Defaults() => new[] {
        new Item { id="ak74", title="AK-74", category=ShopCategory.Primary, price=0, key=1,
            description="Универсальный автомат для боя на средней дистанции.", detail="30 ПАТРОНОВ  /  АВТОМАТИЧЕСКИЙ", icon="UI/Shop/ak74" },
        new Item { id="hk416", title="HK416", category=ShopCategory.Primary, price=600, key=1,
            description="Скорострельный автомат с удобным управлением и механическим прицелом.", detail="30 ПАТРОНОВ  /  800 ВЫСТР./МИН", icon="UI/Shop/hk416" },
        new Item { id="l115a3", title="L115A3", category=ShopCategory.Primary, price=1200, key=1,
            description="Мощная снайперская винтовка с оптическим прицелом и продольно-скользящим затвором.", detail="5 ПАТРОНОВ  /  ОПТИЧЕСКИЙ ПРИЦЕЛ", icon="UI/Shop/l115a3" },
        new Item { id="ucp", title="HK UCP", category=ShopCategory.Pistol, price=150, key=2,
            description="Лёгкий запасной пистолет с быстрым прицеливанием.", detail="20 ПАТРОНОВ  /  ОДИНОЧНЫЙ ОГОНЬ", icon="UI/Shop/ucp" },
        new Item { id="radar", title="РАДАР", category=ShopCategory.Skill, price=250, key=3, killsRequired=5,
            description="Подсвечивает противников за укрытиями на 15 секунд.", detail="5 УБИЙСТВ  /  15 СЕКУНД", icon="UI/Icons/Radar" },
        new Item { id="milkor", title="MILKOR MGL Mk 1S", category=ShopCategory.Skill, price=500, key=4, killsRequired=10,
            description="Шесть гранат. Взрыв при попадании. Без перезарядки.", detail="10 УБИЙСТВ  /  6 ГРАНАТ", icon="UI/Shop/milkor" }
    };
}
