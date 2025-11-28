// 文件名: BossHealthHUDManager.cs
// [v28 - 配置逻辑移交版]
// - 移除: 自身配置加载逻辑
// - 修改: 读取 ModBehaviour.Config
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        private readonly List<CharacterMainControl> _discoveredBosses = new List<CharacterMainControl>();
        private readonly List<CharacterMainControl> _trackedBosses = new List<CharacterMainControl>();
        private bool _uiEnabled = true;

        private float _maxBossDisplayDistance = 20f;
        private readonly Dictionary<CharacterMainControl, float> _lastHpMap = new Dictionary<CharacterMainControl, float>();
        private readonly List<CharacterMainControl?> _cleanupList = new List<CharacterMainControl?>();

        private int _previousTrackedBossCount = -1;
        private int _previousDrawnBossCount = -1;

        private readonly HashSet<string> _knownBossNames = new HashSet<string>
        {
            "光之男","矮鸭","牢登","急速团长","暴走街机","校霸","BA队长","炸弹狂人","三枪哥","喷子","矿长","高级工程师","蝇蝇队长","迷塞尔","维达","？？？","路障","啪啦啪啦","咕噜咕噜","噗咙噗咙","比利比利","口口口口",
            "Man of Light","Pato Chapo","Lordon","Speedy Group Commander","Vida","Big Xing","Rampaging Arcade","Senior Engineer","Triple-Shot Man","Misel","Mine Manager","Shotgunner","Mad Bomber","Security Captain","Fly Captain","School Bully","Billy Billy","Gulu Gulu","Pala Pala","Pulu Pulu","Koko Koko","Roadblock",
        };

        // ───── DUCK HUNTED ─────
        private string? _lastKilledBossName;
        private Coroutine? _duckHuntedCoroutine;

        // ───── UGUI 元素引用 ─────
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
            // [修改] 直接读取 ModBehaviour 的配置
            if (!ModBehaviour.Config.EnableBossHealthBar)
            {
                ModBehaviour.LogToFile("[BossHealthHUDManager] 配置检测: 血条功能已禁用。");
                Destroy(this); 
                return;
            }

            ModBehaviour.LogToFile("[BossHealthHUDManager] Awake (功能已启用)");
            TryFindPlayer();
            CreateUGUI();
        }

        private void Update()
        {
            // 如果禁用了，实际上已经被 Destroy 了，但为了安全保留检查
            if (!ModBehaviour.Config.EnableBossHealthBar) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                if (_canvas != null) _canvas.gameObject.SetActive(_uiEnabled);
                ModBehaviour.LogToFile("[BossHealthHUDManager] HUD " + (_uiEnabled ? "ON" : "OFF"));
            }
            if (!_uiEnabled) return;
            if (_player == null) TryFindPlayer();

            UpdateBossDeathState();

            if (Time.frameCount % 15 == 0)
            {
                UpdateTrackedBosses();
            }

            UpdateUGUI();
        }

        public void RegisterCharacter(CharacterMainControl character)
        {
            if (!ModBehaviour.Config.EnableBossHealthBar) return;

            if (character == null || _discoveredBosses.Contains(character) || character == _player)
                return;

            string displayName = SafeGetName(character);
            
            bool isKnownBoss = _knownBossNames.Contains(character.name) || _knownBossNames.Contains(displayName);

            if (isKnownBoss)
            {
                Health h = character.Health;
                if (h == null) return;

                _discoveredBosses.Add(character);
                if (!_lastHpMap.ContainsKey(character))
                {
                    _lastHpMap[character] = h.CurrentHealth;
                }
                ModBehaviour.LogToFile($"[BossHealthHUDManager] 已注册 Boss: '{displayName}'");
            }
        }

        public void Deactivate()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _discoveredBosses.Clear();
            _trackedBosses.Clear();
            _lastHpMap.Clear();
        }

        // ───── 核心逻辑 ─────

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
                    string bossName = SafeGetName(boss);
                    TriggerDuckHunted(bossName);
                    _cleanupList.Add(boss);
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

        // ───── UGUI 创建 ─────

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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            canvasObj.AddComponent<GraphicRaycaster>();

            GameObject healthBarContainer = CreateUIObject("HealthBarContainer", canvasObj.transform);
            RectTransform containerRect = healthBarContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f);
            containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f);
            containerRect.anchoredPosition = new Vector2(0, 180f);
            containerRect.sizeDelta = new Vector2(1024f, 400f);

            VerticalLayoutGroup layoutGroup = healthBarContainer.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.LowerCenter;
            layoutGroup.spacing = 15f; 
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;

            float barWidth = 1024f;
            float barHeight = 12f; 
            float nameHeight = 24f; 

            for (int i = 0; i < MaxBossBars; i++)
            {
                HealthBarUI ui = new HealthBarUI();

                ui.Root = CreateUIObject($"HealthBar_{i}", healthBarContainer.transform);
                ui.Root.GetComponent<RectTransform>().sizeDelta = new Vector2(barWidth, barHeight + nameHeight + 5f);
                ui.Root.AddComponent<LayoutElement>().preferredHeight = barHeight + nameHeight + 5f;

                ui.NameText = CreateUIText($"NameText_{i}", ui.Root.transform, 22, Color.white, TextAnchor.MiddleLeft);
                RectTransform nameRect = ui.NameText.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 1);
                nameRect.anchorMax = new Vector2(1, 1);
                nameRect.pivot = new Vector2(0, 1);
                nameRect.sizeDelta = new Vector2(barWidth, nameHeight);
                nameRect.anchoredPosition = Vector2.zero;

                GameObject barContainer = CreateUIObject("BarContainer", ui.Root.transform);
                RectTransform barContainerRect = barContainer.GetComponent<RectTransform>();
                barContainerRect.anchorMin = new Vector2(0, 0);
                barContainerRect.anchorMax = new Vector2(1, 0);
                barContainerRect.pivot = new Vector2(0.5f, 0);
                barContainerRect.sizeDelta = new Vector2(0, barHeight);
                barContainerRect.anchoredPosition = new Vector2(0, 0);

                ui.BarBG = CreateUIImage($"BarBG_{i}", barContainer.transform, new Color(0.05f, 0.05f, 0.05f, 0.8f));
                StretchFillRect(ui.BarBG.GetComponent<RectTransform>());

                ui.Fill_Ghost = CreateUIImage("BarFill_Ghost", barContainer.transform, new Color(0.9f, 0.8f, 0.2f, 0.8f));
                ui.Fill_Ghost.type = Image.Type.Filled;
                ui.Fill_Ghost.fillMethod = Image.FillMethod.Horizontal;
                ui.Fill_Ghost.fillOrigin = 0;
                StretchFillRect(ui.Fill_Ghost.GetComponent<RectTransform>(), 0); 

                ui.Fill = CreateUIImage("BarFill", barContainer.transform, new Color(0.5f, 0.02f, 0.02f, 1.0f));
                ui.Fill.type = Image.Type.Filled;
                ui.Fill.fillMethod = Image.FillMethod.Horizontal;
                ui.Fill.fillOrigin = 0;
                StretchFillRect(ui.Fill.GetComponent<RectTransform>(), 0); 

                ui.HpText = CreateUIText($"HPText_{i}", barContainer.transform, 12, new Color(1f,1f,1f,0.7f), TextAnchor.MiddleCenter);
                StretchFillRect(ui.HpText.GetComponent<RectTransform>());

                ui.Root.SetActive(false);
                _healthBarUIs.Add(ui);
            }

            _duckHuntedOverlay = CreateUIObject("DuckHuntedOverlay", canvasObj.transform);
            _duckHuntedCanvasGroup = _duckHuntedOverlay.AddComponent<CanvasGroup>();
            RectTransform overlayRect = _duckHuntedOverlay.GetComponent<RectTransform>();
            
            overlayRect.anchorMin = new Vector2(0, 0.5f);   
            overlayRect.anchorMax = new Vector2(1, 0.5f);   
            overlayRect.pivot = new Vector2(0.5f, 0.5f); 
            
            overlayRect.sizeDelta = new Vector2(0f, 110f); 
            overlayRect.anchoredPosition = new Vector2(0, 0); 

            Image duckHuntedBG = CreateUIImage("DuckHuntedBG", _duckHuntedOverlay.transform, new Color(0f, 0f, 0f, 0.65f));
            StretchFillRect(duckHuntedBG.GetComponent<RectTransform>());

            Color paleGold = new Color(1f, 0.85f, 0.6f);

            _duckHuntedMainText = CreateUIText("MainText", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedMainText.fontStyle = FontStyle.Bold;
            _duckHuntedMainRect = _duckHuntedMainText.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedMainRect);
            _duckHuntedMainRect.sizeDelta = new Vector2(0, 80f);
            _duckHuntedMainRect.anchoredPosition = new Vector2(0, 15f);

            _duckHuntedGhostText1 = CreateUIText("MainText_Ghost1", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText1.fontStyle = FontStyle.Bold;
            _duckHuntedGhost1Rect = _duckHuntedGhostText1.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost1Rect);
            _duckHuntedGhost1Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedGhost1Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;

            _duckHuntedGhostText2 = CreateUIText("MainText_Ghost2", _duckHuntedOverlay.transform, 56, paleGold, TextAnchor.MiddleCenter);
            _duckHuntedGhostText2.fontStyle = FontStyle.Bold;
            _duckHuntedGhost2Rect = _duckHuntedGhostText2.GetComponent<RectTransform>();
            StretchFillRect(_duckHuntedGhost2Rect);
            _duckHuntedGhost2Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedGhost2Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition;

            _duckHuntedSubText = CreateUIText("SubText", _duckHuntedOverlay.transform, 26, Color.white, TextAnchor.MiddleCenter);
            RectTransform subTextRect = _duckHuntedSubText.GetComponent<RectTransform>();
            StretchFillRect(subTextRect);
            subTextRect.sizeDelta = new Vector2(0, 40f);
            subTextRect.anchoredPosition = new Vector2(0, -25f); 

            _duckHuntedOverlay.SetActive(false);
        }

        private void StretchFillRect(RectTransform rect, float margin = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            return obj;
        }

        private Image CreateUIImage(string name, Transform parent, Color color)
        {
            Image img = CreateUIObject(name, parent).AddComponent<Image>();
            img.color = color;

            if (_minimalSprite == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _minimalSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            }

            img.sprite = _minimalSprite;
            return img;
        }

        private Text CreateUIText(string name, Transform parent, int fontSize, Color color, TextAnchor alignment)
        {
            Text txt = CreateUIObject(name, parent).AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;

            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            var shadow = txt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.5f);
            shadow.effectDistance = new Vector2(1, -1);
            return txt;
        }

        // ───── UGUI 更新 ─────

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

                    if (targetRatio < ui.LastKnownFill)
                    {
                        ui.GhostTimer = HealthBarUI.GhostDelay;
                    }
                    else if (targetRatio > ui.CurrentGhostFill)
                    {
                        ui.CurrentGhostFill = targetRatio;
                        ui.GhostTimer = 0f;
                    }

                    if (ui.GhostTimer > 0f)
                    {
                        ui.GhostTimer -= Time.deltaTime;
                    }
                    else
                    {
                        if (ui.CurrentGhostFill > targetRatio)
                        {
                            ui.CurrentGhostFill = Mathf.MoveTowards(
                                ui.CurrentGhostFill,
                                targetRatio,
                                HealthBarUI.GhostLerpSpeed * Time.deltaTime
                            );
                        }
                        else
                        {
                            ui.CurrentGhostFill = targetRatio;
                        }
                    }

                    ui.Fill_Ghost.fillAmount = ui.CurrentGhostFill;
                    ui.LastKnownFill = targetRatio;

                    ui.NameText.text = SafeGetName(boss);
                    ui.HpText.text = string.Format("{0:0}/{1:0}  ({2:P0})", curHp, maxHp, targetRatio);
                    ui.Root.SetActive(true);

                    drawnCount++;
                }
                else
                {
                    _healthBarUIs[i].Root.SetActive(false);
                }
            }

            if (drawnCount != _previousDrawnBossCount)
            {
                _previousDrawnBossCount = drawnCount;
            }
        }

        // ───── 辅助方法 ─────

        private void TryFindPlayer()
        {
            try { _player = CharacterMainControl.Main; }
            catch (Exception) { /* 忽略 */ }
        }

        private void TriggerDuckHunted(string bossName)
        {
            if (_duckHuntedCoroutine != null)
            {
                StopCoroutine(_duckHuntedCoroutine);
            }

            _lastKilledBossName = bossName;
            
            _duckHuntedMainText.text = "DUCK HUNTED";
            _duckHuntedGhostText1.text = "DUCK HUNTED";
            _duckHuntedGhostText2.text = "DUCK HUNTED";
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

                timer += Time.deltaTime;
                yield return null; 
            }

            _duckHuntedCanvasGroup.alpha = 1f;
            _duckHuntedGhost1Rect.anchoredPosition = mainPos;
            _duckHuntedGhost2Rect.anchoredPosition = mainPos;
            _duckHuntedGhostText1.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            _duckHuntedGhostText2.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);

            yield return new WaitForSeconds(GhostHoldTime);

            timer = 0f;
            while (timer < FadeOutTime)
            {
                float t = timer / FadeOutTime; 
                _duckHuntedCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t); 

                timer += Time.deltaTime;
                yield return null; 
            }

            _duckHuntedCanvasGroup.alpha = 0f;
            _duckHuntedOverlay.SetActive(false);
            _duckHuntedCoroutine = null;
        }

        private void TryPlayBossDefeatedSound()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrEmpty(folder)) return;
                string audioDir = Path.Combine(folder, "Audio");
                string filePath = Path.Combine(audioDir, "BossDefeated.mp3");
                if (!System.IO.File.Exists(filePath))
                {
                    ModBehaviour.LogToFile("[BossHealthHUDManager] BossDefeated.mp3 未找到: " + filePath);
                    return;
                }
                AudioManager.PostCustomSFX(filePath, null, false);
            }
            catch (Exception ex)
            {
                ModBehaviour.LogErrorToFile("[BossHealthHUDManager] TryPlayBossDefeatedSound 错误: " + ex);
            }
        }

        // [v21] 新增: 供 InvasionManager 获取当前追踪到的（附近的）Boss 数量
        public int GetTrackedBossCount()
        {
            return _trackedBosses != null ? _trackedBosses.Count : 0;
        }

        // [v22] 新增: 供 InvasionManager 判断某个单位是否为 Boss
        public bool IsKnownBoss(CharacterMainControl ch)
        {
            if (ch == null) return false;
            string displayName = SafeGetName(ch);
            return _knownBossNames.Contains(ch.name) || _knownBossNames.Contains(displayName);
        }

        private static string SafeGetName(CharacterMainControl ch)
        {
            if (ch == null) return string.Empty;
            
            string finalName = ch.name;
            try
            {
                if (ch.characterPreset != null && !string.IsNullOrEmpty(ch.characterPreset.DisplayName))
                {
                    finalName = ch.characterPreset.DisplayName;
                }
            }
            catch { }

            return finalName.Trim('*');
        }
    }
}