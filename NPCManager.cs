#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.PerkTrees.Interactable;
using Duckov.UI; 
using Duckov.Utilities; 
using ItemStatsSystem;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events; 
using UnityEngine.SceneManagement;
using SodaCraft.Localizations; // [新增] 引用本地化命名空间

namespace PileErico
{
    public class NPCManager
    {
        // =========================================================
        // 1. 配置类
        // =========================================================
        [Serializable]
        public class ShopConfig
        {
            public bool EnableMerchant = true;
            public string MerchantPresetName = "EnemyPreset_Merchant_Myst";
            public float PositionX = 0f;
            public float PositionY = 1.5f;
            public float PositionZ = -52.5f;
            public Vector3 SpawnFacing = new Vector3(0f, 1.5f, -50f);
            public List<ShopItemEntry> ItemsToSell = new List<ShopItemEntry>();
            public Vector3 GetSpawnPosition() => new Vector3(PositionX, PositionY, PositionZ);
        }

        [Serializable]
        public class ShopItemEntry
        {
            public int ItemID;
            public int MaxStock = 1;
            public float PriceFactor = 1.0f;
            public float Possibility = 1.0f;
        }

        // =========================================================
        // 2. 自定义交互组件
        // =========================================================
        
        public class ShopInteractable : InteractableBase
        {
            private StockShop? _shop;
            
            protected override void Start() 
            { 
                base.Start(); 
                // [修改] 使用自定义 Key，而不是直接写中文
                this.InteractName = "PE_Interaction_Trade"; 
                _shop = GetComponent<StockShop>(); 
            }

            protected override void OnInteractStart(CharacterMainControl interactCharacter)
            {
                if (_shop == null) _shop = GetComponent<StockShop>();
                _shop?.ShowUI();
                base.StopInteract();
            }
        }

        public class SkillInteractable : InteractableBase
        {
            protected override void Start() 
            { 
                base.Start(); 
                // [修改] 使用自定义 Key
                this.InteractName = "PE_Interaction_SoulAscend"; 
            }

            protected override void OnInteractStart(CharacterMainControl interactCharacter)
            {
                if (SkillTreeManager.CustomRollTree != null) PerkTreeView.Show(SkillTreeManager.CustomRollTree);
                else ModBehaviour.LogErrorToFile("[NPCManager] 技能树实例丢失！");
                base.StopInteract();
            }
        }

        // =========================================================
        // 3. 管理器逻辑
        // =========================================================
        private readonly ModBehaviour _mod;
        private readonly string _configDir;
        private ShopConfig? _config;
        public GameObject? NPCInstance { get; private set; }

        public NPCManager(ModBehaviour mod, string configDir)
        {
            _mod = mod;
            _configDir = configDir;
        }

        public void Initialize()
        {
            LoadConfig();
            
            // [新增] 注册自定义本地化文本
            // 格式：SetOverrideText(Key, 显示内容)
            LocalizationManager.SetOverrideText("PE_Interaction_Trade", "交易");
            LocalizationManager.SetOverrideText("PE_Interaction_SoulAscend", "灵魂升华");
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            ModBehaviour.LogToFile("[NPCManager] 初始化完成。");
        }

        public void Deactivate()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            // [新增] 清理本地化文本
            LocalizationManager.RemoveOverrideText("PE_Interaction_Trade");
            LocalizationManager.RemoveOverrideText("PE_Interaction_SoulAscend");
            
            if (NPCInstance != null) UnityEngine.Object.Destroy(NPCInstance);
        }

        private void LoadConfig()
        {
            string path = Path.Combine(_configDir, "ShopConfig.json"); 
            if (!File.Exists(path))
            {
                _config = new ShopConfig();
                _config.ItemsToSell.Add(new ShopItemEntry { ItemID = 2025000005, MaxStock = 10 }); 
                try { File.WriteAllText(path, JsonConvert.SerializeObject(_config, Formatting.Indented)); } catch { }
            }
            else
            {
                try { _config = JsonConvert.DeserializeObject<ShopConfig>(File.ReadAllText(path)); }
                catch (Exception ex) { ModBehaviour.LogErrorToFile($"[NPCManager] 配置错误: {ex.Message}"); }
                if (_config == null) _config = new ShopConfig();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_config != null && scene.name == "Base_SceneV2" && _config.EnableMerchant)
            {
                SpawnNPCAsync().Forget();
            }
        }

        private async UniTaskVoid SpawnNPCAsync()
        {
            await UniTask.Delay(1000); 
            if (NPCInstance != null) return;
            if (_config == null) return;

            try
            {
                var preset = GetCharacterPreset(_config.MerchantPresetName);
                if (preset == null) return;

                CharacterMainControl npc = await preset.CreateCharacterAsync(
                    _config.GetSpawnPosition(), 
                    _config.SpawnFacing, 
                    SceneManager.GetActiveScene().buildIndex, 
                    null, 
                    false
                );

                if (npc == null) return;

                NPCInstance = npc.gameObject;
                NPCInstance.name = "PileErico_Merchant_Hub";

                DeepCleanNPC(NPCInstance);
                EnsureInteractableLayer(NPCInstance);

                SetupShop(NPCInstance);
                SetupSkillTree(NPCInstance);
                SetupInteractionGroup(NPCInstance);

                ModBehaviour.LogToFile("[NPCManager] 商人生成并组装完毕。");
            }
            catch (Exception ex) { ModBehaviour.LogErrorToFile($"[NPCManager] 生成异常: {ex}"); }
        }

        private void DeepCleanNPC(GameObject go)
        {
            DestroyImmediateIfExist<UnityEngine.AI.NavMeshAgent>(go);
            DestroyImmediateIfExist<CharacterMainControl>(go);
            DestroyImmediateIfExist<Rigidbody>(go);
            foreach (var s in go.GetComponentsInChildren<StockShop>(true)) UnityEngine.Object.DestroyImmediate(s);
            foreach (var p in go.GetComponentsInChildren<PerkTreeUIInvoker>(true)) UnityEngine.Object.DestroyImmediate(p);
            foreach (var i in go.GetComponentsInChildren<InteractableBase>(true)) UnityEngine.Object.DestroyImmediate(i);
        }

        private void EnsureInteractableLayer(GameObject go)
        {
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true; 
            else 
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.height = 2f; cap.radius = 0.5f; cap.center = new Vector3(0, 1f, 0);
            }
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer != -1) go.layer = layer;
        }

        private void DestroyImmediateIfExist<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp != null) UnityEngine.Object.DestroyImmediate(comp);
        }

        // --- A: 商店模块 ---
        private void SetupShop(GameObject host)
        {
            var shop = host.AddComponent<StockShop>();
            SetPrivateField(shop, "merchantID", "PileErico_Hub_Merchant");
            
            SetPrivateField(shop, "accountAvaliable", true); 
            SetPrivateField(shop, "refreshAfterTimeSpan", TimeSpan.FromHours(24).Ticks);

            shop.entries.Clear();
            if (_config != null && _config.ItemsToSell != null)
            {
                foreach (var itemConfig in _config.ItemsToSell)
                {
                    var entry = new StockShopDatabase.ItemEntry
                    {
                        typeID = itemConfig.ItemID, maxStock = itemConfig.MaxStock,
                        priceFactor = itemConfig.PriceFactor, possibility = itemConfig.Possibility,
                        forceUnlock = true
                    };
                    shop.entries.Add(new StockShop.Entry(entry));
                }
            }
            var refreshMethod = typeof(StockShop).GetMethod("DoRefreshStock", BindingFlags.NonPublic | BindingFlags.Instance);
            refreshMethod?.Invoke(shop, null);

            var interact = host.AddComponent<ShopInteractable>();
            interact.interactMarkerOffset = new Vector3(0, 0.65f, 0); 
            
            // [关键] 赋值 Key
            interact.InteractName = "PE_Interaction_Trade"; 

            var collider = host.GetComponent<Collider>();
            if (collider != null) interact.interactCollider = collider;
        }

        // --- B: 技能树模块 ---
        private void SetupSkillTree(GameObject host)
        {
            var invoker = host.AddComponent<SkillInteractable>();
            invoker.MarkerActive = false; 
            invoker.interactMarkerOffset = new Vector3(0, 0.65f, 0);
            
            // [关键] 赋值 Key
            invoker.InteractName = "PE_Interaction_SoulAscend";

            var collider = host.GetComponent<Collider>();
            if (collider != null) invoker.interactCollider = collider;
        }

        private void SetupInteractionGroup(GameObject host)
        {
            var allInteractables = host.GetComponents<InteractableBase>();
            if (allInteractables.Length < 2) return;

            foreach (var current in allInteractables)
            {
                current.interactableGroup = true;
                var listField = typeof(InteractableBase).GetField("otherInterablesInGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField != null)
                {
                    var list = listField.GetValue(current) as System.Collections.IList;
                    if (list == null)
                    {
                        var listType = typeof(List<>).MakeGenericType(typeof(InteractableBase));
                        list = Activator.CreateInstance(listType) as System.Collections.IList;
                        listField.SetValue(current, list);
                    }
                    if (list != null)
                    {
                        list.Clear();
                        foreach (var other in allInteractables) if (current != other) list.Add(other);
                    }
                }
            }
        }

        private CharacterRandomPreset? GetCharacterPreset(string name)
        {
            if (GameplayDataSettings.CharacterRandomPresetData == null) return null;
            foreach (var p in GameplayDataSettings.CharacterRandomPresetData.presets)
                if (p.name == name) return p;
            return null;
        }

        private void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            field?.SetValue(target, value);
        }
    }
}