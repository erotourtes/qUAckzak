# qUAckhat design plan

## Goal

`qUAckhat` is a data-driven custom-hat runtime inside qUAckzak. It loads every
hat authored in the qUAckhat format; the runtime must not contain behavior
specific to the first hat.

The Cossack-themed hat is the first acceptance test. It must prove that the
format can express independently animated decorations, state-driven
animations, component parenting, one randomly selected pet per level,
followers, repeated effects, attached event effects, and randomly selected
death animations. The eventual rendering should use native Duck Game hat
objects so other players can see it online.

The manifest is XML. Its structure and primitive values are validated by
[`schemas/quackhat.xsd`](schemas/quackhat.xsd), then deserialized into typed C#
objects with `XmlSerializer`. C# performs rules that XSD 1.0 cannot express
well, such as checking files, parent cycles, and relationships between fields.
This uses only .NET Framework libraries; qUAckzak does not embed a third-party
configuration parser.

There is no format-version field. If the format changes incompatibly, hats
bundled with the mod are updated at the same time.

## Package layout

The loader scans every direct child of `content/quackhat/` as an independent
hat package:

```text
content/quackhat/<hat-id>/
├── hat.xml
├── preview.png
├── hat.png
└── components/
    ├── moustache.png
    ├── hair.png
    └── ...
```

- `preview.png` is reserved for a later qUAckhat-aware selector preview.
- `hat.png` is the ordinary selectable Duck Game hat and package root.
- `hat.xml` describes every additional component.
- File names have no behavioral meaning; behavior comes from the manifest.
- Paths must be relative and cannot leave their package directory.
- Vanilla custom hats remain handled by Duck Game.
- Hats++ packages are not automatically compatible; that would be a separate
  compatibility feature.

## XML schema

The smallest valid manifest is:

```xml
<?xml version="1.0" encoding="utf-8"?>
<hat xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
     xsi:noNamespaceSchemaLocation="../../../schemas/quackhat.xsd"
     name="Hat displayed name" />
```

It uses `preview.png` and `hat.png` by default. Override them with the
`preview` and `image` attributes on `<hat>`.

The schema-location line gives the VS Code XML extension completion and
immediate diagnostics. At runtime qUAckzak validates against the same XSD
embedded in its DLL, so loading does not depend on that relative schema path.

### Component attributes

Every component is a direct `<component>` child of `<hat>`.

| Attribute | Required | Default | Meaning |
|---|---:|---|---|
| `id` | Yes | — | Lowercase package-local identifier |
| `sprite` | Yes | — | Component PNG relative to the package |
| `frameWidth`, `frameHeight` | For animated components | Entire image | Size of one animation frame; always declared together |
| `parent` | No | `duck` | `duck` or another component ID |
| `offsetX`, `offsetY` | No | `0` | Position relative to the parent, in pixels |
| `layer` | No | `front` | `inherit`, `behind`, `front`, or `foreground` |
| `facing` | No | `inherit` | `inherit`, `movement`, or `fixed` |
| `controller` | No | `attached` | Component movement behavior |
| `speed` | For followers | — | Maximum follower speed in pixels per game tick |
| `group` | No | — | Per-level mutually exclusive selection group |

Supported controllers are:

- `attached`: follow the parent transform exactly;
- `flying_follower`: smoothly approach the configured offset in the air;
- `ground_follower`: approach the offset while remaining on walkable ground;
- `world_one_shot`: snapshot the parent transform when an event starts and
  play independently.

A duck-parented component uses the duck as its common origin. Offsets place art
at the head, torso, feet, or anywhere else without separate semantic anchors.
A component-parented child inherits the parent's position, rotation, facing,
and active selection.

Exactly one root from each `group` is selected with equal probability at level
start. Children inherit their parent's selection. The initial format has no
weights, counts, or alternate selection scopes.

### Animations

Animations are nested directly inside their component:

```xml
<component id="hair" sprite="components/hair.png"
           frameWidth="32" frameHeight="32">
  <animation trigger="default" firstFrame="0" lastFrame="3"
             ticksPerFrame="5" />
  <animation trigger="airborne" firstFrame="4" lastFrame="7"
             ticksPerFrame="3" />
</component>
```

`firstFrame` and `lastFrame` are an inclusive contiguous range.
Frames are numbered left-to-right and then top-to-bottom, matching Duck Game's
`SpriteMap`. `ticksPerFrame` must be positive. Supported triggers are:

```text
default  idle  running  airborne  crouching  sliding  ragdoll  netted
direction_changed  death  follower_moving  follower_idle
```

State animations loop while active. `direction_changed` and `death` are
events and play once. An event-only attached component is hidden while idle.
If multiple animations use the same trigger, one is chosen randomly at level
start and reused for that level.

State priority is fixed for every hat:

```text
death > netted > ragdoll > sliding > airborne > running > crouching > idle > default
```

For follower component trees, `follower_moving` or `follower_idle` is checked
after the duck states and before `idle`/`default`.

### Emitters

An optional emitter is nested after its component's animations:

```xml
<emitter condition="running" cadence="distance"
         minimumInterval="8" maximumInterval="16" />
```

Its `condition` is a state, not an event. `cadence` is either `time` in ticks or
`distance` in pixels. Every emission snapshots the template's world transform,
plays its animation once, and disappears. An emitter does not also specify
`controller="world_one_shot"`, because that would describe the same behavior
twice.

## First hat component plan

The examples use representative numeric values only; final frame dimensions,
offsets, ranges, and speeds depend on the finished art. Unless stated
otherwise, decorations are simultaneous and only pet roots use `group="pets"`.

### Root hat

`hat.png` is the selectable native Duck Game hat, the online profile identity,
and the shared duck origin. It needs no component entry.

### Moustache

The moustache is attached to the duck, offset to the beak, and has its own
animation clock.

```xml
<component id="moustache" sprite="components/moustache.png"
           frameWidth="32" frameHeight="32" offsetX="2" offsetY="-3">
  <animation trigger="default" firstFrame="0" lastFrame="3"
             ticksPerFrame="5" />
</component>
```

No moustache-specific type is needed: all components animate independently.

### Hair

Hair uses a default ground animation and a separate airborne animation.

```xml
<component id="hair" sprite="components/hair.png"
           frameWidth="32" frameHeight="32" offsetY="-8">
  <animation trigger="default" firstFrame="0" lastFrame="3"
             ticksPerFrame="5" />
  <animation trigger="airborne" firstFrame="4" lastFrame="7"
             ticksPerFrame="3" />
</component>
```

If art must pass both behind and in front of the duck, use two ordinary
components with different layers.

## Pets

### Flying windmill and wings

The windmill is a flying selected root. Its separately animated wings are a
child, so they inherit both selection and movement.

```xml
<component id="windmill" sprite="components/pets/windmill.png"
           frameWidth="32" frameHeight="32" offsetX="-24" offsetY="-12"
           facing="movement" controller="flying_follower" speed="2"
           group="pets">
  <animation trigger="default" firstFrame="0" lastFrame="3"
             ticksPerFrame="4" />
</component>
<component id="windmill-wings" sprite="components/pets/windmill-wings.png"
           frameWidth="32" frameHeight="32" parent="windmill"
           layer="inherit">
  <animation trigger="default" firstFrame="0" lastFrame="5"
             ticksPerFrame="3" />
</component>
```

### Dancing sharovary

The sharovary follow along the ground and dance hopak near their target.

```xml
<component id="sharovary" sprite="components/pets/sharovary.png"
           frameWidth="32" frameHeight="32" offsetX="-24" offsetY="8"
           facing="movement" controller="ground_follower" speed="2"
           group="pets">
  <animation trigger="follower_moving" firstFrame="0" lastFrame="3"
             ticksPerFrame="4" />
  <animation trigger="follower_idle" firstFrame="4" lastFrame="9"
             ticksPerFrame="4" />
</component>
```

### Cannon

Initial behavior is a static ground follower. No firing trigger is added until
there is a concrete interaction requirement.

```xml
<component id="cannon" sprite="components/pets/cannon.png"
           offsetX="-24" offsetY="8" facing="movement"
           controller="ground_follower" speed="1.5" group="pets" />
```

### Chaika boat

Initial behavior is a smoothly floating follower. A water-only controller is
not needed by the current requirement.

```xml
<component id="chaika" sprite="components/pets/chaika.png"
           offsetX="-28" offsetY="5" facing="movement"
           controller="flying_follower" speed="1.5" group="pets" />
```

### Rolling barrel

The barrel follows on the ground. Its rotation is animation art, not continuous
physics.

```xml
<component id="barrel" sprite="components/pets/barrel.png"
           frameWidth="32" frameHeight="32" offsetX="-24" offsetY="8"
           facing="fixed" controller="ground_follower" speed="2"
           group="pets">
  <animation trigger="follower_moving" firstFrame="0" lastFrame="5"
             ticksPerFrame="3" />
  <animation trigger="follower_idle" firstFrame="0" lastFrame="0"
             ticksPerFrame="1" />
</component>
```

## Decorations

### Pipe

The pipe is duck-attached and is the parent of the smoke emitter.

```xml
<component id="pipe" sprite="components/decorations/pipe.png"
           offsetX="5" offsetY="-2" />
```

### Vyshyvanka

The vyshyvanka is offset over the torso. It may provide any subset of the
standard state animations needed by the art.

```xml
<component id="vyshyvanka" sprite="components/decorations/vyshyvanka.png"
           frameWidth="32" frameHeight="32" offsetY="5">
  <animation trigger="default" firstFrame="0" lastFrame="0"
             ticksPerFrame="1" />
  <animation trigger="running" firstFrame="1" lastFrame="4"
             ticksPerFrame="3" />
</component>
```

### Bandura

```xml
<component id="bandura" sprite="components/decorations/bandura.png"
           offsetX="-4" offsetY="3" layer="behind" />
```

### Khoruhva/banner

The banner is behind the duck with its own looping cloth animation.

```xml
<component id="banner" sprite="components/decorations/banner.png"
           frameWidth="32" frameHeight="32" offsetX="-7" offsetY="-4"
           layer="behind">
  <animation trigger="default" firstFrame="0" lastFrame="5"
             ticksPerFrame="4" />
</component>
```

### Saber

The saber is offset over the duck's torso/hip and is the parent of its glint.

```xml
<component id="saber" sprite="components/decorations/saber.png"
           offsetX="7" offsetY="5" />
```

## Effects

### Running sunflower

While the duck runs, sunflowers appear after a random travelled distance. Each
instance remains where it was created.

```xml
<component id="sunflower" sprite="components/effects/sunflower.png"
           frameWidth="24" frameHeight="24" offsetY="10"
           layer="behind" facing="fixed">
  <animation trigger="default" firstFrame="0" lastFrame="5"
             ticksPerFrame="4" />
  <emitter condition="running" cadence="distance"
           minimumInterval="8" maximumInterval="16" />
</component>
```

Appearance, fading, and scale are drawn into frames, so separate particle
physics fields are unnecessary.

### Pipe smoke

Smoke is positioned relative to the pipe. Each emitted puff keeps its world
position rather than continuing to follow the duck.

```xml
<component id="pipe-smoke" sprite="components/effects/pipe-smoke.png"
           frameWidth="16" frameHeight="16" parent="pipe" offsetY="-3">
  <animation trigger="default" firstFrame="0" lastFrame="5"
             ticksPerFrame="4" />
  <emitter condition="default" cadence="time"
           minimumInterval="30" maximumInterval="60" />
</component>
```

### Spirit on death

For now, losing means dying. The spirit snapshots the duck position and plays
once; rising and fading are drawn into the animation.

```xml
<component id="spirit" sprite="components/effects/spirit.png"
           frameWidth="32" frameHeight="32" offsetY="-8"
           layer="foreground" controller="world_one_shot">
  <animation trigger="death" firstFrame="0" lastFrame="7"
             ticksPerFrame="5" />
</component>
```

If losing later means the final round loser, add a `round_lost` trigger then.

### Saber glint

The glint plays on a direction change and stays attached to the saber for its
whole animation. It is not an emitted particle.

```xml
<component id="saber-glint" sprite="components/effects/saber-glint.png"
           frameWidth="16" frameHeight="16" parent="saber"
           offsetX="4" offsetY="-5" layer="foreground">
  <animation trigger="direction_changed" firstFrame="0" lastFrame="4"
             ticksPerFrame="3" />
</component>
```

### Random death animation

Multiple `death` animations on one component are variants. One is selected at
level start and reused for all deaths in that level.

```xml
<component id="death-animation" sprite="components/effects/death-animation.png"
           frameWidth="48" frameHeight="48" layer="foreground"
           controller="world_one_shot">
  <animation trigger="death" firstFrame="0" lastFrame="5"
             ticksPerFrame="4" />
  <animation trigger="death" firstFrame="6" lastFrame="12"
             ticksPerFrame="4" />
  <animation trigger="death" firstFrame="13" lastFrame="20"
             ticksPerFrame="4" />
</component>
```

## Requirement coverage

| Requirement | Generic configuration | Components |
|---|---|---|
| Independent moustache animation | Per-component animation clock | `moustache` |
| Hair changes while flying | State-triggered animation | `hair` |
| One random pet per level | Shared component group | Five pet roots |
| Compound pet | Component parent | `windmill-wings` → `windmill` |
| Flying pets | Flying-follower controller | `windmill`, `chaika` |
| Ground pets | Ground-follower controller | `sharovary`, `cannon`, `barrel` |
| Pet movement animation | Controller-state trigger | `sharovary`, `barrel` |
| Body decorations | Duck origin, offsets, and layers | All decorations |
| Random running effect | Distance emitter | `sunflower` |
| Pipe smoke | Time emitter parented to a component | `pipe-smoke` → `pipe` |
| Spirit on death | World one-shot event | `spirit` |
| Glint follows saber | Attached event and component parent | `saber-glint` → `saber` |
| Random death animation | Duplicate event trigger | `death-animation` |

Every non-default manifest concept has an acceptance case:

| Concept | Why it exists |
|---|---|
| `id` | Diagnostics and stable parent references |
| `sprite` | Selects component artwork |
| frame size | Divides an animated sprite sheet into frames |
| `parent` | Attaches to the duck or another component |
| offsets | Align artwork with its attachment |
| `layer` | Body occlusion and foreground effects |
| `facing` | Moving pets and non-mirrored rolling art |
| `controller` | Attached, follower, and detached one-shot motion |
| `speed` | Tunes follower motion |
| `group` | Selects exactly one pet per level |
| animations | Maps frame ranges and timing to states/events |
| emitter | Repeats one-shot effects while a state is active |

## Configuration intentionally omitted

- Format version: incompatible changes update bundled hats.
- Semantic component types: hair, pet, pipe, and saber are ordinary IDs.
- Animation names: trigger and frame range are sufficient.
- Loop/play-once flags: states loop; events and emissions play once.
- Random weights/count/scope: choices are equal, select one, and last a level.
- Separate head/torso/feet anchors: all art uses the duck origin plus offsets.
- Pivot and inheritance flags: duck origin and full child transform inheritance
  cover the proposed components.
- Particle velocity, gravity, friction, alpha, scale, or fade: animation frames
  cover the proposed visuals.
- Replication policy: every visible qUAckhat component is online-capable.
- Hat-specific triggers: the runtime has no concept named after this first hat.

## Validation

XSD rejects malformed XML, unknown elements or attributes, missing required
values, invalid primitive types, and unsupported enum values. The loader then
rejects a package when:

- a referenced file is missing or escapes the package directory;
- component IDs are invalid, reserved, or duplicated;
- a component parent is missing or the parent graph contains a cycle;
- frame width and height are not supplied together;
- a follower has no positive speed, or a non-follower declares speed;
- a component group has fewer than two members;
- an animation ends before it begins;
- an emitter interval maximum is below its minimum;
- an emitter has no animation to play;
- a static component redundantly declares a frame size; or
- a world one-shot has no event that could display it.

During discovery, every component PNG is decoded through Duck Game's native
content loader. The package is rejected if a PNG cannot be decoded, a frame is
larger than its sheet, the sheet is not evenly divisible into frames, or an
animation references a frame outside the sheet. Static components use their
entire image as their single frame.

## Online model

The owning player is authoritative for their qUAckhat. Other modded clients do
not create a second copy of a remote player's components.

For every visual frame, the runtime creates ordinary Duck Game `Team` images
and transfers them with native `NMSpecialHat`. Duck Game custom hats are fixed
at 32×32 pixels, so larger qUAckhat frames are centered on a padded canvas and
split into multiple hidden 32×32 teams. Visible pieces become networked
`TeamHat` objects. Animation changes select a native team through
`TeamHat.netTeamIndex`; position, rotation, facing, visibility, and lifecycle
use normal object synchronization.

Only the owning client creates these objects. The receiving client does not
need `hat.xml`, qUAckhat classes, or a custom network message. Native ghost
state does not carry `Depth`, `scale`, or `alpha`, so remote pieces use native
hat depth and animation art must contain any scaling or fading. Very short
event animations can lose frames between network updates, so glint and death
effects must be tested before adding an event protocol.

## Implementation order

1. **Done:** parse, discover, and validate every
   `content/quackhat/*/hat.xml` package.
2. **Done:** load sprite sheets and validate image dimensions and frame bounds.
3. **Done:** register `hat.png` roots in Duck Game's native hat selector and
   render ungrouped, static `attached` component trees offline. Static children
   inherit their component parent's transform; animation, group selection,
   followers, emitters, and online component rendering remain in later slices.
4. **Done:** run independent per-component animation clocks, resolve looping
   duck-state animations in the documented priority order, and play attached
   `death` and `direction_changed` events once. Event-only attached components
   remain hidden between events.
5. **Done:** choose one component from each selection group and one animation
   variant for each duplicate trigger at level start. Choices are deterministic
   from the level seed and hat ID, shared by every duck wearing that hat in the
   level, and inherited by child components.
6. **Done:** move flying followers toward their inherited target with the
   configured per-tick speed cap, and move ground followers horizontally while
   projecting them onto the nearest walkable platform below their target.
   Movement-facing and `follower_moving`/`follower_idle` animations propagate
   to attached child components.
7. **Done:** keep attached event animations on their live component transform,
   snapshot `world_one_shot` death/direction events into independent visuals,
   and emit independent one-shots on random inclusive time or travelled-distance
   intervals while their configured state is active. Finished instances remove
   themselves and do not alter Duck Game's gameplay RNG.
8. **Done:** convert each component frame into one or more hidden native 32×32
   `Team` images, reserve stable custom-team indices, and reject packages that
   exceed Duck Game's 2,000-team per-profile range. Reloading is disabled while
   online so those indices cannot shift.
9. **Done:** let only the owning client run qUAckhat behavior online, transfer
   its frame teams with reliable native `NMSpecialHat` messages, and represent
   persistent components and one-shots as ordinary ghosted `TeamHat` tiles.
10. **Done:** retain a dying duck's existing component graph after Duck Game
    unequips its root hat, long enough to observe the death edge, start detached
    death effects, and finish any attached death animation.
11. **Done:** keep hidden native event components inactive and defer their
    sprite-refresh pulse until the event becomes visible, preventing an idle
    glint or death frame from flashing on unmodded receivers.
12. Test animated hair between two qUAckzak clients.
13. Test with a receiving client without qUAckzak.
14. Test pet selection, glint, and death effects online before extending the
    network design.
