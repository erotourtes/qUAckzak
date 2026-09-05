using DuckGame;
using qUAckzak.Mod.Modes;

namespace qUAckzak.Mod.Commands
{
    internal static class QuackzakCommands
    {
        private const string Prefix = "quackzak_";

        public static void Register(TurboQuackMode turboQuack)
        {
            CMD statusCommand = new(
                Prefix + "turboquack_status",
                () => LogTurboQuackStatus(turboQuack))
            {
                description = "Reports whether turboqUAck is enabled or disabled."
            };

            DevConsole.AddCommand(statusCommand);
        }

        private static void LogTurboQuackStatus(TurboQuackMode turboQuack)
        {
            string status = turboQuack.Enabled ? "|LIME|enabled" : "|RED|disabled";
            DevConsole.Log($"turboqUAck is {status}");
        }
    }
}
