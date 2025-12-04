using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duckov;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace PileErico
{
    /// <summary>
    /// Boss 血条显示管理器 (UI Tweaked: Pos 160, HP Text Above)
    /// </summary>
    public class BossHealthHUDManager : MonoBehaviour
    {
        private CharacterMainControl? _player;
        private readonly List<CharacterMainControl> _trackedBosses = new List<CharacterMainControl>();
        private bool _uiEnabled = true;
        private const float DisplayDistance = 30f; 

        // 动画变量
        private Coroutine? _duckHuntedCoroutine;
        private const float GhostConvergeTime = 1.0f; 
        private const float GhostHoldTime = 0.5f;     
        private const float FadeOutTime = 1.0f;     
        private const float GhostMaxOffset = 20f; 

        // UI 引用
        private Canvas _canvas = null!;
        private GameObject _healthBarContainer = null!;
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
        private static Sprite? _minimalSprite;

        private class HealthBarUI
        {
            public GameObject Root = null!;
            public Image Fill = null!;
            public Image Fill_Ghost = null!;
            public Text NameText = null!;
            public Text HpText = null!; // 血量文本
            public Image BarBG = null!;
            public Image Border = null!;
            public float CurrentGhostFill = 1.0f;
            public float LastKnownFill = 1.0f;
            public float GhostTimer = 0f;
            public const float GhostDelay = 0.5f;
            public const float GhostLerpSpeed = 2.0f;
        }

        [Serializable]
        public class ModuleConfig { public bool EnableBossHealthBar = true; }
        private static ModuleConfig _currentConfig = new ModuleConfig();

        private void Awake()
        {
            LoadModuleConfig();
            if (!_currentConfig.EnableBossHealthBar) { Destroy(this); return; }
            TryFindPlayer();
            CreateUGUI();
        }

        private void OnEnable() => ScanManager.OnCharacterLost += OnScanCharacterLost;
        private void OnDisable() { ScanManager.OnCharacterLost -= OnScanCharacterLost; _trackedBosses.Clear(); }
        private void OnDestroy() { if (_canvas != null && _canvas.gameObject != null) Destroy(_canvas.gameObject); }

        private void OnScanCharacterLost(CharacterMainControl ch)
        {
            if (ScanManager.IsBoss(ch) && ch.Health != null && ch.Health.IsDead)
            {
                string bossName = ScanManager.GetCleanDisplayName(ch);
                TriggerDuckHunted(bossName);
            }
        }

        private void Update()
        {
            if (!_currentConfig.EnableBossHealthBar) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
            {
                _uiEnabled = !_uiEnabled;
                if (_canvas != null) _canvas.gameObject.SetActive(_uiEnabled);
            }
            if (!_uiEnabled) return;
            if (_player == null) TryFindPlayer();

            if (Time.frameCount % 10 == 0) UpdateTrackedBosses();
            UpdateUGUI();
        }

        private void UpdateTrackedBosses()
        {
            if (_player == null) return;
            _trackedBosses.Clear();
            var nearbyBosses = ScanManager.GetNearbyBosses(_player.transform.position, DisplayDistance);
            var sortedCandidates = nearbyBosses.OrderByDescending(b => b.Health.MaxHealth).ToList();
            for (int i = 0; i < sortedCandidates.Count && i < MaxBossBars; i++) _trackedBosses.Add(sortedCandidates[i]);
        }

        private void UpdateUGUI()
        {
            if (_player == null) return;

            for (int i = 0; i < _healthBarUIs.Count; i++)
            {
                HealthBarUI ui = _healthBarUIs[i];
                if (i < _trackedBosses.Count)
                {
                    CharacterMainControl boss = _trackedBosses[i];
                    if (boss == null || !boss || boss.Health == null) { ui.Root.SetActive(false); continue; }
                    if (!ui.Root.activeSelf) ui.Root.SetActive(true);

                    float maxHp = boss.Health.MaxHealth;
                    float curHp = boss.Health.CurrentHealth;
                    float targetRatio = Mathf.Clamp01(curHp / maxHp);

                    string displayName = ScanManager.GetCleanDisplayName(boss);
                    if (ui.NameText.text != displayName) ui.NameText.text = displayName;

                    ui.HpText.text = $"{curHp:0}/{maxHp:0} ({(targetRatio * 100f):0}%)";
                    ui.Fill.fillAmount = targetRatio;

                    if (Mathf.Abs(ui.CurrentGhostFill - targetRatio) > 0.5f && !ui.Root.activeInHierarchy) { ui.CurrentGhostFill = targetRatio; ui.LastKnownFill = targetRatio; ui.GhostTimer = 0f; }
                    if (targetRatio < ui.LastKnownFill) { ui.GhostTimer = HealthBarUI.GhostDelay; ui.LastKnownFill = targetRatio; }
                    else if (targetRatio > ui.CurrentGhostFill) { ui.CurrentGhostFill = targetRatio; ui.LastKnownFill = targetRatio; ui.GhostTimer = 0f; }

                    if (ui.GhostTimer > 0f) ui.GhostTimer -= Time.deltaTime;
                    else if (ui.CurrentGhostFill > targetRatio) ui.CurrentGhostFill = Mathf.MoveTowards(ui.CurrentGhostFill, targetRatio, HealthBarUI.GhostLerpSpeed * Time.deltaTime);
                    ui.Fill_Ghost.fillAmount = ui.CurrentGhostFill;
                }
                else
                {
                    if (ui.Root.activeSelf) ui.Root.SetActive(false);
                }
            }
        }

        private void LoadModuleConfig() { try { string p=Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)??"", "ModuleEnabled.json"); if(!File.Exists(p)) File.WriteAllText(p, JsonUtility.ToJson(_currentConfig, true)); else _currentConfig=JsonUtility.FromJson<ModuleConfig>(File.ReadAllText(p)); } catch{ _currentConfig=new ModuleConfig(); } }
        private void TryFindPlayer() { try { _player = CharacterMainControl.Main; } catch {} }

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
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            _healthBarContainer = CreateUIObject("HealthBarContainer", canvasObj.transform);
            RectTransform containerRect = _healthBarContainer.GetComponent<RectTransform>();
            
            // [修复] 位置调整为 160f (比180低一点，比100高)
            containerRect.anchorMin = new Vector2(0.5f, 0f); containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f); containerRect.anchoredPosition = new Vector2(0, 160f); 
            containerRect.sizeDelta = new Vector2(1024f, 400f);
            
            VerticalLayoutGroup layout = _healthBarContainer.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerCenter; layout.spacing = 25f; 
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            float innerBarHeight = 12f;
            float borderThickness = 2f; 
            float totalBarHeight = innerBarHeight + (borderThickness * 2); 
            float nameHeight = 22f; 
            float textGap = 3f;
            float totalItemHeight = totalBarHeight + nameHeight + textGap;

            Color borderColor = new Color(0.412f, 0.4f, 0.333f, 1f);

            for (int i = 0; i < MaxBossBars; i++)
            {
                HealthBarUI ui = new HealthBarUI();
                ui.Root = CreateUIObject($"HealthBar_{i}", _healthBarContainer.transform);
                LayoutElement le = ui.Root.AddComponent<LayoutElement>();
                le.minHeight = totalItemHeight; 
                le.preferredHeight = totalItemHeight; 
                le.preferredWidth = 1024f;

                // 1. 边框 (Bottom)
                GameObject borderObj = CreateUIObject("Border", ui.Root.transform);
                RectTransform borderRect = borderObj.GetComponent<RectTransform>();
                borderRect.anchorMin = new Vector2(0, 0); borderRect.anchorMax = new Vector2(1, 0);
                borderRect.pivot = new Vector2(0.5f, 0); 
                borderRect.anchoredPosition = Vector2.zero; 
                borderRect.sizeDelta = new Vector2(0, totalBarHeight);

                ui.Border = borderObj.AddComponent<Image>();
                ui.Border.color = borderColor;

                // 2. 内部血条 (Inside Border)
                GameObject barContainer = CreateUIObject("BarContainer", borderObj.transform);
                RectTransform barRect = barContainer.GetComponent<RectTransform>();
                StretchFillRect(barRect, borderThickness); 

                // 3. 名字文本 (Top-Left)
                ui.NameText = CreateUIText($"NameText_{i}", ui.Root.transform, 20, Color.white, TextAnchor.MiddleLeft);
                RectTransform nameRect = ui.NameText.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 0); nameRect.anchorMax = new Vector2(1, 0); 
                nameRect.pivot = new Vector2(0.5f, 0);
                // 位于血条上方
                nameRect.anchoredPosition = new Vector2(2f, totalBarHeight + textGap); 
                nameRect.sizeDelta = new Vector2(0, nameHeight);

                // 4. [新功能] 血量文本 (Top-Center)
                // 移到了 Root 下，和 NameText 同级，不再位于 barContainer 内部
                ui.HpText = CreateUIText($"HPText_{i}", ui.Root.transform, 18, new Color(1f,1f,1f,0.9f), TextAnchor.MiddleCenter);
                RectTransform hpRect = ui.HpText.GetComponent<RectTransform>();
                hpRect.anchorMin = new Vector2(0, 0); hpRect.anchorMax = new Vector2(1, 0);
                hpRect.pivot = new Vector2(0.5f, 0);
                // 和名字同一高度
                hpRect.anchoredPosition = new Vector2(0, totalBarHeight + textGap);
                hpRect.sizeDelta = new Vector2(0, nameHeight);

                // 血条填充
                ui.BarBG = CreateUIImage("BG", barContainer.transform, new Color(0.05f, 0.05f, 0.05f, 0.85f));
                StretchFillRect(ui.BarBG.GetComponent<RectTransform>());
                
                ui.Fill_Ghost = CreateUIImage("Ghost", barContainer.transform, new Color(0.9f, 0.8f, 0.2f, 0.9f));
                ui.Fill_Ghost.type = Image.Type.Filled; ui.Fill_Ghost.fillMethod = Image.FillMethod.Horizontal;
                StretchFillRect(ui.Fill_Ghost.GetComponent<RectTransform>());

                ui.Fill = CreateUIImage("Fill", barContainer.transform, new Color(0.6f, 0.05f, 0.05f, 1.0f));
                ui.Fill.type = Image.Type.Filled; ui.Fill.fillMethod = Image.FillMethod.Horizontal;
                StretchFillRect(ui.Fill.GetComponent<RectTransform>());

                ui.Root.SetActive(false);
                _healthBarUIs.Add(ui);
            }
            CreateDuckHuntedOverlay(canvasObj.transform);
        }

        private void CreateDuckHuntedOverlay(Transform parent) {
            _duckHuntedOverlay = CreateUIObject("DuckHuntedOverlay", parent);
            _duckHuntedCanvasGroup = _duckHuntedOverlay.AddComponent<CanvasGroup>();
            RectTransform r = _duckHuntedOverlay.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 0.5f); r.anchorMax = new Vector2(1, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(0, 110f); r.anchoredPosition = Vector2.zero;
            Image bg = CreateUIImage("BG", _duckHuntedOverlay.transform, new Color(0,0,0,0.65f)); StretchFillRect(bg.GetComponent<RectTransform>());
            _duckHuntedMainText = CreateUIText("Main", _duckHuntedOverlay.transform, 56, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter); _duckHuntedMainText.fontStyle = FontStyle.Bold;
            _duckHuntedMainRect = _duckHuntedMainText.GetComponent<RectTransform>(); StretchFillRect(_duckHuntedMainRect); _duckHuntedMainRect.sizeDelta = new Vector2(0, 80f); _duckHuntedMainRect.anchoredPosition = new Vector2(0, 15f);
            _duckHuntedGhostText1 = CreateUIText("G1", _duckHuntedOverlay.transform, 56, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter); _duckHuntedGhostText1.fontStyle = FontStyle.Bold;
            _duckHuntedGhost1Rect = _duckHuntedGhostText1.GetComponent<RectTransform>(); StretchFillRect(_duckHuntedGhost1Rect); _duckHuntedGhost1Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition; _duckHuntedGhost1Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedGhostText2 = CreateUIText("G2", _duckHuntedOverlay.transform, 56, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter); _duckHuntedGhostText2.fontStyle = FontStyle.Bold;
            _duckHuntedGhost2Rect = _duckHuntedGhostText2.GetComponent<RectTransform>(); StretchFillRect(_duckHuntedGhost2Rect); _duckHuntedGhost2Rect.anchoredPosition = _duckHuntedMainRect.anchoredPosition; _duckHuntedGhost2Rect.sizeDelta = _duckHuntedMainRect.sizeDelta;
            _duckHuntedSubText = CreateUIText("Sub", _duckHuntedOverlay.transform, 26, Color.white, TextAnchor.MiddleCenter);
            RectTransform subRect = _duckHuntedSubText.GetComponent<RectTransform>(); StretchFillRect(subRect); subRect.sizeDelta = new Vector2(0, 40f); subRect.anchoredPosition = new Vector2(0, -25f);
            _duckHuntedOverlay.SetActive(false);
        }

        private GameObject CreateUIObject(string name, Transform parent) { GameObject o = new GameObject(name); o.transform.SetParent(parent, false); o.AddComponent<RectTransform>(); return o; }
        private void StretchFillRect(RectTransform r, float m = 0) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f); r.offsetMin = new Vector2(m, m); r.offsetMax = new Vector2(-m, -m); }
        private Image CreateUIImage(string name, Transform p, Color c) { Image i = CreateUIObject(name, p).AddComponent<Image>(); i.color = c; if(_minimalSprite==null){ var t=new Texture2D(1,1); t.SetPixel(0,0,Color.white); t.Apply(); _minimalSprite=Sprite.Create(t,new Rect(0,0,1,1),new Vector2(0.5f,0.5f));} i.sprite=_minimalSprite; return i; }
        private Text CreateUIText(string name, Transform p, int s, Color c, TextAnchor a) { Text t = CreateUIObject(name, p).AddComponent<Text>(); Font? f = Resources.GetBuiltinResource<Font>("Arial.ttf"); if(f==null) f=Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x=>x.name=="Arial"); if(f==null) f=Font.CreateDynamicFontFromOSFont("Arial", s); t.font=f; t.fontSize=s; t.color=c; t.alignment=a; t.horizontalOverflow=HorizontalWrapMode.Overflow; t.verticalOverflow=VerticalWrapMode.Overflow; t.gameObject.AddComponent<Shadow>().effectColor=new Color(0,0,0,0.5f); return t; }
        private void TriggerDuckHunted(string name) { if(_duckHuntedCoroutine!=null) StopCoroutine(_duckHuntedCoroutine); _duckHuntedMainText.text="DUCK HUNTED"; _duckHuntedGhostText1.text="DUCK HUNTED"; _duckHuntedGhostText2.text="DUCK HUNTED"; _duckHuntedSubText.text=name; _duckHuntedOverlay.SetActive(true); _duckHuntedCoroutine=StartCoroutine(AnimDuckHunted()); TryPlaySound(); }
        private IEnumerator AnimDuckHunted() { float t = 0; Vector2 pos = _duckHuntedMainRect.anchoredPosition; while(t<GhostConvergeTime) { t+=Time.deltaTime; float p = t/GhostConvergeTime; _duckHuntedCanvasGroup.alpha=p; float off=Mathf.Lerp(GhostMaxOffset,0f,p); _duckHuntedGhost1Rect.anchoredPosition=pos+new Vector2(off,0); _duckHuntedGhost2Rect.anchoredPosition=pos+new Vector2(-off,0); float a = Mathf.Lerp(0.5f,0f,p); _duckHuntedGhostText1.color=new Color(1f,0.85f,0.6f,a); _duckHuntedGhostText2.color=new Color(1f,0.85f,0.6f,a); yield return null; } _duckHuntedCanvasGroup.alpha=1; _duckHuntedGhost1Rect.anchoredPosition=pos; _duckHuntedGhost2Rect.anchoredPosition=pos; _duckHuntedGhostText1.color=new Color(1,0.85f,0.6f,0); yield return new WaitForSeconds(GhostHoldTime); t=0; while(t<FadeOutTime) { t+=Time.deltaTime; _duckHuntedCanvasGroup.alpha=Mathf.Lerp(1f,0f,t/FadeOutTime); yield return null; } _duckHuntedOverlay.SetActive(false); }
        private void TryPlaySound() { try { string p=Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)??"", "Audio", "BossDefeated.mp3"); if(File.Exists(p)) AudioManager.PostCustomSFX(p,null,false); } catch{} }
    }
}