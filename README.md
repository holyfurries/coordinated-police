# Coordinated Police

Police responses get larger as things get more serious. Officers chase, intercept, or hang back with a gun while others close in. Officer health, damage resistance, and movement speed stay at vanilla values.

## Response levels

| Response | Trigger | Dispatch pauses at | Total officer allowance |
| --- | --- | --- | --- |
| Patrol | Minor offenses | 4 active pursuers | 4 |
| Pursuit | Resistance, assault, or trafficking | 6 active pursuers | 8 |
| Armed | Gunfire, serious assault, or lethal pursuit | 12 active pursuers | 36 |
| Tactical | Killing an officer or three damaging lethal hits on police | 16 active pursuers | 64 |

Station crews and nearby backup share the allowance. These are the default solo limits. Each additional connected player adds 50% to active and total allowances, up to four-player scaling. Two players allow 18 active / 54 total at Armed and 24 active / 96 total at Tactical. Each wanted player has a separate response. Officers already dispatched still count. The game's pursuit state also feeds into severity; the mod doesn't raise it just because a timer has elapsed.

These are response tiers, not an on-screen star display. Patrol officers who independently spot you can still join, so the table isn't a hard cap on every officer in a chase.

## Patrols and reserves

| Connected players | Foot patrol target | Ready reserve target |
| --- | ---: | ---: |
| 1 | 5 | 8 |
| 2 | 10 | 12 |
| 3 | 15 | 16 |
| 4+ | 20 | 20 |

Extra patrol officers leave stations one at a time, five seconds apart. Patrol targets
are divided across unlocked districts with native routes. New officers favour districts
below their share, then less-used routes. Existing native foot patrols count toward
coverage. Routes that cross district boundaries belong to the district containing most
of their sampled waypoints.

When the overall target is reached, an idle patrol managed by this mod can walk from an
overstaffed district to an understaffed one. Reassignment requires a complete path and
stays at least 60 metres away from players. Officers in pursuits or other active duties
are left alone. Staffing and reassignment pause while anyone is wanted.

The counts above remain the defaults. With four players and all six districts eligible,
the 20-patrol target divides as 4/4/3/3/3/3. Locked districts or districts without native
routes receive no share. Limits remain subject to the game's existing officer pool.

Defeated officers can return as station replacements after at least **45 seconds**, at
most two every five seconds. Everyone must be **60 metres away from both the body and
station**. Replacements restore normal health; officers still have vanilla toughness.

These are targets, not guaranteed counts. The mod reuses the game's existing officers;
it does not create additional NPCs. Camping near bodies or stations can delay replacements.

## Features

- **Mixed loadouts:** Nonlethal pursuit mixes batons and tasers. Lethal pursuit mixes pistols and pump shotguns, with more shotgun slots at tactical level. Firearms still require the game's lethal-pursuit state.
- **Position-based roles:** The closest officer keeps chasing; groups of five or more keep two chasers. Officers already ahead or beside your route attempt cutoffs. Incoming units spread across approach positions on their side of the fight. Pistol support keeps more distance. Destinations need a complete path and clearance from navigation edges; blocked approaches fall back to normal pursuit.
- **Station backup:** Nearby officers respond first. If none are available, the mod requests a crew from the closest station. It uses the game's officer and vehicle pools.
- **Breathing room:** Solo armed responses send up to four officers per burst with eight-second breaks; tactical sends six with six-second breaks. Calls within those bursts are spaced two seconds and one second apart, respectively. With four players, Armed sends up to eight per burst with five-second breaks; Tactical sends ten with four-second breaks. Patrol and pursuit retain their 30-second breaks. Escalating to Armed or Tactical brings the next call forward to one second after the host processes the escalation. Incoming officers reserve space while they travel.
- **Search the area:** Armed and tactical responses can send one final radioed-in wave within eight seconds of losing sight, subject to cooldown and remaining allowance. After that, new dispatches stop. Foot officers split into separate search positions around your last-known location, spreading farther out with larger groups. They stop tracking your movement while nobody can see you. Being spotted resumes the pursuit without refilling the allowance.
- **Stuck recovery:** Pursuers and foot patrols retry after six seconds without progress. Stuck pursuers drop custom offsets for 12 seconds and retry toward a visible target or last-known position. Recovery runs on the host and does not teleport them.

## Installation

Install through **r2modman** or **Thunderstore Mod Manager**.

For manual installation:

1. Install **MelonLoader 0.7.3**.
2. Extract `CoordinatedPolice.dll` from the ZIP's `Mods` folder into your game's `Mods` folder.
3. Launch the game.

**IL2CPP only.** Built against Schedule I **0.4.6f13**. No weapon mods required.

## Multiplayer

Install the same version on everyone's machine. The host handles response severity, dispatch, patrol staffing, replacements, roles, and search. Each wanted player has their own allowance.

## Configuration

Launch once, then close the game and edit the **[CoordinatedPolice]** section of
**UserData/MelonPreferences.cfg** in your mod-manager profile. Restart to apply changes.
In multiplayer, the host's values govern police behaviour.

| Setting | Default | Meaning |
| --- | ---: | --- |
| patrols_per_player | 5 | Foot patrol target per player, up to four players; range 0–16 |
| reserve_base | 8 | Solo reserve target; range 0–64 |
| reserves_per_extra_player | 4 | Additional reserves per extra player; range 0–16 |
| response_size_multiplier | 1.0 | Multiplies active limits, total allowances and burst sizes; range 0.25–2 |
| reinforcement_time_multiplier | 1.0 | Multiplies dispatch delays and breaks; range 0.25–4; lower is faster |

District settings are **northtown_weight**, **westville_weight**, **downtown_weight**,
**docks_weight**, **suburbia_weight**, and **uptown_weight**. Each defaults to **1**
and accepts **0–10**. A weight of 2 gets roughly twice the patrol share of a weight
of 1. Zero excludes that district from new supplemental assignments; all zeros pause
new supplemental assignments everywhere. These weights divide the overall patrol
target rather than adding more officers.

For a four-player target of **32 patrols**, set **patrols_per_player = 8**.
For shorter reinforcement delays, set **reinforcement_time_multiplier = 0.75**.
The host's one-second coordination tick limits how precisely short delays are observed.

Response limits cap at 64 active officers and 16 per burst per incident. Patrol staffing
still protects at least four reserves or half the reserve target, whichever is larger.
Invalid values are clamped and logged. Setting patrols_per_player to 0 retires this mod's
extra patrols when safe; it does not remove the game's own patrols.

## Logs

Check `MelonLoader/Latest.log` in your mod-manager profile and search for `Police:`.
Both peers log network authority and player-count changes. `client/waiting` means that
peer is not running host decisions; it does not confirm the host has the mod installed.

The host logs each approved station dispatch and nearby pursuit request. Response summaries
show tier, search/pursuit state, active officers, allowance used, cooldown, and the nearest
station's officer pool. Summaries appear when the response changes and every ten seconds
while active. `station_pool=0` means that station has no available officers. Approval logs
confirm requests, not successful arrivals or client replication. Share both peers' logs
when checking multiplayer issues.

Police: District patrols reports assigned/target counts for all six districts. Assignment
and reassignment messages identify the destination district and route. Counts follow
assigned routes, so officers travelling there count toward coverage.

`Police: Population` reports actual patrols, reserves, defeated officers, and targets every
30 seconds. `Reserve replacement requested` includes whether the officer is already pooled;
a delayed return logs `Reserve return confirmed`. Client reconciliation logs show when a
restored officer's local death state is cleared after host health and behaviour updates.

Search for `Police: Navigation recovery` in the **host's** log when checking wall-walking.
A recovery request does not confirm that the officer successfully moved afterward.

Occasionally, an officer makes an unusual headwear choice. It is purely cosmetic.

## Compatibility

Disable **Police Response Overhaul** and **Hardcore Police** before using this mod. Coordinated Police disables itself if either is detected.

Other mods that change police pursuit, weapons, or dispatch may conflict. Pickpocket inventories still use the game's normal loot. The host configuration controls patrol targets and reinforcement pacing.

## Credits

Inspired by [Hardcore Police](https://www.nexusmods.com/schedule1/mods/475) by Babyhamsta and [Police Response Overhaul](https://thunderstore.io/c/schedule-i/p/UncleTyrone/PoliceResponseOverhaul_IL2CPP/) by UncleTyrone. District patrol distribution is also inspired by
[Enhanced Law Enforcement](https://www.nexusmods.com/schedule1/mods/531) by SurrealNirvana.

## Development

Build instructions: [BUILDING.md](https://github.com/holyfurries/coordinated-police/blob/main/BUILDING.md).

## License

[MIT](https://github.com/holyfurries/coordinated-police/blob/main/LICENSE).
