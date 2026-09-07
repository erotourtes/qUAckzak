using System;
using System.Collections.Generic;
using System.Linq;
using DuckGame;

namespace qUAckzak.Mod.Modes
{
    internal sealed class QuackternykMode : IModMode
    {
        public const string ToggleTrigger = "QUACKTERNYK";

        private readonly Dictionary<Duck, QuackternykMarker> _markers = new();

        private bool _enabled;

        public string Name => "qUAckternyk";

        public bool Enabled => _enabled;

        public int HighlightedDuckCount => _markers.Count;

        public void Update()
        {
            if (!MonoMain.shouldPauseGameplay)
            {
                InputProfile inputProfile = GetToggleInputProfile();
                if (inputProfile is not null &&
                    inputProfile is not DuckAI &&
                    inputProfile.Pressed(ToggleTrigger))
                {
                    Toggle();
                }
            }

            if (!_enabled || Level.current is not GameLevel level)
            {
                RemoveMarkers();
                return;
            }

            Duck[] livingDucks = level.things[typeof(Duck)]
                .Cast<Duck>()
                .Where(duck => !duck.dead && !duck.removeFromLevel)
                .ToArray();
            HashSet<Duck> presentDucks = new(livingDucks);

            foreach (Duck duck in livingDucks)
            {
                if (!_markers.TryGetValue(duck, out QuackternykMarker marker))
                {
                    marker = new QuackternykMarker(duck.cameraPosition);
                    level.AddThing(marker);
                    _markers.Add(duck, marker);
                }

                marker.Follow(duck);
            }

            foreach (Duck duck in _markers.Keys
                .Where(duck => !presentDucks.Contains(duck))
                .ToArray())
            {
                RemoveMarker(duck);
            }
        }

        public void Reset()
        {
            RemoveMarkers();
        }

        private static InputProfile GetToggleInputProfile()
        {
            Profile profile = Network.isActive
                ? DuckNetwork.localProfile
                : Profiles.DefaultPlayer1;
            if (profile?.inputProfile is InputProfile inputProfile)
            {
                return inputProfile;
            }

            return Network.isActive ? null : InputProfile.DefaultPlayer1;
        }

        private void Toggle()
        {
            _enabled = !_enabled;

            string status = _enabled ? "|LIME|enabled" : "|RED|disabled";
            HUD.AddPlayerChangeDisplay($"qUAckternyk {status}", 2f);
            DevConsole.Log($"qUAckternyk {(_enabled ? "enabled" : "disabled")}");
        }

        private void RemoveMarker(Duck duck)
        {
            if (!_markers.TryGetValue(duck, out QuackternykMarker marker))
            {
                return;
            }

            marker.Remove();
            _markers.Remove(duck);
        }

        private void RemoveMarkers()
        {
            foreach (QuackternykMarker marker in _markers.Values)
            {
                marker.Remove();
            }

            _markers.Clear();
        }
    }

    internal sealed class QuackternykMarker : Thing
    {
        private const float PulseSpeed = 0.12f;
        private const float MarkerHeight = 14f;
        private const float MarkerHalfWidth = 4f;
        private const float MarkerArmHeight = 4f;

        private float _phase;

        public QuackternykMarker(Vec2 position)
            : base(position.x, position.y)
        {
            IgnoreNetworkSync();
            ignoreGhosting = true;
            shouldbeinupdateloop = false;
            shouldhavevessel = false;
            shouldbegraphicculled = false;
            depth = (Depth)0.95f;
        }

        public void Follow(Duck duck)
        {
            position = duck.cameraPosition;
            _phase += PulseSpeed;
            if (_phase > Math.PI * 2)
            {
                _phase -= (float)(Math.PI * 2);
            }
        }

        public override void Draw()
        {
            float wave = ((float)Math.Sin(_phase) + 1f) * 0.5f;
            float opacity = 0.65f + wave * 0.35f;
            float bob = wave * 2f;
            Vec2 tip = position + new Vec2(0f, -MarkerHeight - bob);
            Vec2 left = tip + new Vec2(-MarkerHalfWidth, -MarkerArmHeight);
            Vec2 right = tip + new Vec2(MarkerHalfWidth, -MarkerArmHeight);

            Graphics.DrawLine(left, tip, Color.Black * opacity, 3f, depth);
            Graphics.DrawLine(right, tip, Color.Black * opacity, 3f, depth);
            Graphics.DrawLine(left, tip, Color.Lime * opacity, 1.25f, depth);
            Graphics.DrawLine(right, tip, Color.Lime * opacity, 1.25f, depth);
        }

        public void Remove()
        {
            if (level is not null && !removeFromLevel)
            {
                level.RemoveThing(this);
            }
        }
    }
}
