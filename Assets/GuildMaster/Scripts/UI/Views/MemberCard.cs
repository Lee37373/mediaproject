using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// A guild member in the inspector (2026-11-13, user picked mock-up 3, "without the AI look"): the class portrait, the
    /// state as a tag, HP / tiredness / stress as gauges, traits as tags, the mood, and the last thought on a paper slip.
    /// A visitor shows only what the recruit talk has brought out instead of its traits (the answer key stays hidden).
    /// Everything else (stats, purse, gear, record, memories, relations) is in [상세].
    /// </summary>
    public class MemberCard
    {
        const float Width = 396f;   // the inspector's 420 less its margins
        const float Col = 80f;      // right of the portrait
        const float GaugeWidth = 222f;
        const float TagH = 24f;

        RectTransform _root;
        Image _portraitBack, _portraitIcon;
        HudStyle.Tag _state;
        Text _quest, _visitor, _mood, _thought;
        HudStyle.Gauge _hp, _tired, _stress;
        readonly List<HudStyle.Tag> _traits = new();
        Image _note;

        Adventurer _who;

        public float Height { get; private set; }
        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public static MemberCard Build(RectTransform panel, float top)
        {
            var c = new MemberCard();
            var root = UIBuilder.Rect(panel, "MemberCard");
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(12f, -top);
            root.sizeDelta = new Vector2(-24f, 200f);
            c._root = root;

            c._portraitBack = HudStyle.NewImage(root, "hud_portrait", "Portrait");
            HudStyle.Place(c._portraitBack.rectTransform, 0, 2, 66, 66);
            c._portraitIcon = HudStyle.NewImage(c._portraitBack.transform, "-", "ClassIcon"); // the class icon goes in on Bind
            c._portraitIcon.preserveAspect = true;
            HudStyle.Place(c._portraitIcon.rectTransform, 10, 10, 46, 46);

            c._state = HudStyle.Tag.Create(root);
            HudStyle.Place(c._state.Root, Col, 0, 60, TagH);
            c._quest = HudStyle.Label(root, "", 14, HudStyle.Muted);
            HudStyle.Place(c._quest.rectTransform, Col + 68, 0, Width - Col - 68, TagH);
            c._quest.verticalOverflow = VerticalWrapMode.Truncate; // a long title is cut, not spilled

            c._hp = HudStyle.Gauge.Create(root, "hp", HudStyle.Hp, GaugeWidth);
            HudStyle.Place(c._hp.Root, Col, 30, Width - Col, 20);
            c._tired = HudStyle.Gauge.Create(root, "tired", HudStyle.Tired, GaugeWidth);
            HudStyle.Place(c._tired.Root, Col, 52, Width - Col, 20);
            c._stress = HudStyle.Gauge.Create(root, "stress", HudStyle.Stress, GaugeWidth);
            HudStyle.Place(c._stress.Root, Col, 74, Width - Col, 20);

            c._visitor = HudStyle.Label(root, "", 14, HudStyle.Fore, FontStyle.Normal, TextAnchor.UpperLeft);
            HudStyle.Place(c._visitor.rectTransform, 0, 102, Width, 44);
            c._mood = HudStyle.Label(root, "", 14, HudStyle.Fore);
            HudStyle.Place(c._mood.rectTransform, 0, 130, Width, 22);
            c._mood.verticalOverflow = VerticalWrapMode.Truncate;

            c._note = HudStyle.NewImage(root, "hud_note", "Thought");
            if (c._note.sprite == null) c._note.color = new Color(0.96f, 0.91f, 0.79f, 1f);
            HudStyle.Place(c._note.rectTransform, 0, 158, Width, 34);
            c._thought = HudStyle.Label(c._note.transform, "", 14, HudStyle.NoteText, FontStyle.Normal, TextAnchor.MiddleLeft);
            c._thought.rectTransform.Stretch(12, 10, 6, 6);
            c._thought.horizontalOverflow = HorizontalWrapMode.Wrap;
            c._thought.verticalOverflow = VerticalWrapMode.Overflow;

            root.gameObject.SetActive(false);
            return c;
        }

        public void Hide()
        {
            if (Visible) _root.gameObject.SetActive(false);
        }

        public void Bind(Adventurer a)
        {
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            var d = a.Data;
            var s = a.Stats;

            _portraitBack.color = Color.Lerp(Color.white, RosterView.ClassColor(d.cls), 0.45f);
            var icon = RosterView.ClassIcon(d.cls);
            _portraitIcon.sprite = icon;
            _portraitIcon.color = Color.white;
            _portraitIcon.enabled = icon != null;

            float w = _state.Set(a.StateLabel, HudStyle.StateTag);
            _quest.text = a.CurrentQuest != null ? $"의뢰 {a.CurrentQuest.Title} ({a.CurrentQuest.ProgressText})" : "의뢰 없음";
            HudStyle.Place(_quest.rectTransform, Col + w + 8, 0, Width - Col - w - 8, TagH);

            bool snap = a != _who; // a newly picked member's bars start where they are; the same one's slide
            _who = a;
            _hp.Set(a.Hp, s.MaxHp, $"{a.Hp:0}/{s.MaxHp:0}", snap);
            _tired.Set(d.fatigue, 100f, $"{d.fatigue:0}", snap);
            _stress.Set(d.stress, 100f, $"{d.stress:0}", snap);

            float y;
            if (a.IsVisitor)
            {
                // Only what the talk has brought out (VisitorReveal, user request 2026-09-29).
                foreach (var t in _traits) t.Root.gameObject.SetActive(false);
                _mood.gameObject.SetActive(false);
                _visitor.gameObject.SetActive(true);
                _visitor.text = $"{RecruitDialogView.KnownTemperament(a)} · 전투 {CombatTiers.Label(a.Tier)}\n" +
                                $"{UIBuilder.Colored("특성", HudStyle.Muted)} {RecruitDialogView.KnownTraits(a)}";
                y = 150f;
            }
            else
            {
                _visitor.gameObject.SetActive(false);
                int n = 0;
                float x = 0f;
                foreach (var t in a.Traits.Traits)
                {
                    if (t == null) continue;
                    var tag = TagAt(n++);
                    float tw = tag.Set(t.displayName, t.isPositive ? HudStyle.GoodTag : HudStyle.BadTag);
                    if (x + tw > Width) { tag.Root.gameObject.SetActive(false); continue; }
                    HudStyle.Place(tag.Root, x, 102, tw, TagH);
                    x += tw + 6f;
                }
                if (n == 0)
                {
                    var tag = TagAt(n++);
                    HudStyle.Place(tag.Root, 0, 102, tag.Set("특성 없음", new Color(0.62f, 0.6f, 0.66f, 1f)), TagH);
                }
                for (int i = n; i < _traits.Count; i++) _traits[i].Root.gameObject.SetActive(false);

                _mood.gameObject.SetActive(true);
                float now = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
                string heaviest = MoodFormat.Heaviest(a, 1);
                _mood.text = $"{UIBuilder.Colored("기분", HudStyle.Muted)} {MoodFormat.Mood(a)}{MoodFormat.Inspiration(a, now)}" + (heaviest == "-" ? "" : "  " + heaviest);
                y = 158f;
            }

            bool thinking = !string.IsNullOrEmpty(a.LastThought);
            _note.gameObject.SetActive(thinking);
            float noteH = 0f;
            if (thinking)
            {
                _thought.text = a.LastThought;
                noteH = Mathf.Max(32f, Mathf.Ceil(_thought.preferredHeight) + 14f);
                HudStyle.Place(_note.rectTransform, 0, y, Width, noteH);
            }
            Height = y + (thinking ? noteH + 6f : 0f);
            _root.sizeDelta = new Vector2(_root.sizeDelta.x, Height);
        }

        HudStyle.Tag TagAt(int i)
        {
            while (_traits.Count <= i) _traits.Add(HudStyle.Tag.Create(_root));
            var t = _traits[i];
            if (!t.Root.gameObject.activeSelf) t.Root.gameObject.SetActive(true);
            return t;
        }
    }
}
