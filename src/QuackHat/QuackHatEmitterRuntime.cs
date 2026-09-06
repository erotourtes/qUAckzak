using System;
using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal sealed class QuackHatEmitterRuntime
    {
        private readonly QuackHatEmitterDefinition _definition;
        private readonly Random _random;

        private bool _active;
        private float _progress;
        private int _interval;
        private Vec2 _previousPosition;

        public QuackHatEmitterRuntime(
            QuackHatEmitterDefinition definition,
            Random random)
        {
            _definition = definition;
            _random = random;
        }

        public bool Update(bool conditionActive, Vec2 position)
        {
            if (!conditionActive)
            {
                _active = false;
                _progress = 0f;
                _previousPosition = position;
                return false;
            }

            if (!_active)
            {
                _active = true;
                _interval = NextInterval();
                _progress = _definition.Cadence == QuackHatEmitterCadence.Time
                    ? 1f
                    : 0f;
            }
            else if (_definition.Cadence == QuackHatEmitterCadence.Time)
            {
                _progress += 1f;
            }
            else
            {
                _progress += (position - _previousPosition).length;
            }

            _previousPosition = position;
            if (_progress < _interval)
            {
                return false;
            }

            _progress = 0f;
            _interval = NextInterval();
            return true;
        }

        private int NextInterval()
        {
            if (_definition.MinimumInterval == _definition.MaximumInterval)
            {
                return _definition.MinimumInterval;
            }

            long range = (long)_definition.MaximumInterval
                - _definition.MinimumInterval
                + 1L;
            return _definition.MinimumInterval
                + (int)(_random.NextDouble() * range);
        }
    }
}
