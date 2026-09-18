using System.Collections.Generic;
using System.Text;
using GuildMaster.Core;
using GuildMaster.LLM;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Debug-only window (F2, editor/development builds) listing recent LLM exchanges:
    /// queue/generation time, outcome and the caller's verdict, with the full prompt and
    /// response of the selected one. The newest exchange stays selected unless another is picked.
    /// </summary>
    public class LlmDebugView : UIPanel
    {
        public override string Title => "LLM 디버그 (F2)";
        protected override float RefreshInterval => 0.5f;

        Text _summary;
        RectTransform _list;
        Text _detail;
        ScrollRect _detailScroll;
        readonly List<ExchangeRow> _rows = new();
        LlmExchange _selected;
        bool _followLatest = true;
        int _lastShownId = -1;
        string _lastShownNote;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);

            var top = UIBuilder.Row(body, 28);
            _summary = UIBuilder.Label(top, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
            UIBuilder.Button(top, "최신 따라가기", () => { _followLatest = true; _lastShownId = -1; Refresh(); }, 120, 26, fontSize: 14);
            UIBuilder.Button(top, "복사", CopySelected, 70, 26, fontSize: 14);

            var split = UIBuilder.Rect(body, "Split");
            UIBuilder.HLayout(split, 8, 0, expandWidth: false);
            split.Size(-1, -1, 1, 1);

            _list = UIBuilder.ScrollList(split, out var listScroll, 2);
            listScroll.GetComponent<RectTransform>().Size(360, -1, 0, 1);

            var detailContent = UIBuilder.ScrollList(split, out _detailScroll, 0);
            _detailScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _detail = UIBuilder.Label(detailContent, "", 13, TextAnchor.UpperLeft);
            _detail.supportRichText = false; // model output may contain '<'
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.verticalOverflow = VerticalWrapMode.Overflow;
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<LlmGateway>(out var gw)) return;
            var history = gw.History;
            _summary.text = $"{gw.StatusText} · 완료 {gw.Completed} · 실패 {gw.Failed} · 대기 {gw.QueueCount} · 기록 {history.Count}개";

            EnsureRows(_list, _rows, history.Count, ExchangeRow.Create);
            // Newest first.
            for (int i = 0; i < history.Count; i++)
            {
                var x = history[history.Count - 1 - i];
                _rows[i].Bind(this, x, x == _selected);
            }

            if (_followLatest && history.Count > 0) _selected = history[history.Count - 1];
            if (_selected == null) { _detail.text = "아직 LLM 요청이 없습니다."; return; }
            if (_selected.Id == _lastShownId && _selected.Note == _lastShownNote) return;
            _lastShownId = _selected.Id;
            _lastShownNote = _selected.Note;
            _detail.text = Describe(_selected);
            _detailScroll.verticalNormalizedPosition = 1f;
        }

        void Select(LlmExchange x)
        {
            _selected = x;
            _followLatest = false;
            _lastShownId = -1;
            Refresh();
        }

        void CopySelected()
        {
            if (_selected != null) GUIUtility.systemCopyBuffer = Describe(_selected);
        }

        static string Outcome(LlmExchange x) => x.Ok ? "성공" : "실패: " + x.Error;

        static string Describe(LlmExchange x)
        {
            var r = x.Request;
            var sb = new StringBuilder();
            sb.AppendLine($"#{x.Id}  {r.Tag}  우선순위 {r.Priority}  {Outcome(x)}");
            sb.AppendLine($"대기 {x.QueueSeconds:0.0}s · 생성 {x.GenerateSeconds:0.0}s · 최대 토큰 {r.MaxTokens} · 온도 {r.Temperature:0.0} · 제한 {r.TimeoutSeconds:0}s");
            if (!string.IsNullOrEmpty(x.Note)) sb.AppendLine("판정: " + x.Note);
            sb.AppendLine();
            sb.AppendLine("━━ 응답 ━━");
            sb.AppendLine(string.IsNullOrEmpty(x.Response) ? "(없음)" : LlmJson.Pretty(x.Response));
            sb.AppendLine();
            sb.AppendLine("━━ 시스템 프롬프트 ━━");
            sb.AppendLine(r.System);
            sb.AppendLine();
            sb.AppendLine("━━ 요청(User) ━━");
            sb.AppendLine(LlmJson.Pretty(r.User));
            sb.AppendLine();
            sb.AppendLine($"━━ 문법(GBNF, {r.Grammar?.Length ?? 0}자) ━━");
            sb.Append(r.Grammar);
            return sb.ToString();
        }

        class ExchangeRow : MonoBehaviour
        {
            Image _bg;
            Text _text;
            LlmDebugView _owner;
            LlmExchange _x;

            public static ExchangeRow Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "ExchangeRow", UITheme.RowBg);
                bg.Size(-1, 44);
                var row = bg.gameObject.AddComponent<ExchangeRow>();
                row._bg = bg;
                row._text = UIBuilder.Label(bg.transform, "", 13, TextAnchor.MiddleLeft);
                row._text.rectTransform.Stretch(8, 8, 2, 2);
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => row._owner?.Select(row._x));
                return row;
            }

            public void Bind(LlmDebugView owner, LlmExchange x, bool selected)
            {
                _owner = owner;
                _x = x;
                _bg.color = selected ? UITheme.Header : UITheme.RowBg;
                var color = x.Ok ? (x.Note != null && x.Note.StartsWith("규칙") ? UITheme.Warn : UITheme.Good) : UITheme.Bad;
                _text.text = $"#{x.Id} {x.Request.Tag}  {UIBuilder.Colored(Outcome(x), color)}\n" +
                             UIBuilder.Colored($"대기 {x.QueueSeconds:0.0}s · 생성 {x.GenerateSeconds:0.0}s · {x.Note ?? "-"}", UITheme.TextMuted);
            }
        }
    }
}
