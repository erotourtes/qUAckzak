using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace qUAckzak.Mod.QuackHat
{
    internal static class QuackHatManifestLoader
    {
        private const string ManifestFileName = "hat.xml";
        private const string SchemaResourceName = "qUAckzak.QuackHat.Schema.xsd";

        private static readonly Regex IdentifierPattern = new(
            "^[a-z0-9][a-z0-9_-]*$",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ReservedComponentIds = new(
            new[] { "duck" },
            StringComparer.Ordinal);

        private static readonly XmlSerializer Serializer = new(typeof(QuackHatManifest));
        private static readonly Lazy<XmlSchemaSet> ManifestSchema = new(CreateSchema);

        public static QuackHatDefinition Load(string packageDirectory)
        {
            string manifestPath = Path.Combine(packageDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw Error($"Missing {ManifestFileName}.");
            }

            QuackHatManifest manifest = Deserialize(manifestPath);
            string name = RequireText(manifest.Name, "hat.name");
            string preview = RequireText(manifest.Preview, "hat.preview");
            string image = RequireText(manifest.Image, "hat.image");

            List<QuackHatComponentDefinition> components = ReadComponents(
                manifest.Components ?? new List<QuackHatComponentManifest>(),
                packageDirectory);

            ValidateComponentRelationships(components);

            return new QuackHatDefinition
            {
                Id = Path.GetFileName(packageDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
                Name = name,
                DirectoryPath = Path.GetFullPath(packageDirectory),
                PreviewPath = ResolvePackageFile(packageDirectory, preview, "hat.preview"),
                HatPath = ResolvePackageFile(packageDirectory, image, "hat.image"),
                Components = components
            };
        }

        private static QuackHatManifest Deserialize(string manifestPath)
        {
            try
            {
                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    ValidationType = ValidationType.Schema
                };
                settings.Schemas.Add(ManifestSchema.Value);

                using XmlReader reader = XmlReader.Create(manifestPath, settings);
                return (QuackHatManifest)Serializer.Deserialize(reader);
            }
            catch (Exception exception) when (
                exception is XmlException
                or XmlSchemaException
                or InvalidOperationException)
            {
                Exception cause = exception;
                while (cause.InnerException != null)
                {
                    cause = cause.InnerException;
                }

                throw Error(
                    $"Could not parse or validate {ManifestFileName}: {cause.Message}",
                    exception);
            }
        }

        private static XmlSchemaSet CreateSchema()
        {
            Assembly assembly = typeof(QuackHatManifestLoader).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(SchemaResourceName)
                ?? throw Error($"Embedded qUAckhat schema '{SchemaResourceName}' is missing.");

            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using XmlReader reader = XmlReader.Create(stream, settings);

            XmlSchemaSet schemas = new()
            {
                XmlResolver = null
            };
            schemas.Add(null, reader);
            schemas.Compile();
            return schemas;
        }

        private static List<QuackHatComponentDefinition> ReadComponents(
            IReadOnlyList<QuackHatComponentManifest> manifests,
            string packageDirectory)
        {
            List<QuackHatComponentDefinition> components = new(manifests.Count);
            HashSet<string> componentIds = new(StringComparer.Ordinal);

            for (int index = 0; index < manifests.Count; index++)
            {
                QuackHatComponentManifest manifest = manifests[index];
                string context = $"component[{index}]";
                string id = RequireIdentifier(manifest.Id, $"{context}.id");

                if (ReservedComponentIds.Contains(id))
                {
                    throw Error(
                        $"{context}.id cannot be '{id}' because that name is the built-in duck anchor.");
                }

                if (!componentIds.Add(id))
                {
                    throw Error($"Duplicate component id '{id}'.");
                }

                bool hasFrameWidth = manifest.FrameWidthSpecified;
                bool hasFrameHeight = manifest.FrameHeightSpecified;
                if (hasFrameWidth != hasFrameHeight)
                {
                    throw Error(
                        $"{context}.frameWidth and {context}.frameHeight must be declared together.");
                }

                string parent = RequireText(manifest.Parent, $"{context}.parent");
                (QuackHatParentKind parentKind, string parentComponentId) = ParseParent(
                    parent,
                    context);

                bool isFollower = manifest.Controller is
                    QuackHatManifestController.FlyingFollower
                    or QuackHatManifestController.GroundFollower;
                if (isFollower && (!manifest.SpeedSpecified || manifest.Speed <= 0f))
                {
                    throw Error($"{context}.speed must be greater than zero for a follower controller.");
                }

                if (!isFollower && manifest.SpeedSpecified)
                {
                    throw Error($"{context}.speed is only valid for follower controllers.");
                }

                string group = manifest.Group == null
                    ? null
                    : RequireIdentifier(manifest.Group, $"{context}.group");
                List<QuackHatAnimationDefinition> animations = ReadAnimations(
                    manifest.Animations ?? new List<QuackHatAnimationManifest>(),
                    context);
                QuackHatEmitterDefinition emitter = ReadEmitter(manifest.Emitter, context);
                bool hasFrameSize = hasFrameWidth && hasFrameHeight;

                if (animations.Count > 0 && !hasFrameSize)
                {
                    throw Error(
                        $"{context}.frameWidth and {context}.frameHeight are required when animations are declared.");
                }

                if (animations.Count == 0 && hasFrameSize)
                {
                    throw Error(
                        $"{context}.frameWidth and {context}.frameHeight are redundant on a static component.");
                }

                if (emitter != null && animations.Count == 0)
                {
                    throw Error($"{context}.emitter requires an animation for emitted instances.");
                }

                QuackHatController controller = ConvertController(manifest.Controller);
                if (controller == QuackHatController.WorldOneShot
                    && emitter == null
                    && !animations.Any(animation => IsEvent(animation.Trigger)))
                {
                    throw Error(
                        $"{context} uses world_one_shot but has no event animation or emitter.");
                }

                if (controller == QuackHatController.WorldOneShot && emitter != null)
                {
                    throw Error(
                        $"{context}.controller is redundant because emitter instances are already world one-shots.");
                }

                components.Add(new QuackHatComponentDefinition
                {
                    Id = id,
                    SpritePath = ResolvePackageFile(
                        packageDirectory,
                        RequireText(manifest.Sprite, $"{context}.sprite"),
                        $"{context}.sprite"),
                    FrameWidth = manifest.FrameWidth,
                    FrameHeight = manifest.FrameHeight,
                    ParentKind = parentKind,
                    ParentComponentId = parentComponentId,
                    OffsetX = manifest.OffsetX,
                    OffsetY = manifest.OffsetY,
                    RenderLayer = ConvertLayer(manifest.Layer),
                    Facing = ConvertFacing(manifest.Facing),
                    Controller = controller,
                    Speed = manifest.Speed,
                    Group = group,
                    Animations = animations,
                    Emitter = emitter
                });
            }

            return components;
        }

        private static List<QuackHatAnimationDefinition> ReadAnimations(
            IReadOnlyList<QuackHatAnimationManifest> manifests,
            string componentContext)
        {
            List<QuackHatAnimationDefinition> animations = new(manifests.Count);
            for (int index = 0; index < manifests.Count; index++)
            {
                QuackHatAnimationManifest manifest = manifests[index];
                string context = $"{componentContext}.animation[{index}]";
                if (manifest.LastFrame < manifest.FirstFrame)
                {
                    throw Error(
                        $"{context}.lastFrame must be equal to or greater than firstFrame.");
                }

                animations.Add(new QuackHatAnimationDefinition
                {
                    Trigger = ConvertTrigger(manifest.Trigger),
                    FirstFrame = manifest.FirstFrame,
                    LastFrame = manifest.LastFrame,
                    TicksPerFrame = manifest.TicksPerFrame
                });
            }

            return animations;
        }

        private static QuackHatEmitterDefinition ReadEmitter(
            QuackHatEmitterManifest manifest,
            string componentContext)
        {
            if (manifest == null)
            {
                return null;
            }

            string context = $"{componentContext}.emitter";
            if (manifest.MaximumInterval < manifest.MinimumInterval)
            {
                throw Error(
                    $"{context}.maximumInterval must be equal to or greater than minimumInterval.");
            }

            return new QuackHatEmitterDefinition
            {
                Condition = ConvertTrigger(manifest.Condition),
                Cadence = manifest.Cadence switch
                {
                    QuackHatManifestEmitterCadence.Time => QuackHatEmitterCadence.Time,
                    QuackHatManifestEmitterCadence.Distance => QuackHatEmitterCadence.Distance,
                    _ => throw Error($"{context}.cadence is unsupported.")
                },
                MinimumInterval = manifest.MinimumInterval,
                MaximumInterval = manifest.MaximumInterval
            };
        }

        private static void ValidateComponentRelationships(
            IReadOnlyList<QuackHatComponentDefinition> components)
        {
            Dictionary<string, QuackHatComponentDefinition> byId = components.ToDictionary(
                component => component.Id,
                StringComparer.Ordinal);

            foreach (QuackHatComponentDefinition component in components)
            {
                if (component.ParentKind == QuackHatParentKind.Component
                    && !byId.ContainsKey(component.ParentComponentId))
                {
                    throw Error(
                        $"Component '{component.Id}' refers to missing parent '{component.ParentComponentId}'.");
                }
            }

            Dictionary<string, int> visitState = new(StringComparer.Ordinal);
            foreach (QuackHatComponentDefinition component in components)
            {
                VisitParent(component, byId, visitState);
            }

            foreach (IGrouping<string, QuackHatComponentDefinition> group in components
                .Where(component => component.Group != null)
                .GroupBy(component => component.Group, StringComparer.Ordinal))
            {
                if (group.Count() < 2)
                {
                    throw Error(
                        $"Component group '{group.Key}' has only one member and would not select anything.");
                }
            }
        }

        private static void VisitParent(
            QuackHatComponentDefinition component,
            IReadOnlyDictionary<string, QuackHatComponentDefinition> byId,
            IDictionary<string, int> visitState)
        {
            visitState.TryGetValue(component.Id, out int state);
            if (state == 2)
            {
                return;
            }

            if (state == 1)
            {
                throw Error($"Component parent cycle contains '{component.Id}'.");
            }

            visitState[component.Id] = 1;
            if (component.ParentKind == QuackHatParentKind.Component)
            {
                VisitParent(byId[component.ParentComponentId], byId, visitState);
            }

            visitState[component.Id] = 2;
        }

        private static (QuackHatParentKind Kind, string ParentComponentId) ParseParent(
            string value,
            string context)
        {
            return value switch
            {
                "duck" => (QuackHatParentKind.Duck, null),
                _ when IdentifierPattern.IsMatch(value) => (QuackHatParentKind.Component, value),
                _ => throw Error(
                    $"{context}.parent must be duck or a lowercase component id.")
            };
        }

        private static QuackHatRenderLayer ConvertLayer(QuackHatManifestLayer value)
        {
            return value switch
            {
                QuackHatManifestLayer.Inherit => QuackHatRenderLayer.Inherit,
                QuackHatManifestLayer.Behind => QuackHatRenderLayer.Behind,
                QuackHatManifestLayer.Front => QuackHatRenderLayer.Front,
                QuackHatManifestLayer.Foreground => QuackHatRenderLayer.Foreground,
                _ => throw Error($"Unsupported render layer '{value}'.")
            };
        }

        private static QuackHatFacing ConvertFacing(QuackHatManifestFacing value)
        {
            return value switch
            {
                QuackHatManifestFacing.Inherit => QuackHatFacing.Inherit,
                QuackHatManifestFacing.Movement => QuackHatFacing.Movement,
                QuackHatManifestFacing.Fixed => QuackHatFacing.Fixed,
                _ => throw Error($"Unsupported facing mode '{value}'.")
            };
        }

        private static QuackHatController ConvertController(QuackHatManifestController value)
        {
            return value switch
            {
                QuackHatManifestController.Attached => QuackHatController.Attached,
                QuackHatManifestController.FlyingFollower => QuackHatController.FlyingFollower,
                QuackHatManifestController.GroundFollower => QuackHatController.GroundFollower,
                QuackHatManifestController.WorldOneShot => QuackHatController.WorldOneShot,
                _ => throw Error($"Unsupported component controller '{value}'.")
            };
        }

        private static QuackHatTrigger ConvertTrigger(QuackHatManifestTrigger value)
        {
            return value switch
            {
                QuackHatManifestTrigger.Default => QuackHatTrigger.Default,
                QuackHatManifestTrigger.Idle => QuackHatTrigger.Idle,
                QuackHatManifestTrigger.Running => QuackHatTrigger.Running,
                QuackHatManifestTrigger.Airborne => QuackHatTrigger.Airborne,
                QuackHatManifestTrigger.Crouching => QuackHatTrigger.Crouching,
                QuackHatManifestTrigger.Sliding => QuackHatTrigger.Sliding,
                QuackHatManifestTrigger.Ragdoll => QuackHatTrigger.Ragdoll,
                QuackHatManifestTrigger.Netted => QuackHatTrigger.Netted,
                QuackHatManifestTrigger.DirectionChanged => QuackHatTrigger.DirectionChanged,
                QuackHatManifestTrigger.Death => QuackHatTrigger.Death,
                QuackHatManifestTrigger.FollowerMoving => QuackHatTrigger.FollowerMoving,
                QuackHatManifestTrigger.FollowerIdle => QuackHatTrigger.FollowerIdle,
                _ => throw Error($"Unsupported animation trigger '{value}'.")
            };
        }

        private static bool IsEvent(QuackHatTrigger trigger)
        {
            return trigger is QuackHatTrigger.DirectionChanged or QuackHatTrigger.Death;
        }

        private static string ResolvePackageFile(
            string packageDirectory,
            string relativePath,
            string context)
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw Error($"{context} must be relative to the hat package.");
            }

            string packageRoot = Path.GetFullPath(packageDirectory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(packageRoot, pathComparison))
            {
                throw Error($"{context} must not leave the hat package directory.");
            }

            if (!File.Exists(fullPath))
            {
                throw Error($"{context} refers to missing file '{relativePath}'.");
            }

            return fullPath;
        }

        private static string RequireIdentifier(string value, string context)
        {
            value = RequireText(value, context);
            if (!IdentifierPattern.IsMatch(value))
            {
                throw Error(
                    $"{context} must start with a lowercase letter or number and contain only lowercase letters, numbers, '-' or '_'.");
            }

            return value;
        }

        private static string RequireText(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Error($"{context} must not be empty.");
            }

            return value;
        }

        private static InvalidDataException Error(string message, Exception innerException = null)
        {
            return new InvalidDataException(message, innerException);
        }
    }
}
