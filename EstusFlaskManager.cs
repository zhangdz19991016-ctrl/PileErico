using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Scenes; // [Gemini] 添加
using UnityEngine; // [Gemini] 添加

namespace PileErico
{
    public class EstusFlaskManager
    {
        // 原素瓶功能
        private const string EstusFlaskItemKey = "EstusFlask";
        private const string EmptyEstusFlaskItemKey = "EstusFlask_Empty";
        private static readonly int EstusFlaskItemHash = "EstusFlask".GetHashCode();
        private static readonly int EmptyEstusFlaskItemHash = "EstusFlask_Empty".GetHashCode();
        
        // [Gemini] 引用主模组，用于启动协程
        private readonly ModBehaviour modBehaviour;

        public EstusFlaskManager(ModBehaviour modBehaviour)
        {
            this.modBehaviour = modBehaviour;
        }
        
        /// <summary>
        /// 由 ModBehaviour 调用以启动此功能
        /// </summary>
        public void Initialize()
        {
            ModBehaviour.LogToFile("[EstusFlaskManager] 正在初始化...");

            // 订阅原素瓶事件
            Item.onUseStatic -= this.OnItemUsed;
            Item.onUseStatic += this.OnItemUsed;
                
            if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.CharacterItem != null && LevelManager.Instance.MainCharacter.CharacterItem.Inventory != null)
            {
                ModBehaviour.LogToFile("[PileErico] 成功获取玩家物品栏，正在挂载原素瓶事件...");
                LevelManager.Instance.MainCharacter.CharacterItem.Inventory.onContentChanged -= this.OnInventoryContentChanged;
                LevelManager.Instance.MainCharacter.CharacterItem.Inventory.onContentChanged += this.OnInventoryContentChanged;
            }
            else
            {
                ModBehaviour.LogWarningToFile("[PileErico] 警告：未能找到玩家物品栏来挂载原素瓶事件。");
            }
        }

        /// <summary>
        /// 由 ModBehaviour 调用以停用此功能
        /// </summary>
        public void Deactivate()
        {
            ModBehaviour.LogToFile("[EstusFlaskManager] 正在停用...");

            Item.onUseStatic -= this.OnItemUsed;
            
            if (LevelManager.Instance != null && LevelManager.Instance.MainCharacter != null && LevelManager.Instance.MainCharacter.CharacterItem != null && LevelManager.Instance.MainCharacter.CharacterItem.Inventory != null)
            {
                LevelManager.Instance.MainCharacter.CharacterItem.Inventory.onContentChanged -= this.OnInventoryContentChanged;
            }
            
            // [Gemini] 移除所有已追踪瓶子的事件
            foreach (var flask in this.trackedFlasks)
            {
                if (flask != null)
                {
                    flask.onDurabilityChanged -= this.OnFlaskDurabilityChanged;
                }
            }
            this.trackedFlasks.Clear();
        }

        // --- 原素瓶切换 (保持不变) ---
        #region Original Estus Flask Functions
        
        private readonly Dictionary<int, int> flaskEmptyMap = new Dictionary<int, int>
        {
            { 201192215, 2025000001 },
            { 201192216, 2025000002 },
            { 201192217, 2025000003 }
        };
        private HashSet<Item> trackedFlasks = new HashSet<Item>();

        private void OnItemUsed(Item item, object user)
        {
            if (item != null && this.flaskEmptyMap.ContainsKey(item.TypeID))
            {
                if (!this.trackedFlasks.Contains(item))
                {
                    ModBehaviour.LogToFile(string.Format("[PileErico] 发现新的元素瓶 (ID: {0})，开始追踪其耐久度。", item.TypeID));
                    item.onDurabilityChanged += this.OnFlaskDurabilityChanged;
                    this.trackedFlasks.Add(item);
                }
                CheckAndReplaceEstusFlask(item);
            }
        }
        
        private void OnInventoryContentChanged(Inventory inventory, int index)
        {
            if (inventory == null) return;
            
            foreach (Item item in inventory.Content.Where(i => i != null && this.flaskEmptyMap.ContainsKey(i.TypeID)))
            {
                if (!this.trackedFlasks.Contains(item))
                {
                    ModBehaviour.LogToFile(string.Format("[PileErico] 发现新的元素瓶 (ID: {0})，开始追踪其耐久度。", item.TypeID));
                    item.onDurabilityChanged += this.OnFlaskDurabilityChanged;
                    this.trackedFlasks.Add(item);
                }
                CheckAndReplaceEstusFlask(item);
            }
            this.trackedFlasks.RemoveWhere((Item item) => item == null || item.IsBeingDestroyed);
        }

        private void CheckAndReplaceEstusFlask(Item item)
        {
            if (item.Durability <= 0f)
            {
                this.OnFlaskDurabilityChanged(item);
            }
        }
        
        private void OnFlaskDurabilityChanged(Item changedFlask)
        {
            if (changedFlask.Durability <= 0f)
            {
                ModBehaviour.LogToFile(string.Format("[PileErico] 元素瓶 (ID: {0}) 已耗尽。", changedFlask.TypeID));
                changedFlask.onDurabilityChanged -= this.OnFlaskDurabilityChanged;
                this.trackedFlasks.Remove(changedFlask);
                
                int emptyFlaskId;
                if (this.flaskEmptyMap.TryGetValue(changedFlask.TypeID, out emptyFlaskId))
                {
                    Slot? pluggedIntoSlot = changedFlask.PluggedIntoSlot;
                    Inventory? inInventory = changedFlask.InInventory;
                    int inventoryIndex = (inInventory != null) ? inInventory.GetIndex(changedFlask) : -1;
                    
                    // [Gemini 修正] CS0103: "originalIndex" 不存在，应为 "inventoryIndex"
                    this.modBehaviour.StartCoroutine(this.PerformFlaskSwap(changedFlask, emptyFlaskId, pluggedIntoSlot, inInventory, inventoryIndex));
                }
            }
        }
        
        private IEnumerator PerformFlaskSwap(Item changedFlask, int emptyFlaskId, Slot? originalSlot, Inventory? originalInventory, int originalIndex)
        {
            yield return null;
            try
            {
                Item? emptyFlask = ItemAssetsCollection.InstantiateSync(emptyFlaskId);
                if (emptyFlask == null)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] 替换失败：无法创建空瓶子实例。");
                    yield break;
                }

                if (originalSlot != null)
                {
                    Item item;
                    originalSlot.Plug(emptyFlask, out item);
                    ModBehaviour.LogToFile("[PileErico] 成功将空瓶替换到插槽: " + originalSlot.Key);
                }
                else if (originalInventory != null && originalIndex != -1)
                {
                    originalInventory.AddAt(emptyFlask, originalIndex);
                    ModBehaviour.LogToFile("[PileErico] 成功将空瓶替换到背包索引: " + originalIndex);
                }
                else
                {
                    ModBehaviour.LogWarningToFile("[PileErico] 物品来自未知位置。使用 'SendToPlayer' 智能后备方案...");
                    ItemUtilities.SendToPlayer(emptyFlask, false, true);
                }
                
                if (changedFlask != null && !changedFlask.IsBeingDestroyed)
                {
                    changedFlask.DestroyTree();
                }

                if (CharacterMainControl.Main != null)
                {
                    DialogueBubblesManager.Show("……原素瓶已尽！", CharacterMainControl.Main.transform, -1f, false, false, -1f, 5f).Forget();
                    ModBehaviour.LogToFile("[PileErico] 已触发“原素瓶已尽”对话气泡。");
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile("[PileErico] PerformFlaskSwap 协程出错: " + ex.ToString());
                if (changedFlask != null && !changedFlask.IsBeingDestroyed)
                {
                    changedFlask.DestroyTree();
                }
            }
        }
        
        #endregion
    }
}