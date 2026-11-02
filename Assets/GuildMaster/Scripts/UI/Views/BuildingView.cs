using System.Text;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Town;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Building window (P4-1), opened by clicking a building: current and next level effects,
    /// the upgrade cost against what the guild has, and the upgrade button.
    /// </summary>
    public class BuildingView : UIPanel
    {
        public override string Title => "건물";

        Building _building;
        Text _header, _current, _next, _cost, _message;
        Button _upgrade, _repair, _demolish;
        // The watchtower's lookout (2026-11-02): the guild master goes up, sends a flare, rings the alarm bell.
        Text _lookout;
        RectTransform _lookoutRow;
        Button _goUp, _flare, _bell;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _current = UIBuilder.Label(body, "", 15).Size(-1, 62);
            _current.verticalOverflow = VerticalWrapMode.Overflow;
            _next = UIBuilder.Label(body, "", 15).Size(-1, 44);
            _cost = UIBuilder.Label(body, "", 15).Size(-1, 84);
            _cost.verticalOverflow = VerticalWrapMode.Overflow;
            _lookout = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _lookout.verticalOverflow = VerticalWrapMode.Overflow;
            _lookoutRow = UIBuilder.Row(body, 38, 6);
            _goUp = UIBuilder.Button(_lookoutRow, "망보기", OnGoUp, 166, 36, new Color(0.3f, 0.38f, 0.5f), 15);
            _goUp.Tooltip(() => $"길드 마스터가 감시탑에 올라가 망을 봅니다: 시야 {LookoutRules.SightRadius:0}칸, 성벽 너머까지. 다른 곳으로 움직이면 내려옵니다.");
            _flare = UIBuilder.Button(_lookoutRow, "조명탄 쏘기", OnFlare, 166, 36, new Color(0.5f, 0.42f, 0.2f), 15);
            _flare.Tooltip(() => $"감시탑에서 {LookoutRules.FlareRange:0}칸 안의 한 곳을 골라 조명탄을 쏩니다. 그 둘레가 잠시 밝아지고 안개가 걷힙니다. ({LookoutRules.FlareCooldownSeconds:0}초마다)");
            _bell = UIBuilder.Button(_lookoutRow, "경종 울리기", OnBell, 166, 36, new Color(0.5f, 0.26f, 0.26f), 15);
            _bell.Tooltip(() => $"마을에서 {LookoutRules.BellRadius:0}칸 안, 의뢰 없이 밖에 나가 있는 길드원을 불러들이고 {LookoutRules.BellHoldHours:0}시간 동안 마을에 머물게 합니다. 의뢰 중인 길드원은 하던 일을 계속합니다. ({LookoutRules.BellCooldownHours:0}시간마다)");
            _upgrade = UIBuilder.Button(body, "업그레이드", OnUpgrade, -1, 42, new Color(0.25f, 0.45f, 0.3f), 17);
            _repair = UIBuilder.Button(body, "수리", OnRepair, -1, 36, new Color(0.3f, 0.38f, 0.5f), 15);
            _repair.Tooltip(() => _building == null ? "" : Services.Get<TownService>().CannotRepairReason(_building) ?? "골드를 들여 내구도를 모두 채웁니다. 무너진 건물은 다시 세워져 제 기능을 되찾습니다.");
            _demolish = UIBuilder.Button(body, "철거", OnDemolish, -1, 36, new Color(0.45f, 0.26f, 0.26f), 15);
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 30);
        }

        /// <summary>What a fallen building no longer does, with its subject particle ("시간당 EXP가").</summary>
        static string StoppedWhileDown(BuildingType type) => type switch
        {
            BuildingType.GuildHall => "시간당 EXP가",
            BuildingType.Blacksmith or BuildingType.Apothecary => "제작이",
            BuildingType.TrainingGround => "훈련이",
            BuildingType.ArrowTower => "사격과 감시가", // P4-113
            _ when FacilityRules.IsFacility(type) => "효과가",
            _ => "건물의 기능이",
        };

        /// <summary>Opens the window for a building (called by the inspector on click).</summary>
        public void ShowBuilding(Building b)
        {
            _building = b;
            _message.text = "";
            Open();
        }

        protected override void Refresh()
        {
            if (_building == null) return;
            var upgrades = Services.Get<UpgradeService>();
            var def = upgrades.Definition(_building);
            _header.text = $"{_building.DisplayName}  Lv.{_building.Level}" + (_building.IsDestroyed ? "  " + UIBuilder.Colored("무너짐", UITheme.Bad) : "");
            bool watchtower = _building.Type == BuildingType.Watchtower;
            _lookout.gameObject.SetActive(watchtower);
            _lookoutRow.gameObject.SetActive(watchtower);
            _cost.gameObject.SetActive(!watchtower); // the watchtower has no upgrade cost to show: its lookout takes the room
            if (watchtower) ShowLookout();
            // Repairs (P4-74): any damaged building, a fallen one included.
            var town = Services.Get<TownService>();
            bool damaged = _building.Hp < _building.MaxHp;
            _repair.gameObject.SetActive(damaged);
            if (damaged)
            {
                _repair.SetLabel($"{(_building.IsDestroyed ? "다시 세우기" : "수리")} ({town.RepairCost(_building)}G) · 내구도 {_building.Hp:0}/{_building.MaxHp:0}");
                _repair.interactable = town.CannotRepairReason(_building) == null;
            }
            // Player-placed buildings can be taken down again for half the price (P4-13).
            var construction = Services.TryGet<ConstructionService>(out var cs) ? cs : null;
            bool canDemolish = construction != null && construction.CanDemolish(_building);
            _demolish.gameObject.SetActive(canDemolish);
            if (canDemolish)
                _demolish.SetLabel(_building.IsDestroyed
                    ? "잔해 치우기"
                    : $"철거 (돌려받기: {construction.RefundText(_building)})");
            if (def == null)
            {
                _current.text = _next.text = _cost.text = "";
                _upgrade.gameObject.SetActive(false);
                return;
            }
            bool facility = FacilityRules.IsFacility(_building.Type);
            if (canDemolish && !facility && def.MaxLevel <= 1) // the watchtower: one level (the arrow tower rises, P4-113)
            {
                _current.text = def.Level(1).effectText;
                _next.text = _building.IsDestroyed
                    ? UIBuilder.Colored("무너졌습니다. 다시 세우기 전까지 감시하지 않습니다.", UITheme.Bad)
                    : $"내구도 {_building.Hp:0}/{_building.MaxHp:0}";
                _cost.text = "";
                _upgrade.gameObject.SetActive(false);
                return;
            }
            _current.text = $"{UIBuilder.Colored("현재", UITheme.TextMuted)}  {def.Level(_building.Level).effectText}";
            // The second and third of a kind count for less (P4-87): say how much this one counts now.
            if (facility && !_building.IsDestroyed && _building.Type != BuildingType.Dormitory
                && Services.TryGet<FacilityService>(out var facilities))
            {
                float weight = facilities.WeightOf(_building);
                if (weight < 1f)
                    _current.text += "\n" + UIBuilder.Colored(weight > 0f
                        ? $"같은 종류 중 더 강한 건물이 있어 효과의 {weight * 100f:0}%만 더해집니다."
                        : "같은 종류가 이미 셋이라 효과가 더해지지 않습니다.", UITheme.TextMuted);
            }
            // Fallen: what it gives stops until it is stood up again (P4-74·76·78), and it cannot be upgraded.
            if (_building.IsDestroyed)
            {
                _next.text = UIBuilder.Colored($"무너졌습니다. 다시 세우기 전까지 {StoppedWhileDown(_building.Type)} 멈춥니다.", UITheme.Bad);
                _cost.text = "";
                _upgrade.gameObject.SetActive(false);
                return;
            }
            bool max = _building.Level >= def.MaxLevel;
            _upgrade.gameObject.SetActive(!max);
            if (max)
            {
                _next.text = UIBuilder.Colored("최고 레벨입니다.", UITheme.Good);
                _cost.text = "";
                return;
            }
            var next = upgrades.NextLevel(_building);
            _next.text = $"{UIBuilder.Colored($"Lv.{_building.Level + 1}", UITheme.Accent)}  {next.effectText}";

            var treasury = Services.Get<GuildTreasury>();
            var db = Services.Get<GameDatabase>();
            var sb = new StringBuilder(UIBuilder.Colored("비용", UITheme.TextMuted) + "\n");
            sb.Append(Have(treasury.Gold, next.goldCost, "골드", "G"));
            foreach (var c in next.itemCosts)
                sb.Append("   ").Append(Have(treasury.GetStock(c.itemId), c.count, db.Item(c.itemId)?.displayName ?? c.itemId, ""));
            _cost.text = sb.ToString();
            _upgrade.interactable = upgrades.CannotUpgradeReason(_building) == null;
        }

        /// <summary>The watchtower's lookout state and its three buttons (2026-11-02).</summary>
        void ShowLookout()
        {
            var lookout = LookoutService.Ensure();
            bool up = lookout.Post == _building;
            string status = up ? UIBuilder.Colored($"길드 마스터가 망을 보는 중 (시야 {LookoutRules.SightRadius:0}칸)", UITheme.Good)
                : lookout.Heading == _building ? "길드 마스터가 감시탑으로 가는 중…"
                : "길드 마스터가 올라가면 더 멀리 보고, 조명탄과 경종을 쓸 수 있습니다.";
            if (lookout.BellHolding)
                status += "\n" + UIBuilder.Colored($"경종: 길드원이 {LookoutRules.Left(lookout.HoldHoursLeft, "시간")} 더 마을에 머뭅니다.", UITheme.Warn);
            _lookout.text = status;
            _goUp.SetLabel(up ? "내려오기" : "망보기 (올라가기)");
            _goUp.interactable = up || lookout.CannotGoUp(_building) == null;
            _flare.SetLabel(lookout.FlareCooldownLeft > 0f ? $"조명탄 ({LookoutRules.Left(lookout.FlareCooldownLeft, "초")})" : "조명탄 쏘기");
            _flare.interactable = up && lookout.CannotFlare() == null;
            _bell.SetLabel(lookout.BellCooldownHoursLeft > 0f ? $"경종 ({LookoutRules.Left(lookout.BellCooldownHoursLeft, "시간")})" : "경종 울리기");
            _bell.interactable = up && lookout.CannotRing() == null;
        }

        void OnGoUp()
        {
            if (_building == null) return;
            var lookout = LookoutService.Ensure();
            if (lookout.Post == _building)
            {
                lookout.StandDown();
                _message.text = "";
            }
            else if (lookout.GoUp(_building, out var error))
                _message.text = UIBuilder.Colored(lookout.Post == _building ? "감시탑에 올라갔습니다." : "길드 마스터가 감시탑으로 향합니다.", UITheme.Good);
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

        static string Have(int have, int need, string name, string unit)
            => UIBuilder.Colored($"{name} {have}/{need}{unit}", have >= need ? UITheme.Good : UITheme.Bad);

        void OnDemolish()
        {
            if (_building == null || !Services.TryGet<ConstructionService>(out var construction)) return;
            string name = _building.DisplayName;
            if (construction.TryDemolish(_building, out var error))
            {
                _building = null;
                _message.text = UIBuilder.Colored($"{name}을(를) 철거했습니다.", UITheme.Good);
                Close();
            }
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }

        void OnRepair()
        {
            if (_building == null) return;
            var town = Services.Get<TownService>();
            string why = town.CannotRepairReason(_building);
            bool wasDown = _building.IsDestroyed;
            if (why == null && town.TryRepair(_building))
                _message.text = UIBuilder.Colored($"{_building.DisplayName} {(wasDown ? "다시 세웠습니다" : "수리 완료")}!", UITheme.Good);
            else
                _message.text = UIBuilder.Colored(why ?? "수리할 수 없습니다.", UITheme.Bad);
            Refresh();
        }

        void OnUpgrade()
        {
            if (_building == null) return;
            if (Services.Get<UpgradeService>().TryUpgrade(_building, out var error))
                _message.text = UIBuilder.Colored($"{_building.DisplayName} 업그레이드 완료! (Lv.{_building.Level})", UITheme.Good);
            else
                _message.text = UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }
    }
}
