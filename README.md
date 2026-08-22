# P2P Spawn Fix

P2P Spawn Fix protects Green Hell co-op clients from incomplete or invalid
network states that can leave parts of a multiplayer session broken.

Version 2.0.1 keeps the stable fixes while narrowing the zero-byte guard so it
cannot suppress map or other progression-sensitive initialization. Testing
confirmed that the same P2P
replication failure can cause invisible remote players, repetitive errors when
using the backpack or inventory, and a broken Pottery Table on affected
clients.

## What it fixes

- **Invisible host or remote players:** rebuilds incomplete replicated-player
  state when a remote model fails to initialize on the client.
- **Endless backpack or inventory errors:** repairs null object-spawn payloads
  and skips impossible zero-byte initial replication reads only inside the
  same malformed object-spawn message.
- **Map and progression safety:** preserves the game's original handling for
  maps, quest items, notebooks, journals, recipes, and blueprints.
- **Broken Pottery Table:** rejects invalid replicated ghost indices before
  they can remove the current craft object or throw an
  `ArgumentOutOfRangeException`.
- Uses rate-limited diagnostics so recurring network faults do not flood
  `Player.log`.
- Loads automatically as a permanent mod.

## Client-side scope

The protection applies to the computer where the mod is installed. It does not
modify the host's save, repair the Pottery Table globally, or force other
players to use the protected state.

A host may still see a broken Pottery Table while a client using this mod keeps
the same table functional. Install the mod on each client affected by invisible
players, repetitive inventory-related errors, object-spawn exceptions, or
Pottery Table desynchronization.

## Installation and use

Install the `.ghmod` normally through Green Hell ModLoader. No configuration is
required. In the console, run:

```text
p2pfix status
```

This displays the number of repaired or blocked network states.

## Verified results

Testing in the same defective co-op session confirmed:

- 1,262 repeated network exceptions without the mod and zero after reconnecting
  with it;
- an invisible host restored locally;
- normal backpack and inventory use;
- multiple Pottery Table crafts completed normally;
- repeated invalid `SetupGhost(-1)` updates blocked while the last valid craft
  state remained usable;
- successful recovery after reconnecting to an already-defective session.

## Version 2.0.1

- Narrowed the zero-byte initial-state guard to the malformed object-spawn
  message currently being repaired.
- Added fail-safe detection for map and progression-sensitive objects.
- Added diagnostics for progression-sensitive states preserved by the mod.
- Retained all fixes delivered in stable version 2.0.0.

## Version 2.0.0

- Promoted the mod from Beta to Stable.
- Expanded the documented scope to the three confirmed player-facing fixes.
- Added client-side Pottery Table protection and diagnostics.
- Preserved the existing null-payload, zero-byte state, and replicated-player
  recovery protections.

## Compatibility

Tested with Green Hell Update 1.5.5. The mod does not attempt to repair unrelated
missing-prefab, missing-script, AI-group, LODGroup, or Event System warnings.

## License

Licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE).
