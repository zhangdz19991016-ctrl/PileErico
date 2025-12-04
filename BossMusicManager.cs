using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Duckov;
using FMOD.Studio;

namespace PileErico
{
    /// <summary>
    /// 全局 Boss 音乐管理器
    /// </summary>
    public class BossMusicManager : MonoBehaviour
    {
        // [修复 1] 允许 Instance 为空 (加上 ?)
        public static BossMusicManager? Instance { get; private set; }

        // --- 配置区域 ---
        private const float TriggerRadius = 30.0f; // 触发/脱战距离
        private const float FadeDuration = 5.0f;   // 淡入淡出时间
        
        private readonly Dictionary<string, string> _musicMapping = new Dictionary<string, string>
        {
            // 光之男
            { "EnemyPreset_Melee_UltraMan", "Dark Souls 3 OST - Vordt of the Boreal Valley (Complete).mp3" },
        };

        // --- 运行时状态 ---
        private EventInstance? _currentEvent;
        
        // [修复 2] 允许 BossID 为空 (加上 ?)
        private string? _currentPlayingBossID = null;
        
        // [修复 3] 允许协程引用为空，并初始化为 null
        private Coroutine? _fadeRoutine = null;
        
        private float _checkTimer = 0f;

        private void Awake()
        {
            if (Instance != null) 
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            _checkTimer += Time.deltaTime;
            if (_checkTimer < 0.5f) return;
            _checkTimer = 0f;

            UpdateMusicState();
        }

        private void UpdateMusicState()
        {
            var player = CharacterMainControl.Main;
            if (player == null)
            {
                StopMusic();
                return;
            }

            // 寻找最近的存活 Boss
            var targetBoss = ScanManager.ActiveBosses
                .Where(b => b != null && !b.Health.IsDead)
                .Select(b => new { Boss = b, Dist = Vector3.Distance(b.transform.position, player.transform.position) })
                .Where(x => x.Dist <= TriggerRadius)
                .OrderBy(x => x.Dist)
                .FirstOrDefault();

            if (targetBoss != null)
            {
                string bossID = ScanManager.GetCharacterID(targetBoss.Boss);
                string? configID = _musicMapping.Keys.FirstOrDefault(k => bossID.Contains(k));

                if (!string.IsNullOrEmpty(configID))
                {
                    if (_currentPlayingBossID != configID)
                    {
                        PlayMusicFor(configID!);
                    }
                    return; 
                }
            }

            if (_currentPlayingBossID != null)
            {
                StopMusic();
            }
        }

        private void PlayMusicFor(string configID)
        {
            if (!_musicMapping.TryGetValue(configID, out string? fileName)) return;

            if (_currentPlayingBossID != null) StopMusic(immediate: true); 

            _currentPlayingBossID = configID;
            
            string? modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(modDir)) return;

            string fullPath = Path.Combine(modDir, "Audio", fileName);

            ModBehaviour.LogToFile($"[BossMusicManager] 开始播放: {fileName}");

            try
            {
                _currentEvent = AudioManager.PlayCustomBGM(fullPath, loop: true);
                if (_currentEvent.HasValue)
                {
                    if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
                    _fadeRoutine = StartCoroutine(FadeVolume(_currentEvent.Value, 0f, 1f, FadeDuration));
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile($"[BossMusicManager] 播放失败: {ex.Message}");
                _currentPlayingBossID = null; // [修复] 这里赋值 null 不再报错
            }
        }

        private void StopMusic(bool immediate = false)
        {
            if (_currentPlayingBossID == null) return;
            
            ModBehaviour.LogToFile($"[BossMusicManager] 停止播放 (立即: {immediate})");
            _currentPlayingBossID = null; // [修复] 这里赋值 null 不再报错

            if (_currentEvent.HasValue)
            {
                if (immediate)
                {
                    var instance = _currentEvent.Value;
                    instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                    instance.release();
                    _currentEvent = null;
                }
                else
                {
                    if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
                    _fadeRoutine = StartCoroutine(FadeVolume(_currentEvent.Value, 1f, 0f, FadeDuration, stopOnComplete: true));
                }
            }
        }

        private IEnumerator FadeVolume(EventInstance instance, float startVol, float endVol, float duration, bool stopOnComplete = false)
        {
            float timer = 0f;
            instance.setVolume(startVol);

            while (timer < duration)
            {
                if (!instance.isValid()) yield break;

                float t = timer / duration;
                instance.setVolume(Mathf.Lerp(startVol, endVol, t));
                
                timer += Time.deltaTime;
                yield return null;
            }

            if (instance.isValid())
            {
                instance.setVolume(endVol);
                if (stopOnComplete)
                {
                    instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                    instance.release();
                    if (_currentEvent.HasValue && _currentEvent.Value.handle == instance.handle) 
                        _currentEvent = null;
                }
            }
        }
    }
}