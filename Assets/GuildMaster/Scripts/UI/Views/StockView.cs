using System.Collections.Generic;
using System.Linq;
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
    /// Guild storage: [보유 품목] materials & goods with the loot buy-rate policy, and [제작]
    /// crafting (P4-2) — order recipes, set stock targets for auto-crafting, watch the queues.
    /// </summary>
    public class StockView : UIPanel
    {
        public override string Title => "길드 창고";

        RectTransform _stockPage, _craftPage;
        Button _stockTab, _craftTab;

        // Stock tab
        RectTransform _content;
        readonly List<Text> _rows = new();
        Text _rateLabel;
        Text _priceHint;

        // Craft tab
        class RecipeRow
        {
            public RecipeDefinition Recipe;
            public Text Info;
            public Text Target;
            public Button Order;
        }
        readonly List<RecipeRow> _recipeRows = new();
        Text _queues;
        Text _craftMessage;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);
            var tabs = UIBuilder.Row(body, 34);
            _stockTab = UIBuilder.Button(tabs, "보유 품목", () => ShowTab(false), 140, 32);
            _craftTab = UIBuilder.Button(tabs, "제작", () => ShowTab(true), 140, 32);

            _stockPage = UIBuilder.Rect(body, "StockPage");
            _stockPage.Size(-1, -1, -1, 1);
            UIBuilder.VLayout(_stockPage, 8, 0);
            BuildStock(_stockPage);

            _craftPage = UIBuilder.Rect(body, "CraftPage");
            _craftPage.Size(-1, -1, -1, 1);
            UIBuilder.VLayout(_craftPage, 6, 0);
            BuildCraft(_craftPage);
            ShowTab(false);
        }

        void ShowTab(bool craft)
        {
            _stockPage.gameObject.SetActive(!craft);
            _craftPage.gameObject.SetActive(craft);
            _stockTab.interactable = craft;
            _craftTab.interactable = !craft;
            Refresh();
        }

        // ---------------- Stock ----------------

        void BuildStock(RectTransform page)
        {
            var policy = UIBuilder.Panel(page, "Policy", UITheme.PanelBgLight);
            policy.Size(-1, 96);
            UIBuilder.VLayout(policy, 4, 10);
            _rateLabel = UIBuilder.Label(policy.transform, "", 15).Size(-1, 22);
            var trade = Services.Get<TradeService>();
            UIBuilder.Slider(policy.transform, 0.5f, 1.5f, trade.LootBuyRate, v =>
            {
                trade.LootBuyRate = Mathf.Round(v * 20f) / 20f;
                Refresh();
            });
            _priceHint = UIBuilder.Label(policy.transform, "", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 34);

            UIBuilder.Label(page, "보유 품목", 16, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
            _content = UIBuilder.ScrollList(page, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        void RefreshStock()
        {
            var trade = Services.Get<TradeService>();
            _rateLabel.text = $"전리품 매입가 배율: {UIBuilder.Colored($"x{trade.LootBuyRate:0.00}", UITheme.Accent)}  ·  상품 판매가 배율 x{trade.GoodsSellRate:0.00}";
            _priceHint.text = Guild.GuildLaws.LootShare < 1f
                ? UIBuilder.Colored($"길드 규칙 '전리품 길드 귀속': 매입가의 {Guild.GuildLawRules.LootFeeShare * 100f:0}%만 수고비로 줍니다.", UITheme.Warn)
                : "매입가를 높이면 모험가가 더 많은 골드를 벌어 장비와 물약을 사고, 낮추면 길드 자금을 아낄 수 있습니다.";

            var t = Services.Get<GuildTreasury>();
            var db = Services.Get<GameDatabase>();
            var items = t.Stock.Where(kv => kv.Value > 0).Select(kv => (item: db.Item(kv.Key), count: kv.Value))
                .Where(x => x.item != null).OrderBy(x => x.item.category).ThenBy(x => x.item.displayName).ToList();
            EnsureRows(_content, _rows, Mathf.Max(1, items.Count), parent => UIBuilder.Label(parent, "", 15).Size(-1, 26));
            if (items.Count == 0)
            {
                _rows[0].text = UIBuilder.Colored("창고가 비어 있습니다.", UITheme.TextMuted);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var (item, count) = items[i];
                string cat = item.category switch { ItemCategory.Material => "재료", ItemCategory.Potion => "물약", ItemCategory.Tool => "도구", _ => "장비" };
                string price = item.category == ItemCategory.Material
                    ? $"매입가 {trade.LootPay(item)}G"
                    : $"판매가 {trade.GoodsPrice(item)}G";
                _rows[i].text = $"<color=#{UITheme.Hex(item.color)}>■</color> {item.displayName} x{count}  {UIBuilder.Colored($"[{cat}] 기준가 {item.baseValue}G · {price}", UITheme.TextMuted)}";
            }
        }

        // ---------------- Craft ----------------

        void BuildCraft(RectTransform page)
        {
            _queues = UIBuilder.Label(page, "", 14).Size(-1, 44);
            _queues.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Label(page, "레시피  (목표 재고: 재고가 이보다 적으면 재료가 있을 때 자동 제작, 0 = 끔)", 13, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 20);
            var crafting = Services.Get<CraftingService>();
            foreach (var r in crafting.Recipes)
            {
                var row = UIBuilder.Panel(page, "Recipe", UITheme.PanelBgLight);
                row.Size(-1, 58, -1, 0);
                UIBuilder.HLayout(row, 6, 6).childForceExpandHeight = false;
                var rr = new RecipeRow { Recipe = r };
                rr.Info = UIBuilder.Label(row.transform, "", 14).Size(-1, 50, 1);
                rr.Info.verticalOverflow = VerticalWrapMode.Overflow;
                var recipe = r;
                UIBuilder.Button(row.transform, "-", () => ChangeTarget(recipe, -1), 28, 28);
                rr.Target = UIBuilder.Label(row.transform, "0", 15, TextAnchor.MiddleCenter).Size(30, 28, 0);
                UIBuilder.Button(row.transform, "+", () => ChangeTarget(recipe, +1), 28, 28);
                rr.Order = UIBuilder.Button(row.transform, "제작", () => Order(recipe), 64, 32, new Color(0.25f, 0.45f, 0.3f));
                _recipeRows.Add(rr);
            }
            _craftMessage = UIBuilder.Label(page, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
        }

        void ChangeTarget(RecipeDefinition r, int delta)
        {
            var crafting = Services.Get<CraftingService>();
            crafting.SetTarget(r, crafting.Target(r) + delta);
            Refresh();
        }

        void Order(RecipeDefinition r)
        {
            var crafting = Services.Get<CraftingService>();
            var name = Services.Get<GameDatabase>().Item(r.outputItemId)?.displayName ?? r.outputItemId;
            _craftMessage.text = crafting.TryOrder(r, out var error)
                ? UIBuilder.Colored($"{name} 제작을 주문했습니다.", UITheme.Good)
                : UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }

        void RefreshCraft()
        {
            var crafting = Services.Get<CraftingService>();
            var db = Services.Get<GameDatabase>();
            var t = Services.Get<GuildTreasury>();
            var town = Services.Get<TownService>();

            var q = new StringBuilder();
            foreach (var station in new[] { BuildingType.Apothecary, BuildingType.Blacksmith })
            {
                var jobs = crafting.Queue(station);
                string name = town.Get(station)?.DisplayName ?? station.ToString();
                if (jobs.Count == 0) { q.Append($"{name}: {UIBuilder.Colored("쉬는 중", UITheme.TextMuted)}\n"); continue; }
                var head = jobs[0];
                float pct = Mathf.Clamp01(head.Hours / head.Recipe.hours) * 100f;
                q.Append($"{name}: {db.Item(head.Recipe.outputItemId)?.displayName} {pct:0}%");
                if (jobs.Count > 1) q.Append(UIBuilder.Colored($"  (+{jobs.Count - 1} 대기)", UITheme.TextMuted));
                q.Append('\n');
            }
            _queues.text = q.ToString();

            foreach (var row in _recipeRows)
            {
                var r = row.Recipe;
                var output = db.Item(r.outputItemId);
                var station = town.Get(r.station);
                var sb = new StringBuilder($"{output?.displayName ?? r.outputItemId}  {UIBuilder.Colored($"보유 {t.GetStock(r.outputItemId)}", UITheme.TextMuted)}");
                if (!crafting.IsUnlocked(r))
                    sb.Append("  ").Append(UIBuilder.Colored($"{station?.DisplayName} Lv.{r.requiredTier} 필요", UITheme.Bad));
                sb.Append('\n');
                var parts = r.inputs.Select(c => Have(t.GetStock(c.itemId), c.count, db.Item(c.itemId)?.displayName ?? c.itemId)).ToList();
                if (r.goldCost > 0) parts.Add(Have(t.Gold, r.goldCost, "골드"));
                sb.Append(string.Join("  ", parts)).Append(UIBuilder.Colored($"  · {crafting.EffectiveHours(r):0.#}시간", UITheme.TextMuted));
                row.Info.text = sb.ToString();
                row.Target.text = crafting.Target(r).ToString();
                row.Order.interactable = crafting.CannotCraftReason(r) == null;
            }
        }

        static string Have(int have, int need, string name)
            => UIBuilder.Colored($"{name} {have}/{need}", have >= need ? UITheme.Good : UITheme.Bad);

        protected override void Refresh()
        {
            if (_stockPage == null) return;
            if (_stockPage.gameObject.activeSelf) RefreshStock();
            else RefreshCraft();
        }
    }
}
