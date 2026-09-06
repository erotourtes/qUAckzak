using DuckGame;
using qUAckzak.Mod.Modes;
using qUAckzak.Mod.QuackHat;

namespace qUAckzak.Mod.Commands
{
    internal static class QuackzakCommands
    {
        private const string Prefix = "quackzak_";

        public static void Register(
            TurboQuackMode turboQuack,
            QuackHatService quackHat,
            QuackHatRuntime quackHatRuntime)
        {
            CMD statusCommand = new(
                Prefix + "turboquack_status",
                () => LogTurboQuackStatus(turboQuack))
            {
                description = "Reports whether turboqUAck is enabled or disabled."
            };

            DevConsole.AddCommand(statusCommand);

            CMD quackHatStatusCommand = new(
                Prefix + "quackhat_status",
                () => LogQuackHatStatus(quackHat, quackHatRuntime))
            {
                description = "Reports loaded qUAckhat packages and manifest errors."
            };

            CMD quackHatReloadCommand = new(
                Prefix + "quackhat_reload",
                () => ReloadQuackHats(quackHat))
            {
                description = "Reloads and validates all qUAckhat package manifests."
            };

            DevConsole.AddCommand(quackHatStatusCommand);
            DevConsole.AddCommand(quackHatReloadCommand);
        }

        private static void LogTurboQuackStatus(TurboQuackMode turboQuack)
        {
            string status = turboQuack.Enabled ? "|LIME|enabled" : "|RED|disabled";
            DevConsole.Log($"turboqUAck is {status}");
        }

        private static void ReloadQuackHats(QuackHatService quackHat)
        {
            quackHat.Reload();
            quackHat.LogStatus();
        }

        private static void LogQuackHatStatus(
            QuackHatService quackHat,
            QuackHatRuntime quackHatRuntime)
        {
            quackHat.LogStatus();
            quackHatRuntime.LogStatus();
        }
    }
}
