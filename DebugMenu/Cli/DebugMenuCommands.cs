using Unity.Pipeline.Commands;

namespace UnityTools.DebugTools.Cli
{
    /// <summary>
    /// Drives the debug menu from the command line, through the Pipeline package:
    /// <c>unity command debugmenu list [--section Enemies]</c> and
    /// <c>unity command debugmenu do --section Enemies --label "Freeze AI" --value true</c>.
    /// Sections exist only while their owners are enabled, so both are for Play mode.
    /// </summary>
    public static class DebugMenuCommands
    {
        [CliCommand("debugmenu", "list", "Every debug menu section with its controls and their current values.")]
        public static string List([CliArg("section", "Only this section (optional)")] string section = "")
        {
            return DebugMenuJson.List(section);
        }

        [CliCommand("debugmenu", "do", "Press a debug button, or set a toggle (true/false), slider (number) or dropdown (option text), by section and label. Returns the section's values afterwards.")]
        public static string Do(
            [CliArg("section", "Section title, e.g. Enemies")] string section,
            [CliArg("label", "Control label, e.g. \"Freeze AI\"")] string label,
            [CliArg("value", "Toggles: true/false. Sliders: a number. Dropdowns: an option's text.")] string value = "")
        {
            return DebugMenuJson.Do(section, label, value);
        }
    }
}
