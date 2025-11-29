using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using Duckov; 

namespace PileErico
{
    /// <summary>
    /// 全局生物扫描管理器 & 中央 ID 数据库
    /// </summary>
    public static class ScanManager
    {
        // === 1. 事件系统 ===
        public static event Action<CharacterMainControl>? OnCharacterSpawned;
        public static event Action<CharacterMainControl>? OnCharacterDespawned;

        // === 2. 缓存列表 (提供给 InvasionManager 等模块查询) ===
        private static readonly HashSet<CharacterMainControl> _activeCharacters = new HashSet<CharacterMainControl>();
        public static IReadOnlyCollection<CharacterMainControl> ActiveCharacters => _activeCharacters;

        // ========================================================================
        // 3. 中央生物数据库 (Name -> IDs)
        // ========================================================================
        public static readonly Dictionary<string, string[]> NameIdMapping = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // === 核心 Boss ===
            { "迷塞尔", new[] { "EnemyPreset_Boss_RPG" } },
            { "三枪哥", new[] { "EnemyPreset_Boss_3Shot" } },
            { "矮鸭", new[] { "EnemyPreset_Boss_ShortEagle" } },
            { "光之男", new[] { "EnemyPreset_Melee_UltraMan" } },
            { "急速团长", new[] { "EnemyPreset_Boss_Speedy" } },
            { "急速团成员", new[] { "EnemyPreset_Boss_Speedy_Child" } },
            { "劳登", new[] { "EnemyPreset_Boss_Deng" } },
            { "劳登的狗", new[] { "EnemyPreset_Boss_Deng_Wolf" } },
            { "神秘商人", new[] { "EnemyPreset_Merchant_Myst0", "EnemyPreset_Merchant" } },
            { "风暴生物", new[] { "EnemyPreset_StormCreature" } },
            { "暴走街机", new[] { "EnemyPreset_Boss_Arcade" } },
            { "蝇蝇队长", new[] { "EnemyPreset_Boss_Fly" } },
            { "蝇蝇队员", new[] { "EnemyPreset_Boss_Fly_Child" } },
            { "矿长", new[] { "EnemyPreset_Boss_ServerGuardian" } },
            { "高级工程师", new[] { "EnemyPreset_Boss_SenorEngineer" } },
            { "喷子", new[] { "EnemyPreset_Boss_Shot" } },
            { "炸弹狂人", new[] { "EnemyPreset_Boss_Grenade" } },
            { "维达", new[] { "EnemyPreset_Boss_Vida" } },
            { "路障", new[] { "EnemyPreset_Boss_Roadblock" } },

            // === 实验室 / 特殊单位 ===
            { "机械蜘蛛", new[] { "EnemyPreset_Spider_Rifle", "EnemyPreset_Spider_Rifle_JLab" } },
            { "实验室蜘蛛", new[] { "EnemyPreset_Spider_Rifle_JLab" } },
            { "???", new[] { "EnemyPreset_Boss_Red" } }, // 实验室红名Boss
            { "测试对象", new[] { "EnemyPreset_JLab_Melee_Invisable" } },
            { "游荡者", new[] { "EnemyPreset_JLab_Raider" } },

            // === 风暴四天王 ===
            { "口口口口", new[] { "EnemyPreset_Boss_Storm_5_Space" } },
            { "比利比利", new[] { "EnemyPreset_Boss_Storm_4_Electric" } },
            { "噗咙噗咙", new[] { "EnemyPreset_Boss_Storm_1_BreakArmor" } },
            { "噗咙", new[] { "EnemyPreset_Boss_Storm_1_Child" } },
            { "啪啦啪啦", new[] { "EnemyPreset_Boss_Storm_3_Fire" } },
            { "咕噜咕噜", new[] { "EnemyPreset_Boss_Storm_2_Poison" } },

            // === 常规单位 (支持模糊前缀匹配) ===
            { "拾荒者", new[] { "EnemyPreset_Scav", "EnemyPreset_Scav_low", "Scav_Farm", "Scav_Elete" } },
            { "雇佣兵", new[] { "EnemyPreset_USEC", "USEC_Farm" } },
            { "狼", new[] { "EnemyPreset_Animal_Wolf" } },
            { "暴走拾荒者", new[] { "EnemyPreset_ScavRage" } },
            
            // === 玩家与宠物 ===
            { "玩家", new[] { "Character(Clone)" } },
            { "宠物", new[] { "PetPreset_NormalPet" } }
        };

        // === 4. 辅助方法：获取标准化 ID ===
        public static string GetCharacterID(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            try
            {
                // 优先返回 Preset Name (最准确的内部 ID)
                if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.name))
                {
                    return ch.characterPreset.name;
                }
            }
            catch { }
            
            // 如果没有 Preset，回退到 GameObject Name (兼容 Character(Clone) 等情况)
            return ch.name ?? string.Empty;
        }

        // === 5. 生命周期管理 ===
        public static void Initialize()
        {
            // 清理旧数据
            _activeCharacters.Clear();

            // 捕获场上已存在的单位 (防止热重载时漏怪)
            var existing = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
            foreach (var ch in existing)
            {
                Register(ch);
            }
            ModBehaviour.LogToFile($"[ScanManager] 初始化完毕，已捕获存量单位: {_activeCharacters.Count}");
        }

        public static void Dispose()
        {
            _activeCharacters.Clear();
            OnCharacterSpawned = null;
            OnCharacterDespawned = null;
        }

        // 内部注册
        internal static void Register(CharacterMainControl ch)
        {
            if (ch == null) return;
            if (_activeCharacters.Add(ch))
            {
                try 
                { 
                    OnCharacterSpawned?.Invoke(ch); 
                }
                catch (Exception ex) 
                { 
                    ModBehaviour.LogErrorToFile($"[ScanManager] Spawn事件异常: {ex}"); 
                }
            }
        }

        // 内部注销
        internal static void Unregister(CharacterMainControl ch)
        {
            if (ch == null) return;
            if (_activeCharacters.Remove(ch))
            {
                try 
                { 
                    OnCharacterDespawned?.Invoke(ch); 
                }
                catch (Exception ex) 
                { 
                    ModBehaviour.LogErrorToFile($"[ScanManager] Despawn事件异常: {ex}"); 
                }
            }
        }
    }

    // === 6. Harmony 补丁 (零轮询核心) ===
    [HarmonyPatch(typeof(CharacterMainControl))]
    public static class CharacterScanPatches
    {
        [HarmonyPatch("Start")] 
        [HarmonyPostfix]
        public static void OnCharacterStart(CharacterMainControl __instance)
        {
            ScanManager.Register(__instance);
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPrefix]
        public static void OnCharacterDestroy(CharacterMainControl __instance)
        {
            ScanManager.Unregister(__instance);
        }
    }
}