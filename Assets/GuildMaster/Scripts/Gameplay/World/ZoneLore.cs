using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// What the zones are to the people of the map (P4-102): how dangerous each is beyond its distance (user decision
    /// 2026-10-24: by zone and by distance — a pocket of danger near the town, havens far out), its name, and the spots
    /// kept for villages. Pure: map data in, zone fields and <see cref="MapData.SettlementSites"/> out.
    /// </summary>
    public static class ZoneLore
    {
        /// <summary>Depth added in the pockets of danger (0..2 scale: 1 is the inner land's edge, about 14 levels) and taken off in the havens.</summary>
        public const float NearPocket = 0.3f, FarPocket = 0.3f, HavenShift = -0.3f;
        /// <summary>Every other zone strays up to this either way.</summary>
        public const float PlainSpread = 0.1f;
        /// <summary>Where the pockets and havens may lie (centre's distance from the town, cells).</summary>
        const float NearPocketMin = 70f, NearPocketMax = 190f, FarPocketMin = 190f, FarPocketMax = 290f, HavenMin = 200f, HavenMax = 330f;
        const int Havens = 2;

        /// <summary>Danger, then names, then village sites.</summary>
        public static void Lay(MapData map, Vector2Int town, GameRandom rng)
        {
            Danger(map, town, rng);
            Name(map, town, rng);
            Settlements(map, town, rng);
        }

        // ---------------- Danger ----------------

        static void Danger(MapData map, Vector2Int town, GameRandom rng)
        {
            var zones = map.Zones;
            if (zones.Count <= 1) return;
            float D(ZoneSite z) => Vector2.Distance(z.Centre, town);
            var farthest = new Dictionary<Region, ZoneSite>();
            foreach (var z in zones)
                if (z.Index != ZoneMap.Home && (!farthest.TryGetValue(z.Region, out var f) || D(z) > D(f))) farthest[z.Region] = z;

            foreach (var z in zones)
            {
                if (z.Index == ZoneMap.Home) continue;
                z.Kind = ZoneKind.Plain;
                z.DangerShift = rng.Range(-PlainSpread, PlainSpread);
            }
            ZoneSite Pick(System.Predicate<ZoneSite> ok)
            {
                var list = zones.FindAll(z => z.Index != ZoneMap.Home && z.Kind == ZoneKind.Plain && ok(z));
                return list.Count == 0 ? null : list[rng.Range(0, list.Count)];
            }
            bool NextTo(ZoneSite z, ZoneKind kind) => z.Neighbours.Exists(n => zones[n].Kind == kind);

            // A pocket on the fields' rim, another further out, never side by side.
            var near = Pick(z => z.Neighbours.Contains(ZoneMap.Home) && D(z) >= NearPocketMin && D(z) <= NearPocketMax);
            if (near != null) { near.Kind = ZoneKind.Pocket; near.DangerShift = NearPocket; }
            var far = Pick(z => D(z) >= FarPocketMin && D(z) <= FarPocketMax && !NextTo(z, ZoneKind.Pocket));
            if (far != null) { far.Kind = ZoneKind.Pocket; far.DangerShift = FarPocket; }
            // Havens far out in different regions, not by a pocket, not a region's farthest (its lord's land).
            var used = new HashSet<Region>();
            for (int k = 0; k < Havens; k++)
            {
                var haven = Pick(z => D(z) >= HavenMin && D(z) <= HavenMax && !used.Contains(z.Region) && !NextTo(z, ZoneKind.Pocket)
                                      && farthest[z.Region] != z);
                if (haven == null) break;
                haven.Kind = ZoneKind.Haven;
                haven.DangerShift = HavenShift;
                used.Add(haven.Region);
            }
        }

        // ---------------- Names ----------------

        static readonly Dictionary<Region, string[]> Adjectives = new()
        {
            [Region.Desert] = new[] { "붉은", "메마른", "타오르는", "황금빛", "바람 부는", "모래바람", "뼈의", "끝없는" },
            [Region.Snow] = new[] { "시린", "하얀", "얼어붙은", "고요한", "푸른", "서리 내린", "눈보라", "잠든" },
            [Region.Swamp] = new[] { "안개 낀", "검은", "축축한", "속삭이는", "질척한", "썩어 가는", "가라앉은", "잿빛" },
            [Region.Wasteland] = new[] { "불타는", "재의", "그을린", "갈라진", "연기 나는", "붉게 타는", "녹아내린", "검게 탄" },
        };
        static readonly string[] PocketAdjectives = { "망자의", "저주받은", "피에 젖은", "울부짖는", "버려진", "죽음의" };
        static readonly string[] HavenAdjectives = { "평온한", "따스한", "고요한", "맑은", "쉼터", "푸른" };

        static string Noun(ZoneSite z)
        {
            if (z.Landmark)
                return z.Region switch
                {
                    Region.Wasteland => "화산",
                    Region.Desert => "메사",
                    Region.Snow => "얼음 호수",
                    _ => "대수렁",
                };
            return (z.Region, z.Character) switch
            {
                (Region.Desert, ZoneCharacter.Open) => "모래언덕",
                (Region.Desert, ZoneCharacter.Rocky) => "협곡",
                (Region.Desert, ZoneCharacter.Lakes) => "오아시스",
                (Region.Desert, _) => "황야",
                (Region.Snow, ZoneCharacter.Woods) => "서리숲",
                (Region.Snow, ZoneCharacter.Lakes) => "빙호",
                (Region.Snow, ZoneCharacter.Rocky) => "설산",
                (Region.Snow, ZoneCharacter.Open) => "눈벌판",
                (Region.Snow, _) => "얼음골",
                (Region.Swamp, ZoneCharacter.Lakes) => "수렁",
                (Region.Swamp, ZoneCharacter.Woods) => "늪숲",
                (Region.Swamp, _) => "습지",
                (Region.Wasteland, ZoneCharacter.Rocky) => "바위산",
                (Region.Wasteland, ZoneCharacter.Lakes) => "용암 호수",
                (Region.Wasteland, ZoneCharacter.Open) => "잿더미 벌판",
                (Region.Wasteland, _) => "불모지",
                _ => "들판",
            };
        }

        /// <summary>Compass word for a zone seen from the town (north is up the map).</summary>
        static string Direction(Vector2 from, Vector2 to)
        {
            var d = to - from;
            float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            string[] words = { "동쪽", "북동쪽", "북쪽", "북서쪽", "서쪽", "남서쪽", "남쪽", "남동쪽" };
            return words[Mathf.RoundToInt(a / 45f) % 8];
        }

        static void Name(MapData map, Vector2Int town, GameRandom rng)
        {
            var used = new HashSet<string>();
            foreach (var z in map.Zones)
            {
                if (z.Index == ZoneMap.Home) { z.Name = "마을 들판"; used.Add(z.Name); continue; }
                var words = z.Kind switch
                {
                    ZoneKind.Pocket => PocketAdjectives,
                    ZoneKind.Haven => HavenAdjectives,
                    _ => Adjectives[z.Region],
                };
                string noun = Noun(z), name = null;
                int start = rng.Range(0, words.Length);
                for (int k = 0; k < words.Length && name == null; k++)
                {
                    string word = words[(start + k) % words.Length];
                    // Not the same word twice ("서리 내린 서리숲").
                    if (noun.Contains(word.Substring(0, Mathf.Min(2, word.Length)))) continue;
                    string candidate = $"{word} {noun}";
                    if (!used.Contains(candidate)) name = candidate;
                }
                if (name == null)
                {
                    name = $"{Direction(town, z.Centre)} {noun}";
                    for (int n = 2; used.Contains(name); n++) name = $"{Direction(town, z.Centre)} {noun} {n}";
                }
                z.Name = name;
                used.Add(name);
            }
        }

        // ---------------- Village sites ----------------

        /// <summary>Distances from the town the first villages keep (<see cref="Villages.VillageRules"/>: inner 60–180, outer from 190).</summary>
        const float InnerMin = 60f, InnerMax = 180f, OuterMin = 190f;
        /// <summary>Open ground this far round a village site (its icon and a ring), and from the map's edge.</summary>
        const int SiteHalf = 2, SiteEdge = 12;

        /// <summary>
        /// In each outer region: a site on the inner land, one beyond it (the haven if the region has one) and one more
        /// for a village founded later — each at its zone's heart or the nearest open ground to it. Roads lead to them.
        /// </summary>
        static void Settlements(MapData map, Vector2Int town, GameRandom rng)
        {
            map.SettlementSites.Clear();
            float D(ZoneSite z) => Vector2.Distance(z.Centre, town);
            foreach (Region r in new[] { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland })
            {
                var mine = map.Zones.FindAll(z => z.Region == r && z.Index != ZoneMap.Home && z.Kind != ZoneKind.Pocket);
                var picks = new List<ZoneSite>();
                var inner = mine.FindAll(z => D(z) >= InnerMin && D(z) <= InnerMax);
                if (inner.Count > 0) picks.Add(inner[rng.Range(0, inner.Count)]);
                var outer = mine.FindAll(z => D(z) >= OuterMin && !picks.Contains(z));
                var haven = outer.Find(z => z.Kind == ZoneKind.Haven);
                if (haven != null) picks.Add(haven);
                else if (outer.Count > 0) picks.Add(outer[rng.Range(0, outer.Count)]);
                var rest = mine.FindAll(z => !picks.Contains(z) && D(z) >= InnerMin);
                if (rest.Count > 0) picks.Add(rest[rng.Range(0, rest.Count)]);
                foreach (var z in picks)
                    if (TrySite(map, z, out var cell)) map.SettlementSites.Add(cell);
            }
        }

        static bool TrySite(MapData map, ZoneSite z, out Vector2Int cell)
        {
            var c = z.Heart;
            for (int ring = 0; ring < 30; ring++)
            for (int dy = -ring; dy <= ring; dy++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                var p = new Vector2Int(c.x + dx, c.y + dy);
                if (map.ZoneIds[map.Index(Mathf.Clamp(p.x, 0, map.Width - 1), Mathf.Clamp(p.y, 0, map.SurfaceHeight - 1))] != z.Index) continue;
                if (!Clear(map, p)) continue;
                cell = p;
                return true;
            }
            cell = default;
            return false;
        }

        static bool Clear(MapData map, Vector2Int p)
        {
            if (p.x < SiteEdge || p.y < SiteEdge || p.x >= map.Width - SiteEdge || p.y >= map.SurfaceHeight - SiteEdge) return false;
            for (int dy = -SiteHalf; dy <= SiteHalf; dy++)
            for (int dx = -SiteHalf; dx <= SiteHalf; dx++)
                if (map[p.x + dx, p.y + dy] is not (TileType.Grass or TileType.Sand or TileType.Road)) return false;
            return true;
        }
    }
}
