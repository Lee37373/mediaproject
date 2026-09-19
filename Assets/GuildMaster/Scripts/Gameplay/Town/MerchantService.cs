using System.Linq;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>The merchant reached its stall; the stall is open until <see cref="MerchantService.CloseAtHour"/>.</summary>
    public struct MerchantArrivedEvent { public float CloseAtHour; }
    /// <summary>The stall closed and the merchant is walking out of town.</summary>
    public struct MerchantDepartingEvent { }

    /// <summary>
    /// Travelling merchant schedule (P4-3): every <see cref="IntervalHours"/> a merchant walks in
    /// from a gate, sets up a cart on the plaza and buys the guild's stock for
    /// <see cref="StayHours"/> hours (prices in <see cref="MerchantMarket"/>), then leaves.
    /// </summary>
    public class MerchantService : MonoBehaviour
    {
        public const float IntervalHours = 48f;
        public const float StayHours = 6f;
        const float FirstVisitMin = 6f;
        const float FirstVisitMax = 10f;

        GameClock _clock;
        WorldService _world;
        SpriteRenderer _cart;
        Vector2 _stall;

        public MerchantMarket Market { get; private set; }
        public Merchant Merchant { get; private set; }
        /// <summary>True while the stall is open and the guild can sell.</summary>
        public bool IsOpen => Merchant != null && Merchant.Current == Merchant.Phase.Trading;
        public float NextVisitHour { get; private set; }
        public float CloseAtHour { get; private set; }
        /// <summary>Gold the guild earned from the current (or last) visit.</summary>
        public int EarnedThisVisit { get; private set; }

        public void Init(GameClock clock, WorldService world, MerchantMarket market)
        {
            _clock = clock;
            _world = world;
            Market = market;
            _stall = FindStallSpot();
            EventBus.Subscribe<MerchantSaleEvent>(OnSale);
            // First visit comes early so the feature is discoverable.
            NextVisitHour = clock.TotalHours + GameRandom.Shared.Range(FirstVisitMin, FirstVisitMax);
        }

        void OnDestroy() => EventBus.Unsubscribe<MerchantSaleEvent>(OnSale);

        void OnSale(MerchantSaleEvent e) => EarnedThisVisit += e.Gold;

        void Update()
        {
            if (_clock == null) return;
            if (Merchant == null && _clock.TotalHours >= NextVisitHour) StartVisit();
            if (IsOpen && _clock.TotalHours >= CloseAtHour) CloseStall();
        }

        /// <summary>Sends a merchant now (also used by debug commands). False if one is already in town.</summary>
        public bool StartVisit()
        {
            if (Merchant != null) return false;
            NextVisitHour = _clock.TotalHours + IntervalHours;
            var gates = _world.Map.Gates;
            var gate = gates.Count > 0 ? GridMap.CellToWorld(gates[GameRandom.Shared.Range(0, gates.Count)]) : _stall;
            Merchant = Merchant.Create(gate, _stall, transform, OnMerchantArrived, OnMerchantLeft);
            return true;
        }

        void OnMerchantArrived()
        {
            CloseAtHour = _clock.TotalHours + StayHours;
            EarnedThisVisit = 0;
            ShowCart(Merchant.Position);
            EventBus.Publish(new MerchantArrivedEvent { CloseAtHour = CloseAtHour });
        }

        /// <summary>Closes the stall now (normally when the stay is over).</summary>
        public void CloseStall()
        {
            if (Merchant == null || Merchant.Current == Merchant.Phase.Leaving) return;
            HideCart();
            Merchant.Leave();
            EventBus.Publish(new MerchantDepartingEvent());
        }

        void OnMerchantLeft()
        {
            HideCart();
            Merchant = null;
        }

        void ShowCart(Vector2 merchantPos)
        {
            if (_cart == null)
            {
                var go = new GameObject("MerchantCart");
                go.transform.SetParent(transform, false);
                _cart = go.AddComponent<SpriteRenderer>();
                _cart.sprite = CartoonArt.MerchantCart();
            }
            var p = merchantPos + new Vector2(-0.95f, -0.2f);
            _cart.transform.position = p;
            _cart.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(p.y * 8f);
            _cart.gameObject.SetActive(true);
        }

        void HideCart()
        {
            if (_cart != null) _cart.gameObject.SetActive(false);
        }

        /// <summary>A free plaza cell a little west of the town centre, with room for the cart on its left.</summary>
        Vector2 FindStallSpot()
        {
            var town = Services.Get<TownService>();
            bool Free(Vector2Int c) => _world.Grid.IsWalkable(c) && !town.Buildings.Any(b => b.Footprint.Contains(c));
            var center = _world.Map.TownCenter;
            var preferred = center + new Vector2Int(-3, -1);
            for (int r = 0; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                var c = preferred + new Vector2Int(dx, dy);
                if (Free(c) && Free(c + Vector2Int.left)) return GridMap.CellToWorld(c);
            }
            return _world.TownCenterWorld;
        }
    }
}
