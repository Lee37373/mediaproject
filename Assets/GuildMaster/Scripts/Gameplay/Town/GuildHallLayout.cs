using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>What a member goes into the guild hall for (P4-115).</summary>
    public enum HallUse
    {
        /// <summary>A chair at a table or a stool by the fire: resting.</summary>
        Seat,
        /// <summary>In front of the quest board: picking a quest.</summary>
        Board,
        /// <summary>At the counter: handing in loot, turning in a quest.</summary>
        Counter,
        /// <summary>Round the big table: a party gathering, or waiting for its split.</summary>
        Table,
        /// <summary>Anywhere there is a place: hanging about.</summary>
        Loiter,
    }

    /// <summary>
    /// The guild hall people walk into (P4-115, user decisions 2026-10-31: big from the start, the guild's life seen inside
    /// it, monsters kept out), as cells from its footprint's corner. Walls all round but for the entrance in the middle of the
    /// front; inside (7×6) two round tables and the counter stand in the way, the fire and the quest board are in the back
    /// wall, and the places people take face what they are there for. The cells in the way are the same at every level (the
    /// levels only add pictures).
    /// </summary>
    public static class GuildHallLayout
    {
        public const int Width = 9, Height = 8;

        /// <summary>Its place from the town's middle: the north-west quarter, between the wall and the streets.</summary>
        public static RectInt FootprintAt(Vector2Int townCenter) => new(townCenter.x - 10, townCenter.y + 2, Width, Height);

        public static readonly Vector2Int Entrance = new(4, 0);
        /// <summary>The middle of the room, on the aisle: the guild's "door" that everyone heads for (its <c>DoorCell</c>).</summary>
        public static readonly Vector2Int Lobby = new(4, 3);
        /// <summary>The big round table (parties gather at it) and the small one.</summary>
        public static readonly Vector2Int BigTable = new(2, 3), SmallTable = new(6, 2);
        public static readonly Vector2Int[] Counter = { new(6, 5), new(7, 5) };
        /// <summary>Where the fire and the quest board are in the back wall (their middle, in cells along it).</summary>
        public const float FireX = 2f, BoardX = 5f;

        public static bool IsWall(Vector2Int c) => (c.x == 0 || c.x == Width - 1 || c.y == 0 || c.y == Height - 1) && c != Entrance;

        /// <summary>A cell nobody walks on: a wall, a table, the counter.</summary>
        public static bool Blocks(Vector2Int c) => IsWall(c) || c == BigTable || c == SmallTable || c == Counter[0] || c == Counter[1];

        /// <summary>Inside the walls (the entrance not counted).</summary>
        public static bool IsInside(Vector2Int c) => c.x > 0 && c.x < Width - 1 && c.y > 0 && c.y < Height - 1;

        public readonly struct Spot
        {
            public readonly HallUse Use;
            public readonly Vector2Int Cell;
            /// <summary>What one standing there faces, in cells from the corner.</summary>
            public readonly Vector2 Face;
            public Spot(HallUse use, int x, int y, Vector2 face) { Use = use; Cell = new Vector2Int(x, y); Face = face; }

            /// <summary>Taken for this: a seat at the big table is a seat too, and any place will do to hang about.</summary>
            public bool Serves(HallUse use) => use == HallUse.Loiter || Use == use || use == HallUse.Seat && Use == HallUse.Table;
        }

        static readonly Vector2 Big = new(BigTable.x + 0.5f, BigTable.y + 0.5f), Small = new(SmallTable.x + 0.5f, SmallTable.y + 0.5f);
        static readonly Vector2 Fire = new(FireX, Height - 0.6f), Board = new(BoardX, Height - 0.6f);

        static readonly Spot[] s_spots =
        {
            new(HallUse.Table, 1, 3, Big), new(HallUse.Table, 3, 3, Big), new(HallUse.Table, 2, 2, Big), new(HallUse.Table, 2, 4, Big),
            new(HallUse.Seat, 5, 2, Small), new(HallUse.Seat, 7, 2, Small), new(HallUse.Seat, 6, 1, Small), new(HallUse.Seat, 6, 3, Small),
            new(HallUse.Seat, 1, 6, Fire), new(HallUse.Seat, 2, 6, Fire),
            new(HallUse.Board, 4, 6, Board), new(HallUse.Board, 5, 6, Board),
            new(HallUse.Counter, 6, 4, new Vector2(6.5f, 5.6f)), new(HallUse.Counter, 7, 4, new Vector2(7.5f, 5.6f)),
            new(HallUse.Loiter, 3, 5, Board), new(HallUse.Loiter, 5, 4, new Vector2(6.5f, 5.6f)), new(HallUse.Loiter, 3, 1, Big),
        };

        public static IReadOnlyList<Spot> Spots => s_spots;

        /// <summary>A cell from the corner on the map.</summary>
        public static Vector2Int ToMap(RectInt footprint, Vector2Int cell) => footprint.min + cell;

        /// <summary>A point in cells from the corner on the map.</summary>
        public static Vector2 ToMap(RectInt footprint, Vector2 point) => (Vector2)footprint.min + point;
    }
}
