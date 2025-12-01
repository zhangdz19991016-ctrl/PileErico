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
                // 1. 本地化 (饱和式注册)
                try 
                {
                    LocalizationManager.SetOverrideText($"Tag_{tagName}", tagName);
                    LocalizationManager.SetOverrideText($"tag_{tagName}", tagName);
                    LocalizationManager.SetOverrideText(tagName, tagName);
                }
                catch (Exception) {}

                // 2. [核心修复] 安静地检查 Tag 是否已存在
                // 不再调用会报错的 TagUtilities.TagFromString
                bool exists = false;
                foreach (var t in rawTagList)
                {
                    // 只要名字对上就算存在
                    if (t.name == tagName) 
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists) continue; // 存在则跳过

                // 3. 创建新 Tag
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
            // 注意：此时 Tag 肯定已经注册好了，所以调用这个方法不会报错
            // 如果这里还报错，说明注册步骤真的失败了，那是需要知道的真错误
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