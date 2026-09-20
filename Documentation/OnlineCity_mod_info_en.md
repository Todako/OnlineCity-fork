**OnlineCity** is a multiplayer mod for **RimWorld** that allows multiple players to play on the same planet online.

Each player develops their own colony, sees the colonies and caravans of other players, and can interact with them: transfer resources and colonists, trigger events, or attack.

1. Installation and Getting Started
2. Multiplayer Basics
3. Transferring Items and Colonists
4. Events and Other Interactions
5. PvP — Attacking Colonies
6. Mods and Compatibility
7. Known Issues and Troubleshooting
8. Frequently Asked Questions (FAQ)
9. Quick Server Setup
10. Preparing Mods for the Server
11. Mod Synchronization
12. Server Settings
13. Player Administration
14. Banning Players
15. Statistics and Monitoring
16. Server Logs
17. Commands

---

# 1. Installation and Getting Started

## Installation

1. Install **RimWorld 1.1–1.4**.
2. Install **Harmony** and **HugsLib**.
3. Install **OnlineCity**.
4. In the **"Mods"** menu, arrange the load order roughly as follows:
**Harmony → Core → HugsLib → other mods → OnlineCity**

OnlineCity should preferably be kept **last**. Restart the game after changing mod order.

### `ModsConfig.xml`

The mod list must start with:

```xml
<li>brrainz.harmony</li>
<li>ludeon.rimworld</li>
<li>unlimitedhugs.hugslib</li>

```

OnlineCity should preferably be placed at the end:

```xml
<li>aant.onlinecity</li>

```

## Getting Started

1. In the main menu, select **"Multiplayer"**.
2. Register a new account or log in to an existing one.
3. Select a landing site.
4. Create or select colonists.
5. Start the game.

> ℹ️ The first login may take several minutes, especially if the server uses a large modpack.

Before connecting for the first time, it is strongly recommended to back up your **`Mods`** and **`Config`** folders.

## Saving the Game

Multiplayer progress is saved **on the server**.

* Autosaves run every **15 minutes**. The interval can be adjusted in the settings.
* The game is also saved when exiting via the in-game menu.
* Closing the game with **Alt+F4** or force-closing it will result in lost unsaved progress.

For offline play, you can load `onlineCityTempLoad`, but changes made there **cannot be transferred back to the server**.

OnlineCity functions only when launched through the **"Multiplayer"** menu option.

---

# 2. Multiplayer Basics

OnlineCity does not alter vanilla RimWorld core gameplay. Factions, NPCs, events, and other mechanics for each player function independently. Each player has their own game session and colony, but all players share the same global world map.

## Colonies and Caravans

Other players' colonies and caravans are displayed on the world map.

**Colonies:**

* The icon appearance roughly indicates its wealth.
* A lit window on the colony icon shows that the player is online.
* Selecting a colony displays the player name, online status, and total colony market value.

**Caravans** of other players are also visible on the world map. Caravan tooltips display remaining available **carrying capacity**.

## Online Chat

The **"Online"** button opens the chat and other multiplayer features.

In the chat, you can:

* View the list of all players and players currently online;
* Create custom channels;
* Invite other players to your channels;
* View player profile information.

---

# 3. Transferring Items and Colonists

Transfers between players are carried out **exclusively via caravans**.

## How to Transfer Items

1. Select your caravan.
2. **Right-click** on another player's colony or caravan.
3. Select **"Trade goods"** (Exchange goods).
4. Select the goods, animals, or colonists to transfer.

You can transfer **any volume of goods an unlimited number of times**. The recipient can be offline.

There is no direct barter window: players negotiate in chat and send items turn by turn.

## Receiving Transfers

Upon receiving a transfer, a notification appears listing the transferred items along with options to **accept or decline**.

⚠️ If a transfer is declined, the transferred items are **permanently destroyed**.

Accepted items are placed:

1. In the largest stockpile containing `trad` or `торг` in its name (e.g., `trade`);
2. If no such stockpile exists — in the largest available stockpile.

## Special Cases

**Prisoners.**

Transferred prisoners appear as passive hostile colonists. You must manually arrest and escort them to a prison cell.

**The Caravan's Last Colonist.**

If you transfer the final colonist, the caravan disbands, and its entire inventory is automatically added to the transfer. To prevent this, leave at least **one animal** in the caravan.

**Social Bonds.**

When a colonist is transferred, all social bonds with other colonists are broken.

**Assisting Another Player.**

Transfer mechanics can be used not only for trade, but also for emergency aid during raids or other disasters. For example, you can send combat colonists to an ally and have them transferred back after the battle.

---

# 4. Events and Other Interactions

Selecting another player's colony on the world map allows you to trigger incidents via the **"Interaction"** menu. For gold, you can summon: pirate raids, tribal assaults, infestations, mechanoids, toxic fallout, and other events.

## Event Power

For certain events, you can specify impact power — a multiplier relative to the standard event scale. The typical maximum multiplier is **×10**.

Cost depends on colony wealth and event power:

```text
(colony_wealth / 100,000)^(2/3) × 100 × impact_power^(3/2)

```

### Threat Type Multipliers

* **×2** — Industrial-tech Pirates
* **×3** — Infestations (Insects)
* **×5** — Mechanoids

### Arrival Method Multipliers

* **×1.0** — Walk-in from map edge
* **×1.4** — Center drop pods
* **×1.5** — Scattered drop pods

The cost of drop pods is automatically calculated into the total raid price.

> ℹ️ Formulas and coefficients may change during rebalancing. The server can also enforce custom cost modifiers and maximum event power caps.

## Summoning Raids

A summoned raid does not necessarily arrive instantly and will only trigger if the target player is online.
If raid power exceeds **50% of the maximum allowed**, an early warning notification is sent **half an in-game day** in advance.

After a raid concludes, the next event cannot occur sooner than **1 in-game day**.
The cooldown delay is calculated as follows:

```text
1 + impact_power × 0.16666 + 0.03333 × (colony_wealth / 100,000)

```

## Novice Protection

Combat interaction is restricted to players who meet **both** criteria:

* Both the aggressor and the target must have over **2 years of in-game time**;
* Colony wealth must exceed **100,000**.

## Event Queue

Events from different categories can be initiated simultaneously.

* A player can queue **1 event from each tab** against a single target.
* Other players can also add events against the same target.
* Events execute **sequentially**, preserving scheduled cooldown delays.
* Maximum queue length is **2 events**.

---

# 5. PvP — Attacking Colonies

PvP allows you to attack another player's colony using your caravan. To initiate an attack, **both players** must have **"Participate in PvP"** enabled. You can only attack a player who is **currently online**.

## Initiating an Attack

Once combat begins, the attacker's caravan spawns at a random location along the map edge.

The attacker can:

* Scout the colony and retreat;
* Pillage resources;
* Conquer the colony by downing all defending colonists.

Interior rooms remain concealed behind fog of war until doors or walls are breached.

## PvP Mechanics & Peculiarities

Attacking colonists are controlled **remotely**. Because of this:

* Movement and commands may exhibit latency;
* Aiming and firing sequences are not displayed in real time;
* After **1 minute** of combat, defender colonist movement speed is halved as partial compensation.

During PvP:

* Game speed is locked to **×0.5**;
* Pausing and fast-forwarding are disabled;
* Time of day and weather synchronize with the defender's instance.

## Colonist Controls

Only fundamental combat commands are permitted during an assault:

* Movement;
* Ranged attack;
* Melee attack;
* Picking up and dropping items;
* Swapping weapons and apparel;
* Consuming food and drinks.

Building, deconstructing structures, activating artifacts, and most non-combat tasks are disabled.

**Auto-attack is always active.**

Once the colony is captured, these restrictions are lifted.

## Maximum Attacker Caravan Wealth

The caravan's total market value cannot exceed:

```text
4 / (25 / 1,000,000 + 10 / colony_wealth)

```

Reference values:

| Colony Wealth | Maximum Caravan Wealth |
| --- | --- |
| 100,000 | 32,000 |
| 400,000 | 80,000 |
| 1,000,000 | 114,400 |
| 2,000,000 | 133,332 |

If a caravan exceeds the limit, split it or reduce the number of attacking pawns.

## Disconnecting and Retreating

If either player disconnects during combat, it is considered an **automatic forfeit**:

* The attacker loses the caravan;
* The defender loses the colony.

After a disconnection, remaining hostile colonists continue fighting under vanilla AI control.

To retreat safely, use the **"Retreat"** command. All colonists within **10 tiles of the map edge** will return to the caravan.

Tamed animals in the attacking caravan do not participate in combat. They remain at the map edge until combat resolves or a retreat is called.

## Limitations and Known Issues

* Move commands may occasionally drop, causing the colonist to fall back.
* Health, inventory, and needs updates sync with a slight delay.
* Downed pawns are excluded from auto-targeting and melee attacks. Use targeted ranged fire to execute them.
* Tiles directly on the map boundary are impassable for attacking pawns.
* Items dropped on map edge tiles can only be retrieved after capturing the settlement.
* If a colonist gets stuck near the map edge, select them and right-click several locations further inland until pathfinding resumes.

> ℹ️ Servers can adjust PvP parameters, including maximum caravan value and grace periods between attacks. The default grace period is **20 minutes**.

---

# 6. Mods and Compatibility

OnlineCity is compatible with many third-party mods, but stability depends on their mechanics and configuration.

## Mod Synchronization

The server can enforce **Mod Synchronization**. When connecting, the following may be overwritten:

* The `Mods` folder;
* The `ModsConfig.xml` file.

⚠️ Always back up your **`Mods`** and **`Config`** directories before connecting for the first time.

**DLCs are not synchronized.**

* If the server runs a DLC, you must own and install it locally.
* If the server does not run a DLC, it will be automatically disabled.

If mod synchronization is disabled, players may run differing mod setups. However, the OnlineCity mod version must strictly match the server build.

## Items from Other Mods

Exercise caution when transferring modded items. If the requisite mod is present on **both clients**, issues rarely arise.

If the recipient lacks the mod, the item may:

* Lose custom stats/properties;
* Disappear entirely;
* Throw null errors or, in rare cases, corrupt the local save.

In case of severe corruption, contact the server administrator to restore a backup.

## Planet-Altering Mods

Mods modifying world generation should never be used without server synchronization, including those changing:

* Terrain and biomes;
* Oceans and coastlines;
* Mountain ranges;
* Global map features.

Mismatched world generation will cause colonies to spawn submerged in water or become unreachable.

These mods are only safe when all players share **identical mods and generation configs**.

## Known Compatibility Issues

Confirmed incompatible mod: **Save Our Ship 2**

Other mods can function with OnlineCity, but extensive overhauls altering core RimWorld game loops may cause desyncs and crashes.

**Zetrith Multiplayer** can run alongside OnlineCity, as they employ distinct multiplayer architectures:

* OnlineCity — single planet with independent colonies;
* Zetrith Multiplayer — co-op control over a shared colony.

---

# 7. Known Issues and Troubleshooting

## Long Loading Times Upon Login

After authenticating, OnlineCity downloads required mods and saves from the server. During this phase, the client may:

* Freeze;
* Display a black screen;
* Become unresponsive for several minutes.

This is expected behavior. Typically, waiting **5–10 minutes** resolves it. First-time connections over slow connections with large modpacks take significantly longer.

## "Not All Files Passed Verification"

If a file verification error appears after connecting, try **reconnecting**. This often happens after minor changes to the local `Mods` directory or `ModsConfig.xml`.

## Game Restarts After Opening the "Mods" Menu

When mod synchronization is enabled, opening the vanilla **"Mods"** menu prompts RimWorld to touch `ModsConfig.xml`. Consequently, OnlineCity may restart the game upon the next connection. This is normal. Avoid modifying mod lists locally when mod sync is enabled.

## Version Mismatch

Navigate to: **"Multiplayer" → "What's what"**
The OnlineCity version is listed on the first line and must match the server version.

## Username Already Taken

Ensure your username and password are longer than 3 Latin characters. If the error persists, select a different nickname.

## Connection Instability

Unstable network connections can cause:

* Desynchronization errors;
* Stalled transfers/downloads;
* Repeated drops.

## Mod Conflicts

Disable secondary mods and test the base mod setup. If the issue resolves, isolate the conflicting mod. Mods modifying vanilla combat/PvP logic are especially prone to breaking game states.

## Colonist Transfer Errors

A known issue exists when transferring colonists generated under **HardcoreSK**. Similar conflicts can occur with complex pawn-modifying frameworks.

## Duplicate Transferred Colonists

Occasionally, after transferring a colonist, a cloned copy with identical names, backgrounds, and skills may spawn in a world quest or ask to join your colony. Do not accept this pawn — it can crash the save file.

## Corrupted Saves

If a colony fails to load or critical errors halt progress, contact your administrator. Administrators can **restore the colony from an earlier server backup**. Alternatively, you can start a fresh colony under your existing account.

## System Requirements

OnlineCity increases system RAM overhead. A **64-bit operating system** is strongly recommended.

---

# 8. Frequently Asked Questions (FAQ)

## Can multiple players manage the same colony?

No. Under standard operation, each player controls their own colony. The only exception is attacking another player's base in PvP, which is not cooperative colony management. For co-op play, use **Zetrith Multiplayer**.

## What happens to other players when I pause the game?

Nothing. Each participant's game runs independently. Pausing only halts your local simulation. You will still see real-time movements of other colonies and caravans on the world map.

## How do I change difficulty and the AI Storyteller?

They cannot be changed by players. They are defined globally by the administrator during world creation.

## How do I delete my colony?

Open the **"Online"** window via the bottom dock or in-game menu. Under the **"About mod"** tab, click **"Start anew"**.
You can also execute this command via chat:

```text
/killmyallplease

```

## How frequently does the game save?

By default, **every 15 minutes**. It also saves whenever you exit through the main menu.

⚠️ **Alt+F4 bypasses exit saving**, which rolls your colony back to the most recent autosave.

## What should I do if I forget my password?

An administrator can reset your password using the command:

```text
/ChangePassword {UserLogin} {NewPassword}

```

Administrators should verify account ownership before resetting passwords.

## Is Developer Mode available?

This depends on the server configuration. If disabled on the server, regular players cannot activate Dev Mode.

## Where are the OnlineCity logs located?

Logs are written to:

```text
%appdata%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\OnlineCity

```

Paste this path directly into the Windows File Explorer address bar.

---

# 9. Quick Server Setup

## Installation

1. Extract the server archive to any folder.
2. Launch `Server.exe` and close it immediately.
3. Upon first launch, the server generates:
* The `World` folder;
* The `Settings.json` file.



For a vanilla server configuration without mod synchronization, simply relaunch `Server.exe`.

## First Launch

The first user to register on the server automatically receives **Administrator** privileges.

During initial setup, configure:

* World seed;
* Planet coverage;
* Game difficulty;
* AI Storyteller.

World generation takes several minutes. Once finished, reconnect and establish your colony like a standard player.

## Testing Local Connection

To verify server functionality:

1. Start the server.
2. Launch RimWorld on the same machine.
3. In the server address field, enter:

```text
localhost

```

If you connect successfully, the server works, and any connection failures from outside are caused by network/firewall routing.

## External Player Connections

The default server port is: **19019**

To allow connections over the internet, forward this port:

* In your local OS firewall;
* In your router settings.

If you changed `Port` in `Settings.json`, forward that designated port instead.

If you lack a dedicated public IP address, use tunneling tools such as **Hamachi**, **Radmin VPN**, or similar virtual networks.

---

# 10. Preparing Mods for the Server

The server requires mod copies stored directly in the local game folder rather than symlinked Steam Workshop directories.

## Preparing the `Mods` Folder

1. Remove all mods not intended for server use.
2. Copy the required mods into the server mod directory.

Whenever the modpack is updated, test stability locally before starting the server.

## Preparing `Config`

Clear the game settings folder prior to setup:

```text
%appdata%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config

```

You may retain `KeyPrefs.xml` and `Prefs.xml`.

Launch the game and configure your mod settings. It is recommended to **start a temporary single-player game and play for a few minutes** so mods finish writing their configurations to disk, as some mods do not initialize configs on launch. Skipping this can cause settings mismatches, prompting the server to repeatedly overwrite configs in an endless loop.

Next, copy these directories over to the server root:

* The `Mods` folder;
* The `Config` folder.

Your root directory next to `Server.exe` should contain:

```text
World
Mods
Config

```

---

# 11. Mod Synchronization

Synchronization rules are defined in `Settings.json` inside the following array:

```json
"EqualFiles": [...]

```

This defines which files and folders must be verified or overwritten on client machines upon connection.

> ⚠️ Ensure all JSON syntax, especially commas, remains valid in `Settings.json`.

Open `Settings.json` in a text editor. By default, it appears as follows:

```json
{
  "ServerName": "Another OnlineCity Server",
  "SaveInterval": 10000,
  "Port": 19019,
  "Description": null,
  "IsModsWhitelisted": false,
  "DisableDevMode": false,
  "MinutesIntervalBetweenPVP": 0,
  "GeneralSettings": {
    "StorytellerDef": "",
    "Difficulty": "",
    "EnablePVP": false,
    "DisableGameSettings": false,
    "IncidentEnable": true,
    "IncidentCountInOffline": 2,
    "IncidentMaxMult": 10,
    "IncidentTickDelayBetween": 60000,
    "IncidentCostPrecent": 100,
    "IncidentPowerPrecent": 100,
    "IncidentCoolDownPercent": 100,
    "IncidentAlarmInHours": 10,
    "EquableWorldObjects": false,
    "ExchengeEnable": true,
    "ScenarioAviable": true,
    "ExchengePrecentCommissionConvertToCashlessCurrency": 50,
    "ExchengeCostCargoDelivery": 1000,
    "ExchengeAddPrecentCostForFastCargoDelivery": 100,
    "StartGameYear": -1,
    "EntranceWarning": null,
    "EntranceWarningRussian": null
  },
  "EqualFiles": [
    {
      "FolderType": 2,
      "ServerPath": "Mods",
      "NeedReplace": true,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        ".cs",
        ".csproj",
        ".sln",
        ".gitignore",
        ".gitattributes"
      ],
      "IgnoreFolder": [
        "bin",
        "obj",
        ".vs"
      ]
    },
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        "KeyPrefs.xml",
        "Knowledge.xml",
        "LastPlayedVersion.txt",
        "Prefs.xml"
      ],
      "IgnoreFolder": null
    },
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": [
        "OnlineCity"
      ],
      "XMLFileName": "..\\HugsLib\\ModSettings.xml",
      "IgnoreFile": null,
      "IgnoreFolder": null
    }
  ],
  "ProtectingNovice": false,
  "DeleteAbandonedSettlements": false,
  "ColonyScreenFolderMaxMb": 0
}

```

Replace the entire `"EqualFiles": [ ... ]` block with:

```json
  "EqualFiles": [
    {
      "FolderType": 2,
      "ServerPath": "Mods",
      "NeedReplace": true,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        ".cs",
        ".csproj",
        ".sln",
        ".gitignore",
        ".gitattributes",
        "resources.assets.resS",
        ".git",
        ".obj",
        "packages",
        ".vs"
      ],
      "IgnoreFolder": [
        "bin",
        "obj",
        ".vs", 
        "packages"
      ]
    },
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        "Mod___LocalCopy_Performance Optimizer_-18-12_PerformanceOptimizerMod.xml",
        "Mod_2664723367_PerformanceOptimizerMod.xml",
        "Mod_Dubs Mint Menus_DubsMintMenusMod.xml",
        "Mod_Dubs Performance Analyzer_Modbase.xml",
        "Mod___LocalCopy_FrameRateControl_-26-12_FrameRateControlMod.xml",
        "Mod_Camera+_CameraPlusMain.xml",
        "Mod_FrameRateControl_FrameRateControlMod.xml",
        "Mod_RimThemes_RimThemes.xml",
        "Mod_performance_optimizer_PerformanceOptimizerMod.xml",
        "KeyPrefs.xml",
        "Knowledge.xml",
        "LastPlayedVersion.txt",
        "Prefs.xml"
      ],
      "IgnoreFolder": [
        "OnlineCity",
        "RimHUD"
      ]
    }
  ],

```

## Enabling Synchronization

To enforce synchronization, set:

```json
"IsModsWhitelisted": true

```

If set to `false`, the `EqualFiles` block is bypassed.

## Structure of `EqualFiles`

Each entry defines an independent synchronization rule.

| Field | Purpose |
| --- | --- |
| `FolderType` | Identifies the client-side root target directory |
| `ServerPath` | Relative path to the folder on the server |
| `NeedReplace` | Overwrite (`true`) or disconnect on mismatch (`false`) |
| `IgnoreFile` | List of file names or extensions to exclude |
| `IgnoreFolder` | List of folder names to exclude |
| `XMLFileName` | Target XML file for discrete node synchronization |
| `IgnoreTag` | XML tags or lines filtered out during comparison |

### `FolderType`

* 0 — Config
* 1 — Game root directory
* 2 — Mods

### `ServerPath`

Specifies the path on the server relative to `Server.exe`.

Examples:

```text
Mods
Config
Game

```

### `NeedReplace`

`true` — On mismatch, the file is automatically downloaded from the server.

`false` — On mismatch, the connection is rejected. This prevents transferring full game directories to clients while still validating file integrity.

---

## Example: Synchronizing the `Mods` Folder

```json
{
  "FolderType": 2,
  "ServerPath": "Mods",
  "NeedReplace": true,
  "IgnoreTag": null,
  "XMLFileName": null,
  "IgnoreFile": [
    ".cs",
    ".csproj",
    ".sln",
    ".gitignore",
    ".gitattributes",
    ".git",
    ".obj",
    "packages",
    ".vs"
  ],
  "IgnoreFolder": [
    "bin",
    "obj",
    ".vs",
    "packages"
  ]
}

```

This block synchronizes the **Mods folder**:

* Files with extensions in `IgnoreFile` and directories listed in `IgnoreFolder` are **ignored**.
* `NeedReplace: true` — Missing or modified mod files are **automatically downloaded and updated** from the server. Without this, an error is thrown and the client exits.
* `IgnoreTag` and `XMLFileName` must always be **`null`** when synchronizing directories.

> `IgnoreFile` — List of filenames or extensions to skip. You can supply exact names like `Prefs.xml` or extensions like `.cs`.
> `IgnoreFolder` — List of folder names to skip. Matches the **exact directory name**, regardless of nesting depth. Both folder content and directory existence checks are bypassed on both ends.

---

## Example: Synchronizing the Game Directory

```json
    {
      "FolderType": 1,
      "ServerPath": "Game",
      "NeedReplace": false,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        ".cs",
        ".csproj",
        ".sln",
        ".gitignore",
        ".gitattributes",
        "unins000.dat",
        "unins000.exe",
        "Version.txt"
      ],
      "IgnoreFolder": [
        "Mods",
        "RimWorldWin64_Data",
        "Languages"
      ]
  },

```

This block synchronizes the **Game directory**:

* For this directory, it is standard practice to use:

```json
"NeedReplace": false

```

Mismatched files are not transferred to the client; the connection is simply rejected to prevent distributing paid DLC files illegally.

* Excluded elements:
* `Mods` — Handled in its own sync rule;
* `RimWorldWin64_Data` — Large asset archive unneeded for simple integrity checks;
* Localization directories;
* `Version.txt` — May vary across game versions and store platforms.


* `IgnoreTag` and `XMLFileName` must be **`null`**.

> When using this block, mirror the game folder structure on the server. Folders listed in `IgnoreFolder` do not need to be copied to the server.

---

## Example: Synchronizing the `Config` Folder

```json
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": null,
      "XMLFileName": null,
      "IgnoreFile": [
        "Mod_performance_optimizer_PerformanceOptimizerMod.xml",
        "Mod_dubs mint menus_DubsMintMenusMod.xml",
        "TrueTerrainColorsCache.xml",
        "Mod_2800857642_map_preview_ModInstance.xml",
        "TrueTerrainColorsCache.xml",
        "Mod_Rpg Style + CE_Sandy_Detailed_RPG_Inventory.xml",
        "KeyPrefs.xml",
        "Knowledge.xml",
        "LastPlayedVersion.txt",
        "Prefs.xml"
      ],
      "IgnoreFolder": [
        "OnlineCity",
        "RimHUD"
      ]
    }

```

This block synchronizes the **Configuration folder**:

* Files in `IgnoreFile` and folders in `IgnoreFolder` are **ignored**.
* `NeedReplace: true` — Modified or missing settings files are **downloaded directly** from the server.
* `IgnoreTag` and `XMLFileName` must be **`null`**.

### Excluding Local Settings

If a specific setting **does not affect server balance** and can be left to player preference, add it to `IgnoreFile`. The server will not overwrite it. While you cannot modify it live during multiplayer sessions, it can be adjusted in **single-player mode** without desyncing other configs.

---

## Example: Synchronizing a Specific XML File

```json
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": [
        "OnlineCity"
      ],
      "XMLFileName": "..\\HugsLib\\ModSettings.xml",
      "IgnoreFile": null,
      "IgnoreFolder": null
    },

```

This block synchronizes **`HugsLib` settings**:

* The `OnlineCity` tags are **ignored** so synchronization does not erase saved server credentials and host history.
* `NeedReplace: true` — Modified files are automatically synchronized.
* `IgnoreFile` and `IgnoreFolder` must be set to **`null`** when syncing single files.

This is useful for synchronizing files while whitelisting select editable settings. Add target nodes to the `IgnoreTag` array.

> This example also demonstrates referencing files **outside the `Config` directory** specified in `ServerPath`. The `..` traverses up one folder level into `HugsLib`. All path separators must be escaped: `\\`.

---

## Example: Synchronizing While Ignoring a Specific Mod

```json
    {
      "FolderType": 0,
      "ServerPath": "Config",
      "NeedReplace": true,
      "IgnoreTag": [
        "version",
        "{lineWith}pirateby.anothertweaks"
      ],
      "XMLFileName": "ModsConfig.xml",
      "IgnoreFile": null,
      "IgnoreFolder": null
    }

```

This block synchronizes **`ModsConfig.xml` inside `Config**`:

* Elements in `IgnoreTag` are **ignored**. To ignore a specific mod, create an entry starting with `{lineWith}` followed by its mod ID tag (e.g., `<li>pirateby.anothertweaks</li>`).
* `NeedReplace: true` — Replaces invalid configurations.
* `IgnoreFile` and `IgnoreFolder` are **`null`**.

> Target tags are stripped before comparing files. If the client file differs only within these tags, it is treated as identical. `{lineWith}text` ignores any line containing that exact string.

To also prevent verification of that mod's folder, add its name to `IgnoreFolder` in your primary `Mods` sync block (e.g., `"AnotherTweaks"`):

```json
      "IgnoreFolder": [
        "bin",
        "obj",
        ".vs", 
        "packages", 
        "AnotherTweaks"
      ]

```

### Critical Behavior

If an XML file contains differences outside of `IgnoreTag`, the entire file will be replaced by the server version.

---

# 12. Server Settings

`Settings.json` parameters:

| Parameter | Description |
| --- | --- |
| `ServerName` | Name of the server. |
| `Description` | Server description. |
| `SaveInterval` | Interval for saving world data in ms. The default **10000** is recommended. |
| `Port` | Server port. **Primary networking parameter**. Default: `19019`. |
| `IsModsWhitelisted` | Strict mod list validation. `true` to enable, `false` to disable. When `false`, `EqualFiles` is skipped. |
| `DisableDevMode` | Forces Developer Mode off when players join. |
| `GeneralSettings` | Structural object containing gameplay and incident configurations. |
| `ProtectingNovice` | Prevents attacks and trade exploits targeting newly founded colonies. |
| `DeleteAbandonedSettlements` | Automatically cleans up inactive, underdeveloped colonies. |
| `DisableGameSettings` | Locks storyteller and mod settings during gameplay. |
| `IncidentEnable` | Enables summoning events via the **"Interaction"** menu on other colonies. |
| `IncidentCountInOffline` | Maximum queued events. Individual players can only queue one event at a time regardless. |
| `IncidentMaxMult` | Maximum event power multiplier. |
| `IncidentTickDelayBetween` | Minimum cooldown between events in ticks. **1 in-game day = 60,000 ticks.** |
| `IncidentCostPrecent` | Global event cost modifier, in percent. |
| `IncidentPowerPrecent` | Global raid power modifier, in percent. |
| `IncidentCoolDownPercent` | Cooldown duration modifier, in percent. Scales `IncidentTickDelayBetween`. |
| `IncidentAlarmInHours` | Early warning alert timer for strong incoming raids, in game hours. |
| `ExchengePrecentWealthForIncident` | Contribution of market assets, open orders, and balance toward threat scale. E.g., `1200` = **120%**. |
| `ExchengeCostCargoDelivery` | Cargo delivery fee per 100 tiles for every 1,000 silver in value. |
| `StartGameYear` | Initial game starting year (vanilla default: **5500**). |
| `EntranceWarning` | MOTD / notification shown when joining the server. |
| `EntranceWarningRussian` | The same notification in Russian. |
| `ColonyScreenEnable` | Takes a daily snapshot of all player colonies at midday. Viewable via **"Zoom in"**. |
| `ColonyScreenHighQuality` | Enables uncompressed colony snapshots. Improves visual fidelity but uses significantly more storage. |
| `ColonyScreenFolderMaxMb` | Snapshot directory storage quota. Older files are purged, but the latest capture of each colony is retained. |
| `ScenarioAviable` | Currently unused. |
| `StorytellerDef` | Currently unused. |
| `Difficulty` | Currently unused. |
| `ExchengePrecentCommissionConvertToCashlessCurrency` | Currently unused. |
| `ExchengeAddPrecentCostForFastCargoDelivery` | Currently unused. |
| `EquableWorldObjects` | Incomplete feature. Delete or set to `false`. |
| `ExchengeEnable` | Incomplete feature. Delete or set to `false`. |
| `EnablePVP` | **Deprecated parameter.** PvP is no longer supported. Formerly toggled PvP functionality. |
| `MinutesIntervalBetweenPVP` | **Deprecated parameter.** Formerly defined cooldown between PvP assaults in minutes. |

## Blueprints and Balance

If the server does not require custom trade blueprints designed for massive economy servers and you wish to maintain vanilla pacing, delete this folder:

```text
OnlineCity\1.1\patches

```

Perform this on:

* The local game client mod folder;
* The server mod directory (if mod sync is used).

Blueprint balances can be manually customized in:

```text
TechBlueprints.xml

```

---

# 13. Player Administration

## Verifying Player Identity

To confirm that a user owns an account:

1. Ask them to **connect to the server**.
2. Request their **client log for the current day**.
3. Locate the connection entry in their log.
4. Cross-reference the timestamp with the server logs.

> Remember to account for time zone differences.

## Restoring Saves

Player save files are stored in `World\DataPlayers`:

Format:

```text
{player_login}.dat{number}

```

Examples:

```text
player.dat1
player.dat2
player.dat3

```

`dat1` — Most recent save state.

`dat2` — Previous backup.

`dat3` — Older backup.

If the primary save is corrupted:

1. Delete:

```text
{player_login}.dat1

```

2. Rename `dat2` or `dat3` to `dat1`.

> ⚠️ In some situations, the server retains both `dat1` and a `.bak` copy to prevent rollback exploits after resource transfers.

## Deleting a Player's Colony

Administrators can purge a colony using:

```text
/killhimplease {UserLogin}

```

The target player will be prompted to settle a new colony upon their next login.

The following setting:

```text
DeleteAbandonedSettlements

```

automates this cleanup for inactive settlements.

## Resetting a Player's Password

Administrators can reset passwords using:

```text
/ChangePassword {UserLogin} {NewPassword}

```

> ⚠️ Always confirm account ownership before resetting passwords.

---

# 14. Banning Players

Create these files inside the `World` folder:

```text
blockip.txt
blockkey.txt

```

## IP Bans: `blockip.txt`

Contains IP addresses to ban, one address per line. You can specify ranges using the mod's custom slash notation: `1.2.3.0/2` will ban three consecutive IPs ending in `.0`, `.1`, and `.2` (offset range, not standard CIDR notation).

## Hardware ID Bans: `blockkey.txt`

Bans users via persistent hardware keys. Locate these in the server logs by searching for `key=`. The line will appear similar to:

`Checked loginName key=hlw57jjSV5HacQbb9iiB@@51b36et2ZBvBUkUztF+N`

Place each unique key on a separate line.

A single machine generates multiple keys. Matching **any single key** triggers a ban, but adding all associated keys ensures total coverage. Keys in the log are separated by `@@@` — do not include this delimiter in the file. A new machine connection also reports a default `111@@@` identifier.

> Comments can be added after an IP or key by inserting a space.

---

# 15. Statistics and Monitoring

## Player Dump

Press `S` in the server console. This exports a `Players_date.csv` file containing player lists, last seen timestamps, and game telemetry.

| Parameter | Description |
| --- | --- |
| `LastOnlineDay` | Real-world days elapsed since the player was last online. |
| `BaseCount` | Number of colonies owned. |
| `CaravanCount` | Number of caravans on the world map. |
| `MarketValue` | Total colony market value. |
| `MarketValuePawn` | Cumulative market value of all colonists. |
| `AttacksWonCount` | Incidents directed at this player. |
| `AttacksInitiatorCount` | Incidents this player initiated against others. |
| `ColonistsCount` | Total colonist count. |
| `ColonistsDownCount` | Count of currently downed/incapacitated colonists. |
| `ColonistsBleedCount` | Count of colonists currently suffering blood loss. |
| `PawnMaxSkill` | Colonists with level 20 in 8 out of 12 skills. Normal value is `0`. If equal to `ColonistsCount`, suspect cheating. |
| `KillsHumanlikes` | Humanlike kills by this player's faction. |
| `KillsMechanoids` | Mechanoid kills by this player's faction. |
| `KillsBestPawnHN` | Name of the colonist with the highest humanlike kill count. |
| `KillsBestPawnH` | Humanlike kills achieved by that colonist. |
| `KillsBestPawnMN` | Name of the colonist with the highest mechanoid kill count. |
| `KillsBestPawnM` | Mechanoids killed by that colonist. |
| `Grants` | Permission tier. `UsualUser` = standard user; `SuperAdmin.Moderator` = world creator (first registered user). |
| `IntruderKeys` | Hardware IDs used to track bans and multi-account logins from the same PC. |
| `StartMarketValue` | Colony wealth at generation. |
| `StartMarketValuePawn` | Colonist market value at generation. |
| `MarketValueBy15Day` | Peak colony wealth recorded within the first 15 days. |
| `MarketValuePawnBy15Day` | Peak colonist market value within the first 15 days. |
| `MarketValueByHour` | Highest colony wealth achieved in a single real-world hour (excluding pause time). |
| `MarketValuePawnByHour` | Highest colonist value achieved in a single real-world hour (excluding pause time). |
| `TicksByHour` | Peak tick volume within one real-world hour online, including pause time. |
| `HourInGame` | Total playtime for the current colony, excluding pause time. |

---

# 16. Server Logs

The server automatically generates:

* `Log_date.txt` — General server log
* `Incidents_month.csv` — Incident ledger

| Event | Description |
| --- | --- |
| `NewIncident` | Player `fromLogin` scheduled an incident targeting `toLogin`. |
| `SendMail` | Pre-raid countdowns (half an in-game day) and queues resolved; incident has spawned. |
| `DayAfterMail` | 24 hours elapsed since incident started. Post-raid cooldowns calculate here. Useful for evaluating raid impact. |
| `End` | All delays resolved; the incident terminates and frees its queue slot. |

| Field | Description |
| --- | --- |
| `fromDay` | Current in-game day of the initiating player. |
| `toDay` | Current in-game day of the target player. |
| `worth` | Combined net worth of items and pawns account-wide. |
| `worthTarget` | Total wealth inside the targeted colony specifically. |
| `delayAfterMail` | Calculated extra cooldown after the incident. One day has already passed at calculation time. |

---

# 17. Commands

Commands can be typed into any chat window and are hidden from chat logs. All commands begin with a `/`.

## User Permissions

| Command | Description |
| --- | --- |
| `/grants add {UserLogin} Moderator` | Grant **Moderator** rights to a user. |
| `/grants revoke {UserLogin} Moderator` | Revoke **Moderator** rights from a user. |
| `/grants type {UserLogin}` | View current user permissions. |

## Colony Management

| Command | Description |
| --- | --- |
| `/killmyallplease` | Purges all server-side saves, colonies, and caravans for the user. Registration remains valid. Prompts a fresh colony setup on the next login. Identical to **"About mod" → "Start anew"**. |
| `/killhimplease {UserLogin}` | **Administrator only.** Executes `/killmyallplease` on the target user. |

## Safe Server Shutdown

`/everybodylogoff` — **Administrator only.** Safely shuts down the server: broadcasts an order to all online clients to save and disconnect. Monitor the server console and close it only after all clients disconnect and saves finish writing. **Do not use during active PvP sessions.**

## Chat and Messaging

| Command | Description |
| --- | --- |
| `/createchat {Name}` | Creates a chat channel. Can also be done via the UI. |
| `/addplayer {UserLogin}` | Invites a player to the current channel. |
| `/renamechat {Name}` | Renames the current channel. |
| `/exitchat` | Leaves the active channel. |
| `/discord` | Discord bot integration. Reference: [https://github.com/Todako/OnlineCity/tree/master/Source/DiscordChatBotServer](https://github.com/Todako/OnlineCity/tree/master/Source/DiscordChatBotServer) |

Administrators can dispatch notifications as in-game letters:

```text
/say {player_name} {color} {title} {text}

```

Target substitutions for `player_name`:

* `all` — Every registered player;
* `online` — Only players currently online.

`color` is an optional prefix argument starting with `/`:

| Value | Letter Style |
| --- | --- |
| `/treatbig` | Red letter with audio alarm. |
| `/treatsmall` | Red letter. |
| `/death` | Gray letter with audio cue. |
| `/negative` | Yellow letter. |
| `/positive` | Blue letter with audio cue. |
| `/visitor` | Blue letter. |
| `/neutral` | Standard gray letter (default). |

## Triggering Incidents

Administrators can force events using:

`/call {event} {"player_name"} {target_colony_sId} {power}* {arrival_mode}* {faction}*`

Arguments marked with `*` are **optional** and apply only to specific event types.

Enclose the player nickname in **double quotes**:

```text
/call raid "babur"

```

### Available Events

| Code | Description |
| --- | --- |
| `raid` | Spawns a raid on the player's colony. Power: integer from `1` to `10`. Supports arrival mode and faction overrides. |
| `inf` | Triggers an infestation (insects). Supports power scaling. |
| `acid` | Triggers toxic fallout on the player's map. |

### Arrival Modes

| Code | Description |
| --- | --- |
| `walk` | Walk in from a random map edge. |
| `air` | Drop pods targeting the home area. |
| `random` | Drop pods scattered randomly across the entire map. |

### Factions

| Code | Description |
| --- | --- |
| `tribe` | Hostile tribal faction — swarm attacks with primitive weaponry. |
| `pirate` | Industrial pirate faction — firearms, rocket launchers, and advanced melee. |
| `mech` | Mechanoid faction — heavily armored combat units. |

### Examples

```text
/call raid "babur" 143 8 random pirate
/call inf "SSDExecutor" 23 4
/call acid "Aant" 16

```

For `raid`, omitting power, arrival mode, and faction falls back to:

```text
/call raid {"player_name"} 1 walk pirate

```

### Vanilla Game Incidents

Trigger standard RimWorld incidents using their internal `defName`:

```text
/call def online 0 0 {defName}

```

The `0 0` values are required internal compatibility flags. Example:

```text
/call def online 0 0 ThrumboPasses

```

Spawns a herd of Thrumbos for all online players.

## States (Nations)

| Command | Description |
| --- | --- |
| `/statecreate {Name}` | Founds a new state with the specified name; you become its ruler. |
| `/stateadd {UserLogin}` | Sends an invitation to join your state. |
| `/stateexclude {UserLogin}` | Expels a player from your state. |
| `/stateposition {PositionName} {1/0 RightAddPlayer} {1/0 RightExcludePlayer} {1/0 RightEditRights}` | Creates or edits a role. Assign `1` or `0` to toggle three permissions: **invite players**, **kick players**, and **manage/assign roles**. |
| `/stateposition {PositionName} del` | Deletes the specified role. |
| `/stateset {UserLogin} {PositionName/del}` | Assigns a role to a player, or strips their role if `del` is specified. |

# Links

GitHub: [https://github.com/Todako/OnlineCity/tree/master](https://github.com/Todako/OnlineCity/tree/master)
