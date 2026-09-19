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
        Button _upgrade;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _current = UIBuilder.Label(body, "", 15).Size(-1, 44);
            _next = UIBuilder.Label(body, "", 15).Size(-1, 44);
            _cost = UIBuilder.Label(body, "", 15).Size(-1, 84);
            _cost.verticalOverflow = VerticalWrapMode.Overflow;
            _upgrade = UIBuilder.Button(body, "업그레이드", OnUpgrade, -1, 42, new Color(0.25f, 0.45f, 0.3f), 17);
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 30);
        }

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
            _header.text = $"{_building.DisplayName}  Lv.{_building.Level}";
            if (def == null)
            {
                _current.text = _next.text = _cost.text = "";
                _upgrade.gameObject.SetActive(false);
                return;
            }
            _current.text = $"{UIBuilder.Colored("현재", UITheme.TextMuted)}  {def.Level(_building.Level).effectText}";
            bool max = _building.Level >= def.MaxLevel;
            _upgrade.gameObject.SetActive(!max);
            if (max)
            {
                _next.text = UIBuilder.Colored("최고 레벨입니다.", UITheme.Good);
                _cost.text = "";
                return;
            }
            var next = def.Level(_building.Level + 1);
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
