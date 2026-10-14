using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Engine;
using GameEntitySystem;
using HarmonyLib;

namespace Game {
    /// <summary>
    ///     语音提示核心：负责提示拦截、文本反查键位、冷却去重与设置，本身不含音频。
    ///     语音由「音频包」提供：PackageName 以 voice.prompt.pack. 开头的纯资源模组，
    ///     音频位于 Assets/Audio/Voice/&lt;包id&gt;/ 下，命名规则「语言段名_键号[_部位].ogg」，
    ///     例如 neuro 包的 Assets/Audio/Voice/neuro/ComponentVitalStats_12.ogg。
    ///     文本 → 逻辑键名 这层映射只由语言包决定，与具体音频包无关；
    ///     播放时拼上当前选中音频包的目录，缺失的音频由 AudioManager 静默忽略。
    ///     兼容说明：不依赖 ModLoader 钩子（DisplaySmallMessage 钩子是 1.9.4+ 才有的），
    ///     全部通过 Harmony 补丁 ComponentGui/ComponentScreenOverlays 实现，1.9.3 起可用。
    /// </summary>
    public class VoicePromptCoreLoader : ModLoader {
        public const string CorePackage = "VoicePrompt.core";
        public const string PackPrefix = "voice.prompt.pack.";
        public const string VoiceRoot = "Audio/Voice";
        public const string SettingsPageId = "VoicePrompt";
        public const float DefaultGlobalGap = 1f; // 任意两句语音之间的最小间隔（秒）

        /// <summary>一个已安装的语音音频包（纯资源模组）。</summary>
        public class VoicePack {
            public readonly string PackageName;
            public readonly string DisplayName;
            public readonly string AudioDir;   // ContentManager 内的音频目录，如 Audio/Voice/neuro
            public readonly int VoiceCount;
            public readonly int LoadOrder;

            public VoicePack(string packageName, string displayName, string audioDir, int voiceCount, int loadOrder) {
                PackageName = packageName;
                DisplayName = displayName;
                AudioDir = audioDir;
                VoiceCount = voiceCount;
                LoadOrder = loadOrder;
            }
        }

        /// <summary>选择对话框 / 设置控件里的一行。</summary>
        public class PackChoice {
            public string Label;
            public string PackageName; // null = 自动（按加载顺序取优先级最高的包）
        }

        // 语音注册表：文案模板 → 逻辑资源名。只有能走到提示通道的键才收录。
        // (语言段名, 键号, 部位后缀 or null)
        static readonly (string section, string key, string slot)[] Entries = [
            // 饥饿 / 进食 / 体力 / 睡眠 / 体温 / 湿度
            ("ComponentVitalStats", "1", null), ("ComponentVitalStats", "2", null),
            ("ComponentVitalStats", "3", null), ("ComponentVitalStats", "4", null),
            ("ComponentVitalStats", "5", null), ("ComponentVitalStats", "6", null),
            ("ComponentVitalStats", "7", null), ("ComponentVitalStats", "8", null),
            ("ComponentVitalStats", "10", null), ("ComponentVitalStats", "11", null),
            ("ComponentVitalStats", "12", null), ("ComponentVitalStats", "13", null),
            ("ComponentVitalStats", "14", null), ("ComponentVitalStats", "15", null),
            ("ComponentVitalStats", "17", null), ("ComponentVitalStats", "18", null),
            ("ComponentVitalStats", "19", null), ("ComponentVitalStats", "20", null),
            ("ComponentVitalStats", "21", null), ("ComponentVitalStats", "22", null),
            ("ComponentVitalStats", "23", null), ("ComponentVitalStats", "24", null),
            ("ComponentVitalStats", "33", null), ("ComponentVitalStats", "34", null),
            ("ComponentVitalStats", "36", null), ("ComponentVitalStats", "37", null),
            ("ComponentVitalStats", "38", null), ("ComponentVitalStats", "39", null),
            ("ComponentVitalStats", "40", null),
            // 寒冷部位提示 27~32 × 头/躯干/腿/脚（部位文案取自键 41~44）
            ("ComponentVitalStats", "27", "Head"), ("ComponentVitalStats", "27", "Torso"),
            ("ComponentVitalStats", "27", "Legs"), ("ComponentVitalStats", "27", "Feet"),
            ("ComponentVitalStats", "28", "Head"), ("ComponentVitalStats", "28", "Torso"),
            ("ComponentVitalStats", "28", "Legs"), ("ComponentVitalStats", "28", "Feet"),
            ("ComponentVitalStats", "29", "Head"), ("ComponentVitalStats", "29", "Torso"),
            ("ComponentVitalStats", "29", "Legs"), ("ComponentVitalStats", "29", "Feet"),
            ("ComponentVitalStats", "30", "Head"), ("ComponentVitalStats", "30", "Torso"),
            ("ComponentVitalStats", "30", "Legs"), ("ComponentVitalStats", "30", "Feet"),
            ("ComponentVitalStats", "31", "Head"), ("ComponentVitalStats", "31", "Torso"),
            ("ComponentVitalStats", "31", "Legs"), ("ComponentVitalStats", "31", "Feet"),
            ("ComponentVitalStats", "32", "Head"), ("ComponentVitalStats", "32", "Torso"),
            ("ComponentVitalStats", "32", "Legs"), ("ComponentVitalStats", "32", "Feet"),
            // 睡觉拒绝理由 / 睡醒 / 睡眠不足失去知觉
            ("ComponentSleep", "1", null), ("ComponentSleep", "2", null),
            ("ComponentSleep", "3", null), ("ComponentSleep", "4", null),
            ("ComponentSleep", "5", null), ("ComponentSleep", "6", null),
            ("ComponentSleep", "7", null), ("ComponentSleep", "9", null),
            // 感冒 / 恶心
            ("ComponentFlu", "1", null), ("ComponentFlu", "2", null), ("ComponentFlu", "3", null),
            ("ComponentSickness", "2", null),
            // 跑出世界边界的召唤
            ("ComponentHealth", "4", null),
            // 挖掘 / 攻击 / 等级提示
            ("ComponentMiner", "1", null), ("ComponentMiner", "2", null), ("ComponentMiner", "3", null),
            ("ComponentMiner", "5", null), ("ComponentMiner", "6", null), ("ComponentMiner", "7", null),
            ("ComponentMiner", "8", null), ("ComponentMiner", "9", null), ("ComponentMiner", "10", null),
            ("ComponentMiner", "11", null), ("ComponentMiner", "12", null),
            ("ComponentLocomotion", "1", null),
            ("ComponentPlayer", "1", null),
            ("ComponentLevel", "1", null),
            ("ComponentClothing", "1", null), ("ComponentClothing", "2", null),
            // 潜行/骑乘/飞行切换、截图、天气雾开关
            ("ComponentGui", "3", null), ("ComponentGui", "4", null), ("ComponentGui", "5", null),
            ("ComponentGui", "6", null), ("ComponentGui", "7", null), ("ComponentGui", "8", null),
            ("ComponentGui", "13", null), ("ComponentGui", "14", null),
            ("ComponentGui", "19", null), ("ComponentGui", "20", null), ("ComponentGui", "21", null),
            ("ComponentGui", "22", null), ("ComponentGui", "23", null),
            // 武器装填
            ("SubsystemBowBlockBehavior", "0", null),
            ("SubsystemMusketBlockBehavior", "0", null), ("SubsystemMusketBlockBehavior", "1", null),
            ("SubsystemCrossbowBlockBehavior", "0", null), ("SubsystemCrossbowBlockBehavior", "1", null),
            // 桶 / 船
            ("SubsystemBucketBlockBehavior", "1", null), ("SubsystemBucketBlockBehavior", "2", null),
            ("SubsystemBucketBlockBehavior", "3", null),
            ("SubsystemBoatBlockBehavior", "1", null),
            // 家具 / 孵蛋 / 配方引导
            ("SubsystemFurnitureBlockBehavior", "0", null), ("SubsystemFurnitureBlockBehavior", "1", null),
            ("SubsystemFurnitureBlockBehavior", "2", null), ("SubsystemFurnitureBlockBehavior", "3", null),
            ("SubsystemFurnitureBlockBehavior", "4", null), ("SubsystemFurnitureBlockBehavior", "5", null),
            ("SubsystemFurnitureBlockBehavior", "6", null),
            ("SubsystemEggBlockBehavior", "1", null),
            ("CRMessage", "0", null), ("CRMessage", "1", null),
            // 大字横幅（DisplayLargeMessage 通道）：开场剧情 / 跨年烟花
            ("ComponentIntro", "1", null), ("ComponentIntro", "2", null),
            ("ComponentIntro", "3", null), ("ComponentIntro", "4", null),
            ("SubsystemFireworksBlockBehavior", "1", null),
            ("SubsystemFireworksBlockBehavior", "2", null)
        ];

        // 部位后缀 → ComponentVitalStats 中的部位文案键（头部/躯干/腿部/脚部）
        static readonly Dictionary<string, string> SlotKeys = new() {
            ["Head"] = "41",
            ["Torso"] = "42",
            ["Legs"] = "43",
            ["Feet"] = "44"
        };

        static Dictionary<string, string> m_exactMap = [];       // 完整提示文本 → 逻辑资源名
        static List<KeyValuePair<Regex, string>> m_regexMap = []; // 带 {0} 参数的模板 → 逻辑资源名
        static string m_indexLanguage;                            // 索引构建时的语言，切语言后重建
        static readonly Dictionary<string, double> m_lastPlayTime = []; // 资源名 → 上次播放时刻（RealTime）
        static double m_lastAnyVoiceTime = -1000.0;

        // 已发现的语音包，按自动选择优先级排序（LoadOrder 大者优先）
        static readonly List<VoicePack> m_packs = [];
        static bool m_activeResolved;
        static VoicePack m_activePack;

        public static IReadOnlyList<VoicePack> Packs => m_packs;

        public override void __ModInitialize() {
            try {
                Harmony harmony = new(CorePackage);
                HarmonyMethod guiPostfix = new(typeof(VoicePromptCoreLoader), nameof(GuiSmallMessagePostfix));
                HarmonyMethod overlayPostfix = new(typeof(VoicePromptCoreLoader), nameof(OverlayMessagePostfix));
                HarmonyMethod largePostfix = new(typeof(VoicePromptCoreLoader), nameof(GuiLargeMessagePostfix));
                // ComponentGui.DisplaySmallMessage 的两个字符串重载（4 参数与带 fontScale 的 5 参数互不调用，各补一次不会重复触发）
                Patch(harmony, AccessTools.Method(
                        typeof(ComponentGui), nameof(ComponentGui.DisplaySmallMessage),
                        [typeof(string), typeof(Color), typeof(bool), typeof(bool)]), guiPostfix);
                Patch(harmony, AccessTools.Method(
                        typeof(ComponentGui), nameof(ComponentGui.DisplaySmallMessage),
                        [typeof(string), typeof(Color), typeof(bool), typeof(bool), typeof(float)]), guiPostfix);
                // 画面浮字（好困啊…/好热啊…）与 ScreenOverlays.Message（睡眠不足失去知觉）
                Patch(harmony, AccessTools.PropertySetter(typeof(ComponentScreenOverlays), nameof(ComponentScreenOverlays.FloatingMessage)), overlayPostfix);
                Patch(harmony, AccessTools.PropertySetter(typeof(ComponentScreenOverlays), nameof(ComponentScreenOverlays.Message)), overlayPostfix);
                // 大字横幅（开场剧情、跨年烟花）：largeText / smallText 都可能是提示文本
                Patch(harmony, AccessTools.Method(
                        typeof(ComponentGui), nameof(ComponentGui.DisplayLargeMessage),
                        [typeof(string), typeof(string), typeof(float), typeof(float)]), largePostfix);
                Log.Information("VoicePromptCore: Harmony patches applied. 语音提示核心补丁已挂接");
            }
            catch (Exception e) {
                Log.Error($"VoicePromptCore: init failed. 初始化失败：{e}");
            }
            DiscoverPacks();
        }

        static void Patch(Harmony harmony, System.Reflection.MethodBase method, HarmonyMethod postfix) {
            if (method != null) {
                harmony.Patch(method, postfix: postfix);
            }
            else {
                Log.Warning($"VoicePromptCore: patch target not found: {postfix.method?.Name}, skipped on this game version. 当前游戏版本缺少该补丁目标，已跳过");
            }
        }

        // ===== 语音包发现 / 选择 =====

        // 加载顺序保证：依赖检查先把 ModList 填完，之后才加载程序集，
        // 因此核心 __ModInitialize 时所有已启用的音频包都已可枚举。
        static void DiscoverPacks() {
            m_packs.Clear();
            InvalidateActivePack();
            foreach (ModEntity entity in ModsManager.ModList) {
                ModInfo info = entity.modInfo;
                if (info?.PackageName == null
                    || !info.PackageName.StartsWith(PackPrefix, StringComparison.Ordinal)) {
                    continue;
                }
                string id = SanitizeId(info.PackageName.Substring(PackPrefix.Length));
                if (string.IsNullOrEmpty(id)) {
                    Log.Warning($"VoicePromptCore: PackageName [{info.PackageName}] has empty/invalid pack id, skipped. 包名前缀后为空或非法，已跳过");
                    continue;
                }
                string zipPrefix = $"Assets/{VoiceRoot}/{id}/";
                int count = entity.ModFiles.Keys.Count(file => file.StartsWith(zipPrefix, StringComparison.OrdinalIgnoreCase));
                m_packs.Add(new VoicePack(
                    info.PackageName,
                    string.IsNullOrEmpty(info.Name) ? info.PackageName : info.Name,
                    $"{VoiceRoot}/{id}",
                    count,
                    info.LoadOrder));
            }
            // 自动选择的优先级：LoadOrder 大者优先（加载越晚优先级越高），同序保持发现顺序
            m_packs.Sort((a, b) => b.LoadOrder.CompareTo(a.LoadOrder));
            foreach (VoicePack pack in m_packs) {
                Log.Information($"VoicePromptCore: found voice pack [{pack.PackageName}] \"{pack.DisplayName}\", {pack.VoiceCount} audio files.");
            }
            if (m_packs.Count == 0) {
                Log.Warning($"VoicePromptCore: no voice packs found. 未发现语音包（需安装 PackageName 以 {PackPrefix} 开头、且声明依赖 {CorePackage} 的音频资源包）");
            }
        }

        // 包名前缀后的 id → 目录名，只保留文件系统与 ContentManager 都安全的字符
        static string SanitizeId(string id) {
            char[] chars = id.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++) {
                char c = chars[i];
                if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.') {
                    chars[i] = '_';
                }
            }
            return new string(chars).Trim('.');
        }

        // 当前生效的语音包：设置里选中的包；未选/失效时取优先级最高的包
        static VoicePack ActivePack() {
            if (!m_activeResolved) {
                m_activeResolved = true;
                if (ModSettingsManager.TryGet(out string stored, CorePackage, SettingsPageId, "VoicePack")
                    && !string.IsNullOrEmpty(stored)) {
                    m_activePack = m_packs.FirstOrDefault(p => p.PackageName == stored);
                    if (m_activePack == null) {
                        Log.Warning($"VoicePromptCore: selected voice pack [{stored}] is not installed, falling back to auto. 选中的语音包未安装，回退为自动选择");
                    }
                }
                m_activePack ??= m_packs.FirstOrDefault();
                if (m_activePack != null) {
                    Log.Information($"VoicePromptCore: active voice pack [{m_activePack.PackageName}] ({m_activePack.VoiceCount} audio files). 当前语音包");
                }
            }
            return m_activePack;
        }

        static void InvalidateActivePack() {
            m_activeResolved = false;
            m_activePack = null;
        }

        public override void OnModSettingChanged(string[] idPath, object value) {
            if (idPath != null
                && idPath.Length == 2
                && idPath[0] == SettingsPageId
                && idPath[1] == "VoicePack") {
                InvalidateActivePack();
            }
        }

        // 选择只在模组设置里进行（设置全局生效，所有世界共用）；未选择时按优先级自动取包
        public static PackChoice[] BuildSelectionChoices() {
            List<PackChoice> choices = [
                new() { Label = L("AutoChoice", "Auto (highest priority)"), PackageName = null }
            ];
            choices.AddRange(m_packs.Select(p => new PackChoice {
                Label = p.VoiceCount > 0
                    ? string.Format(L("VoiceCount", "{0} ({1} voice lines)"), p.DisplayName, p.VoiceCount)
                    : string.Format(L("NoAudio", "{0} (no audio)"), p.DisplayName),
                PackageName = p.PackageName
            }));
            return choices.ToArray();
        }

        // 供设置控件显示当前值
        public static string ChoiceLabel(string packageName) {
            if (!string.IsNullOrEmpty(packageName)) {
                VoicePack pack = m_packs.FirstOrDefault(p => p.PackageName == packageName);
                if (pack != null) {
                    return pack.DisplayName;
                }
            }
            return L("Auto", "Auto");
        }

        // 界面文案走语言包（Assets/Lang/en-US.json 兜底，其他语言可自行补充翻译文件）
        public static string L(string key, string fallback) =>
            LanguageControl.TryGet(out string s, "VoicePromptCore", key) ? s : fallback;

        // ===== 提示拦截与播放（Harmony 后置补丁） =====

        // 小字提示（饥饿、寒冷、装备等绝大多数提示）的后置补丁
        static void GuiSmallMessagePostfix(ComponentGui __instance, string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }
            TryPlayVoice(__instance.m_subsystemAudio, IsLocalPlayer(__instance.Entity), text);
        }

        // 画面浮字属性 setter 的后置补丁
        static void OverlayMessagePostfix(ComponentScreenOverlays __instance, string value) {
            if (string.IsNullOrEmpty(value)) {
                return;
            }
            Project project = __instance.Entity?.Project;
            SubsystemAudio subsystemAudio = project?.FindSubsystem<SubsystemAudio>(false);
            if (subsystemAudio != null) {
                TryPlayVoice(subsystemAudio, IsLocalPlayer(__instance.Entity), value);
            }
        }

        // 大字横幅的后置补丁（largeText 与 smallText 各自过一遍反查；烟花的主/副文本、剧情的台词都能命中）
        static void GuiLargeMessagePostfix(ComponentGui __instance, string largeText, string smallText) {
            bool isLocalPlayer = IsLocalPlayer(__instance.Entity);
            if (!string.IsNullOrEmpty(largeText)) {
                TryPlayVoice(__instance.m_subsystemAudio, isLocalPlayer, largeText);
            }
            if (!string.IsNullOrEmpty(smallText)) {
                TryPlayVoice(__instance.m_subsystemAudio, isLocalPlayer, smallText);
            }
        }

        static bool IsLocalPlayer(GameEntitySystem.Entity entity) {
            ComponentPlayer componentPlayer = entity?.FindComponent<ComponentPlayer>(false);
            if (componentPlayer?.PlayerData == null) {
                return false;
            }
            Project project = entity.Project;
            SubsystemPlayers subsystemPlayers = project?.FindSubsystem<SubsystemPlayers>(false);
            return subsystemPlayers != null
                && subsystemPlayers.PlayersData.Count > 0
                && componentPlayer.PlayerData == subsystemPlayers.PlayersData[0];
        }

        static void TryPlayVoice(SubsystemAudio subsystemAudio, bool isLocalPlayer, string text) {
            if (subsystemAudio == null
                || !isLocalPlayer
                || !ModSettingsManager.TryGet(out bool enabled, CorePackage, SettingsPageId, "Enable")
                || !enabled) {
                return;
            }
            VoicePack pack = ActivePack();
            if (pack == null) {
                return;
            }
            string name = MatchVoice(text);
            if (name == null) {
                return;
            }
            ModSettingsManager.TryGet(out float volume, CorePackage, SettingsPageId, "Volume");
            ModSettingsManager.TryGet(out int cooldown, CorePackage, SettingsPageId, "Cooldown");
            double now = Time.RealTime;
            if (now - m_lastAnyVoiceTime < DefaultGlobalGap
                || m_lastPlayTime.TryGetValue(name, out double last) && now - last < Math.Max(cooldown, 0)) {
                return;
            }
            m_lastAnyVoiceTime = now;
            m_lastPlayTime[name] = now;
            // 资源不存在时 AudioManager 内部静默忽略，不会报错刷屏
            subsystemAudio.PlaySound($"{pack.AudioDir}/{name}", volume, 0f, 0f, 0f);
        }

        // 提示文本 → 逻辑资源名；先精确匹配，再按参数模板正则匹配
        static string MatchVoice(string text) {
            EnsureIndex();
            if (m_exactMap.TryGetValue(text, out string name)) {
                return name;
            }
            foreach (KeyValuePair<Regex, string> pair in m_regexMap) {
                if (pair.Key.IsMatch(text)) {
                    return pair.Value;
                }
            }
            return null;
        }

        static void EnsureIndex() {
            if (m_exactMap.Count > 0
                && m_indexLanguage == LanguageControl.CurrentLanguageName) {
                return;
            }
            m_exactMap = [];
            m_regexMap = [];
            m_indexLanguage = LanguageControl.CurrentLanguageName;
            foreach ((string section, string key, string slot) in Entries) {
                if (!LanguageControl.TryGet(out string template, section, key)) {
                    continue;
                }
                string resourceName = slot == null ? $"{section}_{key}" : $"{section}_{key}_{slot}";
                if (slot != null
                    && LanguageControl.TryGet(out string slotText, "ComponentVitalStats", SlotKeys[slot])) {
                    // 部位提示：把 {0} 填成部位文案后可精确匹配
                    m_exactMap[string.Format(template, slotText)] = resourceName;
                }
                else if (template.Contains('{')) {
                    // 含 {0}/{1} 动态参数的模板（工具名、等级数字等），转成正则。
                    // 先用控制字符占位再 Escape，避免 Regex.Escape 产生的 "\{0\}" 无法整段替换
                    string escaped = Regex.Escape(
                            template.Replace("{0}", "\u0001").Replace("{1}", "\u0002").Replace("{2}", "\u0003"))
                        .Replace("\u0001", ".+?")
                        .Replace("\u0002", ".+?")
                        .Replace("\u0003", ".+?");
                    string pattern = "^" + escaped + "$";
                    try {
                        m_regexMap.Add(new(new Regex(pattern, RegexOptions.Compiled), resourceName));
                    }
                    catch (ArgumentException) {
                        Log.Warning($"VoicePromptCore: invalid template [{section}:{key}] {template}");
                    }
                }
                else {
                    m_exactMap[template] = resourceName;
                }
            }
            // 磁铁超限提示是硬编码英文，不经过语言包
            m_exactMap["Too many magnets"] = "MagnetBlock_TooMany";
            Log.Information($"VoicePromptCore: indexed {m_exactMap.Count} exact + {m_regexMap.Count} pattern prompts ({m_indexLanguage})");
        }
    }
}
