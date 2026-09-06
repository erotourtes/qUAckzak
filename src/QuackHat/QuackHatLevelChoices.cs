using System;
using System.Collections.Generic;
using System.Linq;

namespace qUAckzak.Mod.QuackHat
{
    internal sealed class QuackHatLevelChoices
    {
        private readonly IReadOnlyDictionary<string, QuackHatComponentDefinition> _components;
        private readonly HashSet<string> _selectedGroupMembers =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _componentSelection =
            new(StringComparer.Ordinal);
        private readonly Dictionary<
            string,
            IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition>>
            _animations = new(StringComparer.Ordinal);
        private readonly int _hatSeed;

        public QuackHatLevelChoices(QuackHatDefinition definition, int levelSeed)
        {
            _components = definition.Components.ToDictionary(
                component => component.Id,
                StringComparer.Ordinal);

            _hatSeed = CombineSeed(levelSeed, definition.Id);
            Random random = new(_hatSeed);
            SelectGroups(definition.Components, random);
            SelectAnimationVariants(definition.Components, random);
        }

        public bool IsComponentSelected(string componentId)
        {
            if (_componentSelection.TryGetValue(componentId, out bool selected))
            {
                return selected;
            }

            QuackHatComponentDefinition component = _components[componentId];
            selected = component.Group == null
                || _selectedGroupMembers.Contains(component.Id);

            if (selected && component.ParentKind == QuackHatParentKind.Component)
            {
                selected = IsComponentSelected(component.ParentComponentId);
            }

            _componentSelection[componentId] = selected;
            return selected;
        }

        public IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition>
            GetAnimations(string componentId)
        {
            return _animations[componentId];
        }

        public bool TryGetAnimation(
            string componentId,
            QuackHatTrigger trigger,
            out QuackHatAnimationDefinition animation)
        {
            return _animations[componentId].TryGetValue(trigger, out animation);
        }

        public QuackHatAnimationDefinition GetEmitterAnimation(
            QuackHatComponentDefinition component)
        {
            IReadOnlyDictionary<QuackHatTrigger, QuackHatAnimationDefinition> selected =
                _animations[component.Id];

            if (selected.TryGetValue(component.Emitter.Condition, out QuackHatAnimationDefinition animation)
                || selected.TryGetValue(QuackHatTrigger.Default, out animation)
                || selected.TryGetValue(QuackHatTrigger.Idle, out animation))
            {
                return animation;
            }

            foreach (QuackHatAnimationDefinition candidate in component.Animations)
            {
                if (selected.TryGetValue(candidate.Trigger, out animation))
                {
                    return animation;
                }
            }

            return null;
        }

        public Random CreateEmitterRandom(string componentId, int duckIndex)
        {
            return new Random(CombineSeed(_hatSeed ^ duckIndex, componentId));
        }

        private void SelectGroups(
            IReadOnlyList<QuackHatComponentDefinition> components,
            Random random)
        {
            foreach (IGrouping<string, QuackHatComponentDefinition> group in components
                .Where(component => component.Group != null)
                .GroupBy(component => component.Group, StringComparer.Ordinal))
            {
                QuackHatComponentDefinition[] members = group.ToArray();
                _selectedGroupMembers.Add(members[random.Next(members.Length)].Id);
            }
        }

        private void SelectAnimationVariants(
            IReadOnlyList<QuackHatComponentDefinition> components,
            Random random)
        {
            foreach (QuackHatComponentDefinition component in components)
            {
                Dictionary<QuackHatTrigger, QuackHatAnimationDefinition> selected = new();
                foreach (IGrouping<QuackHatTrigger, QuackHatAnimationDefinition> variants in
                    component.Animations.GroupBy(animation => animation.Trigger))
                {
                    QuackHatAnimationDefinition[] choices = variants.ToArray();
                    int index = choices.Length == 1 ? 0 : random.Next(choices.Length);
                    selected.Add(variants.Key, choices[index]);
                }

                _animations.Add(component.Id, selected);
            }
        }

        private static int CombineSeed(int levelSeed, string hatId)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in hatId)
                {
                    hash = hash * 31 + character;
                }

                return levelSeed * 397 ^ hash;
            }
        }
    }
}
