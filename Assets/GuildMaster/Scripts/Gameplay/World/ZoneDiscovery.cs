using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>The guild has seen a zone's heart for the first time (P4-102): it knows the zone by name now.</summary>
    public struct ZoneDiscoveredEvent { public ZoneSite Zone; }

    /// <summary>
    /// Which zones the guild knows (P4-102). A zone's name — and so its place in the log, the quests and under the
    /// minimap — is known once the fog over its heart has lifted (information hiding: no name for land never seen).
    /// Nothing is saved: on a load the zones whose hearts lie in explored land are known again, without telling.
    /// </summary>
    public class ZoneDiscovery : MonoBehaviour
    {
        const float ScanInterval = 1f;

        MapData _map;
        Fog.FogOfWar _fog;
        bool[] _known;
        bool _primed;
        float _timer;

        public void Init(MapData map, Fog.FogOfWar fog)
        {
            _map = map;
            _fog = fog;
            _known = new bool[map.Zones.Count];
            if (_known.Length > ZoneMap.Home) _known[ZoneMap.Home] = true;
        }

        public bool IsKnown(ZoneSite zone) => zone != null && _known != null && zone.Index < _known.Length && _known[zone.Index];

        /// <summary>The zone at a world point, if the guild knows it.</summary>
        public ZoneSite KnownAt(Vector2 world)
        {
            if (_map == null) return null;
            var c = GridMap.WorldToCell(world);
            var zone = _map.ZoneAt(c.x, c.y);
            return IsKnown(zone) ? zone : null;
        }

        void Update()
        {
            if (_map == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            // The first look is quiet: what a loaded game (or the start) has explored already is simply known.
            Scan(!_primed);
            _primed = true;
        }

        void Scan(bool quiet)
        {
            foreach (var z in _map.Zones)
            {
                if (_known[z.Index] || !_fog.IsExploredRaw(z.Heart)) continue;
                _known[z.Index] = true;
                if (!quiet) EventBus.Publish(new ZoneDiscoveredEvent { Zone = z });
            }
        }

        /// <summary>A few words on a zone for the guild: its region and what it seems to be.</summary>
        public static string Describe(ZoneSite z)
        {
            string region = TerrainRegions.Label(z.Region);
            return z.Kind switch
            {
                ZoneKind.Pocket => $"{region}, 불길한 기운이 감도는 곳",
                ZoneKind.Haven => $"{region}, 비교적 평온한 곳",
                _ => region,
            };
        }
    }
}
