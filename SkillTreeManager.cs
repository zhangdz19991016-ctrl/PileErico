using System;
using System.Collections;
using System.Collections.Generic;
using System.IO; 
using System.Reflection;
using System.Linq; 
using Duckov.Modding;
using Duckov.PerkTrees;
using Duckov.PerkTrees.Behaviours;
using Duckov.PerkTrees.Interactable;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Stats; 
using PileErico;
using UnityEngine;
using UnityEngine.UI; 
using UnityEngine.SceneManagement; 
using Duckov; 
using Duckov.Economy; 

namespace PileErico
{
    public class SkillTreeManager
    {
        private readonly ModBehaviour modBehaviour;
        private readonly string configDir;
        private static Harmony? harmony;
        
        public static PerkTree? CustomRollTree;
        
        public const string TREE_UNIQUE_ID = "PileErico_Roll_Tree_ID";
        public const string SkillID_1 = "PileErico_Roll_1";
        public const string SkillID_2 = "PileErico_Roll_2";
        public const string SkillID_3 = "PileErico_Roll_3";
        public const int CostItemID = 2025000005;

        public static SkillTreeManager? Instance { get; private set; }

        public SkillTreeManager(ModBehaviour modBehaviour, string configDir)
        {
            this.modBehaviour = modBehaviour;
            this.configDir = configDir;
            Instance = this;
        }

        public void Initialize()
        {
            ModBehaviour.LogToFile("[SkillTreeManager] 正在初始化...");
            
            if (harmony == null)
            {
                try
                {
                    harmony = new Harmony("com.pileerico.skilltree");
                    harmony.PatchAll(Assembly.GetExecutingAssembly());
                    ModBehaviour.LogToFile("[SkillTreeManager] Harmony 补丁已应用。");
                }
                catch (Exception ex) { ModBehaviour.LogErrorToFile($"[SkillTreeManager] Harmony 补丁失败: {ex.Message}"); }
            }

            CreateSkillTree();
        }

        public void Deactivate()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll("com.pileerico.skilltree");
                harmony = null;
            }
            
            if (CustomRollTree != null)
            {
                if (PerkTreeManager.Instance != null)
                {
                    PerkTreeManager.Instance.perkTrees.Remove(CustomRollTree);
                }
                if (CustomRollTree.gameObject != null)
                {
                    GameObject.Destroy(CustomRollTree.gameObject);
                }
                CustomRollTree = null;
            }
            
            Instance = null;
        }

        public void AttachToMerchant(GameObject merchantInteractableObj)
        {
            if (merchantInteractableObj == null) return;
            modBehaviour.StartCoroutine(AttachRoutine(merchantInteractableObj));
        }

        private IEnumerator AttachRoutine(GameObject targetObj)
        {
            yield return null; 
            InteractableBase mainInteractable = targetObj.GetComponent<InteractableBase>();
            if (mainInteractable == null) mainInteractable = targetObj.GetComponentInChildren<InteractableBase>();

            if (mainInteractable != null)
            {
                AttachToExistingGroup(mainInteractable);
            }
            else
            {
                ModBehaviour.LogErrorToFile($"[SkillTreeManager] 无法在 {targetObj.name} 上找到 InteractableBase。");
            }
        }

        private void AttachToExistingGroup(InteractableBase mainInteractable)
        {
            try
            {
                GameObject parentObj = mainInteractable.gameObject;
                ModBehaviour.LogToFile($"[SkillTreeManager] 正在挂载技能树到: {parentObj.name}");

                // 1. 创建逻辑子物体
                GameObject skillObj = new GameObject("PileErico_SkillInteract");
                skillObj.transform.SetParent(parentObj.transform);
                skillObj.transform.localPosition = Vector3.zero;
                
                // [修复幽灵点] 强制设置为 Ignore Raycast (Layer 2)
                skillObj.layer = 2; 

                // 2. 添加组件
                PerkTreeUIInvoker skillInvoker = skillObj.AddComponent<PerkTreeUIInvoker>();
                skillInvoker.InteractName = "学习身法"; 
                skillInvoker.perkTreeID = TREE_UNIQUE_ID;
                skillInvoker.MarkerActive = false; 
                skillInvoker.enabled = true;

                // [修复幽灵点] 移除可能自动生成的碰撞体
                var colliders = skillObj.GetComponents<Collider>();
                foreach(var c in colliders) GameObject.Destroy(c);
                
                // 3. 激活
                skillObj.SetActive(false);
                skillObj.SetActive(true);

                // 二次清理
                foreach(var c in skillObj.GetComponents<Collider>()) GameObject.Destroy(c);

                // 预设 isGroup
                var flagField = typeof(InteractableBase).GetField("isGroup", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (flagField != null) flagField.SetValue(mainInteractable, true);

                ModBehaviour.LogToFile("[SkillTreeManager] 挂载对象创建完成。");
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile($"[SkillTreeManager] 挂载出错: {ex.Message}");
            }
        }

        private void CreateSkillTree()
        {
            if (CustomRollTree != null) return;
            try
            {
                GameObject treeObj = new GameObject("PileErico_Roll_Tree_Obj");
                GameObject.DontDestroyOnLoad(treeObj);
                CustomRollTree = treeObj.AddComponent<PerkTree>();
                CustomRollTree.name = "PileErico_Roll_Tree";
                SetPrivateField(CustomRollTree, "perkTreeID", TREE_UNIQUE_ID);
                var perksField = typeof(PerkTree).GetField("perks", BindingFlags.NonPublic | BindingFlags.Instance) ?? typeof(PerkTree).GetField("m_Perks", BindingFlags.NonPublic | BindingFlags.Instance);
                if (perksField != null) {
                    var listType = typeof(List<>).MakeGenericType(typeof(Perk));
                    perksField.SetValue(CustomRollTree, Activator.CreateInstance(listType));
                }
                if (PerkTreeManager.Instance != null && !PerkTreeManager.Instance.perkTrees.Contains(CustomRollTree)) PerkTreeManager.Instance.perkTrees.Add(CustomRollTree);

                Sprite? sharedIcon = null;
                try {
                    string dllPath = Assembly.GetExecutingAssembly().Location;
                    string modDir = Path.GetDirectoryName(dllPath) ?? string.Empty;
                    string iconPath = Path.Combine(modDir, "icons", "灵活翻滚.png");
                    sharedIcon = LoadSprite(iconPath);
                } catch { }

                Perk p1 = CreatePerk(SkillID_1, "灵活翻滚", "降低25%翻滚的耐力消耗", 2, sharedIcon, new Vector2(0, 0));
                AddToTree(CustomRollTree, p1); 
                Perk p2 = CreatePerk(SkillID_2, "灵活翻滚II", "缩短50%的翻滚冷却时间", 4, sharedIcon, new Vector2(200, 0));
                AddRequirement(p2, p1);
                AddToTree(CustomRollTree, p2);
                Perk p3 = CreatePerk(SkillID_3, "灵活翻滚III", "翻滚后获得20%近战伤害倍率（持续3秒）", 6, sharedIcon, new Vector2(400, 0));
                AddRequirement(p3, p2);
                AddToTree(CustomRollTree, p3);
            }
            catch (Exception e) { ModBehaviour.LogErrorToFile($"[SkillTreeManager] 创建技能树失败: {e.Message}"); }
        }

        private Perk CreatePerk(string id, string displayName, string desc, int itemCostCount, Sprite? icon, Vector2 position)
        {
            GameObject perkObj = new GameObject(id);
            if (CustomRollTree != null) {
                perkObj.transform.SetParent(CustomRollTree.transform);
                perkObj.transform.localPosition = Vector3.zero;
                perkObj.transform.localScale = Vector3.one;
            }
            RectTransform rect = perkObj.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(100, 100); 
            Perk p = perkObj.AddComponent<Perk>();
            p.name = id; 
            SetPrivateField(p, "displayNameKey", displayName); 
            SetPrivateField(p, "descriptionKey", desc);
            SetPrivateField(p, "master", CustomRollTree);
            if (icon != null) {
                if (!SetPrivateField(p, "m_Icon", icon) && !SetPrivateField(p, "_icon", icon)) SetProperty(p, "Icon", icon);
            }
            try {
                var listField = typeof(Perk).GetField("requiredItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField != null) {
                    var listObj = listField.GetValue(p); 
                    if (listObj == null) { listObj = Activator.CreateInstance(listField.FieldType); listField.SetValue(p, listObj); }
                    if (listObj != null) {
                        Type itemAmountType = listObj.GetType().GetGenericArguments()[0];
                        object costObj = Activator.CreateInstance(itemAmountType);
                        SetPrivateField(costObj, "itemTypeID", CostItemID);
                        SetPrivateField(costObj, "amount", itemCostCount);
                        listObj.GetType().GetMethod("Add").Invoke(listObj, new object[] { costObj });
                    }
                }
                var reqField = typeof(Perk).GetField("requirement", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (reqField != null && reqField.GetValue(p) == null) { object reqObj = Activator.CreateInstance(reqField.FieldType); reqField.SetValue(p, reqObj); }
            } catch { }
            return p;
        }

        private void AddToTree(PerkTree tree, Perk perk) { try { var listField = typeof(PerkTree).GetField("perks", BindingFlags.NonPublic | BindingFlags.Instance) ?? typeof(PerkTree).GetField("m_Perks", BindingFlags.NonPublic | BindingFlags.Instance); if (listField != null) { var list = listField.GetValue(tree); if (list == null) { list = Activator.CreateInstance(listField.FieldType); listField.SetValue(tree, list); } list.GetType().GetMethod("Add")?.Invoke(list, new object[] { perk }); } } catch { } }
        private void AddRequirement(Perk child, Perk parent) { try { PerkRequirement req = new PerkRequirement(); SetPrivateField(req, "requiredPerk", parent); var reqListField = typeof(Perk).GetField("requirements", BindingFlags.NonPublic | BindingFlags.Instance) ?? typeof(Perk).GetField("m_Requirements", BindingFlags.NonPublic | BindingFlags.Instance); if (reqListField != null) { var list = reqListField.GetValue(child); if (list == null) { list = Activator.CreateInstance(reqListField.FieldType); reqListField.SetValue(child, list); } list.GetType().GetMethod("Add")?.Invoke(list, new object[] { req }); } } catch { } }
        private bool SetPrivateField(object target, string fieldName, object? value) { if (target == null) return false; var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); if (field != null) { field.SetValue(target, value); return true; } return false; }
        private void SetProperty(object target, string propName, object? value) { var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (prop != null && prop.CanWrite) prop.SetValue(target, value); }
        private Sprite? LoadSprite(string filePath) { if (!File.Exists(filePath)) return null; byte[] fileData = File.ReadAllBytes(filePath); Texture2D tex = new Texture2D(2, 2); if (tex.LoadImage(fileData)) return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f); return null; }
    }

    [HarmonyPatch(typeof(InteractableBase))]
    public static class InteractableGroupPatch
    {
        [HarmonyPatch("GetInteractableList")]
        [HarmonyPostfix]
        public static void FixMerchantGroupList(InteractableBase __instance, ref IList __result)
        {
            if (__instance.name != "PerkWeaponShop") return;
            if (__instance.transform.root.name != "PileErico_Cloned_Merchant") return;

            var groupListField = typeof(InteractableBase).GetField("otherInterablesInGroup", BindingFlags.NonPublic | BindingFlags.Instance);
            if (groupListField == null) return;
            var groupList = groupListField.GetValue(__instance) as IList;
            if (groupList == null) return;

            var skillInvoker = __instance.GetComponentInChildren<PerkTreeUIInvoker>(true);
            if (skillInvoker == null) return;

            if (skillInvoker.gameObject.layer != 2) skillInvoker.gameObject.layer = 2; 
            
            bool hasSelf = false;
            bool hasSkill = false;
            foreach (var item in groupList)
            {
                // [CS0252 修复] 使用显式转换 (object) 来消除引用比较的歧义警告
                if ((object)item == (object)__instance) hasSelf = true;
                if ((object)item == (object)skillInvoker) hasSkill = true;
            }

            if (!hasSelf)
            {
                if (groupList.Count > 0) groupList.Insert(0, __instance);
                else groupList.Add(__instance);
            }
            if (!hasSkill)
            {
                groupList.Add(skillInvoker);
            }

            var flagField = typeof(InteractableBase).GetField("isGroup", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (flagField != null) flagField.SetValue(__instance, true);
        }
    }

    [HarmonyPatch(typeof(CA_Dash))] 
    public static class RollSkillPatches
    {
        private static float _originalStaminaCost = -1f;
        private static float _originalCoolTime = -1f; 
        private static CharacterMainControl? GetCharacter(object instance) { var field = instance.GetType().GetField("characterController", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (field != null) return field.GetValue(instance) as CharacterMainControl; return null; }

        [HarmonyPatch("OnStart")] [HarmonyPrefix]
        public static void OnDashStart(CA_Dash __instance) {
            var character = GetCharacter(__instance); if (character == null || character != CharacterMainControl.Main) return;
            bool hasSkill1 = IsPerkUnlocked(SkillTreeManager.SkillID_1);
            bool hasSkill2 = IsPerkUnlocked(SkillTreeManager.SkillID_2);
            if (hasSkill1) { var costField = typeof(CA_Dash).GetField("staminaCost", BindingFlags.Public | BindingFlags.Instance); if (costField != null) { float current = (float)costField.GetValue(__instance); if (_originalStaminaCost < 0) _originalStaminaCost = current; costField.SetValue(__instance, _originalStaminaCost * 0.75f); } }
            if (hasSkill2) { var coolField = typeof(CA_Dash).GetField("coolTime", BindingFlags.Public | BindingFlags.Instance); if (coolField != null) { float current = (float)coolField.GetValue(__instance); if (_originalCoolTime < 0) _originalCoolTime = current; coolField.SetValue(__instance, _originalCoolTime * 0.5f); } }
        }
        [HarmonyPatch("OnStop")] [HarmonyPostfix]
        public static void OnDashStop(CA_Dash __instance) { var character = GetCharacter(__instance); if (character == null || character != CharacterMainControl.Main) return; if (IsPerkUnlocked(SkillTreeManager.SkillID_3)) { character.StartCoroutine(ApplyDamageBuff(character)); } }
        private static bool IsPerkUnlocked(string perkID) { try { var method = typeof(PerkTreeManager).GetMethod("IsPerkUnlocked", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null); if (method != null) return (bool)method.Invoke(null, new object[] { perkID }); } catch { } return false; }
        private static IEnumerator ApplyDamageBuff(CharacterMainControl player) { string statName = "Damage"; float buffValue = 0.20f; var stats = player.CharacterItem.Stats; Stat statObj = stats.GetStat(statName); if (statObj != null) { Modifier dmgBuff = new Modifier(ModifierType.PercentageMultiply, buffValue, "RollSkillBuff"); statObj.AddModifier(dmgBuff); yield return new WaitForSeconds(3.0f); statObj.RemoveModifier(dmgBuff); } }
    }
}