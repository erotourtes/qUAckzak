using System.Collections.Generic;
using System.Xml.Serialization;

namespace qUAckzak.Mod.QuackHat
{
    public enum QuackHatManifestLayer
    {
        [XmlEnum("inherit")]
        Inherit,

        [XmlEnum("behind")]
        Behind,

        [XmlEnum("front")]
        Front,

        [XmlEnum("foreground")]
        Foreground
    }

    public enum QuackHatManifestFacing
    {
        [XmlEnum("inherit")]
        Inherit,

        [XmlEnum("movement")]
        Movement,

        [XmlEnum("fixed")]
        Fixed
    }

    public enum QuackHatManifestController
    {
        [XmlEnum("attached")]
        Attached,

        [XmlEnum("flying_follower")]
        FlyingFollower,

        [XmlEnum("ground_follower")]
        GroundFollower,

        [XmlEnum("world_one_shot")]
        WorldOneShot
    }

    public enum QuackHatManifestTrigger
    {
        [XmlEnum("default")]
        Default,

        [XmlEnum("idle")]
        Idle,

        [XmlEnum("running")]
        Running,

        [XmlEnum("airborne")]
        Airborne,

        [XmlEnum("crouching")]
        Crouching,

        [XmlEnum("sliding")]
        Sliding,

        [XmlEnum("ragdoll")]
        Ragdoll,

        [XmlEnum("netted")]
        Netted,

        [XmlEnum("direction_changed")]
        DirectionChanged,

        [XmlEnum("death")]
        Death,

        [XmlEnum("follower_moving")]
        FollowerMoving,

        [XmlEnum("follower_idle")]
        FollowerIdle
    }

    public enum QuackHatManifestEmitterCadence
    {
        [XmlEnum("time")]
        Time,

        [XmlEnum("distance")]
        Distance
    }

    [XmlRoot("hat")]
    public sealed class QuackHatManifest
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("preview")]
        public string Preview { get; set; } = "preview.png";

        [XmlAttribute("image")]
        public string Image { get; set; } = "hat.png";

        [XmlElement("component")]
        public List<QuackHatComponentManifest> Components { get; set; } = new();
    }

    public sealed class QuackHatComponentManifest
    {
        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlAttribute("sprite")]
        public string Sprite { get; set; }

        [XmlAttribute("frameWidth")]
        public int FrameWidth { get; set; }

        [XmlIgnore]
        public bool FrameWidthSpecified { get; set; }

        [XmlAttribute("frameHeight")]
        public int FrameHeight { get; set; }

        [XmlIgnore]
        public bool FrameHeightSpecified { get; set; }

        [XmlAttribute("parent")]
        public string Parent { get; set; } = "duck";

        [XmlAttribute("offsetX")]
        public float OffsetX { get; set; }

        [XmlAttribute("offsetY")]
        public float OffsetY { get; set; }

        [XmlAttribute("layer")]
        public QuackHatManifestLayer Layer { get; set; } = QuackHatManifestLayer.Front;

        [XmlAttribute("facing")]
        public QuackHatManifestFacing Facing { get; set; } = QuackHatManifestFacing.Inherit;

        [XmlAttribute("controller")]
        public QuackHatManifestController Controller { get; set; } =
            QuackHatManifestController.Attached;

        [XmlAttribute("speed")]
        public float Speed { get; set; }

        [XmlIgnore]
        public bool SpeedSpecified { get; set; }

        [XmlAttribute("group")]
        public string Group { get; set; }

        [XmlElement("animation")]
        public List<QuackHatAnimationManifest> Animations { get; set; } = new();

        [XmlElement("emitter")]
        public QuackHatEmitterManifest Emitter { get; set; }
    }

    public sealed class QuackHatAnimationManifest
    {
        [XmlAttribute("trigger")]
        public QuackHatManifestTrigger Trigger { get; set; }

        [XmlAttribute("firstFrame")]
        public int FirstFrame { get; set; }

        [XmlAttribute("lastFrame")]
        public int LastFrame { get; set; }

        [XmlAttribute("ticksPerFrame")]
        public int TicksPerFrame { get; set; }
    }

    public sealed class QuackHatEmitterManifest
    {
        [XmlAttribute("condition")]
        public QuackHatManifestTrigger Condition { get; set; }

        [XmlAttribute("cadence")]
        public QuackHatManifestEmitterCadence Cadence { get; set; }

        [XmlAttribute("minimumInterval")]
        public int MinimumInterval { get; set; }

        [XmlAttribute("maximumInterval")]
        public int MaximumInterval { get; set; }
    }
}
