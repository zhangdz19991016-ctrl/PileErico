using System;
using UnityEngine;
using Duckov;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace PileErico
{
    // ==============================================================
    // 抽象基类：所有 Boss 强化脚本的通用父类
    // ==============================================================
    public abstract class BossEnhancer
    {
        public abstract string TargetID { get; }
        public abstract void OnEnhance(CharacterMainControl boss);

        protected void Log(string msg) => ModBehaviour.LogToFile($"[{this.GetType().Name}] {msg}");

        /// <summary>
        /// 通用方法：用于属性强化
        /// </summary>
        protected void ApplyStat(CharacterMainControl boss, string statName, float value, bool isMultiplier = true)
        {
            if (boss == null || boss.CharacterItem == null) return;
            int hash = statName.GetHashCode();
            Stat stat = boss.CharacterItem.GetStat(hash);
            if (stat != null)
            {
                if (isMultiplier) stat.BaseValue *= value;
                else stat.BaseValue += value;
                // 特殊处理 MaxHealth，需要重新 Init 和加满血
                if (statName == "MaxHealth") { boss.Health.Init(); boss.Health.AddHealth(999999f); }
            }
        }

        /// <summary>
        /// 直接设置属性值 (用于精确控制速度等)
        /// </summary>
        protected void SetStat(CharacterMainControl boss, string statName, float value)
        {
            if (boss == null || boss.CharacterItem == null) return;
            int hash = statName.GetHashCode();
            Stat stat = boss.CharacterItem.GetStat(hash);
            if (stat != null)
            {
                stat.BaseValue = value;
            }
        }
    }

    /// <summary>
    /// 通用 Boss 阶段控制组件（监听血量比例并触发事件）
    /// 这个组件必须继承自 MonoBehaviour，因为它需要 Update 循环。
    /// </summary>
    public class BossPhaseController : MonoBehaviour
    {
        private CharacterMainControl? _owner;
        private float _triggerRatio;
        private Action<CharacterMainControl>? _onTrigger;
        private bool _triggered = false;

        /// <summary>
        /// 设置阶段触发器。
        /// </summary>
        /// <param name="owner">Boss角色控制器。</param>
        /// <param name="hpRatio">血量触发比例（例如 0.6f 表示 60%）。</param>
        /// <param name="callback">触发时的回调函数。</param>
        public void Setup(CharacterMainControl owner, float hpRatio, Action<CharacterMainControl> callback)
        {
            _owner = owner; _triggerRatio = hpRatio; _onTrigger = callback;
        }

        private void Update()
        {
            if (_triggered || _owner == null || _owner.Health == null || _owner.Health.IsDead) return;
            
            // 使用 _owner.Health 访问血量数据
            float currentRatio = _owner.Health.CurrentHealth / _owner.Health.MaxHealth;
            
            if (currentRatio <= _triggerRatio)
            {
                _triggered = true;
                try { _onTrigger?.Invoke(_owner); } 
                catch (Exception ex) 
                {
                    Debug.LogError($"[BossPhaseController] Phase Trigger Error: {ex}");
                }
            }
        }
    }
}