using System;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>
    /// Short blessings from skills (P4-126, user request 2026-11-04 "스킬 많이 만들어줘"): attack raised by a share (battle
    /// cry, frenzy), damage taken cut by a share (iron wall), defence raised by a share (blessing) and a shield that soaks up
    /// damage before the HP does. Each lasts a few seconds; a second one of the same kind keeps the stronger share and the
    /// later end. Held by every <see cref="Unit"/>, the counterpart of <see cref="Afflictions"/>.
    /// </summary>
    public sealed class Boons
    {
        float _attack, _attackUntil;
        float _guard, _guardUntil;
        float _defense, _defenseUntil;
        float _shield, _shieldUntil;

        /// <summary>Share added to the attack right now.</summary>
        public float Attack => Time.time < _attackUntil ? _attack : 0f;
        /// <summary>Share taken off every blow received right now.</summary>
        public float Guard => Time.time < _guardUntil ? _guard : 0f;
        /// <summary>Share added to the defence right now.</summary>
        public float Defense => Time.time < _defenseUntil ? _defense : 0f;
        /// <summary>Damage the shield still soaks up.</summary>
        public float Shield => Time.time < _shieldUntil ? _shield : 0f;
        public bool Any => Attack > 0f || Guard > 0f || Defense > 0f || Shield > 0f;

        public void Empower(float share, float seconds) => Keep(ref _attack, ref _attackUntil, Mathf.Clamp(share, 0f, 2f), seconds);
        public void Brace(float share, float seconds) => Keep(ref _guard, ref _guardUntil, Mathf.Clamp(share, 0f, 0.9f), seconds);
        public void Fortify(float share, float seconds) => Keep(ref _defense, ref _defenseUntil, Mathf.Clamp(share, 0f, 2f), seconds);
        public void Ward(float amount, float seconds) => Keep(ref _shield, ref _shieldUntil, Mathf.Max(0f, amount), seconds);

        static void Keep(ref float value, ref float until, float share, float seconds)
        {
            if (share <= 0f || seconds <= 0f) return;
            value = Time.time < until ? Mathf.Max(value, share) : share;
            until = Mathf.Max(until, Time.time + seconds);
        }

        /// <summary>What is left of a blow once the guard and the shield have taken their part.</summary>
        public float Soak(float amount)
        {
            if (amount <= 0f) return amount;
            amount *= 1f - Guard;
            float shield = Shield;
            if (shield <= 0f) return amount;
            float taken = Mathf.Min(shield, amount);
            _shield = shield - taken;
            return amount - taken;
        }

        public void Clear()
        {
            _attack = _guard = _defense = _shield = 0f;
            _attackUntil = _guardUntil = _defenseUntil = _shieldUntil = 0f;
        }

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        public class SaveData
        {
            public float attack;
            public float attack_until;
            public float guard;
            public float guard_until;
            public float defense;
            public float defense_until;
            public float shield;
            public float shield_until;
        }

        /// <summary>Null when nothing blesses the unit (most saves).</summary>
        public SaveData Capture()
        {
            if (!Any) return null;
            return new SaveData
            {
                attack = _attack, attack_until = SaveTime.Rel(_attackUntil),
                guard = _guard, guard_until = SaveTime.Rel(_guardUntil),
                defense = _defense, defense_until = SaveTime.Rel(_defenseUntil),
                shield = SaveTime.Hundredths(_shield), shield_until = SaveTime.Rel(_shieldUntil),
            };
        }

        public void Restore(SaveData s)
        {
            Clear();
            if (s == null) return;
            _attack = s.attack;
            _attackUntil = SaveTime.Abs(s.attack_until);
            _guard = s.guard;
            _guardUntil = SaveTime.Abs(s.guard_until);
            _defense = s.defense;
            _defenseUntil = SaveTime.Abs(s.defense_until);
            _shield = s.shield;
            _shieldUntil = SaveTime.Abs(s.shield_until);
        }
    }
}
