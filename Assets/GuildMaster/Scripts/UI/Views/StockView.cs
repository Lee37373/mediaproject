using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Guild storage (materials & goods) and trade policy (loot buy rate).</summary>
    public class StockView : UIPanel
    {
        public override string Title => "길드 창고";

        RectTransform _content;
        readonly List<Text> _rows = new();
        Text _rateLabel;
        Text _priceHint;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);

            var policy = UIBuilder.Panel(body, "Policy", UITheme.PanelBgLight);
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

            UIBuilder.Label(body, "보유 품목", 16, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            var trade = Services.Get<TradeService>();
            _rateLabel.text = $"전리품 매입가 배율: {UIBuilder.Colored($"x{trade.LootBuyRate:0.00}", UITheme.Accent)}  ·  상품 판매가 배율 x{trade.GoodsSellRate:0.00}";
            _priceHint.text = "매입가를 높이면 모험가가 더 많은 골드를 벌어 장비와 물약을 사고, 낮추면 길드 자금을 아낄 수 있습니다.";

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
                string cat = item.category switch { ItemCategory.Material => "재료", ItemCategory.Potion => "물약", _ => "장비" };
                string price = item.category == ItemCategory.Material
                    ? $"매입가 {trade.LootPrice(item)}G"
                    : $"판매가 {trade.GoodsPrice(item)}G";
                _rows[i].text = $"<color=#{UITheme.Hex(item.color)}>■</color> {item.displayName} x{count}  {UIBuilder.Colored($"[{cat}] 기준가 {item.baseValue}G · {price}", UITheme.TextMuted)}";
            }
        }
    }
}
