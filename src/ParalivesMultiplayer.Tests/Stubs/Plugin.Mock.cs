using BepInEx.Logging;

namespace ParalivesMultiplayer
{
    public static class Plugin
    {
        static readonly ManualLogSource _log = new ManualLogSource("TestPlugin");

        public static ManualLogSource Log => _log;

        public static void Initialize()
        {
        }
    }
}
