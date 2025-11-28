﻿using System;
using System.Collections;
using System.IO; 
using System.Reflection; 
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement; 
using ItemStatsSystem; 

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
            public bool EnableSlotExpandedManager = true; // [新增]
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
        private SlotExpandedManager? slotExpandedManager; // [新增]

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

            // 使用 SafeAction 统一处理销毁逻辑，无需重复写 try-catch
            SafeAction("LootManager", () => this.lootManager?.Deactivate());
            SafeAction("ShopManager", () => this.shopManager?.Deactivate());
            SafeAction("EstusFlaskManager", () => this.estusFlaskManager?.Deactivate());
            SafeAction("InvasionManager", () => this.invasionManager?.Deactivate());
            SafeAction("BossHealthHUDManager", () => this.bossHudManager?.Deactivate());
            SafeAction("ItemManager", () => this.itemManager?.Deactivate());

            // 特殊处理 MonoBehaviour 组件
            if (uiManager != null)
            {
                SafeAction("UIManager", () => Destroy(uiManager));
                uiManager = null;
            }

            // [新增] 清理槽位扩展
            SafeAction("SlotExpandedManager", () => 
            {
                slotExpandedManager?.Dispose();
                slotExpandedManager = null;
            });

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
            // 模块初始化区域 - 无论增加多少模块，都保持这种整洁的格式
            // -----------------------------------------------------

            // 1. BossHUD (依赖项)
            SafeInit("BossHealthHUDManager", () => {
                GameObject hudRoot = new GameObject("BossHealthHUDRoot");
                DontDestroyOnLoad(hudRoot);
                this.bossHudManager = hudRoot.AddComponent<BossHealthHUDManager>();
            });

            // 2. 核心功能
            SafeInit("LootManager", () => {
                this.lootManager = new LootManager(this, configDir, this.bossHudManager);
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
                this.invasionManager = new InvasionManager(this, this.bossHudManager!);
                this.invasionManager.Initialize();
            });

            SafeInit("ItemManager", () => {
                this.itemManager = new ItemManager();
                this.itemManager.Initialize();
            });

            // 3. UI 模块 (受配置控制)
            if (Config.EnableSoulsLikeUI)
            {
                SafeInit("UIManager", () => {
                    if (uiManager == null) uiManager = this.gameObject.AddComponent<UIManager>();
                });
            }
            else LogToFile("UIManager 已跳过 (配置禁用)");

            // 4. [新增] 槽位扩展模块 (受配置控制)
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
        // 4. 核心工具方法 (减少重复代码的关键)
        // =========================================================

        /// <summary>
        /// 安全执行初始化逻辑，自动处理异常和日志
        /// </summary>
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

        /// <summary>
        /// 安全执行清理逻辑
        /// </summary>
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
                string logDir = Path.Combine(Directory.GetParent(dllDir)?.FullName ?? dllDir, "logs"); // 尝试放到上级或同级 logs 目录
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
                    
                    // 回写以同步新字段
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

        #region Static Log Functions
        
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
        
        #endregion
    }
}