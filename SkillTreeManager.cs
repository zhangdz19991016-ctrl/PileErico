#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.PerkTrees;
using Duckov.PerkTrees.Behaviours;
using Duckov.Economy;
// using HarmonyLib; 
using ItemStatsSystem;
using NodeCanvas.Framework; 
using ParadoxNotion;       
using SodaCraft.Localizations; 

namespace PileErico
{
    public class SkillTreeManager
    {
        // =========================================================
        // 1. 静态配置
        // =========================================================
        public const string TREE_UNIQUE_ID = "PileErico_Roll_Tree_ID";
        public const int CostItemID = 2025000005; 

        public static PerkTree? CustomRollTree; 
        
        // 静态单例引用
        public static SkillTreeManager? Instance { get; private set; }

        private List<string> _autoRegisteredKeys = new List<string>();
        
        private readonly ModBehaviour _mod;
        private readonly string _modDir;

        public SkillTreeManager(ModBehaviour mod, string configDir)
        {
            _mod = mod;
            _modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            Instance = this;
        }

        public void Initialize()
        {
            RegisterAutoText("PerkTree_" + TREE_UNIQUE_ID, "灵魂升华");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void Deactivate()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            foreach (var key in _autoRegisteredKeys)
            {
                LocalizationManager.RemoveOverrideText(key);
            }
            _autoRegisteredKeys.Clear();

            if (CustomRollTree != null)
            {
                if (PerkTreeManager.Instance != null && PerkTreeManager.Instance.perkTrees.Contains(CustomRollTree))
                    PerkTreeManager.Instance.perkTrees.Remove(CustomRollTree);

                if (CustomRollTree.gameObject != null)
                    UnityEngine.Object.Destroy(CustomRollTree.gameObject);
                
                CustomRollTree = null;
            }
            Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Base_SceneV2" || scene.name == "Base")
            {
                _mod.StartCoroutine(BuildTreeRoutine());
            }
        }

        // =========================================================
        // 2. 核心功能：洗点逻辑
        // =========================================================
        public void ResetSkills(CharacterMainControl player, string ignorePerkID)
        {
            if (CustomRollTree == null || player == null) return;

            int resetCount = 0;

            foreach (var perk in CustomRollTree.Perks)
            {
                bool isResetNode = perk.name == ignorePerkID;

                if (perk.Unlocked)
                {
                    // 强制上锁
                    var prop = typeof(Perk).GetProperty("Unlocked");
                    prop?.SetValue(perk, false);
                    
                    if (!isResetNode) resetCount++;
                }
            }

            if (resetCount > 0)
            {
                player.PopText("<color=#00FFFF>记忆已重置</color>", 2.0f);
            }
        }

        // =========================================================
        // 3. 技能树构建流程
        // =========================================================
        private IEnumerator BuildTreeRoutine()
        {
            yield return null; 
            if (CustomRollTree != null) yield break;

            PerkTree? template = PerkTreeManager.GetPerkTree("Skills");
            if (template == null)
            {
                var building = GameObject.Find("SkillMachine");
                if (building != null) template = building.GetComponentInChildren<PerkTree>(true);
            }
            if (template == null) yield break;

            GameObject treeObj = UnityEngine.Object.Instantiate(template.gameObject);
            treeObj.name = "PileErico_Roll_Tree_Obj";
            UnityEngine.Object.DontDestroyOnLoad(treeObj);
            treeObj.SetActive(false); 

            CustomRollTree = treeObj.GetComponent<PerkTree>();
            SetPrivateField(CustomRollTree, "horizontal", false);

            foreach (Transform child in treeObj.transform) UnityEngine.Object.Destroy(child.gameObject);
            if (CustomRollTree.RelationGraphOwner?.graph != null)
            {
                var graph = (PerkRelationGraph)CustomRollTree.RelationGraphOwner.graph;
                graph.allNodes.Clear(); 
                graph.GetGraphSource().connections.Clear(); 
                graph.UpdateGraph();
            }

            SetPrivateField(CustomRollTree, "perkTreeID", TREE_UNIQUE_ID);
            SetPrivateField(CustomRollTree, "perks", new List<Perk>()); 
            SetPrivateField(CustomRollTree, "perks_ReadOnly", null);    

            // ==========================================
            // [新增] 记忆回溯 (洗点节点)
            // ====================================================================================
            Perk resetNode = CreatePerk(
                id: "PE_Rest_Skill",
                name: "治愈灵魂",
                desc: "连灵魂深处的黑暗一同\n将强加于自身的力量\n全部燃烧殆尽\n\n将学会的技能\n<b><color=#9E0000>全部遗忘</color></b>",
                
                iconName: "治愈灵魂.png", 
                
                uiPos: new Vector2(80, 0), 
                costs: new List<(int, int)> { (2025000005, 1) }, 
                reqLevel: 0
            );
            resetNode.gameObject.AddComponent<PerkResetBehaviour>();

            // ====================================================================================
            // ====================================================================================
            // --- 技能 恢复强化 ---
            Perk EF1 = CreatePerk(
                id: "EstusFlask_I",
                name: "<b><color=#83D179>治疗效果提升 I</color></b>",
                desc: "因为灵魂深处那无法愈合的创伤\n你将比以往任何时候都更渴望被治愈\n\n提高 10% <b><color=#9E0000>回复效果加成</color></b>",
                iconName: "强化回复.png", 
                uiPos: new Vector2(-80, 0),
                costs: new List<(int, int)> { (201192214, 5) },
                reqLevel: 5,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "HealGain", value = 0.1f },
                }

            );
            Perk EF2 = CreatePerk(
                id: "EstusFlask_II",
                name: "<b><color=#83D179>治疗效果提升 II</color></b>",
                desc: "你的饥渴无法被轻易填满\n试着掠夺更多\n并归于己身吧\n\n提高 10% <b><color=#9E0000>回复效果加成</color></b>",
                iconName: "强化回复.png", 
                uiPos: new Vector2(-80, 375),
                costs: new List<(int, int)> { (201192214, 10) },
                reqLevel: 13,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "HealGain", value = 0.1f },
                }
            );

            Perk EF3 = CreatePerk(
                id: "EstusFlask_III",
                name: "<b><color=#83D179>治疗效果提升 III</color></b>",
                desc: "给我更多！！\n\n提高 10% <b><color=#9E0000>回复效果加成</color></b>",
                iconName: "强化回复.png", 
                uiPos: new Vector2(-80, 825),
                costs: new List<(int, int)> { (201192214, 20) },
                reqLevel: 23,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "HealGain", value = 0.1f },
                }
            );

            // ====================================================================================
            // ====================================================================================
            // --- 技能 黑暗印记 I ---
            Perk DS1 = CreatePerk(
                id: "Dark_Sigil_I",
                name: "<b><color=#000000>黑暗印记 I</color></b>",
                desc: "触碰自己内在的<b><color=#000000>黑暗</color></b>\n将其转化为力量\n但这并非没有代价\n\n你的行动将变得迟缓，但身体却会更为强韧\n在减少移动速度的同时提高些许<b><color=#9E0000>生命上限</color></b>",
                
                iconName: "黑暗印记.png", // [修正] 直接使用字符串
                
                uiPos: new Vector2(0, 0),
                costs: new List<(int, int)> { 
                    (201192214, 5), 
                    (308, 10)
                },
                reqLevel: 3,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "WalkSpeed", value = -0.3f},
                    new ModifyCharacterStatsBase.Entry { key = "RunSpeed", value = -0.6f},
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 5f},
                }
            );
            // --- 光之男 ---
            Perk UM1 = CreatePerk(
                id: "UltraMan_I",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华",
                desc: "通过与<b><color=#FFBABA>光之男</color></b>的<b><color=#7EC1D9>灵魂能量碎片</color></b>产生共鸣\n你将获得来自<b><color=#FFBABA>光之男</color></b>的力量\n\n提高 5% 近战伤害倍率",
                iconName: "光之男.png", 
                uiPos: new Vector2(-40, 75),
                costs: new List<(int, int)> { (2025111101, 1) },
                reqLevel: 5,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MeleeDamageMultiplier", value = 0.05f },
                }
            );

            Perk UM2 = CreatePerk(
                id: "UltraMan_II",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华：肉体 I",
                desc: "你从<b><color=#FFBABA>光之男</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加强壮\n这使你可以携带的物品数量上升了\n\n提高 10 背包容量",
                iconName: "光之男.png", 
                uiPos: new Vector2(-60, 150),
                costs: new List<(int, int)> { (2025111101, 1) },
                reqLevel: 7,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "InventoryCapacity", value = 10f},
                }
            );

            Perk UM3 = CreatePerk(
                id: "UltraMan_III",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华：肉体 II",
                desc: "你从<b><color=#FFBABA>光之男</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加强壮\n这使你可以承受的物品重量上升了\n\n提高 10 负重上限",
                iconName: "光之男.png", 
                uiPos: new Vector2(-60, 225),
                costs: new List<(int, int)> { (2025111101, 1) },
                reqLevel: 9,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MaxWeight", value = 2f},
                }
            );

            Perk UM4 = CreatePerk(
                id: "UltraMan_IV",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华：意志 I",
                desc: "你从<b><color=#FFBABA>光之男</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加坚韧\n这使你的物理抗性提高了\n\n降低 5% 受到的物理伤害",
                iconName: "光之男.png", 
                uiPos: new Vector2(-20, 150),
                costs: new List<(int, int)> { (2025111101, 1) },
                reqLevel: 7,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ElementFactor_Physics", value = -0.05f},
                }
            );
            Perk UM5 = CreatePerk(
                id: "UltraMan_V",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华：意志 II",
                desc: "你从<b><color=#FFBABA>光之男</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加坚韧\n这使你的物理抗性进一步提高了\n\n再降低 5% 受到的物理伤害",
                iconName: "光之男.png", 
                uiPos: new Vector2(-20, 225),
                costs: new List<(int, int)> { (2025111101, 1) },
                reqLevel: 9,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ElementFactor_Physics", value = -0.05f},
                }
            );

            Perk UM6 = CreatePerk(
                id: "UltraMan_VI",
                name: "<b><color=#FFBABA>光之男</color></b>的灵魂升华：狂战士",
                desc: "你从<b><color=#FFBABA>光之男</color></b>的灵魂中攫取了全部的碎片\n<b><color=#FFBABA>光之男</color></b>的力量在你的体内回响\n你感到热血沸腾\n\n提高 10% 近战暴击率\n提高 10% 近战暴击伤害\n<b><color=#9E0000>降低 0.25 头部护甲</color></b>\n<b><color=#9E0000>降低 0.25 身体护甲</color></b>",
                iconName: "狂战士.png", 
                uiPos: new Vector2(-40, 300),
                costs: new List<(int, int)> { (2025111101, 2) },
                reqLevel: 11,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MeleeCritRateGain", value = 0.1f },
                    new ModifyCharacterStatsBase.Entry { key = "MeleeCritDamageGain", value = 0.1f },
                    new ModifyCharacterStatsBase.Entry { key = "HeadArmor", value = -0.25f},
                    new ModifyCharacterStatsBase.Entry { key = "BodyArmor", value = -0.25f},
                }
            );      
            // ====================================================================================              
            // ====================================================================================
            // --- 矮鸭 ---
            Perk SE1 = CreatePerk(
                id: "ShortEagle_I",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华",
                desc: "通过与<b><color=#EB901A>矮鸭</color></b>的<b><color=#7EC1D9>灵魂能量碎片</color></b>产生共鸣\n你将获得来自<b><color=#EB901A>矮鸭</color></b>的力量\n\n提高 5% 远程伤害倍率",
                iconName: "矮鸭.png", 
                uiPos: new Vector2(40, 75),
                costs: new List<(int, int)> { (2025111102, 1) },
                reqLevel: 5,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "GunDamageMultiplier", value = 0.05f },
                }
            );

            Perk SE2 = CreatePerk(
                id: "ShortEagle_II",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华：迅捷 I",
                desc: "你从<b><color=#EB901A>矮鸭</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加灵巧\n这使你的翻滚速度提高了\n\n提高翻滚的距离（因速度变化而提高）",
                iconName: "矮鸭.png", 
                uiPos: new Vector2(20, 150),
                costs: new List<(int, int)> { (2025111102, 1) },
                reqLevel: 7,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "DashSpeed", value = 1f },
                }
            );  

            Perk SE3 = CreatePerk(
                id: "ShortEagle_III",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华：迅捷 II",
                desc: "你从<b><color=#EB901A>矮鸭</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加灵巧\n这使你能在翻滚途中做出额外的动作\n\n允许在翻滚时执行额外动作",
                iconName: "矮鸭.png", 
                uiPos: new Vector2(20, 225),
                costs: new List<(int, int)> { (2025111102, 1) },
                reqLevel: 9,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "DashCanControl", value = 1f },
                }
            );

            Perk SE4 = CreatePerk(
                id: "ShortEagle_IV",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华：敏锐 I",
                desc: "你从<b><color=#EB901A>矮鸭</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加警觉\n这使你的视野变得更宽\n\n提高 15 视野角度",
                iconName: "矮鸭.png", 
                uiPos: new Vector2(60, 150),
                costs: new List<(int, int)> { (2025111102, 1) },
                reqLevel: 7,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ViewAngle", value =15f },
                }
            );

            Perk SE5 = CreatePerk(
                id: "ShortEagle_V",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华：敏锐 II",
                desc: "你从<b><color=#EB901A>矮鸭</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加警觉\n这使你的视野变得更远\n\n提高 5 视野距离",
                iconName: "矮鸭.png", 
                uiPos: new Vector2(60, 225),
                costs: new List<(int, int)> { (2025111102, 1) },
                reqLevel: 9,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ViewDistance", value = 5f },
                }
            );

            Perk SE6 = CreatePerk(
                id: "ShortEagle_VI",
                name: "<b><color=#EB901A>矮鸭</color></b>的灵魂升华：快枪手",
                desc: "你从<b><color=#EB901A>矮鸭</color></b>的灵魂中攫取了全部的碎片\n<b><color=#EB901A>矮鸭</color></b>的力量在你的体内回响\n你已经火力全开\n\n提高 10% 武器装填速度\n提高 10% 子弹速度\n<b><color=#9E0000>增加 2 体力消耗</color></b>",
                iconName: "快枪手.png", 
                uiPos: new Vector2(40, 300),
                costs: new List<(int, int)> { (2025111102, 2) },
                reqLevel: 11,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ReloadSpeedGain", value = 0.1f },
                    new ModifyCharacterStatsBase.Entry { key = "BulletSpeedMultiplier", value = 0.1f },
                    new ModifyCharacterStatsBase.Entry { key = "StaminaDrainRate", value = 2f },
                }
            );
            // ====================================================================================              
            // ====================================================================================
            // --- 杂项内容-锻体 ---
            Perk PE1 = CreatePerk(
                id: "PE_Trainning_I",
                name: "锻体学徒",
                desc: "以泥巴为榜样\n你踏上了锻体之旅\n\n提高 5 <b><color=#9E0000>生命上限</color></b>",
                iconName: "锻炼1.png", 
                uiPos: new Vector2(80, 75),
                costs: new List<(int, int)> { (875, 2) },
                reqLevel: 5,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 5f },
                }
            );
            Perk PE2 = CreatePerk(
                id: "PE_Trainning_II",
                name: "锻体精英",
                desc: "你变得更加强壮\n你的信心前所未有的高涨\n\n提高 5 <b><color=#9E0000>生命上限</color></b>\n降低 5 <b><color=#4B90D6>水分上限</color></b>",
                iconName: "锻炼2.png", 
                uiPos: new Vector2(80, 300),
                costs: new List<(int, int)> { (875, 2), (409, 2) },
                reqLevel: 10,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 5f },
                    new ModifyCharacterStatsBase.Entry { key = "MaxWater", value = -5f },
                }
            );
            Perk PE3 = CreatePerk(
                id: "PE_Trainning_III",
                name: "锻体大师",
                desc: "你已是远近闻名的锻体大师\n但仍无法与泥巴相比\n这并不合常理\n\n提高 5% 近战伤害倍率\n降低 5 <b><color=#4B90D6>水分上限</color></b>",
                iconName: "锻炼3.png", 
                uiPos: new Vector2(80, 450),
                costs: new List<(int, int)> { (875, 2), (409, 2), (800, 2) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MeleeDamageMultiplier", value = 0.05f },
                    new ModifyCharacterStatsBase.Entry { key = "MaxWater", value = -5f },
                }
            );
            Perk PE4 = CreatePerk(
                id: "PE_Trainning_IV",
                name: "锻体传奇",
                desc: "你发现了泥巴完美身材背后的秘密\n九 龙 拉 棺\n————你终究还是踏上了这条不归路\n\n提高 10 <b><color=#9E0000>生命上限</color></b>\n提高 0.3 饱腹消耗\n提高 0.3 水分消耗",
                iconName: "锻炼4.png", 
                uiPos: new Vector2(80, 750),
                costs: new List<(int, int)> { (875, 2), (409, 2), (800, 2), (872, 2) },
                reqLevel: 20,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 10f },
                    new ModifyCharacterStatsBase.Entry { key = "EnergyCost", value = 0.3f },
                    new ModifyCharacterStatsBase.Entry { key = "WaterCost", value = 0.3f },
                }
            );
            // ====================================================================================              
            // ====================================================================================
            // --- 杂项内容-其他 ---
            Perk ARMOR = CreatePerk(
                id: "ARMOR_I",
                name: "橘子的支援",
                desc: "橘子的防具店正在蒸蒸日上\n于情于理你都应该给他一点好处\n而他也不会辜负你的友善\n\n提高 0.5 头部护甲\n提高 0.5 身体护甲",
                iconName: "全副武装.png", 
                uiPos: new Vector2(-80, -75),
                costs: new List<(int, int)> { (14, 3), (1252, 1) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "HeadArmor", value = 0.5f },
                    new ModifyCharacterStatsBase.Entry { key = "BodyArmor", value = 0.5f },
                }
            );

            Perk GHOST = CreatePerk(
                id: "GHOST_I",
                name: "The Chosen One",
                desc: "对于选择成为不死鸭的你来说\n所谓的幽灵不过只是一个笑话罢了\n\n降低 40% 灵伤害倍率",
                iconName: "闹鬼.png", 
                uiPos: new Vector2(-40, -75),
                costs: new List<(int, int)> { (2025000005, 3) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ElementFactor_Ghost", value = -0.4f },
                }
            );

            Perk CARRY = CreatePerk(
                id: "CARRY",
                name: "永不言弃",
                desc: "身无分文又如何？\n多跑一趟的事罢了\n\n增加 12 背包容量\n增加 10 负重上限 ",
                iconName: "搬运工.png", 
                uiPos: new Vector2(0, -75),
                costs: new List<(int, int)> { (2025000004, 1) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "InventoryCapacity", value = 12f },
                    new ModifyCharacterStatsBase.Entry { key = "MaxWeight", value = 2f },
                }
            );

            Perk FISH = CreatePerk(
                id: "FISH",
                name: "何谓空军？",
                desc: "钓鱼真比搜打车好玩吧\n骗你的\n其实那些鱼都是从跳蚤市场上买来的\n\n减少 10% 被觉察距离",
                iconName: "钓鱼佬.png", 
                uiPos: new Vector2(40, -75),
                costs: new List<(int, int)> { (1119, 3) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "VisableDistanceFactor", value = -0.1f },
                }
            );



            // ====================================================================================
            // ====================================================================================
            // --- 黑暗印记 II ---
            Perk DS2 = CreatePerk(
                id: "Dark_Sigil_II",
                name: "<b><color=#000000>黑暗印记 II</color></b>",
                desc: "你渴望从中攫取更多\n掠夺他人的<b><color=#7EC1D9>灵魂</color></b>使你感到满足\n但你的身体也将因此承受更多\n\n身体变得更加沉重，但生命力却在涌现\n降低更多移动速度的同时提高更多<b><color=#9E0000>生命上限</color></b>",
                iconName: "黑暗印记.png", 
                uiPos: new Vector2(0, 375),
                costs: new List<(int, int)> { (201192214, 10), (309, 10) },
                reqLevel: 13,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "WalkSpeed", value = -0.3f},
                    new ModifyCharacterStatsBase.Entry { key = "RunSpeed", value = -0.6f},
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 10f},
                }
            );
            // --- 牢登 ---
            Perk L1 = CreatePerk(
                id: "Lordon_I",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华",
                desc: "通过与<b><color=#6E8DC9>牢登</color></b>的<b><color=#7EC1D9>灵魂能量碎片</color></b>产生共鸣\n你将获得来自<b><color=#6E8DC9>牢登</color></b>的力量\n\n降低 5% 射击散布",
                iconName: "牢登.png", 
                uiPos: new Vector2(-40, 450),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "GunScatterMultiplier", value = -0.05f },
                }
            );

            Perk L2 = CreatePerk(
                id: "Lordon_II",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华：生存 I",
                desc: "你从<b><color=#6E8DC9>牢登</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加娴熟\n这使你可以在户外坚持更长时间\n\n提高 10 <b><color=#F08E05>饱腹上限</color></b>\n提高 10 <b><color=#4B90D6>水分上限</color></b>",
                iconName: "牢登.png", 
                uiPos: new Vector2(-60, 525),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 17,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "MaxEnergy", value = 10f},
                    new ModifyCharacterStatsBase.Entry { key = "MaxWater", value = 10f},
                }
            );

            Perk L3 = CreatePerk(
                id: "Lordon_III",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华：生存 II",
                desc: "你从<b><color=#6E8DC9>牢登</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加娴熟\n这使你可以减少自己发出的噪音\n\n略微降低行动时的噪音范围",
                iconName: "牢登.png", 
                uiPos: new Vector2(-60, 600),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 19,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "WalkSoundRange", value = -2f },
                    new ModifyCharacterStatsBase.Entry { key = "RunSoundRange", value = -4f },
                }
            );

            Perk L4 = CreatePerk(
                id: "Lordon_IV",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华：精准 I",
                desc: "你从<b><color=#6E8DC9>牢登</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加专注\n这使你的射程变得更远\n\n提高 10% 射程",
                iconName: "牢登.png", 
                uiPos: new Vector2(-20, 525),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 17,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "GunDistanceMultiplier", value = 0.1f },
                }
            );
            Perk L5 = CreatePerk(
                id: "Lordon_V",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华：精准 II",
                desc: "你从<b><color=#6E8DC9>牢登</color></b>的灵魂中攫取了力量的碎片\n你感到自己变得更加专注\n这使你的射击精度进一步提高\n\n降低 10% 射击散布",
                iconName: "牢登.png", 
                uiPos: new Vector2(-20, 600),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 19,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "GunScatterMultiplier", value = -0.1f },
                }
            );

            Perk L6 = CreatePerk(
                id: "Lordon_VI",
                name: "<b><color=#6E8DC9>牢登</color></b>的灵魂升华：狙击手",
                desc: "你从<b><color=#6E8DC9>牢登</color></b>的灵魂中攫取了全部的碎片\n<b><color=#6E8DC9>牢登</color></b>的力量在你的体内回响\n你感到无比平静\n\n提高 15% 远程暴击率\n提高 15% 远程暴击伤害\n<b><color=#9E0000>降低 10 生命上限</color></b>\n<b><color=#9E0000>降低 10 体力上限</color></b>",
                iconName: "狙击手.png", 
                uiPos: new Vector2(-40, 675),
                costs: new List<(int, int)> { (2025111103, 2) },
                reqLevel: 21,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "GunCritRateGain", value = 0.15f },
                    new ModifyCharacterStatsBase.Entry { key = "GunCritDamageGain", value = 0.15f },
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = -10f},
                    new ModifyCharacterStatsBase.Entry { key = "Stamina", value = -10f},
                }
            ); 

            Perk L7 = CreatePerk(
                id: "Lordon_VII",
                name: "<b><color=#6E8DC9>牢登的爱犬</color></b>",
                desc: "<b><color=#6E8DC9>牢登的狗</color></b>是<b><color=#6E8DC9>牢登</color></b>最信任的伙伴\n彼此间的牵绊让他们的<b><color=#7EC1D9>灵魂</color></b>\n紧密相连\n\n提高 1 宠物物品栏上限",
                iconName: "牢登.png", 
                uiPos: new Vector2(-40, 750),
                costs: new List<(int, int)> { (2025111103, 1) },
                reqLevel: 21,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "PetCapcity", value = 1f},
                    // 注意：游戏本体字段名就拼成了 PetCapcity（不是 PetCapacity），只能跟着错用
                }
            );

            // --- 急速团长 ---
            Perk SC1 = CreatePerk(
                id: "SpeedyCommander_I",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华",
                desc: "通过与<b><color=#EDD74A>急速团长</color></b>的<b><color=#7EC1D9>灵魂能量碎片</color></b>产生共鸣\n你将获得来自<b><color=#EDD74A>急速团长</color></b>的力量\n\n提高 1.5 冲刺速度",
                iconName: "急速团长.png", 
                uiPos: new Vector2(40, 450),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 15,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "RunSpeed", value = 1.5f },
                }
            );

            Perk SC2 = CreatePerk(
                id: "SpeedyCommander_II",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华：疾驰 I",
                desc: "你从<b><color=#EDD74A>急速团长</color></b>的灵魂中攫取了力量的碎片\n你感到自己身轻如燕\n这使你可以更加灵活地辗转腾挪\n\n提高 50 转向速率",
                iconName: "急速团长.png", 
                uiPos: new Vector2(20, 525),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 17,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "TurnSpeed", value = 50f },
                }
            );

            Perk SC3 = CreatePerk(
                id: "SpeedyCommander_III",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华：疾驰 II",
                desc: "你从<b><color=#EDD74A>急速团长</color></b>的灵魂中攫取了力量的碎片\n你感到自己身轻如燕\n这使你可以更快地冲刺\n\n提高 0.5 冲刺加速",
                iconName: "急速团长.png", 
                uiPos: new Vector2(20, 600),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 19,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "RunAcc", value = 0.5f },
                }
            );

            Perk SC4 = CreatePerk(
                id: "SpeedyCommander_IV",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华：压制 I",
                desc: "你从<b><color=#EDD74A>急速团长</color></b>的灵魂中攫取了力量的碎片\n你开始热衷于向敌人倾泻子弹\n这使你的装填速度变得更快\n\n提高 15% 装填速度",
                iconName: "急速团长.png", 
                uiPos: new Vector2(60, 525),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 17,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ReloadSpeedGain", value = 0.15f },
                }
            );
            Perk SC5 = CreatePerk(
                id: "SpeedyCommander_V",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华：压制 II",
                desc: "你从<b><color=#EDD74A>急速团长</color></b>的灵魂中攫取了力量的碎片\n你开始热衷于向敌人倾泻子弹\n为此你必须能更好地控制自己的武器\n\n提高 20% 后坐力控制",
                iconName: "急速团长.png", 
                uiPos: new Vector2(60, 600),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 19,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "RecoilControl", value = 0.2f },
                }
            );

            Perk SC6 = CreatePerk(
                id: "SpeedyCommander_VI",
                name: "<b><color=#EDD74A>急速团长</color></b>的灵魂升华：团伙领袖",
                desc: "你从<b><color=#EDD74A>急速团长</color></b>的灵魂中攫取了全部的碎片\n<b><color=#EDD74A>急速团长</color></b>的力量在你的体内回响\n你需要更多刺激来让自己亢奋\n\n提高 15% 全方面的机动性能\n降低 20% 体力回复延迟\n<b><color=#9E0000>降低 1.25 头部护甲</color></b>\n<b><color=#9E0000>降低 1.25 身体护甲</color></b>",
                iconName: "领袖.png", 
                uiPos: new Vector2(40, 675),
                costs: new List<(int, int)> { (2025111104, 2) },
                reqLevel: 21,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "Moveability", value = 0.15f },
                    new ModifyCharacterStatsBase.Entry { key = "StaminaRecoverTime", value = -0.2f },
                    new ModifyCharacterStatsBase.Entry { key = "HeadArmor", value = -1.25f},
                    new ModifyCharacterStatsBase.Entry { key = "BodyArmor", value = -1.25f},
                }
            ); 

            Perk SC7 = CreatePerk(
                id: "SpeedyCommander_VII",
                name: "<b><color=#EDD74A>急速团的羁绊</color></b>",
                desc: "<b><color=#EDD74A>急速团长</color></b>和<b><color=#EDD74A>急速团员</color></b>总是并肩作战\n他们相互扶持\n至死方休\n\n减少 15% 电伤害倍率",
                iconName: "急速团长.png", 
                uiPos: new Vector2(40, 750),
                costs: new List<(int, int)> { (2025111104, 1) },
                reqLevel: 21,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "ElementFactor_Electricity", value = -0.15f},
                }
            );

            // ====================================================================================
            // ====================================================================================
            // --- 黑暗印记 III ---

            Perk DS3 = CreatePerk(
                id: "Dark_Sigil_III",
                name: "<b><color=#000000>黑暗印记 III</color></b>",
                desc: "你沉浸于<b><color=#7EC1D9>灵魂</color></b>的滋养\n你渴望更多\n但也因此付出更多\n\n诅咒缠身的同时，但你的肉体也变得前所未有的强韧\n进一步降低移动速度的同时提高了大量<b><color=#9E0000>生命上限</color></b>",
                iconName: "黑暗印记.png", 
                uiPos: new Vector2(0, 825),
                costs: new List<(int, int)> { (201192214, 10),(2025000005, 5), (309, 20) },
                reqLevel: 23,
                statModifiers: new List<ModifyCharacterStatsBase.Entry> {
                    new ModifyCharacterStatsBase.Entry { key = "WalkSpeed", value = -0.6f},
                    new ModifyCharacterStatsBase.Entry { key = "RunSpeed", value = -1.2f},
                    new ModifyCharacterStatsBase.Entry { key = "MaxHealth", value = 20},
                }
            );

            // ====================================================================================
            // ====================================================================================     
            
            //强化回复
            ConnectPerks(EF1, EF2);
            ConnectPerks(EF2, EF3);
            //杂项_锻体
            ConnectPerks(PE1, PE2);ConnectPerks(PE2, PE3);ConnectPerks(PE3, PE4);
            
            //黑暗印记
            ConnectPerks(DS1, DS2);
            ConnectPerks(DS2, DS3);

            //光之男
            ConnectPerks(DS1, UM1);
            ConnectPerks(UM1, UM2);ConnectPerks(UM2, UM3);
            ConnectPerks(UM1, UM4);ConnectPerks(UM4, UM5);
            ConnectPerks(UM3, UM6);ConnectPerks(UM5, UM6);
            //矮鸭
            ConnectPerks(DS1, SE1);
            ConnectPerks(SE1, SE2);ConnectPerks(SE2, SE3);
            ConnectPerks(SE1, SE4);ConnectPerks(SE4, SE5);
            ConnectPerks(SE3, SE6);ConnectPerks(SE5, SE6);
            //牢登
            ConnectPerks(DS2, L1);
            ConnectPerks(L1, L2);ConnectPerks(L2, L3);
            ConnectPerks(L1, L4);ConnectPerks(L4, L5);
            ConnectPerks(L3, L6);ConnectPerks(L5, L6);
            ConnectPerks(L6, L7);
            //急速团长
            ConnectPerks(DS2, SC1);
            ConnectPerks(SC1, SC2);ConnectPerks(SC2, SC3);
            ConnectPerks(SC1, SC4);ConnectPerks(SC4, SC5);
            ConnectPerks(SC3, SC6);ConnectPerks(SC5, SC6);
            ConnectPerks(SC6, SC7);

            treeObj.SetActive(true);
            if (PerkTreeManager.Instance != null && !PerkTreeManager.Instance.perkTrees.Contains(CustomRollTree))
            {
                PerkTreeManager.Instance.perkTrees.Add(CustomRollTree);
            }

            ModBehaviour.LogToFile("<<< [SkillTreeManager] 黑暗印记技能树构建完成");
        }

        // =========================================================
        // 4. 辅助工具
        // =========================================================
        
        private void RegisterAutoText(string key, string text)
        {
            LocalizationManager.SetOverrideText(key, text);
            if (!_autoRegisteredKeys.Contains(key)) _autoRegisteredKeys.Add(key);
        }

        // [统一] 唯一的 CreatePerk，支持 string iconName
        private Perk CreatePerk(string id, string name, string desc, string? iconName, Vector2 uiPos, List<(int id, int amount)> costs, int reqLevel = 0, List<ModifyCharacterStatsBase.Entry>? statModifiers = null)
        {
            if (CustomRollTree == null) throw new InvalidOperationException("Tree is null");

            GameObject go = new GameObject(id);
            go.SetActive(false); 
            go.transform.SetParent(CustomRollTree.transform);
            go.transform.localPosition = Vector3.zero;

            Perk perk = go.AddComponent<Perk>();
            perk.name = id;

            string nameKey = $"PE_Name_{id}"; 
            RegisterAutoText(nameKey, name); 
            SetPrivateField(perk, "displayName", nameKey);

            SetPrivateField(perk, "master", CustomRollTree);
            
            // [新逻辑] 内部加载
            if (!string.IsNullOrEmpty(iconName))
            {
                Sprite? icon = LoadSprite(iconName!);
                if (icon != null) SetPrivateField(perk, "icon", icon);
            }
            
            SetPrivateField(perk, "hasDescription", false);
            
            var descComp = go.AddComponent<PerkDescriptionBehaviour>();
            descComp.descriptionText = desc;

            if (statModifiers != null && statModifiers.Count > 0)
            {
                var modStats = go.AddComponent<SilentModifyCharacterStats>();
                var field = typeof(ModifyCharacterStatsBase).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(modStats, statModifiers);
            }

            ConfigureRequirement(perk, reqLevel, costs);

            var perksList = GetPrivateField<IList>(CustomRollTree, "perks");
            perksList?.Add(perk);
            AddToGraph(perk, uiPos);
            
            go.SetActive(true);
            return perk;
        }

        // [重载] 用于支持传递 Sprite 对象的特殊情况 (如 resetNode 使用 fallback 图标)
        private Perk CreatePerk(string id, string name, string desc, Sprite? icon, Vector2 uiPos, List<(int id, int amount)> costs, int reqLevel = 0, List<ModifyCharacterStatsBase.Entry>? statModifiers = null)
        {
            if (CustomRollTree == null) throw new InvalidOperationException("Tree is null");

            GameObject go = new GameObject(id);
            go.SetActive(false); 
            go.transform.SetParent(CustomRollTree.transform);
            go.transform.localPosition = Vector3.zero;

            Perk perk = go.AddComponent<Perk>();
            perk.name = id;

            string nameKey = $"PE_Name_{id}"; 
            RegisterAutoText(nameKey, name); 
            SetPrivateField(perk, "displayName", nameKey);

            SetPrivateField(perk, "master", CustomRollTree);
            
            if (icon != null) SetPrivateField(perk, "icon", icon);
            
            SetPrivateField(perk, "hasDescription", false);
            
            var descComp = go.AddComponent<PerkDescriptionBehaviour>();
            descComp.descriptionText = desc;

            if (statModifiers != null && statModifiers.Count > 0)
            {
                var modStats = go.AddComponent<SilentModifyCharacterStats>();
                var field = typeof(ModifyCharacterStatsBase).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(modStats, statModifiers);
            }

            ConfigureRequirement(perk, reqLevel, costs);

            var perksList = GetPrivateField<IList>(CustomRollTree, "perks");
            perksList?.Add(perk);
            AddToGraph(perk, uiPos);
            
            go.SetActive(true);
            return perk;
        }

        private void ConfigureRequirement(Perk perk, int requiredLevel, List<(int id, int amount)> costs)
        {
            try {
                PerkRequirement req = new PerkRequirement();
                req.level = requiredLevel;
                req.requireTime = TimeSpan.FromSeconds(1).Ticks;

                Cost c = new Cost();
                c.items = new Cost.ItemEntry[costs.Count];
                
                for(int i = 0; i < costs.Count; i++)
                {
                    var entry = new Cost.ItemEntry();
                    entry.id = costs[i].id;
                    entry.amount = costs[i].amount;
                    c.items[i] = entry;
                }

                req.cost = c;
                SetPrivateField(perk, "requirement", req);
            } catch { }
        }

        private void AddToGraph(Perk perk, Vector2 pos)
        {
            if (CustomRollTree?.RelationGraphOwner?.graph == null) return;
            var graph = (PerkRelationGraph)CustomRollTree.RelationGraphOwner.graph;
            PerkRelationNode node = graph.AddNode<PerkRelationNode>();
            if (node != null) { node.relatedNode = perk; node.cachedPosition = pos; }
            graph.UpdateGraph();
        }

        private void ConnectPerks(Perk parent, Perk child)
        {
            if (CustomRollTree?.RelationGraphOwner?.graph == null) return;
            try {
                var req = GetPrivateField<PerkRequirement>(child, "requirement");
                if (req != null) {
                    var perksField = typeof(PerkRequirement).GetField("perks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var list = perksField?.GetValue(req) as IList;
                    if (list == null) {
                        var listType = typeof(List<Perk>);
                        list = Activator.CreateInstance(listType) as IList;
                        perksField?.SetValue(req, list);
                    }
                    list?.Add(parent);
                }
            } catch { }
            var graph = (PerkRelationGraph)CustomRollTree.RelationGraphOwner.graph;
            var pNode = graph.GetRelatedNode(parent);
            var cNode = graph.GetRelatedNode(child);
            if (pNode != null && cNode != null) graph.ConnectNodes(pNode, cNode);
        }

        private Sprite? LoadSprite(string fileName)
        {
            if (string.IsNullOrEmpty(_modDir)) return null;
            string path = Path.Combine(_modDir, "Icons", fileName);
            if (File.Exists(path)) {
                try {
                    byte[] data = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(tex, data))
                        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                } catch {}
            }
            return null;
        }

        private void SetPrivateField(object target, string name, object? value)
        {
            if (target == null) return;
            var t = target.GetType();
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            while(f == null && t.BaseType != null) { t = t.BaseType; f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); }
            f?.SetValue(target, value);
        }

        private T? GetPrivateField<T>(object target, string name) where T : class
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return f?.GetValue(target) as T;
        }
    }

    // =========================================================
    // 5. 辅助类
    // =========================================================
    
    // [洗点逻辑]
    public class PerkResetBehaviour : PerkBehaviour
    {
        protected override void OnUnlocked()
        {
            SkillTreeManager.Instance?.ResetSkills(CharacterMainControl.Main, "PE_Reset_Node");
        }
    }

    public class PerkDescriptionBehaviour : PerkBehaviour
    {
        public string descriptionText = "";
        public override string Description => descriptionText;
    }

    public class SilentModifyCharacterStats : ModifyCharacterStatsBase
    {
        public override string Description => string.Empty;
    }
}