using System.Collections.Generic;
using System.Text;

namespace GuildMaster.LLM
{
    /// <summary>
    /// Builds GBNF grammars for flat JSON objects with fixed key order. Constraining the
    /// model this way guarantees parseable output (and valid enum values such as quest ids),
    /// so JsonUtility is sufficient and no lenient parser is needed.
    /// </summary>
    public class GbnfObject
    {
        readonly List<(string key, string rule)> _fields = new();
        readonly Dictionary<string, string> _rules = new();

        const string Common =
            "ws ::= [ \\t\\n]?\n" +
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | \"\\\\\" [\"\\\\/bfnrt]\n";

        /// <summary>String field restricted to one of the given values.</summary>
        public GbnfObject Enum(string key, IEnumerable<string> values)
        {
            var sb = new StringBuilder();
            foreach (var v in values)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append("\"\\\"").Append(EscapeLiteral(v)).Append("\\\"\"");
            }
            if (sb.Length == 0) sb.Append("\"\\\"\\\"\"");
            return Add(key, sb.ToString());
        }

        /// <summary>Free text string of at most maxChars characters.</summary>
        public GbnfObject String(string key, int maxChars = 120)
            => Add(key, $"\"\\\"\" char{{0,{maxChars}}} \"\\\"\"");

        /// <summary>Integer with optional minus sign and up to 'digits' digits (clamp in code).</summary>
        public GbnfObject Int(string key, int digits = 3, bool allowNegative = true)
            => Add(key, (allowNegative ? "\"-\"? " : "") + $"[0-9]{{1,{digits}}}");

        GbnfObject Add(string key, string rule)
        {
            string name = "f-" + key.Replace('_', '-');
            _rules[name] = rule;
            _fields.Add((key, name));
            return this;
        }

        public string Build()
        {
            var sb = new StringBuilder();
            sb.Append("root ::= \"{\" ws ");
            for (int i = 0; i < _fields.Count; i++)
            {
                var (key, rule) = _fields[i];
                if (i > 0) sb.Append("\",\" ws ");
                sb.Append("\"\\\"").Append(key).Append("\\\":\" ws ").Append(rule).Append(" ws ");
            }
            sb.Append("\"}\"\n");
            foreach (var kv in _rules) sb.Append(kv.Key).Append(" ::= ").Append(kv.Value).Append('\n');
            sb.Append(Common);
            return sb.ToString();
        }

        static string EscapeLiteral(string s) => s.Replace("\\", "\\\\\\\\").Replace("\"", "\\\\\\\"");
    }
}
