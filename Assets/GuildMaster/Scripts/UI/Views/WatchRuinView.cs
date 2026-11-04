using GuildMaster.Core;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The ruined watchtower's window (user decisions 2026-11-02), opened by clicking a known ruin: the guild master climbs it
    /// to look out, sends a flare from it and rings the alarm bell (<see cref="LookoutService"/>).
    /// </summary>
    public class WatchRuinView : UIPanel
    {
        public override string Title => "폐허 감시탑";

        PlaceSite _ruin;
        Text _header, _about, _status, _message;
        Button _goUp, _flare, _bell;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _about = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 58);
            _about.verticalOverflow = VerticalWrapMode.Overflow;
            _status = UIBuilder.Label(body, "", 15, TextAnchor.UpperLeft).Size(-1, 44);
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            var row = UIBuilder.Row(body, 40, 6);
            _goUp = UIBuilder.Button(row, "올라가기", OnGoUp, 150, 38, new Color(0.3f, 0.38f, 0.5f), 15);
            _goUp.Tooltip(() => $"길드 마스터가 걸어가서 올라갑니다. 처음 오르면 둘레 {LookoutRules.SightRadius:0}칸 안개가 걷히고, 올라가 있는 동안 그만큼 봅니다. 다른 곳으로 움직이면 내려옵니다. 가는 길에 몬스터를 만날 수 있습니다.");
            _flare = UIBuilder.Button(row, "조명탄 쏘기", OnFlare, 150, 38, new Color(0.5f, 0.42f, 0.2f), 15);
            _flare.Tooltip(() => $"감시탑에서 {LookoutRules.FlareRange:0}칸 안의 한 곳을 골라 조명탄을 쏩니다. 그 둘레가 잠시 밝아지고 안개가 걷힙니다. ({LookoutRules.FlareCooldownSeconds:0}초마다)");
            _bell = UIBuilder.Button(row, "경종 울리기", OnBell, 150, 38, new Color(0.5f, 0.26f, 0.26f), 15);
            _bell.Tooltip(() => $"마을에서 {LookoutRules.BellRadius:0}칸 안, 의뢰 없이 밖에 나가 있는 길드원을 불러들이고 {LookoutRules.BellHoldHours:0}시간 동안 마을에 머물게 합니다. 의뢰 중인 길드원은 하던 일을 계속합니다. ({LookoutRules.BellCooldownHours:0}시간마다)");
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 30);
        }

        /// <summary>Opens the window for a ruin (called by the inspector on click).</summary>
        public void ShowRuin(PlaceSite ruin)
        {
            _ruin = ruin;
            _message.text = "";
            Open();
        }

        protected override void Refresh()
        {
            if (_ruin == null) return;
            var lookout = LookoutService.Ensure();
            bool climbed = Services.TryGet<Places.PlaceService>(out var places) && places.IsClimbed(_ruin);
            bool up = lookout.Post == _ruin;
            _header.text = _ruin.Name();
            _about.text = $"{Quests.QuestBoard.DescribeArea(_ruin.WorldPosition)}\n"
                          + (climbed ? "이미 올라가 둘레를 살폈습니다. 다시 올라가 망을 볼 수 있습니다." : "아직 아무도 올라가 보지 않았습니다.");
            string status = up ? UIBuilder.Colored($"길드 마스터가 망을 보는 중 (시야 {LookoutRules.SightRadius:0}칸)", UITheme.Good)
                : lookout.Heading == _ruin ? "길드 마스터가 감시탑으로 가는 중…"
                : "길드 마스터가 올라가 있으면 조명탄과 경종을 쓸 수 있습니다.";
            if (lookout.BellHolding)
                status += "\n" + UIBuilder.Colored($"경종: 길드원이 {LookoutRules.Left(lookout.HoldHoursLeft, "시간")} 더 마을에 머뭅니다.", UITheme.Warn);
            _status.text = status;
            _goUp.SetLabel(up ? "내려오기" : lookout.Heading == _ruin ? "가는 중…" : "올라가기");
            _goUp.interactable = up || lookout.CannotGoUp(_ruin) == null;
            _flare.SetLabel(lookout.FlareCooldownLeft > 0f ? $"조명탄 ({LookoutRules.Left(lookout.FlareCooldownLeft, "초")})" : "조명탄 쏘기");
            _flare.interactable = up && lookout.CannotFlare() == null;
            _bell.SetLabel(lookout.BellCooldownHoursLeft > 0f ? $"경종 ({LookoutRules.Left(lookout.BellCooldownHoursLeft, "시간")})" : "경종 울리기");
            _bell.interactable = up && lookout.CannotRing() == null;
        }

        void OnGoUp()
        {
            if (_ruin == null) return;
            var lookout = LookoutService.Ensure();
            if (lookout.Post == _ruin)
            {
                lookout.StandDown();
                _message.text = "";
            }
            else if (lookout.GoUp(_ruin, out var error))
                _message.text = UIBuilder.Colored(lookout.Post == _ruin ? "감시탑에 올라갔습니다." : "길드 마스터가 폐허 감시탑으로 향합니다.", UITheme.Good);
            else
                _message.text = UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }

        void OnFlare()
        {
            if (LookoutService.Ensure().AimFlare(out var error)) Close(); // the map has to be free to click
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }

        void OnBell()
        {
            int called = LookoutService.Ensure().RingBell(out var error);
            _message.text = error != null
                ? UIBuilder.Colored(error, UITheme.Bad)
                : UIBuilder.Colored(called > 0 ? $"경종! 길드원 {called}명이 돌아옵니다." : "경종을 울렸습니다.", UITheme.Good);
            Refresh();
        }
    }
}
