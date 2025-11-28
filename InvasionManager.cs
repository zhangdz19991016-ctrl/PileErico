using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.UI;       
using Duckov.Utilities; 
using Duckov.Buffs; 
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace PileErico
{
    public class InvasionManager
    {
        // --- 引用 ---
        private readonly ModBehaviour modBehaviour;
        private readonly BossHealthHUDManager bossManager;

        // --- 配置 ---
        // 频率: 10秒一次; 概率: 2%; 冷却: 3分钟
        private const float CheckInterval = 10f;      
        private const float InvasionChance = 0.02f;   

        // --- 物品 ID ---
        private const int MolotovID = 941;    
        private const int FlashbangID = 66;   
        private const int PoisonGasID = 933;  

        // --- 状态 ---
        private bool isInvading = false;
        private enum FingerType { Thumb, Index, Middle, Ring, Little }
        private List<FingerType> availableFingers = new List<FingerType>();

        private List<CharacterRandomPreset> candidatePresets = new List<CharacterRandomPreset>();
        private HashSet<string> recordedPresetNames = new HashSet<string>();

        // --- UI 组件 ---
        private GameObject? _uiRoot;
        private CanvasGroup? _uiCanvasGroup;
        private Text? _uiText;
        private Coroutine? _animationCoroutine;

        public InvasionManager(ModBehaviour mod, BossHealthHUDManager bossMgr)
        {
            this.modBehaviour = mod;
            this.bossManager = bossMgr;
        }

        public void Initialize()
        {
            // [配置检查] 如果在 ModBehaviour 中关闭了入侵，则不启动
            if (!ModBehaviour.Config.EnableInvasion)
            {
                ModBehaviour.LogToFile("[InvasionManager] 配置检测: 入侵功能已禁用 (EnableInvasion = false)。");
                return;
            }

            ModBehaviour.LogToFile("[InvasionManager] 正在初始化 (Final Complete Version)...");
            
            // 初始化冠名池
            availableFingers = new List<FingerType>() {
                FingerType.Thumb, FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Little
            };

            candidatePresets.Clear();
            recordedPresetNames.Clear();

            CreateInvasionUI();

            modBehaviour.StartCoroutine(InvasionCheckRoutine());
        }

        public void Deactivate() 
        {
            if (_uiRoot != null) UnityEngine.Object.Destroy(_uiRoot);
        }

        private void CreateInvasionUI()
        {
            GameObject canvasObj = new GameObject("PileErico_InvasionCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasObj);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; 
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _uiRoot = canvasObj;

            GameObject bgObj = new GameObject("BackgroundBar");
            bgObj.transform.SetParent(canvasObj.transform, false);
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.85f); 
            RectTransform bgRect = bgImage.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f); 
            bgRect.anchorMax = new Vector2(1, 0.5f); 
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(0, 100);  
            bgRect.anchoredPosition = new Vector2(0, -250); 

            GameObject textObj = new GameObject("InvasionText");
            textObj.transform.SetParent(bgObj.transform, false);
            _uiText = textObj.AddComponent<Text>();
            _uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _uiText.fontSize = 42;
            _uiText.alignment = TextAnchor.MiddleCenter;
            _uiText.color = Color.white;
            _uiText.supportRichText = true;
            Shadow shadow = textObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 1f);
            shadow.effectDistance = new Vector2(2, -2);
            RectTransform textRect = _uiText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            _uiCanvasGroup = bgObj.AddComponent<CanvasGroup>();
            _uiCanvasGroup.alpha = 0f; 
            _uiCanvasGroup.blocksRaycasts = false; 
        }

        private IEnumerator InvasionCheckRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(CheckInterval);

                // 1. 枯竭检查
                if (availableFingers.Count == 0)
                {
                    ModBehaviour.LogToFile("[InvasionManager] 所有冠名雇佣兵已全部出场，本局入侵结束。");
                    yield break;
                }

                // 2. 基地安全检查
                if (LevelManager.Instance == null || LevelManager.Instance.IsBaseLevel)
                    continue;

                // 3. 基础合法性检查
                if (!LevelManager.LevelInited || CharacterMainControl.Main == null || CharacterMainControl.Main.Health.IsDead)
                    continue;

                // 4. 扫描并积累候选人
                ScanSceneEnemies();

                // 5. 正在入侵中
                if (isInvading) continue;

                // 6. 附近 Boss 检查
                int nearbyBosses = bossManager != null ? bossManager.GetTrackedBossCount() : 0;

                // 7. 触发判定
                if (nearbyBosses == 0)
                {
                    if (Random.value < InvasionChance)
                    {
                        TriggerInvasionSequence().Forget();
                    }
                }
            }
        }

        private void ScanSceneEnemies()
        {
            try
            {
                var allChars = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                foreach (var c in allChars)
                {
                    if (c == null || c.characterPreset == null) continue;
                    if (c == CharacterMainControl.Main || c.Health.IsDead) continue; 

                    string pName = c.characterPreset.name;
                    string pDispName = c.characterPreset.nameKey;

                    if (recordedPresetNames.Contains(pName)) continue;
                    
                    // 黑名单过滤
                    if (pName.Contains("Merchant") || pName.Contains("Pet") || pName.Contains("Wolf") || pName.Contains("Animal")) 
                        continue;
                    
                    if (bossManager != null && bossManager.IsKnownBoss(c)) continue;

                    if (pDispName.Contains("<color") || pDispName.Contains("[")) continue;

                    candidatePresets.Add(c.characterPreset);
                    recordedPresetNames.Add(pName);
                    ModBehaviour.LogToFile($"[InvasionManager] 录入新候选单位: {c.characterPreset.DisplayName} ({pName})");
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile($"[InvasionManager] 扫描出错: {ex.Message}");
            }
        }

        private async UniTaskVoid TriggerInvasionSequence()
        {
            if (candidatePresets.Count == 0) return;

            isInvading = true;

            if (availableFingers.Count == 0) { isInvading = false; return; }

            // 1. 寻找生成点 (高空射线法)
            Vector3? validSpawnPos = GetAirDropSpawnPosition(CharacterMainControl.Main.transform.position, 8f, 18f);
            
            if (validSpawnPos == null)
            {
                // 地形不合适则跳过
                isInvading = false;
                return;
            }

            int idx = Random.Range(0, availableFingers.Count);
            FingerType finger = availableFingers[idx];
            availableFingers.RemoveAt(idx); 

            // 2. 准备数据
            CharacterRandomPreset basePreset = candidatePresets[Random.Range(0, candidatePresets.Count)];
            string fingerTitle = GetFingerTitle(finger);
            string cleanName = CleanName(basePreset.DisplayName);
            
            CharacterRandomPreset modifiedPreset = PrepareModifiedPreset(basePreset, fingerTitle, cleanName, finger);

            // 3. UI 提示
            string message = $"遭到 <color=#FF2020>{fingerTitle}</color> {cleanName} 入侵";
            if (_animationCoroutine != null) modBehaviour.StopCoroutine(_animationCoroutine);
            _animationCoroutine = modBehaviour.StartCoroutine(ShowInvasionAnimation(message));
            
            ModBehaviour.LogToFile($"[InvasionManager] 触发入侵: {message}");

            // 4. 等待动画 (4秒)
            await UniTask.Delay(4000);

            // 5. 生成单位
            if (CharacterMainControl.Main != null)
            {
                CharacterMainControl enemy = await modifiedPreset.CreateCharacterAsync(
                    validSpawnPos.Value, 
                    Vector3.forward, 
                    CharacterMainControl.Main.gameObject.scene.buildIndex, 
                    null, 
                    true 
                );

                if (enemy != null)
                {
                    enemy.SetTeam(Teams.scav); 
                    ApplyFingerBuffs(enemy, finger);
                    enemy.name = $"{fingerTitle}_{enemy.name}";
                    
                    // 登场台词
                    string dialogue = GetFingerEntranceLine(finger);
                    enemy.PopText(dialogue, 4f);

                    // 挂载战斗对话
                    var talker = enemy.gameObject.AddComponent<InvaderCombatTalker>();
                    talker.Setup(enemy, GetFingerCombatLines(finger));

                    // 启动离去监控
                    MonitorInvader(enemy, fingerTitle, cleanName).Forget();
                }
                else
                {
                    ModBehaviour.LogErrorToFile("[InvasionManager] 敌人生成失败！");
                }
            }

            // 6. 冷却 3分钟
            await UniTask.Delay(180000); 
            isInvading = false;
        }

        // --- 台词库 ---
        private string[] GetFingerCombatLines(FingerType t) => t switch {
            FingerType.Thumb => new[] { "不痛不痒。", "就这？", "我会碾碎你！", "不过是徒劳。" },
            FingerType.Index => new[] { "我准备好了。", "面对我！", "一击致命！", "哈！" },
            FingerType.Middle => new[] { "你想去哪？", "我才刚热好身！", "我正在全速前进！", "芜湖！" },
            FingerType.Ring => new[] { "炽热。", "温腥。", "腐朽。", "麻痹。" },
            FingerType.Little => new[] { "我还没准备好……", "你在干什么？", "我感觉伤口正在愈合。", "你可能也需要来一针。" },
            _ => new[] { "……" }
        };

        private string GetFingerEntranceLine(FingerType t) => t switch {
            FingerType.Thumb => "老子闪亮登场！",        
            FingerType.Index => "食指已经就位。",       
            FingerType.Middle => "正在赶往目标点！",           
            FingerType.Ring => "……降临。",      
            FingerType.Little => "我也要上吗？",    
            _ => "……"
        };

        // --- 预设修改 ---
        private CharacterRandomPreset PrepareModifiedPreset(CharacterRandomPreset original, string title, string cleanName, FingerType finger)
        {
            var clone = UnityEngine.Object.Instantiate(original);
            string newName = $"<color=#FF2020>[{title}]</color> {cleanName}";
            clone.nameKey = newName;

            // 疯狗 AI
            clone.forceTracePlayerDistance = 9999f; 
            clone.sightAngle = 360f;
            clone.sightDistance = 100f;
            clone.forgetTime = 999f;
            clone.hearingAbility = 10f;

            // 指头特性调整
            switch (finger)
            {
                case FingerType.Thumb: 
                    clone.moveSpeedFactor *= 0.8f; // 减速
                    break;

                case FingerType.Middle: 
                    clone.moveSpeedFactor *= 1.5f; // 加速
                    clone.reactionTime *= 0.1f;    
                    clone.shootDelay *= 0.1f;      
                    break;

                case FingerType.Little: 
                    clone.moveSpeedFactor *= 1.15f; 
                    break;
            }

            return clone;
        }

        // --- 属性与Buff应用 ---
        private void ApplyFingerBuffs(CharacterMainControl enemy, FingerType type)
        {
            Item item = enemy.CharacterItem;
            if (item == null) return;

            float hpMultiplier = 2.0f; 

            switch (type)
            {
                case FingerType.Thumb: 
                    hpMultiplier = 4.0f;
                    enemy.transform.localScale *= 2.0f; 
                    Stat bodyArmor = item.GetStat("BodyArmor".GetHashCode());
                    Stat headArmor = item.GetStat("HeadArmor".GetHashCode());
                    if (bodyArmor != null) bodyArmor.BaseValue += 1f; 
                    if (headArmor != null) headArmor.BaseValue += 1f;
                    break;

                case FingerType.Index: 
                    hpMultiplier = 3.0f;
                    Stat gunDmg = item.GetStat("GunDamageMultiplier".GetHashCode());
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                    if (gunDmg != null) gunDmg.BaseValue *= 1.5f;
                    if (meleeDmg != null) meleeDmg.BaseValue *= 1.5f;
                    AddRegen(enemy, 0.5f);
                    break;

                case FingerType.Middle: 
                    hpMultiplier = 2.0f;
                    Stat reload = item.GetStat("ReloadSpeedGain".GetHashCode());
                    if (reload != null) reload.BaseValue += 0.5f;
                    AddRegen(enemy, 0.5f);
                    break;

                case FingerType.Ring: 
                    hpMultiplier = 2.0f;
                    ApplyGhostVisuals(enemy);
                    enemy.gameObject.AddComponent<RingFingerRandomDebuffAura>();
                    AddRegen(enemy, 0.5f);
                    break;

                case FingerType.Little: 
                    hpMultiplier = 2.0f;
                    enemy.transform.localScale *= 0.7f; 
                    AddRegen(enemy, 6.0f);
                    break;
            }

            Stat hp = item.GetStat("MaxHealth".GetHashCode());
            if (hp != null) {
                hp.BaseValue *= hpMultiplier;
                enemy.Health.Init(); 
                enemy.Health.AddHealth(100000f); 
            }
        }

        private void AddRegen(CharacterMainControl enemy, float amount)
        {
            var regen = enemy.gameObject.AddComponent<SimpleRegenComponent>();
            regen.Setup(enemy, amount);
        }

        private void ApplyGhostVisuals(CharacterMainControl enemy)
        {
            try
            {
                var renderers = enemy.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var r in renderers)
                {
                    foreach (var mat in r.materials)
                    {
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.EnableKeyword("_EMISSION");
                            mat.SetColor("_EmissionColor", new Color(0.5f, 0.1f, 0.1f) * 2f); 
                        }
                    }
                }
            }
            catch { }
        }

        // --- 监控与生成算法 ---

        private async UniTaskVoid MonitorInvader(CharacterMainControl enemy, string fingerTitle, string cleanName)
        {
            await UniTask.Delay(10000);

            while (enemy != null && !enemy.Health.IsDead)
            {
                if (CharacterMainControl.Main == null) break;

                float dist = Vector3.Distance(enemy.transform.position, CharacterMainControl.Main.transform.position);

                if (dist > 50f)
                {
                    ModBehaviour.LogToFile($"[InvasionManager] 入侵者 {fingerTitle} 距离过远 ({dist:F1}m)，判定为离去。");
                    UnityEngine.Object.Destroy(enemy.gameObject);

                    string message = $"<color=#FF2020>{fingerTitle}</color> {cleanName} 已经离去";
                    if (_animationCoroutine != null) modBehaviour.StopCoroutine(_animationCoroutine);
                    _animationCoroutine = modBehaviour.StartCoroutine(ShowInvasionAnimation(message));
                    break;
                }
                await UniTask.Delay(1000);
            }
        }

        private Vector3? GetAirDropSpawnPosition(Vector3 center, float min, float max)
        {
            for (int i = 0; i < 20; i++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(min, max);
                Vector3 targetXZ = center + new Vector3(circle.x, 0, circle.y);
                Vector3 rayOrigin = new Vector3(targetXZ.x, center.y + 50f, targetXZ.z);
                
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 100f, -1, QueryTriggerInteraction.Ignore))
                {
                    if (Vector3.Distance(hit.point, center) < min) continue;
                    return hit.point + Vector3.up * 1.5f;
                }
            }
            
            for (int i = 0; i < 5; i++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(min, max);
                Vector3 target = center + new Vector3(circle.x, 0, circle.y);
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(target, out hit, 50f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    if (Vector3.Distance(hit.position, center) >= min)
                        return hit.position + Vector3.up * 1.5f;
                }
            }
            return null;
        }

        // --- UI 动画 ---
        private IEnumerator ShowInvasionAnimation(string text)
        {
            if (_uiCanvasGroup == null || _uiText == null) yield break;

            _uiText.text = text;
            _uiCanvasGroup.alpha = 0f;

            float SmoothFade(float start, float end, float t)
            {
                return Mathf.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
            }

            // 1. 淡入 (0.5s)
            float timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(0f, 1f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 1f;

            // 2. 停留 (1.0s)
            yield return new WaitForSeconds(1.0f);

            // 3. 呼吸淡出 (0.5s)
            timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(1f, 0f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 0f;

            // 4. 呼吸淡入 (0.5s)
            timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(0f, 1f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 1f;

            // 5. 再次停留 (1.0s)
            yield return new WaitForSeconds(1.0f);

            // 6. 最终淡出 (0.5s)
            timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(1f, 0f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 0f;
        }

        // --- 辅助方法 ---
        private string CleanName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "未知单位";
            return input.Replace("*", "").Trim();
        }

        private string GetFingerTitle(FingerType t) => t switch {
            FingerType.Thumb => "拇指", FingerType.Index => "食指",
            FingerType.Middle => "中指", FingerType.Ring => "无名指", FingerType.Little => "小指", _ => ""
        };
    }

    // --- 组件类 ---
    public class InvaderCombatTalker : MonoBehaviour
    {
        private CharacterMainControl? _owner;
        private string[]? _lines;

        public void Setup(CharacterMainControl owner, string[] lines)
        {
            _owner = owner;
            _lines = lines;
            StartCoroutine(TalkRoutine());
        }

        private IEnumerator TalkRoutine()
        {
            while (_owner != null && !_owner.Health.IsDead)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(6f, 12f));

                if (_owner == null || _owner.Health.IsDead) break;
                if (CharacterMainControl.Main == null) break;

                if (Vector3.Distance(transform.position, CharacterMainControl.Main.transform.position) < 25f)
                {
                    if (_lines != null && _lines.Length > 0)
                    {
                        string line = _lines[UnityEngine.Random.Range(0, _lines.Length)];
                        _owner.PopText(line, 2.5f);
                    }
                }
            }
        }
    }

    public class RingFingerRandomDebuffAura : MonoBehaviour
    {
        private float nextBuffTime = 0f;
        private const float BuffInterval = 2.0f; 
        private const float AuraRadius = 20.0f;

        private List<Buff> _debuffPool = new List<Buff>();

        void Start()
        {
            var buffsData = GameplayDataSettings.Buffs;
            if (buffsData == null) return;

            var type = buffsData.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            string[] keywords = { "BleedS", "Electr", "Shock", "Fire", "Burn", "Poison" };

            foreach (var p in properties)
            {
                if (p.PropertyType == typeof(Buff))
                {
                    foreach (var key in keywords)
                    {
                        if (p.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var val = p.GetValue(buffsData) as Buff;
                            if (val != null && !_debuffPool.Contains(val))
                            {
                                _debuffPool.Add(val);
                            }
                            break;
                        }
                    }
                }
            }
        }

        void Update()
        {
            if (CharacterMainControl.Main == null || CharacterMainControl.Main.Health.IsDead) return;

            float dist = Vector3.Distance(transform.position, CharacterMainControl.Main.transform.position);
            
            if (dist <= AuraRadius)
            {
                if (Time.time > nextBuffTime)
                {
                    ApplyRandomDebuff(CharacterMainControl.Main);
                    nextBuffTime = Time.time + BuffInterval;
                }
            }
        }

        private void ApplyRandomDebuff(CharacterMainControl player)
        {
            if (_debuffPool.Count == 0) return;

            Buff selected = _debuffPool[UnityEngine.Random.Range(0, _debuffPool.Count)];
            if (selected != null)
            {
                player.AddBuff(selected, null, 0);
            }
        }
    }

    public class SimpleRegenComponent : MonoBehaviour
    {
        private CharacterMainControl? target;
        private float rate;
        public void Setup(CharacterMainControl c, float r) { target = c; rate = r; }
        void Update() {
            if (target != null && !target.Health.IsDead) target.AddHealth(rate * Time.deltaTime);
        }
    }
}