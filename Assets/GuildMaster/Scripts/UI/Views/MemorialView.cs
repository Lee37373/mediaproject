using System.Collections.Generic;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Legacy;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The wall of the fallen (F-6): everyone who died, how they fell, what became of what they left behind, and the
    /// guild master's answers — a memorial that eases the survivors' minds and a member sent to fetch the relics.
    /// Opened with H and from the "유품 회수 가능" notifications.
    /// </summary>
    public class MemorialView : UIPanel
    {
        public override string Title => "추모의 벽";
        protected override float RefreshInterval => 0.5f;

        RectTransform _content;
        readonly List<Row> _rows = new();
        Text _summary, _result;
        string _resultText = "";
        int _focusId;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            _summary = Wrapped(UIBuilder.Label(body, "", 15, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 44));
            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _result = Wrapped(UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft).Size(-1, 44));
        }

        static Text Wrapped(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>Opens the wall with this record highlighted (from a notification).</summary>
        public void ShowRecord(int recordId)
        {
            _focusId = recordId;
            _resultText = "";
            if (!Visible) Open();
            else
            {
                BringToFront();
                Refresh();
            }
        }

        protected override void OnOpened()
        {
            if (!Services.TryGet<GuildLegacy>(out _)) _focusId = 0;
        }

        protected override void Refresh()
        {
            _result.text = _resultText;
            if (!Services.TryGet<GuildLegacy>(out var legacy) || !Services.TryGet<GameClock>(out var clock))
            {
                _summary.text = UIBuilder.Colored("추모의 벽을 열 수 없습니다.", UITheme.TextMuted);
                EnsureRows(_content, _rows, 0, Row.Create);
                return;
            }
            var fallen = legacy.Fallen;
            _summary.text = LegacyTexts.Summary(fallen.Count);
            EnsureRows(_content, _rows, fallen.Count, Row.Create);
            float now = clock.TotalHours;
            // Newest first: the one who just fell is at the top.
            for (int i = 0; i < fallen.Count; i++)
                _rows[i].Bind(fallen[fallen.Count - 1 - i], legacy, now, i, _focusId, this);
        }

        // ---------------- Actions ----------------

        void HoldMemorial(FallenRecord rec)
        {
            if (rec == null || !Services.TryGet<GuildLegacy>(out var legacy)) return;
            Report(legacy.HoldMemorial(rec));
            Refresh();
        }

        void SendRetriever(FallenRecord rec)
        {
            if (rec == null || !Services.TryGet<GuildLegacy>(out var legacy)) return;
            Report(legacy.SendRetriever(rec));
            Refresh();
        }

        void Report(CareResult r)
        {
            _resultText = UIBuilder.Colored(r.Message, r.Ok ? UITheme.Good : UITheme.Bad);
        }

        // ---------------- Rows ----------------

        class Row : MonoBehaviour
        {
            const float ButtonWidth = 124f;

            Image _bg;
            Text _main, _sub, _relic;
            Button _memorial, _fetch, _place;
            FallenRecord _rec;
            string _memorialTip = "", _fetchTip = "";

            public static Row Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "FallenRow", UITheme.RowBg);
                bg.Size(-1, 104);
                var row = bg.gameObject.AddComponent<Row>();
                row._bg = bg;
                UIBuilder.VLayout(bg, 2, 6).padding = new RectOffset(12, (int)ButtonWidth + 24, 6, 6);
                row._main = UIBuilder.Label(bg.transform, "", 16).Size(-1, 26);
                row._sub = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
                row._relic = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
                row._relic.Tooltip(() => LegacyTexts.RelicList(row._rec) is { Length: > 0 } list ? list : "남긴 물건이 없습니다.");
                row._memorial = Corner(bg.transform, "추모식", 8, row.OnMemorial);
                row._memorial.Tooltip(() => row._memorialTip);
                row._fetch = Corner(bg.transform, "유품 회수", 38, row.OnFetch);
                row._fetch.Tooltip(() => row._fetchTip);
                row._place = Corner(bg.transform, "현장 보기", 68, row.OnPlace);
                row._place.Tooltip(() => "쓰러진 자리로 카메라를 옮깁니다.");
                return row;
            }

            /// <summary>A button pinned to the row's top-right corner, outside its layout.</summary>
            static Button Corner(Transform parent, string label, float top, UnityEngine.Events.UnityAction onClick)
            {
                var b = UIBuilder.Button(parent, label, onClick, ButtonWidth, 26, UITheme.Header, 14);
                if (!b.TryGetComponent<LayoutElement>(out var le)) le = b.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                ((RectTransform)b.transform).Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -top), new Vector2(ButtonWidth, 26));
                return b;
            }

            public void Bind(FallenRecord rec, GuildLegacy legacy, float now, int index, int focusId, MemorialView owner)
            {
                _rec = rec;
                _owner = owner;
                _bg.color = rec.Id == focusId ? UITheme.Header : index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;

                string held = rec.MemorialHeld ? UIBuilder.Colored("  [추모식 완료]", UITheme.Good) : "";
                _main.text = $"{UIBuilder.Colored(rec.Name, UITheme.Accent)}  {UIBuilder.Colored($"{rec.ClassLabel} Lv.{rec.Level} · 가치관 {rec.Values}", UITheme.TextMuted)}{held}";
                _sub.text = $"{rec.Day}일차 · {LegacyTexts.Killer(rec.KillerName)} · 처치 {rec.Kills} · 완료한 의뢰 {rec.QuestsDone}" +
                            (rec.FriendIds.Count > 0 ? $" · 친했던 길드원 {rec.FriendIds.Count}명" : "");
                _relic.text = LegacyTexts.RelicLine(rec, now);

                var memorial = legacy.CanHoldMemorial(rec);
                int cost = legacy.MemorialCost(rec);
                _memorial.interactable = memorial.Ok;
                _memorial.SetLabel(rec.MemorialHeld ? "추모 완료" : $"추모식 {cost}G");
                _memorialTip = memorial.Ok
                    ? $"{cost}G · 고인에게 정이 있던(호감도 0 이상) 길드원의 스트레스가 -{LegacyRules.MemorialReliefMin:0}~{LegacyRules.MemorialReliefMax:0}만큼 내립니다. 호감도가 높을수록 크고, 0 미만이면 변화가 없습니다."
                    : memorial.Reason;

                var fetch = legacy.CanSendRetriever(rec);
                _fetch.interactable = fetch.Ok;
                _fetch.SetLabel(legacy.IsRetrieving(rec) ? "회수 중..." : "유품 회수");
                _fetchTip = fetch.Ok
                    ? "쉬고 있는 길드원 한 명이 홀로 현장으로 가 유품을 찾아옵니다. 가까웠던 길드원이 우선입니다."
                    : fetch.Reason;
            }

            MemorialView _owner;

            void OnMemorial() => _owner?.HoldMemorial(_rec);
            void OnFetch() => _owner?.SendRetriever(_rec);

            void OnPlace()
            {
                if (_rec != null && Services.TryGet<CameraController>(out var cam)) cam.JumpTo(_rec.Position);
            }
        }
    }
}
