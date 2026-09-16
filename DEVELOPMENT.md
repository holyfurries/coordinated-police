# Development notes

Behavior and regression checks for Coordinated Police 0.6.2. The in-game checklist
is not a record of completed tests.

## Severity and dispatch

`ResponseState` owns an incident's phase, severity, spent allowance, incoming slot
reservations, and burst cooldown. `ResponseRules` defines the limits:

| Severity | Active limit | Total allowance | Burst size | Call spacing | Break |
| --- | --- | --- | --- | --- | --- |
| Patrol | 4 | 4 | 2 | 8 s | 30 s |
| Pursuit | 6 | 8 | 2 | 7 s | 30 s |
| Armed | 12 | 36 | 4 | 2 s | 8 s |
| Tactical | 16 | 64 | 6 | 1 s | 6 s |

Host-side `PlayerCrimeData.AddCrime` events raise severity by actual crime type.
Resistance, assault, brandishing, and trafficking select Pursuit; gunfire, deadly
assault, and vehicular assault select Armed. The synced native pursuit level also
provides a fallback: NonLethal selects Pursuit and Lethal selects Armed. The game
can escalate its own pursuit state over time; this mod has no independent timer
that raises severity.

`NPC.RpcLogic___ReceiveImpact_427288424` is the installed game's receive-impact RPC
logic. Before damage, the hook resolves the player's NetworkObject from ImpactSource;
afterward it requires a health decrease or death before recording the hit. Only
police victims and directly attributable player sources qualify. Bullet, sharp-metal,
and explosion hits count as lethal attacks. Three distinct damaging lethal hits,
or an attributed police death, select Tactical. The last 64 attack IDs per player
are deduplicated. Unattributed sources are ignored rather than blamed on the host.
Damage attribution still needs validation with a joining player and different weapons.

Severity cannot decrease during an incident. Raising it adds allowance headroom but
does not refund officers already requested. Escalation to Armed or Tactical shortens
the next delay to at most one second on the next observation and resets the burst counter.
Searching allows one station wave within eight seconds of lost sight for Armed/Tactical,
subject to cooldown, reservations, and allowance. It uses on-foot dispatch with
beginAsSighted=false and selects the station using the frozen last-known position.
Nearby recruitment remains blocked during search. Reacquisition permits a later search wave. Escape, arrest, death, disconnect, and main-scene
unload clear the incident. State is keyed by player code rather than list position.

Nearby recruitment waits five seconds and needs a visible target. It selects an
available on-foot officer within 60 metres of a witnessing pursuer. If none is available,
it requests a native crew from the closest station. Requests use the same prefix and
budget as the game's own `PoliceStation.Dispatch` calls; they are not counted twice.
An initial reported crime can receive a crew before an officer first sees the player.
Odd-sized station grants use on-foot dispatch to avoid rounding up to a vehicle crew.
Officers are not cloned. Availability comes from native pools and delayed recycling described below.

Active foot and vehicle pursuers count toward the ceiling. Independently witnessing
patrols are not blocked. Granted requests reserve slots for 30 seconds and consume
lifetime allowance. Reservations are conservative: arriving officers can briefly
count as both incoming and active. Expiration frees capacity, not the allowance. A
native dispatch failure after a grant still consumes that allowance. Empty pools and
rejected calls do not. No queue of rejected calls is maintained.

Connected player count scales each incident independently: +50% of solo active and total
allowances per additional player, clamped to four-player scaling; bursts gain one slot per
additional player, plus one extra Armed/Tactical slot at four players. Armed/Tactical breaks
shorten by one second per extra player, down to four seconds. Joining/leaving updates limits on observation without refunding spent
allowance. Multiple wanted players can therefore produce larger combined responses.
Native officer pools still limit simultaneous availability. Delayed recycling replenishes
defeated officers when players are distant; it does not enlarge the NPC registry.

## Patrol population and reserve recycling

`PolicePopulation` runs after load completion on the host every five seconds, tracking up
to 128 officers by identity rather than registry index. Targets are 5/10/15/20 foot patrols
and 8/12/16/20 ready reserves for one through four players. Station scans cover at most
16 stations and 128 entries each. The reserve target is global, not per station.

When nobody is wanted, at most one idle station officer is assigned per five-second tick to native
routes from the current law settings (up to 64). Supplemental patrols run regardless of
the route's scheduled time. They leave the station without warping to the route start.
Initial waypoint indices are staggered using existing route membership (first 128 waypoints).
Patrol groups advance when ready. Extra staffing preserves at least half the reserve
target (minimum four). Existing native patrols count toward the target; the mod returns
only its own excess patrol officers, away from players, when staffing needs fall. Original
AutoDeactivate values are restored when released or on scene unload.

Dead/knocked-out officers become eligible for recycling 45 seconds after first observation.
Every tracked player must be at least 60 metres from both body and nearest station; missing
or invalid player positions block the operation. Distance is a proximity rule, not a
line-of-sight test. Officers assigned to vehicles are excluded. At most two replacements
per five seconds are attempted until ready reserves reach the target. Recycling hides the
officer through native networking, moves them to the station, clears old duties/lethal
effects, revives/restores vanilla health, then calls native Deactivate to return them to the
pool. Delayed returns get at most three total Deactivate attempts. Already reassigned
officers are left alone. Population failures disable this module and log the exception;
response coordination continues. Native pool availability still bounds simultaneous numbers.

No custom network protocol or NPC cloning is used. Clients clear stale death/knockout flags
only after synced health reaches native maximum and both native dead/unconscious behaviours
are disabled. This relies on native health and behaviour replication and MUST be tested with
remote players, including late join and a second death after replacement. It does not prove
replication from a successful compile. Population caches reset on Main scene unload.

Manual checks for this release:

- Four players, no wanted level: allow staffing to settle and inspect Population logs for
  20 foot patrols, reserve count, and registered officer count. Verify multiple route waypoints.
- Defeat officers, remain near the bodies for over 45 seconds: no recycling should occur.
- Move every player over 60 metres away from bodies and station; confirm limited recycling,
  pool membership, healthy station departures, and matching animations/damage on every peer.
- Keep one remote player near the body or station: that player must prevent recycling.
- Join and leave: targets change; active combat officers must never be returned to the pool.
- Reload and late-join after replacements, then kill a replaced officer again. Check both
  logs for errors and stale ragdolls. Pool exhaustion or unsafe locations may prevent targets.

## Combat roles and weapons

Roles use the officer's index in the native registry, repeating chaser, interceptor,
support, interceptor. Movement eligibility and weapon selection use the same index. This is
a selection pattern rather than a guarantee that every arriving crew contains all roles.

During native NonLethal pursuit, every third registry slot uses a baton and the others
use tasers. During native Lethal pursuit, one in four slots uses a pump shotgun at Armed
severity, rising to two in four at Tactical; remaining slots use the native police gun.
Support slots keep the native gun. Arrest-only and investigating weapon choices stay native.

The host changes the path passed to `CombatBehaviour.SetWeapon`; the native weapon RPC
replicates the selected asset. It does not modify shared prefab fields. The shotgun path
is `Avatar/Equippables/PumpShotgun`; if Resources cannot resolve it, selection falls back
to the native gun and logs one warning per scene. No third-party weapon mod is required.

The pursuit's ideal ranged distance is five metres for ordinary pistol roles, eight
for pistol support, and four for shotguns, clamped within the equipped weapon's native
range. Baton/taser spacing stays native. Health, armor, speed, weapon damage, accuracy,
and fire rate are not increased. Replacement recycling restores native maximum health. Pickpocket inventories remain vanilla.

There is no custom HUD or severity synchronization protocol. Dispatch and combat are
host-controlled and use native networking. Crime hooks only count events observed on
the host, with synced pursuit level supplying the baseline for remote players.

## Search and interception

On loss of the last officer's sighting, the coordinator freezes its last observed
position. An initial search can instead start from the game's reported last-known
position. Hidden movement cannot update the snapshot. Movement and native search hooks
assign reachable points around it, expanding from four to twelve metres in six-second
steps. Each officer tries at most four candidates every six seconds, with at most eight
candidate checks per second globally. Unusable points fall back to native navigation.
Vehicle search and native pursuit timeouts are preserved.

Visible-target flanking leaves the nearest visible on-foot pursuer on native pursuit.
Interceptor roles split sides within each target's pursuit, excluding that nearest officer.
Beyond twelve metres, moving-target flankers spread sideways before cutting ahead. Each
interceptor gets a per-target slot, alternating sides with widths of six, eight, ten, and
twelve metres. Further rows trail by two metres. Up to 64 slots are admitted. Velocity
prediction still leads by 1.25 seconds, capped at eight metres before the row offset.

Other visible pursuers receive a lateral adjustment away from same-target officers within
three metres and within two metres vertically. Summed separation is capped at 2.5 metres
and projected sideways so it does not directly push officers away from the target. The
nearest visible pursuer and officers already within two metres keep native movement.
This also handles stationary targets. It does not change speed, weapon ranges, or collision
radii. Hidden targets immediately clear movement caches and use last-known-position search.

Movement redirects only native destinations within ten metres of a visible on-foot target.
Each officer caches one candidate for one second, keyed by player and original destination.
Candidates must sample within 0.75 metres using the officer's area mask, stay within one
metre of candidate elevation and two metres of target elevation, and leave at least 0.25
metres to the closest navigation edge. NavMeshAgent.CalculatePath must return PathComplete.
A NavMesh raycast from the sampled original destination to the candidate must be clear,
using the officer's agent type and area mask. This rejects offsets across navigation
boundaries even when the candidate could be reached by a long detour. Search candidates
use the same complete-path and edge checks without the offset raycast. Positions within 1.5 metres of another current same-target cached destination
are rejected. Failed candidates retain native movement. Checks are bounded to one per officer
per second (128 officers maximum); this does not guarantee separation in narrow passages.

Validate open streets, building corners, and narrow alleys: the nearest pursuer should
keep pressure while interceptors spread then advance. Check both wanted players in co-op
and confirm sides are assigned within each pursuit. Test stationary shootouts with 10+
officers, melee/arrest contact, and station departures for clustering or jitter. Break sight and turn behind a building;
search must remain around the old sighting. Check for repeated side-switching or stalled
movement near obstacles. Reachable endpoints do not guarantee obstacle-avoiding flanks.

## Stuck recovery

Active on-foot pursuers and foot patrols with a destination more than two metres away
request a fresh route after moving less than half a metre for six seconds. Paused,
disoriented, unconscious, seated, and ladder/off-mesh-link movement is skipped. Pending
paths can finish. Recovery is capped at two attempts per tick and three per stationary
episode, with no teleportation. Movement resumes custom visible-target offsets only after
a twelve-second pause. Search caches are cleared. The recovery goal is the currently
visible target, otherwise the incident's frozen last-known position; patrols retain their
route goal. Goals must pass the complete-path and edge checks before native movement is
asked to route again. Hidden targets are never read for recovery coordinates.

Navigation paths are reused through one lazily allocated NavMeshPath, released from the
managed cache on scene unload. Validation remains under the existing movement/search query
budgets, plus at most two recovery checks per tick. Recovery logs distinguish a submitted
request from failure to find a complete safe path. They do not establish movement success.
Validate navigation changes on the host as well as clients. NavMesh boundaries can disagree
with physical colliders, so map geometry or dynamic obstacles can still block officers.

## In-game checks

- Start with a minor offense. Check the small response and unchanged officer toughness.
- Compare resistance/nonlethal assault with gunfire and serious assault. The latter should
  permit more reinforcements. Remaining allowance must account for earlier dispatches.
- During lethal pursuit, check for pistols and shotguns. Confirm weapon meshes, animations,
  firing, and damage work on both computers; check the log for the shotgun fallback warning.
- Confirm ordinary arrest-level encounters do not immediately turn into firefights.
- Inflict three distinct lethal hits on police, then test an attributed police kill.
  Tactical should allow larger bursts and more shotgun slots.
- With no available local patrol, confirm a station crew arrives without double-budgeting.
- Check support officers keep more distance and interceptors cut ahead of movement.
- Break all line of sight: low tiers stop dispatches; Armed/Tactical may send one final wave within eight seconds, then stop. Search last-known positions.
- Escape and provoke a minor incident: the prior tactical tier and allowance must reset.
- In co-op, make only the joining Windows player violent. The host must not inherit that severity.
- Make both players wanted, then let one escape or disconnect. The other incident must survive.
- Check indoor paths, stuck recovery, scene reload, logs, and frame pacing with large responses.
- Reproduce wall-walking on the host at a building corner, narrow doorway, and station exit.
  Check offset fallback and twelve-second recovery pauses, patrol route resumption, and no
  teleportation. Try losing sight behind the wall: recovery must use the old sighting.
- Compare all clients with the host: a visual-only client stall may be replication rather
  than route selection. Pure regression checks cannot verify the native NavMesh queries.

## Build and checks

```sh
MELONLOADER_DIR='/path/to/profile/MelonLoader' ./build.sh
nix-shell -p dotnet-sdk_8 --run 'dotnet run --project tests/Tests.csproj'
```

MelonLoader must contain `net6` and generated `Il2CppAssemblies` from a successful
modded game launch. Game references are not redistributed.
Output: `dist/CoordinatedPolice-0.6.2.zip`.

Format C# with `dotnet format CoordinatedPolice.csproj` in a .NET SDK Nix shell,
with `MelonLoaderDir` in the environment. Format `tests/Tests.csproj` separately.
Format `package.py` with Ruff if changed.

Automated checks cover severity triggers, duplicate attacks, tier limits, shared budgets,
escalation during breaks, roles, weapon policy, search, interception, and stuck recovery.
They do not establish Harmony hook behavior, asset loading, native damage ordering,
station delivery, population lifecycle, animation, or multiplayer compatibility. Those require the game checks.

The host processes at most 16 incidents and 128 officers. Caught game API failures disable
custom behavior for that scene and log the failure. Native behavior then continues.

## Multiplayer diagnostics

Authority and connected-count changes log on both peers at the one-second update cadence.
Host response snapshots log on phase, severity, identity, or active-limit changes, otherwise
once per ten seconds per incident. Up to sixteen diagnostic slots reset on scene unload.
Dispatch messages distinguish approved station requests from actual arrivals. Pool counts
in snapshots refer to the closest station to the saved last-known position. Diagnostics
do not establish client replication. Compare both logs during a joining-player pursuit,
join/leave, search, escape, and scene reload.
