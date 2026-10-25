using System.Collections.Generic;
using GuildMaster.AI;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Walked off its work but not off its party (user decision 2026-10-21, "태업 뒤 귀환": it follows the party): it gave the quest up
    /// and takes no share of the pay, does not go home alone and does not fight — it trails behind the comrades it was out with and
    /// comes home when they do. Used by a mood break's walking off (P4-104d) and by F-2's at the breaking point.
    /// </summary>
    public class TagAlongState : State<Adventurer>
    {
        /// <summary>Kept this far behind whoever it follows.</summary>
        const float TrailDistance = 3.5f;
        const float RepathSeconds = 0.8f;

        readonly List<Adventurer> _comrades = new();
        float _repath;

        public override string Label => "따라다니는 중";

        /// <summary>Who it trails (the first still out); set before the state is entered.</summary>
        public void Follow(IEnumerable<Adventurer> comrades)
        {
            _comrades.Clear();
            if (comrades == null) return;
            foreach (var c in comrades)
                if (c != null && c != Owner && !_comrades.Contains(c)) _comrades.Add(c);
        }

        /// <summary>Whom it trails now: the first of its comrades still alive and out (not home yet).</summary>
        Adventurer Lead()
        {
            foreach (var c in _comrades)
                if (c != null && c.IsAlive && c.IsGuildMember && !AdvCtx.Town.IsInTown(c.Position)) return c;
            return null;
        }

        public override void Enter() => _repath = 0f;

        public override void Tick(float dt)
        {
            var a = Owner;
            var lead = Lead();
            // Its comrades are home, gone or dead: it walks the rest of the way itself.
            if (lead == null) { a.ChangeState(a.ReturnState); return; }
            // Walking home with them: the way home is the same, as one of the party again.
            if (lead.Fsm.Current == lead.ReturnState && !lead.ReturnState.Luring) { a.ChangeState(a.ReturnState); return; }
            _repath -= dt;
            if (_repath > 0f) return;
            _repath = RepathSeconds;
            var to = lead.Position + (a.Position - lead.Position).normalized * TrailDistance;
            if (Vector2.Distance(a.Position, to) > 1f) a.Mover.SetDestination(to, 1f);
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class Saved { public List<string> comrades = new(); }

        public override string SaveState()
        {
            var s = new Saved();
            foreach (var c in _comrades) if (c != null && c.IsAlive) s.comrades.Add(c.UnitId);
            return JsonUtility.ToJson(s);
        }

        public override void ResumeState(string json, SaveContext ctx)
        {
            _comrades.Clear();
            _repath = 0f;
            var s = JsonUtility.FromJson<Saved>(json);
            foreach (var id in s.comrades)
                if (ctx.Find<Adventurer>(id) is { } c) _comrades.Add(c);
        }
    }
}
