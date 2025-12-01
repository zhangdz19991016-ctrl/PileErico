using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq; 
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement; 
using Duckov.Modding;
using Duckov.UI; 
using Duckov.UI.BarDisplays;
using ItemStatsSystem;
using Duckov; 

namespace PileErico
{
    public class UIManager : MonoBehaviour
    {
        // ───── 1. UI 对象封装 ─────
        private class StatBar
        {
            public RectTransform Root = null!; 
            public Image? FillImage;
            public Image? SecondaryFillImage; 
            public Image? BgImage;      
            public Image? BorderImage;  
            public Text? ValueText;            

            public void SetActive(bool active)
            {
                if (Root != null && Root.gameObject.activeSelf != active)
                    Root.gameObject.SetActive(active);
            }
        }

        private GameObject? _canvasRoot;
        
        private StatBar? _hpBar;
        private StatBar? _staminaBar;
        private StatBar? _expBar;
        
        private RectTransform? _statsRoot;
        private Image? _hungerFill;
        private Image? _thirstFill;

        // ───── 2. 核心变量 ─────
        private CharacterMainControl? _player;
        private Health? _playerHealth; 
        
        // 性能优化：缓存变量（脏标记）
        private float _lastHealth = -1f;
        private float _lastMaxHealth = -1f;
        private float _lastStamina = -1f;
        private float _lastMaxStamina = -1f;
        private float _lastHunger = -1f;
        private float _lastThirst = -1f;
        private long _lastExp = -1;
        private float _secondaryHpFill = 1f; 

        private float _checkTimer = 0f;
        private const float CHIP_SPEED = 0.5f; 

        // 官方UI缓存列表 
        private List<CanvasGroup> _cachedOfficialUI = new List<CanvasGroup>();
        private bool _hasCachedOfficialUI = false;

        // 武器槽缓存
        private List<Transform> _cachedIndividualSlots = new List<Transform>();

        // 弹药栏缓存
        private BulletCountHUD? _bulletCountHUD;
        private RectTransform? _bulletCountRect;

        // 快捷栏相关
        private ItemShortcutPanel? _shortcutPanel;
        private RectTransform? _shortcutPanelRect;

        [SerializeField]
        private Vector2 _shortcutCenterOffset = new Vector2(-30f, 15f);

        // ───── 3. 配置参数 ─────
        [Header("UI Layout")]
        private float _statsBarWidth = 520f;  
        private float _statsBarHeight = 15f;  
        private float _statsOffsetY = 130f; 
        private float _statsOffsetX = 0f;

        // [配置] 1点血 = 6像素宽
        private const float HP_PIXELS_PER_POINT = 6f;       
        private const float STAMINA_PIXELS_PER_POINT = 3f;  
        private const float MAX_BAR_WIDTH = 2000f;          

        private readonly Color _colBorder = new Color(0.733f, 0.867f, 0.98f, 1f); 
        private readonly Color _colBackground = new Color(0.506f, 0.576f, 0.639f, 1f);

        private readonly Vector2[] _weaponTargetPositions = new Vector2[]
        {
            new Vector2(240, 180),    // Slot 1
            new Vector2(400, 180),    // Slot 2
            new Vector2(340.5f, 60)   // Slot V
        };

        private readonly Vector2 _bulletHudTargetPos = new Vector2(335f, 135f);

        // ───── 生命周期与场景管理 ─────

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSceneChanged(Scene current, Scene next)
        {
            _cachedIndividualSlots.Clear();
            _cachedOfficialUI.Clear();
            _hasCachedOfficialUI = false;
            _player = null;
            _playerHealth = null;
            _shortcutPanel = null;
            _shortcutPanelRect = null;
            _bulletCountHUD = null;
            _bulletCountRect = null;
            
            _lastHealth = -1; _lastMaxHealth = -1;
            _lastStamina = -1; _lastMaxStamina = -1;
        }

        private void Start()
        {
            InitializeCanvas();
            _hpBar = CreateStatBar("HP_Bar", new Vector2(20, -155), new Vector2(100, 15), 
                new Color(0.65f, 0.1f, 0.1f, 1f), hasSecondaryFill: true);
            _staminaBar = CreateStatBar("Stamina_Bar", new Vector2(20, -173), new Vector2(100, 15), 
                new Color(0.1f, 0.6f, 0.25f, 1f));

            CreateExpBar(); 
            CreateDualStatsBar(); 

            ModBehaviour.LogToFile("[UIManager] Optimized UI System Initialized");
        }

        private void OnDestroy()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);
        }

        private void Update()
        {
            // 1. 玩家有效性检查
            if (!ValidatePlayerReference()) return;

            // =========================================================
            // 2. [修改] UI 显示/隐藏逻辑
            // =========================================================
            
            // A. 全局隐藏：如果有菜单/背包打开，隐藏整个 Canvas
            if (View.ActiveView != null)
            {
                if (_canvasRoot != null && _canvasRoot.activeSelf) 
                    _canvasRoot.SetActive(false);
                return; // 直接返回，节省性能
            }
            else
            {
                if (_canvasRoot != null && !_canvasRoot.activeSelf) 
                    _canvasRoot.SetActive(true);
            }

            // B. 部分隐藏：计算“是否需要隐藏血条和体力条”
            bool hideBars = false;

            // 条件1：正在对话 (DialogueUI系统)
            if (Dialogues.DialogueUI.Active) hideBars = true;

            // 条件2：持枪 + 开镜瞄准 + 特定倍镜
            // 判定时间 > 0.4f
            if (!hideBars && _player != null)
            {
                // 先判断瞄准进度，减少不必要的 GetComponent 调用
                if (_player.AdsValue > 0.4f) 
                {
                    var currentGun = _player.GetGun(); // 获取当前持有的枪械
                    if (currentGun != null && currentGun.Item != null)
                    {
                        // 获取 "Scope" 槽位的内容
                        var scopeSlot = currentGun.Item.Slots.GetSlot("Scope");
                        if (scopeSlot != null && scopeSlot.Content != null)
                        {
                            int scopeID = scopeSlot.Content.TypeID;
                            // 检查是否为 4倍镜(568) 或 8倍镜(569)
                            if (scopeID == 568 || scopeID == 569)
                            {
                                hideBars = true;
                            }
                        }
                    }
                }
            }
            // =========================================================

            // 3. 数据更新逻辑 (传入 hideBars 参数控制显隐)
            UpdateHealthLogic(hideBars);
            UpdateStaminaLogic(hideBars);
            
            // 其他UI不隐藏
            UpdateExpLogic(); 
            UpdateStatsLogic(); 

            // 4. 布局维护
            ApplyWeaponSlotsPosition();
            MaintainShortcutPanel();
            MaintainBulletCountHUD(); 

            // 5. 官方UI隐藏逻辑
            _checkTimer += Time.deltaTime;
            if (_checkTimer > 2.0f) 
            {
                FindOfficialUIReferences();
                _checkTimer = 0f;
            }
            HideOfficialUI();
        }

        // ───── 核心逻辑 (优化版) ─────

        private bool ValidatePlayerReference()
        {
            if (_player == null || _playerHealth == null)
            {
                _player = CharacterMainControl.Main;
                if (_player != null) _playerHealth = _player.Health;
                
                if (_canvasRoot != null) _canvasRoot.SetActive(false);
                return false;
            }

            if (!_player.gameObject.activeInHierarchy)
            {
                if (_canvasRoot != null && _canvasRoot.activeSelf) _canvasRoot.SetActive(false);
                return false;
            }

            return true;
        }

        // [修改] 增加 hide 参数控制显示
        private void UpdateHealthLogic(bool hide)
        {
            var bar = _hpBar; 
            if (bar == null) return;

            // 如果需要隐藏，直接设置不可见并返回
            if (hide)
            {
                bar.SetActive(false);
                return;
            }
            bar.SetActive(true); // 确保可见

            if (_playerHealth == null) return;

            float current = _playerHealth.CurrentHealth;
            float max = _playerHealth.MaxHealth;
            if (max <= 0) max = 1;

            float targetWidth = Mathf.Clamp(max * HP_PIXELS_PER_POINT, 10f, MAX_BAR_WIDTH);
            float pct = Mathf.Clamp01(current / max);

            if (Mathf.Abs(max - _lastMaxHealth) > 0.1f)
            {
                bar.Root.sizeDelta = new Vector2(targetWidth, 15f);
                if (bar.BorderImage != null) bar.BorderImage.rectTransform.sizeDelta = new Vector2(targetWidth + 4f, 19f);
                if (bar.BgImage != null) bar.BgImage.rectTransform.sizeDelta = new Vector2(targetWidth, 15f);
                if (bar.FillImage != null) bar.FillImage.rectTransform.sizeDelta = new Vector2(targetWidth, 15f);
                if (bar.SecondaryFillImage != null) bar.SecondaryFillImage.rectTransform.sizeDelta = new Vector2(targetWidth, 15f);
                _lastMaxHealth = max;
            }

            if (Mathf.Abs(current - _lastHealth) > 0.1f)
            {
                if (bar.ValueText != null) 
                    bar.ValueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
                
                if (bar.FillImage != null) bar.FillImage.fillAmount = pct;
                
                if (pct < _secondaryHpFill)
                {
                    // 下降时标记
                }
                else
                {
                    _secondaryHpFill = pct;
                    if (bar.SecondaryFillImage != null) bar.SecondaryFillImage.fillAmount = pct;
                }
                _lastHealth = current;
            }

            if (bar.SecondaryFillImage != null && _secondaryHpFill > pct)
            {
                _secondaryHpFill -= Time.deltaTime * CHIP_SPEED;
                if (_secondaryHpFill < pct) _secondaryHpFill = pct;
                bar.SecondaryFillImage.fillAmount = _secondaryHpFill;
            }
        }

        // [修改] 增加 hide 参数控制显示
        private void UpdateStaminaLogic(bool hide)
        {
            var bar = _staminaBar;
            if (bar == null) return;

            if (hide)
            {
                bar.SetActive(false);
                return;
            }
            bar.SetActive(true);

            if (_player == null) return;

            float current = _player.CurrentStamina;
            float max = _player.MaxStamina;
            if (max <= 0) max = 1;

            float targetWidth = Mathf.Clamp(max * STAMINA_PIXELS_PER_POINT, 10f, MAX_BAR_WIDTH);
            float pct = Mathf.Clamp01(current / max);

            if (Mathf.Abs(max - _lastMaxStamina) > 0.1f)
            {
                bar.Root.sizeDelta = new Vector2(targetWidth, 15f);
                if (bar.BorderImage != null) bar.BorderImage.rectTransform.sizeDelta = new Vector2(targetWidth + 4f, 19f);
                if (bar.BgImage != null) bar.BgImage.rectTransform.sizeDelta = new Vector2(targetWidth, 15f);
                if (bar.FillImage != null) bar.FillImage.rectTransform.sizeDelta = new Vector2(targetWidth, 15f);
                _lastMaxStamina = max;
            }

            if (Mathf.Abs(current - _lastStamina) > 0.1f)
            {
                if (bar.ValueText != null) 
                    bar.ValueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
                _lastStamina = current;
            }

            if (bar.FillImage != null)
            {
                bar.FillImage.fillAmount = Mathf.Lerp(bar.FillImage.fillAmount, pct, Time.deltaTime * 15f);
            }
        }

        private void UpdateExpLogic()
        {
            if (_expBar == null || EXPManager.Instance == null) return;

            long currentExp = EXPManager.EXP;
            
            if (currentExp != _lastExp)
            {
                int level = EXPManager.Instance.LevelFromExp(currentExp);
                var range = EXPManager.Instance.GetLevelExpRange(level);
                long start = range.Item1;
                long next = range.Item2;

                if (next == long.MaxValue)
                {
                    if(_expBar.FillImage != null) _expBar.FillImage.fillAmount = 1f;
                    if(_expBar.ValueText != null) _expBar.ValueText.text = $"Lv.{level} (MAX)";
                }
                else
                {
                    long needed = next - start;
                    long progress = currentExp - start;
                    float pct = (needed > 0) ? (float)((double)progress / needed) : 0f;
                    
                    if(_expBar.FillImage != null) _expBar.FillImage.fillAmount = pct;
                    if(_expBar.ValueText != null) _expBar.ValueText.text = $"Lv.{level}  {progress}/{needed} ({pct*100:F1}%)";
                }
                _lastExp = currentExp;
            }
        }

        private void UpdateStatsLogic()
        {
            if (_statsRoot == null || _player == null) return;

            float h = _player.CurrentEnergy;
            float w = _player.CurrentWater;

            if (Mathf.Abs(h - _lastHunger) > 0.5f)
            {
                if (_hungerFill != null)
                    _hungerFill.fillAmount = _player.MaxEnergy > 0 ? Mathf.Clamp01(h / _player.MaxEnergy) : 0;
                _lastHunger = h;
            }

            if (Mathf.Abs(w - _lastThirst) > 0.5f)
            {
                if (_thirstFill != null)
                    _thirstFill.fillAmount = _player.MaxWater > 0 ? Mathf.Clamp01(w / _player.MaxWater) : 0;
                _lastThirst = w;
            }
            
            if (Vector2.Distance(_statsRoot.anchoredPosition, new Vector2(_statsOffsetX, _statsOffsetY)) > 0.1f)
                _statsRoot.anchoredPosition = new Vector2(_statsOffsetX, _statsOffsetY);
        }

        // ───── UI 构建 (保持原样) ─────

        private void InitializeCanvas()
        {
            _canvasRoot = new GameObject("Mod_UIManager_Canvas");
            DontDestroyOnLoad(_canvasRoot);
            
            Canvas cv = _canvasRoot.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 100;
            
            CanvasScaler cs = _canvasRoot.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            
            _canvasRoot.AddComponent<GraphicRaycaster>();
        }

        private StatBar CreateStatBar(string name, Vector2 pos, Vector2 size, Color fillColor, bool hasSecondaryFill = false)
        {
            StatBar bar = new StatBar();
            if (_canvasRoot == null) InitializeCanvas();

            GameObject rootObj = new GameObject(name);
            rootObj.transform.SetParent(_canvasRoot!.transform, false); 
            bar.Root = rootObj.AddComponent<RectTransform>();
            bar.Root.anchorMin = new Vector2(0, 1);
            bar.Root.anchorMax = new Vector2(0, 1);
            bar.Root.pivot = new Vector2(0, 1);
            bar.Root.anchoredPosition = pos;
            bar.Root.sizeDelta = size;

            bar.BorderImage = CreateImage(rootObj, _colBorder, new Vector2(size.x + 4, size.y + 4));
            bar.BgImage = CreateImage(rootObj, _colBackground, size);

            if (hasSecondaryFill)
            {
                bar.SecondaryFillImage = CreateImage(rootObj, new Color(0.8f, 0.7f, 0.2f, 1f), size);
                SetFillMode(bar.SecondaryFillImage);
            }

            bar.FillImage = CreateImage(rootObj, fillColor, size);
            SetFillMode(bar.FillImage);
            bar.ValueText = CreateText(rootObj, 14, new Vector2(0, 1));

            return bar;
        }

        private void CreateDualStatsBar()
        {
            if (_canvasRoot == null) InitializeCanvas();

            GameObject rootObj = new GameObject("Stats_Dual_Bar");
            rootObj.transform.SetParent(_canvasRoot!.transform, false);
            _statsRoot = rootObj.AddComponent<RectTransform>();
            _statsRoot.anchorMin = new Vector2(0.5f, 0f); 
            _statsRoot.anchorMax = new Vector2(0.5f, 0f);
            _statsRoot.pivot = new Vector2(0.5f, 0f);
            _statsRoot.anchoredPosition = new Vector2(_statsOffsetX, _statsOffsetY);
            _statsRoot.sizeDelta = new Vector2(_statsBarWidth, _statsBarHeight);

            Image border = CreateImage(rootObj, _colBorder, Vector2.zero);
            border.rectTransform.anchorMin = Vector2.zero;
            border.rectTransform.anchorMax = Vector2.one;
            border.rectTransform.offsetMin = new Vector2(-2, -2);
            border.rectTransform.offsetMax = new Vector2(2, 2);

            Image bg = CreateImage(rootObj, _colBackground, Vector2.zero);
            SetRectFull(bg.rectTransform);

            GameObject left = new GameObject("Hunger");
            left.transform.SetParent(rootObj.transform, false);
            RectTransform lr = left.AddComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = new Vector2(0.5f, 1f); 
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            
            _hungerFill = CreateImage(left, new Color(1f, 0.6f, 0f, 1f), Vector2.zero);
            SetFillMode(_hungerFill);
            if (_hungerFill != null) _hungerFill.fillOrigin = (int)Image.OriginHorizontal.Right; 
            SetRectFull(_hungerFill!.rectTransform);
            CreateTicks(lr, true);

            GameObject right = new GameObject("Thirst");
            right.transform.SetParent(rootObj.transform, false);
            RectTransform rr = right.AddComponent<RectTransform>();
            rr.anchorMin = new Vector2(0.5f, 0f); 
            rr.anchorMax = Vector2.one;
            rr.offsetMin = rr.offsetMax = Vector2.zero;

            _thirstFill = CreateImage(right, new Color(0f, 0.7f, 1f, 1f), Vector2.zero);
            SetFillMode(_thirstFill); 
            SetRectFull(_thirstFill!.rectTransform);
            CreateTicks(rr, false);

            Image centerLine = CreateImage(rootObj, _colBorder, new Vector2(2, _statsBarHeight + 2)); 
            centerLine.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            centerLine.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            centerLine.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void CreateTicks(RectTransform parent, bool isReversed)
        {
            float[] percentages = new float[] { 0.2f, 0.4f, 0.6f, 0.8f };
            Color tickColor = new Color(0f, 0f, 0f, 0.3f); 

            foreach (float p in percentages)
            {
                GameObject tick = new GameObject("Tick");
                tick.transform.SetParent(parent, false);
                Image img = tick.AddComponent<Image>();
                img.color = tickColor;
                RectTransform rt = img.rectTransform;
                rt.anchorMin = new Vector2(p, 0);
                rt.anchorMax = new Vector2(p, 1);
                rt.offsetMin = new Vector2(-0.5f, 0); 
                rt.offsetMax = new Vector2(0.5f, 0);
            }
        }

        private void CreateExpBar()
        {
            if (_canvasRoot == null) InitializeCanvas();

            _expBar = new StatBar();
            GameObject obj = new GameObject("Exp_Bar");
            obj.transform.SetParent(_canvasRoot!.transform, false);
            _expBar.Root = obj.AddComponent<RectTransform>();
            _expBar.Root.anchorMin = new Vector2(0, 0);
            _expBar.Root.anchorMax = new Vector2(1, 0);
            _expBar.Root.pivot = new Vector2(0.5f, 0);
            _expBar.Root.sizeDelta = new Vector2(0, 5); 
            _expBar.Root.anchoredPosition = Vector2.zero;

            _expBar.BgImage = CreateImage(obj, new Color(0.7f, 0.7f, 0.7f, 0.6f), Vector2.zero);
            SetRectFull(_expBar.BgImage!.rectTransform);

            _expBar.FillImage = CreateImage(obj, new Color(1f, 0.95f, 0.5f, 0.9f), Vector2.zero);
            SetFillMode(_expBar.FillImage);
            SetRectFull(_expBar.FillImage!.rectTransform);

            _expBar.ValueText = CreateText(obj, 12, new Vector2(0, 8));
            _expBar.ValueText.alignment = TextAnchor.MiddleCenter;
            
            RectTransform tr = _expBar.ValueText.rectTransform;
            tr.anchorMin = Vector2.zero; 
            tr.anchorMax = new Vector2(1, 0);
            tr.sizeDelta = new Vector2(0, 20);
        }

        private Image CreateImage(GameObject parent, Color color, Vector2 size)
        {
            GameObject go = new GameObject("Img");
            go.transform.SetParent(parent.transform, false);
            Image img = go.AddComponent<Image>();
            
            if (img.sprite == null)
            {
                Texture2D t = new Texture2D(1, 1);
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                img.sprite = Sprite.Create(t, new Rect(0, 0, 1, 1), Vector2.zero);
            }
            
            img.color = color;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (size != Vector2.zero) rt.sizeDelta = size;
            return img;
        }

        private Text CreateText(GameObject parent, int fontSize, Vector2 offset)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent.transform, false);
            Text txt = go.AddComponent<Text>();
            
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            txt.font = font;
            
            txt.fontSize = fontSize;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            
            Outline ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0,0,0,0.8f);
            ol.effectDistance = new Vector2(1, -1);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = offset;
            return txt;
        }

        private void SetFillMode(Image? img)
        {
            if (img == null) return;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        private void SetRectFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ───── 原生UI操控逻辑 (高度优化) ─────

        private void ApplyWeaponSlotsPosition()
        {
            _cachedIndividualSlots.RemoveAll(x => x == null);

            if (_cachedIndividualSlots.Count == 0)
            {
                var slots = FindObjectsOfType<WeaponButton>();
                if (slots != null && slots.Length > 0)
                {
                    _cachedIndividualSlots = slots.OrderBy(s => s.transform.GetSiblingIndex())
                                                  .Select(s => s.transform).ToList();
                    
                    foreach (var t in _cachedIndividualSlots) UnbindLayout(t, true);
                }
            }

            for (int i = 0; i < _cachedIndividualSlots.Count; i++)
            {
                Transform t = _cachedIndividualSlots[i];
                if (t != null && i < _weaponTargetPositions.Length)
                {
                    RectTransform rt = t.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.zero;
                    rt.pivot = Vector2.zero; 
                    
                    if (Vector2.Distance(rt.anchoredPosition, _weaponTargetPositions[i]) > 0.5f)
                        rt.anchoredPosition = _weaponTargetPositions[i];
                }
            }
        }

        private void MaintainShortcutPanel()
        {
            if (_shortcutPanel == null || _shortcutPanelRect == null)
            {
                _shortcutPanel = FindObjectOfType<ItemShortcutPanel>();
                if (_shortcutPanel == null) return;

                _shortcutPanelRect = _shortcutPanel.GetComponent<RectTransform>();
                if (_shortcutPanelRect == null) return;

                UnbindLayout(_shortcutPanelRect.transform, true);
            }

            var rt = _shortcutPanelRect;
            if (rt == null) return;

            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);

            if (Vector2.Distance(rt.anchoredPosition, _shortcutCenterOffset) > 0.1f)
                rt.anchoredPosition = _shortcutCenterOffset;
        }

        private void MaintainBulletCountHUD()
        {
            if (_bulletCountHUD == null || _bulletCountRect == null)
            {
                // 尝试查找场景中的弹药栏
                _bulletCountHUD = FindObjectOfType<BulletCountHUD>();
                if (_bulletCountHUD == null) return;

                _bulletCountRect = _bulletCountHUD.GetComponent<RectTransform>();
                if (_bulletCountRect == null) return;

                UnbindLayout(_bulletCountRect.transform, true);
            }

            var rt = _bulletCountRect;
            if (rt == null) return;

            // 强制将锚点设为左下角 (与武器槽一致)，然后设置固定位置
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;

            // _bulletHudTargetPos 为 (335, 135)
            if (Vector2.Distance(rt.anchoredPosition, _bulletHudTargetPos) > 0.1f)
            {
                rt.anchoredPosition = _bulletHudTargetPos;
            }
        }

        // 1. 查找并缓存官方 UI (低频调用)
        private void FindOfficialUIReferences()
        {
            if (_hasCachedOfficialUI) return; // 已经缓存过了就不再大规模查找，除非重置

            // 辅助函数：查找并添加到缓存列表
            void Cache<T>() where T : MonoBehaviour 
            {
                var objs = FindObjectsOfType<T>();
                foreach(var obj in objs)
                {
                    if (obj == null) continue;
                    CanvasGroup cg = obj.GetComponent<CanvasGroup>();
                    if (cg == null) cg = obj.gameObject.AddComponent<CanvasGroup>();
                    if (!_cachedOfficialUI.Contains(cg)) _cachedOfficialUI.Add(cg);
                }
            }

            // 缓存所有需要隐藏的类型
            Cache<StaminaHUD>();
            Cache<HealthHUD>();
            Cache<EnergyHUD>();
            Cache<WaterHUD>();
            Cache<BarDisplayController>(); 

            // 特殊处理 BuffDisplay
            var buffDisplay = FindObjectOfType<BuffsDisplay>();
            if (buffDisplay != null)
            {
                if (Math.Abs(buffDisplay.transform.localScale.x - 1.1f) > 0.01f)
                {
                    buffDisplay.transform.localScale = Vector3.one * 1.1f;
                    UnbindLayout(buffDisplay.transform, false); 
                }
            }

            if (_cachedOfficialUI.Count > 0) _hasCachedOfficialUI = true;
        }

        // 2. 对缓存的 UI 进行隐藏 (高频调用，开销极小)
        private void HideOfficialUI()
        {
            for (int i = _cachedOfficialUI.Count - 1; i >= 0; i--)
            {
                var cg = _cachedOfficialUI[i];
                if (cg == null) 
                {
                    _cachedOfficialUI.RemoveAt(i);
                    continue;
                }

                // 只有当属性不对时才赋值，避免 Dirty
                if (cg.alpha != 0f) cg.alpha = 0f;
                if (cg.blocksRaycasts) cg.blocksRaycasts = false;
                if (cg.interactable) cg.interactable = false;
            }
        }

        private void UnbindLayout(Transform target, bool reparentToCanvas)
        {
            var le = target.GetComponent<LayoutElement>();
            if (le == null) le = target.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            if (target.parent != null)
            {
                var group = target.parent.GetComponent<LayoutGroup>();
                if (group != null) group.enabled = false;
            }

            if (reparentToCanvas)
            {
                var rootCanvas = target.GetComponentInParent<Canvas>();
                if (rootCanvas != null && target.parent != rootCanvas.transform)
                {
                    target.SetParent(rootCanvas.transform, true);
                }
            }
        }
    }
}