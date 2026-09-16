using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NumberFormat.Patches
{
    /// <summary>
    /// The game never formats numbers (12345 gold, 1250 damage). Rather than chasing the 600+ places that set a label,
    /// this intercepts the text as it is handed to TextMeshPro (TMP_Text.text setter, SetText(string[, bool])) and to
    /// the few legacy UnityEngine.UI.Text labels, and inserts thousands separators into digit runs that look like
    /// plain numbers:
    ///   - not inside a rich-text tag (&lt;color=#CBB396&gt;, &lt;size=12&gt;, &lt;sprite=3&gt; keep their digits),
    ///   - not glued to a letter, '_' or '#' on either side (lobby codes, item ids, hex colours),
    ///   - not part of a date/time (a run followed by '-' '/' ':' and another digit, or preceded by one of those),
    ///   - not the fraction part of a decimal (the integer part IS formatted: 12345.6 -> 12,345.6),
    ///   - no leading zero (0042 is a code, not a number).
    /// Text belonging to an input field is never touched (what you type must stay what you typed).
    /// </summary>
    internal static class NumberPatches
    {
        private static readonly Dictionary<Component, bool> _isInput = new Dictionary<Component, bool>();
        private static int _logged;

        private static NumberFormatConfig Cfg => NumberFormatPlugin.Cfg;

        internal static void Reset() { _isInput.Clear(); _logged = 0; }

        [HarmonyPatch]
        private static class TextSetters
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                var list = new List<MethodBase>();
                MethodBase m;
                m = AccessTools.PropertySetter(typeof(TMP_Text), "text"); if (m != null) list.Add(m);
                m = AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string) }); if (m != null) list.Add(m);
                m = AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string), typeof(bool) }); if (m != null) list.Add(m);
                if (Cfg == null || Cfg.IncludeLegacyText.Value) { m = AccessTools.PropertySetter(typeof(Text), "text"); if (m != null) list.Add(m); }
                return list;
            }

            // __0 = the first argument (named 'value' on the setter, 'sourceText' on SetText)
            private static void Prefix(Component __instance, ref string __0)
            {
                try
                {
                    if (Cfg == null || !Cfg.Enabled.Value || __0 == null || __0.Length < Cfg.MinDigits.Value) return;
                    if (!HasDigitRun(__0, Cfg.MinDigits.Value)) return;
                    if (Cfg.SkipInputFields.Value && IsInputText(__instance)) return;
                    string formatted = Format(__0, Cfg.Separator.Value, Cfg.MinDigits.Value);
                    if ((object)formatted == (object)__0) return;
                    if (Cfg.Verbose.Value && _logged < 40) { _logged++; NumberFormatPlugin.Log.LogInfo("Formatted: '" + Trunc(__0) + "' -> '" + Trunc(formatted) + "'" + (__instance != null ? " (" + __instance.name + ")" : "")); }
                    __0 = formatted;
                }
                catch { }
            }
        }

        private static string Trunc(string s) { return s.Length > 80 ? s.Substring(0, 80) + "..." : s; }

        private static bool IsInputText(Component c)
        {
            if (c == null) return false;
            bool r;
            if (_isInput.TryGetValue(c, out r)) return r;
            try { r = c.GetComponentInParent<TMP_InputField>() != null || c.GetComponentInParent<InputField>() != null; }
            catch { r = false; }
            if (_isInput.Count > 4096) _isInput.Clear();
            _isInput[c] = r;
            return r;
        }

        /// <summary>Cheap pre-check: does the string contain `min` consecutive digits at all?</summary>
        private static bool HasDigitRun(string s, int min)
        {
            int run = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= '0' && s[i] <= '9') { if (++run >= min) return true; }
                else run = 0;
            }
            return false;
        }

        /// <summary>True when the digit run ending right before index `sepIndex` is 1-2 digits long (a date/time part).</summary>
        private static bool ShortRunBefore(string s, int sepIndex)
        {
            int j = sepIndex - 1, len = 0;
            while (j >= 0 && char.IsDigit(s[j])) { len++; j--; }
            return len > 0 && len <= 2;
        }

        /// <summary>True when the digit run starting at `from` is 1-2 digits long (a date/time part).</summary>
        private static bool ShortRunAfter(string s, int from)
        {
            int j = from, len = 0;
            while (j < s.Length && char.IsDigit(s[j])) { len++; j++; }
            return len > 0 && len <= 2;
        }

        /// <summary>Insert separators into qualifying digit runs. Returns the same instance when nothing changed.</summary>
        internal static string Format(string s, string sep, int minDigits)
        {
            StringBuilder sb = null;
            int i = 0, copied = 0, n = s.Length;
            bool inTag = false;
            while (i < n)
            {
                char c = s[i];
                if (inTag) { if (c == '>') inTag = false; i++; continue; }
                if (c == '<') { inTag = true; i++; continue; }
                if (c < '0' || c > '9') { i++; continue; }
                int start = i;
                while (i < n && s[i] >= '0' && s[i] <= '9') i++;
                int len = i - start;
                if (len < minDigits) continue;
                if (s[start] == '0') continue;                                   // 0042: a code
                char before = start > 0 ? s[start - 1] : ' ';
                char after = i < n ? s[i] : ' ';
                char after2 = i + 1 < n ? s[i + 1] : ' ';
                if (char.IsLetter(before) || before == '_' || before == '#') continue;
                if (char.IsLetter(after) || after == '_' || after == '#') continue;
                if (before == '.' && start > 1 && char.IsDigit(s[start - 2])) continue;   // fraction part of a decimal
                // dates and times: 2026-09-15, 09/15/2026, 12:34:56 - a separator with a SHORT (1-2 digit) run on the other side.
                // "12500/12500" (health) and "1200-1500" (damage range) have long runs on both sides and are formatted.
                if ((before == ':' || before == '/' || before == '-') && ShortRunBefore(s, start - 1)) continue;
                if ((after == '-' || after == '/' || after == ':') && ShortRunAfter(s, i + 1)) continue;
                if (after == sep[0] && char.IsDigit(after2)) continue;           // already separated
                if (sb == null) sb = new StringBuilder(n + 8);
                sb.Append(s, copied, start - copied);
                int first = len % 3; if (first == 0) first = 3;
                sb.Append(s, start, first);
                for (int k = start + first; k < i; k += 3) { sb.Append(sep); sb.Append(s, k, 3); }
                copied = i;
            }
            if (sb == null) return s;
            sb.Append(s, copied, n - copied);
            return sb.ToString();
        }
    }
}
