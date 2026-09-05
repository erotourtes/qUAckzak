using DuckGame;
using HarmonyLib;
using qUAckzak.Mod.Commands;
using qUAckzak.Mod.Modes;

namespace qUAckzak.Mod
{
    public sealed class QuackzakMod : DuckGame.Mod
    {
        protected override void OnPreInitialize()
        {
            base.OnPreInitialize();

            new Harmony("qUAckzak.Mod").PatchAll(typeof(QuackzakMod).Assembly);
        }

        protected override void OnPostInitialize()
        {
            base.OnPostInitialize();

            TurboQuackMode turboQuack = new();
            KonuamiMode konuami = new();

            MonoMain.instance.Components.Add(
                new ModeHost(MonoMain.instance, turboQuack, konuami));

            QuackzakCommands.Register(turboQuack);

            DevConsole.Log("qUAckzak loaded mode: turboqUAck");
            DevConsole.Log("qUAckzak loaded mode: konUAmi");
        }
    }
}
