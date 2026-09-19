using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Town;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Travelling merchant window (P4-3): while the stall is open, sell guild stock
    /// (materials, potions, gear) at base × 0.8 × demand. Opened from the side menu (T)
    /// or by clicking the merchant.
    /// </summary>
    public class MerchantView : UIPanel
    {
        public override string Title => "행상인";

        class Row
        {
            public RectTransform Root;
            public string ItemId;
            public Text Info;
            public Button One, Ten, All;
        }

        Text _status;
        Text _message;
        RectTransform _content;
        readonly List<Row> _rows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            _status = UIBuilder.Label(body, "", 16).Size(-1, 48);
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Label(body,
                $"판매가 = 기준가 x{MerchantMarket.SaleRate:0.0} x 수요.  행상인은 바깥 시세로 값을 매깁니다: 이 지역에서 개체가 줄어든 몬스터의 재료일수록 비싸게 사 갑니다(생태계의 힌트). 같은 품목을 팔수록 값이 내려가고, 하루가 지나면 조금씩 회복합니다.",
                13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _message = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
            EventBus.Subscribe<MerchantArrivedEvent>(OnArrived);
        }

        void OnDestroy() => EventBus.Unsubscribe<MerchantArrivedEvent>(OnArrived);

        void OnArrived(MerchantArrivedEvent e)
        {
            if (_message != null) _message.text = "";
        }

        protected override void OnOpened() => _message.text = "";

        Row CreateRow()
        {
            var row = new Row();
            var panel = UIBuilder.Panel(_content, "SellRow", UITheme.PanelBgLight);
            row.Root = panel.rectTransform;
            row.Root.Size(-1, 46, -1, 0);
            UIBuilder.HLayout(panel, 6, 6).childForceExpandHeight = false;
            row.Info = UIBuilder.Label(panel.transform, "", 14).Size(-1, 40, 1);
            row.Info.verticalOverflow = VerticalWrapMode.Overflow;
            row.One = UIBuilder.Button(panel.transform, "1개", () => Sell(row, 1), 52, 30, fontSize: 14);
            row.Ten = UIBuilder.Button(panel.transform, "10개", () => Sell(row, 10), 56, 30, fontSize: 14);
            row.All = UIBuilder.Button(panel.transform, "전부", () => Sell(row, int.MaxValue), 56, 30, new Color(0.25f, 0.45f, 0.3f), 14);
            return row;
        }

        void Sell(Row row, int count)
        {
            var service = Services.Get<MerchantService>();
            if (!service.IsOpen || row.ItemId == null) return;
            var t = Services.Get<GuildTreasury>();
            int n = Mathf.Min(count, t.GetStock(row.ItemId));
            int gold = service.Market.Sell(row.ItemId, n);
            if (gold > 0)
            {
                var name = Services.Get<GameDatabase>().Item(row.ItemId)?.displayName ?? row.ItemId;
                _message.text = UIBuilder.Colored($"{name} x{n} 판매: +{gold}G", UITheme.Good);
            }
            Refresh();
        }

        protected override void Refresh()
        {
            var service = Services.Get<MerchantService>();
            var clock = Services.Get<GameClock>();
            var market = service.Market;
            bool open = service.IsOpen;

            if (open)
            {
                float left = Mathf.Max(0f, service.CloseAtHour - clock.TotalHours);
                _status.text = $"{UIBuilder.Colored("노점 영업 중", UITheme.Good)}  ·  남은 시간 {Mathf.FloorToInt(left)}시간 {Mathf.FloorToInt(left % 1f * 60f):00}분" +
                               $"\n{UIBuilder.Colored($"이번 방문 판매액 {service.EarnedThisVisit}G", UITheme.Accent)}";
            }
            else if (service.Merchant != null)
                _status.text = $"{UIBuilder.Colored(service.Merchant.StateLabel, UITheme.Warn)}\n{UIBuilder.Colored("노점을 열어야 팔 수 있습니다.", UITheme.TextMuted)}";
            else
                _status.text = $"{UIBuilder.Colored("행상인이 마을에 없습니다.", UITheme.TextMuted)}\n다음 방문 예정: {FormatHour(service.NextVisitHour)}";

            var t = Services.Get<GuildTreasury>();
            var db = Services.Get<GameDatabase>();
            var crafting = Services.TryGet<CraftingService>(out var c) ? c : null;
            var items = t.Stock.Where(kv => kv.Value > 0).Select(kv => (item: db.Item(kv.Key), count: kv.Value))
                .Where(x => market.CanSell(x.item))
                .OrderBy(x => x.item.category).ThenByDescending(x => market.Demand(x.item)).ThenBy(x => x.item.displayName).ToList();

            EnsureSellRows(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var (item, count) = items[i];
                var row = _rows[i];
                row.ItemId = item.id;
                float demand = market.Demand(item);
                var demandColor = demand >= 1.15f ? UITheme.Good : demand >= 0.9f ? UITheme.Text : UITheme.Bad;
                string cat = item.category switch { ItemCategory.Material => "재료", ItemCategory.Potion => "물약", _ => "장비" };
                bool usedInCrafting = crafting != null && crafting.Recipes.Any(r => r.inputs.Any(x => x.itemId == item.id));
                row.Info.text =
                    $"<color=#{UITheme.Hex(item.color)}>■</color> {item.displayName} x{count}  {UIBuilder.Colored($"[{cat}]" + (usedInCrafting ? " 제작 재료" : ""), UITheme.TextMuted)}\n" +
                    $"{UIBuilder.Colored(MerchantMarket.DemandLabel(demand), demandColor)} {UIBuilder.Colored($"(x{demand:0.00})", UITheme.TextMuted)}  " +
                    $"개당 {UIBuilder.Colored($"{market.Price(item)}G", UITheme.Accent)}  {UIBuilder.Colored($"기준가 {item.baseValue}G · 전부 {market.Quote(item, count)}G", UITheme.TextMuted)}";
                row.One.interactable = open;
                row.Ten.interactable = open && count >= 2;
                row.All.interactable = open;
            }
            if (items.Count == 0 && _message.text == "")
                _message.text = "팔 수 있는 품목이 없습니다.";
        }

        void EnsureSellRows(int count)
        {
            while (_rows.Count < count) _rows.Add(CreateRow());
            for (int i = 0; i < _rows.Count; i++) _rows[i].Root.gameObject.SetActive(i < count);
        }

        static string FormatHour(float totalHours)
        {
            int day = Mathf.FloorToInt(totalHours / 24f) + 1;
            int hour = Mathf.FloorToInt(totalHours % 24f);
            return $"{day}일차 {hour:00}시경";
        }
    }
}
