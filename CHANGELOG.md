## Changelog

### 1.2.0

- Add support for [LobbyCompatibility](https://thunderstore.io/c/lethal-company/p/BMX/LobbyCompatibility/)
    - Compatibility level is set to minor version. E.g. Any ``1.2.X`` lobby is compatible with any other ``1.2.X`` version of Mirage.
    - This only applies to ``v1.2.X`` and forward, as I did not follow proper versioning in the ``1.0.X`` versions.
- Fixed issues of players not being able to hear voices mimicking them at times.

### 1.1.1

- Add configuration option ``ImitateMode`` to change how a recording is picked.
    - Set this to ``Random`` to keep the current behaviour (recordings are picked completely random).
    - Set this to ``NoRepeat`` to avoid repeating the same recording (this can still happen when not many recordings exist). Here is how it works:
        - A pool of recordings is filled.
        - A recording is randomly picked, and then removed from the pool.
        - When pool becomes empty, it becomes filled again.
- ``SpawnOnPlayerDeath`` no longer spawns an enemy if the player is not on a navmesh.
    - Previously, this was band-aid fixed by preventing a spawn if the player died by gravity.
    - ``CauseOfDeath: Gravity`` no longer prevents the masked enemy from spawning, meaning ladders can spawn them properly now.
    - Note: Many areas throughout the map such as standing on top of a railing is detected as a missing navmesh.
      Such areas will prevent ``SpawnOnPlayerDeath`` from spawning a masked enemy.

### 1.1.0

- Rewrote the config syncing logic. Thanks to Owen3H (author of CSync) for their config syncing [implementation](https://gist.github.com/Owen3H/c73e09314ed71b254256cbb15fd8c51e/5f314116ccd2ba3e5a2a38f01cf889dc674f2cfa).
- ``DeleteRecordingsPerRound`` has been slightly reworked.
    - Setting this to ``true`` is unchanged (recordings get deleted after the lever is pulled, per round).
    - Setting this to ``false`` now only deletes when closing the game.
    - Default value is now set to ``false``.
- ``MuteLocalPlayerVoice`` is now set to ``true`` as the default.
- ``EnablePenalty`` is now set to ``true`` as the default.
    - To clarify, setting this to ``true`` is the vanilla behaviour.
    - This will likely be removed in a future update, as it's a relic of the past back when Mirage had different goals.

### 1.0.16

- Fixed a bug that caused the ship teleporter to not work if ``SpawnOnPlayerDeath`` spawned a masked enemy
- [DissonanceLagFix](https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/) is no longer needed when using Mirage
- Modify configuration descriptions to hopefully be easier to understand
- Modify configuration default values

### 1.0.15

- Fixed a mod conflict issue that caused enemies to not despawn properly
- ``SpawnOnPlayerDeath`` no longer spawns an enemy when ``CauseOfDeath = Gravity`` (this means dying to fall damage, and I believe dying to the ladder as well)
    - This was originally done by **jarylc**, but I tried alternatives to avoid having the mechanic completely ignore dying to gravity
    - This is probably the best band-aid fix for now for avoiding log spam when a masked enemy spawns inside a pit

Huge thanks to **TheDebbyCase** for their immense effort on bug-testing and figuring out a way to reproduce errors that others have been experiencing.
Thanks to **dinogolfer** from TMLC for figuring out why non-masked enemies weren't despawning as well, which helped me understand why this mod conflict was even happening.

### 1.0.14

- Fixed a bug where some enemies types didn't mimic voices
- Add configuration option to always mute the mimicking voice of a local player
    - Set ``AlwaysMuteLocalPlayer`` to true to never hear your voice at all (even while dead)
    - This ignores the ``MuteLocalPlayerVoice`` value if enabled
    - This value is not synced to all clients, as it's an optional feature for those who don't want to hear their own voice

### 1.0.13

- AdvancedCompany cosmetics now properly apply on hosts (previously only applied on non-hosts)
- Fixed a bug that caused ``MuteLocalPlayerVoice`` to get ignored

### 1.0.12

- Mimicked voices should now sound a lot more like an actual player's voice (changed audio filters)
- ``SpawnOnPlayerDeath`` no longer spams an error if a player falls into the void
- Masked enemies no longer mimic voices if it was spawned after a player falls into the void

### 1.0.11

- Hotfix: Revert the crossed-out change from v1.0.10, which caused non-host players to not hear any voices

### 1.0.10

- Fixed dress girl issues
    - Mimicking voice is now muted while the dress girl is invisible
    - In singleplayer, dress girl will always mimic the local player's voice
    - In multiplayer, dress girl will always mimic the non-local player's voice
- Bees no longer mimic voices when ``EnableModdedEnemies`` is enabled.
- Add configuration option to mimic voices for locust swarms
- ~~Fixed navmesh error spam when an enemy tries to calculate its path~~
- Recordings deletion can now be ignored (not synced to all players)

### 1.0.9

- Ship camera now spectates the correct masked enemy
- Recording deletion can now be configured in two ways:
    - ``DeleteRecordingsPerRound = true`` to delete recordings per-round
    - ``DeleteRecordingsPerRound = false`` to delete recordings per-game
- Recordings now automatically delete upon closing the game

### 1.0.8

- Fixed a bug where voice filters weren't being applied, causing mimicked voices to behave unpredictably
- Fixed a bug where **SpawnOnPlayerDeath** could spawn two masked enemies in certain scenarios

### 1.0.7

- Hotfix: If the local player's mimicking voice is muted, it now becomes unmuted when the player is dead

### 1.0.6

Thanks to [jarylc](https://github.com/jarylc) for the initial [fixes](https://gist.github.com/jarylc/3f6fc305466d4970deae16629469b3c2) for masked enemy spawns.

- Rewrite masked enemy spawn and mimicking logic
    - This is less invasive now and should conflict with other mods less often
    - Fixed a bug where naturally spawned masked enemies weren't synced properly
- Fixed a bug where players not haunted by the ghost girl could hear the mimicking voice.
- Add configuration option to only spawn player on death, when the dying player is alone
- Add configuration option to mute the mimicking voice when mimicking the local player
- Add configuration option to adjust voice mimicking delay for non-masked enemies
- Add configuration option to mimic voices on modded enemies

### 1.0.5

- Use audio filters to sound more like vanilla voices
- Mimicked voices on the outside can no longer be heard from inside the facility (and vice-versa)
- Masked enemies no longer mimic voices while hiding on the ship

### 1.0.4

- Support voice mimicking on all vanilla enemies.
- Add configuration option for mask texture and arms-out animation (for masked enemies).
- Dead enemies no longer mimic voices.

### 1.0.3

- Support voice activity.

### 1.0.2

- Bundled all dependencies I control into a single core lib (users now pull less packages).

### 1.0.1

- Spawn on player death is now configurable.
- Naturally spawned masked enemies now mimic a player at random.
- Dependencies are now separated into its own packages.

### 1.0.0

- Initial release.
