using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Places;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Gone to a place worth going to (P4-105b, user decision 2026-10-26): a hot spring to soak in, a shrine to pray at, an ore vein
    /// or a herb patch to gather from. It walks there on its own — fighting what comes at it, pulling back when hurt, making camp if
    /// night falls far out — stays its hours beside it (<see cref="PlaceRules.Hours"/>; only the time there counts, a fight does
    /// not), takes what the place gives (<see cref="PlaceVisits.Finish"/>) and walks home with it. A walk that drags on past
    /// <see cref="PlaceRules.GiveUpHours"/> is given up; a gathering spot someone else got to first, or that is picked clean, is
    /// walked away from.
    /// </summary>
    public class PlaceVisitState : State<Adventurer>
    {
        /// <summary>Close enough to its spot to be there.</summary>
        const float Reach = 1.2f;
        /// <summary>Knocked this far off its spot (a fight), it walks back to it.</summary>
        const float Drifted = 3f;
        const float FxEvery = 0.9f;

        PlaceSite _place;
        Vector2 _spot;
        bool _arrived;
        float _hoursThere, _setOut, _campCheck, _fxTimer;

        public PlaceSite Place => _place;

        /// <summary>Where it goes: set before the state is entered (a camp on the way resumes the same walk).</summary>
        public void Go(Adventurer a, PlaceSite place)
        {
            _place = place;
            _arrived = false;
            _hoursThere = 0f;
            _setOut = AdvCtx.Clock.TotalHours;
            _spot = place != null ? SpotBeside(place, a) : default;
        }

        public override string Label
            => _place == null ? "장소로 가는 중"
             : !_arrived ? $"{_place.Name()}에 가는 중"
             : _place.Kind switch
             {
                 PlaceKind.HotSpring => "온천욕 중",
                 PlaceKind.Shrine => "기도 중",
                 PlaceKind.OreVein => "광석 캐는 중",
                 _ => "약초 따는 중",
             };

        static PlaceVisits Visits => Services.TryGet<PlaceVisits>(out var v) ? v : null;

        static readonly Vector2Int[] Beside =
        {
            new(1, 0), new(-1, 0), new(1, -1), new(-1, -1), new(0, -1), new(2, 0), new(-2, 0),
        };

        /// <summary>
        /// A walkable cell beside the place (not on its picture), a different one for each visitor — picked by its id, not the
        /// dice, so a loaded game puts it back where it stood.
        /// </summary>
        static Vector2 SpotBeside(PlaceSite p, Adventurer a)
        {
            var grid = Services.Get<WorldService>().Grid;
            var offsets = Beside;
            int start = 0;
            foreach (char ch in a?.Data?.id ?? "") start = (start * 31 + ch) & 0xFFFF;
            start %= offsets.Length;
            for (int i = 0; i < offsets.Length; i++)
            {
                var c = p.Cell + offsets[(start + i) % offsets.Length];
                if (grid.IsWalkable(c)) return GridMap.CellToWorld(c);
            }
            return p.WorldPosition;
        }

        public override void Enter()
        {
            var a = Owner;
            _campCheck = 0f;
            _fxTimer = FxEvery;
            if (_place == null || Visits == null || !Visits.Claim(_place, a)) { _place = null; a.ChangeState(a.ReturnState); return; }
            if (_arrived) return;
            if (!a.Mover.SetDestination(_spot, Reach * 0.5f, RouteChoice.ForParty(a)))
            {
                a.Say("거기엔 갈 수가 없어...");
                GiveUp(a);
            }
        }

        public override void Exit()
        {
            Visits?.Release(_place, Owner);
        }

        void GiveUp(Adventurer a)
        {
            Visits?.Release(_place, a);
            _place = null;
            a.ChangeState(a.ReturnState);
        }

        public override void Tick(float dt)
        {
            var a = Owner;
            if (_place == null) { a.ChangeState(a.ReturnState); return; }
            // Taken on a quest meanwhile (persuaded, or its party's): the plaza sends it on its way.
            if (a.CurrentQuest != null) { GiveUp(a); return; }
            if (a.CheckRetreat()) return;
            if (a.HandleCombat(dt)) return;
            if (a.Looting.Tick(dt, a.WithinPartyLeash)) return;
            // Someone else picked it clean first.
            if (PlaceRules.IsGathering(_place.Kind) && Services.TryGet<PlaceService>(out var places) && !places.IsReady(_place))
            {
                a.Say("벌써 누가 다 가져갔네.");
                GiveUp(a);
                return;
            }

            if (!_arrived)
            {
                if (AdvCtx.TryNightCamp(a, ref _campCheck, dt)) return;
                if (AdvCtx.Clock.TotalHours - _setOut > PlaceRules.GiveUpHours) { a.Say("너무 멀다. 돌아가자."); GiveUp(a); return; }
                if (Vector2.Distance(a.Position, _spot) <= Reach)
                {
                    _arrived = true;
                    a.Mover.Stop();
                    if (GameRandom.Shared.Chance(0.5f)) a.Say(ArriveLine(_place.Kind), toFeed: false);
                    return;
                }
                if (!a.Mover.IsMoving && !a.Mover.SetDestination(_spot, Reach * 0.5f, RouteChoice.ForParty(a))) GiveUp(a);
                return;
            }

            // Pushed off its spot by a fight: back to it before the stay goes on.
            if (Vector2.Distance(a.Position, _spot) > Drifted)
            {
                if (!a.Mover.IsMoving) a.Mover.SetDestination(_spot, Reach * 0.5f, RouteChoice.ForParty(a));
                return;
            }
            if (a.Mover.IsMoving) return;
            a.Visual?.FaceTowards(_place.WorldPosition);
            ShowWork(a, dt);
            _hoursThere += dt / AdvCtx.Clock.RealSecondsPerGameHour;
            if (_hoursThere < PlaceRules.Hours(_place.Kind)) return;

            var place = _place;
            int got = Visits?.Finish(a, place) ?? 0;
            _place = null;
            a.Say(DoneLine(place.Kind, got));
            a.ChangeState(a.ReturnState);
        }

        /// <summary>Steam off a spring, a glow at a shrine, chips and leaves off a gathering.</summary>
        void ShowWork(Adventurer a, float dt)
        {
            _fxTimer -= dt;
            if (_fxTimer > 0f || !Services.TryGet<FxService>(out var fx)) return;
            _fxTimer = FxEvery * GameRandom.Shared.Range(0.7f, 1.3f);
            var at = _place.WorldPosition;
            switch (_place.Kind)
            {
                case PlaceKind.HotSpring:
                    fx.Spark(at + new Vector2(GameRandom.Shared.Range(-0.5f, 0.5f), 0.4f), new Color(1f, 1f, 1f, 0.55f), 0.6f, 0.6f);
                    break;
                case PlaceKind.Shrine:
                    fx.Spark(a.Position + new Vector2(0f, 1.1f), new Color(1f, 0.92f, 0.55f), 0.5f, 0.4f);
                    break;
                default:
                    var c = _place.Kind == PlaceKind.OreVein ? new Color(0.75f, 0.75f, 0.8f) : new Color(0.55f, 0.85f, 0.4f);
                    fx.Spark(Vector2.MoveTowards(a.Position, at, 0.5f) + new Vector2(0f, 0.2f), c, 0.4f, 0.15f);
                    break;
            }
        }

        static string ArriveLine(PlaceKind k) => k switch
        {
            PlaceKind.HotSpring => "따끈해 보이는데!",
            PlaceKind.Shrine => "잠시 기도를 올리자.",
            PlaceKind.OreVein => "좋아, 캐 볼까.",
            _ => "이 정도면 넉넉하겠다.",
        };

        static string DoneLine(PlaceKind k, int got) => k switch
        {
            PlaceKind.HotSpring => "개운하다! 이제 돌아가자.",
            PlaceKind.Shrine => "몸에 힘이 차오르는 것 같아.",
            PlaceKind.OreVein => got > 0 ? $"철광석 {got}개, 이 정도면 됐다." : "돌아가자.",
            _ => got > 0 ? $"약초 {got}개 챙겼다." : "돌아가자.",
        };

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved
        {
            public int place = -1;
            public bool arrived;
            public float hours_there, set_out;
        }

        public override string SaveState() => JsonUtility.ToJson(new Saved
        {
            place = _place?.Index ?? -1, arrived = _arrived, hours_there = _hoursThere, set_out = _setOut,
        });

        public override void ResumeState(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            var places = Services.TryGet<PlaceService>(out var p) ? p : null;
            _place = places != null && s.place >= 0 && s.place < places.All.Count ? places.All[s.place] : null;
            _arrived = s.arrived;
            _hoursThere = s.hours_there;
            _setOut = s.set_out;
            _spot = _place != null ? SpotBeside(_place, Owner) : default;
            Enter();
        }
    }
}
