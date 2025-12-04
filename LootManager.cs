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
            this.LoadLootConfig(this.configDir);
            
            // [修复] 使用 OnCharacterReady
            ScanManager.OnCharacterReady += OnEnemyFound;
            
            foreach (var character in ScanManager.ActiveCharacters)
            {
                OnEnemyFound(character);
            }
        }

        public void Deactivate()
        {
            // [修复] 取消订阅 OnCharacterReady
            ScanManager.OnCharacterReady -= OnEnemyFound;
            ModBehaviour.LogToFile("[LootManager] 已停止监听。");
        }

        private void OnEnemyFound(CharacterMainControl character)
        {
            if (!this.configLoaded || this.lootConfig == null || character == null) return;
            if (ScanManager.IsPlayer(character)) return;

            if (character.GetComponent<LootDropper>() != null) return;

            EnemyLootRule? matchingRule = FindMatchingRule(character);
            
            if (matchingRule != null && matchingRule.LootItems != null && matchingRule.LootItems.Count > 0)
            {
                var dropper = character.gameObject.AddComponent<LootDropper>();
                dropper.Setup(this, character, matchingRule);
            }
        }

        public void TriggerLootDrop(CharacterMainControl deadCharacter, EnemyLootRule rule)
        {
            modBehaviour.StartCoroutine(AddLootToSpawnedLootBoxCoroutine(deadCharacter, rule));
        }

        private EnemyLootRule? FindMatchingRule(CharacterMainControl character)
        {
            if (character == null || this.lootConfig == null) return null;
            
            string targetID = ScanManager.GetCharacterID(character);
            string targetName = ScanManager.GetCleanDisplayName(character);

            foreach (EnemyLootRule rule in this.lootConfig.EnemyLootRules)
            {
                if (rule.EnemyNameKeywords != null && rule.EnemyNameKeywords.Count > 0)
                {
                    foreach (string keyword in rule.EnemyNameKeywords)
                    {
                        if (string.IsNullOrEmpty(keyword)) continue;

                        if (ScanManager.NameIdMapping.TryGetValue(keyword, out string[] mappedIds))
                        {
                            if (mappedIds.Any(id => targetID.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0))
                                return rule;
                        }

                        if (targetID.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 || 
                            targetName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return rule;
                        }
                    }
                }
            }
            return this.lootConfig.EnemyLootRules.FirstOrDefault(r => r.EnemyNameKeywords == null || r.EnemyNameKeywords.Count == 0);
        }

        private IEnumerator AddLootToSpawnedLootBoxCoroutine(CharacterMainControl deadCharacter, EnemyLootRule matchingRule)
        {
            InteractableLootbox? foundLootbox = null;
            float timeout = 3.0f; 

            while (timeout > 0f)
            {
                if (deadCharacter == null) yield break; 
                foundLootbox = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>()
                    .FirstOrDefault(lootbox => lootbox.Inventory != null && Vector3.Distance(lootbox.transform.position, deadCharacter.transform.position) < 3.0f);

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
                    _target.Health.OnDeadEvent.AddListener(OnDeath);
            }

            private void OnDeath(DamageInfo info)
            {
                if (_isQuitting) return;
                if (_manager != null && _target != null && _rule != null)
                    _manager.TriggerLootDrop(_target, _rule);
                Cleanup();
            }

            private void Cleanup()
            {
                if (_target != null && _target.Health != null)
                    _target.Health.OnDeadEvent.RemoveListener(OnDeath);
                Destroy(this);
            }

            private void OnDestroy() => Cleanup();
            private void OnApplicationQuit() => _isQuitting = true;
        }
    }

    [Serializable] public class LootConfig { public int ExtraSlots = 12; public List<EnemyLootRule> EnemyLootRules = new List<EnemyLootRule>(); }
    [Serializable] public class EnemyLootRule { public List<string> EnemyNameKeywords = new List<string>(); public List<LootItem> LootItems = new List<LootItem>(); }
    [Serializable] public class LootItem { public int ItemID; public int Count = 1; public float Chance = 100f; }
}