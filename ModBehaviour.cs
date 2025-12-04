﻿using System;
using System.Collections;
using System.IO; 
using System.Reflection; 
using UnityEngine;
using HarmonyLib; 

namespace PileErico
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // =========================================================
        // 1. 状态与日志
        // =========================================================
        
        public static bool isActivated = false; 
        public static string? logPath;
        
        private static readonly object _logLock = new object();
        private Harmony? _harmonyInstance;

        // =========================================================
        // 2. 模块引用
        // =========================================================
        private BossHealthHUDManager? bossHudManager;
        private BossManager? bossManager; 
        public InvasionManager? InvasionManager { get; private set; }
        private LootManager? lootManager;
        private NPCManager? npcManager;
        private SkillTreeManager? skillTreeManager;
        private EstusFlaskManager? estusFlaskManager;
        private ItemManager? itemManager;
        private UIManager? uiManager;
        private SlotExpandedManager? slotExpandedManager;

        // [新增] 引用设置管理器组件 (UI部分)
        private SettingManager? settingManager;

        // =========================================================
        // 3. Mod 生命周期
        // =========================================================

        protected override void OnAfterSetup()
        {
            base.OnAfterSetup();
            
            SetupLogging();
            LogToFile(">>> Mod 核心加载 (OnAfterSetup)...");

            // [新增] 挂载 SettingManager (包含UI和配置功能)
            // 这一步非常重要，它负责显示 Ctrl+F12 的菜单
            this.settingManager = this.gameObject.AddComponent<SettingManager>();
            LogToFile("[System] 设置管理器 UI 已加载 (快捷键 Ctrl+F12)。");

            // 1. 激活 Harmony 补丁
            try 
            {
                if (Harmony.HasAnyPatches("com.PileErico.Mod"))
                {
                    var h = new Harmony("com.PileErico.Mod");
                    h.UnpatchAll("com.PileErico.Mod");
                }
                _harmonyInstance = new Harmony("com.PileErico.Mod");
                _harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                LogToFile("[System] Harmony 补丁已应用。");
            }
            catch (Exception ex)
            {
                LogErrorToFile($"[System] Harmony 补丁应用失败: {ex}");
            }

            // 2. 初始化 ScanManager (核心依赖)
            ScanManager.Initialize();

            // 技能树初始化 - [修改] 改为读取 SettingManager
            if (SettingManager.EnableSkillTree)
            {
                SafeInit("SkillTreeManager", () => {
                    this.skillTreeManager = new SkillTreeManager(this, GetConfigDir());
                    this.skillTreeManager.Initialize();
                });
            }
            else
            {
                LogToFile("[Config] SkillTreeManager 已根据配置禁用。");
            }

            // 3. 其他非核心模块延迟加载
            base.StartCoroutine(this.DelayedInitialization());
        }

        protected override void OnBeforeDeactivate()
        {
            if (!isActivated) return; 
            isActivated = false;
            
            LogToFile(">>> 模组开始停用...");

            // [新增] 销毁 SettingManager 组件 (关闭 UI)
            if (this.settingManager != null) Destroy(this.settingManager);

            if (_harmonyInstance != null)
            {
                try { _harmonyInstance.UnpatchAll("com.PileErico.Mod"); } catch {}
                _harmonyInstance = null;
            }
            
            ScanManager.Dispose();

            SafeAction("SlotExpandedManager", () => { slotExpandedManager?.Dispose(); slotExpandedManager = null; });
            
            if (uiManager != null) { SafeAction("UIManager", () => Destroy(uiManager)); uiManager = null; }

            SafeAction("ItemManager", () => this.itemManager?.Deactivate());
            SafeAction("InvasionManager", () => this.InvasionManager?.Deactivate());
            SafeAction("BossManager", () => this.bossManager?.Deactivate());
            
            SafeAction("EstusFlaskManager", () => this.estusFlaskManager?.Deactivate());
            SafeAction("NPCManager", () => this.npcManager?.Deactivate());
            SafeAction("SkillTreeManager", () => this.skillTreeManager?.Deactivate());
            SafeAction("LootManager", () => this.lootManager?.Deactivate());

            SafeAction("BossHealthHUDManager", () => {
                if (this.bossHudManager != null) {
                    if (this.bossHudManager.gameObject != null) Destroy(this.bossHudManager.gameObject);
                    this.bossHudManager = null;
                }
            });

            // 清理 Boss 音乐管理器
            SafeAction("BossMusicManager", () => {
                if (BossMusicManager.Instance != null) 
                    Destroy(BossMusicManager.Instance.gameObject);
            });

            LogToFile("<<< 模组已停用。");
        }

        private IEnumerator DelayedInitialization()
        {
            // 延迟 1 秒
            yield return new WaitForSeconds(1.0f); 
            
            if (isActivated) yield break;
            isActivated = true;
            
            LogToFile(">>> Mod (其余功能) 延迟初始化开始...");

            string configDir = GetConfigDir();

            // -----------------------------------------------------
            // 模块初始化区域 - [修改] 全部使用 SettingManager 读取配置
            // -----------------------------------------------------

            // 1. Boss 血条
            if (SettingManager.EnableBossHealthBar)
            {
                SafeInit("BossHealthHUDManager", () => {
                    GameObject hudRoot = new GameObject("BossHealthHUDRoot");
                    DontDestroyOnLoad(hudRoot);
                    this.bossHudManager = hudRoot.AddComponent<BossHealthHUDManager>();
                });
            }

            // 2. Boss 强化
            if (SettingManager.EnableBossEnhancement)
            {
                SafeInit("BossManager", () => {
                    this.bossManager = new BossManager(this);
                    this.bossManager.Initialize();
                });
            }

            // 3. Boss 音乐管理器 (带开关检查)
            if (SettingManager.EnableBossMusic)
            {
                SafeInit("BossMusicManager", () => {
                    if (BossMusicManager.Instance == null)
                    {
                        GameObject musicGo = new GameObject("PileErico_BossMusicManager");
                        DontDestroyOnLoad(musicGo);
                        musicGo.AddComponent<BossMusicManager>();
                    }
                });
            }
            else
            {
                LogToFile("[Config] BossMusicManager 已根据配置禁用。");
            }

            // 4. 掉落管理
            SafeInit("LootManager", () => {
                this.lootManager = new LootManager(this, configDir);
                this.lootManager.Initialize();
            });

            // 5. 商人
            SafeInit("NPCManager", () => {
                this.npcManager = new NPCManager(this, configDir);
                this.npcManager.Initialize();
            });

            // 6. 元素瓶
            SafeInit("EstusFlaskManager", () => {
                this.estusFlaskManager = new EstusFlaskManager(this);
                this.estusFlaskManager.Initialize();
            });

            // 7. 入侵
            if (SettingManager.EnableInvasion)
            {
                SafeInit("InvasionManager", () => {
                    this.InvasionManager = new InvasionManager(this);
                    this.InvasionManager.Initialize();
                });
            }

            // 8. 物品管理
            SafeInit("ItemManager", () => {
                this.itemManager = new ItemManager();
                this.itemManager.Initialize();
            });

            // 9. UI
            if (SettingManager.EnableSoulsLikeUI)
            {
                SafeInit("UIManager", () => {
                    if (uiManager == null) uiManager = this.gameObject.AddComponent<UIManager>();
                });
            }

            // 10. 背包扩展
            if (SettingManager.EnableSlotExpandedManager)
            {
                SafeInit("SlotExpandedManager", () => {
                    if (slotExpandedManager == null)
                    {
                        slotExpandedManager = new SlotExpandedManager();
                        slotExpandedManager.Initialize();
                    }
                });
            }

            LogToFile("<<< Mod (全部功能) 初始化完成。");
        }

        // =========================================================
        // 4. 辅助方法
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
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string logDir = Path.Combine(dllDir, "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                
                logPath = Path.Combine(logDir, "PileErico.log");
                
                lock (_logLock)
                {
                    File.WriteAllText(logPath, $"--- [PileErico] Log Start: {DateTime.Now} ---\n");
                }
            }
            catch (Exception ex) { Debug.LogError($"[PileErico] Log setup failed: {ex.Message}"); }
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
            lock (_logLock)
            {
                try { File.AppendAllText(logPath, message + "\n"); } 
                catch {}
            }
        }
        #endregion
    }
}