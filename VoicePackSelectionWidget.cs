using System;
using System.Xml.Linq;
using Engine;

namespace Game {
    /// <summary>
    ///     「语音包」设置项控件：点按钮弹出 ListSelectionDialog，动态列出当前已安装的语音音频包。
    ///     设置值为选中包的 PackageName，空串 = 自动（按加载顺序取优先级最高的包）。
    ///     在 modinfo.json 里以 "Widget": "Game.VoicePackSelectionWidget" 引用。
    /// </summary>
    public class VoicePackSelectionWidget : UniformSpacingPanelSettingWidget {
        BevelledButtonWidget m_button;

        public override void Initialize(ModSettingItem descriptor, object currentValue, string name, string description) {
            base.Initialize(descriptor, currentValue, name, description);
            m_button = new BevelledButtonWidget {
                Style = ContentManager.Get<XElement>("Styles/ButtonStyle_310x60"),
                Text = ValueToText(Value as string),
                VerticalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(20, 0)
            };
            Assemble(m_button);
        }

        public override bool Supports(Type type) => type == typeof(string);

        public override void Update() {
            base.Update();
            IsOperating = m_button.IsClicked;
            if (IsOperating) {
                DialogsManager.ShowDialog(null, new ListSelectionDialog(
                    VoicePromptCoreLoader.L("SelectTitle", "Select Voice Pack"),
                    VoicePromptCoreLoader.BuildSelectionChoices(),
                    60f,
                    item => ((VoicePromptCoreLoader.PackChoice)item).Label,
                    item => {
                        string package = ((VoicePromptCoreLoader.PackChoice)item).PackageName ?? "";
                        CommitValue(package);
                        m_button.Text = ValueToText(package);
                    }
                ));
            }
        }

        string ValueToText(string packageName) => VoicePromptCoreLoader.ChoiceLabel(packageName);
    }
}
