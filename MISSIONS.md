# Hot Cargo missions

`spec/24-mechanics-missions.md` holds the numbered requirements. This file describes how each mission plays. Numbers are defaults. The `Mechanics` section of `tunables.rules` holds the live numbers. Rewards are in `mod/missions/mechanics/sysgen_mechanics.rules`.

## Escorts

Spec 64 (requirements 1850 to 1855). Every escort (Convoy Duty, the chain escort stages and the Mining Escort) waits for the players before it leaves. After the accept, the escorted ship flies to your nearest ship and stops close (`Missions.EscortMeetGap`, 300 from your hull). It goes at most `EscortMeetRange` (3,000) from where it started; past that, fly to it. The map ring and the objective line show where it is. When your hull is within `EscortHailGap` (800), it hails you: "Ready to leave?". Click **Ready. Lead the way.** in its comms, and it starts its route from there. **Not yet** closes the window. It never leaves by itself: it hails again every 60 s while you stay close, and you can always call it. In co-op, "Ready" is a shared choice, so every player sees it and can vote.

## Mechanics missions

These missions use the game's own systems: building, tractor beams, mining, crew, surrender, sensors and careful targeting. Each one shows up in half of the systems (`Mechanics.<Name>Chance`, 50 %; BALANCE.md, finding 10). The Mining Escort also needs an asteroid field without pirates or platforms. Rewards use the vanilla tier tables, so the pay rises with the sector tier. The table shows each reward as a multiple of a standard vanilla mission.

| Mission | Offered at | Money | Fame | Contract terms |
|---|---|---|---|---|
| Drone Siege | Stations | 2× | 3× (no kill fame) | free |
| Prototype Retrieval | Bounties | 2× | 2× | fee 0.3, heat +5, informant 10 % |
| Sunside Ore | Deliveries | 1.25× plus ore sales | 1× | free, rival `ore_claims`, informant 5 % |
| Salvage Rights | Stations | 1.25× | 1× | free, rival `salvage_rights` |
| Repossession | Bounties | 2× | 2× | fee 0.4, heat +4, rival `salvage_rights`, informant 10 % |
| Storm Survey | Stations | 1.25× | 1× | free, trail weight 0.5 |
| Claim Jumpers | Stations | 2× | 3× (no kill fame) | free, rival `ore_claims`, informant 5 % |
| Listening Post | Faction wars | 2×, more with intensive scans | 3× (no kill fame) | heat +3, informant 10 % |
| Mining Escort | Stations | 2× | 3× (no kill fame) | free, trail weight 0.5, rival `ore_claims`, informant 5 % |

The contract terms are `HotCargoContract` entries in each mission's rewards (the economy area reads them). The fee is in units of a standard mission's pay at the mission's tier.

### Drone Siege
Drones attack a faction listening post in open space. Hold the post for five minutes. The clock runs only while one of your ships is within 1,500 of the post. Any crewed ship counts, and so does a drone with a Computer. The offer text says so. So the clever answer is a cheap gun platform: use **Build New Ship** next to the post, add a reactor, guns and a crew, and fly on. Waves come every 35 s while the post is guarded. They are tier-1 to tier-3 combat ships, the smallest built-in ships. If the post dies, the mission fails. A 25-minute cap stops the offer from lasting forever.

### Prototype Retrieval
A pirate ship carries a Prototype Core on its outer hull. The core is a 2×2 part with 8,000 health. Fight with care and target the reactors, bridge or crew instead. There are two ways to win. Wreck the ship, then bring a ship within 400 of the core, and the crew lift it into your hold (it needs a free storage slot). Or beat the ship until it offers surrender, and accept: it hands the core over at once. Then hand the core to the broker in the station's comms. You can ask for more money, and the broker sells your hull number to the corporation (heat). If the core part is destroyed, or the core is lost from your hold, the mission fails. When the ship breaks apart, the mission follows the core to its new piece.

### Sunside Ore
A seam of Helion Ore asteroids sits 2,000 to 3,300 from a single sun, inside the sun's heat zone. Mine the ore with mining lasers. Then sell 30 to 45 units to the station within 30 minutes (vanilla DeliverResources). Each unit needs a storage slot (5 to a tile), so bring storage room. The heat damages parts, so keep moving. Binary suns get no seam. Hook for other system generators: a doodad tagged `sun` can host the seam, and the spawn flag `hc_no_sun_mining` blocks it.

### Salvage Rights
A crewless freighter drifts 3,000 to 4,500 from a station. Bring its hull within 800 of the station's hull within 25 minutes. Tractor beams work best. Your crew can also claim the wreck and fly it, if its engines still work. If the hulk is destroyed or salvaged to nothing, the mission fails.

### Repossession
A stolen freighter flies with one or two escorts. Take it intact. Beat it until it offers surrender and accept, or kill its crew and move your own crew aboard to claim it. Then bring it alongside the station and hail the bank: they take it once your crew are off. A surrendered freighter stays in the system and its crew fly it to the station. If the freighter is destroyed, the mission fails. A wreck that nobody claims yet does not fail the mission.

### Storm Survey
Three markers sit inside clouds or storms (gas clouds, electron clouds or ion storms). Hold a ship with a working sensor array within 400 of each marker for 15 s. Ion storms and electron clouds are the danger. A system without any of them offers no survey.

### Claim Jumpers
A civilian mining barge sits in an asteroid field. Raiders one tier below the sector come in small waves. Keep the barge alive for four minutes of guarded time. It works like the drone siege, with fewer and stronger enemies, but the raiders go for the barge, like convoy raiders go for the freighter. They turn on your ship only when it is right in their path. So kill them before they wreck it. A drone or a gun platform next to the barge keeps the clock running while you fly elsewhere, but a one-gun platform alone cannot stop the raiders; one small ship that fights can. The other guard job is the Mining Escort.

### Listening Post
A faction listening post hides in a gas cloud, 4,000 to 5,000 from a hostile base. Its scout follows you. Take it anywhere within 3,500 of the base. Then click **Start the scan here** in the mission panel. The base sends four waves, one every 45 s, from its side. Before each wave you can click **Intensive scan**, at most twice. Each click gives the next wave one more ship and one more tier, and gives the pay +25 %. If the scout dies, the mission fails.

### Mining Escort
Spec 64 (requirements 1860 to 1870). A civilian miner, the Ore Tender (two mining lasers, a collector beam, two storage bays, 12 crew, in the paint of the station's faction), waits at the station. It meets you and asks "Ready to leave?" like every escort. Then it flies to the nearest asteroid field without pirates or platforms and mines the field's most common ore with its lasers. Its hold sets the goal, at most 35 units: about 2.5 to 3 minutes of mining alone. You can help: its collector beam and crew take in loose ore of that type within 1,500 of its hull, also on its way to the field, so drop your own ore next to it. Raiders come in two waves, at a quarter and at about two thirds of the goal. With a full hold it flies back to the station. When it docks, the pay waits at the station. If the miner dies, the mission fails. It sits next to Claim Jumpers, which stays.

### Leave early (Drone Siege, Claim Jumpers, Listening Post)
After the first wave interval, the mission panel shows **Leave now and keep X credits**. One click completes the mission at once. The pay is one share for each whole wave interval you survived, times the intensive-scan factor. For example, 2 of 4 waves pay half. Fame scales the same way, but without the intensive-scan factor. You collect the reward at the station as usual.

### Orders from the mission panel
The clickable lines start with **[Click]**. A click sends a multiplayer input (`HotCargoHoldOutOrder`), so all co-op players see the same result. The game checks each order again when it runs. For example, the scan does not start when the scout is out of range.

## Debt missions

Spec 55 (requirements 1420 to 1422). They exist only while the players owe a faction (section 55).

| Mission | Offered at | Money | Fame | Contract terms |
|---|---|---|---|---|
| Collect a Debt | A trade station of the lender, once in each system | 1.5×, always off the debt | 1× | free, heat +2 |

Any mission of the lender's faction pays off the debt instead of money with chance `Debt.MissionPayoffChance` (35 %). Its money line shows it.
