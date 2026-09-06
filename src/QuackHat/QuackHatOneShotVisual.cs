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
        private readonly Level _level;
        private readonly SpriteMap _sprite;
        private readonly SpriteThing _thing;
        private readonly QuackHatAnimationDefinition _animation;

        private int _frame;
        private int _ticksRemaining;

        public QuackHatOneShotVisual(
            Level level,
            QuackHatComponentDefinition component,
            QuackHatAnimationDefinition animation,
            QuackHatTransformSnapshot transform)
        {
            _level = level;
            _animation = animation;
            _frame = animation.FirstFrame;
            _ticksRemaining = animation.TicksPerFrame;

            _sprite = new SpriteMap(
                Content.GetTex2D(component.SpriteTexture),
                component.FrameWidth,
                component.FrameHeight)
            {
                frame = _frame
            };
            _thing = new SpriteThing(
                transform.Position.x,
                transform.Position.y,
                _sprite)
            {
                angle = transform.Angle,
                scale = transform.Scale,
                alpha = transform.Alpha,
                offDir = transform.OffDir,
                flipHorizontal = transform.OffDir < 0,
                depth = transform.Depth,
                solid = false,
                enablePhysics = false,
                shouldhavevessel = false,
                shouldbeinupdateloop = false,
                shouldbegraphicculled = false
            };

            _level.AddThing(_thing);
        }

        public bool Update()
        {
            if (_thing.removeFromLevel)
            {
                return false;
            }

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
            _sprite.frame = _frame;
            return true;
        }

        public void Remove()
        {
            if (!_thing.removeFromLevel)
            {
                _level.RemoveThing(_thing);
            }
        }
    }
}
