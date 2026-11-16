using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UnityTools.DebugTools
{
    /// <summary>
    /// Lists and drives debug menu controls by section and label, as JSON. Used by the `unity command debugmenu`
    /// CLI commands, and usable from tests: <c>DebugMenuJson.Do("Enemies", "Freeze AI", "true")</c>.
    /// </summary>
    public static class DebugMenuJson
    {
        /// <summary>Every section (or only <paramref name="section"/>) with its controls and their current values.</summary>
        public static string List(string section = "")
        {
            DebugMenu.RemoveDestroyedOwners();
            IEnumerable<string> sections = DebugMenu.Sections
                .Where(s => string.IsNullOrEmpty(section) || s.Title == section)
                .GroupBy(s => s.Title)
                .Select(g => "{" + Field("section", Quote(g.Key)) + "," +
                             Field("controls", Array(g.SelectMany(s => s.Controls).Select(Describe))) + "}");
            return Array(sections);
        }

        /// <summary>
        /// Presses a button, or sets a toggle (true/false), slider (number) or dropdown (option text or index),
        /// then returns the section's values. Returns <c>{"error": ...}</c> if it can't.
        /// </summary>
        public static string Do(string section, string label, string value = "")
        {
            DebugMenu.RemoveDestroyedOwners();
            DebugControl control = DebugMenu.Sections.Where(s => s.Title == section).SelectMany(s => s.Controls)
                .FirstOrDefault(c => c.Label == label);
            if (control == null) return Error($"no control '{label}' in section '{section}' (is the game in Play mode?)");

            string text = (value ?? "").Trim();
            switch (control.Kind)
            {
                case DebugControlKind.Button:
                    control.OnClick?.Invoke();
                    break;

                case DebugControlKind.Toggle:
                    switch (text.ToLowerInvariant())
                    {
                        case "true": case "1": case "on": control.SetBool?.Invoke(true); break;
                        case "false": case "0": case "off": control.SetBool?.Invoke(false); break;
                        default: return Error($"'{label}' is a toggle: pass --value true or false");
                    }
                    break;

                case DebugControlKind.Slider:
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        return Error($"'{label}' is a slider: pass --value <number>");
                    control.SetFloat?.Invoke(number);
                    break;

                case DebugControlKind.IntSlider:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole))
                        return Error($"'{label}' is a whole-number slider: pass --value <integer>");
                    control.SetInt?.Invoke(whole);
                    break;

                case DebugControlKind.Dropdown:
                    int index = IndexOf(control.Options, text);
                    if (index < 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int given) &&
                        given >= 0 && given < control.Options.Count) index = given;
                    if (index < 0) return Error($"'{label}' is a dropdown: pass one of {string.Join(", ", control.Options.Select(o => $"'{o}'"))}");
                    control.SetInt?.Invoke(index);
                    break;

                default:
                    return Error($"'{label}' is a readout and can't be set");
            }

            return List(section);
        }

        private static int IndexOf(IReadOnlyList<string> options, string text)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == text) return i;
            }
            return -1;
        }

        private static string Describe(DebugControl c)
        {
            var fields = new List<string> { Field("label", Quote(c.Label)), Field("kind", Quote(c.Kind.ToString())) };
            switch (c.Kind)
            {
                case DebugControlKind.Readout:
                    fields.Add(Field("value", Quote(c.GetText?.Invoke())));
                    break;
                case DebugControlKind.Toggle:
                    if (c.GetBool != null) fields.Add(Field("value", c.GetBool() ? "true" : "false"));
                    break;
                case DebugControlKind.Slider:
                    if (c.GetFloat != null) fields.Add(Field("value", Number(c.GetFloat())));
                    fields.Add(Field("min", Number(c.Min)));
                    fields.Add(Field("max", Number(c.Max)));
                    break;
                case DebugControlKind.IntSlider:
                    if (c.GetInt != null) fields.Add(Field("value", c.GetInt().ToString(CultureInfo.InvariantCulture)));
                    fields.Add(Field("min", Number(c.Min)));
                    fields.Add(Field("max", Number(c.Max)));
                    break;
                case DebugControlKind.Dropdown:
                    int index = c.GetInt?.Invoke() ?? -1;
                    fields.Add(Field("value", index >= 0 && index < c.Options.Count ? Quote(c.Options[index]) : "null"));
                    fields.Add(Field("options", Array(c.Options.Select(Quote))));
                    break;
            }
            return "{" + string.Join(",", fields) + "}";
        }

        // A few lines of JSON writing, so nothing depends on a JSON package.

        private static string Error(string message) => "{" + Field("error", Quote(message)) + "}";

        private static string Field(string key, string json) => $"{Quote(key)}:{json}";

        private static string Array(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

        private static string Number(float f) =>
            float.IsFinite(f) ? System.Math.Round(f, 3).ToString(CultureInfo.InvariantCulture) : "null";

        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
