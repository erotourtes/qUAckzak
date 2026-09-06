# How qUAckhat works

This document is a code tour of the qUAckhat runtime. It explains how a hat
package becomes Duck Game objects, how those objects are updated every frame,
and how the same behavior is rendered in offline and online games.

For the XML format and instructions for creating a hat, see
[`hat.md`](../../hat.md). This document is about the C# implementation behind
that format.

## The central idea

Duck Game already understands custom hats, but a native hat is essentially one
small image attached to a duck. qUAckhat keeps that ordinary native hat as the
package root and creates additional objects for independently animated pieces
such as hair, a moustache, a follower, or a particle effect.

The runtime does not replace Duck Game's hat-selection logic. Instead, every
frame it observes the `TeamHat` currently equipped by each duck. When that hat
belongs to a loaded qUAckhat package, the runtime updates the package's extra
components around that duck.

The names in Duck Game can be surprising:

- `Team` describes a hat and is the asset registered in the hat selector.
- `TeamHat` is the in-level `Thing` worn by a duck or synchronized online.
- `Duck` is the player character whose state drives qUAckhat animations.

The full data flow is:

```text
hat.xml + PNG files
        |
        v
XML DTOs --XSD and semantic validation--> runtime definitions
        |                                  |
        |                                  +--> component textures
        |                                  +--> selectable root Team
        |                                  +--> hidden network Teams
        v
per-level deterministic choices
        |
        v
per-duck component graph --> animation/controller/emitter state
        |
        +--> offline: SpriteThing objects
        |
        `--> online: native TeamHat tile objects --> Duck Game networking
```

This separation is important. Package data is loaded once, random choices are
made once per level, and mutable animation and movement state is kept once per
wearing duck.

## Where qUAckhat starts

[`../Mod.cs`](../Mod.cs) constructs a `QuackHatService` during
`OnPostInitialize`, points it at `content/quackhat`, and calls `Reload()`. It
then constructs a `QuackHatRuntime` and gives it to the shared `ModeHost`.

[`../Modes/ModeHost.cs`](../Modes/ModeHost.cs) is a Duck Game `GameComponent`.
Duck Game calls its `Update()` method every simulation tick. The host forwards
that call to qUAckhat and the other qUAckzak modes, and resets their level-local
state when the active level changes.

qUAckhat therefore has no Harmony patch for drawing hats. The ordinary update
loop is enough: inspect ducks, create or update visuals, and remove stale ones.
This also avoids depending on a particular private drawing method in Duck Game.

## File map

| File | Responsibility | Lifetime of its state |
|---|---|---|
| [`QuackHatManifest.cs`](QuackHatManifest.cs) | XML-serializer classes and XML-facing enums | During manifest parsing |
| [`QuackHatManifestLoader.cs`](QuackHatManifestLoader.cs) | XSD validation, semantic validation, path checks, and conversion to runtime definitions | During load or reload |
| [`QuackHatDefinition.cs`](QuackHatDefinition.cs) | Validated runtime model shared by the other systems | Until the next reload |
| [`QuackHatSpriteSheetLayout.cs`](QuackHatSpriteSheetLayout.cs) | Calculates and validates animation-frame grids | During loading |
| [`QuackHatAssetLoader.cs`](QuackHatAssetLoader.cs) | Decodes component PNGs into FNA textures | Until unload or reload |
| [`QuackHatRootHatRegistry.cs`](QuackHatRootHatRegistry.cs) | Registers each package's ordinary selectable Duck Game hat | Until unload or reload |
| [`QuackHatNetworkAssetRegistry.cs`](QuackHatNetworkAssetRegistry.cs) | Converts component frames into hidden native custom-hat tiles | Until unload or reload |
| [`QuackHatService.cs`](QuackHatService.cs) | Owns the loaded catalog and coordinates transactional reloads | Whole mod session |
| [`QuackHatLevelChoices.cs`](QuackHatLevelChoices.cs) | Selects groups and animation variants deterministically | One level per hat |
| [`QuackHatAnimationPlayer.cs`](QuackHatAnimationPlayer.cs) | Chooses triggers and advances one component's frames | One wearing duck/component |
| [`QuackHatEmitterRuntime.cs`](QuackHatEmitterRuntime.cs) | Tracks time- or distance-based emission progress | One wearing duck/emitter |
| [`QuackHatRenderedVisual.cs`](QuackHatRenderedVisual.cs) | Offline and online implementations of a rendered component | One wearing duck/component or one-shot |
| [`QuackHatOneShotVisual.cs`](QuackHatOneShotVisual.cs) | Plays a detached animation at a captured world transform | From spawn until its last frame |
| [`QuackHatNetworkTransport.cs`](QuackHatNetworkTransport.cs) | Sends hidden native hats to each remote connection | One online session |
| [`QuackHatRuntime.cs`](QuackHatRuntime.cs) | Connects ducks, definitions, controllers, animations, effects, rendering, and cleanup | Whole mod session, with level-local children |

The following sections walk through these files in the order data reaches
them.

## 1. Reading and validating a package

### `QuackHatManifest.cs`: the XML-shaped model

`XmlSerializer` needs simple public classes whose shape matches the XML. Those
classes live in `QuackHatManifest.cs`. For example, XML attributes become C#
properties and nested `<animation>` or `<emitter>` elements become lists or
child objects.

These are data-transfer objects, not the objects used by the live runtime.
They intentionally preserve details of XML deserialization, including
`...Specified` flags for optional value-type attributes. Without such a flag,
an omitted integer and an explicitly written `0` would look identical after
deserialization.

The manifest enums also use XML names such as `flying_follower`. Keeping these
enums separate from the runtime enums prevents serialization concerns from
leaking throughout the behavior code.

### `QuackHatManifestLoader.cs`: making the data trustworthy

The loader turns a package directory into a `QuackHatDefinition` in four
stages:

1. Find `hat.xml` and deserialize it with `XmlSerializer`.
2. Validate it against the XSD embedded in the mod assembly.
3. Check rules that XSD 1.0 cannot express conveniently.
4. Resolve safe package-local paths and create runtime definitions.

The XML reader prohibits DTDs and has no resolver. Sprite, preview, and root-hat
paths are resolved under the package directory and rejected if they escape it.
This means a package cannot use `../` to read an unrelated file.

Semantic validation includes rules such as:

- component IDs must be lowercase and unique;
- `duck` is reserved as the root-parent name;
- referenced parents must exist and parent graphs cannot contain cycles;
- frame width and height must be provided together;
- animation ranges must be internally valid (the asset-loading stage later
  checks them against the decoded sprite sheet);
- follower controllers require a valid speed;
- emitters require animation data;
- component groups must contain enough alternatives to make selection useful.

After this point, the rest of the runtime can work with normalized
`QuackHatDefinition` objects rather than repeatedly defending itself against
malformed XML.

### `QuackHatDefinition.cs`: the runtime-shaped model

This file contains the vocabulary used by the engine: component definitions,
animation definitions, emitter definitions, controllers, triggers, facing,
layers, and network tiles.

A component definition eventually contains both authored data and loaded data:
its local offset and behavior come from XML, its `Texture2D` comes from the
asset loader, and its network tile definitions come from the network registry.

The parent is normalized to either the duck or another component. That creates
a component graph. A child does not need to know how its ancestor follows the
duck; it inherits the resulting transform of its direct parent.

## 2. Loading images and registering the root hat

### `QuackHatSpriteSheetLayout.cs`: interpreting a PNG as frames

A component image may be static or divided into equal animation frames. The
layout calculator verifies positive dimensions, divisibility by the declared
frame dimensions, the resulting frame count, and every referenced frame index.

It returns a normalized layout so renderers can address a frame with a single
integer. The art can place frames in a two-dimensional grid; the `SpriteMap`
handles translating that linear frame number into the correct source rectangle.

### `QuackHatAssetLoader.cs`: creating FNA textures

The asset loader opens every component PNG and asks Duck Game's `ContentPack`
to create a `Texture2D`. It enables the game's pink-transparency processing,
then runs the sprite-sheet layout validation and records the normalized frame
size and count on the component definition.

Texture ownership is explicit. `Unload()` disposes the created textures, and a
partially failed load disposes everything it created before returning the
error. This matters during repeated offline reloads.

### `QuackHatRootHatRegistry.cs`: entering the native hat selector

Every package still has an ordinary `hat.png`. The registry loads it through
Duck Game's own `Team.DeserializeFromPNG()` and registers the resulting `Team`
with `Teams.AddExtraTeam()`.

That root `Team` provides three things:

- a normal entry in Duck Game's hat selector;
- the ordinary root hat visible on the duck;
- an identity the runtime can use to recognize the selected qUAckhat package.

When a package is replaced during an offline reload, the registry transfers
profiles and in-level `TeamHat` instances from the previous root `Team` to the
replacement. It also invalidates Duck Game's remembered hat-selector data so
the selector rebuilds from the new registrations.

The runtime normally compares root teams by object reference. It also compares
their `hatID`, because Duck Game can recreate or deserialize an equivalent
`Team` object during networking.

## 3. How online play makes this possible

It is reasonable to expect an online game to have one server that receives
everyone's inputs, calculates the whole world, and sends the result back. That
is a common architecture, but it is not the only one, and it is not a complete
description of Duck Game's networking.

### The classic authoritative-server model

In a strictly server-authoritative game, the data flow is approximately:

```text
player input -----> authoritative server -----> world snapshots
                        |
                        `---- physics, hits, deaths, and rules happen here
```

The client may predict its own movement immediately so controls do not feel one
network round trip late, but the server's result wins if they disagree. Remote
objects are usually drawn between received snapshots using interpolation. This
model is attractive for competitive games because clients are not trusted to
declare that they hit something or moved somewhere valid.

A variation uses one player's game as a *listen server*: it is both that
player's client and the authoritative server for the match. Another variation
is deterministic lockstep, where every machine receives the same inputs and
runs the same simulation. Real games often mix these techniques rather than
following one model everywhere.

### Duck Game distributes authority by object

Duck Game has a host and also supports server roles for coordinating the
session, lobby, level transitions, and other global decisions. Gameplay
objects, however, have their own owning `NetworkConnection`. The machine that
owns a particular object is allowed to simulate and publish that object's
authoritative state.

This is visible in Duck Game Rebuilt's
[`Thing.cs`](../../docs/DuckGameRebuilt/DuckGame/src/DuckGame/Thing.cs). A
`Thing` stores `connection` and `authority`, and exposes `isServerForObject`.
Despite the name, `isServerForObject` does **not** mean “this process is the
match's central server.” During online play it is also true when the object's
connection is the local connection. A useful way to read it is:

> Is this machine currently responsible for calculating this object?

For example, your machine is normally responsible for your duck and things it
creates or controls. Another player's machine is responsible for that player's
duck. Ownership of interactive objects can be transferred; Duck Game calls
this process “fondling” in methods such as `Fondle()` and `TransferControl()`.
The authority counter helps competing ownership transfers resolve consistently.

Each machine still has a local `Thing` representing remote objects. Duck
Game's [`GhostObject.cs`](../../docs/DuckGameRebuilt/DuckGame/src/DuckGame/Network/GhostObject.cs)
connects an authoritative object to those remote copies, commonly called
*ghosts*. It does not serialize every C# field. It serializes fields registered
through `StateBinding` objects, tracks which values changed, and sends compact
delta state. Received states are buffered and applied to the remote object;
positions and similar properties can be interpolated so network updates look
smoother than the packet rate.

For objects implementing `ITakeInput`, ghost data can also carry recent input
states and feed them into a remote virtual input device. So Duck Game is not
purely “send inputs” or purely “send positions.” It has a hybrid system:

- continuously changing object properties use ghost state;
- recent controls can accompany suitable controlled objects;
- discrete occurrences use explicit network messages;
- ownership decides which machine is authoritative for each object.

For instance,
[`PhysicsObject.cs`](../../docs/DuckGameRebuilt/DuckGame/src/DuckGame/PhysicsObject.cs)
binds position, velocity, angle, facing, owner, and physics flags. A
[`TeamHat`](../../docs/DuckGameRebuilt/DuckGame/src/DuckGame/Equipment/TeamHat.cs)
inherits those physics bindings and adds a binding for its native team index.
That existing state is precisely what qUAckhat reuses.

### State, assets, and behavior are different things

Showing a custom image remotely requires three separate questions to be
answered:

1. **Asset:** does the receiving machine have the pixels?
2. **State:** which object, image frame, position, angle, and facing should it
   draw now?
3. **Behavior:** which machine decides how those values change next?

qUAckhat sends the asset as a native custom hat, represents the state as native
`TeamHat` objects, and runs the behavior only on the duck owner's machine.
This is why the receiver does not need our component engine.

The owner-side flow is:

```text
local Duck state
      |
      v
qUAckhat calculates component transforms and animation frames
      |
      v
local native TeamHat tiles are updated
      |
      v
Duck Game GhostObject serializes their bound native state
      |
      v
remote Duck Game updates and draws ghost TeamHat tiles
```

Before those tile objects refer to a custom team index, qUAckhat sends each
frame's image using Duck Game's standard
[`NMSpecialHat`](../../docs/DuckGameRebuilt/DuckGame/src/DuckGame/Network/NMSpecialHat.cs)
message. The receiver's unmodified Duck Game already knows how to decode that
message and register the image as a `Team`. Later, when ghost state says a tile
uses that team's index, the native `TeamHat` knows which sprite to draw.

The remote player receives neither `hat.xml` nor instructions such as “use the
airborne animation.” They receive the native images and a changing collection
of native object states. To their game, these are simply custom hats moving
around the level.

### Why qUAckhat simulates only the local duck online

Every qUAckzak client can see all `Duck` objects, including remote ghosts. If
each client ran component behavior for every duck, the owner's networked tiles
would arrive and the receiver would also create its own local tiles for the
same duck. The hat would be duplicated. Timing, collision queries, or random
effects could also diverge slightly between machines.

`QuackHatRuntime` therefore checks `duck.profile.localPlayer` online. Only the
owner calculates a duck's components. Everyone else consumes the native ghosts
produced by that calculation. Offline every duck is local, so the same check is
unnecessary.

This is called **owner-authoritative cosmetic simulation**. It is appropriate
here because our objects are deliberately nonphysical visuals: they do not
decide hits, movement, scores, or deaths. A gameplay-changing feature would
need much more care. Trusting a client to authoritatively create damaging or
collidable objects could become a cheat, and an unmodded receiver would not
know custom rules that were implemented only in our DLL.

### What the host still does—and what it does not do for us

The existence of per-object authority does not make the host irrelevant. Duck
Game still gives the host special responsibilities for establishing the
session and coordinating global match state. It can also own objects for which
the host is responsible. But the host does not run qUAckhat's XML parser,
animation player, or component controllers on behalf of another player.

Our mod works with an unmodded host because it stays inside vocabulary every
Duck Game client already understands:

- native custom-hat asset messages;
- native `Team` indices;
- native ghosted `TeamHat` objects;
- native position, angle, facing, visibility, and removal state.

That is also the boundary of compatibility. We can only expect unmodded peers
to reproduce properties the native protocol knows how to send. The next
section explains how component art is converted into that vocabulary.

## 4. Preparing components for online play

### Why a component becomes many hidden hats

Duck Game already knows how to transfer custom hats to peers and synchronize a
`TeamHat`. Reusing that protocol lets an unmodded peer display qUAckhat pieces;
we do not require it to understand `hat.xml` or run qUAckhat behavior.

Native custom hats are 32 by 32 pixels. A qUAckhat component frame can be
larger, so `QuackHatNetworkAssetRegistry.cs` pads it to a multiple of 32 and
cuts it into tiles. It repeats this for every frame:

```text
component frame 0         component frame 1
+--------+--------+       +--------+--------+
| tile A | tile B |       | tile A | tile B |
+--------+--------+       +--------+--------+

tile A => [Team for frame 0, Team for frame 1]
tile B => [Team for frame 0, Team for frame 1]
```

Each tile/frame pair becomes a native `Team`, registered as a hidden extra hat.
A `QuackHatNetworkTileDefinition` remembers the tile's offset and which `Team`
represents each animation frame.

The registry checks Duck Game's finite custom-hat index range before adding
anything. These indices are why qUAckhat reload is disabled online: changing
the registration order during a session would make peers disagree about which
index represents which image.

### `QuackHatNetworkTransport.cs`: sending those hats

The transport visits each remote connection and sends every hidden frame team
with Duck Game's native `NMSpecialHat` message. It remembers which package has
already been sent to each connection so the images are not retransmitted every
tick. It also honors the receiver's native custom-hat mute setting.

Only the client that owns a duck simulates that duck's qUAckhat components.
The resulting native `TeamHat` objects are then ghosted by Duck Game to other
players. A remote client running qUAckzak deliberately does not simulate the
same remote duck, because that would create a duplicate set of visuals.

This design gives useful compatibility but also inherits native protocol
limits. Position, rotation, facing, visibility, and the selected native team
are suitable for synchronization. Custom scale, alpha, and arbitrary depth
are not reliably represented by the native ghost state. Effects intended to
look identical to an unmodded peer should therefore bake size and fading into
their frames. Very fast animations may also skip intermediate frames under
real network conditions.

## 5. Loading as a transaction

`QuackHatService.cs` owns the complete catalog. For each package it coordinates
the manifest loader, texture loader, root registry, and network asset registry.

Loading is treated as a transaction:

```text
manifest -> textures -> root Team -> network Teams -> publish definition
                  failure at any point
                           |
                           v
                 undo registrations and dispose textures
```

If an edited package fails during reload, the service reports the failure and
keeps the previously working definition instead of replacing it with a
half-loaded one. Packages removed from disk are unregistered and unloaded.

`FindByTeam()` is the bridge from normal Duck Game equipment to qUAckhat data.
Given the `Team` worn by a duck, it returns the matching loaded definition.

## 6. Making deterministic level choices

`QuackHatLevelChoices.cs` resolves choices that should remain stable throughout
a level:

- exactly one root component from each `group` is enabled;
- children follow the selection state of their parent;
- one animation is selected when a trigger has multiple variants;
- every emitter receives its own random-number stream.

The seed combines Duck Game's level seed with the package and component IDs
using a stable custom hash. It does not consume Duck Game's gameplay random
number generator. All ducks wearing the same hat therefore get the same
per-level art choice without hat cosmetics perturbing weapon or level
randomness.

There is an important split here:

- `QuackHatLevelChoices` decides *which authored alternative* this level uses.
- `QuackHatAnimationPlayer` decides *which trigger and frame* is active now.
- `QuackHatDuckVisual` decides *where that frame is placed* for one duck.

## 7. Following one duck every tick

Most orchestration lives in `QuackHatRuntime.cs`.

### Top-level `QuackHatRuntime`

On every update, the runtime:

1. Detects a level or offline/online-mode change and resets level-local state.
2. Advances and removes detached one-shot effects.
3. Gets or creates the deterministic choices for each loaded hat.
4. Enumerates ducks in the current level.
5. Finds the `TeamHat` equipped by each duck and resolves its package.
6. Creates or updates a `QuackHatDuckVisual` for each relevant local duck.
7. Removes visuals belonging to ducks that no longer exist or wear that hat.

In online play it also asks the network transport to make the required hidden
hats available to remote connections.

### Per-duck `QuackHatDuckVisual`

This object owns the live component graph for one duck. It creates a renderer
and animation player for every selected non-one-shot component, plus emitter
state for components that emit repeated effects.

Each tick it derives a small state snapshot from Duck Game's `Duck` object:
netted, ragdolled, sliding, airborne, running, crouching, facing direction, and
dead/alive. Comparing the snapshot to the previous tick produces edge events
such as `direction_changed` and `death`.

Components update recursively, parent before child. For each component the
runtime:

1. Finds the parent transform: the duck root or an already updated component.
2. Transforms the authored local offset by parent scale, facing, and rotation.
3. Applies the selected controller to find the actual position.
4. Selects and advances the animation.
5. Resolves facing, layer depth, visibility, scale, angle, and alpha.
6. Applies the complete transform and frame to its renderer.
7. Evaluates event effects and emitters.

Because children consume their parent's final transform, a glint parented to a
saber stays on the saber even when the entire duck flips or rotates.

### Controllers

The runtime currently implements four controller types:

- `attached` snaps to the transformed parent offset each tick;
- `flying_follower` moves toward that target by no more than `speed` pixels per
  tick;
- `ground_follower` approaches horizontally and probes Duck Game platforms to
  keep the bottom of the sprite on walkable ground;
- `world_one_shot` is not kept in the attached graph; it captures a transform
  when its event fires and then plays independently.

All duck-parented components share one general duck anchor. A moustache is near
the head and trousers are near the feet because they have different offsets,
not because the engine contains separate semantic head and feet anchors.

### Facing and layers

Facing can inherit the parent's direction, follow movement direction, or stay
fixed. Local X offsets are mirrored when the resolved transform faces left;
the sprite itself is flipped as well.

Layers are converted into a depth relative to the duck or parent. `inherit`
uses the parent depth, while `behind`, `front`, and `foreground` use offsets
based on Duck Game's own backpack and wing depth conventions. This keeps a
component on the intended side of the duck without requiring the XML author to
know raw depth numbers.

## 8. Selecting and advancing animations

`QuackHatAnimationPlayer.cs` owns mutable playback state for one component. A
static component with no animations simply displays frame zero. Animated
components choose an event or state trigger and advance from `firstFrame` to
`lastFrame`, holding each frame for `ticksPerFrame` updates.

Events and continuous states behave differently:

- `death` and `direction_changed` are edge-triggered events. They play once,
  then the component returns to its current state animation.
- duck and follower states remain selected while their condition remains true
  and loop their frame range.

Trigger selection has a deliberate priority. Events are considered first,
with death protected from interruption by a later direction change. Continuous
duck states are then checked in this order:

```text
netted -> ragdoll -> sliding -> airborne -> running -> crouching
       -> follower_moving/follower_idle -> idle -> default
```

The priority makes overlapping Duck Game states predictable. For example, a
netted duck can also satisfy lower-priority movement conditions, but the netted
animation wins.

If XML provides several animations for the same trigger, the animation player
uses the variant chosen by `QuackHatLevelChoices`; it does not reroll every time
the trigger activates.

## 9. Rendering offline and online

`QuackHatRenderedVisual.cs` hides two substantially different implementations
behind `IQuackHatRenderedVisual`:

```csharp
Apply(frame, transform, visible);
Remove();
```

The behavior and animation code therefore does not care whether it is creating
a local sprite or native networked hats.

### Offline renderer

`QuackHatOfflineRenderedVisual` creates a `SpriteMap` from the loaded texture,
wraps it in a nonphysical `SpriteThing`, and adds that thing to the current
level. `Apply()` writes its frame, position, angle, scale, alpha, facing, depth,
and visibility every tick. `Remove()` removes it from the level.

The object is excluded from physics and vessel logic because it is purely
visual. qUAckhat owns its update; Duck Game should neither collide with it nor
move it independently.

### Online renderer

`QuackHatOnlineRenderedVisual` creates one native `TeamHat` per 32-by-32 tile.
For a requested animation frame it selects that tile's corresponding hidden
`Team`, adds the transformed tile offset, and applies the shared world
transform. Duck Game then synchronizes these ordinary ghostable things.

Switching frames means switching the tile's `Team`. The renderer briefly makes
the object active when a sprite refresh is required so native `TeamHat` code
notices the new team. It otherwise keeps qUAckhat in control and avoids hidden
event visuals flashing unexpectedly.

`QuackHatTransform.OffsetLocal()` is shared transform math. It scales a local
offset, mirrors it for left-facing art, rotates it, and returns the world-space
offset used by both components and network tiles.

## 10. Emitters and detached effects

### `QuackHatEmitterRuntime.cs`

An emitter decides *when* to spawn a one-shot. It tracks whether its trigger is
active, its current progress, and its randomly selected interval.

- Time cadence adds one unit per active update.
- Distance cadence adds the distance travelled since the previous update.
- Becoming inactive resets progress, so inactive time or movement is not saved
  for an immediate later emission.

When progress reaches the interval, the runtime requests a spawn, resets the
progress, and chooses another inclusive random interval from the manifest's
minimum and maximum.

### `QuackHatOneShotVisual.cs`

A one-shot captures position, rotation, facing, scale, alpha, and depth at the
moment it is spawned. It owns a normal offline or online renderer, advances its
frames, and removes itself after the last frame. It does not continue following
the parent.

This is suitable for smoke left behind by a pipe, a sunflower spawned while
running, or a spirit that remains where a duck died.

Death needs one extra safeguard. Duck Game can hide or move a duck as part of
its pop/death handling before qUAckhat observes the event. The per-duck runtime
therefore remembers the last visible live root transform and uses it as the
death-effect origin. It also retains a dead duck's visual briefly when the root
hat is no longer discoverable, allowing attached death animations to finish.

## 11. Cleanup and lifecycle boundaries

qUAckhat creates real Duck Game `Thing` objects, so every creation path needs a
matching removal path.

- Changing levels removes all per-duck visuals and live one-shots.
- Taking off or changing a qUAckhat root removes that duck's components.
- A completed one-shot removes its own renderer.
- Leaving online play clears connection-send state.
- Reloading offline disposes old textures and unregisters obsolete native
  teams after the replacement is ready.
- Calling the runtime's `Reset()` tears down runtime-owned level objects.

Keeping these boundaries explicit prevents invisible objects, textures, and
custom-hat registrations from accumulating across rounds or reloads.

## 12. A concrete example: the moustache

Suppose the Cossack manifest defines a moustache attached to the duck with a
default animation and a running animation.

At startup, the loader validates the component and the asset loader decodes its
sheet. The network registry also builds hidden 32-by-32 teams for every frame.
When a duck equips the Cossack root hat, `FindByTeam()` identifies the package
and the runtime creates the moustache component visual.

On one running tick:

1. The runtime sees that the duck is grounded and moving fast enough to be
   `running`.
2. The animation player chooses the running trigger and advances its frame
   timer.
3. The component takes the duck transform and applies the moustache offset.
4. The renderer receives that position, facing, depth, visibility, and frame.
5. Offline, a `SpriteThing` changes `SpriteMap.frame`; online, each tile changes
   to the hidden `Team` representing that frame.

When the duck stops, the next snapshot selects `idle` or `default` and the
player begins that animation. No moustache-specific C# exists: all of this is
the generic component, trigger, controller, and renderer pipeline.

## 13. Diagnosing problems

The console command `quackzak_quackhat_status` reports package load failures,
the current offline/online runtime mode, active wearers, selected groups and
animation variants, and live one-shot count. Use
`quackzak_quackhat_reload` after editing XML or PNG files in an offline session.

A useful debugging order is:

1. **Package missing:** inspect `QuackHatManifestLoader` errors and XSD
   validation.
2. **Package loads but image fails:** inspect `QuackHatAssetLoader` and
   `QuackHatSpriteSheetLayout`.
3. **Hat cannot be selected:** inspect `QuackHatRootHatRegistry` and root
   `Team` registration.
4. **Component does not appear:** break in `QuackHatRuntime.Update()` and check
   `FindByTeam()`, group selection, and component visibility.
5. **Wrong animation:** inspect the derived duck state and
   `QuackHatAnimationPlayer`'s selected trigger.
6. **Wrong position:** inspect the parent transform, local offset, facing, and
   controller result in `QuackHatDuckVisual`.
7. **Works offline but not remotely:** inspect network-team registration,
   per-connection sends, owner filtering, and native `TeamHat` creation.

Duck Game Rebuilt sources under `docs/DuckGameRebuilt` are especially useful
when following types such as `Duck`, `Team`, `TeamHat`, `NMSpecialHat`,
`SpriteThing`, and `Teams`. qUAckhat relies on their native behavior instead of
duplicating it.

## 14. Extending the engine

Most features cross more than one layer. The following checklists show the
usual change path.

### Adding an XML field

1. Add the attribute or element to `schemas/quackhat.xsd`.
2. Add its XML-facing property to `QuackHatManifest.cs`.
3. Validate and convert it in `QuackHatManifestLoader.cs`.
4. Store the normalized value in `QuackHatDefinition.cs`.
5. Consume it in the relevant runtime or renderer.
6. Document it in `hat.md` and add it to the acceptance package if practical.

### Adding a trigger

1. Add the XML enum value and XSD value.
2. Add the runtime trigger and loader conversion.
3. Detect the corresponding state or edge in `QuackHatRuntime.cs`.
4. Place it at an intentional priority in `QuackHatAnimationPlayer.cs`.
5. Decide whether attached animations, one-shots, emitters, or all three may
   consume it.

### Adding a controller

1. Add the XML and runtime enum values and validation rules.
2. Add any required definition fields.
3. Implement its movement in the recursive component update.
4. Check parented children, facing, death, and level cleanup.
5. Test both renderer implementations; online peers only receive the resulting
   transforms, not the controller logic.

### Changing rendering or networking

Keep `IQuackHatRenderedVisual` as the boundary when possible. Animation and
controller code should produce a frame plus a transform, while the renderer
decides how that becomes Duck Game objects. Changes to frame dimensions or
native-hat encoding must be mirrored in `QuackHatNetworkAssetRegistry`; changes
to synchronized object behavior belong in `QuackHatRenderedVisual` or
`QuackHatNetworkTransport`.

The bundled Cossack hat is an acceptance test for the generic system, not a
special case. A new engine feature should be expressed as reusable manifest
vocabulary and runtime behavior rather than a check for the Cossack package or
one of its component IDs.
