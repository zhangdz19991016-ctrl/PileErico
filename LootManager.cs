using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Newtonsoft.Json;
using UnityEngine;
using Duckov; 
using Duckov.Scenes; // [v21] 包含 LevelManager
using Duckov.Utilities; // [v21] 包含 InteractableLootbox (基于 v19 的修复)
using UnityEngine.Events;

namespace PileErico 
{
    public class LootManager
    {
        // 战利品功能
        private LootConfig? lootConfig; 
        private bool configLoaded;
        
        // [v21] 恢复 v13 的订阅字典 (用于扫描)
        private Dictionary<CharacterMainControl, UnityAction<DamageInfo>> subscribedHandlers = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();
        
        // 引用主模组
        private readonly ModBehaviour modBehaviour;
        private readonly string configDir;
        
        // [v21] 两个功能都需要
        private readonly BossHealthHUDManager? bossHudManager;
        private Coroutine? checkEnemiesCoroutine;


        // [v21] 构造函数需要 hudManager
        public LootManager(ModBehaviour modBehaviour, string configDir, BossHealthHUDManager? hudManager)
        {
            this.modBehaviour = modBehaviour;
            this.configDir = configDir;
            this.bossHudManager = hudManager;
        }

        // [v21] 恢复 v13 的初始化
        public void Initialize()
        {
            ModBehaviour.LogToFile("[LootManager] 正在初始化 (v21 等待战利品箱)...");
            this.LoadLootConfig(this.configDir);
            
            if (this.checkEnemiesCoroutine != null)
            {
                this.modBehaviour.StopCoroutine(this.checkEnemiesCoroutine);
            }
            this.checkEnemiesCoroutine = this.modBehaviour.StartCoroutine(this.CheckForNewEnemiesCoroutine());
        }

        // [v21] 恢复 v13 的停用
        public void Deactivate()
        {
            if (this.checkEnemiesCoroutine != null)
            {
                this.modBehaviour.StopCoroutine(this.checkEnemiesCoroutine);
            }
            
            foreach (var pair in subscribedHandlers)
            {
                if (pair.Key != null && pair.Key.Health != null)
                {
                    pair.Key.Health.OnDeadEvent.RemoveListener(pair.Value);
                }
            }
            subscribedHandlers.Clear();
            ModBehaviour.LogToFile("[LootManager] 已停止并清理订阅。");
        }

        #region 敌人扫描与订阅 (v21 - 两个功能都需要)
        
        private IEnumerator CheckForNewEnemiesCoroutine()
        {
            for (;;)
            {
                try
                {
                    if (this.configLoaded && CharacterMainControl.Main != null)
                    {
                        var allCharacters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                        foreach (var character in allCharacters)
                        {
                            if (character == null || character == CharacterMainControl.Main || subscribedHandlers.ContainsKey(character))
                            {
                                continue;
                            }

                            if (character.Health != null)
                            {
                                // 1. 订阅战利品掉落 (在 OnCharacterDeath 中处理)
                                UnityAction<DamageInfo> handler = (DamageInfo dmgInfo) => OnCharacterDeath(character, dmgInfo);
                                character.Health.OnDeadEvent.AddListener(handler);
                                subscribedHandlers.Add(character, handler);
                                
                                // 2. 将敌人信息共享给 Boss HUD 管理器
                                bossHudManager?.RegisterCharacter(character); //
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile("[LootManager] CheckForNewEnemiesCoroutine 异常: " + ex);
                }
                
                yield return new WaitForSeconds(3.0f); // 每3秒检查一次新敌人
            }
        }
        
        #endregion

        #region 战利品核心逻辑 (v21)

        // [v21] 这是由 Health.OnDeadEvent 触发的
        private void OnCharacterDeath(CharacterMainControl deadCharacter, DamageInfo damageInfo)
        {
            // [v21] (修复 CS0019) 'DamageInfo' 是 struct, 不能与 null 比较
            if (!ModBehaviour.isActivated || !this.configLoaded || this.lootConfig == null || deadCharacter == null)
            {
                return;
            }

            try
            {
                // 1. [v21] 查找规则
                EnemyLootRule? matchingRule = FindMatchingRule(deadCharacter); //
                if (matchingRule?.LootItems == null)
                {
                    ModBehaviour.LogToFile($"[LootManager] '{SafeGetName(deadCharacter)}' 死亡，但未找到匹配的战利品规则。");
                    return;
                }
                
                ModBehaviour.LogToFile($"[LootManager] 正在为 '{SafeGetName(deadCharacter)}' 准备战利品...");

                // 2. [v21] 启动新协程，等待 LootBox 生成
                this.modBehaviour.StartCoroutine(this.AddLootToSpawnedLootBoxCoroutine(deadCharacter, matchingRule));
            }
            catch (Exception ex) 
            { 
                ModBehaviour.LogErrorToFile("[PileErico] OnCharacterDeath (v21) 出错: " + ex.Message); 
            }
            finally
            {
                // [v21] 清理订阅
                if (deadCharacter != null && subscribedHandlers.TryGetValue(deadCharacter, out var handler))
                {
                    if (deadCharacter.Health != null)
                    {
                        deadCharacter.Health.OnDeadEvent.RemoveListener(handler);
                    }
                    subscribedHandlers.Remove(deadCharacter);
                }
            }
        }
        
        // [v21 新增] 核心修复：等待战利品箱生成
        private IEnumerator AddLootToSpawnedLootBoxCoroutine(CharacterMainControl deadCharacter, EnemyLootRule matchingRule)
        {
            // [v21] (修复 CS1061) 'DamageInfo' 没有 'DeadCharacter', 我们已经有 'deadCharacter' 了
            
            InteractableLootbox? foundLootbox = null;
            float timeout = 2.0f; // 等待最多2秒

            ModBehaviour.LogToFile($"[LootManager] 等待 '{SafeGetName(deadCharacter)}' 的战利品箱生成...");

            while (timeout > 0f)
            {
                // [v21] 在死亡位置附近 2 米范围内搜索战利品箱
                foundLootbox = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>()
                    .FirstOrDefault(lootbox => 
                        lootbox.Inventory != null && //
                        Vector3.Distance(lootbox.transform.position, deadCharacter.transform.position) < 2.0f
                        // 也许还需要检查它是否 "未被初始化" 或 "刚生成"
                    );

                if (foundLootbox != null)
                {
                    ModBehaviour.LogToFile($"[LootManager] 已找到战利品箱: {foundLootbox.name}");
                    break;
                }
                
                yield return null; // 等待下一帧
                timeout -= Time.deltaTime;
            }

            // [v21] 找到战利品箱后，执行 v15 的添加逻辑
            if (foundLootbox != null && foundLootbox.Inventory != null)
            {
                try
                {
                    if (lootConfig != null && lootConfig.ExtraSlots > 0) 
                        foundLootbox.Inventory.SetCapacity(foundLootbox.Inventory.Capacity + lootConfig.ExtraSlots); //
                    
                    foreach (LootItem lootItem in matchingRule.LootItems) //
                    {
                        if (UnityEngine.Random.Range(0f, 100f) < lootItem.Chance) //
                        {
                            Item? newItem = ItemAssetsCollection.InstantiateSync(lootItem.ItemID); //
                            if (newItem != null)
                            {
                                newItem.StackCount = lootItem.Count; //
                                foundLootbox.Inventory.AddAndMerge(newItem, 0); //
                                ModBehaviour.LogToFile($"[PileErico] 成功添加物品: [ID: {lootItem.ItemID}, 数量: {lootItem.Count}] 到 {foundLootbox.name}。");
                            }
                            else 
                            { 
                                ModBehaviour.LogWarningToFile($"[!!!] 物品生成失败！TypeID: {lootItem.ItemID} 未在游戏中注册或无效。"); //
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile("[PileErico] AddLootToSpawnedLootBoxCoroutine 出错: " + ex.Message);
                }
            }
            else
            {
                ModBehaviour.LogErrorToFile($"[LootManager] 未能为 '{SafeGetName(deadCharacter)}' 找到生成的战利品箱。战利品添加失败。");
            }
        }

        #endregion
        
        #region 配置加载 (v10 修复)
        
        private void LoadLootConfig(string modDir)
        {
            try
            {
                string configPath = Path.Combine(modDir, "LootConfig.json");
                ModBehaviour.LogToFile("[LootManager] 正在加载战利品配置: " + configPath);

                if (!File.Exists(configPath))
                {
                    ModBehaviour.LogToFile("[LootManager] 未找到 LootConfig.json，正在创建默认配置...");
                    this.lootConfig = this.GenerateDefaultConfig();
                    string json = JsonConvert.SerializeObject(this.lootConfig, Formatting.Indented);
                    File.WriteAllText(configPath, json);
                }
                else
                {
                    string json = File.ReadAllText(configPath); // [v10 修复]
                    this.lootConfig = JsonConvert.DeserializeObject<LootConfig>(json); 
                    
                    if (this.lootConfig == null)
                    {
                        ModBehaviour.LogErrorToFile("[LootManager] 加载 LootConfig.json 失败! 文件内容为空或格式错误。");
                        this.configLoaded = false;
                        return;
                    }
                }

                ModBehaviour.LogToFile($"[LootManager] 战利品配置加载成功。包含 {this.lootConfig.EnemyLootRules.Count} 条规则。"); //
                this.configLoaded = true;
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile("[LootManager] LoadLootConfig 异常: " + ex);
                this.configLoaded = false;
            }
        }
        
        private LootConfig GenerateDefaultConfig()
        {
            return new LootConfig
            {
                ExtraSlots = 0, //
                EnemyLootRules = new List<EnemyLootRule> //
                {
                    new EnemyLootRule
                    {
                        EnemyNameKeywords = new List<string> { "光之男", "Man of Light", "LighMan_Prefab" }, //
                        LootItems = new List<LootItem> //
                        {
                            new LootItem { ItemID = 254, Count = 1, Chance = 100 } //
                        }
                    },
                    new EnemyLootRule
                    {
                        EnemyNameKeywords = new List<string>(), //
                        LootItems = new List<LootItem> //
                        {
                            new LootItem { ItemID = 254, Count = 1, Chance = 5 } //
                        }
                    }
                }
            };
        }
        
        #endregion

        #region 辅助方法 (v21 修复)
        
        // [v21] 恢复 v1 的辅助方法
        private string GetEnemyNameKey(CharacterMainControl character, string fallbackName)
        {
            // [v21 修复警告] 检查 null
            if (character == null) return fallbackName;
            
            if (character.characterPreset != null && !string.IsNullOrEmpty(character.characterPreset.DisplayName))
            {
                return character.characterPreset.DisplayName;
            }
            if (character.CharacterItem != null && !string.IsNullOrEmpty(character.CharacterItem.DisplayName))
            {
                return character.CharacterItem.DisplayName;
            }
            return fallbackName;
        }

        // [v21] 恢复 v1 的 FindMatchingRule
        private EnemyLootRule? FindMatchingRule(CharacterMainControl character)
        {
            // [v21 修复警告] 检查 null
            if (character == null) return null;
            
            if (this.lootConfig == null || this.lootConfig.EnemyLootRules == null) //
            {
                return null;
            }
            string enemyNameKey = this.GetEnemyNameKey(character, character.name);
            string enemyDisplayName = character.CharacterItem?.DisplayName ?? enemyNameKey;

            foreach (EnemyLootRule enemyLootRule in this.lootConfig.EnemyLootRules) //
            {
                // [v21 修复警告] 检查 null
                if (enemyLootRule?.EnemyNameKeywords != null && enemyLootRule.EnemyNameKeywords.Count > 0) //
                {
                    if (enemyLootRule.EnemyNameKeywords.Any(keyword => 
                        !string.IsNullOrEmpty(keyword) && 
                        (enemyNameKey.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || 
                         enemyDisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        return enemyLootRule;
                    }
                }
            }
            
            return this.lootConfig.EnemyLootRules.FirstOrDefault((EnemyLootRule r) => r.EnemyNameKeywords == null || r.EnemyNameKeywords.Count == 0); //
        }
        
        // [v21] SafeGetName (来自 v10)
        private string SafeGetName(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            try
            {
                if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.DisplayName))
                {
                    return ch.characterPreset.DisplayName;
                }
            }
            catch { }
            return ch.name;
        }

        #endregion
        
    } // <-- LootManager 类结束

    // [集成] 以下是来自外部 .cs 文件的定义 (无改动)

    /// <summary>
    /// 来自 LootConfig.cs
    /// </summary>
    [Serializable]
    public class LootConfig //
    {
        public int ExtraSlots = 12; //
        public List<EnemyLootRule> EnemyLootRules = new List<EnemyLootRule>(); //
    }

    /// <summary>
    /// 来自 EnemyLootRule.cs
    /// </summary>
    [Serializable]
    public class EnemyLootRule //
    {
        public List<string> EnemyNameKeywords = new List<string>(); //
        public List<LootItem> LootItems = new List<LootItem>(); //
    }

    /// <summary>
    /// 来自 LootItem.cs
    /// </summary>
    [Serializable]
    public class LootItem //
    {
        public int ItemID; //
        public int Count = 1; //
        public float Chance = 100f; //
    }

} // <-- 命名空间 PileErico 结束