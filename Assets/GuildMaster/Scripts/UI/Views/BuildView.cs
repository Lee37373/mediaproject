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
    /// window instead (click the building).
    /// </summary>
    public class BuildView : UIPanel
    {
        public override string Title => "건설";

        readonly List<Text> _costs = new();
        readonly List<Button> _buttons = new();
        RectTransform _list;
        Text _message;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 6);
            UIBuilder.Label(body, "[배치]를 누르면 미리보기가 커서를 따라옵니다. 초록이면 지을 수 있고, 우클릭이나 ESC로 취소합니다.\n무너지거나 필요 없어진 건물은 클릭해서 철거합니다.",
                            14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 42);
            _list = UIBuilder.Rect(body, "List");
            UIBuilder.VLayout(_list, 6, 0);
            _list.Size(-1, -1, -1, 1);
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);

            var construction = Services.Get<ConstructionService>();
            foreach (var type in ConstructionService.Placeable)
            {
                var row = UIBuilder.Rect(_list, "Row");
                UIBuilder.HLayout(row, 8, 0);
                // An HLayout force-expands its height, which makes the row itself report as
                // flexible: pin it to 0 so it keeps its own height inside the list.
                row.Size(-1, 100, -1, 0);

                var info = UIBuilder.Rect(row, "Info");
                UIBuilder.VLayout(info, 2, 0);
                info.Size(-1, 100, flexWidth: 1);
                UIBuilder.Label(info, BuildingNames.Of(type), 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 28);
                var effect = construction.CostOf(type)?.effectText ?? "";
                UIBuilder.Label(info, effect, 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                var cost = UIBuilder.Label(info, "", 15, TextAnchor.MiddleLeft).Size(-1, 40);
                cost.verticalOverflow = VerticalWrapMode.Overflow;
                _costs.Add(cost);

                var captured = type;
                var button = UIBuilder.Button(row, "배치", () => OnPlace(captured), 130, 48, new Color(0.25f, 0.45f, 0.3f), 17).Size(130, 48, flexWidth: 0);
                _buttons.Add(button);
            }
        }

        protected override void Refresh()
        {
            var construction = Services.Get<ConstructionService>();
            var treasury = Services.Get<GuildTreasury>();
            var db = Services.Get<GameDatabase>();
            for (int i = 0; i < ConstructionService.Placeable.Length && i < _costs.Count; i++)
            {
                var type = ConstructionService.Placeable[i];
                var cost = construction.CostOf(type);
                if (cost == null) continue;
                var sb = new StringBuilder(Have(treasury.Gold, cost.goldCost, "골드", "G"));
                foreach (var c in cost.itemCosts)
                    sb.Append("   ").Append(Have(treasury.GetStock(c.itemId), c.count, db.Item(c.itemId)?.displayName ?? c.itemId, ""));
                sb.Append("\n").Append(UIBuilder.Colored($"철거하면 {construction.RefundText(type)} 돌려받습니다.", UITheme.TextMuted));
                _costs[i].text = sb.ToString();
                _buttons[i].interactable = construction.CannotAffordReason(type) == null;
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
