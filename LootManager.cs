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
using Duckov.Scenes;
using Duckov.Utilities;

namespace PileErico 
{
    public class LootManager
    {
        private readonly ModBehaviour modBehaviour;
        private readonly string configDir;
        private LootConfig? lootConfig; 
        private bool configLoaded;

        public LootManager(ModBehaviour modBehaviour, string configDir)
        {
            this.modBehaviour = modBehaviour;
            this.configDir = configDir;
        }

        public void Initialize()
        {
            ModBehaviour.LogToFile("[LootManager] 正在初始化...");
            
            // 1. 加载配置
            this.LoadLootConfig(this.configDir);
            
            // 2. 订阅全局扫描事件
            ScanManager.OnCharacterSpawned += OnEnemyFound;
            
            // 3. 处理已经在场上的敌人
            foreach (var character in ScanManager.ActiveCharacters)
            {
                OnEnemyFound(character);
            }
        }

        public void Deactivate()
        {
            ScanManager.OnCharacterSpawned -= OnEnemyFound;
            ModBehaviour.LogToFile("[LootManager] 已停止监听。");
        }

        // === 核心逻辑：发现敌人 -> 匹配规则 -> 挂载组件 ===
        private void OnEnemyFound(CharacterMainControl character)
        {
            if (!this.configLoaded || this.lootConfig == null || character == null) return;
            if (character == CharacterMainControl.Main) return; // 排除玩家

            // 1. 防止重复挂载
            if (character.GetComponent<LootDropper>() != null) return;

            // 2. 匹配规则 (支持 ID 精确匹配)
            EnemyLootRule? matchingRule = FindMatchingRule(character);
            
            // 3. 挂载组件
            if (matchingRule != null && matchingRule.LootItems != null && matchingRule.LootItems.Count > 0)
            {
                var dropper = character.gameObject.AddComponent<LootDropper>();
                // 传入 this 引用，以便组件回调 TriggerLootDrop
                dropper.Setup(this, character, matchingRule);
                
                // ModBehaviour.LogToFile($"[LootManager] 监控目标: {ScanManager.GetCharacterID(character)}");
            }
        }

        // === 执行逻辑：组件监听到死亡后调用此方法 ===
        public void TriggerLootDrop(CharacterMainControl deadCharacter, EnemyLootRule rule)
        {
            // 启动协程
            modBehaviour.StartCoroutine(AddLootToSpawnedLootBoxCoroutine(deadCharacter, rule));
        }

        // === 智能匹配算法 (调用 ScanManager 数据库) ===
        private EnemyLootRule? FindMatchingRule(CharacterMainControl character)
        {
            if (character == null || this.lootConfig == null) return null;
            
            // 获取目标的真实 ID (PresetName) 和 显示名
            string targetID = ScanManager.GetCharacterID(character);
            string targetName = character.name;

            foreach (EnemyLootRule rule in this.lootConfig.EnemyLootRules)
            {
                if (rule.EnemyNameKeywords != null && rule.EnemyNameKeywords.Count > 0)
                {
                    foreach (string keyword in rule.EnemyNameKeywords)
                    {
                        if (string.IsNullOrEmpty(keyword)) continue;

                        // 1. 查 ScanManager 的映射表 (精准匹配)
                        if (ScanManager.NameIdMapping.TryGetValue(keyword, out string[] mappedIds))
                        {
                            // 只要目标 ID 包含映射表里的任意一个 ID (忽略大小写)
                            if (mappedIds.Any(id => targetID.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                return rule;
                            }
                        }

                        // 2. 后备：直接模糊匹配 (兼容旧配置或英文 ID)
                        if (targetID.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || 
                            targetName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return rule;
                        }
                    }
                }
            }
            // 返回默认规则
            return this.lootConfig.EnemyLootRules.FirstOrDefault(r => r.EnemyNameKeywords == null || r.EnemyNameKeywords.Count == 0);
        }

        // === 协程：等待箱子生成并注入物品 ===
        private IEnumerator AddLootToSpawnedLootBoxCoroutine(CharacterMainControl deadCharacter, EnemyLootRule matchingRule)
        {
            InteractableLootbox? foundLootbox = null;
            float timeout = 3.0f; // 等待 3 秒

            while (timeout > 0f)
            {
                if (deadCharacter == null) yield break; 

                // 搜索死亡位置附近的战利品箱 (3米内)
                foundLootbox = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>()
                    .FirstOrDefault(lootbox => 
                        lootbox.Inventory != null && 
                        Vector3.Distance(lootbox.transform.position, deadCharacter.transform.position) < 3.0f
                    );

                if (foundLootbox != null) break;
                
                yield return null; 
                timeout -= Time.deltaTime;
            }

            if (foundLootbox != null && foundLootbox.Inventory != null)
            {
                try
                {
                    if (lootConfig != null && lootConfig.ExtraSlots > 0) 
                        foundLootbox.Inventory.SetCapacity(foundLootbox.Inventory.Capacity + lootConfig.ExtraSlots);
                    
                    foreach (LootItem lootItem in matchingRule.LootItems)
                    {
                        if (UnityEngine.Random.Range(0f, 100f) <= lootItem.Chance)
                        {
                            Item? newItem = ItemAssetsCollection.InstantiateSync(lootItem.ItemID);
                            if (newItem != null)
                            {
                                newItem.StackCount = lootItem.Count;
                                foundLootbox.Inventory.AddAndMerge(newItem, 0);
                                ModBehaviour.LogToFile($"[LootManager] 掉落注入: [ID:{lootItem.ItemID} x{lootItem.Count}] -> {foundLootbox.name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile("[LootManager] 注入错误: " + ex.Message);
                }
            }
        }

        // === 配置加载 ===
        private void LoadLootConfig(string modDir)
        {
            try
            {
                string configPath = Path.Combine(modDir, "LootConfig.json");
                if (!File.Exists(configPath))
                {
                    this.lootConfig = new LootConfig(); 
                    string json = JsonConvert.SerializeObject(this.lootConfig, Formatting.Indented);
                    File.WriteAllText(configPath, json);
                }
                else
                {
                    string json = File.ReadAllText(configPath);
                    this.lootConfig = JsonConvert.DeserializeObject<LootConfig>(json); 
                }
                this.configLoaded = true;
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile("[LootManager] 配置加载异常: " + ex);
                this.configLoaded = false;
            }
        }

        // =============================================================
        //  组件类：LootDropper (集成在同一文件)
        // =============================================================
        public class LootDropper : MonoBehaviour
        {
            private LootManager? _manager;
            private CharacterMainControl? _target;
            private EnemyLootRule? _rule;
            private bool _isQuitting = false;

            public void Setup(LootManager manager, CharacterMainControl target, EnemyLootRule rule)
            {
                _manager = manager;
                _target = target;
                _rule = rule;

                if (_target != null && _target.Health != null)
                {
                    _target.Health.OnDeadEvent.AddListener(OnDeath);
                }
            }

            private void OnDeath(DamageInfo info)
            {
                if (_isQuitting) return;
                if (_manager != null && _target != null && _rule != null)
                {
                    _manager.TriggerLootDrop(_target, _rule);
                }
                Cleanup();
            }

            private void Cleanup()
            {
                if (_target != null && _target.Health != null)
                {
                    _target.Health.OnDeadEvent.RemoveListener(OnDeath);
                }
                Destroy(this);
            }

            private void OnDestroy() => Cleanup();
            private void OnApplicationQuit() => _isQuitting = true;
        }
    }

    // === 数据结构 ===
    [Serializable]
    public class LootConfig
    {
        public int ExtraSlots = 12;
        public List<EnemyLootRule> EnemyLootRules = new List<EnemyLootRule>();
    }

    [Serializable]
    public class EnemyLootRule
    {
        public List<string> EnemyNameKeywords = new List<string>();
        public List<LootItem> LootItems = new List<LootItem>();
    }

    [Serializable]
    public class LootItem
    {
        public int ItemID;
        public int Count = 1;
        public float Chance = 100f;
    }
}