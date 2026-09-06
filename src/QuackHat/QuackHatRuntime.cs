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
        private readonly Dictionary<QuackHatDefinition, QuackHatLevelChoices> _levelChoices =
            new();

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

            foreach (QuackHatDefinition definition in _service.Hats)
            {
                GetLevelChoices(definition, level.seed);
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
                        visual = new QuackHatDuckVisual(
                            level,
                            duck,
                            definition,
                            GetLevelChoices(definition, level.seed));
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
            _levelChoices.Clear();
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

        private QuackHatLevelChoices GetLevelChoices(
            QuackHatDefinition definition,
            int levelSeed)
        {
            if (!_levelChoices.TryGetValue(
                definition,
                out QuackHatLevelChoices choices))
            {
                choices = new QuackHatLevelChoices(definition, levelSeed);
                _levelChoices.Add(definition, choices);
            }

            return choices;
        }
    }

    internal sealed class QuackHatDuckVisual
    {
        private const int EquippedHatDepth = 6;
        private const int ForegroundDepth = Duck.WingDepth + 1;
        private const float RunningSpeedThreshold = 0.1f;

        private readonly Level _level;
        private readonly Duck _duck;
        private readonly IReadOnlyDictionary<string, QuackHatComponentDefinition> _definitions;
        private readonly QuackHatLevelChoices _choices;
        private readonly Dictionary<string, QuackHatVisualComponent> _components =
            new(StringComparer.Ordinal);

        private sbyte _previousOffDir;
        private bool _wasDead;

        public QuackHatDuckVisual(
            Level level,
            Duck duck,
            QuackHatDefinition definition,
            QuackHatLevelChoices choices)
        {
            _level = level;
            _duck = duck;
            _choices = choices;
            Definition = definition;
            _definitions = definition.Components.ToDictionary(
                component => component.Id,
                StringComparer.Ordinal);
            _previousOffDir = duck.offDir;
            _wasDead = duck.dead;

            try
            {
                Dictionary<string, bool> eligibility = new(StringComparer.Ordinal);
                foreach (QuackHatComponentDefinition component in definition.Components)
                {
                    if (!IsRenderableAttached(component, eligibility))
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
                    _components.Add(
                        component.Id,
                        new QuackHatVisualComponent(
                            sprite,
                            thing,
                            _choices.GetAnimations(component.Id)));
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
            QuackHatDuckAnimationState state = new()
            {
                Netted = _duck.inNet,
                Ragdoll = _duck.ragdoll != null,
                Sliding = _duck.sliding,
                Airborne = !_duck.grounded,
                Running = _duck.grounded
                    && Math.Abs(_duck.hSpeed) > RunningSpeedThreshold,
                Crouching = _duck.crouch
            };
            QuackHatAnimationEvents events = new()
            {
                Death = !_wasDead && _duck.dead,
                DirectionChanged = _previousOffDir != 0
                    && _duck.offDir != 0
                    && _previousOffDir != _duck.offDir
            };

            HashSet<string> updated = new(StringComparer.Ordinal);
            foreach (string componentId in _components.Keys)
            {
                UpdateComponent(componentId, state, events, updated);
            }

            _previousOffDir = _duck.offDir;
            _wasDead = _duck.dead;
        }

        public void Remove()
        {
            foreach (QuackHatVisualComponent component in _components.Values)
            {
                SpriteThing thing = component.Thing;
                if (!thing.removeFromLevel)
                {
                    _level.RemoveThing(thing);
                }
            }

            _components.Clear();
        }

        private bool IsRenderableAttached(
            QuackHatComponentDefinition component,
            IDictionary<string, bool> eligibility)
        {
            if (eligibility.TryGetValue(component.Id, out bool eligible))
            {
                return eligible;
            }

            eligible = component.Controller == QuackHatController.Attached
                && component.Emitter == null
                && _choices.IsComponentSelected(component.Id);

            if (eligible && component.ParentKind == QuackHatParentKind.Component)
            {
                eligible = IsRenderableAttached(
                    _definitions[component.ParentComponentId],
                    eligibility);
            }

            eligibility[component.Id] = eligible;
            return eligible;
        }

        private void UpdateComponent(
            string componentId,
            QuackHatDuckAnimationState state,
            QuackHatAnimationEvents events,
            ISet<string> updated)
        {
            if (!updated.Add(componentId))
            {
                return;
            }

            QuackHatComponentDefinition component = _definitions[componentId];
            Thing parent = _duck;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                UpdateComponent(component.ParentComponentId, state, events, updated);
                parent = _components[component.ParentComponentId].Thing;
            }

            QuackHatVisualComponent visual = _components[componentId];
            visual.Animation.Update(state, events);
            visual.Sprite.frame = visual.Animation.Frame;

            SpriteThing thing = visual.Thing;
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
            thing.visible = visual.Animation.Visible
                && parent.visible
                && !_duck.removeFromLevel;
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

        private sealed class QuackHatVisualComponent
        {
            public QuackHatVisualComponent(
                SpriteMap sprite,
                SpriteThing thing,
                IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition> animations)
            {
                Sprite = sprite;
                Thing = thing;
                Animation = new QuackHatAnimationPlayer(animations);
            }

            public SpriteMap Sprite { get; }

            public SpriteThing Thing { get; }

            public QuackHatAnimationPlayer Animation { get; }
        }
    }
}
