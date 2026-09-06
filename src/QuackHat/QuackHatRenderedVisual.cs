using System.Collections.Generic;
using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal interface IQuackHatRenderedVisual
    {
        void Apply(
            int frame,
            QuackHatTransformSnapshot transform,
            bool visible);

        void Remove();
    }

    internal sealed class QuackHatOfflineRenderedVisual : IQuackHatRenderedVisual
    {
        private readonly Level _level;
        private readonly SpriteMap _sprite;
        private readonly SpriteThing _thing;

        public QuackHatOfflineRenderedVisual(
            Level level,
            QuackHatComponentDefinition component,
            Vec2 position)
        {
            _level = level;
            _sprite = new SpriteMap(
                Content.GetTex2D(component.SpriteTexture),
                component.FrameWidth,
                component.FrameHeight);
            _thing = new SpriteThing(position.x, position.y, _sprite)
            {
                solid = false,
                enablePhysics = false,
                shouldhavevessel = false,
                shouldbeinupdateloop = false,
                shouldbegraphicculled = false
            };
            _level.AddThing(_thing);
        }

        public void Apply(
            int frame,
            QuackHatTransformSnapshot transform,
            bool visible)
        {
            _sprite.frame = frame;
            _thing.position = transform.Position;
            _thing.angle = transform.Angle;
            _thing.scale = transform.Scale;
            _thing.alpha = transform.Alpha;
            _thing.offDir = transform.OffDir;
            _thing.flipHorizontal = transform.OffDir < 0;
            _thing.depth = transform.Depth;
            _thing.visible = visible;
        }

        public void Remove()
        {
            if (!_thing.removeFromLevel)
            {
                _level.RemoveThing(_thing);
            }
        }
    }

    internal sealed class QuackHatNetworkRenderedVisual : IQuackHatRenderedVisual
    {
        private const float NativeNetworkDepth = -0.2f;

        private readonly Level _level;
        private readonly IReadOnlyList<QuackHatNetworkTileDefinition> _definitions;
        private readonly List<TeamHat> _tiles = new();
        private readonly List<bool> _spriteRefreshPending = new();

        public QuackHatNetworkRenderedVisual(
            Level level,
            Duck owner,
            QuackHatComponentDefinition component,
            Vec2 position)
        {
            _level = level;
            _definitions = component.NetworkTiles;

            foreach (QuackHatNetworkTileDefinition tile in _definitions)
            {
                TeamHat teamHat = new(
                    position.x,
                    position.y,
                    tile.FrameTeams[0],
                    owner?.profile)
                {
                    owner = null,
                    solid = false,
                    enablePhysics = false,
                    gravMultiplier = 0f,
                    shouldhavevessel = false,
                    shouldbegraphicculled = false,
                    visible = false
                };
                _level.AddThing(teamHat);
                _tiles.Add(teamHat);
                _spriteRefreshPending.Add(true);
            }
        }

        public void Apply(
            int frame,
            QuackHatTransformSnapshot transform,
            bool visible)
        {
            for (int index = 0; index < _tiles.Count; index++)
            {
                QuackHatNetworkTileDefinition definition = _definitions[index];
                TeamHat tile = _tiles[index];
                Team frameTeam = definition.FrameTeams[frame];
                if (!ReferenceEquals(tile.team, frameTeam))
                {
                    tile.team = frameTeam;
                    _spriteRefreshPending[index] = true;
                }

                Vec2 offset = QuackHatTransform.OffsetLocal(
                    new Vec2(definition.OffsetX, definition.OffsetY),
                    transform.Angle,
                    transform.Scale,
                    transform.OffDir);
                tile.position = transform.Position + offset;
                tile.angle = transform.Angle;
                tile.scale = transform.Scale;
                tile.alpha = transform.Alpha;
                tile.offDir = transform.OffDir;
                tile.depth = NativeNetworkDepth;
                tile.owner = null;
                tile.hSpeed = 0f;
                tile.vSpeed = 0f;
                tile.solid = false;
                tile.enablePhysics = false;
                tile.visible = visible;
                if (tile.active)
                {
                    tile.active = false;
                }
                else if (_spriteRefreshPending[index])
                {
                    _spriteRefreshPending[index] = false;
                    tile.active = true;
                }
            }
        }

        public void Remove()
        {
            foreach (TeamHat tile in _tiles)
            {
                if (!tile.removeFromLevel)
                {
                    _level.RemoveThing(tile);
                }
            }

            _tiles.Clear();
        }
    }

    internal static class QuackHatTransform
    {
        public static Vec2 OffsetLocal(
            Vec2 offset,
            float angle,
            Vec2 scale,
            sbyte offDir)
        {
            Vec2 transformed = offset * scale;
            if (offDir < 0)
            {
                transformed.x *= -1f;
            }

            return transformed.Rotate(angle, Vec2.Zero);
        }
    }
}
