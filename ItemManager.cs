using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI;
using Duckov.Buffs;

namespace PileErico
{
    public class ItemManager
    {
        private List<int> _processedItemIds = new List<int>();
        private GameObject? _buffPrefabHolder; 

        public void Initialize()
        {
            // 创建一个不销毁的节点存放 Buff 模板
            _buffPrefabHolder = new GameObject("[Lifegem_Buff_Templates]");
            UnityEngine.Object.DontDestroyOnLoad(_buffPrefabHolder);

            // =============================================================
            // 1. 修理箱 
            // =============================================================
            ProcessItem<OpenRepairViewBehavior>(2025000006);

            // =============================================================
            // 2. 黑魂滴石系列 (无名称显示版)
            // =============================================================

            // --- 滴石 (Small) ---
            ProcessItem<StackableHealBehavior>(201192220, (behavior, itemPrefab) => 
            {
                behavior.buffID = "Lifegem_Small";
                behavior.duration = 15f;
                behavior.healPerSec = 1.0f;
                behavior.maxStacks = 3;
                
                behavior.visualBuffTemplate = CreateBuffTemplate(
                    2025111210, 
                    "", // [修改] 名字留空，只显示图标
                    "持续缓慢恢复生命值。", 
                    itemPrefab.Icon, 
                    15f, 
                    3
                );
            });

            // --- 辉滴石 (Radiant) ---
            ProcessItem<StackableHealBehavior>(201192221, (behavior, itemPrefab) => 
            {
                behavior.buffID = "Lifegem_Radiant";
                behavior.duration = 25f;
                behavior.healPerSec = 1.5f;
                behavior.maxStacks = 3;

                behavior.visualBuffTemplate = CreateBuffTemplate(
                    2025111211, 
                    "", // [修改] 名字留空
                    "持续恢复生命值。", 
                    itemPrefab.Icon, 
                    25f, 
                    3
                );
            });

            // --- 古老辉滴石 (Old) ---
            ProcessItem<StackableHealBehavior>(201192222, (behavior, itemPrefab) => 
            {
                behavior.buffID = "Lifegem_Old";
                behavior.duration = 35f;
                behavior.healPerSec = 2.0f;
                behavior.maxStacks = 3;

                behavior.visualBuffTemplate = CreateBuffTemplate(
                    2025111212, 
                    "", // [修改] 名字留空
                    "持续强力恢复生命值。", 
                    itemPrefab.Icon, 
                    35f, 
                    3
                );
            });

            Debug.Log("[ItemManager] 滴石模组加载完成。");
        }

        public void Deactivate() 
        {
            if (_buffPrefabHolder != null) UnityEngine.Object.Destroy(_buffPrefabHolder);
        }

        private Buff CreateBuffTemplate(int id, string displayName, string description, Sprite? icon, float duration, int maxLayers)
        {
            // 确保父对象存在
            if (_buffPrefabHolder == null)
            {
                _buffPrefabHolder = new GameObject("[Lifegem_Buff_Templates]");
                UnityEngine.Object.DontDestroyOnLoad(_buffPrefabHolder);
            }

            GameObject go = new GameObject($"BuffTemplate_{id}");
            go.transform.SetParent(_buffPrefabHolder.transform);
            go.SetActive(false); 

            Buff buff = go.AddComponent<Buff>();
            
            // 使用反射设置属性
            SetField(buff, "id", id);
            SetField(buff, "displayName", displayName);
            SetField(buff, "description", description);
            SetField(buff, "icon", icon); 
            SetField(buff, "limitedLifeTime", true);
            SetField(buff, "totalLifeTime", duration);
            SetField(buff, "maxLayers", maxLayers);
            SetField(buff, "exclusiveTag", 0); // 0 = NotExclusive

            return buff;
        }

        // 通用注册方法
        private void ProcessItem<T>(int itemId, Action<T, Item>? configure = null) where T : UsageBehavior
        {
            var item = ItemAssetsCollection.GetPrefab(itemId);
            if (item == null) { Debug.LogError($"未找到物品 ID: {itemId}"); return; }

            UsageUtilities usage = item.GetComponent<UsageUtilities>();
            if (usage == null) usage = item.gameObject.AddComponent<UsageUtilities>();
            SetField(item, "usageUtilities", usage);

            if (usage.behaviors == null) usage.behaviors = new List<UsageBehavior>();
            
            var old = item.GetComponents<T>();
            foreach (var b in old) UnityEngine.Object.DestroyImmediate(b);
            usage.behaviors.RemoveAll(b => b == null || b is T);

            var comp = item.gameObject.AddComponent<T>();
            if (configure != null) configure(comp, item);
            usage.behaviors.Add(comp);

            if (!_processedItemIds.Contains(itemId)) _processedItemIds.Add(itemId);
        }

        private void SetField(object target, string fieldName, object? value)
        {
            if (target == null) return;
            var type = target.GetType();
            FieldInfo? field = null;
            while (type != null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) break;
                type = type.BaseType;
            }
            if (field != null) field.SetValue(target, value);
        }

        // =============================================================
        // 行为脚本
        // =============================================================
        public class OpenRepairViewBehavior : UsageBehavior
        {
            public override bool CanBeUsed(Item item, object user) => true;
            protected override void OnUse(Item item, object user)
            {
                if (ItemRepairView.Instance != null) ItemRepairView.Show();
            }
        }

        public class StackableHealBehavior : UsageBehavior
        {
            public string buffID = "";
            public float duration;
            public float healPerSec;
            public int maxStacks;
            public Buff? visualBuffTemplate; 

            public override bool CanBeUsed(Item item, object user)
            {
                var character = user as CharacterMainControl ?? CharacterMainControl.Main;
                // [使用修正过的判断逻辑]
                return character != null && character.Health != null && !character.Health.IsDead;
            }

            protected override void OnUse(Item item, object user)
            {
                var character = user as CharacterMainControl ?? CharacterMainControl.Main;
                if (character == null) return;

                // 1. 触发实际回血逻辑
                var controller = character.GetComponent<HealBuffController>();
                if (controller == null) controller = character.gameObject.AddComponent<HealBuffController>();
                controller.AddBuff(buffID, healPerSec, duration, maxStacks);

                // 2. 触发 UI 图标显示
                if (visualBuffTemplate != null)
                {
                    var gameBuffManager = character.GetComponent<CharacterBuffManager>();
                    if (gameBuffManager != null)
                    {
                        gameBuffManager.AddBuff(visualBuffTemplate, character, 0);
                    }
                }
            }
        }

        public class HealBuffController : MonoBehaviour
        {
            private class BuffState
            {
                public float healPerSec;
                public float timer;
                public int stacks;
                public int maxStacks;
            }

            private Dictionary<string, BuffState> _activeBuffs = new Dictionary<string, BuffState>();
            private CharacterMainControl? _character;
            private float _tickTimer = 0f;
            private const float TickInterval = 0.5f; 

            private void Awake() => _character = GetComponent<CharacterMainControl>();

            public void AddBuff(string id, float hps, float duration, int limit)
            {
                if (!_activeBuffs.ContainsKey(id))
                    _activeBuffs[id] = new BuffState { healPerSec = hps, maxStacks = limit, stacks = 0 };

                var buff = _activeBuffs[id];
                buff.timer = duration; 
                if (buff.stacks < buff.maxStacks) buff.stacks++; 
            }

            private void Update()
            {
                // [使用修正过的判断逻辑]
                if (_character == null || _character.Health == null || _character.Health.IsDead) return;
                if (_activeBuffs.Count == 0) return;

                float dt = Time.deltaTime;
                List<string>? toRemove = null;

                foreach (var kv in _activeBuffs)
                {
                    kv.Value.timer -= dt;
                    if (kv.Value.timer <= 0) (toRemove ??= new List<string>()).Add(kv.Key);
                }
                if (toRemove != null) foreach (var key in toRemove) _activeBuffs.Remove(key);

                _tickTimer += dt;
                if (_tickTimer >= TickInterval)
                {
                    float totalHeal = 0f;
                    foreach (var buff in _activeBuffs.Values)
                        totalHeal += buff.healPerSec * buff.stacks * TickInterval;

                    if (totalHeal > 0) 
                    {
                        try 
                        { 
                            // [使用修正过的方法名 AddHealth]
                            _character.Health.AddHealth(totalHeal); 
                        } 
                        catch { }
                    }
                    _tickTimer = 0f;
                }
            }
        }
    }
}