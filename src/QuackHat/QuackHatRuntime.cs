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
        private readonly List<QuackHatOneShotVisual> _oneShots = new();

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

            UpdateOneShots();

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
                            GetLevelChoices(definition, level.seed),
                            SpawnOneShot);
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

            foreach (QuackHatOneShotVisual oneShot in _oneShots)
            {
                oneShot.Remove();
            }

            _visuals.Clear();
            _levelChoices.Clear();
            _oneShots.Clear();
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

        private void UpdateOneShots()
        {
            for (int index = _oneShots.Count - 1; index >= 0; index--)
            {
                if (!_oneShots[index].Update())
                {
                    _oneShots.RemoveAt(index);
                }
            }
        }

        private void SpawnOneShot(
            QuackHatComponentDefinition component,
            QuackHatAnimationDefinition animation,
            QuackHatTransformSnapshot transform)
        {
            try
            {
                _oneShots.Add(new QuackHatOneShotVisual(
                    Level.current,
                    component,
                    animation,
                    transform));
            }
            catch (Exception exception)
            {
                DevConsole.Log(
                    $"|RED|qUAckhat could not create effect '{component.Id}': {exception.Message}");
            }
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
        private readonly Action<
            QuackHatComponentDefinition,
            QuackHatAnimationDefinition,
            QuackHatTransformSnapshot> _spawnOneShot;
        private readonly Dictionary<string, QuackHatVisualComponent> _components =
            new(StringComparer.Ordinal);
        private readonly List<QuackHatComponentDefinition> _worldOneShots = new();
        private readonly List<QuackHatEmitterComponent> _emitters = new();

        private sbyte _previousOffDir;
        private bool _wasDead;

        public QuackHatDuckVisual(
            Level level,
            Duck duck,
            QuackHatDefinition definition,
            QuackHatLevelChoices choices,
            Action<
                QuackHatComponentDefinition,
                QuackHatAnimationDefinition,
                QuackHatTransformSnapshot> spawnOneShot)
        {
            _level = level;
            _duck = duck;
            _choices = choices;
            _spawnOneShot = spawnOneShot;
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

                CreateEffectControllers(definition.Components);
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

            SpawnWorldOneShots(events);
            UpdateEmitters(state);

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
            _worldOneShots.Clear();
            _emitters.Clear();
        }

        private void CreateEffectControllers(
            IReadOnlyList<QuackHatComponentDefinition> definitions)
        {
            int duckIndex = _duck.profile?.networkIndex ?? 0;
            foreach (QuackHatComponentDefinition component in definitions)
            {
                if (!_choices.IsComponentSelected(component.Id)
                    || !HasLiveParent(component))
                {
                    continue;
                }

                if (component.Controller == QuackHatController.WorldOneShot)
                {
                    _worldOneShots.Add(component);
                }
                else if (component.Emitter != null)
                {
                    _emitters.Add(new QuackHatEmitterComponent(
                        component,
                        new QuackHatEmitterRuntime(
                            component.Emitter,
                            _choices.CreateEmitterRandom(component.Id, duckIndex))));
                }
            }
        }

        private bool HasLiveParent(QuackHatComponentDefinition component)
        {
            return component.ParentKind == QuackHatParentKind.Duck
                || _components.ContainsKey(component.ParentComponentId);
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

        private void SpawnWorldOneShots(QuackHatAnimationEvents events)
        {
            QuackHatTrigger? trigger = events.Death
                ? QuackHatTrigger.Death
                : events.DirectionChanged
                    ? QuackHatTrigger.DirectionChanged
                    : null;
            if (trigger == null)
            {
                return;
            }

            foreach (QuackHatComponentDefinition component in _worldOneShots)
            {
                if (_choices.TryGetAnimation(
                        component.Id,
                        trigger.Value,
                        out QuackHatAnimationDefinition animation)
                    && TryCreateSnapshot(component, out QuackHatTransformSnapshot transform))
                {
                    _spawnOneShot(component, animation, transform);
                }
            }
        }

        private void UpdateEmitters(QuackHatDuckAnimationState duckState)
        {
            foreach (QuackHatEmitterComponent emitter in _emitters)
            {
                if (!TryCreateSnapshot(
                    emitter.Definition,
                    out QuackHatTransformSnapshot transform))
                {
                    emitter.Runtime.Update(conditionActive: false, Vec2.Zero);
                    continue;
                }

                QuackHatDuckAnimationState state = duckState;
                if (emitter.Definition.ParentKind == QuackHatParentKind.Component)
                {
                    QuackHatVisualComponent parent =
                        _components[emitter.Definition.ParentComponentId];
                    state.IsFollower = parent.IsFollower;
                    state.FollowerMoving = parent.FollowerMoving;
                }

                bool conditionActive = IsConditionActive(
                    emitter.Definition.Emitter.Condition,
                    state);
                if (!emitter.Runtime.Update(conditionActive, transform.Position))
                {
                    continue;
                }

                QuackHatAnimationDefinition animation =
                    _choices.GetEmitterAnimation(emitter.Definition);
                if (animation != null)
                {
                    _spawnOneShot(emitter.Definition, animation, transform);
                }
            }
        }

        private bool TryCreateSnapshot(
            QuackHatComponentDefinition component,
            out QuackHatTransformSnapshot transform)
        {
            Thing parent = _duck;
            QuackHatVisualComponent parentVisual = null;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                if (!_components.TryGetValue(
                    component.ParentComponentId,
                    out parentVisual))
                {
                    transform = default;
                    return false;
                }

                parent = parentVisual.Thing;
            }

            if (!parent.visible || _duck.removeFromLevel)
            {
                transform = default;
                return false;
            }

            sbyte offDir = component.Facing switch
            {
                QuackHatFacing.Fixed => 1,
                QuackHatFacing.Movement when parentVisual?.MovementDirection != 0 =>
                    parentVisual.MovementDirection,
                _ => parent.offDir
            };
            transform = new QuackHatTransformSnapshot(
                GetTarget(component, parent),
                parent.angle,
                parent.scale,
                parent.alpha,
                offDir,
                ResolveDepth(component.RenderLayer, parent.depth));
            return true;
        }

        private static bool IsConditionActive(
            QuackHatTrigger condition,
            QuackHatDuckAnimationState state)
        {
            return condition switch
            {
                QuackHatTrigger.Default => true,
                QuackHatTrigger.Idle => !state.Netted
                    && !state.Ragdoll
                    && !state.Sliding
                    && !state.Airborne
                    && !state.Running
                    && !state.Crouching,
                QuackHatTrigger.Running => state.Running,
                QuackHatTrigger.Airborne => state.Airborne,
                QuackHatTrigger.Crouching => state.Crouching,
                QuackHatTrigger.Sliding => state.Sliding,
                QuackHatTrigger.Ragdoll => state.Ragdoll,
                QuackHatTrigger.Netted => state.Netted,
                QuackHatTrigger.FollowerMoving =>
                    state.IsFollower && state.FollowerMoving,
                QuackHatTrigger.FollowerIdle =>
                    state.IsFollower && !state.FollowerMoving,
                _ => false
            };
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

        private sealed class QuackHatEmitterComponent
        {
            public QuackHatEmitterComponent(
                QuackHatComponentDefinition definition,
                QuackHatEmitterRuntime runtime)
            {
                Definition = definition;
                Runtime = runtime;
            }

            public QuackHatComponentDefinition Definition { get; }

            public QuackHatEmitterRuntime Runtime { get; }
        }
    }
}
