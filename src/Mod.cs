using DuckGame;
using HarmonyLib;
using qUAckzak.Mod.Commands;
using qUAckzak.Mod.Modes;
using qUAckzak.Mod.QuackHat;

namespace qUAckzak.Mod
{
    public sealed class QuackzakMod : DuckGame.ClientMod
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
            QuackHatService quackHat = new(GetPath("quackhat"));

            quackHat.Reload();
            quackHat.LogStatus();

            MonoMain.instance.Components.Add(
                new ModeHost(MonoMain.instance, turboQuack, konuami));

            QuackzakCommands.Register(turboQuack, quackHat);

            DevConsole.Log("qUAckzak loaded mode: turboqUAck");
            DevConsole.Log("qUAckzak loaded mode: konUAmi");
            DevConsole.Log("qUAckzak loaded runtime: qUAckhat");
        }
    }
}
