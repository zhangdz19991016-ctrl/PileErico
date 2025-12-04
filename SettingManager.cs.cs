using UnityEngine;

namespace PileErico
{
    public class SettingManager : MonoBehaviour
    {
        // =========================================================
        // Part 1: 配置存储核心 (Static)
        // 其他脚本直接通过 SettingManager.EnableXXX 访问，无需引用实例
        // =========================================================
        
        private const string KEY_PREFIX = "PileErico_Config_";

        // 辅助方法：读取 PlayerPrefs (1=true, 0=false)
        private static bool GetBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(KEY_PREFIX + key, defaultValue ? 1 : 0) == 1;
        }

        // 辅助方法：写入 PlayerPrefs 并立即保存
        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(KEY_PREFIX + key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        // --- 配置项属性 ---

        public static bool EnableBossHealthBar
        {
            get => GetBool("EnableBossHealthBar", true);
            set => SetBool("EnableBossHealthBar", value);
        }

        public static bool EnableInvasion
        {
            get => GetBool("EnableInvasion", true);
            set => SetBool("EnableInvasion", value);
        }

        public static bool EnableBossMusic
        {
            get => GetBool("EnableBossMusic", true);
            set => SetBool("EnableBossMusic", value);
        }

        public static bool EnableSoulsLikeUI
        {
            get => GetBool("EnableSoulsLikeUI", true);
            set => SetBool("EnableSoulsLikeUI", value);
        }

        public static bool EnableSlotExpandedManager
        {
            get => GetBool("EnableSlotExpandedManager", true);
            set => SetBool("EnableSlotExpandedManager", value);
        }

        public static bool EnableSkillTree
        {
            get => GetBool("EnableSkillTree", true);
            set => SetBool("EnableSkillTree", value);
        }

        public static bool EnableBossEnhancement
        {
            get => GetBool("EnableBossEnhancement", true);
            set => SetBool("EnableBossEnhancement", value);
        }

        // =========================================================
        // Part 2: UI 界面逻辑 (Instance)
        // 负责监听按键和绘制窗口
        // =========================================================

        private bool _showMenu = false;
        private Rect _windowRect = new Rect(20, 20, 350, 520); // 窗口位置和大小

        void Update()
        {
            // 快捷键: Ctrl + F12
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.F12))
            {
                _showMenu = !_showMenu;
            }
        }

        void OnGUI()
        {
            if (!_showMenu) return;

            // 设置字体大小
            GUI.skin.window.fontSize = 14;
            GUI.skin.label.fontSize = 13;
            GUI.skin.toggle.fontSize = 13;
            GUI.skin.button.fontSize = 13;

            // 绘制窗口
            _windowRect = GUI.Window(19991016, _windowRect, DrawWindowContent, "鸭科夫 Mod 设置 (PileErico)");
        }

        void DrawWindowContent(int windowID)
        {
            GUILayout.BeginVertical();
            
            GUILayout.Space(10);
            GUILayout.Label("按住 [Ctrl + F12] 打开/关闭此菜单", GUI.skin.label);
            GUILayout.Label("<color=yellow>注意：修改后需重启游戏或重进存档生效</color>", GUI.skin.label);
            GUILayout.Space(15);

            // 1. Boss 血条
            bool newHealth = GUILayout.Toggle(EnableBossHealthBar, " 开启 Boss 血条");
            if (newHealth != EnableBossHealthBar) EnableBossHealthBar = newHealth;
            GUILayout.Label("   <color=grey>Boss血条和击杀提示</color>");
            GUILayout.Space(5);

            // 2. Boss 音乐
            bool newMusic = GUILayout.Toggle(EnableBossMusic, " 开启 Boss 音乐");
            if (newMusic != EnableBossMusic) EnableBossMusic = newMusic;
            GUILayout.Label("   <color=grey>建议开启/玩Scav模式建议关闭</color>");
            GUILayout.Space(5);

            // 3. 入侵事件
            bool newInvasion = GUILayout.Toggle(EnableInvasion, " 开启入侵事件");
            if (newInvasion != EnableInvasion) EnableInvasion = newInvasion;
            GUILayout.Space(5);

            // 4. 魂类 UI
            bool newUI = GUILayout.Toggle(EnableSoulsLikeUI, " 开启魂类 UI");
            if (newUI != EnableSoulsLikeUI) EnableSoulsLikeUI = newUI;
            GUILayout.Space(5);

            // 5. 技能树
            bool newSkill = GUILayout.Toggle(EnableSkillTree, " 开启灵魂升华技能树");
            if (newSkill != EnableSkillTree) EnableSkillTree = newSkill;
            GUILayout.Label("   <color=red>警告: 关闭可能导致洗点</color>");
            GUILayout.Space(5);

            // 6. Boss 强化
            bool newEnhance = GUILayout.Toggle(EnableBossEnhancement, " 开启 Boss 强化");
            if (newEnhance != EnableBossEnhancement) EnableBossEnhancement = newEnhance;
            GUILayout.Label("   <color=grey>关闭则 Boss 恢复原版数值</color>");
            GUILayout.Space(5);

            // 7. 槽位扩展
            bool newSlot = GUILayout.Toggle(EnableSlotExpandedManager, " 开启槽位扩展");
            if (newSlot != EnableSlotExpandedManager) EnableSlotExpandedManager = newSlot;
            GUILayout.Label("   <color=grey>不建议关闭</color>");

            GUILayout.FlexibleSpace(); // 弹簧，把按钮顶到底部
            
            if (GUILayout.Button("关闭菜单", GUILayout.Height(30)))
            {
                _showMenu = false;
            }

            GUILayout.EndVertical();
            
            // 允许拖动窗口
            GUI.DragWindow();
        }
    }
}