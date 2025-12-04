using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// <summary>
    /// 入侵事件管理器 (Final Version)
    /// 集成 30米 Boss 避让、详细日志与独立扫描逻辑
    /// </summary>
    public class InvasionManager
    {
        private readonly ModBehaviour modBehaviour;
        
        // 检查频率 (秒)
        private const float CheckInterval = 10f;      
        // 每次检查触发入侵的概率 (2%)
        private const float InvasionChance = 0.02f;   
        
        // [配置] 30米内有 Boss 则视为战斗中，不打扰
        private const float BossSafeZoneRadius = 30f;

        private bool isInvading = false;
        private enum FingerType { Thumb, Index, Middle, Ring, Little }
        private List<FingerType> availableFingers = new List<FingerType>();

        // 候选入侵者预设库 (从场景中扫描到的小怪)
        private List<CharacterRandomPreset> candidatePresets = new List<CharacterRandomPreset>();
        private HashSet<string> recordedPresetNames = new HashSet<string>();

        // UI 引用
        private GameObject? _uiRoot;
        private CanvasGroup? _uiCanvasGroup;
        private Text? _uiText;
        private Coroutine? _animationCoroutine;

        // 构造函数 (已解耦，只需 ModBehaviour)
        public InvasionManager(ModBehaviour mod)
        {
            this.modBehaviour = mod;
        }

        public void Initialize()
        {
            if (!SettingManager.EnableInvasion)
            {
                ModBehaviour.LogToFile("[InvasionManager] 配置检测: 入侵功能已禁用。");
                return;
            }

            ModBehaviour.LogToFile("[InvasionManager] 正在初始化...");
            
            // 重置手指列表
            availableFingers = new List<FingerType>() {
                FingerType.Thumb, FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Little
            };

            candidatePresets.Clear();
            recordedPresetNames.Clear();

            CreateInvasionUI();

            // 启动循环检查
            modBehaviour.StartCoroutine(InvasionCheckRoutine());
        }

        public void Deactivate() 
        {
            if (_uiRoot != null) UnityEngine.Object.Destroy(_uiRoot);
        }

        /// <summary>
        /// 主循环：定期检查是否满足入侵条件
        /// </summary>
        private IEnumerator InvasionCheckRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(CheckInterval);

                // 如果手指用完了，不再触发
                if (availableFingers.Count == 0) yield break;
                
                // 基础环境检查
                if (LevelManager.Instance == null || LevelManager.Instance.IsBaseLevel) continue;
                if (!LevelManager.LevelInited || CharacterMainControl.Main == null || CharacterMainControl.Main.Health.IsDead) continue;

                // 1. 扫描并录入场景中的小怪信息 (为入侵做准备)
                ScanSceneEnemies();

                // 如果正在入侵中，跳过
                if (isInvading) continue;

                // 2. [核心逻辑] 检查是否处于 Boss 战状态
                // 利用 ScanManager 快速查询玩家周围 30米 是否有 Boss
                bool isBossNearby = ScanManager.IsBossNearby(CharacterMainControl.Main.transform.position, BossSafeZoneRadius);

                // 只有在安全（没有 Boss）的情况下才尝试触发
                if (!isBossNearby)
                {
                    if (Random.value < InvasionChance)
                    {
                        TriggerInvasionSequence().Forget();
                    }
                }
            }
        }

        /// <summary>
        /// 扫描场景，寻找适合作为入侵者原型的单位
        /// </summary>
        private void ScanSceneEnemies()
        {
            int addedCount = 0;
            try
            {
                // 使用 ScanManager 维护的全局列表，效率更高
                foreach (var c in ScanManager.ActiveCharacters)
                {
                    if (c == null || c.characterPreset == null) continue;
                    if (ScanManager.IsPlayer(c) || c.Health.IsDead) continue; 

                    // [关键] 排除 Boss
                    // ScanManager 现已修复判定逻辑，不会再把雇佣兵误判为 Boss
                    // 因此雇佣兵会通过此检查，进入下方的录入流程
                    if (ScanManager.IsBoss(c)) continue;

                    string pName = c.characterPreset.name;
                    string pDispName = c.characterPreset.nameKey;

                    // 防止重复录入
                    if (recordedPresetNames.Contains(pName)) continue;
                    
                    // 过滤友军、商人、动物等不适合入侵的单位
                    if (pName.Contains("Merchant") || pName.Contains("Pet") || pName.Contains("Wolf") || pName.Contains("Animal")) 
                        continue;
                    
                    // 过滤特殊单位 (名字带颜色代码的一般是特殊怪)
                    if (pDispName.Contains("<color") || pDispName.Contains("[")) continue;

                    // 录入成功
                    candidatePresets.Add(c.characterPreset);
                    recordedPresetNames.Add(pName);
                    addedCount++;
                    
                    // [调试日志] 明确打印录入的单位名称
                    ModBehaviour.LogToFile($"[InvasionManager] 录入新样本: {pName} (显示名: {c.characterPreset.DisplayName})");
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile($"[InvasionManager] 扫描出错: {ex.Message}");
            }

            if (addedCount > 0)
            {
                ModBehaviour.LogToFile($"[InvasionManager] 本轮扫描新增 {addedCount} 个样本，当前总库: {candidatePresets.Count}");
            }
        }

        /// <summary>
        /// 触发入侵流程
        /// </summary>
        private async UniTaskVoid TriggerInvasionSequence()
        {
            // 如果没有样本，无法生成
            if (candidatePresets.Count == 0) return;

            isInvading = true;

            if (availableFingers.Count == 0) { isInvading = false; return; }

            // 寻找生成点 (空投逻辑)
            Vector3? validSpawnPos = GetAirDropSpawnPosition(CharacterMainControl.Main.transform.position, 8f, 18f);
            
            if (validSpawnPos == null)
            {
                // 找不到位置，放弃本次入侵
                isInvading = false;
                return;
            }

            // 随机抽取一个手指代号
            int idx = Random.Range(0, availableFingers.Count);
            FingerType finger = availableFingers[idx];
            availableFingers.RemoveAt(idx); 

            // 随机抽取一个敌人模板
            CharacterRandomPreset basePreset = candidatePresets[Random.Range(0, candidatePresets.Count)];
            string fingerTitle = GetFingerTitle(finger);
            string cleanName = basePreset.DisplayName.Replace("*", "").Replace("(Clone)", "").Trim();
            
            // 创建强化版预设
            CharacterRandomPreset modifiedPreset = PrepareModifiedPreset(basePreset, fingerTitle, cleanName, finger);

            // UI 提示
            string message = $"遭到 <color=#FF2020>{fingerTitle}</color> {cleanName} 入侵";
            if (_animationCoroutine != null) modBehaviour.StopCoroutine(_animationCoroutine);
            _animationCoroutine = modBehaviour.StartCoroutine(ShowInvasionAnimation(message));
            
            ModBehaviour.LogToFile($"[InvasionManager] 触发入侵: {message}");

            // 延迟 4 秒后生成
            await UniTask.Delay(4000);

            if (CharacterMainControl.Main != null)
            {
                // 异步生成单位
                CharacterMainControl enemy = await modifiedPreset.CreateCharacterAsync(
                    validSpawnPos.Value, 
                    Vector3.forward, 
                    CharacterMainControl.Main.gameObject.scene.buildIndex, 
                    null, 
                    true 
                );

                if (enemy != null)
                {
                    enemy.SetTeam(Teams.scav); // 设为敌对阵营
                    ApplyFingerBuffs(enemy, finger); // 应用 Buff
                    enemy.name = $"{fingerTitle}_{enemy.name}";
                    
                    // 出场台词
                    string dialogue = GetFingerEntranceLine(finger);
                    enemy.PopText(dialogue, 4f);

                    // 添加战斗对话组件
                    var talker = enemy.gameObject.AddComponent<InvaderCombatTalker>();
                    talker.Setup(enemy, GetFingerCombatLines(finger));

                    // 启动监控 (超时或过远则消失)
                    MonitorInvader(enemy, fingerTitle, cleanName).Forget();
                }
                else
                {
                    ModBehaviour.LogErrorToFile("[InvasionManager] 敌人生成失败！");
                }
            }

            // 冷却时间 3 分钟
            await UniTask.Delay(180000); 
            isInvading = false;
        }

        // =========================================================
        //  数据与配置区 (台词、Buff、生成逻辑)
        // =========================================================

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

        private CharacterRandomPreset PrepareModifiedPreset(CharacterRandomPreset original, string title, string cleanName, FingerType finger)
        {
            var clone = UnityEngine.Object.Instantiate(original);
            string newName = $"<color=#FF2020>[{title}]</color> {cleanName}";
            clone.nameKey = newName;

            // 极高的感知能力，确保它能追着玩家打
            clone.forceTracePlayerDistance = 9999f; 
            clone.sightAngle = 360f;
            clone.sightDistance = 100f;
            clone.forgetTime = 999f;
            clone.hearingAbility = 10f;

            // 基础属性微调
            switch (finger)
            {
                case FingerType.Thumb: 
                    clone.moveSpeedFactor *= 0.8f; // 坦克走得慢
                    break;
                case FingerType.Middle: 
                    clone.moveSpeedFactor *= 1.5f; // 突击手很快
                    clone.reactionTime *= 0.1f;    
                    clone.shootDelay *= 0.1f;      
                    break;
                case FingerType.Little: 
                    clone.moveSpeedFactor *= 1.15f; 
                    break;
            }
            return clone;
        }

        private void ApplyFingerBuffs(CharacterMainControl enemy, FingerType type)
        {
            Item item = enemy.CharacterItem;
            if (item == null) return;

            float hpMultiplier = 2.0f; 

            switch (type)
            {
                case FingerType.Thumb: // 拇指：坦克
                    hpMultiplier = 4.0f;
                    enemy.transform.localScale *= 1.2f; // 体型变大
                    Stat bodyArmor = item.GetStat("BodyArmor".GetHashCode());
                    Stat headArmor = item.GetStat("HeadArmor".GetHashCode());
                    if (bodyArmor != null) bodyArmor.BaseValue += 2f; 
                    if (headArmor != null) headArmor.BaseValue += 2f;
                    break;
                case FingerType.Index: // 食指：狙击/高伤
                    hpMultiplier = 3.0f;
                    Stat gunDmg = item.GetStat("GunDamageMultiplier".GetHashCode());
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                    if (gunDmg != null) gunDmg.BaseValue *= 1.5f;
                    if (meleeDmg != null) meleeDmg.BaseValue *= 1.5f;
                    AddRegen(enemy, 0.5f);
                    break;
                case FingerType.Middle: // 中指：速射
                    hpMultiplier = 2.0f;
                    Stat reload = item.GetStat("ReloadSpeedGain".GetHashCode());
                    if (reload != null) reload.BaseValue += 0.5f;
                    AddRegen(enemy, 0.5f);
                    break;
                case FingerType.Ring: // 无名指：Debuff 光环
                    hpMultiplier = 2.0f;
                    ApplyGhostVisuals(enemy);
                    enemy.gameObject.AddComponent<RingFingerRandomDebuffAura>();
                    AddRegen(enemy, 0.5f);
                    break;
                case FingerType.Little: // 小指：强力回血
                    hpMultiplier = 2.0f;
                    enemy.transform.localScale *= 0.85f; 
                    AddRegen(enemy, 6.0f); 
                    break;
            }

            Stat hp = item.GetStat("MaxHealth".GetHashCode());
            if (hp != null) {
                hp.BaseValue *= hpMultiplier;
                enemy.Health.Init(); 
                enemy.Health.AddHealth(100000f); // 补满血
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
            bgRect.anchorMin = new Vector2(0, 0.5f); bgRect.anchorMax = new Vector2(1, 0.5f); 
            bgRect.pivot = new Vector2(0.5f, 0.5f); bgRect.sizeDelta = new Vector2(0, 100);  
            bgRect.anchoredPosition = new Vector2(0, -250); 

            GameObject textObj = new GameObject("InvasionText");
            textObj.transform.SetParent(bgObj.transform, false);
            _uiText = textObj.AddComponent<Text>();
            
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "Arial");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 42);

            _uiText.font = font;
            _uiText.fontSize = 42; 
            _uiText.alignment = TextAnchor.MiddleCenter;
            _uiText.color = Color.white; 
            _uiText.supportRichText = true;
            
            Shadow shadow = textObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 1f); 
            shadow.effectDistance = new Vector2(2, -2);
            
            RectTransform textRect = _uiText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.sizeDelta = Vector2.zero;

            _uiCanvasGroup = bgObj.AddComponent<CanvasGroup>();
            _uiCanvasGroup.alpha = 0f; 
            _uiCanvasGroup.blocksRaycasts = false; 
        }

        private IEnumerator ShowInvasionAnimation(string text)
        {
            if (_uiCanvasGroup == null || _uiText == null) yield break;

            _uiText.text = text;
            _uiCanvasGroup.alpha = 0f;

            float SmoothFade(float start, float end, float t) => Mathf.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
            float timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(0f, 1f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 1f;

            yield return new WaitForSeconds(3.0f);

            timer = 0f;
            while (timer < 0.5f)
            {
                timer += Time.deltaTime;
                _uiCanvasGroup.alpha = SmoothFade(1f, 0f, timer / 0.5f);
                yield return null;
            }
            _uiCanvasGroup.alpha = 0f;
        }

        private string GetFingerTitle(FingerType t) => t switch {
            FingerType.Thumb => "拇指", FingerType.Index => "食指",
            FingerType.Middle => "中指", FingerType.Ring => "无名指", FingerType.Little => "小指", _ => ""
        };
    }

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
                            if (val != null && !_debuffPool.Contains(val)) _debuffPool.Add(val);
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
            if (selected != null) player.AddBuff(selected, null, 0);
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