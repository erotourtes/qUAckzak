using System.Collections.Generic;
using DuckGame;

namespace qUAckzak.Mod.Modes
{
    internal sealed class TurboQuackMode : IModMode
    {
        public const string ToggleTrigger = "TURBOQUACK";

        private const float RagdollNudgeTimerPerFrame = 0.07f;
        // Alternating 7- and 8-tick gaps averages 7.5 ticks: 8 presses per
        // second at Duck Game's 60-tick simulation rate, without randomness.
        private const int ShortNetJumpRepeatIntervalTicks = 7;
        private const int LongNetJumpRepeatIntervalTicks = 8;

        private readonly List<InjectedInput> _injectedInputs = new();

        private bool _enabled;
        private bool _netJumpWasHeld;
        private int _netJumpHeldTicks;
        private int _netJumpRepeatIntervalTicks = ShortNetJumpRepeatIntervalTicks;

        public string Name => "turboqUAck";

        public bool Enabled => _enabled;

        public void Update()
        {
            RemoveInjectedInputs();

            if (MonoMain.shouldPauseGameplay)
            {
                ResetNetJumpRepeat();
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
                ResetNetJumpRepeat();
                return;
            }

            if (!_enabled)
            {
                ResetNetJumpRepeat();
                return;
            }

            UpdateRagdoll(duck, duckInputProfile);
            UpdateTrappedJump(duck, duckInputProfile);
        }

        public void Reset()
        {
            RemoveInjectedInputs();
            ResetNetJumpRepeat();
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

        // Previous implementation, retained to document the balance change:
        //
        // private void UpdateTrappedJump(Duck duck, InputProfile inputProfile)
        // {
        //     bool isTrapped = duck.inNet || duck.ragdoll?.inSleepingBag == true;
        //     if (!isTrapped || !inputProfile.Down(Triggers.Jump))
        //     {
        //         return;
        //     }
        //
        //     Inject(inputProfile, Triggers.Jump);
        // }
        //
        // InputProfile treats an injected input as Pressed on every simulation
        // tick. Nets have no internal struggle cooldown, so the old version
        // generated 60 presses per second and escaped much faster than a player
        // could reasonably mash. Sleeping bags do have a native cooldown, so
        // they now receive a pulse only when that cooldown can accept it.
        private void UpdateTrappedJump(Duck duck, InputProfile inputProfile)
        {
            if (duck.dead || !inputProfile.Down(Triggers.Jump))
            {
                ResetNetJumpRepeat();
                return;
            }

            if (duck.inNet)
            {
                UpdateNetJump(inputProfile);
                return;
            }

            ResetNetJumpRepeat();

            Ragdoll ragdoll = duck.ragdoll;
            if (ragdoll?.inSleepingBag != true ||
                ragdoll._timeSinceNudge + RagdollNudgeTimerPerFrame <= 1f)
            {
                return;
            }

            Inject(inputProfile, Triggers.Jump);
        }

        private void UpdateNetJump(InputProfile inputProfile)
        {
            if (!_netJumpWasHeld)
            {
                _netJumpWasHeld = true;
                _netJumpHeldTicks = 0;
                return;
            }

            _netJumpHeldTicks++;
            if (_netJumpHeldTicks < _netJumpRepeatIntervalTicks)
            {
                return;
            }

            _netJumpHeldTicks = 0;
            _netJumpRepeatIntervalTicks =
                _netJumpRepeatIntervalTicks == ShortNetJumpRepeatIntervalTicks
                    ? LongNetJumpRepeatIntervalTicks
                    : ShortNetJumpRepeatIntervalTicks;
            Inject(inputProfile, Triggers.Jump);
        }

        private void ResetNetJumpRepeat()
        {
            _netJumpWasHeld = false;
            _netJumpHeldTicks = 0;
            _netJumpRepeatIntervalTicks = ShortNetJumpRepeatIntervalTicks;
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
