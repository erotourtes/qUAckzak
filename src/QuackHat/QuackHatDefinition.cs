using System.Collections.Generic;
using DuckGame;
using Microsoft.Xna.Framework.Graphics;

namespace qUAckzak.Mod.QuackHat
{
    internal enum QuackHatParentKind
    {
        Duck,
        Component
    }

    internal enum QuackHatRenderLayer
    {
        Inherit,
        Behind,
        Front,
        Foreground
    }

    internal enum QuackHatFacing
    {
        Inherit,
        Movement,
        Fixed
    }

    internal enum QuackHatController
    {
        Attached,
        FlyingFollower,
        GroundFollower,
        WorldOneShot
    }

    internal enum QuackHatTrigger
    {
        Default,
        Idle,
        Running,
        Airborne,
        Crouching,
        Sliding,
        Ragdoll,
        Netted,
        DirectionChanged,
        Death,
        FollowerMoving,
        FollowerIdle
    }

    internal enum QuackHatEmitterCadence
    {
        Time,
        Distance
    }

    internal sealed class QuackHatDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DirectoryPath { get; set; }
        public string PreviewPath { get; set; }
        public string HatPath { get; set; }
        public Team RootTeam { get; set; }
        public IReadOnlyList<QuackHatComponentDefinition> Components { get; set; }
    }

    internal sealed class QuackHatComponentDefinition
    {
        public string Id { get; set; }
        public string SpritePath { get; set; }
        public Texture2D SpriteTexture { get; set; }
        public int SheetWidth { get; set; }
        public int SheetHeight { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int FrameCount { get; set; }
        public IReadOnlyList<QuackHatNetworkTileDefinition> NetworkTiles { get; set; }
        public QuackHatParentKind ParentKind { get; set; }
        public string ParentComponentId { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public QuackHatRenderLayer RenderLayer { get; set; }
        public QuackHatFacing Facing { get; set; }
        public QuackHatController Controller { get; set; }
        public float Speed { get; set; }
        public string Group { get; set; }
        public IReadOnlyList<QuackHatAnimationDefinition> Animations { get; set; }
        public QuackHatEmitterDefinition Emitter { get; set; }
    }

    internal sealed class QuackHatNetworkTileDefinition
    {
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public IReadOnlyList<Team> FrameTeams { get; set; }
    }

    internal sealed class QuackHatAnimationDefinition
    {
        public QuackHatTrigger Trigger { get; set; }
        public int FirstFrame { get; set; }
        public int LastFrame { get; set; }
        public int TicksPerFrame { get; set; }
    }

    internal sealed class QuackHatEmitterDefinition
    {
        public QuackHatTrigger Condition { get; set; }
        public QuackHatEmitterCadence Cadence { get; set; }
        public int MinimumInterval { get; set; }
        public int MaximumInterval { get; set; }
    }

    internal sealed class QuackHatLoadFailure
    {
        public string PackagePath { get; set; }
        public string Message { get; set; }
    }
}
