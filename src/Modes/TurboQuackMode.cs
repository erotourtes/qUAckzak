using System.Collections.Generic;
using DuckGame;

namespace qUAckzak.Mod.Modes
{
    internal sealed class TurboQuackMode : IModMode
    {
        public const string ToggleTrigger = "TURBOQUACK";

        private const float RagdollNudgeTimerPerFrame = 0.07f;

        private readonly List<InjectedInput> _injectedInputs = new();

        private bool _enabled;

        public string Name => "turboqUAck";

        public bool Enabled => _enabled;

        public void Update()
        {
            RemoveInjectedInputs();

            if (MonoMain.shouldPauseGameplay)
            {
                return;
            }

            Profile profile = GetCurrentProfile();
            InputProfile toggleInputProfile = GetToggleInputProfile(profile);

            if (toggleInputProfile is not null &&
                toggleInputProfile is not DuckAI &&
                toggleInputProfile.Pressed(ToggleTrigger))
            {
                Toggle();
            }

            Duck duck = profile?.duck;
            InputProfile duckInputProfile = duck?.inputProfile;

            if (duck is null || duckInputProfile is null || duckInputProfile is DuckAI)
            {
                return;
            }

            if (!_enabled)
            {
                return;
            }

            UpdateRagdoll(duck, duckInputProfile);
            UpdateTrappedJump(duck, duckInputProfile);
        }

        public void Reset()
        {
            RemoveInjectedInputs();
        }

        private static Profile GetCurrentProfile()
        {
            return Network.isActive ? DuckNetwork.localProfile : Profiles.DefaultPlayer1;
        }

        private static InputProfile GetToggleInputProfile(Profile profile)
        {
            if (profile?.inputProfile is InputProfile profileInput)
            {
                return profileInput;
            }

            return Network.isActive ? null : InputProfile.DefaultPlayer1;
        }

        private void Toggle()
        {
            _enabled = !_enabled;

            string status = _enabled ? "|LIME|enabled" : "|RED|disabled";
            HUD.AddPlayerChangeDisplay($"turboqUAck {status}", 2f);
            DevConsole.Log($"turboqUAck {(_enabled ? "enabled" : "disabled")}");
        }

        private void UpdateRagdoll(Duck duck, InputProfile inputProfile)
        {
            if (duck.ragdoll is null || duck.dead)
            {
                return;
            }

            if (duck.HasEquipment(typeof(FancyShoes)))
            {
                return;
            }

            string heldDirection = GetHeldDirection(inputProfile);
            if (heldDirection is null)
            {
                return;
            }

            // ModeHost runs after Ragdoll.Update. Queue the press one frame before
            // DGR's nudge timer will cross its > 1 threshold.
            if (duck.ragdoll.jetting ||
                duck.ragdoll._timeSinceNudge + RagdollNudgeTimerPerFrame <= 1f)
            {
                return;
            }

            Inject(inputProfile, heldDirection);
        }

        private void UpdateTrappedJump(Duck duck, InputProfile inputProfile)
        {
            bool isTrapped = duck.inNet || duck.ragdoll?.inSleepingBag == true;
            if (!isTrapped || !inputProfile.Down(Triggers.Jump))
            {
                return;
            }

            Inject(inputProfile, Triggers.Jump);
        }

        private static string GetHeldDirection(InputProfile inputProfile)
        {
            if (inputProfile.Down(Triggers.Left))
            {
                return Triggers.Left;
            }

            if (inputProfile.Down(Triggers.Right))
            {
                return Triggers.Right;
            }

            return inputProfile.Down(Triggers.Up) ? Triggers.Up : null;
        }

        private void Inject(InputProfile inputProfile, string trigger)
        {
            if (trigger is null || inputProfile.doInputs.Contains(trigger))
            {
                return;
            }

            inputProfile.doInputs.Add(trigger);
            _injectedInputs.Add(new InjectedInput(inputProfile, trigger));
        }

        private void RemoveInjectedInputs()
        {
            foreach (InjectedInput input in _injectedInputs)
            {
                input.Profile.doInputs.Remove(input.Trigger);
            }

            _injectedInputs.Clear();
        }

        private sealed class InjectedInput
        {
            public InjectedInput(InputProfile profile, string trigger)
            {
                Profile = profile;
                Trigger = trigger;
            }

            public InputProfile Profile { get; }

            public string Trigger { get; }
        }
    }
}
