using DuckGame;

namespace qUAckzak.Mod
{
    public sealed class FirstMod : DuckGame.Mod
    {
        protected override void OnPostInitialize()
        {
            base.OnPostInitialize();
            DevConsole.Log("Sirmax.FirstMod loaded");
        }
    }
}
