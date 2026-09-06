using System.Collections.Generic;

namespace qUAckzak.Mod.QuackHat
{
    internal struct QuackHatDuckAnimationState
    {
        public bool Netted { get; set; }
        public bool Ragdoll { get; set; }
        public bool Sliding { get; set; }
        public bool Airborne { get; set; }
        public bool Running { get; set; }
        public bool Crouching { get; set; }
    }

    internal struct QuackHatAnimationEvents
    {
        public bool Death { get; set; }
        public bool DirectionChanged { get; set; }
    }

    internal sealed class QuackHatAnimationPlayer
    {
        private readonly IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition>
            _animations;

        private QuackHatAnimationDefinition _activeAnimation;
        private QuackHatTrigger _activeTrigger;
        private bool _playingEvent;
        private int _frame;
        private int _ticksRemaining;

        public QuackHatAnimationPlayer(
            IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition> animations)
        {
            _animations = animations;
        }

        public int Frame { get; private set; }

        public bool Visible { get; private set; }

        public void Update(
            QuackHatDuckAnimationState state,
            QuackHatAnimationEvents events)
        {
            if (_animations.Count == 0)
            {
                Frame = 0;
                Visible = true;
                return;
            }

            bool startedEvent = TryStartEvent(events);
            if (!startedEvent && _playingEvent && !PrepareFrame(loop: false))
            {
                _activeAnimation = null;
                _playingEvent = false;
            }

            if (!_playingEvent)
            {
                QuackHatAnimationDefinition stateAnimation = SelectStateAnimation(state);
                if (!ReferenceEquals(_activeAnimation, stateAnimation))
                {
                    Start(stateAnimation, playingEvent: false);
                }
                else if (_activeAnimation != null)
                {
                    PrepareFrame(loop: true);
                }
            }

            Visible = _activeAnimation != null;
            if (!Visible)
            {
                Frame = 0;
                return;
            }

            Frame = _frame;
            _ticksRemaining--;
        }

        private bool TryStartEvent(QuackHatAnimationEvents events)
        {
            if (events.Death
                && _animations.TryGetValue(
                    QuackHatTrigger.Death,
                    out QuackHatAnimationDefinition deathAnimation))
            {
                Start(deathAnimation, playingEvent: true);
                return true;
            }

            if (events.DirectionChanged
                && (!_playingEvent || _activeTrigger != QuackHatTrigger.Death)
                && _animations.TryGetValue(
                    QuackHatTrigger.DirectionChanged,
                    out QuackHatAnimationDefinition directionAnimation))
            {
                Start(directionAnimation, playingEvent: true);
                return true;
            }

            return false;
        }

        private QuackHatAnimationDefinition SelectStateAnimation(
            QuackHatDuckAnimationState state)
        {
            if (state.Netted && TryGet(QuackHatTrigger.Netted, out QuackHatAnimationDefinition animation))
            {
                return animation;
            }

            if (state.Ragdoll && TryGet(QuackHatTrigger.Ragdoll, out animation))
            {
                return animation;
            }

            if (state.Sliding && TryGet(QuackHatTrigger.Sliding, out animation))
            {
                return animation;
            }

            if (state.Airborne && TryGet(QuackHatTrigger.Airborne, out animation))
            {
                return animation;
            }

            if (state.Running && TryGet(QuackHatTrigger.Running, out animation))
            {
                return animation;
            }

            if (state.Crouching && TryGet(QuackHatTrigger.Crouching, out animation))
            {
                return animation;
            }

            if (TryGet(QuackHatTrigger.Idle, out animation))
            {
                return animation;
            }

            return TryGet(QuackHatTrigger.Default, out animation) ? animation : null;
        }

        private bool TryGet(
            QuackHatTrigger trigger,
            out QuackHatAnimationDefinition animation)
        {
            return _animations.TryGetValue(trigger, out animation);
        }

        private void Start(
            QuackHatAnimationDefinition animation,
            bool playingEvent)
        {
            _activeAnimation = animation;
            _playingEvent = playingEvent && animation != null;
            if (animation == null)
            {
                _frame = 0;
                _ticksRemaining = 0;
                return;
            }

            _activeTrigger = animation.Trigger;
            _frame = animation.FirstFrame;
            _ticksRemaining = animation.TicksPerFrame;
        }

        private bool PrepareFrame(bool loop)
        {
            if (_activeAnimation == null)
            {
                return false;
            }

            if (_ticksRemaining > 0)
            {
                return true;
            }

            if (_frame < _activeAnimation.LastFrame)
            {
                _frame++;
                _ticksRemaining = _activeAnimation.TicksPerFrame;
                return true;
            }

            if (!loop)
            {
                return false;
            }

            _frame = _activeAnimation.FirstFrame;
            _ticksRemaining = _activeAnimation.TicksPerFrame;
            return true;
        }
    }
}
