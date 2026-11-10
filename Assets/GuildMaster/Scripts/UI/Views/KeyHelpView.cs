using System.Text;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// ` (backquote) toggles a key reference at the top of the screen: what each F-key (and the other
    /// global shortcuts) does, with the current on/off state of the toggles. Debug-only keys are marked.
    /// </summary>
    public class KeyHelpView : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;

        RectTransform _root;
        Text _text;
        float _timer;

        public void Build(RectTransform canvas)
        {
            var bg = UIBuilder.Panel(canvas, "KeyHelp", new Color(0.08f, 0.07f, 0.11f, 0.94f), raycast: false);
            _root = bg.rectTransform;
            _root.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -58), new Vector2(620, 236));
            _text = UIBuilder.Label(_root, "", 16, TextAnchor.UpperLeft);
            _text.rectTransform.Stretch(16, 16, 12, 12);
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>ESC closes it first, as the top-most thing on screen (P6-2, UIRoot).</summary>
        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_root == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.backquoteKey.wasPressedThisFrame && !IsTyping())
            {
                _root.gameObject.SetActive(!_root.gameObject.activeSelf);
                _root.SetAsLastSibling();
                _timer = 0f;
            }
            if (!_root.gameObject.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            _text.text = Describe();
            // As tall as its lines (P6-2: the window list wraps to more lines than the fixed 236 held).
            _root.sizeDelta = new Vector2(_root.sizeDelta.x, Mathf.Ceil(_text.preferredHeight) + 24f);
        }

        string Describe()
        {
            bool dev = Debug.isDebugBuild;
            var sb = new StringBuilder();
            sb.AppendLine(UIBuilder.Colored("단축키  (` 또는 ESC로 닫기)", UITheme.Accent));
            if (dev)
            {
                bool llm = Services.TryGet<LlmDebugView>(out var llmView) && llmView.Visible;
                Line(sb, "F2", "LLM 디버그 창", llm ? "열림" : "닫힘", llm, "개발");
            }
            if (dev)
            {
                bool fogOff = Services.TryGet<FogOfWar>(out var fog) && fog.Disabled;
                Line(sb, "F3", "안개 끄기(전체 보기)", fogOff ? "켬" : "끔", fogOff, "개발");
            }
            if (Application.isEditor) Line(sb, "F4", "유닛 기즈모·둥지 영역", UnitGizmos.ShowAll ? "켬" : "끔", UnitGizmos.ShowAll, "에디터");
            bool tags = Services.TryGet<NameTagLayer>(out var nameTags) && nameTags.Enabled;
            Line(sb, "F5", "이름표", tags ? "켬" : "끔", tags, null);
            if (dev)
            {
                bool god = Unit.GuildInvulnerable;
                string state = !god ? "끔" : Unit.GuildUntargetable ? "켬(피해 없음·표적 불가)" : "켬(피해만 없음)";
                Line(sb, "F6", "길드 무적", state, god, "개발");
            }
            sb.AppendLine();
            bool minimap = Services.TryGet<MinimapView>(out var mm) && mm.IsShown;
            var clock = Services.Get<GameClock>();
            Line(sb, "M", "미니맵", minimap ? "켬" : "끔", minimap, null);
            Line(sb, "C", "관전 모드(볼 만한 장면으로 카메라)", Director.SpectatorCamera.On ? "켬" : "끔", Director.SpectatorCamera.On, null);
            // P4-132: the guild master's orders, from the bar at the bottom.
            if (Services.TryGet<Commands.MasterCommands>(out var orders))
                Line(sb, "5~8", "지휘: 응원 · 집중 공격 · 집결 깃발 · 후퇴 나팔", $"지휘력 {Mathf.FloorToInt(orders.Power)}", false, null);
            Line(sb, $"Space · 1~{GameClock.SpeedScales.Length - 1}", "일시정지 · 속도",
                 clock.SpeedIndex == 0 ? "정지" : $"x{GameClock.SpeedScales[clock.SpeedIndex]:0}", false, null);
            // Keep this in step with the AddWindow hotkeys in UIRoot.
            sb.Append(UIBuilder.Colored("Q 의뢰 · R 길드원 · I 창고 · U 살림 · E 생태계 · T 행상인 · B 건설 · L 정산 · O 소식지 · H 추모 · G 등급 · K 도감 · J 결정 · P 규칙 · V 관계도 · N 알림 · Y 할 일 · ESC 창 닫기 · Shift+ESC 창 모두 닫기", UITheme.TextMuted));
            return sb.ToString();
        }

        static void Line(StringBuilder sb, string key, string what, string state, bool on, string tag)
        {
            string stateText = UIBuilder.Colored($"[{state}]", on ? UITheme.Good : UITheme.TextMuted);
            string tagText = tag == null ? "" : UIBuilder.Colored($"  ({tag})", UITheme.TextMuted);
            sb.Append(UIBuilder.Colored(key.PadRight(4), UITheme.Accent)).Append("  ").Append(what).Append("  ").Append(stateText).Append(tagText).Append('\n');
        }

        static bool IsTyping() => Session.TextFocus.Typing; // P6-2: a field with the caret, not just a selected one
    }
}
