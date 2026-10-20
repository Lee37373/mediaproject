using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Recruitment;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Guild members overview. Clicking a row focuses the camera and opens the inspector.</summary>
    public class RosterView : UIPanel
    {
        public override string Title => "길드원";

        RectTransform _content;
        readonly List<MemberRow> _rows = new();
        Text _summary;
        Button _notice;
        Text _noticeState;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            _summary = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);

            // Recruitment notice (P4-38): pay to bring the next applicant within a few hours.
            var notice = UIBuilder.Rect(body, "Notice");
            UIBuilder.HLayout(notice, 8, 0);
            notice.Size(-1, 32, -1, 0);
            _notice = UIBuilder.Button(notice, $"모집 공고 ({VisitorService.NoticeCost}G)", PostNotice, 160, 32, UITheme.Header, 15);
            _noticeState = UIBuilder.Label(notice, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 32, flexWidth: 1);

            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        // ---------------- Grouping by party (P4-49) ----------------

        class Group
        {
            public string Title;
            public bool IsParty;
            public readonly List<Adventurer> Members = new();
        }

        readonly List<GroupHeader> _headers = new();
        readonly List<Group> _groups = new();
        readonly List<Group> _freeGroups = new();
        readonly List<Adventurer> _party = new();
        readonly HashSet<Adventurer> _placed = new();

        Group NewGroup(string title, bool isParty)
        {
            Group g;
            if (_freeGroups.Count > 0) { g = _freeGroups[^1]; _freeGroups.RemoveAt(_freeGroups.Count - 1); }
            else g = new Group();
            g.Title = title;
            g.IsParty = isParty;
            g.Members.Clear();
            _groups.Add(g);
            return g;
        }

        /// <summary>
        /// Parties: members sharing a quest, or a hunting band (P4-37) — from gathering at the gate
        /// to coming home — leader first, in roster order of their leaders; everyone else in one
        /// last group.
        /// </summary>
        void BuildGroups(IReadOnlyList<Adventurer> members)
        {
            _freeGroups.AddRange(_groups);
            _groups.Clear();
            _placed.Clear();
            // Standing parties first (P4-50), leader first, with what they are up to.
            if (Services.TryGet<GuildParties>(out var parties))
            {
                foreach (var p in parties.Parties)
                {
                    var leader = p.Leader;
                    if (leader == null) continue;
                    string doing = p.Settling ? "정산 중"
                        : p.OnOuting ? (leader.CurrentQuest != null ? $"의뢰 {leader.CurrentQuest.Title}" : "자율 사냥") + (p.Arrived.Count > 0 ? " · 귀환 중" : "")
                        : "마을";
                    var g = NewGroup($"{p.Name} · {doing} · {p.Members.Count}명", true);
                    foreach (var m in p.Members)
                        if (Contains(members, m) && _placed.Add(m)) g.Members.Add(m);
                }
            }
            Services.TryGet<HuntBandService>(out var bands);
            foreach (var a in members)
            {
                if (_placed.Contains(a) || !a.IsAlive) continue;
                string what = null;
                _party.Clear();
                var quest = a.CurrentQuest;
                if (quest != null && quest.IsActive)
                {
                    // Assignees are in the order they took it: the one who took it first leads.
                    foreach (var t in quest.Assignees)
                        if (t is Adventurer m && m.IsAlive && m.CurrentQuest == quest && Contains(members, m)) _party.Add(m);
                    what = $"의뢰 {quest.Title}";
                }
                else if (bands != null && bands.BandOf(a) is { } band)
                {
                    foreach (var m in band.Members) if (m.IsAlive && Contains(members, m)) _party.Add(m);
                    what = band.Mustering ? "자율 사냥 · 길드에서 모이는 중" : "자율 사냥";
                }
                _party.RemoveAll(m => _placed.Contains(m)); // already shown with their standing party
                if (_party.Count < 2) continue;
                var g = NewGroup($"{_party[0].Data.name}의 파티 · {what} · {_party.Count}명", true);
                foreach (var m in _party) { g.Members.Add(m); _placed.Add(m); }
            }
            Group solo = null;
            foreach (var a in members)
            {
                if (_placed.Contains(a)) continue;
                solo ??= NewGroup("", false);
                solo.Members.Add(a);
            }
            if (solo != null) solo.Title = $"파티 없음 · {solo.Members.Count}명";
        }

        static bool Contains(IReadOnlyList<Adventurer> list, Adventurer a)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == a) return true;
            return false;
        }

        protected override void Refresh()
        {
            var roster = Services.Get<GuildRoster>();
            _summary.text = $"길드원 {roster.CountText} · 파티별로 묶어 보여 줍니다(★ 리더). 행을 클릭하면 해당 모험가를 추적합니다.";
            BuildGroups(roster.Members);
            EnsureRows(_content, _headers, _groups.Count, GroupHeader.Create);
            EnsureRows(_content, _rows, roster.Members.Count, MemberRow.Create);
            int sibling = 0, row = 0;
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                var g = _groups[gi];
                _headers[gi].Bind(g.Title, g.IsParty);
                _headers[gi].transform.SetSiblingIndex(sibling++);
                for (int i = 0; i < g.Members.Count; i++)
                {
                    _rows[row].Bind(g.Members[i], i, g.IsParty && i == 0);
                    _rows[row].transform.SetSiblingIndex(sibling++);
                    row++;
                }
            }

            if (Services.TryGet<VisitorService>(out var visitors))
            {
                var reason = visitors.CannotPostNoticeReason();
                _notice.interactable = reason == null;
                _noticeState.text = reason ?? $"{VisitorService.NoticeMinHours:0}~{VisitorService.NoticeMaxHours:0}시간 안에 지원자가 마을 광장에 옵니다.";
            }
        }

        void PostNotice()
        {
            if (Services.TryGet<VisitorService>(out var visitors)) visitors.PostNotice();
        }

        class GroupHeader : MonoBehaviour
        {
            Image _bg;
            Text _label;

            public static GroupHeader Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "GroupHeader", UITheme.Header, raycast: false);
                bg.Size(-1, 30);
                var h = bg.gameObject.AddComponent<GroupHeader>();
                h._bg = bg;
                UIBuilder.HLayout(bg, 0, 0).padding = new RectOffset(10, 10, 0, 0);
                h._label = UIBuilder.Label(bg.transform, "", 15, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 30, flexWidth: 1);
                return h;
            }

            public void Bind(string title, bool isParty)
            {
                _label.text = title;
                _label.color = isParty ? UITheme.Accent : UITheme.TextMuted;
                _bg.color = isParty ? UITheme.Header : UITheme.PanelBgLight;
            }
        }

        class MemberRow : MonoBehaviour
        {
            Image _bg;
            Text _main;
            Text _sub;
            Text _traits;
            Adventurer _adv;

            public static MemberRow Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "MemberRow", UITheme.RowBg);
                bg.Size(-1, 86);
                var row = bg.gameObject.AddComponent<MemberRow>();
                row._bg = bg;
                UIBuilder.VLayout(bg, 2, 6).padding = new RectOffset(12, 12, 6, 6);
                row._main = UIBuilder.Label(bg.transform, "", 16).Size(-1, 24);
                row._sub = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                row._traits = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(row.Focus);
                // Detail window (P4-6), pinned to the top-right corner outside the row's layout.
                var detail = UIBuilder.Button(bg.transform, "상세", row.OpenDetail, 72, 28, UITheme.Header, 14);
                if (!detail.TryGetComponent<LayoutElement>(out var le)) le = detail.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                ((RectTransform)detail.transform).Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -8), new Vector2(72, 28));
                // Care window (F-2): interview, leave, bonus, dismissal.
                var care = UIBuilder.Button(bg.transform, "관리", row.OpenCare, 72, 28, UITheme.Header, 14);
                if (!care.TryGetComponent<LayoutElement>(out var careLe)) careLe = care.gameObject.AddComponent<LayoutElement>();
                careLe.ignoreLayout = true;
                ((RectTransform)care.transform).Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -42), new Vector2(72, 28));
                return row;
            }

            public void Bind(Adventurer a, int index, bool leader = false)
            {
                _adv = a;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                var d = a.Data;
                string hpColor = UITheme.Hex(a.HpRatio > 0.6f ? UITheme.Good : a.HpRatio > 0.3f ? UITheme.Warn : UITheme.Bad);
                string star = leader ? UIBuilder.Colored("★ ", UITheme.Accent) : "";
                // Titles (P4-83): one for each region lord this member helped bring down.
                string titles = d.titles.Count > 0 ? " " + UIBuilder.Colored($"[군주 토벌 {d.titles.Count}]", UITheme.Accent) : "";
                _main.text = $"{star}{d.name} {InspectorView.GradeTag(d.grade)}{titles}  <color=#{UITheme.Hex(UITheme.TextMuted)}>{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · {d.mbti} · 가치관 {d.values}</color>  " +
                             $"<color=#{hpColor}>HP {a.Hp:0}/{a.Stats.MaxHp:0}</color>  {UIBuilder.Colored(a.StateLabel, UITheme.Accent)}";
                string quest = a.CurrentQuest != null ? $" · 의뢰 {a.CurrentQuest.Title}" : "";
                var level = Welfare.StressRules.LevelOf(d.stress);
                string stress = level == Welfare.StressLevel.Calm ? $"{d.stress:0}"
                    : UIBuilder.Colored($"{d.stress:0}({Welfare.StressRules.Label(level)})", MemberCareView.LevelColor(level));
                if (Services.TryGet<Welfare.GuildWelfare>(out var welfare))
                {
                    var care = welfare.Care(a);
                    if (care.Demand != Welfare.CareDemand.None)
                        stress += UIBuilder.Colored($" [{Welfare.CareRules.Label(care.Demand)} 요구]", UITheme.Warn);
                    else if (welfare.IsOnLeave(a)) stress += UIBuilder.Colored(" [휴가]", UITheme.Exp);
                }
                if (Services.TryGet<Legacy.GuildLegacy>(out var legacy) && legacy.IsWounded(a))
                    stress += UIBuilder.Colored(" [중상]", UITheme.Bad);
                _traits.text = "특성: " + Traits.TraitSet.Format(a.Traits.Traits, UITheme.Good, UITheme.Bad);
                _sub.text = $"{LivingCostRules.Purse(d.gold, d.debt)} · EXP {d.exp}/{d.ExpToNextLevel} · 피로 {d.fatigue:0} · 스트레스 {stress} · 물약 {d.potions} · 조명탄 {d.flares} · 전리품 {d.LootCount} · 처치 {d.kills} · 완료 {d.questsCompleted}{quest}";
            }

            void OpenDetail()
            {
                if (_adv != null && Services.TryGet<AdventurerDetailView>(out var view)) view.Show(_adv);
            }

            void OpenCare()
            {
                if (_adv != null && Services.TryGet<MemberCareView>(out var view)) view.Show(_adv);
            }

            void Focus()
            {
                if (_adv == null) return;
                if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(_adv.Position);
                if (!Services.TryGet<InspectorView>(out var inspector)) return;
                inspector.Show(_adv);
                inspector.FocusSelected();
            }
        }
    }
}
