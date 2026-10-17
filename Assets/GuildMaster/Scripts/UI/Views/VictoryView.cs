using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The victory (P4-83, user decision 2026-10-16): all four region lords down. The game holds while it is shown;
    /// "계속하기" carries on in the same world (endless play), "타이틀로" leaves.
    /// </summary>
    public class VictoryView : MonoBehaviour
    {
        RectTransform _root;
        Text _stats;
        bool _holding;

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "Victory", new Color(0f, 0f, 0f, 0.7f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 470));
            UIBuilder.VLayout(box, 12, 24).childAlignment = TextAnchor.UpperCenter;
            UIBuilder.Label(box.transform, "승리!", 36, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 52);
            UIBuilder.Label(box.transform, "네 지대의 군주를 모두 쓰러뜨렸습니다.", 19, TextAnchor.MiddleCenter).Size(-1, 30);
            _stats = UIBuilder.Label(box.transform, "", 15, TextAnchor.UpperCenter, UITheme.TextMuted).Size(-1, 210);
            _stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIBuilder.Button(box.transform, "계속하기", Continue, -1, 44, new Color(0.25f, 0.45f, 0.3f), 18)
                .Tooltip(() => "같은 세계에서 계속 플레이합니다. 정화된 땅은 그대로 비어 갑니다.");
            UIBuilder.Button(box.transform, "타이틀로", () => GameSettings.ReloadScene(showTitle: true), -1, 40, null, 17);
            _root.gameObject.SetActive(false);
            EventBus.Subscribe<AllLordsSlainEvent>(Show);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<AllLordsSlainEvent>(Show);
            if (_holding && Services.TryGet<GameClock>(out var clock)) clock.Release();
        }

        void Show(AllLordsSlainEvent e)
        {
            var sb = new StringBuilder();
            int done = Services.TryGet<QuestBoard>(out var board) ? board.History.Count(q => q.Status == QuestStatus.TurnedIn) : 0;
            int members = Services.TryGet<GuildRoster>(out var roster) ? roster.Members.Count : 0;
            sb.AppendLine($"{e.Day}일차 · 길드원 {members}명 · 완료한 의뢰 {done}건");
            sb.AppendLine();
            if (Services.TryGet<RegionLordService>(out var lords))
                foreach (var l in lords.Lords)
                {
                    int day = Mathf.FloorToInt(l.SlainHour / 24f) + 1;
                    string who = l.Participants.Count > 0 ? string.Join(", ", l.Participants) : "알 수 없음";
                    sb.AppendLine($"{RegionLords.RegionName(l.Region)} · {l.Def.displayName} — {day}일차 ({who})");
                }
            _stats.text = sb.ToString();
            if (!_holding && Services.TryGet<GameClock>(out var clock))
            {
                clock.Hold();
                _holding = true;
            }
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        void Continue()
        {
            _root.gameObject.SetActive(false);
            if (_holding && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holding = false;
        }
    }
}
