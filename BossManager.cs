using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov;

namespace PileErico
{
    /// <summary>
    /// Boss 强化管理器
    /// 负责识别 Boss 并分配强化逻辑
    /// </summary>
    public class BossManager
    {
        private readonly ModBehaviour _mod;
        private readonly HashSet<CharacterMainControl> _processedBosses = new HashSet<CharacterMainControl>();
        
        // 存储所有的强化方案
        private readonly List<BossEnhancer> _enhancers = new List<BossEnhancer>();

        public BossManager(ModBehaviour mod)
        {
            _mod = mod;
            RegisterEnhancers(); // 初始化时注册方案
        }

        private void RegisterEnhancers()
        {
            // ==========================================
            // [在此处注册具体的 Boss 强化脚本]
            // ==========================================
            _enhancers.Add(new Enhancer_Ultraman());
            
            // _enhancers.Add(new Enhancer_Misel()); 
            // _enhancers.Add(new Enhancer_ShortEagle());
            
            ModBehaviour.LogToFile($"[BossManager] 已加载 {_enhancers.Count} 个强化方案。");
        }

        public void Initialize()
        {
            // [修改] 不再检查 EnableBossHealthBar，改为检查新的 Boss 强化开关
            if (!SettingManager.EnableBossEnhancement) 
            {
                ModBehaviour.LogToFile("[BossManager] 强化模块已根据配置关闭。");
                return;
            }
            
            ScanManager.OnCharacterReady += OnBossFound;
            ScanManager.OnCharacterLost += OnBossLost;

            // 处理重载时场上已存在的 Boss
            foreach (var boss in ScanManager.ActiveBosses) 
            {
                OnBossFound(boss);
            }
            
            ModBehaviour.LogToFile("[BossManager] 强化模块初始化完成。");
        }

        public void Deactivate()
        {
            ScanManager.OnCharacterReady -= OnBossFound;
            ScanManager.OnCharacterLost -= OnBossLost;
            _processedBosses.Clear();
        }

        private void OnBossLost(CharacterMainControl ch)
        {
            _processedBosses.Remove(ch);
        }

        private void OnBossFound(CharacterMainControl boss)
        {
            // 防止重复处理
            if (boss == null || _processedBosses.Contains(boss)) return;
            // 双重确认是 Boss
            if (!ScanManager.IsBoss(boss)) return;

            _processedBosses.Add(boss);
            string id = ScanManager.GetCharacterID(boss);

            // 遍历所有强化方案，寻找匹配项
            foreach (var enhancer in _enhancers)
            {
                // 使用 IndexOf 模糊匹配 (忽略 (Clone) 等后缀)
                if (id.IndexOf(enhancer.TargetID, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try
                    {
                        enhancer.OnEnhance(boss);
                        ModBehaviour.LogToFile($"[BossManager] Boss {ScanManager.GetCleanDisplayName(boss)} (ID: {id}) 强化完毕。");
                        break; // 找到一个匹配的就停止
                    }
                    catch (Exception ex)
                    {
                        ModBehaviour.LogErrorToFile($"[BossManager] 强化 {enhancer.TargetID} 时出错: {ex}");
                    }
                }
            }
        }
    }
}