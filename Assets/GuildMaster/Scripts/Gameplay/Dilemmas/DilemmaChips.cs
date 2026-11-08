using System;
using System.Collections.Generic;

namespace GuildMaster.Dilemmas
{
    /// <summary>A chip under a choice or a result: its words and which way it reads for the guild (+1 good, -1 bad, 0 neither).</summary>
    public readonly struct DilemmaChip
    {
        public readonly string Text;
        public readonly int Sign;

        public DilemmaChip(string text, int sign)
        {
            Text = text;
            Sign = Math.Sign(sign);
        }

        public override string ToString() => Text;
    }

    /// <summary>
    /// The decision card window's chips (P4-135, KMS's event card redesign of 2026-11-05 moved onto the decision cards): under
    /// each choice which way it moves things — the direction only, as the cards have shown it since 2026-10-30 — and under a
    /// result what it actually did, with the numbers, once it has happened (<see cref="DilemmaChangeLog"/>). Pure.
    /// </summary>
    public static class DilemmaChips
    {
        /// <summary>Most chips under one choice, and under a result (the rest is left out).</summary>
        public const int MaxChoiceChips = 5, MaxChangeChips = 8;

        public static DilemmaChip Of(DilemmaHint h) => h switch
        {
            DilemmaHint.GoldUp => new("골드 ▲", 1),
            DilemmaHint.GoldDown => new("골드 ▼", -1),
            DilemmaHint.MoodUp => new("기분 ▲", 1),
            DilemmaHint.MoodDown => new("기분 ▼", -1),
            DilemmaHint.FameUp => new("명성 ▲", 1),
            DilemmaHint.FameDown => new("명성 ▼", -1),
            DilemmaHint.StockUp => new("물자 ▲", 1),
            DilemmaHint.StockDown => new("물자 ▼", -1),
            DilemmaHint.Growth => new("성장", 1),
            DilemmaHint.Tired => new("피로", -1),
            DilemmaHint.Risk => new("운에 맡김", 0),
            DilemmaHint.Later => new("나중에 무슨 일이", 0),
            DilemmaHint.Leaving => new("누군가 떠날 수도", -1),
            DilemmaHint.TownUp => new("주민 평판 ▲", 1),
            _ => new("주민 평판 ▼", -1),
        };

        /// <summary>A choice's chips, in the order its hints were written; "변화 없음" for one that does nothing.</summary>
        public static List<DilemmaChip> Directions(IReadOnlyList<DilemmaHint> hints)
        {
            var result = new List<DilemmaChip>();
            if (hints == null || hints.Count == 0)
            {
                result.Add(new DilemmaChip("변화 없음", 0));
                return result;
            }
            foreach (var h in hints)
                if (result.Count < MaxChoiceChips) result.Add(Of(h));
            return result;
        }

        /// <summary>"+80" / "−6" (a real minus sign, as KMS's chips had it).</summary>
        public static string Signed(float v) => v > 0f ? $"+{v:0}" : $"−{-v:0}";
    }

    /// <summary>
    /// What a decision actually did, gathered while it is carried out: amounts summed by what they are ("골드 +80",
    /// "제나 스트레스 −10"), a bond counted once whichever way round it was written, and things that just happened
    /// ("방문자가 옴"). Amounts under half a point are left out.
    /// </summary>
    public sealed class DilemmaChangeLog
    {
        sealed class Amount
        {
            public string What;
            public float Value;
            public bool GoodWhenUp;
            public bool Bond;
        }

        readonly List<Amount> _amounts = new();
        readonly List<DilemmaChip> _marks = new();

        /// <summary>Adds to what <paramref name="what"/> has moved so far.</summary>
        public void Add(string what, float amount, bool goodWhenUp = true)
        {
            if (string.IsNullOrEmpty(what) || amount == 0f) return;
            var a = _amounts.Find(x => x.What == what && !x.Bond);
            if (a == null) _amounts.Add(new Amount { What = what, Value = amount, GoodWhenUp = goodWhenUp });
            else a.Value += amount;
        }

        /// <summary>
        /// How two get on: a card writes both ways round ("A → B +10", "B → A +10"), which is one change between them — the
        /// larger of the two is kept, not their sum.
        /// </summary>
        public void Bond(string nameA, string nameB, float delta)
        {
            if (string.IsNullOrEmpty(nameA) || string.IsNullOrEmpty(nameB) || delta == 0f) return;
            string what = string.CompareOrdinal(nameA, nameB) <= 0 ? $"{nameA}·{nameB} 사이" : $"{nameB}·{nameA} 사이";
            var a = _amounts.Find(x => x.What == what && x.Bond);
            if (a == null) _amounts.Add(new Amount { What = what, Value = delta, GoodWhenUp = true, Bond = true });
            else if (Math.Abs(delta) > Math.Abs(a.Value)) a.Value = delta;
        }

        /// <summary>Something that happened rather than moved (shown once).</summary>
        public void Mark(string text, int sign)
        {
            if (string.IsNullOrEmpty(text) || _marks.Exists(m => m.Text == text)) return;
            _marks.Add(new DilemmaChip(text, sign));
        }

        /// <summary>The chips: amounts first, in the order they came, then the marks.</summary>
        public List<DilemmaChip> Chips(int max = DilemmaChips.MaxChangeChips)
        {
            var result = new List<DilemmaChip>();
            foreach (var a in _amounts)
            {
                if (Math.Abs(a.Value) < 0.5f) continue;
                result.Add(new DilemmaChip($"{a.What} {DilemmaChips.Signed(a.Value)}", (a.Value > 0f) == a.GoodWhenUp ? 1 : -1));
            }
            result.AddRange(_marks);
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }
    }
}
