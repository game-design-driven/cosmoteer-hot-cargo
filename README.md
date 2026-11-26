# Hot Cargo

A career mod for [Cosmoteer](https://store.steampowered.com/app/799600/Cosmoteer_Starship_Architect__Commander/) that turns the open-ended career into a short, tense run, like FTL.

You stole corporate tech, and the corporation wants it back. Haul it across about 8 systems to the Buyer at the edge of the map. Every minute you stay, the heat rises and the hunters get closer. Sell a unit to buy a better ship, or hold it all for a bigger score. Talk your way past a hunter, hire a mercenary, or hide your tech on a friendly freighter.

Vanilla Cosmoteer lets you build forever. Hot Cargo gives you a goal, a clock and an enemy that learns where you are. A run takes 2 to 3 hours, alone or in co-op.

<p>
<img src="docs/screenshots/title.jpg" width="49%" alt="Main menu with the Hot Cargo title">
<img src="docs/screenshots/intro.jpg" width="49%" alt="The run intro">
<img src="docs/screenshots/heat-alert.jpg" width="49%" alt="A heat stage starts: the hunters are coming">
<img src="docs/screenshots/event.jpg" width="49%" alt="A choice event">
</p>

## How a run plays

- **Hold or sell.** You start with 8 Stolen Tech in Tech Vaults. The more you hold, the harder the corporation hunts you. Stations buy tech, and the price rises during the run. The Buyer pays 4×, and every unit you still hold counts in the score.
- **Move on or burn.** Heat rises with time and with every mission. Each heat stage sends hunters. A jump to a new system cools you down.
- **Protect the cargo.** Tech lives in real parts of your ship. A destroyed vault spills crates that hunters grab. Lose all your tech and the run ends.
- **Deliver.** The Buyer waits in the last sector. Sell it your tech to win.

<p>
<img src="docs/screenshots/contract.jpg" width="32%" alt="Contract terms on a mission offer">
<img src="docs/screenshots/arrival.jpg" width="32%" alt="Arrival in a new sector type">
<img src="docs/screenshots/map.jpg" width="32%" alt="Galaxy map tooltip">
<img src="docs/screenshots/trade.jpg" width="32%" alt="Selling tech on the trade tab">
<img src="docs/screenshots/win.jpg" width="32%" alt="Tech delivered: the win panel">
<img src="docs/screenshots/loss.jpg" width="32%" alt="The corporation recovered the tech">
</p>

## Features

Click a feature to open it.

### The heat and the hot cargo

<details>
<summary><b>Heat and hunter waves</b>: stay too long and the corporation sends ships</summary>

Heat rises every minute and with every mission you finish. At each heat stage the corporation reacts: first scouts, then a hunter pack, then a strike force that arrives in waves until you leave. A red strip in the heat panel and an alarm tell you when a stage starts. Holding more tech makes the heat rise faster.

<img src="docs/screenshots/heat-alert.jpg" width="70%" alt="A heat stage alert">

Spec: [Heat](spec/09-heat.md), [Corporate response](spec/10-corporate-response.md), [Strike waves](spec/23-strike-waves-and-enemy-ai.md)
</details>

<details>
<summary><b>Tech Vaults</b>: your cargo is a part of your ship, and it can be shot off</summary>

Tech lives in Tech Vaults, small armored parts that you place, protect and spread out. Hits do not cost tech, but a destroyed vault spills its tech as crates, and some of it burns. Hunters go for your vaults and grab loose crates. A gold outline shows where your vaults are, and map blips show where tech is in the system. If you hold no tech for 3 minutes, the run is lost.

<img src="docs/screenshots/vault-crates.jpg" width="70%" alt="Tech crates from a destroyed vault">

Spec: [Tech units and the Tech Vault](spec/07-tech-units-and-the-tech-vault.md), [One-tile Tech Vault](spec/39-one-tile-tech-vault.md)
</details>

<details>
<summary><b>Sell now or deliver later</b>: one unit buys a ship, but every sale lowers your score</summary>

Any trade station buys tech, and the price rises with time, danger and your notoriety. One or two units pay for a ship that keeps up with the hunters. The Buyer pays 4×, and unsold units count the same in the score. The trade tab shows what a sale does to your heat and your score before you sell. You can buy back tech that you sold, at a markup.

<img src="docs/screenshots/trade.jpg" width="70%" alt="The trade tab shows the effect of a sale">

Spec: [Tech value and selling](spec/08-tech-value-and-selling.md), [Win and loss](spec/06-win-and-loss.md)
</details>

<details>
<summary><b>Jumps cool you down</b>: reach a quiet relay first</summary>

Every jump to another system leaves most of the heat behind. A relay with no hunter near it cuts more heat than a watched one. Ships that cannot make the jump get a warning before you leave, so nobody is stranded by surprise. Tech that stays behind is lost, and the jump dialog says how much before you go.

<img src="docs/screenshots/jump-warnings.jpg" width="60%" alt="The jump window names the ships that stay behind">
<img src="docs/screenshots/jump-carrier.jpg" width="38%" alt="The jump dialog warns that all your tech stays behind and is lost">

Spec: [Factions, jumps and station prices](spec/38-factions-jumps-and-station-prices.md), [Jump warnings](spec/58-jump-warnings.md)
</details>

<details>
<summary><b>Tech outside a vault decays</b>: stash it anywhere, but not for long</summary>

Normal storage takes tech too, but there it breaks down in 6 minutes. The part card and the map blips mark decaying tech. Build more vaults, or sell before it is gone.

<img src="docs/screenshots/storage-decay-card.jpg" width="70%" alt="Tech in normal storage decays">

Spec: [Tech in storage and decay](spec/60-tech-in-storage-and-decay.md)
</details>

<details>
<summary><b>Bring your own ship</b>: any design gets its vaults fitted at the start</summary>

The starter ships come with vaults built in. Pick another ship from your library and the new career screen offers to fit the vaults for you, inside the hull and where crew can reach them.

<img src="docs/screenshots/fitvaults-offer.jpg" width="70%" alt="The Fit Tech Vaults offer">

Spec: [One-tile Tech Vault](spec/39-one-tile-tech-vault.md)
</details>

<details>
<summary><b>Difficulty, Ironman and Strike music</b>: the vanilla settings scale the chase</summary>

The vanilla difficulty settings also set how fast heat rises, how many hunters come and how much tech pays. In an Ironman career, losing the tech deletes the save. When the last heat stage starts, the battle music kicks in and stays until you get away.

<img src="docs/screenshots/runmodes-paneltip.jpg" width="70%" alt="The heat panel names the difficulty">

Spec: [Run modes](spec/34-run-modes.md)
</details>

### Co-op

<details>
<summary><b>Decide together</b>: see what your crewmates hover, and vote on choices</summary>

In co-op, event and story pages decide for everyone. Each player sees which option the others point at, framed in their color. Switch the page to vote mode and the most votes win, with a timer so nobody stalls.

<img src="docs/screenshots/coop-vote.jpg" width="70%" alt="Votes and hovers on a shared choice">

Spec: [Shared choices in co-op](spec/32-shared-choices-in-co-op.md)
</details>

<details>
<summary><b>Listen in</b>: hear your friend bargain with a hunter, and vote on the answer</summary>

When one player hails a ship, the others get a "Listen in" button. They see the same page live and can vote on it, so a bluff or a bribe is a crew decision.

<img src="docs/screenshots/coop-listen.jpg" width="70%" alt="A player listens in on a hail">

Spec: [Co-op listen-in](spec/47-co-op-listen-in.md)
</details>

<details>
<summary><b>Share resources</b>: one switch pools the whole crew's steel and parts</summary>

In co-op, a "Share resources" switch sits next to the ship tool buttons. Any player can flip it. While it is on, everyone's ships share construction resources and help each other build, like one player's fleet. A chat line says who turned it on or off.

<img src="docs/screenshots/coop-share.jpg" width="70%" alt="The Share resources switch, on, next to the ship tool buttons, and the chat line after a player turns it on">

Spec: [AI upkeep, requirements 1318 to 1320](spec/53-ai-upkeep.md)
</details>

<details>
<summary><b>Ping</b>: Alt + click to mark a spot for everyone</summary>

Alt + left click in space or on the minimap places a marker in your color that every player sees, also at the screen edge. It fades after 30 seconds.

<img src="docs/screenshots/coop-ping.jpg" width="70%" alt="Pings from two players">

Spec: [Co-op ping](spec/57-co-op-ping.md)
</details>

<details>
<summary><b>Enemies scale with the crew</b>: more players, more hunters</summary>

Vanilla never scales enemies with the player count. Hot Cargo does: a slider on the new career screen sets how many enemy ships each extra player adds, for hunters, pirates, missions and ambushes alike.

<img src="docs/screenshots/scaling-newgame.jpg" width="70%" alt="The enemy scaling slider">

Spec: [Co-op enemy scaling](spec/28-co-op-enemy-scaling.md)
</details>

### Hunters and the Pursuit

<details>
<summary><b>Enemies with a personality</b>: they kite, flank, bait and go for your guns</summary>

Every hostile ship fights with a personality that fits its faction and its build. Some keep out of your range, some split up and hit you from two sides, some send a fast ship to lure you away, and some aim at your weapons and engines. Damaged ships pull back to repair. You read it all from how they move.

Spec: [Hunter AI tactics](spec/12-hunter-ai-tactics.md), [AI personalities](spec/31-ai-personalities.md)
</details>

<details>
<summary><b>Calls for help and runaways</b>: kill the caller before the ring closes</summary>

An enemy can call for help: a ring and a "CALLING HELP" label count down, and when it ends, more ships come. A ship with a sensor array hears the call early and makes it slower. Badly hurt enemies flee, and a fleeing hunter jumps out of the system unless you finish it.

Spec: [Strike waves and enemy AI](spec/23-strike-waves-and-enemy-ai.md), [AI upkeep](spec/53-ai-upkeep.md)
</details>

<details>
<summary><b>The Pursuit</b>: scouts report you, hunters blockade the relay, and a hacker sells you silence</summary>

A director paces the chase. Hunters start weaker and grow toward the Buyer, and a stronger fleet draws a stronger response. A scout can race to a relay to report your position. A hunter pack can park at the relay you need. A network outage can stop all heat for a while, and a hacker offers to cause one for a price.

<img src="docs/screenshots/pursuit-blockade.jpg" width="49%" alt="Hunters blockade a relay">
<img src="docs/screenshots/pursuit-hacker.jpg" width="49%" alt="A hacker offers an outage">

Spec: [The Pursuit](spec/51-pursuit.md)
</details>

<details>
<summary><b>Wanted</b>: the police join in, with a way out</summary>

When a faction wants you and the heat is up, a warning names the faction and your ways out: pay the bounty, leave, or wait until the search ends. A wanted event can offer another deal.

Spec: [Events, Wanted and hunted](spec/22-events.md)
</details>

### Factions and diplomacy

<details>
<summary><b>Talk your way out</b>: pay off a hunter, bluff, or tell them to come and take it</summary>

A hunter may hail you before it fights. Pay it off in tech or money, or hand over a few units and claim that is all you have. A bluff works better when your tech is spread over many vaults. You can also demand an enemy's surrender, or offer your own to a ship that is not a hunter. Enemies weigh the odds before they demand, flee or give up.

<img src="docs/screenshots/diplomacy-demand.jpg" width="70%" alt="A hunter demands the tech">

Spec: [Diplomacy](spec/37-diplomacy.md)
</details>

<details>
<summary><b>Hide your tech with friends</b>: a friendly freighter holds it, unless it betrays you</summary>

Ask a friendly ship to hold some of your tech, so the hunters chase an empty ship. Hire an armed ship to fight for you, or take a mercenary's contract by the minute. Trust has a price: the more heat and hunters around, the likelier a friend betrays you and keeps the tech.

<img src="docs/screenshots/diplomacy-held.jpg" width="49%" alt="A friendly ship holds tech">
<img src="docs/screenshots/diplomacy-merc.jpg" width="49%" alt="A mercenary offers a contract">

Spec: [Diplomacy](spec/37-diplomacy.md)
</details>

<details>
<summary><b>Factions take sides</b>: hot zones pay more, friends give shelter, rivals buy tech</summary>

One faction works with the corporation. Its space is a hot zone: heat rises faster, nobody there helps you, but missions pay more. A faction that likes you gives shelter, and heat rises slower in its space. Rival stations pay more for tech. After a strike, the corporation watches the systems around you. The galaxy map tags each system, so the route is a real choice.

<img src="docs/screenshots/factions-map.jpg" width="49%" alt="Hot zone and shelter tags on the galaxy map">
<img src="docs/screenshots/factions-refusal.jpg" width="49%" alt="A station refuses to trade at high heat">

Spec: [Factions, jumps and station prices](spec/38-factions-jumps-and-station-prices.md)
</details>

<details>
<summary><b>Relay lockdowns</b>: a closed relay, a toll, or pirates at the gate</summary>

Now and then a system next to you locks its relays. The galaxy map shows it before you jump: closed, a toll, or pirates who want a bribe. Pay or fight your way out; a closed relay opens after a while.

<img src="docs/screenshots/relays-bribe.jpg" width="70%" alt="Pirates hold a relay and ask for a bribe">

Spec: [Relay lockdowns and the timed stash](spec/43-relays-and-stash.md)
</details>

### Ship AI and autonomous ships

<details>
<summary><b>Program your ships</b>: "if this, then that" rows for each ship, like Dragon Age tactics</summary>

Give each ship up to 16 rows of conditions and actions: "health below 30 % → retreat to an ally", "enemy attacks a vault ship → target it". Pick a preset (Aggressive, Defensive, Sniper, Escort, Vault Guard, Formation) and change it, copy and paste rows between ships, and watch the live row light up in a fight. The rows save with the ship design.

<img src="docs/screenshots/shipai-editor-live.jpg" width="70%" alt="The ship tactics editor">

Spec: [Player ship tactics](spec/30-player-ship-tactics.md)
</details>

<details>
<summary><b>Autonomous ships</b>: build a drone miner, looter or supply runner that works on its own</summary>

A ship with a Computer part and no control room runs only on its rows. Build a miner that sells its ore, a looter that sweeps up crates with a Funnel, a salvager, a supply runner or an escort. You cannot give it orders, so set it up well. New parts: Computer, Funnel and Targeted Reactor.

<img src="docs/screenshots/autonomy-looter.jpg" width="49%" alt="An autonomous looter at work">
<img src="docs/screenshots/autonomy-parts.jpg" width="49%" alt="The new autonomy parts">

Spec: [Autonomous ships](spec/45-autonomous-ships.md)
</details>

<details>
<summary><b>Enemies keep fighting</b>: damaged ships patch up from their allies</summary>

Enemy ships of one faction share their repair stock. A damaged ship flies to a friendly ship or station, or salvages a wreck, and repairs, so a long fight stays a fight.

Spec: [AI upkeep](spec/53-ai-upkeep.md)
</details>

### Events and the world

<details>
<summary><b>FTL-style events</b>: distress beacons, checkpoints, fences and rival thieves</summary>

Choice events pop up on arrival, at stations and from ships you meet. Each choice shows its cost; the outcome can bring money, tech, crew, heat or an ambush. Some events come to you as a ship that hails you, with the page in its comms. The first system almost always has one.

<img src="docs/screenshots/event.jpg" width="70%" alt="A choice event">

Spec: [Events](spec/22-events.md), [Research, abandoned ships and ship comms](spec/40-events-research-abandoned-ships-and-ship.md)
</details>

<details>
<summary><b>Story chains</b>: each station has a story in parts, and choices come back later</summary>

A station can offer the first part of a story. What you do decides the next part, and some parts find you systems later. No chain repeats until you have seen the others.

Spec: [Chains](spec/25-chains.md)
</details>

<details>
<summary><b>Friendly encounters</b>: recruits, apprentices, traders, riddles and the Wayfarers</summary>

Not every ship wants a fight. A small ship asks to join you. An apprentice follows you to learn, and joins your fleet if it sees enough fights. A wandering trader swaps resources. A painted ship asks a riddle. And when you are badly outnumbered, the Wayfarers may jump in to help, and then ask you to pay it forward.

<img src="docs/screenshots/friendly-wayfarers.jpg" width="70%" alt="The Wayfarers arrive to help">

Spec: [Friendly encounters](spec/54-friendly-encounters.md)
</details>

<details>
<summary><b>Gambles in the world</b>: open the sealed case, listen to a patrol, loot a hulk</summary>

Risky actions happen on your ship, not in a menu. Open a courier's sealed case from the storage part: it may hold access codes or a tracer that adds heat until you space it. Point a sensor array at a patrol to overhear something useful. Old hulks hold loot, and one dead ship each run still carries a unit of tech.

<img src="docs/screenshots/sealed-case.jpg" width="49%" alt="The sealed case opened in the hold">
<img src="docs/screenshots/hulk.jpg" width="49%" alt="A hulk with loot">

Spec: [World actions](spec/46-world-actions.md), [Mission items and hand-overs](spec/52-mission-items-and-hand-overs.md)
</details>

<details>
<summary><b>Sector types</b>: trade hubs with currents, mining belts, solar zones and ion storms</summary>

Systems have an identity and a hazard. A trade hub has a current that pushes ships and crates. A mining belt is a dense ring of rock. A solar zone burns ships that fly too deep. Deep space hides a pirate lair in an ion storm. The arrival text and the map tooltip name the hazard.

<img src="docs/screenshots/arrival.jpg" width="70%" alt="Arrival in a trade hub">

Spec: [System archetypes](spec/27-system-archetypes.md)
</details>

<details>
<summary><b>Scavengers</b>: they pick the system clean, but you get the first pick</summary>

Scavenger ships salvage old wrecks and loose resources and sell what they find. They never take anything fresh, and they stay friendly unless you shoot them. Factions and pirates scavenge too.

<img src="docs/screenshots/scavengers.jpg" width="70%" alt="A scavenger salvages a wreck">

Spec: [Scavengers](spec/56-scavengers.md)
</details>

<details>
<summary><b>The rumoured stash</b>: a timed treasure hunt in open space</summary>

Some systems have a rumour of a stash in open space. Get there before the corporation does: the longer you wait, the likelier it is empty or a trap.

<img src="docs/screenshots/stash-log.jpg" width="70%" alt="The rumoured stash">

Spec: [Relay lockdowns and the timed stash](spec/43-relays-and-stash.md)
</details>

<details>
<summary><b>Debris burns up</b>: old junk glows, then goes, so long fights stay smooth</summary>

Small loose junk with no value glows orange after a few minutes and then burns up. Wrecks worth salvage, anything with cargo or crew, and stations stay.

<img src="docs/screenshots/debris-glow.jpg" width="45%" alt="A loose junk piece glows orange before it burns up">

Spec: [Debris decay](spec/50-debris-decay.md)
</details>

### Missions, money and debt

<details>
<summary><b>New missions</b>: convoys, joint strikes, sieges, sundives and bounties that may be traps</summary>

Escort a freighter, attack a base with an allied fleet, hold a listening post against drones, tow a hulk home, survey a storm, retrieve a prototype or repossess a ship. A shady bounty pays double, and the hotter you are, the likelier it is a trap.

Spec: [Missions](spec/11-missions.md), [Mechanics missions](spec/24-mechanics-missions.md)
</details>

<details>
<summary><b>Contracts with strings</b>: fees, rivals and clients who sell you out</summary>

Some clients charge a fee up front. Some offers are rivals, and taking one closes the others. Some clients sell you out to the corporation. Every open contract leaves a trail that adds heat. The offer shows its terms before you accept.

<img src="docs/screenshots/contract.jpg" width="70%" alt="Contract terms on an offer">

Spec: [Contract economy](spec/26-contract-economy.md)
</details>

<details>
<summary><b>Debt</b>: borrow now, and the collectors come later</summary>

A faction station can lend you money, and spending below zero borrows from the local faction, up to a credit limit. In co-op, each player has their own debts. The money tooltip shows what you owe and how much credit is left. Interest grows, and collectors jump in to ask for payment, with a harder tone each time. Let it run away and the lender turns hostile. The lender's debt page lets you refinance, and in a hard fight a faction can send help that you pay for afterwards.

<img src="docs/screenshots/debt.jpg" width="49%" alt="A debt collector hails the players">
<img src="docs/screenshots/debt-money-tip.jpg" width="49%" alt="The money tooltip shows debts and credit left">

Spec: [Debt](spec/55-debt.md)
</details>

<details>
<summary><b>Faster mission choice</b>: kind icons, a filter and a risk sort</summary>

Mission lists show each kind's icon, the jumps to other systems and the heat and risk of each offer. Filter by Hot Cargo, story, contracts or vanilla missions, and sort by risk.

<img src="docs/screenshots/missions.jpg" width="70%" alt="The mission list with icons, risk and the filter">

Spec: [UI: mission select, icons and event options](spec/41-ui.md)
</details>

### Quality of life

<details>
<summary><b>Learn as you go</b>: How to Play pages and a log of your run</summary>

Hot Cargo pages join the game's How to Play list and codex and show when you first meet each mechanic. The career log gets a "Hot Cargo run" tab with the story of your run.

<img src="docs/screenshots/onboarding-howtoplay.jpg" width="32%" alt="How to Play list">
<img src="docs/screenshots/onboarding-page.jpg" width="32%" alt="A Hot Cargo How to Play page">
<img src="docs/screenshots/onboarding-careerlog.jpg" width="32%" alt="The Hot Cargo run tab in the career log">

Spec: [How to Play and the run log](spec/33-how-to-play-and-the-run-log.md)
</details>

<details>
<summary><b>Past runs</b>: every run gets a score on your record</summary>

The main menu has a Past Runs table with every run on this machine, sorted by score, with its result, length and peak heat.

<img src="docs/screenshots/runrecord-pastruns.jpg" width="70%" alt="The Past Runs table">

Spec: [Run record](spec/49-run-record.md)
</details>

<details>
<summary><b>A clearer HUD</b>: hull %, loss alerts, under-fire pings and richer tooltips</summary>

Hull integrity on every ship, a banner when you lose a ship, warnings for low ammo and battery, and a ping when an off-screen ship takes fire. The galaxy map shows jumps and what waits in each system. The fame tooltip shows your crew limit, stations show their services in the comms list, and number keys pick dialogue options. A comms window pauses the game. You can switch off each change.

<img src="docs/screenshots/map.jpg" width="70%" alt="The galaxy map tooltip">

Spec: [UI](spec/41-ui.md), [Payment hint](spec/59-payment-hint.md), [UX.md](UX.md)
</details>

<details>
<summary><b>Tune it live</b>: change any number while you play</summary>

A settings window in the escape menu holds every Hot Cargo number, with a description and a tag that says when a change takes effect. In co-op the host's settings apply to everyone.

<img src="docs/screenshots/settings.jpg" width="70%" alt="The Hot Cargo settings window">

Spec: [Settings menu](spec/29-settings-menu.md)
</details>

## Install

Windows, Cosmoteer from Steam, game version 0.30.5a.

1. Download the latest zip from [Releases](https://github.com/game-design-driven/cosmoteer-hot-cargo/releases).
2. Close Cosmoteer, unzip, and double-click `install.bat`.
3. In Cosmoteer, open **Mods**, turn on **Hot Cargo**, and restart the game when it asks.
4. Start a new career. The map size "Hot Cargo run" is the default.

In co-op, every player needs the same version. Run `install.bat` again after a Cosmoteer update. `uninstall.bat` restores the game and keeps your files (tunables, telemetry, screenshots). `INSTALL.md` in the zip has the details.

Releases marked "dev" are built for every change and are not game tested. For a tested build, use the latest release without "dev".

## Tuning

Change any number in game: open the escape menu and click **Hot Cargo settings**. Changes apply at once and are saved to `Mods\hot_cargo\tunables.rules`, which you can also edit by hand while the game runs. `tunables.defaults.rules` lists every value with its default. [TUNABLES.md](TUNABLES.md) says when each change takes effect.

In co-op, the host's values apply to every player, and the new career screen has an **Enemy ships per extra player** slider.

## Develop

- [SPEC.md](SPEC.md) and [spec/](spec/): the design and the numbered requirements, one file per section.
- [CONTRIBUTING.md](CONTRIBUTING.md): the layout, the build, the tests (a headless game on macOS and Linux, test slots on a Windows PC) and releases.
- [docs/WORKFLOW.md](docs/WORKFLOW.md): how the project works, from agents and pull requests to tests and releases.
- [PLAYTEST.md](PLAYTEST.md): manual checks.
- [EVENTS.md](EVENTS.md), [MISSIONS.md](MISSIONS.md), [CHAINS.md](CHAINS.md), [WORLDS.md](WORLDS.md): the content catalogs.

## License

[MIT](LICENSE)
