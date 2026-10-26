using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Quests;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Layers of a member's think tree (P4-104a, G3 — RimWorld's think tree): the first layer with anything worth doing
    /// decides. Urgent reactions (town guard, taking cover, defence) and a standing party's duty (following the leader)
    /// stay in the state machine, which cuts in before a decision is ever asked for.
    /// </summary>
    public enum ThinkLayer
    {
        /// <summary>A mental state (a break, P4-104d) that overrides everything else.</summary>
        Mind,
        /// <summary>Rest when hurt or worn out, a potion, better gear.</summary>
        Body,
        /// <summary>Reasons not to go out at all: leave, a bad wound, the breaking point (F-2, F-6).</summary>
        Hold,
        /// <summary>Quests on the board.</summary>
        Work,
        /// <summary>A newcomer's training (P4-99).</summary>
        Growth,
        /// <summary>Hunting without a quest (P4-37).</summary>
        Roam,
        /// <summary>Staying about the plaza.</summary>
        Leisure,
    }

    /// <summary>One thing a member could do now, with how good it looks to it (0 = not for it).</summary>
    public struct ThinkOption
    {
        public ThinkLayer Layer;
        public AdventurerAction Action;
        public Quest Quest;
        /// <summary>Where a visit goes (P4-105b).</summary>
        public World.PlaceSite Place;
        public float Score;
        /// <summary>What it is, for the "considered" line ("의뢰 '슬라임 토벌'").</summary>
        public string Label;
        public string Reason;
        public string Say;

        public AdventurerDecision ToDecision()
            => new() { Action = Action, QuestId = Quest?.Id, Place = Place, Reason = Reason, Say = Say };
    }

    /// <summary>
    /// Something that offers a member choices with their scores — the board its quests, the training ground its drill, the
    /// member's own body its rest (The Sims' smart objects advertising). A new kind of thing to do is one more source.
    /// </summary>
    public interface IThinkSource
    {
        ThinkLayer Layer { get; }
        void Offer(Adventurer a, IReadOnlyList<Quest> available, List<ThinkOption> into);
    }

    /// <summary>What a member weighed in its latest decision (not saved: the next decision fills it again).</summary>
    public class ThinkRecord
    {
        public readonly List<ThinkOption> Options = new();
        /// <summary>Index into <see cref="Options"/> of what it chose (-1 = nothing came up).</summary>
        public int Chosen = -1;

        public ThinkOption? Choice => Chosen >= 0 && Chosen < Options.Count ? Options[Chosen] : null;

        /// <summary>
        /// The considered line for the inspector: the chosen layer's best few against the choice, and what the layers
        /// before it turned down, e.g. "의뢰 '슬라임 토벌'(고름) · 의뢰 '고블린 토벌' 74% · 안 맞는 의뢰 2건".
        /// </summary>
        public string Describe(int max = 3)
        {
            var choice = Choice;
            if (choice == null) return "-";
            var c = choice.Value;
            var parts = new List<string> { $"{c.Label}(고름)" };
            float best = Options.Where(o => o.Layer == c.Layer).Max(o => o.Score);
            foreach (var o in Options.Where((o, i) => i != Chosen && o.Layer == c.Layer && o.Score > 0f)
                                     .OrderByDescending(o => o.Score).Take(Math.Max(0, max - 1)))
                parts.Add($"{o.Label} {Math.Round(100f * o.Score / Math.Max(0.0001f, best))}%");
            int unfit = Options.Count(o => o.Layer == ThinkLayer.Work && o.Score <= 0f);
            if (unfit > 0) parts.Add($"안 맞는 의뢰 {unfit}건");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// A member's high-level decisions (P4-104a, G3, user decision 2026-10-24): layers in order (<see cref="ThinkLayer"/>), each
    /// asking its sources for options; the first layer with an option worth anything decides. Where a layer only orders
    /// needs (body, hold) the highest wins; among quests it draws from the best few (<see cref="PickWithin"/>,
    /// <see cref="PickAmong"/>) in proportion to their scores, so the same board sends different people different ways.
    /// Layers past the deciding one are never asked — a source's own dice (the hunt's stay-in-town chance) only roll
    /// when its layer is reached.
    /// </summary>
    public class ThinkTree
    {
        /// <summary>Quests this close to the best one are in the draw.</summary>
        public const float PickWithin = 0.7f;
        /// <summary>At most this many are.</summary>
        public const int PickAmong = 3;

        static readonly ThinkLayer[] Layers = (ThinkLayer[])Enum.GetValues(typeof(ThinkLayer));

        readonly List<IThinkSource> _sources;

        public ThinkTree(IEnumerable<IThinkSource> sources) => _sources = sources.ToList();

        /// <summary>The tree every member thinks with: the rules as they were before P4-104, laid out as sources.</summary>
        public static ThinkTree Standard { get; } = new(ThinkSources.Standard());

        public IReadOnlyList<IThinkSource> Sources => _sources;

        /// <summary>Whether a layer draws among its best options (true) or takes the highest (false).</summary>
        public static bool Draws(ThinkLayer layer) => layer == ThinkLayer.Work;

        /// <summary>Decides for this member and keeps what it weighed in <see cref="Adventurer.LastThinking"/>.</summary>
        public AdventurerDecision Decide(Adventurer a, IReadOnlyList<Quest> available, Func<float> roll = null)
        {
            var record = new ThinkRecord();
            available ??= Array.Empty<Quest>();
            foreach (var layer in Layers)
            {
                int from = record.Options.Count;
                foreach (var s in _sources)
                    if (s.Layer == layer) s.Offer(a, available, record.Options);
                // A source may only offer for its own layer.
                for (int i = from; i < record.Options.Count; i++)
                {
                    var o = record.Options[i];
                    o.Layer = layer;
                    record.Options[i] = o;
                }
                int pick = Pick(record.Options, from, roll ?? (() => GameRandom.Shared.Value));
                if (pick < 0) continue;
                record.Chosen = pick;
                break;
            }
            if (a != null) a.LastThinking = record;
            return record.Choice?.ToDecision() ?? AdventurerDecision.Idle("마땅한 의뢰가 없다");
        }

        /// <summary>
        /// Which of <paramref name="options"/> (from <paramref name="from"/> on, all of one layer) is taken, -1 = none is worth
        /// anything (pure). Needs take the highest, the first one offered on a tie; a drawing layer takes one of the best
        /// <see cref="PickAmong"/> within <see cref="PickWithin"/> of the best, in proportion to the scores — the dice only
        /// roll when there is more than one to choose from.
        /// </summary>
        public static int Pick(IReadOnlyList<ThinkOption> options, int from, Func<float> roll)
        {
            int best = -1;
            for (int i = from; i < options.Count; i++)
                if (options[i].Score > 0f && (best < 0 || options[i].Score > options[best].Score)) best = i;
            if (best < 0 || !Draws(options[best].Layer)) return best;

            float floor = options[best].Score * PickWithin;
            var pool = new List<int>();
            for (int i = from; i < options.Count; i++)
                if (options[i].Score > 0f && options[i].Score >= floor) pool.Add(i);
            if (pool.Count <= 1) return best;
            // Best first; on a tie the one offered first.
            pool.Sort((x, y) => options[y].Score != options[x].Score ? options[y].Score.CompareTo(options[x].Score) : x.CompareTo(y));
            if (pool.Count > PickAmong) pool.RemoveRange(PickAmong, pool.Count - PickAmong);

            float total = 0f;
            foreach (int i in pool) total += options[i].Score;
            float r = roll() * total;
            foreach (int i in pool)
            {
                r -= options[i].Score;
                if (r < 0f) return i;
            }
            return pool[pool.Count - 1];
        }
    }
}
