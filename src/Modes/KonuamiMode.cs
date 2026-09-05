using DuckGame;

namespace qUAckzak.Mod.Modes
{
    internal sealed class KonuamiMode : IModMode
    {
        public const string Trigger = "KONUAMI";

        public string Name => "konUAmi";

        public void Update()
        {
            if (MonoMain.shouldPauseGameplay)
            {
                return;
            }

            Profile profile = Network.isActive
                ? DuckNetwork.localProfile
                : Profiles.DefaultPlayer1;
            Duck duck = profile?.duck;
            InputProfile inputProfile = duck?.inputProfile;

            if (duck is null ||
                inputProfile is null ||
                inputProfile is DuckAI ||
                !duck.isServerForObject ||
                !inputProfile.Pressed(Trigger))
            {
                return;
            }

            // Match Duck's built-in Konami-code behavior. cameraPosition points to
            // the visible ragdoll or net position when the Duck itself is hidden.
            duck.position = duck.cameraPosition;
            duck.Presto();
        }

        public void Reset()
        {
        }
    }
}
