using System;
using System.Collections;
using System.Collections.Generic;
using System.IO; // [修复] 添加了 IO 引用，解决 Path 报错
using System.Linq;
using Duckov;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace PileErico
{
    public class BossHealthHUDManager : MonoBehaviour
    {
        // ───── 核心逻辑 ─────
        private CharacterMainControl? _player;
        
        // 1. 已发现的Boss列表 (由 ScanManager 事件填充)
        private readonly List<CharacterMainControl> _discoveredBosses = new List<CharacterMainControl>();
        // 2. 当前追踪显示的Boss列表 (根据距离筛选)
        private readonly List<CharacterMainControl> _trackedBosses = new List<CharacterMainControl>();
        // 3. 已知 Boss ID 集合 (从 ScanManager 初始化)
        private HashSet<string> _knownBossIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 我们关注的 Boss 关键词 (用于从 ScanManager 提取 ID)
        private readonly string[] _bossKeywords = 
        {
            "迷塞尔", "三枪哥", "矮鸭", "光之男", "急速团长", "劳登", "风暴生物", 
            "暴走街机", "蝇蝇队长", "矿长", "高级工程师", "喷子", "炸弹狂人", 
            "维达", "路障", "???", "口口口口", "比利比利", "噗咙噗咙", "啪啦啪啦", "咕噜咕噜"
        };

        private bool _uiEnabled = true;
        private float _maxBossDisplayDistance = 20f;
        private readonly Dictionary<CharacterMainControl, float> _lastHpMap = new Dictionary<CharacterMainControl, float>();
        private readonly List<CharacterMainControl?> _cleanupList = new List<CharacterMainControl?>();

        private int _previousTrackedBossCount = -1;
        private int _previousDrawnBossCount = -1;

        // DUCK HUNTED 动画变量
        private string? _lastKilledBossName;
        private Coroutine? _duckHuntedCoroutine;

        // UI 引用
        private Canvas _canvas = null!;
        private List<HealthBarUI> _healthBarUIs = new List<HealthBarUI>();
        private const int MaxBossBars = 3;
        private GameObject _duckHuntedOverlay = null!;
        private CanvasGroup _duckHuntedCanvasGroup = null!;
        private Text _duckHuntedMainText = null!;
        private Text _duckHuntedSubText = null!;
        private Text _duckHuntedGhostText1 = null!;
        private Text _duckHuntedGhostText2 = null!;
        private RectTransform _duckHuntedMainRect = null!;
        private RectTransform _duckHuntedGhost1Rect = null!;
        private RectTransform _duckHuntedGhost2Rect = null!;
        private const float GhostConvergeTime = 1.0f; 
        private const float GhostHoldTime = 0.5f;     
        private const float FadeOutTime = 1.0f;     
        private const float GhostMaxOffset = 20f;     
        private static Sprite? _minimalSprite;

        private class HealthBarUI
        {
            public GameObject Root = null!;
            public Image Fill = null!;
            public Image Fill_Ghost = null!;
            public Text NameText = null!;
            public Text HpText = null!;
            public Image BarBG = null!;
            public float CurrentGhostFill = 1.0f;
            public float LastKnownFill = 1.0f;
            public float GhostTimer = 0f;
            public const float GhostDelay = 0.5f;
            public const float GhostLerpSpeed = 2.0f;
        }

        // ───── 模组入口与生命周期 ─────

        private void Awake()
        {
            if (!ModBehaviour.Config.EnableBossHealthBar)
            {
                Destroy(this);
                return;
            }

            // 1. 初始化 Boss ID 集合
            InitializeBossIdList();

            // 2. 订阅 ScanManager
            ScanManager.OnCharacterSpawned += OnEnemyFound;
            ScanManager.OnCharacterDespawned += OnEnemyLost;

            // 3. 捕获场上已存在的单位
            foreach (var ch in ScanManager.ActiveCharacters)
            {
                OnEnemyFound(ch);
            }

            TryFindPlayer();
            CreateUGUI();
        }

        private void OnDestroy()
        {
            ScanManager.OnCharacterSpawned -= OnEnemyFound;
            ScanManager.OnCharacterDespawned -= OnEnemyLost;
            
            if (_canvas != null) Destroy(_canvas.gameObject);
            _discoveredBosses.Clear();
            _trackedBosses.Clear();
            _lastHpMap.Clear();
        }

        // === 初始化：从 ScanManager 加载 ID ===
        private void InitializeBossIdList()
        {
            _knownBossIds.Clear();
            foreach (var key in _bossKeywords)
            {
                if (ScanManager.NameIdMapping.TryGetValue(key, out string[] ids))
                {
                    foreach (var id in ids) _knownBossIds.Add(id);
                }
            }
            ModBehaviour.LogToFile($"[BossHUD] 已加载 Boss ID 列表，共 {_knownBossIds.Count} 个特征码。");
        }

        // === 事件回调：新单位 ===
        private void OnEnemyFound(CharacterMainControl ch)
        {
            if (IsKnownBoss(ch)) // [修复] 调用改名后的方法
            {
                if (!_discoveredBosses.Contains(ch))
                {
                    _discoveredBosses.Add(ch);
                    if (ch.Health != null && !_lastHpMap.ContainsKey(ch))
                    {
                        _lastHpMap[ch] = ch.Health.CurrentHealth;
                    }
                    ModBehaviour.LogToFile($"[BossHUD] 发现Boss: {ch.name} (ID: {ScanManager.GetCharacterID(ch)})");
                }
            }
        }

        // === 事件回调：单位消失 ===
        private void OnEnemyLost(CharacterMainControl ch)
        {
            if (_discoveredBosses.Contains(ch))
            {
                _discoveredBosses.Remove(ch);
                _trackedBosses.Remove(ch);
                _lastHpMap.Remove(ch);
            }
        }

        // === 核心判断：是否是 Boss ===
        // [修复] 方法名改回 IsKnownBoss 以兼容 InvasionManager
        public bool IsKnownBoss(CharacterMainControl ch)
        {
            if (ch == null) return false;
            string id = ScanManager.GetCharacterID(ch);
            
            // 使用 ID 精确匹配
            if (_knownBossIds.Any(knownId => id.IndexOf(knownId, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                if (_canvas != null) _canvas.gameObject.SetActive(_uiEnabled);
            }
            if (!_uiEnabled) return;
            if (_player == null) TryFindPlayer();

            UpdateBossDeathState();

            // 每 10 帧更新一次距离筛选
            if (Time.frameCount % 10 == 0)
            {
                UpdateTrackedBosses();
            }

            UpdateUGUI();
        }

        private void UpdateTrackedBosses()
        {
            if (_player == null) return;

            List<CharacterMainControl> candidates = new List<CharacterMainControl>();

            foreach (CharacterMainControl boss in _discoveredBosses)
            {
                if (boss == null || !boss) continue;
                Health h = boss.Health;
                if (h == null || h.CurrentHealth <= 0f) continue;

                float dist = Vector3.Distance(_player.transform.position, boss.transform.position);
                if (dist > _maxBossDisplayDistance) continue;

                candidates.Add(boss);
            }

            candidates.Sort((a, b) =>
            {
                Health? ha = a != null ? a.Health : null;
                Health? hb = b != null ? b.Health : null;
                float ma = (ha != null) ? ha.MaxHealth : 0f;
                float mb = (hb != null) ? hb.MaxHealth : 0f;
                return mb.CompareTo(ma);
            });

            _trackedBosses.Clear();
            for (int i = 0; i < candidates.Count && i < MaxBossBars; i++)
            {
                _trackedBosses.Add(candidates[i]);
            }

            if (_trackedBosses.Count != _previousTrackedBossCount)
            {
                _previousTrackedBossCount = _trackedBosses.Count;
            }
        }

        private void UpdateBossDeathState()
        {
            if (_discoveredBosses.Count == 0) return;

            _cleanupList.Clear();

            foreach (CharacterMainControl boss in _discoveredBosses)
            {
                if (boss == null || !boss)
                {
                    _cleanupList.Add(boss);
                    continue;
                }

                Health h = boss.Health;
                if (h == null)
                {
                    _cleanupList.Add(boss);
                    continue;
                }

                float curHp = h.CurrentHealth;
                if (!_lastHpMap.TryGetValue(boss, out float prevHp))
                {
                    _lastHpMap[boss] = curHp;
                    continue;
                }

                if (prevHp > 0f && curHp <= 0f)
                {
                    string bossName = GetDisplayName(boss);
                    TriggerDuckHunted(bossName);
                }

                _lastHpMap[boss] = curHp;
            }

            foreach (CharacterMainControl? dead in _cleanupList)
            {
                if (dead != null)
                {
                    _lastHpMap.Remove(dead);
                    _discoveredBosses.Remove(dead);
                    _trackedBosses.Remove(dead);
                }
            }
        }

        // ───── UI 构建 (保留) ─────
        private void CreateUGUI()
        {
            GameObject canvasObj = new GameObject("BossHealthHUDCanvas");
            DontDestroyOnLoad(canvasObj);
            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();

            GameObject healthBarContainer = CreateUIObject("HealthBarContainer", canvasObj.transform);
            RectTransform containerRect = healthBarContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f); containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f); containerRect.anchoredPosition = new Vector2(0, 180f);
            containerRect.sizeDelta = new Vector2(1024f, 400f);
            VerticalLayoutGroup layoutGroup = healthBarContainer.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.LowerCenter; layoutGroup.spacing = 15f; 
            layoutGroup.childControlWidth = true; layoutGroup.childControlHeight = false;

            float barWidth = 1024f; float barHeight = 12f; float nameHeight = 24f; 

            for (int i = 0; i < MaxBossBars; i++)
            {
                HealthBarUI ui = new HealthBarUI();
                ui.Root = CreateUIObject($"HealthBar_{i}", healthBarContainer.transform);
                ui.Root.GetComponent<RectTransform>().sizeDelta = new Vector2(barWidth, barHeight + nameHeight + 5f);
                ui.Root.AddComponent<LayoutElement>().preferredHeight = barHeight + nameHeight + 5f;

                ui.NameText = CreateUIText($"NameText_{i}", ui.Root.transform, 22, Color.white, TextAnchor.MiddleLeft);
                RectTransform nameRect = ui.NameText.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 1); nameRect.anchorMax = new Vector2(1, 1);
                nameRect.pivot = new Vector2(0, 1); nameRect.sizeDelta = new Vector2(barWidth, nameHeight);
                nameRect.anchoredPosition = Vector2.zero;

                GameObject barContainer = CreateUIObject("BarContainer", ui.Root.transform);
                RectTransform barContainerRect = barContainer.GetComponent<RectTransform>();
                barContainerRect.anchorMin = new Vector2(0, 0); barContainerRect.anchorMax = new Vector2(1, 0);
                barContainerRect.pivot = new Vector2(0.5f, 0); barContainerRect.sizeDelta = new Vector2(0, barHeight);
                barContainerRect.anchoredPosition = new Vector2(0, 0);

                ui.BarBG = CreateUIImage($"BarBG_{i}", barContainer.transform, new Color(0.05f, 0.05f, 0.05f, 0.8f));
                StretchFillRect(ui.BarBG.GetComponent<RectTransform>());

                ui.Fill_Ghost = CreateUIImage("BarFill_Ghost", barContainer.transform, new Color(0.9f, 0.8f, 0.2f, 0.8f));
                ui.Fill_Ghost.type = Image.Type.Filled; ui.Fill_Ghost.fillMethod = Image.FillMethod.Horizontal;
                StretchFillRect(ui.Fill_Ghost.GetComponent<RectTransform>(), 0); 

                ui.Fill = CreateUIImage("BarFill", barContainer.transform, new Color(0.5f, 0.02f, 0.02f, 1.0f));
                ui.Fill.type = Image.Type.Filled; ui.Fill.fillMethod = Image.FillMethod.Horizontal;
                StretchFillRect(ui.Fill.GetComponent<RectTransform>(), 0); 

                ui.HpText = CreateUIText($"HPText_{i}", barContainer.transform, 12, new Color(1f,1f,1f,0.7f), TextAnchor.MiddleCenter);
                StretchFillRect(ui.HpText.GetComponent<RectTransform>());

                ui.Root.SetActive(false);
                _healthBarUIs.Add(ui);
            }

            _duckHuntedOverlay = CreateUIObject("DuckHuntedOverlay", canvasObj.transform);
            _duckHuntedCanvasGroup = _duckHuntedOverlay.AddComponent<CanvasGroup>();
            RectTransform overlayRect = _duckHuntedOverlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = new Vector2(0, 0.5f); overlayRect.anchorMax = new Vector2(1, 0.5f);
            overlayRect.pivot = new Vector2(0.5f, 0.5f); overlayRect.sizeDelta = new Vector2(0f, 110f);
            overlayRect.anchoredPosition = new Vector2(0, 0); 
            Image duckHuntedBG = CreateUIImage("DuckHuntedBG", _duckHuntedOverlay.transform, new Color(0f, 0f, 0f, 0.65f));
            StretchFillRect(duckHuntedBG.GetComponent<RectTransform>());
            Color paleGold = new Color(1f, 0.85f, 0.6f);
            _duckHuntedMainText = CreateUIText("MainText", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedMainText.fontStyle = FontStyle.Bold;
            _duckHuntedMainRect = _duckHuntedMainText.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedMainRect);
            _duckHuntedMainRect.sizeDelta = new Vector2(0, 80f); _duckHuntedMainRect.anchoredPosition = new Vector2(0, 15f);
            _duckHuntedGhostText1 = CreateUIText("MainText_Ghost1", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText1.fontStyle = FontStyle.Bold;
            _duckHuntedGhost1Rect = _duckHuntedGhostText1.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost1Rect);
            _duckHuntedGhost1Rect.sizeDelta = _duckHuntedMainRect.sizeDelta; _duckHuntedGhost1Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;
            _duckHuntedGhostText2 = CreateUIText("MainText_Ghost2", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText2.fontStyle = FontStyle.Bold;
            _duckHuntedGhost2Rect = _duckHuntedGhostText2.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost2Rect);
            _duckHuntedGhost2Rect.sizeDelta = _duckHuntedMainRect.sizeDelta; _duckHuntedGhost2Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;
            _duckHuntedSubText = CreateUIText("SubText", _duckHuntedOverlay.transform, 26, Color.white, TextAnchor.MiddleCenter);
            RectTransform subTextRect = _duckHuntedSubText.GetComponent<RectTransform>();
            StretchFillRect(subTextRect);
            subTextRect.sizeDelta = new Vector2(0, 40f); subTextRect.anchoredPosition = new Vector2(0, -25f); 
            _duckHuntedOverlay.SetActive(false);
        }

        private void UpdateUGUI()
        {
            if (_player == null)
            {
                if (_previousDrawnBossCount != 0)
                {
                    for (int i = 0; i < _healthBarUIs.Count; i++) _healthBarUIs[i].Root.SetActive(false);
                    _previousDrawnBossCount = 0;
                }
                return;
            }

            int drawnCount = 0;
            for (int i = 0; i < _healthBarUIs.Count; i++)
            {
                if (i < _trackedBosses.Count && drawnCount < MaxBossBars)
                {
                    CharacterMainControl boss = _trackedBosses[i];
                    HealthBarUI ui = _healthBarUIs[i];

                    if (boss == null || !boss || boss.Health == null || boss.Health.CurrentHealth <= 0f)
                    {
                        ui.Root.SetActive(false);
                        continue;
                    }

                    bool wasJustActivated = !ui.Root.activeSelf;
                    float maxHp = boss.Health.MaxHealth;
                    float curHp = boss.Health.CurrentHealth;
                    float targetRatio = Mathf.Clamp01(curHp / maxHp);

                    if (wasJustActivated)
                    {
                        ui.CurrentGhostFill = targetRatio;
                        ui.LastKnownFill = targetRatio;
                        ui.GhostTimer = 0f;
                    }

                    ui.Fill.fillAmount = targetRatio;

                    if (targetRatio < ui.LastKnownFill) ui.GhostTimer = HealthBarUI.GhostDelay;
                    else if (targetRatio > ui.CurrentGhostFill) { ui.CurrentGhostFill = targetRatio; ui.GhostTimer = 0f; }

                    if (ui.GhostTimer > 0f) ui.GhostTimer -= Time.deltaTime;
                    else
                    {
                        if (ui.CurrentGhostFill > targetRatio) ui.CurrentGhostFill = Mathf.MoveTowards(ui.CurrentGhostFill, targetRatio, HealthBarUI.GhostLerpSpeed * Time.deltaTime);
                        else ui.CurrentGhostFill = targetRatio;
                    }

                    ui.Fill_Ghost.fillAmount = ui.CurrentGhostFill;
                    ui.LastKnownFill = targetRatio;
                    ui.NameText.text = GetDisplayName(boss);
                    ui.HpText.text = string.Format("{0:0}/{1:0}  ({2:P0})", curHp, maxHp, targetRatio);
                    ui.Root.SetActive(true);
                    drawnCount++;
                }
                else _healthBarUIs[i].Root.SetActive(false);
            }
            if (drawnCount != _previousDrawnBossCount) _previousDrawnBossCount = drawnCount;
        }

        private void TriggerDuckHunted(string bossName)
        {
            if (_duckHuntedCoroutine != null) StopCoroutine(_duckHuntedCoroutine);
            _lastKilledBossName = bossName;
            _duckHuntedMainText.text = "DUCK HUNTED";
            _duckHuntedGhostText1.text = "DUCK HUNTED"; _duckHuntedGhostText2.text = "DUCK HUNTED";
            _duckHuntedSubText.text = bossName;
            _duckHuntedOverlay.SetActive(true);
            _duckHuntedCoroutine = StartCoroutine(AnimateDuckHunted());
            ModBehaviour.LogToFile($"[BossHealthHUDManager] DUCK HUNTED -> {bossName}");
            TryPlayBossDefeatedSound();
        }

        private IEnumerator AnimateDuckHunted()
        {
            float timer = 0f;
            Vector2 mainPos = _duckHuntedMainRect.anchoredPosition;
            Color baseColor = _duckHuntedMainText.color;

            while (timer < GhostConvergeTime)
            {
                float t = timer / GhostConvergeTime; 
                _duckHuntedCanvasGroup.alpha = t;
                float offset = Mathf.Lerp(GhostMaxOffset, 0f, t);
                _duckHuntedGhost1Rect.anchoredPosition = mainPos + new Vector2(offset, 0);
                _duckHuntedGhost2Rect.anchoredPosition = mainPos + new Vector2(-offset, 0);
                float ghostAlpha = Mathf.Lerp(0.5f, 0f, t);
                _duckHuntedGhostText1.color = new Color(baseColor.r, baseColor.g, baseColor.b, ghostAlpha);
                _duckHuntedGhostText2.color = new Color(baseColor.r, baseColor.g, baseColor.b, ghostAlpha);
                timer += Time.deltaTime; yield return null; 
            }
            _duckHuntedCanvasGroup.alpha = 1f;
            _duckHuntedGhost1Rect.anchoredPosition = mainPos; _duckHuntedGhost2Rect.anchoredPosition = mainPos;
            _duckHuntedGhostText1.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            _duckHuntedGhostText2.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            yield return new WaitForSeconds(GhostHoldTime);
            timer = 0f;
            while (timer < FadeOutTime) { float t = timer / FadeOutTime; _duckHuntedCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t); timer += Time.deltaTime; yield return null; }
            _duckHuntedCanvasGroup.alpha = 0f; _duckHuntedOverlay.SetActive(false); _duckHuntedCoroutine = null;
        }

        private void TryPlayBossDefeatedSound()
        {
            try
            {
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(dllPath) ?? ""; // [修复] Path.GetDirectoryName
                string filePath = Path.Combine(folder, "Audio", "BossDefeated.mp3"); // [修复] Path.Combine
                if (System.IO.File.Exists(filePath))
                    AudioManager.PostCustomSFX(filePath, null, false);
            }
            catch (Exception ex) { ModBehaviour.LogErrorToFile("[BossHUD] Sound Error: " + ex); }
        }

        public int GetTrackedBossCount() => _trackedBosses?.Count ?? 0;

        private void TryFindPlayer() { try { _player = CharacterMainControl.Main; } catch { } }

        private string GetDisplayName(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            try { if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.DisplayName)) return ch.characterPreset.DisplayName; } catch { }
            return ch.name.Trim('*');
        }

        // 辅助方法
        private void StretchFillRect(RectTransform rect, float margin = 0) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f); rect.offsetMin = new Vector2(margin, margin); rect.offsetMax = new Vector2(-margin, -margin); }
        private GameObject CreateUIObject(string name, Transform parent) { GameObject obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.AddComponent<RectTransform>(); return obj; }
        private Image CreateUIImage(string name, Transform parent, Color color) {
            Image img = CreateUIObject(name, parent).AddComponent<Image>(); img.color = color;
            if (_minimalSprite == null) { var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply(); _minimalSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)); }
            img.sprite = _minimalSprite; return img;
        }
        private Text CreateUIText(string name, Transform parent, int fontSize, Color color, TextAnchor alignment) {
            Text txt = CreateUIObject(name, parent).AddComponent<Text>(); txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); txt.fontSize = fontSize; txt.color = color; txt.alignment = alignment;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow; txt.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = txt.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.5f); shadow.effectDistance = new Vector2(1, -1); return txt;
        }
    }
}