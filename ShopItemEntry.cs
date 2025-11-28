using System;

namespace PileErico
{
    [Serializable]
    public class ShopItemEntry
    {
        // 物品ID
        public int ItemID { get; set; }
        
        // 最大库存
        public int MaxStock { get; set; } = 1;
        
        // 价格系数 (1.0 = 100%原价)
        public float PriceFactor { get; set; } = 1.0f;
        
        // 刷新时出现的概率 (1.0 = 100%)
        public float Possibility { get; set; } = 1.0f;
    }
}