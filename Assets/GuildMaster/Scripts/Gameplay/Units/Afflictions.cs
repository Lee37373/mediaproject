using System;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Marks left on a unit by the guild's facilities (P4-87, user decision 2026-10-19): poison that burns away a share of
    /// what each hit dealt over a few seconds, armour broken by a share, and a chill that slows moving and striking. Each
    /// lasts a few seconds and another hit only starts the time again; poison adds each hit's share to what is still
    /// burning. Held by every <see cref="Unit"/> and ticked with its status.
    /// </summary>
    public sealed class Afflictions
    {
        /// <summary>Poison burns in steps this far apart (each step a whole number of damage, the rest carried on).</summary>
        public const float PoisonStep = 0.5f;

        float _poisonLeft, _poisonUntil, _poisonCarry, _poisonTimer;
        Unit _poisoner;
        float _armorBreak, _armorUntil;
        float _slow, _slowUntil;

        /// <summary>Share of defence taken off right now.</summary>
        public float ArmorBreak => Time.time < _armorUntil ? _armorBreak : 0f;
        /// <summary>Share taken off moving and attacking speed right now.</summary>
        public float Slow => Time.time < _slowUntil ? _slow : 0f;
        /// <summary>Damage still to burn.</summary>
        public float PoisonLeft => _poisonLeft;
        public bool Poisoned => _poisonLeft > 0f;
        public bool Any => Poisoned || ArmorBreak > 0f || Slow > 0f;

        /// <summary>Adds <paramref name="damage"/> to burn by the time <paramref name="seconds"/> are up (the clock starts again).</summary>
        public void Poison(float damage, float seconds, Unit source)
        {
            if (damage <= 0f) return;
            if (!Poisoned) _poisonTimer = PoisonStep;
            _poisonLeft += damage;
            _poisonUntil = Time.time + seconds;
            _poisoner = source;
        }

        public void BreakArmor(float share, float seconds)
        {
            if (share <= 0f) return;
            _armorBreak = Mathf.Clamp01(share);
            _armorUntil = Time.time + seconds;
        }

        public void Chill(float share, float seconds)
        {
            if (share <= 0f) return;
            _slow = Mathf.Clamp(share, 0f, 0.9f);
            _slowUntil = Time.time + seconds;
        }

        public void Clear()
        {
            _poisonLeft = _poisonCarry = 0f;
            _poisonUntil = _armorUntil = _slowUntil = 0f;
            _poisoner = null;
        }

        /// <summary>Burns the poison (call with the unit's status tick).</summary>
        internal void Tick(Unit self, float dt)
        {
            if (!Poisoned || !self.IsAlive) return;
            _poisonTimer -= dt;
            if (_poisonTimer > 0f) return;
            _poisonTimer += PoisonStep;
            float left = _poisonUntil - Time.time;
            // What is due this step: an even share of the rest over the time left (all of it once the time is up).
            float due = left <= PoisonStep * 0.5f ? _poisonLeft : _poisonLeft * Mathf.Min(1f, PoisonStep / left);
            _poisonLeft -= due;
            _poisonCarry += due;
            if (_poisonLeft < 0.01f) _poisonLeft = 0f;
            float whole = Mathf.Floor(_poisonCarry);
            if (!Poisoned && _poisonCarry - whole >= 0.5f) whole += 1f; // the last step rounds up
            if (whole < 1f) return;
            _poisonCarry = Mathf.Max(0f, _poisonCarry - whole);
            var source = _poisoner != null && _poisoner.IsAlive ? _poisoner : null;
            self.ReceiveDamage(whole, source, false, overTime: true);
            if (!Poisoned) _poisonCarry = 0f;
        }

        /// <summary>A tint for the body while marked (white when not): green poison, blue chill, rust for broken armour.</summary>
        public Color Tint
        {
            get
            {
                if (Poisoned) return new Color(0.72f, 1f, 0.6f);
                if (Slow > 0f) return new Color(0.7f, 0.85f, 1f);
                if (ArmorBreak > 0f) return new Color(1f, 0.82f, 0.68f);
                return Color.white;
            }
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        public class SaveData
        {
            public float poison_left;
            public float poison_until;
            public float poison_carry;
            public float poison_timer;
            public string poisoner;
            public float armor_break;
            public float armor_until;
            public float slow;
            public float slow_until;
        }

        /// <summary>Null when nothing marks the unit (most saves).</summary>
        public SaveData Capture()
        {
            if (!Any) return null;
            return new SaveData
            {
                poison_left = SaveTime.Hundredths(_poisonLeft), poison_until = SaveTime.Rel(_poisonUntil),
                poison_carry = SaveTime.Hundredths(_poisonCarry), poison_timer = SaveTime.Hundredths(_poisonTimer),
                poisoner = _poisoner != null && _poisoner.IsAlive ? _poisoner.UnitId : null,
                armor_break = _armorBreak, armor_until = SaveTime.Rel(_armorUntil),
                slow = _slow, slow_until = SaveTime.Rel(_slowUntil),
            };
        }

        public void Restore(SaveData s)
        {
            Clear();
            if (s == null) return;
            _poisonLeft = s.poison_left;
            _poisonUntil = SaveTime.Abs(s.poison_until);
            _poisonCarry = s.poison_carry;
            _poisonTimer = s.poison_timer;
            _armorBreak = s.armor_break;
            _armorUntil = SaveTime.Abs(s.armor_until);
            _slow = s.slow;
            _slowUntil = SaveTime.Abs(s.slow_until);
        }

        public void Link(SaveData s, SaveContext ctx)
        {
            if (s != null && !string.IsNullOrEmpty(s.poisoner)) _poisoner = ctx.Find<Unit>(s.poisoner);
        }
    }
}
