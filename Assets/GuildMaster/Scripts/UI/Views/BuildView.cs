using System.Collections.Generic;
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
    /// Build window (P4-13): what the guild can put up, what it costs against what it has, and a
    /// button that starts "click where it goes". Placed buildings are torn down from the building
    /// window instead (click the building). On top, pushing the town walls out (P4-86); below the
    /// watchtower, the guild's facilities (P4-87, inside the walls, raised by clicking them).
    /// </summary>
    public class BuildView : UIPanel
    {
        public override string Title => "건설";

        readonly List<Text> _titles = new();
        readonly List<Text> _costs = new();
        readonly List<Button> _buttons = new();
        RectTransform _list;
        Text _message;
        Text _expandTitle, _expandEffect, _expandCost;
        Button _expandButton;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 6);
            UIBuilder.Label(body, "[배치]를 누르면 미리보기가 커서를 따라옵니다. 초록이면 지을 수 있고, 우클릭이나 ESC로 취소합니다.\n지은 건물은 클릭해서 업그레이드·수리·철거합니다. 길드 내실은 성벽 안에, 방어 시설은 성벽 밖 " +
                            $"{DefenceRules.Reach}칸 안에만 지을 수 있습니다.", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 42);
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);
            _list = UIBuilder.ScrollList(body, out var scroll, 6);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);

            BuildExpansionRow();
            var construction = Services.Get<ConstructionService>();
            foreach (var type in ConstructionService.Placeable)
            {
                bool facility = FacilityRules.IsFacility(type);
                if (type == BuildingType.ArrowTower)
                    UIBuilder.Label(_list, $"방어 시설 — 성벽 밖 {DefenceRules.Reach}칸 안. 아무도 지키지 않으며, 침공은 가장 가까운 건물부터 노립니다.",
                                    14, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
                if (type == BuildingType.Armory)
                    UIBuilder.Label(_list, "길드 내실 — 스탯 3종(공격·방어·체력), 적 약화 3종, 대숙소(스트레스 회복, 1채만). 같은 종류를 더 지으면 효과는 절반·¼, 비용은 ×1.5씩.",
                                    14, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
                var row = UIBuilder.Rect(_list, "Row");
                UIBuilder.HLayout(row, 8, 0);
                // An HLayout force-expands its height, which makes the row itself report as
                // flexible: pin it to 0 so it keeps its own height inside the list.
                row.Size(-1, 100, -1, 0);

                var info = UIBuilder.Rect(row, "Info");
                UIBuilder.VLayout(info, 2, 0);
                info.Size(-1, 100, flexWidth: 1);
                _titles.Add(UIBuilder.Label(info, BuildingNames.Of(type), 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28));
                var effect = construction.CostOf(type)?.effectText ?? "";
                if (facility)
                    effect = $"Lv.1 {effect} · Lv.3 {FacilityRules.EffectText(type, FacilityRules.MaxLevel)}";
                if (DefenceRules.IsDefence(type))
                    effect = $"Lv.1 {DefenceRules.TowerEffect(1)} · Lv.3 {DefenceRules.TowerEffect(DefenceRules.TowerLevels)}";
                UIBuilder.Label(info, effect, 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                var cost = UIBuilder.Label(info, "", 15, TextAnchor.MiddleLeft).Size(-1, 40);
                cost.verticalOverflow = VerticalWrapMode.Overflow;
                _costs.Add(cost);

                var captured = type;
                var button = UIBuilder.Button(row, "배치", () => OnPlace(captured), 130, 48, new Color(0.25f, 0.45f, 0.3f), 17).Size(130, 48, flexWidth: 0);
                _buttons.Add(button);
                if (type == BuildingType.ArrowTower) BuildTrapRow();
            }
        }

        // ---------------- Traps (P4-113) ----------------

        Text _trapTitle, _trapCost;
        Button _trapButton, _rearmButton;

        void BuildTrapRow()
        {
            var row = UIBuilder.Rect(_list, "Traps");
            UIBuilder.HLayout(row, 8, 0);
            row.Size(-1, 100, -1, 0);
            var info = UIBuilder.Rect(row, "Info");
            UIBuilder.VLayout(info, 2, 0);
            info.Size(-1, 100, flexWidth: 1);
            _trapTitle = UIBuilder.Label(info, "함정", 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28);
            UIBuilder.Label(info, $"몬스터만 밟음: 체력 {DefenceRules.TrapDamage * 100f:0}% 피해·{DefenceRules.TrapHoldSeconds:0}초 묶음, {DefenceRules.TrapCharges}번 쓰면 다시 놓기. 숙련 이상은 가끔 피함",
                            14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
            _trapCost = UIBuilder.Label(info, "", 15, TextAnchor.MiddleLeft).Size(-1, 40);
            _trapCost.verticalOverflow = VerticalWrapMode.Overflow;
            var buttons = UIBuilder.Rect(row, "Buttons");
            UIBuilder.VLayout(buttons, 4, 0);
            buttons.Size(130, 100, flexWidth: 0);
            _trapButton = UIBuilder.Button(buttons, "놓기", OnTrap, 130, 46, new Color(0.25f, 0.45f, 0.3f), 17);
            _rearmButton = UIBuilder.Button(buttons, "다시 놓기", OnRearm, 130, 46, new Color(0.45f, 0.35f, 0.2f), 15);
        }

        void RefreshTraps(GuildTreasury treasury, GameDatabase db)
        {
            if (_trapCost == null || !Services.TryGet<DefenceService>(out var defences)) return;
            int spent = defences.SpentCount;
            _trapTitle.text = $"함정  {UIBuilder.Colored($"{defences.TrapCount}개{(spent > 0 ? $" (다 씀 {spent})" : "")}", UITheme.TextMuted)}";
            _trapCost.text = $"{Have(treasury.Gold, DefenceRules.TrapGold, "골드", "G")}   "
                             + Have(treasury.GetStock(DefenceRules.TrapIronItem), DefenceRules.TrapIron, db.Item(DefenceRules.TrapIronItem)?.displayName ?? "철광석", "")
                             + "\n" + UIBuilder.Colored(spent > 0 ? $"다시 놓기: {spent}개 × {DefenceRules.TrapRearmGold}G = {defences.RearmCost}G" : "놓은 자리를 연달아 클릭할 수 있습니다.", UITheme.TextMuted);
            _trapButton.interactable = defences.CannotAffordTrap() == null;
            _rearmButton.interactable = spent > 0 && treasury.Gold >= defences.RearmCost;
        }

        void OnTrap()
        {
            if (!Services.TryGet<DefenceService>(out var defences)) return;
            if (defences.BeginTrapPlacement(out var error))
            {
                _message.text = UIBuilder.Colored("지도에서 자리를 클릭하세요.", UITheme.Good);
                Close();
            }
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }

        void OnRearm()
        {
            if (!Services.TryGet<DefenceService>(out var defences)) return;
            int spent = defences.SpentCount;
            _message.text = defences.TryRearmAll(out var error)
                ? UIBuilder.Colored($"함정 {spent}개를 다시 놓았습니다.", UITheme.Good)
                : UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }

        // ---------------- Town walls (P4-86) ----------------

        void BuildExpansionRow()
        {
            var row = UIBuilder.Rect(_list, "Expand");
            UIBuilder.HLayout(row, 8, 0);
            row.Size(-1, 118, -1, 0);
            var info = UIBuilder.Rect(row, "Info");
            UIBuilder.VLayout(info, 2, 0);
            info.Size(-1, 118, flexWidth: 1);
            _expandTitle = UIBuilder.Label(info, "", 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28);
            _expandEffect = UIBuilder.Label(info, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
            _expandCost = UIBuilder.Label(info, "", 15, TextAnchor.UpperLeft).Size(-1, 60);
            _expandCost.verticalOverflow = VerticalWrapMode.Overflow;
            _expandButton = UIBuilder.Button(row, "넓히기", OnExpand, 130, 48, new Color(0.45f, 0.35f, 0.2f), 17).Size(130, 48, flexWidth: 0);
        }

        void RefreshExpansion(GuildTreasury treasury, GameDatabase db)
        {
            if (_expandTitle == null || !Services.TryGet<TownExpansion>(out var expansion)) return;
            int width = TownExpansionRules.Width(expansion.HalfSize);
            if (expansion.AtMax)
            {
                _expandTitle.text = "마을 넓히기 (최대)";
                _expandEffect.text = $"성벽 안 {width}×{width}칸, 더 넓힐 수 없습니다.";
                _expandCost.text = "";
                _expandButton.interactable = false;
                return;
            }
            int next = TownExpansionRules.Width(expansion.NextHalfSize);
            _expandTitle.text = $"마을 넓히기 ({expansion.Stage + 1}/{TownExpansionRules.MaxStage}단계)";
            _expandEffect.text = $"성벽 안 {width}×{width}칸 → {next}×{next}칸. 성벽과 성문이 바로 밀려납니다.";
            var cost = expansion.NextCost.Value;
            var sb = new StringBuilder(Have(treasury.Gold, cost.Gold, "골드", "G"));
            foreach (var c in cost.Items)
                sb.Append("   ").Append(Have(treasury.GetStock(c.itemId), c.count, db.Item(c.itemId)?.displayName ?? c.itemId, ""));
            string reason = expansion.CannotExpandReason();
            // The cost line already shows what is short; other reasons get a line of their own.
            bool cantAfford = treasury.Gold < cost.Gold || System.Array.Exists(cost.Items, c => treasury.GetStock(c.itemId) < c.count);
            if (reason != null && !cantAfford) sb.Append("\n").Append(UIBuilder.Colored(reason, UITheme.Bad));
            _expandCost.text = sb.ToString();
            _expandButton.interactable = reason == null;
        }

        void OnExpand()
        {
            if (!Services.TryGet<TownExpansion>(out var expansion)) return;
            if (expansion.TryExpand(out var error))
            {
                int width = TownExpansionRules.Width(expansion.HalfSize);
                _message.text = UIBuilder.Colored($"마을이 {width}×{width}칸으로 넓어졌습니다.", UITheme.Good);
            }
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }

        protected override void Refresh()
        {
            var construction = Services.Get<ConstructionService>();
            var treasury = Services.Get<GuildTreasury>();
            var db = Services.Get<GameDatabase>();
            RefreshExpansion(treasury, db);
            RefreshTraps(treasury, db);
            var town = Services.Get<TownService>();
            for (int i = 0; i < ConstructionService.Placeable.Length && i < _costs.Count; i++)
            {
                var type = ConstructionService.Placeable[i];
                var cost = construction.PriceToBuild(type);
                if (cost == null) continue;
                bool facility = FacilityRules.IsFacility(type);
                int built = town.CountOf(type);
                _titles[i].text = facility
                    ? $"{BuildingNames.Of(type)}  {UIBuilder.Colored($"{built}/{FacilityRules.MaxCopies(type)}채", UITheme.TextMuted)}"
                    : BuildingNames.Of(type);
                string reason = construction.CannotBuildReason(type);
                var sb = new StringBuilder();
                if (facility && built >= FacilityRules.MaxCopies(type)) sb.Append(UIBuilder.Colored(reason, UITheme.TextMuted));
                else
                {
                    sb.Append(Have(treasury.Gold, cost.goldCost, "골드", "G"));
                    foreach (var c in cost.itemCosts)
                        sb.Append("   ").Append(Have(treasury.GetStock(c.itemId), c.count, db.Item(c.itemId)?.displayName ?? c.itemId, ""));
                    string note = facility && built > 0
                        ? $"{built + 1}번째 채: 효과 {FacilityRules.CopyWeights[built] * 100f:0}%·비용 ×{FacilityRules.CopyCost(built):0.##}. "
                        : "";
                    sb.Append("\n").Append(UIBuilder.Colored($"{note}철거하면 {construction.RefundText(type)} 돌려받습니다.", UITheme.TextMuted));
                }
                _costs[i].text = sb.ToString();
                _buttons[i].interactable = reason == null;
            }
        }

        static string Have(int have, int need, string name, string unit)
            => UIBuilder.Colored($"{name} {have}/{need}{unit}", have >= need ? UITheme.Good : UITheme.Bad);

        void OnPlace(BuildingType type)
        {
            var construction = Services.Get<ConstructionService>();
            if (construction.BeginPlacement(type, out var error))
            {
                _message.text = UIBuilder.Colored("지도에서 자리를 클릭하세요.", UITheme.Good);
                Close();
            }
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }
    }
}
