using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Guild;
using GuildMaster.Session;
using GuildMaster.Town;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Reward effects on the screen (P4-106 H8, user decision 2026-10-27: coins and the rank banner). Gold the guild earns —
    /// a client's fee, the town's grant, the day's tax, sales to members or the merchant — flies as coins from the guild
    /// counter to the gold on the bar, which counts up as they land (<see cref="HudView.Withheld"/>); with the counter off
    /// screen the number just counts up. A new rank short of S (S is the victory screen) shows a banner across the screen for a
    /// few seconds without stopping the game. Fewer coins at the top speed; off in the settings
    /// (<see cref="GameSettings.RewardEffects"/>). Real time throughout, so a paused game still finishes them.
    /// </summary>
    public class RewardFxLayer : MonoBehaviour
    {
        const int MaxCoins = 10;
        const float CoinSize = 24f, FlightSeconds = 0.75f, Stagger = 0.06f;
        const float BannerIn = 0.3f, BannerHold = 2.6f, BannerOut = 0.6f;

        RectTransform _canvas;
        Sprite _coinSprite;
        readonly List<Coin> _coins = new();
        readonly Stack<RectTransform> _spare = new();

        RectTransform _banner;
        CanvasGroup _bannerGroup;
        Text _bannerTitle, _bannerLine;
        readonly Queue<GuildRankChangedEvent> _ranks = new();
        float _bannerT = -1f;

        struct Coin
        {
            public RectTransform Rect;
            public Vector2 From, Control;
            public float Start;
            public int Share;
        }

        public void Build(RectTransform canvas)
        {
            _canvas = canvas;
            _coinSprite = SpriteFactory.Circle(32, new Color(1f, 0.84f, 0.3f), new Color(0.72f, 0.5f, 0.12f));

            var banner = UIBuilder.Panel(canvas, "RankBanner", new Color(0.12f, 0.09f, 0.18f, 0.92f), raycast: false);
            _banner = banner.rectTransform;
            _banner.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(760, 130));
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.blocksRaycasts = false;
            UIBuilder.VLayout(banner, 4, 14).childAlignment = TextAnchor.MiddleCenter;
            _bannerTitle = UIBuilder.Label(banner.transform, "", 40, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 56);
            _bannerLine = UIBuilder.Label(banner.transform, "", 18, TextAnchor.MiddleCenter).Size(-1, 30);
            _banner.gameObject.SetActive(false);

            EventBus.Subscribe<GoldFlowEvent>(OnGold);
            EventBus.Subscribe<GuildRankChangedEvent>(OnRank);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<GoldFlowEvent>(OnGold);
            EventBus.Unsubscribe<GuildRankChangedEvent>(OnRank);
        }

        static bool Fastest => Services.TryGet<GameClock>(out var c) && c.SpeedIndex >= GameClock.SpeedScales.Length - 1;

        /// <summary>Earnings worth showing (not escrowed quest gold coming back, not members repaying their keep).</summary>
        static bool Shown(string reason) => reason is GoldReason.Commission or GoldReason.Grant or GoldReason.Tax or GoldReason.Goods or GoldReason.Merchant;

        // ---------------- Coins ----------------

        void OnGold(GoldFlowEvent e)
        {
            if (e.Amount <= 0 || !Shown(e.Reason) || !GameSettings.RewardEffects) return;
            if (!Services.TryGet<HudView>(out var hud) || hud.GoldAnchor == null) return;
            var cam = Camera.main;
            var hall = Services.TryGet<TownService>(out var town) ? town.Get(BuildingType.GuildHall) : null;
            if (cam == null || hall == null) return;
            var screen = cam.WorldToScreenPoint(hall.DoorPosition + new Vector2(0f, 1f));
            // The counter out of sight: no coins, the number just counts up.
            if (screen.z < 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen, null, out var from)) return;

            int n = Mathf.Clamp(1 + e.Amount / 25, 2, MaxCoins);
            if (Fastest) n = Mathf.Max(1, n / 3);
            hud.Withheld += e.Amount;
            float now = Time.unscaledTime;
            int left = e.Amount;
            for (int i = 0; i < n; i++)
            {
                int share = i == n - 1 ? left : e.Amount / n;
                left -= share;
                var start = from + Random.insideUnitCircle * 26f;
                _coins.Add(new Coin
                {
                    Rect = TakeCoin(start),
                    From = start,
                    Control = start + new Vector2(Random.Range(-140f, 140f), Random.Range(80f, 220f)),
                    Start = now + i * Stagger,
                    Share = share,
                });
            }
        }

        RectTransform TakeCoin(Vector2 at)
        {
            RectTransform rt;
            if (_spare.Count > 0) rt = _spare.Pop();
            else
            {
                // SpriteImage stretches to its parent: a coin is a fixed little square about the canvas middle.
                rt = UIBuilder.SpriteImage(_canvas, "Coin", _coinSprite, Color.white).rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(CoinSize, CoinSize);
            }
            rt.gameObject.SetActive(true);
            rt.SetAsLastSibling();
            rt.localPosition = at;
            rt.localScale = Vector3.zero;
            return rt;
        }

        void Update()
        {
            UpdateCoins();
            UpdateBanner();
        }

        void UpdateCoins()
        {
            if (_coins.Count == 0) return;
            var hud = Services.TryGet<HudView>(out var h) ? h : null;
            Vector2 to = hud != null && hud.GoldAnchor != null ? (Vector2)_canvas.InverseTransformPoint(hud.GoldAnchor.position) : Vector2.zero;
            float now = Time.unscaledTime;
            for (int i = _coins.Count - 1; i >= 0; i--)
            {
                var c = _coins[i];
                float t = (now - c.Start) / FlightSeconds;
                if (t < 0f) continue;
                if (t >= 1f)
                {
                    // Landed: its share shows on the bar.
                    if (hud != null)
                    {
                        hud.Withheld = Mathf.Max(0, hud.Withheld - c.Share);
                        hud.Pulse();
                    }
                    c.Rect.gameObject.SetActive(false);
                    _spare.Push(c.Rect);
                    _coins.RemoveAt(i);
                    continue;
                }
                // Up and over, faster towards the end.
                float e = t * t;
                var a = Vector2.Lerp(c.From, c.Control, e);
                var b = Vector2.Lerp(c.Control, to, e);
                c.Rect.localPosition = Vector2.Lerp(a, b, e);
                float s = t < 0.15f ? t / 0.15f : 1f - 0.35f * t;
                c.Rect.localScale = new Vector3(s, s, 1f);
            }
        }

        // ---------------- The rank banner ----------------

        void OnRank(GuildRankChangedEvent e)
        {
            if (e.Rank == GuildRank.S || !GameSettings.RewardEffects) return;
            _ranks.Enqueue(e);
        }

        void UpdateBanner()
        {
            if (_bannerT < 0f)
            {
                if (_ranks.Count == 0) return;
                var e = _ranks.Dequeue();
                _bannerTitle.text = $"길드 {GuildRankRules.Name(e.Rank)} 달성!";
                _bannerLine.text = GuildRankRules.Rewards(e.Rank);
                _bannerT = 0f;
                _banner.SetAsLastSibling();
                _banner.gameObject.SetActive(true);
            }
            _bannerT += Time.unscaledDeltaTime;
            float t = _bannerT;
            float scale, alpha;
            if (t < BannerIn)
            {
                float k = t / BannerIn;
                scale = Mathf.LerpUnclamped(0.6f, 1f, 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f)); // a small overshoot
                alpha = k;
            }
            else if (t < BannerIn + BannerHold) { scale = 1f; alpha = 1f; }
            else
            {
                float k = (t - BannerIn - BannerHold) / BannerOut;
                scale = 1f;
                alpha = 1f - k;
                if (k >= 1f)
                {
                    _banner.gameObject.SetActive(false);
                    _bannerT = -1f;
                    return;
                }
            }
            _banner.localScale = new Vector3(scale, scale, 1f);
            _bannerGroup.alpha = Mathf.Clamp01(alpha);
        }
    }
}
