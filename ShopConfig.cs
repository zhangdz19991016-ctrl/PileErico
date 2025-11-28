using System;
using System.Collections.Generic;
using UnityEngine; // 需要 Vector3

namespace PileErico
{
    [Serializable]
    public class ShopConfig
    {
        // 商店NPC使用的预设体名称
        public string MerchantPresetName { get; set; } = "EnemyPreset_Merchant_Myst";
        
        // 商店NPC在基地的位置
        public float PositionX { get; set; } = 0f;
        public float PositionY { get; set; } = 1.5f;
        public float PositionZ { get; set; } = -52.5f;

        // 商店NPC的朝向
        public float FacingX { get; set; } = 0f;
        public float FacingY { get; set; } = 1.5f;
        public float FacingZ { get; set; } = -50f;

        // 售卖的物品列表
        public List<ShopItemEntry> ItemsToSell { get; set; } = new List<ShopItemEntry>();

        // 辅助方法，用于获取 Vector3 坐标
        public Vector3 GetPosition() => new Vector3(PositionX, PositionY, PositionZ);
        public Vector3 GetFacing() => new Vector3(FacingX, FacingY, FacingZ);
    }
}