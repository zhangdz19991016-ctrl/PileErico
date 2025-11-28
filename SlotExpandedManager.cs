using System;
using System.Collections.Generic;
using System.Reflection; 
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;
using SodaCraft.Localizations; 

namespace PileErico
{
    public class SlotExpandedManager
    {
        // 核心 Tag ID 列表 (中文)
        private readonly List<string> _customTags = new List<string> 
        { 
            "誓约", 
            "戒指", 
            "臂甲", 
            "腿甲" 
        };

        public void Initialize()
        {
            RegisterCustomTags();
            LevelManager.OnAfterLevelInitialized += OnLevelInitialized;
        }

        public void Dispose()
        {
            LevelManager.OnAfterLevelInitialized -= OnLevelInitialized;
        }

        private void RegisterCustomTags()
        {
            if (GameplayDataSettings.Tags == null) return;

            FieldInfo allTagsField = typeof(GameplayDataSettings.TagsData).GetField("allTags", BindingFlags.Instance | BindingFlags.NonPublic);
            if (allTagsField == null) return;

            List<Tag>? rawTagList = allTagsField.GetValue(GameplayDataSettings.Tags) as List<Tag>;
            if (rawTagList == null) return;

            foreach (var tagName in _customTags)
            {
                // [关键修改] 饱和式本地化注册
                // 把所有可能的前缀都注册一遍，彻底杜绝 "Tag_xxx" 或 "*tag_xxx*"
                try 
                {
                    // 1. 针对 "Tag_誓约" (解决您刚才遇到的问题)
                    LocalizationManager.SetOverrideText($"Tag_{tagName}", tagName);

                    // 2. 针对 "tag_誓约" (解决之前的 *tag_誓约*)
                    LocalizationManager.SetOverrideText($"tag_{tagName}", tagName);

                    // 3. 针对 "誓约" (无前缀直查)
                    LocalizationManager.SetOverrideText(tagName, tagName);
                    
                    ModBehaviour.LogToFile($"[SlotExpandedManager] 本地化覆盖完毕: {tagName}");
                }
                catch (Exception) {}

                // Tag 对象注册
                if (TagUtilities.TagFromString(tagName) != null) continue;

                try
                {
                    Tag newTag = ScriptableObject.CreateInstance<Tag>();
                    newTag.name = tagName; 
                    rawTagList.Add(newTag);
                    ModBehaviour.LogToFile($"[SlotExpandedManager] Tag 对象注册: {tagName}");
                }
                catch (Exception e)
                {
                    ModBehaviour.LogErrorToFile($"[SlotExpandedManager] Tag 注册失败: {e.Message}");
                }
            }
        }

        private void OnLevelInitialized()
        {
            AddOrFixExpandedSlots();
        }

        // 包含读档修复逻辑
        private void AddOrFixExpandedSlots()
        {
            if (LevelManager.Instance == null || LevelManager.Instance.MainCharacter == null) return;

            Item characterItem = LevelManager.Instance.MainCharacter.CharacterItem;
            if (characterItem == null || characterItem.Slots == null) return;

            var slotsToEnsure = new List<(string id, string tagName)>
            {
                ("Slot_Covenant", "誓约"),
                ("Slot_Ring1", "戒指"),      
                ("Slot_Ring2", "戒指"),
                ("Slot_Gauntlet", "臂甲"),
                ("Slot_Legging", "腿甲")
            };

            bool anyChangeMade = false;

            foreach (var (id, tagName) in slotsToEnsure)
            {
                Slot slot = characterItem.Slots.GetSlot(id);

                if (slot == null)
                {
                    CreateAndAddSlot(characterItem, id, tagName);
                    anyChangeMade = true;
                }
                else
                {
                    // 修复读档后的空 Tag
                    if (EnsureSlotTags(slot, tagName))
                    {
                        anyChangeMade = true;
                        ModBehaviour.LogToFile($"[SlotExpandedManager] 修复读档槽位: {id}");
                    }
                }
            }

            if (anyChangeMade)
            {
                MethodInfo notifyMethod = typeof(Item).GetMethod("InitiateNotifyItemTreeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                if (notifyMethod != null) notifyMethod.Invoke(characterItem, null);
            }
        }

        private void CreateAndAddSlot(Item targetItem, string slotId, string tagName)
        {
            Slot newSlot = new Slot(slotId);
            EnsureSlotTags(newSlot, tagName); 
            targetItem.Slots.Add(newSlot);
            newSlot.Initialize(targetItem.Slots);
        }

        private bool EnsureSlotTags(Slot slot, string tagName)
        {
            Tag tag = TagUtilities.TagFromString(tagName);
            if (tag == null) return false;

            if (!slot.requireTags.Contains(tag))
            {
                slot.requireTags.Add(tag);
                return true; 
            }
            return false; 
        }
    }
}