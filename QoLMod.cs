using MioModLoader;
using QoL.Managers;

namespace QoL
{
    public class QoLMod : Mod
    {
        internal static QoLMod? Instance { get; private set; }

        public override void Initialize()
        {
            Instance = this;

            // One manager per feature. Each installs its hooks once and checks its own
            // Enabled flag, so features can be toggled independently at runtime.
            IntroSkipManager.Initialize();

            ModOptions.Register(this);

            LogMessage("Initialization complete");
        }

        internal static void Log(string message) => Instance?.LogMessage(message);
    }
}