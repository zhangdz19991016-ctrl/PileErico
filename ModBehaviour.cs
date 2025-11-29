﻿using System;
using System.Collections;
using System.IO; 
using System.Reflection; 
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement; 
using ItemStatsSystem; 
using HarmonyLib; // 确保添加 Harmony 引用

namespace PileErico
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // =========================================================
        // 1. 配置与状态
        // =========================================================
        [Serializable]
        public class ModuleConfig
        {
            public bool EnableBossHealthBar = true; 
            public bool EnableInvasion = true;
            public bool EnableSoulsLikeUI = true; 
            public bool EnableSlotExpandedManager = true; 
        }

        public static ModuleConfig Config = new ModuleConfig();
        public static bool isActivated = false; 
        public static string? logPath;

        // =========================================================
        // 2. 模块引用
        // =========================================================
        private BossHealthHUDManager? bossHudManager;
        private LootManager? lootManager;
        private ShopManager? shopManager;
        private EstusFlaskManager? estusFlaskManager;
        private InvasionManager? invasionManager;
        private ItemManager? itemManager;
        private UIManager? uiManager;
        private SlotExpandedManager? slotExpandedManager; 

        // =========================================================
        // 3. Mod 生命周期
        // =========================================================

        protected override void OnAfterSetup()
        {
            base.OnAfterSetup();
            base.StartCoroutine(this.DelayedInitialization());
        }

        protected override void OnBeforeDeactivate()
        {
            if (!isActivated) return; 
            isActivated = false;
            
            LogToFile(">>> 模组开始停用...");

            // 1. 停用业务模块
            SafeAction("LootManager", () => this.lootManager?.Deactivate());
            SafeAction("ShopManager", () => this.shopManager?.Deactivate());
            SafeAction("EstusFlaskManager", () => this.estusFlaskManager?.Deactivate());
            SafeAction("InvasionManager", () => this.invasionManager?.Deactivate());
            SafeAction("BossHealthHUDManager", () => {
                if (this.bossHudManager != null) Destroy(this.bossHudManager.gameObject); // 销毁 Root
            });
            SafeAction("ItemManager", () => this.itemManager?.Deactivate());

            if (uiManager != null)
            {
                SafeAction("UIManager", () => Destroy(uiManager));
                uiManager = null;
            }

            SafeAction("SlotExpandedManager", () => 
            {
                slotExpandedManager?.Dispose();
                slotExpandedManager = null;
            });

            // 2. 停用核心引擎 (ScanManager)
            SafeAction("ScanManager", () => ScanManager.Dispose());
            
            // 可选：如果支持热重载，这里可以 Unpatch Harmony
            // try { var harmony = new Harmony("com.pileerico.scan"); harmony.UnpatchAll("com.pileerico.scan"); } catch { }

            LogToFile("<<< 模组已停用。");
        }

        private IEnumerator DelayedInitialization()
        {
            yield return new WaitForSeconds(1.0f); 
            
            if (isActivated) yield break;
            isActivated = true;
            
            SetupLogging();
            LogToFile(">>> Mod (全部功能) 开始初始化...");

            LoadModuleConfig();
            string configDir = GetConfigDir();

            // -----------------------------------------------------
            // 0. 核心引擎初始化 (ScanManager + Harmony)
            // -----------------------------------------------------
            try 
            {
                // 确保只 Patch 一次
                if (!Harmony.HasAnyPatches("com.pileerico.scan"))
                {
                    var harmony = new Harmony("com.pileerico.scan");
                    // 扫描 PileErico 程序集中的所有 Patch (包含 CharacterScanPatches)
                    harmony.PatchAll(Assembly.GetExecutingAssembly()); 
                    LogToFile("[Harmony] 补丁应用成功。");
                }
                
                // 初始化扫描器 (捕获场上已有的怪)
                ScanManager.Initialize();
            }
            catch (Exception ex)
            {
                LogErrorToFile($"[致命错误] Harmony/ScanManager 初始化失败: {ex}");
            }

            // -----------------------------------------------------
            // 模块初始化区域
            // -----------------------------------------------------

            // 1. BossHUD (现在它订阅 ScanManager)
            SafeInit("BossHealthHUDManager", () => {
                GameObject hudRoot = new GameObject("BossHealthHUDRoot");
                DontDestroyOnLoad(hudRoot);
                this.bossHudManager = hudRoot.AddComponent<BossHealthHUDManager>();
            });

            // 2. 核心功能
            SafeInit("LootManager", () => {
                // 注意：构造函数不再需要 bossHudManager
                this.lootManager = new LootManager(this, configDir);
                this.lootManager.Initialize();
            });

            SafeInit("ShopManager", () => {
                this.shopManager = new ShopManager(this, configDir);
                this.shopManager.Initialize();
            });

            SafeInit("EstusFlaskManager", () => {
                this.estusFlaskManager = new EstusFlaskManager(this);
                this.estusFlaskManager.Initialize();
            });

            SafeInit("InvasionManager", () => {
                // InvasionManager 现在内部应该使用 ScanManager.ActiveCharacters，
                // 但为了兼容旧代码结构，构造函数保持传递引用即可
                this.invasionManager = new InvasionManager(this, this.bossHudManager!);
                this.invasionManager.Initialize();
            });

            SafeInit("ItemManager", () => {
                this.itemManager = new ItemManager();
                this.itemManager.Initialize();
            });

            // 3. UI 模块
            if (Config.EnableSoulsLikeUI)
            {
                SafeInit("UIManager", () => {
                    if (uiManager == null) uiManager = this.gameObject.AddComponent<UIManager>();
                });
            }
            else LogToFile("UIManager 已跳过 (配置禁用)");

            // 4. 槽位扩展模块
            if (Config.EnableSlotExpandedManager)
            {
                SafeInit("SlotExpandedManager", () => {
                    if (slotExpandedManager == null)
                    {
                        slotExpandedManager = new SlotExpandedManager();
                        slotExpandedManager.Initialize();
                    }
                });
            }
            else LogToFile("SlotExpandedManager 已跳过 (配置禁用)");

            LogToFile("<<< Mod (全部功能) 初始化完成。");
        }

        // =========================================================
        // 4. 核心工具方法
        // =========================================================

        private void SafeInit(string moduleName, Action initLogic)
        {
            try
            {
                initLogic.Invoke();
                LogToFile($"[{moduleName}] 初始化成功。");
            }
            catch (Exception ex)
            {
                LogErrorToFile($"[{moduleName}] 初始化失败: {ex.Message}\nStack: {ex.StackTrace}");
            }
        }

        private void SafeAction(string moduleName, Action action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                LogErrorToFile($"[{moduleName}] 执行操作/清理时出错: {ex.Message}");
            }
        }

        // =========================================================
        // 5. 基础功能 (日志与配置)
        // =========================================================

        private string GetConfigDir()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Configs");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
            catch { return string.Empty; }
        }

        private void SetupLogging()
        {
            if (!Application.isPlaying) return;
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string logDir = Path.Combine(Directory.GetParent(dllDir)?.FullName ?? dllDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                
                logPath = Path.Combine(logDir, "PileErico.log");
                File.WriteAllText(logPath, $"--- [PileErico] Log Start: {DateTime.Now} ---\n");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PileErico] Log path setup failed: {ex.Message}");
            }
        }

        private void LoadModuleConfig()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(dllPath) ?? "";
                string configPath = Path.Combine(dir, "ModuleEnabled.json");

                Config = new ModuleConfig(); 

                if (!File.Exists(configPath))
                {
                    string json = JsonUtility.ToJson(Config, true);
                    File.WriteAllText(configPath, json);
                    LogToFile($"已生成默认配置文件: {configPath}");
                }
                else
                {
                    string json = File.ReadAllText(configPath);
                    JsonUtility.FromJsonOverwrite(json, Config);
                    File.WriteAllText(configPath, JsonUtility.ToJson(Config, true));
                    LogToFile($"已加载配置文件: {configPath}");
                }
            }
            catch (Exception ex)
            {
                LogErrorToFile($"加载配置文件失败: {ex.Message}");
                Config = new ModuleConfig(); 
            }
        }

        public static void LogToFile(string message)
        {
            string logMessage = $"[INFO] {DateTime.Now:T}: {message}";
            Debug.Log("[PileErico] " + logMessage);
            WriteLog(logMessage);
        }

        public static void LogWarningToFile(string message)
        {
            string logMessage = $"[WARN] {DateTime.Now:T}: {message}";
            Debug.LogWarning("[PileErico] " + logMessage);
            WriteLog(logMessage);
        }

        public static void LogErrorToFile(string message)
        {
            string logMessage = $"[ERROR] {DateTime.Now:T}: {message}";
            Debug.LogError("[PileErico] " + logMessage);
            WriteLog(logMessage);
        }
        
        private static void WriteLog(string message)
        {
            if (string.IsNullOrEmpty(logPath)) return;
            try
            {
                File.AppendAllText(logPath, message + "\n");
            }
            catch {}
        }
    }
}