using MioModdingApi;
using QoL.Managers;

namespace QoL
{
    /// <summary>
    /// The mod's page in the in-game Mods menu. This class only connects toggles to managers.
    /// Feature logic lives in each manager.
    /// </summary>
    internal static class ModOptions
    {
        private static ToggleOption? skipIntro;

        /// <summary>Registers the mod's page.</summary>
        internal static void Register(QoLMod mod)
        {
            var page = ModMenu.RegisterMod(mod, displayName: "QoL", description: "Quality of Life improvements for MIO.");

            skipIntro = page.RegisterToggle("skip_intro", "Skip intro sequence", defaultValue: true,
                description: "Skips the opening cutscene and the tutorial sequence after it, so you start with Mel waking you up.");
            Bind(skipIntro, IntroSkipManager.SetEnabled);
        }

        /// <summary>Applies a toggle's current value now and again whenever it changes.</summary>
        private static void Bind(ToggleOption toggle, Action<bool> apply)
        {
            apply(toggle.Value);
            toggle.Changed += apply;
        }
    }
}