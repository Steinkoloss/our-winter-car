# Full sync scope and experimental-alpha readiness

**Initial inventory audited 2026-09-13 at mod 0.1.33 / protocol233; implementation checkpoints updated through protocol 257.**

The multiplayer foundation and several useful co-op loops exist. The mod is **not
yet feature-complete even under “everything could work, bugs expected.”** There are
missing guest actions, missing generated objects and incomplete result/rejoin paths
in ordinary gameplay. These are development tasks, not just requests for more testing.

The strongest current slice is joining, buying/unpacking groceries, Sorbet driving
with a passenger and driver swaps, agreed sleep, and host save/rejoin. Recent local
checks also cover individual parts of assembly, utility payments, firewood delivery,
moose chopping, death and Corris punctures. Those narrow successes must not be
extrapolated to every car, item, job or native save configuration.

The inventory below contains **82 named outcome areas: 27 Candidate, 52 Partial,
0 Missing and 3 Review**. These are deliberately unweighted and sometimes share
dependencies: **27/82 is not a percentage-complete estimate**. A Partial row may
need one missing transaction or a substantial gameplay pipeline.

## What “the broad alpha is implemented” means

For each normal shared activity, either player can initiate it; the authoritative
machine resolves it once; both players see the same useful result; a joining guest
can reconstruct it; and host save/reload preserves whatever vanilla persists.
Personal needs and controls stay personal. A rough animation or a newly discovered
bug is compatible with this milestone. A deliberately absent guest operation is not.

This is a feature-complete **experimental** milestone, not the four-player winter
soak or stable-release gate in PLAN. Voice, host migration and imported computer
minigames do not need to delay this alpha. A smaller limited-feature test release
can precede it, provided its advertised scope says so.

## Evidence and limits

- Reviewed production handlers, discovery/catalog bindings, guest suppression,
  result application, creation, snapshots and persistence across session/player,
  item/home, vehicle/assembly and economy/activity code. Independent read-only
  audits covered the latter three areas; central findings were cross-checked.
- Native inventory: [catalog/dump-23268598.json](../catalog/dump-23268598.json),
  **8,615 FSMs and 1,940 rigidbodies**, captured **2026-06-13**. The installed Steam
  manifest still identifies build **23268598**. Existing extracted native action
  evidence and recent local run artifacts supplement that inventory. This audit
  did **not** recapture every native action or run a new gameplay session.
- An FSM count is not a completion percentage: many FSMs are local presentation,
  dormant legacy content or repeated templates; one missing transaction can break
  an entire activity. Counts of protocol messages or passing tests are equally
  unsuitable. The tables below are a feature inventory, not a weighted estimate.
- **Candidate** means a plausible implementation exists for the precisely named
  slice, with no concrete missing link identified here. It is not a claim of live
  acceptance. **Partial** means code exists but a required link is missing or
  unresolved. **Missing** means no applicable shared adapter was found. **Review**
  means native reachability or the detailed operation still needs classification.
- Historical checkboxes in the roadmap record earlier slices, not whole-feature
  acceptance. This audit supersedes those broader completion interpretations.

Paths in evidence columns are relative to `src/WinterMP.Core/` unless stated
otherwise; the [code map](CODEMAP.md) supplies detailed routing.

## Session, players, saves and delivery

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| S01 | Steam host, friends/invites, join, version/catalog refusal, failed-attempt retry | Candidate | `Steam/SteamLobbyManager.cs`, `SteamP2PTransport.cs`, `Session/SessionManager.Launch.cs`, `.Handlers.cs`. Real Steam/two-PC acceptance remains separate from local UDP checks. Friends-only hosting exists; public/invite-only mode selection is not implemented. |
| S02 | Late join, reconnect, peer cleanup, ownership/seat release, targeted resync | Partial | Session snapshots and item/vehicle/FSM recovery exist. Coverage is limited by each subsystem's snapshot; generic last-action replay is not an absolute world image. `Sync/WorldSyncManager.Snapshots.cs`, `FsmWorldSync.Snapshots.cs`. |
| S03 | Host saves the world; guests retain their own native save | Candidate | `Session/GuestSaveGuard.cs`, `GuestProfileStore.cs`, native SAVEGAME observer and v214 save/restart checks. Guest protection intentionally lasts until process exit; returning to solo/hosting needs restart. |
| S04 | Player presence, movement, names, chat and occupied seats visible | Candidate | `Sync/PlayerSyncManager.cs`, `RemoteAvatar.cs`, text chat and host occupancy. Remote avatars are visual; detailed hand/tool animation and physical player pushing are not complete. Vehicle seat availability is limited below. |
| S05 | Personal hunger, thirst, fatigue, urine, body warmth, stress, intoxication, dirt and BAC resume | Candidate | `PlayerNeedsSync.cs`; `WinterMP.Net/Sync/GuestProfile.cs` stores pose plus nine need values with availability. This does not establish persistence for every other native personal statistic. |
| S06 | Clothing/garments agree and resume after reconnect/restart | Partial | `ClothingSync.cs` transmits stage/type/garment. Avatar lookup consumes only stage/type; winter garment visuals are unfinished. Guest profile has no clothing fields or restore path. |
| S07 | Agreed sleep, cancellation, disconnect during sleep, normal death/recovery and group permadeath | Candidate | `SleepConsentManager.cs`, `PlayerSleepHook.cs`, `DeathSyncManager.cs`, `PermadeathSettings.cs`. Recent bounded native two-game evidence exists. Unconscious/pass-out clock changes need their own review; they are not the bed hook. |
| S08 | Installation, matching package, backup/restore, diagnostics and usable test distribution | Partial | Launcher installer, hashes/compatibility, backups and diagnostics exist. Current tester docs describe protocol120 while working code is257; release packaging/docs must match. Updater reads GitHub `/releases/latest`; it has no implemented beta/test-channel selector. Manual matching tester packages remain usable. |
| S09 | One failed feature does not stop unrelated world sync | Partial | Newer adapters contain failures locally, but `WorldSyncManager.HandleSyncError` still counts failures across its update and ultimately disables all world sync. Subsystem containment remains architectural work; this audit does not assert a new reproduced crash. |
| S10 | Other personal state: caffeine/smoking dependency, unconsciousness, carried equipment and statistics | Review | These are not all represented by the nine-value profile. Define which affect resumable survival and inspect native PassOut/KnockOut time/relocation behavior. Lifetime achievement/statistics presentation can remain local; do not invent a shared inventory where vanilla uses world objects. |

## Items, factories, contents and consumption

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| I01 | Arbitrary loose items have stable identity across different saves and discovery times | Partial | `ItemWorldSync.Scan.cs` still has a generic same-path/initial-position ordinal fallback that assumes matching saves and skips late collisions. Dedicated native bag/part/meat adapters improve specific families, not every item. |
| I02 | Exclusive grocery-bag pickup; one/all unpacking; partial bag reconnect | Candidate | `.BagBindings`, `.BagPickup`, `.Bags`, `.BagSpill`, `.BagReplicas`; local grocery and restart checks exist. Acceptance of every mixed product remains open. |
| I03 | Saved supported groceries, replacement boxes and trophies materialize for a new guest | Candidate | `.SavedProducts`, `.PackageFactories/.PackageOpening/.PackageReplicas`, `.Factories`. Thirty part-box profiles plus spark-plug box, and fifteen trophy factories have dedicated paths. Fitting and race rewards are separate outcomes. |
| I04 | All separately sold/generated supplies materialize once, including after joining | Partial | `Spawner/CreateItemsSeparate` is outside the explicit bag creation manifests. v254 now provides all three engine-oil grades with host factory identities, guest purchase, quantity/material/empty state and native save/rejoin evidence. Beer cases, charcoal, extinguisher, two-stroke fuel, coolant and batteries still need end-to-end factory accounting. Live purchase replay may create some outputs but is not a late-join manifest. |
| I05 | Fuse, light-bulb and R20 battery boxes yield shared individual contents | Candidate | v238 covers native five-fuse boxes: guest purchase/bag unpacking, host-authoritative extraction, shared persistent loose fuses, remaining quantities, retirement and native save/rejoin. Twenty-two distinct native assertions plus a final four-assertion cold repeat cover this family. v252 adds single-use light-bulb boxes, shared transient bulbs/condition, guest purchase/bag opening and save/rejoin with 21 controlled checks. Only unopened boxes persist natively; loose bulbs do not. v257 adds four-cell R20 boxes and persistent loose batteries, guest purchase/bag unpacking, exact native output IDs, competing pickup, retirement and save/rejoin with 32 controlled checks. Fuse-holder use is H04; bulb fitting and battery-powered appliances remain separate. Manual controls and Steam/two-PC acceptance remain open. |
| I06 | Shared eating/drinking/disposal consumes an item once | Partial | `.Despawn.cs` validates terminal despawn/retirement, but generic native benefit/destruction happens locally before host acceptance. Simultaneous use, finite portions and native persistent deletion need explicit accounting. |
| I07 | Milk freshness/refrigeration and moose-meat condition/presentation | Candidate | `.Milk`, `MilkConditionBinding`, `.MooseMeat`, `.MooseMeatReplicas`; host condition and joining exist. Natural moose-meat cooking has controlled local evidence in v233; spoilage and native meat save/reload remain acceptance gaps. |
| I08 | Other spoilable food keeps host freshness/rotten state | Partial | v248 carries host loose-sausage condition, cooked/charred/spoiled presentation and native food effects. Unopened sausage packages and pizza still lack continuous host Condition/fridge decay; milk/meat adapters do not cover every food. |
| I09 | Sausage package converts into four shared loose sausages | Candidate | v248 binds all eight native conversion graphs, captures four outputs with shared IDs/condition and retires the exact package. Stable native package IDs prevent a subsequent purchase inheriting an earlier retirement. Host/guest opening, food state, consumption and rejoin have controlled native evidence. Native cold reload retains the unopened package and removes consumed packages; loose sausages have no vanilla save routine. Physical input, other cooking spots and Steam/two-PC acceptance remain open. |
| I10 | Beer-case bottle count and extraction; wood-carrier contents | Partial | Generic `Remove bottle` replay is not the absolute remaining count. Wood carriers have native Woods/object arrays without a contents adapter. Shared motion alone does not reconstruct either container. |
| I11 | Jerrycan fuel amount and pouring state | Candidate | `FluidContainerSync.cs` carries owned FuelLevel/MaxCapacity/Pouring, validates ownership/order and supplies snapshots. The full source-to-destination transfer, mixture and fire effects remain separate. |
| I12 | Maintenance bottles, finite water, charcoal and other resource transfers | Partial | Protocol235 implements ATF into the installed Corris automatic: shared bottle identity/remainder/empty state, guest cap, host-conserved transfer, local gauge and native save/rejoin. Its controlled fixture and acceptance limits are in BUILDING.md. v255 adds the Corris engine-oil cap and conserved bottle-to-mounted/saved-pan transfer, including contamination/viscosity, epochs and empty-bottle save protection. Physical/Steam acceptance and detached-engine filling remain outside that bounded path. Coolant/brake-fluid, finite buckets/dippers, charcoal and other resource transfers remain missing or incomplete; destination state alone cannot close them. |

## Vehicles, assembly and practical maintenance

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| V01 | Vehicle pose/ownership, parked corrections and carried cargo | Candidate | `ItemWorldSync` motion/cargo and `VehicleWorldSync` streams. Corris parking-joint repair has local evidence. Multi-car collisions and long loaded drives remain validation gaps. |
| V02 | Sorbet start/drive, passenger travel, running handoff and parking brake | Candidate | `.EngineHandoff`, `.ParkingBrake`; 24 retained local two-game handoff checks. Physical controls and Steam/long drives remain open. |
| V03 | Corris and every other drivable vehicle complete normal operation | Partial | Discovery covers eight families, but current dedicated engine-handoff metadata is Sorbet-only. Corris engine-powered co-op travel from a completely assembled car is not established. Test each native control/engine graph rather than extrapolating from registration. |
| V04 | Human passengers in every appropriate vehicle | Partial | v249 adds taxi front-right and rear-left human seats; rear-right stays reserved for the native customer. Sorbet/Corris retain three seats. Controlled native checks cover both driver roles, motion, fare boarding, occupancy rejection, rejoin, tutorial/exit recovery and cold load. Other suitable vehicles still lack seat registration; physical input, camera comfort, long driving and Steam/two-PC remain unverified. |
| V05 | Complete car construction: stock/saved/bought parts, mounts, bolts, removal and adjustment | Partial | Thirty-nine replacement factory adapters plus dedicated persistent cylinder head, generic identities and bolt state exist. Complete body/suspension/brake/nested-assembly coverage is not established; unsupported factory outputs and missing/different guest assembly reconstruction remain incomplete. Engine input proxies do not create or fit the physical parts. |
| V06 | Shared wiring, battery, heater and engine installation feed a working engine | Partial | Host circuit/battery/block/gearbox/heater states and 102 cataloged engine consumer mappings exist. v239 adds native two-endpoint ignition-to-fuse-box installation, shared cable/endpoint presentation, host destruction, persistent reload and guest restoration, with 22 controlled native assertions. Other wiring circuits, complete physical assembly/start/repair and manual control acceptance remain unresolved. |
| V07 | Engine heat, cooling, electrical draw and wear follow a guest driver | Partial | Native host RPM/torque/movement inputs, charging, starter draw/wear and other readers exist. Remaining accessory demand and full integrated engine operation/failure need closure. Individual safe readers are not proof of an operational engine. |
| V08 | Damage/breakoff, drivetrain wear and repairs have matching mechanical/visual results | Partial | `.Damage.cs` deliberately stores accepted damage without replaying destructive native breakage. Exact wear/oil streams exist; complete breakoff/failure and fitted-part repair effects do not. |
| V09 | Tyre pressure, health, punctures, worn/flat/rim behavior and repairs | Partial | Host health, pressure readers, rim presentation and v232 puncture authority exist. Delegated continuous tyre wear, ordinary parked refill publication and the complete physical repair lifecycle remain open. Controlled rolling checks were not full powered-car acceptance. |
| V10 | Fuel-station use and jerrycan/bottle transfers into vehicles | Partial | `.Fuel.cs` accepts bounded nearby guest refueling of a parked vehicle. Protocol235 adds the named ATF-to-Corris-automatic slice with controlled native source/destination and persistence evidence. v255 adds the Corris engine-oil cap/refill slice. Full jerrycan transfer acceptance, detached engines and coolant/brake-fluid adapters remain open (I12); full Corris assembly/driving is separate. |
| V11 | Cabin heating/defrost, frost/fog and guest scraping | Partial | Climate/engine/passenger inputs exist for native climate-equipped Corris/Sorbet/Machtwagen. Guest scraping has no dedicated intent. Do not add invented cabin simulation to vehicles that have none natively. |
| V12 | Fleetari services, bodywork/tuning/springs/tyres and spray/fabric appearance | Partial | `RepairShopSync.cs` captures the order and payment; completed paint/bodywork/all mechanical results are not fully projected. Can Fluid/ColorID, target paint and fabric MaterialID/FabricLeft lack a complete shared path. |
| V13 | Trailer, towing rope and implement attachment/detachment | Partial | v247 implements native tractor proximity attachment, host-validated guest release, the three-body trailer graph, shared physics authority and native save/rejoin (24 controlled checks). Generic mass-based chassis ownership is excluded; parked coupled tractors stay host-owned. Towing-rope Car1/Car2/Hook1/Hook2 graphs, other implements, loaded physical travel, extended-bed loose cargo and hydraulic/hatch control acceptance remain open. |
| V14 | Inspection, registration, renewal and physical plates | Partial | `InspectionSync.cs` mirrors the standard checklist, renewal, museum flags, standard/museum plate strings and all four plates' availability. Complete physical plate installation and museum inspection action/result flow remain unresolved. |

## Home, survival and equipment

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| H01 | Time, calendar, forecast, ambient temperature and snow agree | Candidate | `TimeWeatherSync.cs` and join seed; weekday events and periodic correction exist. Cloud sprite choice may remain cosmetic/local. |
| H02 | Doors, switches, showers and TV power operate and reconstruct loaded state | Partial | Catalog-matched live controls exist. Generic snapshots require LastSyncedState; untouched loaded state and non-idempotent action replay need an absolute-state audit. TV power does not cover every channel/remote operation. |
| H03 | Bills, main switches, effective electricity supply and cutoff | Candidate | `UtilityBillSync` and dedicated electricity/phone payment partials; bounded local host-away settlement and milk/power checks exist. Natural envelope access, accrual and native reload remain acceptance work. |
| H04 | Individual fuses/holders, insertion, tightening, blowout and removal | Candidate | v246 binds seven house and four apartment holders. Shared fuse consumption, fitting/removal, turns, native circuit flags, exclusive pickup, guest isolation and host save/rejoin have controlled native evidence. Cold-load classification and native PART pickup are covered; physical input, automatic overload/fatal shock, every circuit and Steam/two-PC remain unverified. Cross-home fitting is refused on both roles. |
| H05 | Fridge cooling and home radiator/room heating | Partial | Fridge doors, milk outcomes and two thermostat families exist. Full cooling-area reconstruction, all food and whole-room heat/consumption across fuses/doors remain incomplete. |
| H06 | Home stove knobs, temperatures and natural cooking/fire triggers | Candidate | Protocol233 supplies both homes' guest knob requests, full native heat, grill/burn triggers, light/smoke and reversible guest simulation. One shared moose-meat cooking result uses state210. See the stove checkpoint in BUILDING.md for controlled native evidence and acceptance limits. Sausage conversion is now covered by I09/v248. Other food condition (I08) and full fire lifecycle (H11) remain separate gaps. |
| H07 | Fixed woodstoves/fireplaces: shared fuel, light, heat and finite feeding | Partial | `HeatSourceSync.cs` broadcasts six fixed sources and action intents. Resource identity/once-only consumption is absent; observer hooks do not fully suppress independent guest actions. |
| H08 | Electric sauna timer, power, water and steam | Partial | Temperature source exists. Native ButtonTime knob lacks an adapter; steam lookup expects direct StoveTrigger while the home native path is Kiuas/StoveTrigger. Finite dipper/bucket water is also unresolved. |
| H09 | Portable grill charcoal/light/heat/cook/dump | Partial | Generic Burn/Off only. Portable grill is outside fixed heat-source simulation; charcoal transfer, cooking outputs and dumping/fire consequences need a complete path. |
| H10 | Coffee water/grounds, boiling, pan/cup transfer and caffeine | Partial | v250 implements the household pot/cup and finite grounds packets: shared native preparation, conserved transfer, accepted personal drinking, rejoin and native saving. Vendor/vending coffee remains missing; physical input, varied recipes and Steam/two-PC remain unverified. |
| H11 | House/other shared fires, extinguisher use, damage and joining | Partial | Stove ignition edges exist. Targeted put-out, extinguisher Fluid, remaining fire, destruction and persistent/join reconstruction are not a complete shared fire lifecycle. |
| H12 | Flashlight/portable radio, bulbs/batteries/charge and lantern fuel | Partial | General objects can move; lantern ON/OFF matches a generic control. Battery installation/charge, flashlight/radio controls and lantern Burntime/depletion need adapters. |
| H13 | Jacks, hoist, alarms and smaller useful equipment | Review | Native jack/hoist motion/hook and alarm TimeSet/Alarm controls lack dedicated mappings. Hoist bolts alone do not establish operation. Classify tool cases, triangle, sandbag/digging tools and folding/opening effects individually; moose axe support is a specific exception. |
| H14 | Fireworks/explosions, garbage burning/disposal and world fish trap | Review | Native graphs exist, but general retirement/physics is not a demonstrated complete output/consequence path. Check current native reachability, target effects and factory identities before treating these as supported or excluding them. |
| H15 | Guest-created persistent yard stains | Partial | `PissAreaSync.cs` only publishes host scales. No guest PISS contribution to the authoritative marks was found. Personal urine need is already separate. |

## Economy, property and leisure

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| E01 | Shared cash/bank/income, ATM transfer and host interest | Candidate | `WalletSync.cs`, `.Banking.cs`: actual globals, acknowledged transfers, guest bank suppression and snapshots. Bank-statement/achievement history is not fully mirrored. |
| E02 | Ordinary shop/vendor purchase and debt-letter settlement | Candidate | Catalog purchase guard and dedicated debt quote/receipt path. Goods must separately satisfy I03–I05; payment success alone is not a complete purchase. |
| E03 | Slots, VideoPoker and Ventti play/settle/property wagers | Candidate | Current host ledgers, leases, private decks/outcomes, escrow, receipts, native presentation and Ventti access restoration exist. Broad physical/LOD/save acceptance remains; old observer-only descriptions are stale. |
| E04 | Lotto draw, selected tickets and once-only cash/bank claims | Candidate | `LotterySync`, `LottoTicketSync` host-issued identities, rows, outcome/claim/retirement and joining exist. No reason to keep polishing Lotto while other loops are absent. |
| E05 | Hockey odds/results/standings and Megaveto bets | Partial | Board tables and reload/restoration are implemented. Megaveto selection/ticket/claim ledger is missing; generic ticket payment cannot submit the selected bet. Individual-player scoring/Pistepörssi is not in the board snapshot. |
| E06 | Classified listings, phone orders and mailed deliveries | Partial | `MailOrderSync` pairs exact guest order with payment and snapshots pending records. Actual classified listings still generate locally; every delivery family depends on missing factory coverage. |
| E07 | Rent/eviction, Kela paperwork, applications/contracts and benefits | Partial | Host rent/welfare scalars, benefits and eviction presentation exist. Guest application generator/choices are not a dedicated authoritative shared paperwork workflow. |
| E08 | Shared home/car stereo settings and other useful media state | Partial | Power/channel/volume/bass have adapters. Exact CD/jukebox content is deferred/local; TV remote, other live pages and classified content must not be inferred from power or issue-number sync. |

## Jobs and earned money

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| J01 | Firewood unloading, pile/load accounting, offer and collection | Candidate | `FirewoodDeliverySync`, buyer bindings and reserved payment path; signed penalties and rejoin have local evidence. Full cutting/loading/tractor hookup remain J02/V13. |
| J02 | Firewood cutting, loading, cutter/PTO/implement interaction | Partial | Some controls and cargo exist; guest dynamic implement/resource references and complete production/loading are absent. |
| J03 | Sewage hose attach/suction, house progress, treatment/disposal and payout | Partial | `JobSiteSync` publishes house/GIFU scalars; dump and payout controls exist. Guest hose identity/placement/suction and complete disposal transaction were not found. |
| J04 | Flea-market place/price/sell/remove items and collect proceeds | Partial | v237 adds a native chips listing journey to v236 paid rental/proceeds: shared prices and saved identities, exact native sales/retirement, expiry returns, once-only collection and native save/rejoin. Twenty-one final native assertions cover this family. Other accepted item families, legacy random listings and early manual withdrawal remain open. |
| J05 | Kilju ingredients/quality/fermentation, bottles and buyer sale | Partial | `KiljuSync` carries only Alcohol/BrewTime/Finished/LidOn. Sugar/Yeast/Water/Sweetness/Vinegar, bottle transfers and authoritative sale are not covered by these fields. |
| J06 | Taxi employment, calls, customer boarding/destination/luggage, fare and card/cash payment | Partial | Native audit below corrects the earlier fare model. Host pickup recognizes an accepted guest driver; v240 shares availability, incoming calls, routes and customer presentation. v241 adds duty/meter; v242 adds native arrival, guest terminal quote/cash, unpaid-offer rejoin and collected-income save/reload. v243 adds shared receipt printing/identity/handoff and receipt/distance save/reload. v244 adds five-piece luggage selection, identity, carrying/cargo, recall and rejoin. v245 shares native salary reports/read acknowledgment and verifies bank settlement, ledger reset and save/reload. One connected call/luggage/fare/receipt/earned-payday/save journey passes 21 controlled native checks, including menu-transition cleanup. Host-only tutorial/outbound phone, player passenger seats, physical/two-PC input and saved mid-fare progress remain gaps. |
| J07 | Hitchhikers and related rides/story/suitcase payments | Partial | Stage/flags/payment and pose exist. Boarding references, passenger mass, story outcomes and suitcase Pick money/spawn are not complete. Dormant story variants need reachability review. |
| J08 | Farm/hay/combine and factory employment/production/pay | Partial | Farmer progress/Done and factory punch-clock/aggregate package/pay fields exist. Guest delivery/production item identities, machinery, employment branches and complete payouts are not established. Legacy farm reachability must be checked. |
| J09 | Advert delivery and exact completed mailboxes | Candidate | v253 now shares the finite pile, sheet identities/carrying, exact native mailbox flags, once-only delivery, day reset/pay, rejoin and native saved progress. All 27 mailbox targets remain available independently of local-camera house LOD; native index22 is absent. Loose sheets are session-only. The selected delivery/payout slice has controlled two-game evidence. v256 adds host-approved 08231206 enrolment through the apartment, old-house and taxi phones, shared household charges, cancellation and native persistence. The initial 600-second wait remains native; no arbitrary job-start event is accepted from guests. Physical input, full-route and Steam/two-PC acceptance remain open. |
| J10 | Scrap delivery, accepted weight/items and once-only payment | Partial | `WorldScalarsSync` sends scrap price. Native Scrapmetal/GarbageTrigger Weight/ValueTotal/Object transaction has no complete adapter. |
| J11 | Every job payout is reserved/collected once and never replayed by a join snapshot | Partial | The dedicated firewood payment guard excludes replay and reserves an offer. Other ordinary payout controls lack that safeguard. This is a code-confirmed gap, **not** a claim that every payout has been reproduced duplicating. |

## NPCs, wildlife, crime and racing

| ID | Required outcome | Status | Evidence / remaining implementation |
|---|---|---|---|
| W01 | Ordinary traffic/NPC movement, activation, spawn/despawn and guest interactions | Partial | `NpcTrafficSync` streams registered bodies; full ordinary-AI suppression, dynamic manifests and interactions are incomplete. Bus boarding/tickets/doors and NPC reactions are not supplied by pose alone. |
| W02 | Train motion and collision hazard agree | Candidate | v251 adds dedicated host train motion, route/wait state, eleven colliders, lights/horn counters, stale withdrawal and full/rejoin reconstruction. Twenty controlled native checks include actual guest-player train death and host native death comparison. The guest body must remain dynamic for player contacts. Physical crossing/car crashes, audible horns, full respawn and Steam/two-PC remain unverified. Native train position has no save fields; cold start follows vanilla. |
| W03 | Moose death/corpse/guest chopping produces shared meat | Candidate | Independent corpse/ragdoll/chop counters, host output factories and meat state have recent local evidence. Physical axe/vehicle collision and native meat persistence still need acceptance. |
| W04 | Incoming calls at both homes, active-call joining, answering and job consequences | Partial | `PhoneSync` binds one first-found phone and sends topic-only events. No per-phone active-call snapshot or answer/consequence ledger. It now activates ringing objects; that old specific roadmap complaint is obsolete. |
| W05 | Reijo, pub/dance-hall fighters and consequential NPC reactions | Partial | Reijo Move/pose exists but Janitor Anger/Target/SHOOT remains local; pub bodies alone do not carry fighting. Dance-hall fighter is outside registered NPC roots. Check dormant Suski/story reachability before promising it. |
| W06 | Police checkpoint fines and payment | Candidate | `PoliceSync` validates checkpoint/pose/vehicle/sequence and publishes fine records; physical roadside acceptance remains. |
| W07 | Crimes, pursuit targets, DUI/escalation and impound consequences | Partial | Group crime counters and sirens exist. Reports do not identify/validate the source incident; shared pursuit target and complete arrest escalation are missing. A host vehicle relocation can stream, but guest offense-to-impound flow is not complete. |
| W08 | Arrest, jail, sentence persistence and release for multiple players | Partial | `JailSync` relays one offender-local countdown with a 30-second freshness slot. It does not implement authoritative multi-offender confinement/release or persist jail in guest profiles. |
| R01 | Rally stage crossing/timing | Candidate | `RallySync` has authenticated crossing/progress and recovery. Race enrollment/results are separate below. |
| R02 | Rally enrollment, full results, penalties, prize eligibility and collection | Partial | `RallyResultsSync` carries selected times/class/flags, not complete native result lists or guest registration action. Prize/trophy adapters do not supply missing race eligibility. |
| R03 | Ice-race enrollment, grid/heat/progress/results/prizes | Partial | Marker ledger, host grid/heat, six result rows, AI opponent stream and prize routing exist. End-to-end enrollment and outcome authority remain unresolved. |
| R04 | JOKKIS registration, guest laps/crossings, result/reward | Partial | `JokkisRaceSync` only broadcasts laps/time/checkpoint scalars; no guest crossing ledger equivalent to rally/ice progress. Full lifecycle is not implemented by those fields. |

## Intentional boundaries and unresolved native content

Keep individual needs, camera/input, infinite thirst fixtures, ordinary sound/particle
variation and display preferences local. Remote-player physical pushing is not a v1
requirement. Exact CD/jukebox tracks/cloud sprites and the imported computer/fishing
minigame remain deferred under the existing roadmap. Host migration is out of scope.
Voice is planned functionality, but can follow this experimental gameplay alpha.

The world fish trap is not automatically excluded because a computer fishing toy is.
Likewise, a dormant Mummola/StrawberryField well or mailbox is not proof of an active
grandmother/berry job. Resolve reachability of legacy farm/story/minor equipment
features against the current game before adding invented work or claiming coverage.

## Work order toward the broad alpha

These are completion areas, **not instructions to implement a whole area in one
turn**. Select one bounded player outcome at a time, finish its necessary checks,
record evidence, then rotate. Reassess new tester bugs at each boundary. Shared
identity/economy/save corruption takes precedence over this suggested order.

1. **Stove implementation checkpoint (v233)**: both homes' controls, native host
   cooking result, joining and cleanup are implemented. Its bounded native check
   and remaining acceptance limits are in BUILDING.md. Keep the absent sausage
   factory explicit; do not extend cooking by inertia.
2. **ATF maintenance checkpoint (v235)**: guest bottle depletion, host Corris
   gearbox gain, cap/gauge, joining and native persistence have controlled evidence.
   Keep other fluid families open and rotate to a job.
3. **Flea chips checkpoint (J04, v237)**: the bounded listing/sale/expiry/collection
   journey has native persistence and rejoin evidence. Other families remain open;
   rotate after this checkpoint instead of extending flea-market scope by inertia.
4. **Fuse-box checkpoint (I05, v238)**: shared purchase/opening, remaining contents,
   exact loose fuse identities, retirement and native save/rejoin have bounded
   evidence. Other supply families and household fuse installation remain open.
5. **Next: one Corris ignition-to-fuse-box wiring journey (V05/V06)**: audit and
   implement guest installation/removal, shared physical/engine result and native
   save/rejoin. Continue Corris assembly/operation by actual player steps: missing part families,
   wiring, start/drive, damage/repair and save/rejoin. Interleave those bounded tasks
   with remaining jobs/home tasks; do not return to hundreds of isolated engine
   reader checks unless a concrete failure requires one.
6. **Complete other essential world loops**: towing/implements; sauna/fuses/food
   contents/coffee/firefighting; remaining job initiation/progression/calls; police
   arrest/jail; race enrollment and JOKKIS progress. Each needs an action/result/
   rejoin path, not another status-only broadcaster.
7. **Finish remaining scope and distribution**: omitted train/NPC interactions,
   equipment, service/paint output, garment/profile details, Megaveto, then resolve
   the Review rows. Match package version/protocol/catalog/docs and provide a
   feature checklist for testers. Do not spend these cycles polishing Lotto or
   performance without a new demonstrated blocker.

For every task, use the smallest meaningful native/two-peer check of the new path,
including host-away guest input where relevant. **Do not require every physical,
platform, four-player or long-session test before allowing a Candidate into the
experimental alpha.** Those belong on a separate PASS/FAIL/NOT TESTED sheet. This
keeps uncertainty visible without turning pre-alpha development into endless polish.

Per-task follow-up notes (2026-09-13..14) were removed on 2026-09-22; see the [snapshot](https://github.com/Steinkoloss/our-winter-car/blob/snapshot/hermes-2026-09-14/docs/SYNC-SCOPE-AUDIT.md).
