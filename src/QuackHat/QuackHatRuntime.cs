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
        private readonly QuackHatNetworkTransport _networkTransport = new();
        private readonly Dictionary<Duck, QuackHatDuckVisual> _visuals = new();
        private readonly Dictionary<QuackHatDefinition, QuackHatLevelChoices> _levelChoices =
            new();
        private readonly List<QuackHatOneShotVisual> _oneShots = new();
        private Level _currentLevel;
        private bool _networked;

        public QuackHatRuntime(QuackHatService service)
        {
            _service = service;
            _service.Reloading += Reset;
        }

        public string Name => "qUAckhat";

        public void Update()
        {
            Level level = Level.current;
            if (level == null)
            {
                ResetVisuals();
                _currentLevel = null;
                return;
            }

            bool networkChanged = _networked != Network.isActive;
            if (!ReferenceEquals(_currentLevel, level) || networkChanged)
            {
                ResetVisuals();
                _currentLevel = level;
                _networked = Network.isActive;
                if (networkChanged)
                {
                    _networkTransport.Reset();
                }
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
                if (_networked && duck.profile?.localPlayer != true)
                {
                    RemoveVisual(duck);
                    continue;
                }

                TeamHat teamHat = duck.GetEquipment(typeof(TeamHat)) as TeamHat;
                QuackHatDefinition definition = _service.FindByTeam(teamHat?.team);

                if (duck.removeFromLevel)
                {
                    RemoveVisual(duck);
                    continue;
                }

                if (definition == null)
                {
                    if (_visuals.TryGetValue(duck, out QuackHatDuckVisual dyingVisual)
                        && duck.dead)
                    {
                        dyingVisual.Update();
                        if (!dyingVisual.IsPlayingDeathAnimation)
                        {
                            RemoveVisual(duck);
                        }
                    }
                    else
                    {
                        RemoveVisual(duck);
                    }

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
                        _networkTransport.EnsureSent(definition, duck.profile);
                        visual = new QuackHatDuckVisual(
                            level,
                            duck,
                            definition,
                            GetLevelChoices(definition, level.seed),
                            (component, animation, transform) =>
                                SpawnOneShot(duck, component, animation, transform),
                            _networked);
                        _visuals.Add(duck, visual);
                    }
                    catch (Exception exception)
                    {
                        DevConsole.Log(
                            $"|RED|qUAckhat could not create visuals for '{definition.Name}': {exception.Message}");
                        continue;
                    }
                }

                _networkTransport.EnsureSent(definition, duck.profile);
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
            ResetVisuals();
            _networkTransport.Reset();
            _currentLevel = null;
        }

        private void ResetVisuals()
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
            Duck owner,
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
                    transform,
                    _networked,
                    owner));
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
                QuackHatTransformSnapshot> spawnOneShot,
            bool networked)
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

                    Vec2 position = new(duck.x, duck.y);
                    IQuackHatRenderedVisual rendered = networked
                        ? new QuackHatNetworkRenderedVisual(
                            level,
                            duck,
                            component,
                            position)
                        : new QuackHatOfflineRenderedVisual(
                            level,
                            component,
                            position);
                    _components.Add(
                        component.Id,
                        new QuackHatVisualComponent(
                            rendered,
                            position,
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

        public bool IsPlayingDeathAnimation => _components.Values.Any(
            component => component.Animation.IsPlayingEvent(QuackHatTrigger.Death));

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
                component.Rendered.Remove();
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
            QuackHatVisualComponent parentVisual = null;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                UpdateComponent(component.ParentComponentId, state, events, updated);
                parentVisual = _components[component.ParentComponentId];
            }

            QuackHatVisualComponent visual = _components[componentId];
            Vec2 target = GetTarget(component, parentVisual);
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
            visual.Angle = parentVisual?.Angle ?? _duck.angle;
            visual.Scale = parentVisual?.Scale ?? _duck.scale;
            visual.Alpha = parentVisual?.Alpha ?? _duck.alpha;
            visual.OffDir = ResolveFacing(
                component.Facing,
                parentVisual?.OffDir ?? _duck.offDir,
                visual);
            visual.Depth = ResolveDepth(
                component.RenderLayer,
                parentVisual?.Depth ?? _duck.depth);
            visual.Visible = visual.Animation.Visible
                && (parentVisual?.Visible ?? _duck.visible)
                && !_duck.removeFromLevel;
            visual.Rendered.Apply(
                visual.Animation.Frame,
                visual.CreateSnapshot(),
                visual.Visible);
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
            }

            bool parentVisible = parentVisual?.Visible ?? _duck.visible;
            if (!parentVisible || _duck.removeFromLevel)
            {
                transform = default;
                return false;
            }

            sbyte offDir = component.Facing switch
            {
                QuackHatFacing.Fixed => 1,
                QuackHatFacing.Movement when parentVisual?.MovementDirection != 0 =>
                    parentVisual.MovementDirection,
                _ => parentVisual?.OffDir ?? _duck.offDir
            };
            transform = new QuackHatTransformSnapshot(
                GetTarget(component, parentVisual),
                parentVisual?.Angle ?? _duck.angle,
                parentVisual?.Scale ?? _duck.scale,
                parentVisual?.Alpha ?? _duck.alpha,
                offDir,
                ResolveDepth(
                    component.RenderLayer,
                    parentVisual?.Depth ?? _duck.depth));
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

        private Vec2 GetTarget(
            QuackHatComponentDefinition component,
            QuackHatVisualComponent parent)
        {
            Vec2 offset = new(component.OffsetX, component.OffsetY);
            return component.ParentKind == QuackHatParentKind.Duck
                ? _duck.anchorPosition + _duck.OffsetLocal(offset)
                : parent.Position + QuackHatTransform.OffsetLocal(
                    offset,
                    parent.Angle,
                    parent.Scale,
                    parent.OffDir);
        }

        private void UpdatePosition(
            QuackHatComponentDefinition component,
            QuackHatVisualComponent visual,
            Vec2 target)
        {
            Vec2 previous = visual.Position;
            if (!visual.PositionInitialized)
            {
                visual.Position = component.Controller == QuackHatController.GroundFollower
                    && TryFindGround(target.x, target.y, out Vec2 ground)
                    ? PlaceOnGround(component, ground)
                    : target;
                visual.PositionInitialized = true;
                visual.MovedThisTick = false;
                return;
            }

            switch (component.Controller)
            {
                case QuackHatController.Attached:
                    visual.Position = target;
                    break;

                case QuackHatController.FlyingFollower:
                    visual.Position = MoveTowards(
                        previous,
                        target,
                        component.Speed);
                    break;

                case QuackHatController.GroundFollower:
                    float nextX = MoveTowards(previous.x, target.x, component.Speed);
                    if (TryFindGround(nextX, target.y, out Vec2 ground))
                    {
                        visual.Position = PlaceOnGround(component, ground);
                    }
                    break;
            }

            Vec2 movement = visual.Position - previous;
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

        private static Vec2 PlaceOnGround(
            QuackHatComponentDefinition component,
            Vec2 ground)
        {
            ground.y -= component.FrameHeight / 2f;
            return ground;
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
            sbyte parentOffDir,
            QuackHatVisualComponent visual)
        {
            return facing switch
            {
                QuackHatFacing.Fixed => 1,
                QuackHatFacing.Movement when visual.MovementDirection != 0 =>
                    visual.MovementDirection,
                _ => parentOffDir
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
                IQuackHatRenderedVisual rendered,
                Vec2 position,
                IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition> animations)
            {
                Rendered = rendered;
                Position = position;
                Animation = new QuackHatAnimationPlayer(animations);
            }

            public IQuackHatRenderedVisual Rendered { get; }

            public QuackHatAnimationPlayer Animation { get; }

            public Vec2 Position { get; set; }

            public float Angle { get; set; }

            public Vec2 Scale { get; set; }

            public float Alpha { get; set; }

            public sbyte OffDir { get; set; }

            public Depth Depth { get; set; }

            public bool Visible { get; set; }

            public bool PositionInitialized { get; set; }

            public bool MovedThisTick { get; set; }

            public sbyte MovementDirection { get; set; }

            public bool IsFollower { get; set; }

            public bool FollowerMoving { get; set; }

            public QuackHatTransformSnapshot CreateSnapshot()
            {
                return new QuackHatTransformSnapshot(
                    Position,
                    Angle,
                    Scale,
                    Alpha,
                    OffDir,
                    Depth);
            }
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
