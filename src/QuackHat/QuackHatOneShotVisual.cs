using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal readonly struct QuackHatTransformSnapshot
    {
        public QuackHatTransformSnapshot(
            Vec2 position,
            float angle,
            Vec2 scale,
            float alpha,
            sbyte offDir,
            Depth depth)
        {
            Position = position;
            Angle = angle;
            Scale = scale;
            Alpha = alpha;
            OffDir = offDir;
            Depth = depth;
        }

        public Vec2 Position { get; }

        public float Angle { get; }

        public Vec2 Scale { get; }

        public float Alpha { get; }

        public sbyte OffDir { get; }

        public Depth Depth { get; }
    }

    internal sealed class QuackHatOneShotVisual
    {
        private readonly IQuackHatRenderedVisual _visual;
        private readonly QuackHatAnimationDefinition _animation;
        private readonly QuackHatTransformSnapshot _transform;

        private int _frame;
        private int _ticksRemaining;

        public QuackHatOneShotVisual(
            Level level,
            QuackHatComponentDefinition component,
            QuackHatAnimationDefinition animation,
            QuackHatTransformSnapshot transform,
            bool networked,
            Duck owner)
        {
            _animation = animation;
            _transform = transform;
            _frame = animation.FirstFrame;
            _ticksRemaining = animation.TicksPerFrame;
            _visual = networked
                ? new QuackHatNetworkRenderedVisual(
                    level,
                    owner,
                    component,
                    transform.Position)
                : new QuackHatOfflineRenderedVisual(
                    level,
                    component,
                    transform.Position);
            _visual.Apply(_frame, _transform, visible: true);
        }

        public bool Update()
        {
            _visual.Apply(_frame, _transform, visible: true);
            _ticksRemaining--;
            if (_ticksRemaining > 0)
            {
                return true;
            }

            if (_frame >= _animation.LastFrame)
            {
                Remove();
                return false;
            }

            _frame++;
            _ticksRemaining = _animation.TicksPerFrame;
            _visual.Apply(_frame, _transform, visible: true);
            return true;
        }

        public void Remove()
        {
            _visual.Remove();
        }
    }
}
