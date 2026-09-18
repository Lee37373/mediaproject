using System;
using System.Text;
using UnityEngine;

namespace GuildMaster.LLM
{
    /// <summary>JSON helpers built on JsonUtility (DTO fields use snake_case names directly).</summary>
    public static class LlmJson
    {
        /// <summary>Parses the first top-level JSON object in the text. Returns false on any failure.</summary>
        public static bool TryParse<T>(string text, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            try
            {
                value = JsonUtility.FromJson<T>(text.Substring(start, end - start + 1));
                return value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string ToJson(object o, bool pretty = false) => JsonUtility.ToJson(o, pretty);

        /// <summary>Escapes a string for safe embedding in prompt text / JSON.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    default: sb.Append(c < ' ' ? ' ' : c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Trims model output text for display (removes quotes/whitespace, caps length).</summary>
        public static string CleanLine(string s, int maxLen = 100)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().Trim('"').Trim();
            return s.Length > maxLen ? s.Substring(0, maxLen - 1) + "…" : s;
        }
    }
}
