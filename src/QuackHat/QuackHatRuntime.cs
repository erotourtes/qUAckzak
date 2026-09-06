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
        private const float MovementThreshold = 0.01f;
        private const float GroundProbeAbove = 16f;
        private const float GroundProbeBelowLevel = 32f;

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
                    if (!IsRenderableComponent(component, eligibility))
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

        private bool IsRenderableComponent(
            QuackHatComponentDefinition component,
            IDictionary<string, bool> eligibility)
        {
            if (eligibility.TryGetValue(component.Id, out bool eligible))
            {
                return eligible;
            }

            eligible = component.Controller is
                    QuackHatController.Attached
                    or QuackHatController.FlyingFollower
                    or QuackHatController.GroundFollower
                && component.Emitter == null
                && _choices.IsComponentSelected(component.Id);

            if (eligible && component.ParentKind == QuackHatParentKind.Component)
            {
                eligible = IsRenderableComponent(
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
            QuackHatVisualComponent parentVisual = null;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                UpdateComponent(component.ParentComponentId, state, events, updated);
                parentVisual = _components[component.ParentComponentId];
                parent = parentVisual.Thing;
            }

            QuackHatVisualComponent visual = _components[componentId];
            Vec2 target = GetTarget(component, parent);
            UpdatePosition(component, visual, target);

            visual.IsFollower = component.Controller is
                    QuackHatController.FlyingFollower
                    or QuackHatController.GroundFollower
                || parentVisual?.IsFollower == true;
            visual.FollowerMoving = component.Controller is
                    QuackHatController.FlyingFollower
                    or QuackHatController.GroundFollower
                ? visual.MovedThisTick
                : parentVisual?.FollowerMoving == true;

            QuackHatDuckAnimationState componentState = state;
            componentState.IsFollower = visual.IsFollower;
            componentState.FollowerMoving = visual.FollowerMoving;
            visual.Animation.Update(componentState, events);
            visual.Sprite.frame = visual.Animation.Frame;

            SpriteThing thing = visual.Thing;
            thing.angle = parent.angle;
            thing.scale = parent.scale;
            thing.alpha = parent.alpha;
            thing.offDir = ResolveFacing(component.Facing, parent, visual);
            thing.flipHorizontal = thing.offDir < 0;
            thing.depth = ResolveDepth(component.RenderLayer, parent.depth);
            thing.visible = visual.Animation.Visible
                && parent.visible
                && !_duck.removeFromLevel;
        }

        private Vec2 GetTarget(QuackHatComponentDefinition component, Thing parent)
        {
            Vec2 offset = new(component.OffsetX, component.OffsetY);
            return component.ParentKind == QuackHatParentKind.Duck
                ? _duck.anchorPosition + _duck.OffsetLocal(offset)
                : parent.Offset(offset);
        }

        private void UpdatePosition(
            QuackHatComponentDefinition component,
            QuackHatVisualComponent visual,
            Vec2 target)
        {
            Vec2 previous = visual.Thing.position;
            if (!visual.PositionInitialized)
            {
                visual.Thing.position = component.Controller == QuackHatController.GroundFollower
                    && TryFindGround(target.x, target.y, out Vec2 ground)
                    ? ground
                    : target;
                visual.PositionInitialized = true;
                visual.MovedThisTick = false;
                return;
            }

            switch (component.Controller)
            {
                case QuackHatController.Attached:
                    visual.Thing.position = target;
                    break;

                case QuackHatController.FlyingFollower:
                    visual.Thing.position = MoveTowards(
                        previous,
                        target,
                        component.Speed);
                    break;

                case QuackHatController.GroundFollower:
                    float nextX = MoveTowards(previous.x, target.x, component.Speed);
                    if (TryFindGround(nextX, target.y, out Vec2 ground))
                    {
                        visual.Thing.position = ground;
                    }
                    break;
            }

            Vec2 movement = visual.Thing.position - previous;
            visual.MovedThisTick = movement.length > MovementThreshold;
            if (Math.Abs(movement.x) > MovementThreshold)
            {
                visual.MovementDirection = movement.x < 0f ? (sbyte)-1 : (sbyte)1;
            }
        }

        private bool TryFindGround(
            float x,
            float desiredY,
            out Vec2 ground)
        {
            Vec2 start = new(x, desiredY - GroundProbeAbove);
            float endY = Math.Max(
                start.y + 1f,
                _level.bottomRight.y + GroundProbeBelowLevel);
            Vec2 end = new(x, endY);
            float closestDistance = float.MaxValue;
            ground = Vec2.Zero;

            foreach (IPlatform platform in _level.CollisionLineAll<IPlatform>(start, end))
            {
                if (platform is not Thing thing)
                {
                    continue;
                }

                Vec2 hit = Collision.LinePoint(start, end, thing);
                float distance = hit.y - start.y;
                if (distance < 0f || distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                ground = hit;
            }

            return closestDistance < float.MaxValue;
        }

        private static Vec2 MoveTowards(Vec2 current, Vec2 target, float maximumDistance)
        {
            Vec2 difference = target - current;
            float distance = difference.length;
            return distance <= maximumDistance || distance <= 0f
                ? target
                : current + difference * (maximumDistance / distance);
        }

        private static float MoveTowards(float current, float target, float maximumDistance)
        {
            float difference = target - current;
            if (Math.Abs(difference) <= maximumDistance)
            {
                return target;
            }

            return current + Math.Sign(difference) * maximumDistance;
        }

        private static sbyte ResolveFacing(
            QuackHatFacing facing,
            Thing parent,
            QuackHatVisualComponent visual)
        {
            return facing switch
            {
                QuackHatFacing.Fixed => 1,
                QuackHatFacing.Movement when visual.MovementDirection != 0 =>
                    visual.MovementDirection,
                _ => parent.offDir
            };
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

            public bool PositionInitialized { get; set; }

            public bool MovedThisTick { get; set; }

            public sbyte MovementDirection { get; set; }

            public bool IsFollower { get; set; }

            public bool FollowerMoving { get; set; }
        }
    }
}
