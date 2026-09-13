# How to spawn a fully-rendered, animated entity that only one client can see

## The problem

You want an entity that:
- Looks and animates like a real mob.
- Is visible only to one player's client.
- Never exists on the server.
- Never syncs to other clients.

The normal spawn methods do not do this.

## Why the normal methods fail

`IWorldAccessor.SpawnEntity(entity)` looks like the right call. It is not.

On the client, `SpawnEntity` is an empty method. It does nothing. Check `ClientMain.SpawnEntity` in the decompiled source to confirm this yourself.

`IWorldAccessor.LoadEntity(entity, chunkIndex)` also looks right. On the client, it throws:

```
throw new InvalidOperationException("Cannot use LoadEntity on the Client side");
```

There is no supported, documented way to spawn a private, client-only entity. You have to use the same internal path the network layer uses.

## The real path

A real entity becomes visible through two steps:
1. The entity object gets added to the client's tracked entity list.
2. An event fires that tells the renderer system to build a renderer for it.

You can do both steps yourself, by hand, for an entity the server never sent.

**Step 1: Build the entity object.**

```csharp
Entity entity = capi.World.ClassRegistry.CreateEntity(entityProperties);
entity.EntityId = yourOwnUniqueId; // use a negative number, real server IDs are always positive
entity.Pos.SetPos(spawnPos);
entity.Initialize(entityProperties, capi.World.Api, 0);
```

This builds a normal `Entity` object. It does not add it to the game world yet.

**Step 2: Add it to the client's real entity list.**

`IClientWorldAccessor.LoadedEntities` is a public dictionary. Most mod code never touches it directly, but it is public.

```csharp
((IClientWorldAccessor)capi.World).LoadedEntities[entity.EntityId] = entity;
```

**Step 3: Tell the renderer system the entity is ready.**

The client class that creates renderers is `ClientSystemEntities`. It listens for an event called `OnEntityLoaded`. You can fire that event yourself:

```csharp
ClientMain game = (ClientMain)capi.World;
game.eventManager.TriggerEntityLoaded(entity);
```

One warning here. `capi.Event` is not the object you want. `capi.Event`'s real type is `ClientEventAPI`, a wrapper. The wrapper does not expose `TriggerEntityLoaded`. The real event manager is a public field on `ClientMain` called `eventManager`. Reach it through a cast, not through `capi.Event`.

After this call, `ClientSystemEntities` builds a real `EntityRenderer` for your entity and adds it to its own internal renderer list. From this point on, the normal client rendering system draws your entity every frame. It gets correct animation, correct lighting, correct shading, and correct frustum culling, automatically. You write none of that code yourself.

## Why other players never see it

Two things make the entity private to one client:
1. You never add it to a chunk's own entity list on the server. The server has no record of it.
2. You never send any network packet about it. No other client's `LoadedEntities` dictionary ever hears about it.

The entity exists only inside the memory of one running client.

## How to despawn it

Reverse the same steps, in the same order a real despawn packet handler uses:

```csharp
EntityDespawnData despawnData = new EntityDespawnData { Reason = EnumDespawnReason.Removed };
game.eventManager.TriggerEntityDespawn(entity, despawnData);
game.RemoveEntityRenderer(entity);
entity.OnEntityDespawn(despawnData);
((IClientWorldAccessor)capi.World).LoadedEntities.Remove(entity.EntityId);
```

`RemoveEntityRenderer` is a public method on `ClientMain`. Call it directly.

## How to move the entity

Moving this entity is manual. There is no AI system to do it for you.

Do this once per frame, in your own tick method:

**Step 1: Pick a target position.** This can be a fixed point, a random point, or the player's own position.

**Step 2: Compute the direction and distance to that target.**

```csharp
Vec3d toTarget = targetPos - yourTrackedPosition;
double dist = toTarget.Length();
```

Track your own position in a plain `Vec3d` field. Do not read `entity.Pos` back as your "current position" if you are also feeding the interpolation behavior (explained below) - `entity.Pos` becomes a smoothed, lagging value once you do that, not the true position. Keep your own ground-truth copy.

**Step 3: Step toward the target, by a fixed distance per second.**

```csharp
double step = Math.Min(yourSpeed * dt, dist);
yourTrackedPosition.X += toTarget.X / dist * step;
yourTrackedPosition.Z += toTarget.Z / dist * step;
```

`Math.Min` stops the entity from overshooting the target on a slow frame.

**Step 4: Set the walk animation's direction vector.**

```csharp
if (entity is EntityAgent agent)
{
    agent.Controls.WalkVector.Set(toTarget.X / dist * yourSpeed, 0, toTarget.Z / dist * yourSpeed);
}
```

Some animation-adjacent code reads this field to judge movement direction and speed. Setting only the position is not enough.

## How to face the entity in the right direction

**Step 1: Compute the yaw angle toward the target.**

```csharp
float desiredYaw = (float)Math.Atan2(toTarget.X, toTarget.Z);
```

This is the same formula the game's own AI movement code uses (`StraightLineTraverser.cs`). Confirm this in the decompiled source before trusting it - do not guess at the axis order, `Atan2(Z, X)` is a different, wrong answer.

**Step 2: Do not write this yaw straight onto the entity.** See the interpolation-behavior trap below. Feed it through `OnReceivedServerPos` instead, at roughly 15 times per second, exactly as shown in that section.

**Step 3: Also update the yaw on ticks where the entity is not actually stepping.** If your target-reached logic returns early before computing a new yaw, the entity's facing goes stale for a frame at every waypoint change. Compute `desiredYaw` unconditionally, every tick, even on ticks where you do not call the movement code.

Put together, one frame of movement looks like this:

```csharp
Vec3d toTarget = targetPos - yourTrackedPosition;
double dist = toTarget.Length();
float desiredYaw = dist > 0.01 ? (float)Math.Atan2(toTarget.X, toTarget.Z) : lastYaw;

if (dist > 0.01)
{
    double step = Math.Min(yourSpeed * dt, dist);
    yourTrackedPosition.X += toTarget.X / dist * step;
    yourTrackedPosition.Z += toTarget.Z / dist * step;
    if (entity is EntityAgent agent)
    {
        agent.Controls.WalkVector.Set(toTarget.X / dist * yourSpeed, 0, toTarget.Z / dist * yourSpeed);
    }
}

// push yourTrackedPosition + desiredYaw through OnReceivedServerPos, throttled to ~15/sec
```

## The one trap: movement and facing

Do not write directly to `entity.Pos.Yaw` every frame if the entity has the `interpolateposition` behavior attached (most vanilla creatures do, check the entity's own JSON file for `"code": "interpolateposition"`).

This behavior is client-only. Its whole job is smoothing rotation and position between real server position updates. It runs every frame and overwrites `Pos.Yaw`, `Pos.Pitch`, `Pos.Roll`, and a separate field called `agent.BodyYaw` (this field, not `Pos.Yaw`, is what actually drives the rendered body-facing direction for many creatures).

If you write `Pos.Yaw` directly, this behavior overwrites your value on the very next frame. You will not see your change take effect. The entity will appear to face a frozen, wrong direction, or turn in strange ways.

The fix is not to remove this behavior. The fix is to feed it, the same way a real server position update would:

```csharp
entity.Pos.SetPos(yourTargetPosition);
entity.Pos.Yaw = yourTargetYaw;
if (entity is EntityAgent agent) { agent.BodyYawServer = yourTargetYaw; }

EnumHandling handling = EnumHandling.PassThrough;
var interp = entity.GetBehavior<EntityBehaviorInterpolatePosition>();
interp?.OnReceivedServerPos(isTeleport: false, ref handling);
```

Call this at roughly 15 times per second, not every render frame. `EntityBehaviorInterpolatePosition` assumes a roughly 15-updates-per-second cadence internally. Calling it 60 times per second floods its internal queue and causes periodic jarring snaps once the queue hits its own overflow limit (20 entries).

After you call this, the behavior smoothly interpolates the entity's rendered rotation toward your new target on its own, every frame, exactly like it would for a real networked creature turning to face a new direction.

## One more animation detail

Setting position and yaw is not enough to make the walk animation look right. You must also:
1. Set `agent.Controls.WalkVector` to a vector pointing in your movement direction, scaled by your movement speed. Some animation-adjacent code checks this field.
2. Explicitly call `entity.AnimManager.StartAnimation("walk")` when the entity starts moving, and `entity.AnimManager.StopAnimation("walk")` plus `StartAnimation("idle")` when it stops. The animation system does not switch automatically based on position changes alone.

## Caveat

This technique uses classes from the `Vintagestory.Client.NoObf` namespace: `ClientMain`, `ClientEventManager`. These classes are not part of the documented, supported modding API. They are public, so the C# compiler allows the code, but the game's developers could rename or restructure them in a future update without warning. Test this again after every game update.
