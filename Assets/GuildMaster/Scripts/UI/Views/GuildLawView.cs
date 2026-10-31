using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Guild;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The guild's rules (P4-112, H2): each rule with its ways, the one in force marked, what it does and how the members take
    /// it; a rule not yet opened says at which rank it opens, one changed lately how long until it can change again. A way's
    /// button shows what it does and whom it pleases on hover. Opened with P.
    /// </summary>
    public class GuildLawView : UIPanel
    {
        public override string Title => "길드 규칙";

        static readonly Color CurrentColor = new(0.30f, 0.52f, 0.32f, 1f);
        static readonly Color OptionColor = new(0.25f, 0.35f, 0.5f, 1f);
        static readonly Color ClosedColor = new(0.3f, 0.3f, 0.3f, 0.6f);

        Text _status;
        RectTransform _listContent;
        readonly List<LawRow> _rows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);
            _status = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 44);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _listContent = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<GuildLaws>(out var laws))
            {
                _status.text = "길드 규칙을 쓸 수 없습니다.";
                return;
            }
            _status.text = $"규칙은 바꾼 뒤 {GuildLawRules.LockHours / 24f:0}일 동안 다시 바꿀 수 없습니다. 길드원은 성격·가치관에 맞는 규칙을 반기고 " +
                           "반하는 규칙을 못마땅해합니다(기분에만 영향). 버튼에 마우스를 올리면 그 방식의 효과가 보입니다.";
            EnsureRows(_listContent, _rows, GuildLawRules.Count, parent => LawRow.Create(parent, this));
            for (int i = 0; i < GuildLawRules.Count; i++) _rows[i].Bind((GuildLaw)i, i, laws);
        }

        void Choose(GuildLaw law, int option)
        {
            if (!Services.TryGet<GuildLaws>(out var laws)) return;
            laws.Change(law, option);
            Refresh();
        }

        /// <summary>One rule: its name and state, a button for each way, and the way in force.</summary>
        class LawRow : MonoBehaviour
        {
            const int MaxOptions = 3;
            Image _bg;
            Text _title, _now;
            readonly Button[] _buttons = new Button[MaxOptions];
            GuildLaw _law;
            GuildLawView _owner;

            public static LawRow Create(Transform parent, GuildLawView owner)
            {
                var bg = UIBuilder.Panel(parent, "LawRow", UITheme.RowBg);
                bg.Size(-1, 124);
                var row = bg.gameObject.AddComponent<LawRow>();
                row._bg = bg;
                row._owner = owner;
                var v = UIBuilder.VLayout(bg, 4, 0);
                v.padding = new RectOffset(12, 12, 8, 8);
                row._title = UIBuilder.Label(bg.transform, "", 16).Size(-1, 24);
                var line = UIBuilder.Row(bg.transform, 34, 10);
                for (int i = 0; i < MaxOptions; i++)
                {
                    int index = i;
                    row._buttons[i] = UIBuilder.Button(line, "", () => row._owner.Choose(row._law, index), 220, 32, OptionColor, 14);
                    row._buttons[i].Tooltip(() => Hint(row._law, index));
                }
                row._now = UIBuilder.Label(bg.transform, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
                row._now.horizontalOverflow = HorizontalWrapMode.Wrap;
                row._now.verticalOverflow = VerticalWrapMode.Truncate;
                return row;
            }

            static string Hint(GuildLaw law, int option)
            {
                var def = GuildLawRules.Def(law);
                return option < def.Options.Length ? $"{def.Options[option].Label}: {def.Options[option].Effect}\n{GuildLawRules.Who(law, option)}" : "";
            }

            public void Bind(GuildLaw law, int index, GuildLaws laws)
            {
                _law = law;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                var def = GuildLawRules.Def(law);
                int current = GuildLaws.Of(law);
                bool open = laws.IsOpen(law);
                float locked = laws.HoursLocked(law);
                string state = !open ? UIBuilder.Colored($"길드 {GuildRankRules.Name(def.OpensAt)}에 열림", UITheme.TextMuted)
                             : locked > 0f ? UIBuilder.Colored($"{Mathf.CeilToInt(locked)}시간 뒤 바꿀 수 있음", UITheme.Warn)
                             : UIBuilder.Colored("바꿀 수 있음", UITheme.Good);
                _title.text = $"{UIBuilder.Colored(def.Name, UITheme.Accent)}  {UIBuilder.Colored(def.Text, UITheme.TextMuted)}  ·  {state}";
                for (int i = 0; i < MaxOptions; i++)
                {
                    bool has = i < def.Options.Length;
                    _buttons[i].gameObject.SetActive(has);
                    if (!has) continue;
                    bool isCurrent = i == current;
                    _buttons[i].SetLabel(isCurrent ? $"● {def.Options[i].Label}" : def.Options[i].Label);
                    _buttons[i].interactable = !isCurrent && laws.CannotChange(law, i) == null;
                    var colors = _buttons[i].colors;
                    colors.disabledColor = isCurrent ? CurrentColor : ClosedColor;
                    _buttons[i].colors = colors;
                }
                var (liked, disliked) = laws.Reactions(law, current);
                string mood = liked + disliked == 0 ? "신경 쓰는 길드원 없음"
                    : $"{UIBuilder.Colored($"반김 {liked}명", UITheme.Good)} · {UIBuilder.Colored($"못마땅 {disliked}명", UITheme.Bad)}";
                _now.text = $"지금: {def.Options[current].Effect}  ({mood})";
            }
        }
    }
}
