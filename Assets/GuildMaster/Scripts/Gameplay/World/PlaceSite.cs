using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>A kind of place worth going to (P4-105, user decision 2026-10-26: all four).</summary>
    public enum PlaceKind { HotSpring, Shrine, OreVein, HerbPatch, OldCamp, WatchRuin }

    /// <summary>
    /// A place on the map members go to of their own accord (P4-105, G3's smart objects): a hot spring, a shrine, an ore vein or
    /// a herb patch, an abandoned camp, a ruined watchtower. Laid by <see cref="PlacePlacement"/> from the seed, so not saved;
    /// what has happened at each (gathered, climbed) is the place service's.
    /// </summary>
    public class PlaceSite
    {
        public int Index;
        public PlaceKind Kind;
        /// <summary>A walkable surface cell it stands on.</summary>
        public Vector2Int Cell;

        public Vector2 WorldPosition => GridMap.CellToWorld(Cell);

        public static string Name(PlaceKind k) => k switch
        {
            PlaceKind.HotSpring => "온천",
            PlaceKind.Shrine => "신전",
            PlaceKind.OreVein => "광맥",
            PlaceKind.HerbPatch => "약초밭",
            PlaceKind.OldCamp => "버려진 야영지",
            PlaceKind.WatchRuin => "폐허 감시탑",
            _ => k.ToString(),
        };

        public string Name() => Name(Kind);
    }
}
