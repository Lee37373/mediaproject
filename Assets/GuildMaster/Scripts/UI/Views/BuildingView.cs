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

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _current = UIBuilder.Label(body, "", 15).Size(-1, 62);
            _current.verticalOverflow = VerticalWrapMode.Overflow;
            _next = UIBuilder.Label(body, "", 15).Size(-1, 44);
            _cost = UIBuilder.Label(body, "", 15).Size(-1, 84);
            _cost.verticalOverflow = VerticalWrapMode.Overflow;
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
            if (canDemolish && !facility)
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
