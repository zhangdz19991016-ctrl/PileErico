using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items; 
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PileErico
{
    public class ShopManager
    {
        private ShopConfig shopConfig = null!;
        private readonly ModBehaviour modBehaviour;
        private readonly string configDir;

        public ShopManager(ModBehaviour modBehaviour, string configDir)
        {
            this.modBehaviour = modBehaviour;
            this.configDir = configDir;
        }

        public void Initialize()
        {
            ModBehaviour.LogToFile("[ShopManager] 正在初始化...");
            LoadShopConfig(configDir);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void Deactivate()
        {
            ModBehaviour.LogToFile("[ShopManager] 正在停用...");
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void LoadShopConfig(string configDir)
        {
            string path = Path.Combine(configDir, "ShopConfig.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    this.shopConfig = JsonConvert.DeserializeObject<ShopConfig>(json)!;
                }
                catch (Exception ex)
                {
                    ModBehaviour.LogErrorToFile("[ShopManager] 配置加载失败: " + ex.Message);
                    this.shopConfig = new ShopConfig();
                }
            }
            else
            {
                this.shopConfig = new ShopConfig();
                this.shopConfig.ItemsToSell.Add(new ShopItemEntry { ItemID = 2025000005, MaxStock = 10, PriceFactor = 1.0f, Possibility = 1.0f });
                try { File.WriteAllText(path, JsonConvert.SerializeObject(this.shopConfig, Formatting.Indented)); } catch { }
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Base_SceneV2")
            {
                if (this.shopConfig == null) return;
                modBehaviour.StartCoroutine(SetupClonedMerchant());
            }
        }

        private IEnumerator SetupClonedMerchant()
        {
            yield return new WaitForSeconds(1f);

            var originalSaleMachine = GameObject.Find("Buildings/SaleMachine");
            if (originalSaleMachine == null) yield break;

            ModBehaviour.LogToFile("[ShopManager] 开始创建克隆商人...");
            
            var myMerchantClone = GameObject.Instantiate(originalSaleMachine);
            myMerchantClone.name = "PileErico_Cloned_Merchant";
            myMerchantClone.transform.SetParent(originalSaleMachine.transform.parent, true);
            myMerchantClone.transform.position = shopConfig.GetPosition();
            
            myMerchantClone.SetActive(false);

            foreach (Transform child in myMerchantClone.transform)
            {
                if (child.name == "Visual") child.gameObject.SetActive(false);
            }

            LoadMerchantModel(myMerchantClone.transform).Forget();

            var stockShopTransform = myMerchantClone.transform.Find("PerkWeaponShop");
            if (stockShopTransform == null)
            {
                ModBehaviour.LogErrorToFile("[ShopManager] 找不到 PerkWeaponShop，克隆失败。");
                GameObject.Destroy(myMerchantClone);
                yield break;
            }

            var stockShop = stockShopTransform.GetComponent<StockShop>();
            if (stockShop != null)
            {
                ConfigureClonedShop(stockShop);
            }

            myMerchantClone.SetActive(true);
            ModBehaviour.LogToFile($"[ShopManager] 商人已激活。");

            // 此处原有的 SkillTreeManager 调用代码已删除
        }

        async UniTask LoadMerchantModel(Transform parentMachine)
        {
            try
            {
                CharacterRandomPreset? characterRandomPreset = GetCharacterPreset(shopConfig.MerchantPresetName);
                if (characterRandomPreset == null) return;

                Vector3 position = shopConfig.GetPosition();
                Vector3 faceTo = shopConfig.GetFacing();

                var merchantCharacter = await characterRandomPreset!.CreateCharacterAsync(position, faceTo,
                    MultiSceneCore.MainScene!.Value.buildIndex, (CharacterSpawnerGroup)null!, false);
                
                if (merchantCharacter == null) return;

                merchantCharacter.transform.SetParent(parentMachine, true);
                merchantCharacter.transform.localPosition = Vector3.zero;
                merchantCharacter.transform.rotation = Quaternion.LookRotation(faceTo - position);

                var aiChild = merchantCharacter.transform.Find(shopConfig.MerchantPresetName.Replace("EnemyPreset_", "AIController_") + "(Clone)");
                if (aiChild != null) GameObject.Destroy(aiChild.gameObject);

                if (merchantCharacter.GetComponent<CharacterController>()) GameObject.Destroy(merchantCharacter.GetComponent<CharacterController>());
                if (merchantCharacter.GetComponent<Health>()) GameObject.Destroy(merchantCharacter.GetComponent<Health>());
                if (merchantCharacter.GetComponent<Movement>()) GameObject.Destroy(merchantCharacter.GetComponent<Movement>());
                if (merchantCharacter.GetComponent<CharacterItemControl>()) GameObject.Destroy(merchantCharacter.GetComponent<CharacterItemControl>());

                var merchantChild = GetSpecialMerchantChild(merchantCharacter.transform);
                if (merchantChild != null) 
                {
                    GameObject.Destroy(merchantChild.gameObject);
                }
                
                foreach (var col in merchantCharacter.GetComponentsInChildren<Collider>()) GameObject.Destroy(col);
            }
            catch (Exception ex) { ModBehaviour.LogErrorToFile($"[ShopManager] 加载模型出错: {ex.Message}"); }
        }

        private void ConfigureClonedShop(StockShop stockShop)
        {
            try
            {
                var merchantIDField = typeof(StockShop).GetField("merchantID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (merchantIDField != null) merchantIDField.SetValue(stockShop, "PileErico_Merchant_ID");
            }
            catch {}
            
            stockShop.entries.Clear();
            
            if (shopConfig.ItemsToSell != null)
            {
                foreach (var itemEntry in shopConfig.ItemsToSell)
                {
                    var newShopItem = new StockShopDatabase.ItemEntry
                    {
                        typeID = itemEntry.ItemID,
                        maxStock = itemEntry.MaxStock,
                        priceFactor = itemEntry.PriceFactor,
                        possibility = itemEntry.Possibility,
                        forceUnlock = true 
                    };
                    stockShop.entries.Add(new StockShop.Entry(newShopItem));
                }
            }
            RefreshShop(stockShop);
        }

        CharacterRandomPreset? GetCharacterPreset(string characterPresetName)
        {
            foreach (var characterRandomPreset in GameplayDataSettings.CharacterRandomPresetData.presets)
            {
                if (characterPresetName == characterRandomPreset.name) return characterRandomPreset;
            }
            return null;
        }

        Transform? GetSpecialMerchantChild(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (child.name.StartsWith("SpecialAttachment_Merchant_")) return child;
            }
            return null;
        }

        public static void RefreshShop(StockShop stockShop)
        {
            if (stockShop == null) return;
            var refreshMethod = typeof(StockShop).GetMethod("DoRefreshStock", BindingFlags.NonPublic | BindingFlags.Instance);
            if (refreshMethod != null) refreshMethod.Invoke(stockShop, null);

            var lastTimeField = typeof(StockShop).GetField("lastTimeRefreshedStock", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lastTimeField != null) lastTimeField.SetValue(stockShop, DateTime.UtcNow.ToBinary());
        }
    }
}