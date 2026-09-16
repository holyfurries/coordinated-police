# Coordinated Police

Police responses get larger as things get more serious. Officers chase, intercept, or hang back with a gun while others close in. Officer health, damage resistance, and movement speed stay at vanilla values.

## Response levels

| Response | Trigger | Dispatch pauses at | Total officer allowance |
| --- | --- | --- | --- |
| Patrol | Minor offenses | 4 active pursuers | 4 |
| Pursuit | Resistance, assault, or trafficking | 6 active pursuers | 8 |
| Armed | Gunfire, serious assault, or lethal pursuit | 12 active pursuers | 36 |
| Tactical | Killing an officer or three damaging lethal hits on police | 16 active pursuers | 64 |

Station crews and nearby backup share the allowance. These are solo limits. Each additional connected player adds 50% to active and total allowances, up to four-player scaling. Two players allow 18 active / 54 total at Armed and 24 active / 96 total at Tactical. Each wanted player has a separate response. Officers already dispatched still count. The game's pursuit state also feeds into severity; the mod doesn't raise it just because a timer has elapsed.

These are response tiers, not an on-screen star display. Patrol officers who independently spot you can still join, so the table isn't a hard cap on every officer in a chase.

## Patrols and reserves

| Connected players | Foot patrol target | Ready reserve target |
| --- | ---: | ---: |
| 1 | 5 | 8 |
| 2 | 10 | 12 |
| 3 | 15 | 16 |
| 4+ | 20 | 20 |

Extra patrol officers leave stations one at a time, five seconds apart, and spread across initial waypoints on the game's routes. Staffing pauses while
anyone is wanted, keeping reserves available for backup.

Defeated officers can return as station replacements after at least **45 seconds**, at
most two every five seconds. Everyone must be **60 metres away from both the body and
station**. Replacements restore normal health; officers still have vanilla toughness.

These are targets, not guaranteed counts. The mod reuses the game's existing officers;
it does not create additional NPCs. Camping near bodies or stations can delay replacements.

## Features

- **Mixed loadouts:** Nonlethal pursuit mixes batons and tasers. Lethal pursuit mixes pistols and pump shotguns, with more shotgun slots at tactical level. Firearms still require the game's lethal-pursuit state.
- **Different roles:** Chasers close in, interceptors move ahead and to the sides, and pistol support officers keep more distance. The nearest visible pursuer keeps chasing while flankers spread to opposite sides before cutting ahead. Flankers use separate positions instead of sharing two spots. Nearby pursuers shift sideways to reduce crowding, while the closest officer keeps pressure. Offsets across navigation boundaries or too close to navigation edges fall back to normal pursuit.
- **Station backup:** Nearby officers respond first. If none are available, the mod requests a crew from the closest station. It uses the game's officer and vehicle pools.
- **Breathing room:** Solo armed responses send up to four officers per burst with eight-second breaks; tactical sends six with six-second breaks. Calls within those bursts are spaced two seconds and one second apart, respectively. With four players, Armed sends up to eight per burst with five-second breaks; Tactical sends ten with four-second breaks. Patrol and pursuit retain their 30-second breaks. Escalating to Armed or Tactical brings the next call forward to one second after the host processes the escalation. Incoming officers reserve space while they travel.
- **Search the area:** Armed and tactical responses can send one final radioed-in wave within eight seconds of losing sight, subject to cooldown and remaining allowance. After that, new dispatches stop. Foot officers search around your last-known position. Being spotted resumes the pursuit without refilling the allowance.
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

`Police: Population` reports actual patrols, reserves, defeated officers, and targets every
30 seconds. `Reserve replacement requested` includes whether the officer is already pooled;
a delayed return logs `Reserve return confirmed`. Client reconciliation logs show when a
restored officer's local death state is cleared after host health and behaviour updates.

Search for `Police: Navigation recovery` in the **host's** log when checking wall-walking.
A recovery request does not confirm that the officer successfully moved afterward.

## Compatibility

Disable **Police Response Overhaul** and **Hardcore Police** before using this mod. Coordinated Police disables itself if either is detected.

Other mods that change police pursuit, weapons, or dispatch may conflict. Pickpocket inventories still use the game's normal loot. There are no configuration options in this version.

## Credits

Inspired by [Hardcore Police](https://www.nexusmods.com/schedule1/mods/475) by Babyhamsta and [Police Response Overhaul](https://thunderstore.io/c/schedule-i/p/UncleTyrone/PoliceResponseOverhaul_IL2CPP/) by UncleTyrone.

## Development

Build instructions: [BUILDING.md](https://github.com/holyfurries/coordinated-police/blob/main/BUILDING.md).

## License

[MIT](https://github.com/holyfurries/coordinated-police/blob/main/LICENSE).
