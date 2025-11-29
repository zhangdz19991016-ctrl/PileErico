﻿using System;
using System.Collections;
using System.IO; 
using System.Reflection; 
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement; 
using ItemStatsSystem; 
using HarmonyLib; 

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
            public bool EnableNPCSystem = true; 
        }

        public static ModuleConfig Config = new ModuleConfig();
        public static bool isActivated = false; 
        public static string? logPath;

        // =========================================================
        // 2. 模块引用
        // =========================================================
        private BossHealthHUDManager? bossHudManager;
        private LootManager? lootManager;
        private NPCManager? npcManager;
        private SkillTreeManager? skillTreeManager;
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

            SafeAction("SlotExpandedManager", () => { slotExpandedManager?.Dispose(); slotExpandedManager = null; });
            
            if (uiManager != null) { SafeAction("UIManager", () => Destroy(uiManager)); uiManager = null; }

            SafeAction("ItemManager", () => this.itemManager?.Deactivate());
            SafeAction("InvasionManager", () => this.invasionManager?.Deactivate());
            SafeAction("EstusFlaskManager", () => this.estusFlaskManager?.Deactivate());
            SafeAction("NPCManager", () => this.npcManager?.Deactivate());
            SafeAction("SkillTreeManager", () => this.skillTreeManager?.Deactivate());
            SafeAction("LootManager", () => this.lootManager?.Deactivate());

            // [修复] 使用反射调用 Deactivate 以规避编译错误
            SafeAction("BossHealthHUDManager", () => {
                if (this.bossHudManager != null) {
                    // 尝试反射调用 Deactivate
                    var method = this.bossHudManager.GetType().GetMethod("Deactivate");
                    if (method != null)
                    {
                        method.Invoke(this.bossHudManager, null);
                    }
                    else
                    {
                        // 备用方案：直接销毁
                        if (this.bossHudManager.gameObject != null) Destroy(this.bossHudManager.gameObject);
                    }
                    this.bossHudManager = null;
                }
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
            // 模块初始化区域
            // -----------------------------------------------------

            SafeInit("BossHealthHUDManager", () => {
                GameObject hudRoot = new GameObject("BossHealthHUDRoot");
                DontDestroyOnLoad(hudRoot);
                this.bossHudManager = hudRoot.AddComponent<BossHealthHUDManager>();
            });

            SafeInit("LootManager", () => {
                this.lootManager = new LootManager(this, configDir);
                this.lootManager.Initialize();
            });

            if (Config.EnableNPCSystem)
            {
                SafeInit("SkillTreeManager", () => {
                    this.skillTreeManager = new SkillTreeManager(this, configDir);
                    this.skillTreeManager.Initialize();
                });

                SafeInit("NPCManager", () => {
                    this.npcManager = new NPCManager(this, configDir);
                    this.npcManager.Initialize();
                });
            }
            else LogToFile("NPC系统已跳过 (配置禁用)");

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

            if (Config.EnableSoulsLikeUI)
            {
                SafeInit("UIManager", () => {
                    if (uiManager == null) uiManager = this.gameObject.AddComponent<UIManager>();
                });
            }
            else LogToFile("UIManager 已跳过 (配置禁用)");

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
            try { action.Invoke(); }
            catch (Exception ex) { LogErrorToFile($"[{moduleName}] 清理出错: {ex.Message}"); }
        }

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
            catch (Exception ex) { Debug.LogError($"[PileErico] Log setup failed: {ex.Message}"); }
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
                    File.WriteAllText(configPath, JsonUtility.ToJson(Config, true));
                }
                else
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(configPath), Config);
                    File.WriteAllText(configPath, JsonUtility.ToJson(Config, true)); 
                }
                LogToFile($"已加载配置文件: {configPath}");
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
            try { File.AppendAllText(logPath, message + "\n"); } catch {}
        }
        #endregion
    }
}