using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using Duckov;
using System.Linq; 

namespace PileErico
{
    /// <summary>
    /// 全局生物扫描管理器 (Boss Logic Refined)
    /// 移除了 机械蜘蛛/神秘商人/风暴生物 的 Boss 身份
    /// </summary>
    public static class ScanManager
    {
        public static event Action<CharacterMainControl>? OnCharacterReady;
        public static event Action<CharacterMainControl>? OnCharacterLost;

        private static readonly HashSet<CharacterMainControl> _pendingCharacters = new HashSet<CharacterMainControl>();
        private static readonly HashSet<CharacterMainControl> _readyCharacters = new HashSet<CharacterMainControl>();
        private static ScanLifecycleHandler? _runner;

        public static IEnumerable<CharacterMainControl> ActiveCharacters => 
            _readyCharacters.Where(c => IsValid(c));

        public static IEnumerable<CharacterMainControl> ActiveBosses => 
            ActiveCharacters.Where(c => IsBoss(c));

        public static IEnumerable<CharacterMainControl> GetNearbyBosses(Vector3 center, float radius)
        {
            foreach (var boss in ActiveBosses)
            {
                if (boss != null && Vector3.Distance(boss.transform.position, center) <= radius)
                    yield return boss;
            }
        }

        public static bool IsBossNearby(Vector3 center, float radius) => GetNearbyBosses(center, radius).Any();

        // =========================================================
        //  数据库：黑名单 (普通敌人/召唤物/NPC/伪Boss)
        // =========================================================
        private static readonly HashSet<string> _nonBossIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // --- 常规小怪 ---
            "EnemyPreset_Scav", "Scav_Farm", "Scav_Elete", 
            "EnemyPreset_USEC", "USEC_Farm", "Mercenary", 
            "EnemyPreset_Animal_Wolf", "EnemyPreset_ScavRage", 
            
            // --- 玩家相关 ---
            "Character", "PetPreset_NormalPet",

            // --- [新增] 被降级的特殊单位 ---
            "EnemyPreset_Spider_Rifle",      // 机械蜘蛛
            "EnemyPreset_Merchant_Myst0",    // 神秘商人
            "EnemyPreset_StormCreature"      // 风暴生物
        };

        private static readonly HashSet<string> _excludeKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mercenary", "Guard", "Soldier", "Minion", "Summon", "Follower",
            "雇佣兵", "护卫", "士兵", "随从", "召唤物", "保镖",
            "Spider", "Merchant", "Storm Creature", // 同时也把关键词加入黑名单
            "机械蜘蛛", "神秘商人", "风暴生物"
        };

        // =========================================================
        //  数据库：白名单 ID (ID 绝对匹配)
        // =========================================================
        public static readonly Dictionary<string, string[]> NameIdMapping = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // --- 核心 Boss ---
            { "迷塞尔", new[] { "EnemyPreset_Boss_RPG" } },
            { "三枪哥", new[] { "EnemyPreset_Boss_3Shot" } },
            { "矮鸭", new[] { "EnemyPreset_Boss_ShortEagle" } },
            { "光之男", new[] { "EnemyPreset_Melee_UltraMan" } },
            { "急速团长", new[] { "EnemyPreset_Boss_Speedy" } },
            { "急速团成员", new[] { "EnemyPreset_Boss_Speedy_Child" } },
            { "劳登", new[] { "EnemyPreset_Boss_Deng" } }, 
            { "劳登的狗", new[] { "EnemyPreset_Boss_Deng_Wolf" } }, 
            
            // --- 实验室 ---
            { "暴走街机", new[] { "EnemyPreset_Boss_Arcade" } },
            { "测试对象", new[] { "EnemyPreset_JLab_Melee_Invisable" } },
            { "游荡者", new[] { "EnemyPreset_JLab_Raider" } },
            { "???", new[] { "EnemyPreset_Boss_Red" } }, 

            // --- 矿坑 & 设施 ---
            { "矿长", new[] { "EnemyPreset_Boss_ServerGuardian" } },
            { "高级工程师", new[] { "EnemyPreset_Boss_SenorEngineer" } },
            { "喷子", new[] { "EnemyPreset_Boss_Shot" } },
            { "炸弹狂人", new[] { "EnemyPreset_Boss_Grenade" } },
            { "维达", new[] { "EnemyPreset_Boss_Vida" } },
            { "路障", new[] { "EnemyPreset_Boss_Roadblock" } },
            { "蝇蝇队长", new[] { "EnemyPreset_Boss_Fly" } },
            { "蝇蝇队员", new[] { "EnemyPreset_Boss_Fly_Child" } },

            // --- 四天王/风暴 Boss ---
            { "口口口口", new[] { "EnemyPreset_Boss_Storm_5_Space" } },
            { "比利比利", new[] { "EnemyPreset_Boss_Storm_4_Electric" } },
            { "噗咙噗咙", new[] { "EnemyPreset_Boss_Storm_1_BreakArmor" } },
            { "噗咙", new[] { "EnemyPreset_Boss_Storm_1_Child" } },
            { "啪啦啪啦", new[] { "EnemyPreset_Boss_Storm_3_Fire" } },
            { "咕噜咕噜", new[] { "EnemyPreset_Boss_Storm_2_Poison" } }
        };

        // =========================================================
        //  数据库：关键词 (中英文 DisplayName)
        // =========================================================
        private static readonly HashSet<string> _bossKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 中文 (已移除 机械蜘蛛, 风暴生物)
            "光之男", "矮鸭", "劳登", "急速团长", "暴走街机", 
            "校霸", "BA队长", "炸弹狂人", "三枪哥", "喷子", "矿长", 
            "高级工程师", "蝇蝇队长", "迷塞尔", "维达", "路障", 
            "？？？", "???", 
            "啪啦啪啦", "咕噜咕噜", "噗咙噗咙", "比利比利", "口口口口", 
            "测试对象",
            
            // 英文 (已移除 Storm Creature)
            "Man of Light", "Pato Chapo", "Lordon", "Speedy Group Commander", 
            "Vida", "Big Xing", "Rampaging Arcade", "Senior Engineer", 
            "Triple-Shot Man", "Misel", "Mine Manager", "Shotgunner", 
            "Mad Bomber", "Security Captain", "Fly Captain", "School Bully", 
            "Billy Billy", "Gulu Gulu", "Pala Pala", "Pulu Pulu", 
            "Koko Koko", "Roadblock"
        };

        public static void Initialize()
        {
            Dispose();
            GameObject go = new GameObject("PileErico_ScanRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<ScanLifecycleHandler>();
            var existing = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
            foreach (var ch in existing) { if (ch.gameObject.activeInHierarchy) Register(ch); }
            ModBehaviour.LogToFile($"[ScanManager] 系统启动，Boss库: {NameIdMapping.Count}, 黑名单: {_nonBossIds.Count}");
        }

        public static void Dispose()
        {
            _pendingCharacters.Clear(); _readyCharacters.Clear();
            OnCharacterReady = null; OnCharacterLost = null;
            if (_runner != null) { UnityEngine.Object.Destroy(_runner.gameObject); _runner = null; }
        }

        internal static void Register(CharacterMainControl ch)
        {
            if (ch == null || _runner == null) return;
            if (_readyCharacters.Contains(ch) || _pendingCharacters.Contains(ch)) return;
            _pendingCharacters.Add(ch);
            _runner.StartCoroutine(ValidationRoutine(ch));
        }

        internal static void Unregister(CharacterMainControl? ch)
        {
            if (ch == null) return;
            CharacterMainControl validCh = ch;
            _pendingCharacters.Remove(validCh);
            if (_readyCharacters.Remove(validCh)) { try { OnCharacterLost?.Invoke(validCh); } catch { } }
        }

        private static IEnumerator ValidationRoutine(CharacterMainControl ch)
        {
            float timeout = 5f;
            while (timeout > 0)
            {
                if (ch == null || ch.gameObject == null) yield break;
                if (ch.characterPreset != null) break; 
                timeout -= Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);

            if (ch == null || ch.gameObject == null || !ch.gameObject.activeInHierarchy)
            {
                if (ch != null) _pendingCharacters.Remove(ch);
                yield break;
            }

            _pendingCharacters.Remove(ch);
            if (_readyCharacters.Add(ch))
            {
                if (IsBoss(ch)) ModBehaviour.LogToFile($"[ScanManager] 发现 BOSS: {GetCleanDisplayName(ch)} (ID: {GetCharacterID(ch)})");
                try { OnCharacterReady?.Invoke(ch); } catch { }
            }
        }

        public static void PruneInvalidCharacters() => _readyCharacters.RemoveWhere(c => c == null || !c || (c.Health != null && c.Health.IsDead));
        public static bool IsValid(CharacterMainControl? c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy && !c.Health.IsDead;
        public static bool IsPlayer(CharacterMainControl c) => c == CharacterMainControl.Main;

        public static bool IsBoss(CharacterMainControl ch)
        {
            if (!IsValid(ch)) return false;
            if (IsPlayer(ch)) return false;

            string id = GetCharacterID(ch);
            string rawDispName = ch.characterPreset?.DisplayName ?? ch.name; 
            string nameKey = ch.characterPreset?.nameKey ?? "";

            // 1. [最高优先级] 黑名单检查
            if (_excludeKeywords.Any(k => 
                id.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || 
                rawDispName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                nameKey.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }
            foreach (var nonBossId in _nonBossIds)
            {
                if (id.IndexOf(nonBossId, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }

            // 2. [次高优先级] DisplayName 关键词匹配
            foreach (var keyword in _bossKeywords)
            {
                if (rawDispName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            // 3. [兜底优先级] ID 白名单匹配
            foreach (var kvp in NameIdMapping)
            {
                if (kvp.Key == "玩家") continue;
                if (kvp.Value.Any(bossId => id.IndexOf(bossId, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }

            return false;
        }

        public static string GetCleanDisplayName(CharacterMainControl ch)
        {
            if (ch == null) return "Unknown";
            
            // 1. 优先原声 DisplayName
            try
            {
                if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.DisplayName))
                {
                    if (!ch.characterPreset.DisplayName.Contains("EnemyPreset"))
                    {
                        return CleanName(ch.characterPreset.DisplayName);
                    }
                }
            }
            catch { }

            // 2. 其次 ID 映射
            string id = GetCharacterID(ch);
            foreach (var kvp in NameIdMapping)
            {
                if (kvp.Value.Any(bossId => id.IndexOf(bossId, StringComparison.OrdinalIgnoreCase) >= 0)) return kvp.Key;
            }

            // 3. 兜底
            string fallback = ch.characterPreset?.nameKey ?? ch.name;
            return CleanName(fallback);
        }

        private static string CleanName(string name) => name.Replace("(Clone)", "").Replace("*", "").Trim();

        public static string GetCharacterID(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            try { if (ch.characterPreset != null) return ch.characterPreset.name; } catch { }
            return ch.name.Replace("(Clone)", "").Trim();
        }
    }

    public class ScanLifecycleHandler : MonoBehaviour
    {
        private void Start() => StartCoroutine(CleanupRoutine());
        private IEnumerator CleanupRoutine()
        {
            var wait = new WaitForSeconds(5f);
            while (true) { yield return wait; try { ScanManager.PruneInvalidCharacters(); } catch {} } 
        }
    }

    [HarmonyPatch(typeof(CharacterMainControl))]
    public static class CharacterScanPatches
    {
        [HarmonyPatch("OnEnable")] [HarmonyPostfix]
        public static void OnEnablePatch(CharacterMainControl __instance) => ScanManager.Register(__instance);
        [HarmonyPatch("OnDestroy")] [HarmonyPrefix]
        public static void OnDestroyPatch(CharacterMainControl __instance) => ScanManager.Unregister(__instance);
    }
}