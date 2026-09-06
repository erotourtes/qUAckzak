using System;
using System.Collections.Generic;
using System.Linq;
using DuckGame;
using qUAckzak.Mod.Modes;

namespace qUAckzak.Mod.QuackHat
{
    internal sealed class QuackHatRuntime : IModMode
    {
        private readonly QuackHatService _service;
        private readonly Dictionary<Duck, QuackHatDuckVisual> _visuals = new();

        public QuackHatRuntime(QuackHatService service)
        {
            _service = service;
            _service.Reloading += Reset;
        }

        public string Name => "qUAckhat";

        public void Update()
        {
            Level level = Level.current;
            if (Network.isActive || level == null)
            {
                Reset();
                return;
            }

            Duck[] ducks = level.things[typeof(Duck)].Cast<Duck>().ToArray();
            HashSet<Duck> presentDucks = new(ducks);

            foreach (Duck duck in ducks)
            {
                TeamHat teamHat = duck.GetEquipment(typeof(TeamHat)) as TeamHat;
                QuackHatDefinition definition = _service.FindByTeam(teamHat?.team);

                if (duck.removeFromLevel || definition == null)
                {
                    RemoveVisual(duck);
                    continue;
                }

                if (_visuals.TryGetValue(duck, out QuackHatDuckVisual visual)
                    && !ReferenceEquals(visual.Definition, definition))
                {
                    RemoveVisual(duck);
                    visual = null;
                }

                if (visual == null)
                {
                    try
                    {
                        visual = new QuackHatDuckVisual(level, duck, definition);
                        _visuals.Add(duck, visual);
                    }
                    catch (Exception exception)
                    {
                        DevConsole.Log(
                            $"|RED|qUAckhat could not create visuals for '{definition.Name}': {exception.Message}");
                        continue;
                    }
                }

                visual.Update();
            }

            foreach (Duck duck in _visuals.Keys
                .Where(duck => !presentDucks.Contains(duck))
                .ToArray())
            {
                RemoveVisual(duck);
            }
        }

        public void Reset()
        {
            foreach (QuackHatDuckVisual visual in _visuals.Values)
            {
                visual.Remove();
            }

            _visuals.Clear();
        }

        private void RemoveVisual(Duck duck)
        {
            if (!_visuals.TryGetValue(duck, out QuackHatDuckVisual visual))
            {
                return;
            }

            visual.Remove();
            _visuals.Remove(duck);
        }
    }

    internal sealed class QuackHatDuckVisual
    {
        private const int EquippedHatDepth = 6;
        private const int ForegroundDepth = Duck.WingDepth + 1;

        private readonly Level _level;
        private readonly Duck _duck;
        private readonly IReadOnlyDictionary<string, QuackHatComponentDefinition> _definitions;
        private readonly Dictionary<string, SpriteThing> _things = new(StringComparer.Ordinal);

        public QuackHatDuckVisual(
            Level level,
            Duck duck,
            QuackHatDefinition definition)
        {
            _level = level;
            _duck = duck;
            Definition = definition;
            _definitions = definition.Components.ToDictionary(
                component => component.Id,
                StringComparer.Ordinal);

            try
            {
                Dictionary<string, bool> eligibility = new(StringComparer.Ordinal);
                foreach (QuackHatComponentDefinition component in definition.Components)
                {
                    if (!IsStaticAttached(component, eligibility))
                    {
                        continue;
                    }

                    SpriteMap sprite = new(
                        Content.GetTex2D(component.SpriteTexture),
                        component.FrameWidth,
                        component.FrameHeight)
                    {
                        frame = 0
                    };
                    SpriteThing thing = new(duck.x, duck.y, sprite)
                    {
                        solid = false,
                        enablePhysics = false,
                        shouldhavevessel = false,
                        shouldbeinupdateloop = false,
                        shouldbegraphicculled = false
                    };

                    _level.AddThing(thing);
                    _things.Add(component.Id, thing);
                }
            }
            catch
            {
                Remove();
                throw;
            }
        }

        public QuackHatDefinition Definition { get; }

        public void Update()
        {
            HashSet<string> updated = new(StringComparer.Ordinal);
            foreach (string componentId in _things.Keys)
            {
                UpdateComponent(componentId, updated);
            }
        }

        public void Remove()
        {
            foreach (SpriteThing thing in _things.Values)
            {
                if (!thing.removeFromLevel)
                {
                    _level.RemoveThing(thing);
                }
            }

            _things.Clear();
        }

        private bool IsStaticAttached(
            QuackHatComponentDefinition component,
            IDictionary<string, bool> eligibility)
        {
            if (eligibility.TryGetValue(component.Id, out bool eligible))
            {
                return eligible;
            }

            eligible = component.Controller == QuackHatController.Attached
                && component.Animations.Count == 0
                && component.Emitter == null
                && component.Group == null;

            if (eligible && component.ParentKind == QuackHatParentKind.Component)
            {
                eligible = IsStaticAttached(
                    _definitions[component.ParentComponentId],
                    eligibility);
            }

            eligibility[component.Id] = eligible;
            return eligible;
        }

        private void UpdateComponent(string componentId, ISet<string> updated)
        {
            if (!updated.Add(componentId))
            {
                return;
            }

            QuackHatComponentDefinition component = _definitions[componentId];
            Thing parent = _duck;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                UpdateComponent(component.ParentComponentId, updated);
                parent = _things[component.ParentComponentId];
            }

            SpriteThing thing = _things[componentId];
            Vec2 offset = new(component.OffsetX, component.OffsetY);
            thing.position = component.ParentKind == QuackHatParentKind.Duck
                ? _duck.anchorPosition + _duck.OffsetLocal(offset)
                : parent.Offset(offset);
            thing.angle = parent.angle;
            thing.scale = parent.scale;
            thing.alpha = parent.alpha;
            thing.offDir = ResolveFacing(component.Facing, parent);
            thing.flipHorizontal = thing.offDir < 0;
            thing.depth = ResolveDepth(component.RenderLayer, parent.depth);
            thing.visible = parent.visible && !_duck.removeFromLevel;
        }

        private static sbyte ResolveFacing(QuackHatFacing facing, Thing parent)
        {
            // Movement-facing becomes meaningful for follower controllers. An
            // attached component has no independent movement, so it inherits.
            return facing == QuackHatFacing.Fixed ? (sbyte)1 : parent.offDir;
        }

        private Depth ResolveDepth(QuackHatRenderLayer layer, Depth parentDepth)
        {
            return layer switch
            {
                QuackHatRenderLayer.Inherit => parentDepth,
                QuackHatRenderLayer.Behind => _duck.depth + Duck.BackpackDepth,
                QuackHatRenderLayer.Front => _duck.depth + EquippedHatDepth,
                QuackHatRenderLayer.Foreground => _duck.depth + ForegroundDepth,
                _ => parentDepth
            };
        }
    }
}
