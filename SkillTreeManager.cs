using System;
using System.Collections;
using System.Collections.Generic;
using System.IO; 
using System.Reflection; 
using UnityEngine;
using Duckov.PerkTrees;
using Duckov.PerkTrees.Behaviours;
using Duckov.UI;
using ItemStatsSystem;
using HarmonyLib;

namespace PileErico
{
    public class SkillTreeManager
    {
        private readonly ModBehaviour modBehaviour;
        private Harmony? _harmony;
        
        public const string TREE_UNIQUE_ID = "PileErico_Roll_Tree_ID";
        public const string TREE_OBJ_NAME = "PileErico_Roll_Tree"; 
        public static PerkTree? CustomRollTree;
        
        public const string SkillID_1 = "PileErico_Roll_1";
        public const string SkillID_2 = "PileErico_Roll_2";
        public const string SkillID_3 = "PileErico_Roll_3";
        public const int CostItemID = 2025000005;

        public SkillTreeManager(ModBehaviour modBehaviour, string configDir)
        {
            this.modBehaviour = modBehaviour;
        }

        public void Initialize()
        {
            ModBehaviour.LogToFile("[SkillTreeManager] 正在初始化...");
            
            // 1. 应用 UI 修复补丁
            if (_harmony == null)
            {
                try
                {
                    _harmony = new Harmony("PileErico.SkillTree.UIFixes");
                    
                    // [修复] PatchAll 需要 Assembly 参数，而不是 Type
                    _harmony.PatchAll(typeof(SkillTreeUIPatches).Assembly);
                    
                    ModBehaviour.LogToFile("[SkillTreeManager] UI 强制渲染补丁已应用。");
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile($"[SkillTreeManager] 补丁失败: {ex}");
                }
            }

            // 2. 创建数据
            CreateSkillTreeData();
        }

        public void Deactivate()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll("PileErico.SkillTree.UIFixes");
                _harmony = null;
            }

            if (CustomRollTree != null)
            {
                if (PerkTreeManager.Instance != null && PerkTreeManager.Instance.perkTrees.Contains(CustomRollTree))
                {
                    PerkTreeManager.Instance.perkTrees.Remove(CustomRollTree);
                }
                if (CustomRollTree.gameObject != null) UnityEngine.Object.Destroy(CustomRollTree.gameObject);
                CustomRollTree = null;
            }
        }

        private void CreateSkillTreeData()
        {
            if (CustomRollTree != null) return;

            try
            {
                GameObject treeObj = new GameObject("PileErico_Roll_Tree_Data");
                UnityEngine.Object.DontDestroyOnLoad(treeObj);
                
                CustomRollTree = treeObj.AddComponent<PerkTree>();
                CustomRollTree.name = TREE_OBJ_NAME; 
                
                SetPrivateField(CustomRollTree, "perkTreeID", TREE_UNIQUE_ID);
                
                var perksField = typeof(PerkTree).GetField("perks", BindingFlags.NonPublic | BindingFlags.Instance) ?? 
                                 typeof(PerkTree).GetField("m_Perks", BindingFlags.NonPublic | BindingFlags.Instance);
                if (perksField != null)
                {
                    var listType = typeof(List<>).MakeGenericType(typeof(Perk));
                    perksField.SetValue(CustomRollTree, Activator.CreateInstance(listType));
                }

                if (PerkTreeManager.Instance != null && !PerkTreeManager.Instance.perkTrees.Contains(CustomRollTree)) 
                {
                    PerkTreeManager.Instance.perkTrees.Add(CustomRollTree);
                }

                // 创建节点 (坐标 X, Y)
                Sprite? sharedIcon = null;

                // 根节点 (0,0)
                Perk p1 = CreatePerk(SkillID_1, "灵活翻滚", "降低25%翻滚的耐力消耗", 2, sharedIcon, new Vector2(0, 0));
                AddToTree(CustomRollTree, p1); 
                
                // 子节点 (300, 0)
                Perk p2 = CreatePerk(SkillID_2, "灵活翻滚II", "缩短50%的翻滚冷却时间", 4, sharedIcon, new Vector2(300, 0));
                AddRequirement(p2, p1);
                AddToTree(CustomRollTree, p2);
                
                // 孙节点 (600, 0)
                Perk p3 = CreatePerk(SkillID_3, "灵活翻滚III", "翻滚后获得20%近战伤害倍率（持续3秒）", 6, sharedIcon, new Vector2(600, 0));
                AddRequirement(p3, p2);
                AddToTree(CustomRollTree, p3);
                
                ModBehaviour.LogToFile("[SkillTreeManager] 数据注册完成。");
            }
            catch (Exception e) { ModBehaviour.LogErrorToFile($"[SkillTreeManager] 数据创建失败: {e.Message}"); }
        }

        private Perk CreatePerk(string id, string displayName, string desc, int itemCostCount, Sprite? icon, Vector2 position)
        {
            GameObject perkObj = new GameObject(id);
            if (CustomRollTree != null) 
            {
                perkObj.transform.SetParent(CustomRollTree.transform);
                perkObj.transform.localPosition = Vector3.zero;
            }
            
            RectTransform rect = perkObj.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(100, 100); 

            Perk p = perkObj.AddComponent<Perk>();
            p.name = id; 
            
            SetPrivateField(p, "displayName", displayName); 
            SetPrivateField(p, "hasDescription", false); 
            SetPrivateField(p, "master", CustomRollTree);
            if (icon != null) SetPrivateField(p, "icon", icon);

            // 挂载描述组件
            var descHolder = perkObj.AddComponent<PerkDescriptionHolder>();
            descHolder.text = desc;

            try 
            {
                var reqField = typeof(Perk).GetField("requirement", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (reqField != null)
                {
                    var req = Activator.CreateInstance(reqField.FieldType); 
                    
                    var costField = req.GetType().GetField("cost");
                    if(costField != null)
                    {
                        var cost = Activator.CreateInstance(costField.FieldType);
                        var itemsField = cost.GetType().GetField("items");
                        if(itemsField != null)
                        {
                            var itemsList = Activator.CreateInstance(itemsField.FieldType);
                            itemsField.SetValue(cost, itemsList);

                            Type itemAmountType = itemsList.GetType().GetGenericArguments()[0];
                            object itemAmount = Activator.CreateInstance(itemAmountType);
                            
                            SetFieldOnObject(itemAmount, "itemTypeID", CostItemID);
                            SetFieldOnObject(itemAmount, "amount", itemCostCount);

                            itemsList.GetType().GetMethod("Add").Invoke(itemsList, new object[] { itemAmount });
                        }
                        costField.SetValue(req, cost);
                    }
                    reqField.SetValue(p, req);
                }
            } catch { }
            return p;
        }

        private void AddToTree(PerkTree tree, Perk perk) 
        { 
            try { 
                var listField = typeof(PerkTree).GetField("perks", BindingFlags.NonPublic | BindingFlags.Instance) ?? 
                                typeof(PerkTree).GetField("m_Perks", BindingFlags.NonPublic | BindingFlags.Instance); 
                if (listField != null) { 
                    var list = listField.GetValue(tree); 
                    list.GetType().GetMethod("Add")?.Invoke(list, new object[] { perk }); 
                } 
            } catch { } 
        }

        private void AddRequirement(Perk child, Perk parent) 
        { 
            try { 
                var reqField = typeof(Perk).GetField("requirement", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (reqField != null)
                {
                    var req = reqField.GetValue(child);
                    var perksListField = req.GetType().GetField("perks");
                    if (perksListField != null)
                    {
                        var list = perksListField.GetValue(req);
                        if (list == null)
                        {
                            var listType = typeof(List<>).MakeGenericType(typeof(Perk));
                            list = Activator.CreateInstance(listType);
                            perksListField.SetValue(req, list);
                        }
                        list.GetType().GetMethod("Add")?.Invoke(list, new object[] { parent });
                    }
                }
            } catch { } 
        }

        private void SetPrivateField(object target, string fieldName, object? value) 
        { 
            if (target == null) return; 
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); 
            field?.SetValue(target, value); 
        }

        private void SetFieldOnObject(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if(field != null) field.SetValue(target, value);
        }

        private void SetProperty(object target, string propName, object? value) 
        { 
            var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); 
            if (prop != null && prop.CanWrite) prop.SetValue(target, value); 
        }
    }

    public class PerkDescriptionHolder : MonoBehaviour
    {
        public string text = "";
    }

    // =============================================================
    // 关键补丁：强制劫持 UI 绘制流程
    // =============================================================
    public static class SkillTreeUIPatches
    {
        // 1. 劫持 PopulatePerks：绕过官方 Graph 检查，强制显示图标
        [HarmonyPatch(typeof(PerkTreeView), "PopulatePerks")]
        [HarmonyPrefix]
        public static bool PopulatePerks_Prefix(PerkTreeView __instance)
        {
            // 获取 target 字段
            var targetField = typeof(PerkTreeView).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
            var target = targetField?.GetValue(__instance) as PerkTree;

            // 只有当打开的是我们的技能树时，才进行劫持
            if (target != null && target.name == SkillTreeManager.TREE_OBJ_NAME)
            {
                // 手动执行类似官方的逻辑，但去掉 Graph 检查
                try
                {
                    // 获取私有成员
                    var contentParent = GetField<RectTransform>(__instance, "contentParent");
                    var perkEntryPoolProp = typeof(PerkTreeView).GetProperty("PerkEntryPool", BindingFlags.NonPublic | BindingFlags.Instance);
                    var perkEntryPool = perkEntryPoolProp?.GetValue(__instance) as Duckov.Utilities.PrefabPool<PerkEntry>;
                    
                    var perkLinePoolProp = typeof(PerkTreeView).GetProperty("PerkLinePool", BindingFlags.NonPublic | BindingFlags.Instance);
                    var perkLinePool = perkLinePoolProp?.GetValue(__instance) as Duckov.Utilities.PrefabPool<Duckov.UI.PerkLineEntry>;

                    if (contentParent != null && perkEntryPool != null && perkLinePool != null)
                    {
                        // 1. 刷新布局
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentParent);
                        perkEntryPool.ReleaseAll();
                        perkLinePool.ReleaseAll();

                        // 2. 暴力生成所有 Perk 图标
                        // [修复] 使用公开属性 Perks，因为 perks 字段是 internal
                        foreach (Perk perk in target.Perks)
                        {
                            var entry = perkEntryPool.Get(contentParent);
                            entry.Setup(__instance, perk);
                        }

                        // 3. 调用布局计算 (反射调用 FitChildren)
                        var fitMethod = typeof(PerkTreeView).GetMethod("FitChildren", BindingFlags.NonPublic | BindingFlags.Instance);
                        fitMethod?.Invoke(__instance, null);

                        // 4. 调用连线绘制 (反射调用 RefreshConnections - 注意：我们也需要补丁它)
                        var refreshMethod = typeof(PerkTreeView).GetMethod("RefreshConnections", BindingFlags.NonPublic | BindingFlags.Instance);
                        refreshMethod?.Invoke(__instance, null);
                    }
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile($"[SkillTreeUIPatches] 强制渲染出错: {ex}");
                }

                return false; // [关键] 阻止执行原版逻辑
            }
            return true; // 其他技能树照常执行
        }

        // 2. 修复坐标获取：防止 UI 访问 Graph 报错
        [HarmonyPatch(typeof(Perk), "GetLayoutPosition")]
        [HarmonyPrefix]
        public static bool GetLayoutPosition_Prefix(Perk __instance, ref Vector2 __result)
        {
            if (__instance.Master != null && __instance.Master.name == SkillTreeManager.TREE_OBJ_NAME)
            {
                var rt = __instance.GetComponent<RectTransform>();
                __result = rt != null ? rt.anchoredPosition : Vector2.zero;
                return false; 
            }
            return true;
        }

        // 3. 修复解锁判断：防止 UI 访问 Graph 报错
        [HarmonyPatch(typeof(PerkTree), "AreAllParentsUnlocked")]
        [HarmonyPrefix]
        public static bool AreAllParentsUnlocked_Prefix(PerkTree __instance, Perk perk, ref bool __result)
        {
            if (__instance.name == SkillTreeManager.TREE_OBJ_NAME)
            {
                if (perk.Requirement == null) { __result = true; return false; }

                var req = perk.Requirement;
                var listField = req.GetType().GetField("perks");
                if (listField != null)
                {
                    var parents = listField.GetValue(req) as System.Collections.IList;
                    if (parents != null)
                    {
                        foreach (Perk parent in parents)
                        {
                            if (!parent.Unlocked) { __result = false; return false; }
                        }
                    }
                }
                __result = true;
                return false;
            }
            return true;
        }

        // 4. 修复描述文本显示
        [HarmonyPatch(typeof(Perk), "Description", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Description_Getter_Prefix(Perk __instance, ref string __result)
        {
            if (__instance.Master != null && __instance.Master.name == SkillTreeManager.TREE_OBJ_NAME)
            {
                var holder = __instance.GetComponent<PerkDescriptionHolder>();
                __result = holder != null ? holder.text : "No Description";
                return false;
            }
            return true;
        }

        // 5. 阻止连线绘制崩溃
        [HarmonyPatch(typeof(PerkTreeView), "RefreshConnections")]
        [HarmonyPrefix]
        public static bool RefreshConnections_Prefix(PerkTreeView __instance)
        {
            var targetField = typeof(PerkTreeView).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
            var target = targetField?.GetValue(__instance) as PerkTree;

            if (target != null && target.name == SkillTreeManager.TREE_OBJ_NAME)
            {
                // 我们暂时不绘制连线，为了防止访问 Graph 报错，直接跳过原版逻辑
                return false; 
            }
            return true;
        }

        // 工具方法
        private static T? GetField<T>(object instance, string name) where T : class
        {
            var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(instance) as T;
        }
    }
}