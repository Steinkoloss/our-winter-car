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

## Scope-audit checkpoint, before the stove follow-up

**Historical checkpoint; the follow-up below supersedes its unfinished status.**

No gameplay code was changed for the scope audit, no game was launched, and nothing
was committed, deployed to the personal install or published. Current Core builds
against the installed game with **zero warnings/errors** and deployment disabled.
A fresh Net test run could not execute because this environment lacks the .NET8
targeting pack; its empty initial invocation is not a test pass. Earlier checkpoint
test results remain historical evidence, not a current full-suite claim.

Protocol233 stove edits are retained as **unfinished**, with their prior local
evidence under `build/stove-cooking-audit/`. The last retained retry demonstrates a
host-away guest knob and matching >200°C heat; it does not demonstrate the full
natural cooking/rejoin outcome or final both-home bindings. The latest build passes,
but that does not close gameplay acceptance. Final scope-audit hashes confirm all
18 protected personal files and both disposable profiles' 12 native text saves
remain unchanged. Resume this exact WIP rather than silently restarting or marking
it complete.


## Stove follow-up and next task (2026-09-13)

H06 is now Candidate for its named slice, not all food or fire gameplay. The
protocol233 implementation and controlled native evidence are documented in
[BUILDING.md](BUILDING.md#guest-home-stove-cooking-2026-09-13-unreleased-v233).
The final virtual-desktop run passed **29 two-game assertions** across natural
cooking, smoke, disconnect, rejoin/resync and refused requests. Both games exited
normally; the test probe was removed and protected saves are unchanged.
The SDK dependency problem above is resolved: **4,625 Net and 18 launcher tests
pass**, and final Release Core/Net35/probe builds have zero warnings/errors.
The historical scope-audit checkpoint above is retained as it happened.

Next, implement **guest ATF refill of the installed Corris gearbox**, including its
uncatalogued filler cap. ATF already goes through `Spawner/CreateItems`; its child
`ATFOilTrigger::Data.Fluid` is not covered by `FluidContainerSync`'s jerrycan fields.
The native filler subtracts 0.1 units/second from that source and adds it to mounted
gearbox `Data.OilLevel` (capacity 6.3). Message202 already supplies gearbox oil to
engine readers; bottle remainder, cap state, an authoritative transfer and the
filler gauge are the missing links. Extract the bottle's exact root/child copying
and empty behavior before implementing. Verify guest operation with the host away,
finite depletion/gain, cap/full/empty stop conditions, replay refusal, differing-save
rejoin and native persistence. Then rotate to a guest earning loop.


## ATF follow-up and next task (2026-09-13)

The ATF slice above is now implemented and its bounded checks/cleanup are closed:
**33 final two-game checks plus six native full-restart checks pass** on one payload,
with **4,755 Net and 18 launcher tests** and clean Release builds. Native bottle
remainders, empty identity and gearbox oil persist; guest cap/gauge/native objects
restore on disconnect. Protected personal files and all guest native saves are
unchanged. [BUILDING.md](BUILDING.md#guest-corris-atf-refill-2026-09-13-unreleased-v235)
records exact quantities, initial failures, the stabilized incomplete-engine
fixture, native-control injection and outstanding physical/Steam/assembled-car
acceptance. A cap-only projection may offset against a different guest engine mesh.
Recurring native and general sync log errors are retained, not declared harmless.
I12/V10 remain Partial, so the aggregate scope counts do not change.

Next is **one complete guest flea-market selling journey (J04)**. Rent/proceeds
already broadcast, but placement/pricing and exact listing identities remain local.
Audit the native paid rental, PriceGuide and MoneyFlea boundaries, choose one
supported shared item family that vanilla accepts, and carry it through listing,
unsold retrieval or sale, exact retirement and once-only collection. Conflicting
inputs, rejoin and native host persistence belong to that same bounded task.
This closes a missing earning loop and rotates away from maintenance; sewage and
taxi currently have larger physical dependencies. Reassess after closure and
rotate to the queued ordinary supply-creation task. The ATF validation limits do
not justify expanding engine internals or another performance/soak cycle now.

## Flea-market native audit and payment dependency (2026-09-13, v236 unreleased)

Fresh build-23268598 inspection found an existing correctness blocker before
shared listing work: `BuyTableRent::Buy/Add` only adds a week and 150 MK to the
local basket. The old guest hook sent `Logic/RENT` immediately, so it granted days
without the later cash-register debit. The generic cash-register handler also
intercepted the new dedicated checkout during the first native test; that overlap
is removed. `MoneyFlea::Use/State 1` clears table proceeds, adds its cached `Money`
to PlayerMoney and hides itself. Generic replay could repeat that cached credit.
Both boundaries now use a host quote, request and cached receipt. Day zero is
rented; -1 is inactive. Proceeds become collectable after rental expiry, not after
every sale. A renewed rental retains uncollected proceeds for the next expiry.

The next listing implementation must preserve these native facts:

- `SaleTable` stores `ItemIDs` (array) and `Items` (price hash), saved as
  `FleamarketArray` and `FleamarketHash`; days/money use `FleamarketDays` and
  `FleamarketMoney` in the host's native savefile.
- A new listing gets the item's name plus an eight-digit random suffix. Selling
  strips that suffix and calls `FindChild` by the original name, so two identically
  named items need an explicit shared-ID mapping to retire the correct object.
- Pricing uses `Sheets/KirpputoriSticker/Price::Logic`; input accepts up to three
  digits (0–999 MK). `Set price` writes Logic.Price and sends PRICESET. The table
  parents the item, changes its layer to 0 and makes it kinematic before storing
  the key. These are authority/ownership boundaries, not safe generic replay.
- The native PriceGuide has 169 entries, including potato chips at 5 MK. A first
  supported family still needs its actual native acceptance, saved identity and
  garbage/retirement path verified. No family is declared complete by this audit.
- Native Sell uses a timed random roll while the local player is away from the
  table; DayChanger separately decrements rental days. Guest Logic/Sell/DayChanger
  now pause and restore. Host sale remains native.
- RESET restores proxy snapshots; v237 cold-load testing found they can contain
  old saved listings. Shared expiry now explicitly removes its keys and releases
  unsold physical pins. Native early manual withdrawal remains unestablished.

The controlled payment checks stage proceeds directly in a disposable host save;
they do **not** claim an actual item listing or sale. J04 and aggregate readiness
counts remain unchanged. [Payment evidence](BUILDING.md#flea-rental-and-proceeds-2026-09-13-unreleased-v236)
records test payloads, failures and limits. Continue J04 because shared item
identity is a remaining part of the selected earning journey. After that journey
closes, reassess and rotate to ordinary supply creation as already queued.


## Flea chips journey closed; supply contents next (2026-09-13, v237 unreleased)

The selected supported family is native Chips. Factory `Use.ID = chipsN` maps to
an `OWNNNNNN` native listing suffix, retaining the native price guide while avoiding
ambiguous same-name child lookup. Native saved collections persist prices and
identities even when runtime network IDs change. Guest price-sheet input is host
validated; accepted listings block loose motion/despawn claims. Native timer and
controlled SELL checks retire exact objects and preserve native consumed deletion.
Expiry clears stale saved proxy entries and releases unsold items.

**21 final native assertions**, **4,814 Net tests** and **18 launcher tests** pass,
with a clean Core/probe Release build. In the controlled journey an initial packet
sold natively for 4 MK; two other same-name packets were listed at 3 and 2 MK.
Selling the latter preserved the former and produced 6 MK total proceeds. Guest
collection left the wallet at 706 MK, and cold reload retained that cash, deleted
both sold native products, and kept the unsold packet as a pickup. Rejoin at
listed, sold and collected stages, stale messages and native action restoration
are covered within [the recorded fixture limits](BUILDING.md#shared-flea-listings-and-sales-2026-09-13-unreleased-v237).

J04 remains Partial and the 82-area counts stay **22 Candidate / 50 Partial /
7 Missing / 3 Review**. Other flea families and manual early withdrawal remain;
this checkpoint does not claim broad 0.1 completeness. The next bounded task is
**fuse-box purchase/opening with shared individual fuses and remaining contents**
(I04/I05), including persistence and rejoin. That supplies a currently missing
household gameplay dependency and rotates from jobs to ordinary supplies.


## Fuse-box journey closed; Corris wiring next (2026-09-14, v238 unreleased)

A fresh installed-build audit found FusePackage and Fuse on Spawner/CreateItems.
A package bought for 14.95 MK arrives through the existing grocery-bag path. Its
Use/Create Fuse subtracts one from the native five-count quantity and dispatches
one Fuse/SPAWNITEM at the box. The individual prefab captures fuse0N before
renaming; save keys preserve the surviving transform and delete retired identities.
The shared box adapter now supports that direct ordinary-supply output without
requiring car-part Data/Wear/Screw FSMs. New SupplyItemState227 carries its native
identity; the existing opening ledger and item movement/retirement paths are reused.

The two-player native fixture covers guest purchase and unpacking, host and guest
opening, duplicate/stale requests, exact disposal plus late replay, rejoin with a
conflicting guest original, native pickup, partial/empty save reload, and a
spark-plug opening regression. There are 22 distinct assertions and a final four
assertion cold-load repeat. Core/probe builds cleanly; 4,839 Net and 18 launcher
tests pass. See [the full evidence and limits](BUILDING.md#shared-fuse-boxes-and-loose-fuses-2026-09-14-unreleased-v238).

I05 is now Partial, so the current inventory is **22 Candidate / 51 Partial /
6 Missing / 3 Review**. H04 remains Missing. Light-bulb/R20 outputs, household
fuse installation, tightening, blowout and actual circuit effects were not added.
The next bounded task is a Corris ignition-to-fuse-box wiring journey: a meaningful
rotation toward assembling a usable car, not more supply families by inertia.

## Ignition-wire journey closed; taxi fare next (2026-09-14, v239 unreleased)

Source5 WiringIgnitionFusebox now has a real guest installation transaction and
host-derived cable/endpoint presentation. Both players complete the native two-end
handshake; the host validates the saved revision, steering-column prerequisite,
nearby live player and tracked wiring tool before running native Finish assembly.
Native host DESTROY is replicated without inventing a manual-removal action. Saved
installation and destruction survive cold reload; guest native data/actions/visuals
restore even when native binding is rejected. Pickup/rejoin resolves the persistent
native WiringTool reference because the tool's scene path changes while held.

There are **22 distinct controlled native assertions**, **4,864 passing Net tests**,
**18 passing launcher tests** and clean Core/probe Release builds. The final build
repeats four cold assertions and adds four binding-failure/recovery assertions.
[BUILDING.md](BUILDING.md#shared-corris-ignition-wire-2026-09-14-unreleased-v239) records
artifacts and limits. The fixture pins the held tool/player after real pickup and
sets a disposable steering-column prerequisite; it is not physical aiming/column
fitting or proof of a running engine. The existing ignition input receives host
state, but the incomplete fixture's native Electrics graph waits on other inputs.

V05/V06 remain Partial. The inventory stays **82 areas: 22 Candidate / 51 Partial /
6 Missing / 3 Review**, without an invented readiness percentage. Full native
wiring has 35 connections; other circuits, battery terminals, shock/fire effects,
and the integrated engine remain open. Next is a single reachable taxi fare,
from call acceptance through boarding, drop-off and payment, so another normal
income loop gains shared actions/results before further engine polish.


## Taxi native lifecycle audit and pickup prerequisite (2026-09-14, still v239)

The selected one-fare journey exposed multiple missing dependencies. The first
bounded implementation is native host pickup for an accepted guest taxi driver;
**J06 and the end-to-end fare remain open**. This is a gameplay dependency within
the selected income loop, not a return to engine/performance work. The global
82-area inventory remains 22 Candidate / 51 Partial / 6 Missing / 3 Review.

Fresh installed build **23268598** extraction: `build/taxi-fare-audit/native-taxi.json`
contains **164 FSMs, 11 ArrayLists, 1 Hashtable and 752 transforms**. `flow.txt`
contains the decoded state/action trace. Asset evidence is distinct from the
controlled pickup run described in BUILDING; no completed fare was playtested.

| Native step | Actual source and behavior | Remaining shared behavior |
|---|---|---|
| Employment and availability | `JOBS/TAXIJOB::Logic` stages 0 inactive, 1 tutorial, 2 working, 3 warned. Check stage runs activation/timers; setting JobStage alone does not replay it. | Host-owned activation/tutorial/progression and guest reconstruction. The local pickup fixture explicitly enables both disposable worlds. |
| Go on duty | `Tripmeter/KnobMode::Knob` steps RotationInt 0–210 in increments of 35; 35 waits, 70/105 select tariffs. It sets ActivateCustomer and carphone DriverBreak. ButtonMode controls the roof light and Tripmeter.On, with a separate long-press reset. | Validated knob/button intents and shared indicators. The native auxiliary mode is not the next priority. |
| Generate and accept call | `TaxiWalker::Logic` Idle observes ActivateCustomer, waits 240–MaxDelay seconds, picks addresses over 1,200 m apart and away from local SavePlayer, then Call sets Ring's call ID/length/subtitle. `UseHandle::Use` sets Answer/Occupied; Ring Caller→Hangup sends SUCCESS to the waiting customer. | Shared call availability, guest answer/hangup, pickup/destination display and host-only generation. |
| Pickup and boarding | Customer New location→Activate→Distance 1/2 uses SavePlayerCam; Which car checks PlayerCurrentVehicle == Taxi. Then State 8→Towards pick point→Open door reparents the walker to GetInPivotTaxi, animates and activates passenger mass. | **Implemented prerequisite:** only these host pickup inputs can temporarily use an accepted, fresh, live guest taxi driver's seat. Other guest drivers, walkers and passengers do not qualify. Native host globals remain untouched. Shared activation and visual lifecycle are still missing. |
| Luggage | `TaxiWalker::Suitcases` randomizes five usable objects: three suitcases, beer case and mattress. The rifle reference has no Rigidbody and is absent from the selection pool (corrected by v244 native audit). DISTANCES adds 120 seconds to MaxDelay per active piece beyond 10 m. | v244 implements host selection/activation, shared identities, carrying/cargo, reset and rejoin, with controlled local distance checks. Physical loading/road acceptance remains open. |
| Meter and arrival | `Tripmeter::Function` owns live Price, distance/time accrual, OdoTrip and totals. Seated customer states test DropOffPoint within 20 m, then Out→State 9 waits for CASHIER. | Host-only meter with the driving car's accepted motion, shared destination/arrival and guest controls. **Customer Cost is not the live meter.** |
| Accept fare | Terminal Make payment copies Tripmeter.Price to terminal Cost, reduces the job timer and sends CASHIER after 2 s. Customer Pay copies Cost and opens PayMoney. That hand button sets Paid; customer Add money adds Cost to **Tripmeter.IncomeTotal**. | Once-only host validation of terminal/offer actions, current availability and shared finalized amount. **This does not add the fare directly to PlayerMoney.** |
| Receipt and departure | Terminal State 6 adds OdoTrip to OdoTotal and Cost to IncomeReceipts, prints/reuses `receipt(itemx)`, then Drop ticket detaches it. Some customers request it; hand Receipt checks the held receipt, parents it to the hand and sends CASHIER before departure. | Receipt identity/ownership, once-only printing and handoff, animation/parent/active state and late joining. |
| Payday and persistence | `TaxiFunctions::Payments` later calculates wages from IncomeTotal, receipt/distance consistency, fuel compensation and calls, then adds Money to PlayerBankAccount/PlayerNetIncome. | Host-only payday and shared rundown/letter. Scalar Money replication does not isolate native guest payment actions. |

Native saves include `TaxiJobStage`, `TaxiJobMeetday`, `TaxiJobTimer`,
`TaxiCustomerDelay`, `TaxiIncome0`, `TaxiReceipts0`, `TaxiOdo0`, `TaxiCalls`,
`TaxiOdoOld0`, `TaxiRundown`, `TaxiRundownOpen` and car transform `TaxiPos1`.
The audited customer saves its penalty/delay, **not an in-progress passenger,
route or fare**. Do not promise mid-fare cold-load recovery without implementing
and validating it. Completed earnings/receipts/odometer should follow native host
save behavior. Separate card-specific behavior was not established by this audit;
keep the broader payment inventory open rather than inventing a card branch.

Older R2.8 text and protocol-message comments incorrectly called customer Cost a
live meter and described immediate wallet payout. Those claims are corrected;
message 103 still transmits the same native fields with the same meaning. No wire
layout or admission semantics changed, so protocol239 and next free ID230 remain.

Next required dependency: host-owned taxi availability and call acceptance with
shared pickup/destination/customer presentation. Then complete meter, terminal,
receipt and saved earnings before closing the selected one-fare journey.


## Taxi service dependency (2026-09-14, v240 unreleased)

The pickup audit above remains the native lifecycle reference. This checkpoint
closes its separate activation, incoming-call and customer-presentation dependency;
**J06 remains Partial** and the inventory remains **82 areas: 22 Candidate /
51 Partial / 6 Missing / 3 Review**. No completed fare or cold-load fare recovery is
claimed. See [controlled evidence](BUILDING.md#shared-taxi-calls-and-customer-presentation-2026-09-14-unreleased-v240).

The host's native job activates the taxi/customer/phone/ignition and availability
colliders. Guests pause their independent job, customer, movement, luggage-roll,
ringing, tutorial and payments FSMs. A dedicated ordered state carries the shared
pickup/destination, customer root and local walker pose, car-relative boarding,
passenger mass, GUI and native animation clips. Generic NPC pose streaming no
longer competes for this walker. Guest teardown restores original variables,
actives, colliders, parent/pose, animations, actions and FSM enabled/restart flags.

A guest native handset press sends an authenticated intent for the current call.
Only a nearby, living guest with a fresh pose can answer an unowned ringing call.
The host writes the native Answer/Occupied outputs; native caller completion still
sends SUCCESS and creates the pickup. Duplicate/old answers cannot advance another
fare, and only the owner can hang up. Host native handset input remains final.
Last-guest departure needed a direct cleanup hook because ordinary taxi sync stops
when no peers remain; this is fixed. Current caller state and customer presentation
are restored on reconnect, without rerunning the guest job.

Remaining: guest duty and meter controls, live host meter ownership while the guest
drives, terminal/arrival flow, native luggage selection/identity/transport, shared
receipt creation/handoff and completed wage/receipt/odometer save/reload. Guest
luggage and fare-offer objects remain hidden until those paths are implemented;
this checkpoint does not claim they work. The taxi tutorial is host-operated and
outbound guest keypad calls remain unsupported. Taxi player passenger seats are a
separate vehicle gap. Physical mouse/keyboard use and Steam/two-PC play remain
unverified. Next is the bounded duty/meter/arrival input dependency; rotate after
the selected fare's actual requirements close, not into more unrelated profiling.


## Taxi duty/meter dependency (2026-09-14, v241 unreleased)

Guest normal knob modes (0–5), roof-light/start-pause and native total reset now
operate validated host controls. Only the host runs native Base cost, Time and
Distance states. Accepted matching-owner VehicleState gauge speed supplies the
meter's MpS input while a guest owns the taxi; missing/stale telemetry contributes
zero distance. This preserves the native tariff and waiting thresholds. Shared
values/LCD text/knob pose/indicator and roof lights restore on rejoin. Guests pause
independent meter/display/payment calculations; original variables, actions,
transitions, visuals and FSM enabled/restart settings restore on teardown.

Control revisions serialize native host and accepted guest changes separately
from ordinary fare updates. Player admission advances the control revision; old
revision requests are rejected before sequence admission, so delayed pre-rejoin
traffic cannot poison the new sequence. Current-revision request sequences are
consumed on rejection as well as success; duplicate, stale, distant or unauthenticated requests
cannot repeat a toggle/reset. A rejoining player gets a fresh request sequence.
The protocol is 241 with messages 232/233. See [native evidence and limits](BUILDING.md#shared-taxi-duty-and-meter-2026-09-14-unreleased-v241).

This supersedes the v240 checkpoint's duty/meter gap. **J06 remains Partial**;
the broad inventory remains **82 areas: 22 Candidate / 51 Partial / 6 Missing /
3 Review**. Native terminal/arrival flow, receipt identity/handoff, luggage and
completed wages/receipt/odometer save/reload remain required for the selected fare.
Native mid-fare persistence does not exist, and this change does not add it. The
host alone operates auxiliary knob mode 6, tutorial and outbound calls; player
passenger seats and ordinary two-PC physical input remain open validation/scope.

Project reassessment: the next useful step is shared arrival/payment because the
customer still cannot complete the selected co-op fare. Grocery duplication and
stutter reports remain in the tracker with their existing evidence; the recent
stove, ATF, flea chips, fuse-box and ignition-wire slices retain their acceptance
boundaries. Complete this fare's actual dependencies, then rotate to another
missing ordinary loop instead of adding optional taxi polish.


## Taxi arrival/payment dependency (2026-09-14, v242 unreleased)

The native host customer now exposes arrival and a single shared quote/cash offer.
Guests can accept payment at the terminal after arrival with the meter paused,
then collect the offered cash. The native terminal captures Price into Cost,
subtracts 4,500 from the job Timer, and sends delayed CASHIER. **Timer counts up as
non-work time**; the subtraction credits work time and can make it negative until
native SAVE clamps it to zero. The customer credits IncomeTotal on Paid → Add money,
using its exact Cost, not the rounded cash label. No immediate wallet/bank award or
host achievement replay occurs for remote collection.

Current-fare/control revisions, authenticated fresh nearby players and per-player
request sequences protect quote/collection. Rejoin restores unpaid cash and rejects
pre-rejoin traffic without poisoning fresh input. Guest terminal/cash commit states
send intents; independent printing, price reset and income transitions stay paused.
Service binds before fare, and fare restores before meter/service, preserving the
guest's original actions, variables and visibility. Messages 234/235 require v242.

**17 controlled native assertions** passed across guest pickup, short-distance
arrival, terminal quote, cash collection, duplicate/distant/stale rejection,
unpaid-offer rejoin, teardown and native save/cold reload. Collected **28.449 MK**
restored to both peers with no pending offer or second credit. Core/probe builds
have zero warnings/errors; **4,944 Net tests** and **18 launcher tests** pass.
All 18 protected personal files and 15 guest save files remained unchanged.
[Evidence and fixture limits](BUILDING.md#shared-taxi-arrival-quote-and-cash-2026-09-14-unreleased-v242).

This supersedes the arrival/terminal/cash and collected-income gaps above, but
**J06 remains Partial** and the totals stay **82 areas: 22 Candidate / 51 Partial /
6 Missing / 3 Review**. Receipt printing/physical identity/handoff is next because
the customer may require it to leave; then luggage and full payday/receipt/odometer
persistence are required. Native mid-fare persistence remains absent. No complete
road fare, physical-input/role-reversal or Steam/two-PC acceptance is claimed.

Reassessment: this closes the bounded payment result. Existing grocery, stutter,
launcher and earlier gameplay evidence remains recorded; no new report displaces
the unfinished fare dependencies. Receipt handoff is needed to finish the selected
ordinary loop, not optional taxi polish. Once that loop closes, rotate to another
missing gameplay area instead of extending its tools or presentation indefinitely.


## Taxi receipt dependency (2026-09-14, v243 unreleased)

Guests now print the host's charged fare, take and carry the single native physical
receipt and give it to a paid customer who requests it. Printing adds native Cost to
IncomeReceipts and Trip to OdoTotal once, then clears live Price. The pre-existing
inactive paper has a stable identity; only its loose phase permits ordinary item
motion. Host proximity/current-fare/order checks and the native pickup guard protect
handoff and exclusive holding. Customer anchoring clears the guest's pickup joint,
and the host's native ten-second timer returns the paper to its hidden printer pivot.

The test exposed and fixed a native co-op departure mismatch: a distant host could
hide the customer even when the guest was beside them, suspending paper return.
The original 99-m check now considers the nearest fresh living guest as well as the
host camera; walking and receipt return remain native.

**28 controlled native two-player assertions** passed on the identical final v243
payload, including quote/cash regression, duplicate rejection, loose-paper rejoin,
exclusive guest holding, handoff with distant host, native return, save and cold
reload. Both peers recover exactly **28.9919987 MK** collected/receipted income and
**0.1 km**. Core/probe builds have zero warnings/errors; **4,951 Net tests** and
**18 launcher tests** pass. All 18 personal files and 15 copied guest world files
remain unchanged. [Evidence, failed fixtures and limitations](BUILDING.md#shared-taxi-receipt-2026-09-14-unreleased-v243).

This supersedes the receipt printing/identity/handoff and completed receipt/distance
persistence gaps above. **J06 remains Partial**, and the inventory stays **82 areas:
22 Candidate / 51 Partial / 6 Missing / 3 Review**. Luggage identity/transport is the
next required missing output, followed by complete payday and integrated fare
persistence. No full road journey, human mouse/keyboard, receipt role reversal,
Steam/two-PC acceptance or new mid-fare persistence is claimed.

Reassessment: receipt cleanup is complete for this bounded controlled journey.
Existing grocery/stutter reports and recent gameplay checkpoints retain their
recorded limits. Luggage and payday still block the already-selected fare; finish
those dependencies, then rotate to another missing ordinary loop. Do not extend
this task into optional taxi polish or another general performance cycle.


## Taxi luggage dependency (2026-09-14, v244 unreleased)

The five usable native luggage bodies now share host selection, stable per-reset
identities, exclusive pickup, item/cargo motion and recall. Reset releases held and
loaded pieces, restores their cargo collision settings and replaces old identities
so stale movement cannot affect the next set. Rejoin restores the current selection
without rerolling it. Guest placement writes Transform pose before activating
physics; inactive Rigidbody writes alone had left guest pieces at the wrong location.

The earlier six-piece inventory was overstated. Three suitcases, a beer case and a
mattress have usable PART rigidbodies. The rifle-bag reference is unfinished native
content with no Rigidbody and no selection-pool entry. The native Amounts pool can
request six despite only five choices; the host now bounds its count before the
native zero check, allowing the selection loop to finish without altering its pool.
Native DISTANCES uses accepted shared poses and adds 120 seconds to MaxDelay for a
piece left beyond 10 m. That affects later customer waiting, not immediate cash.

**17 controlled native two-player assertions** passed on the final v244 payload:
selection/count bounding, all usable luggage families, exclusive pickup, rejection
of guest destruction/old movement, held/cargo recall, rejoin, cargo transfer,
controlled short loaded-car relocation, host unload and native distance delay.
Core/probe build with zero warnings/errors; **4,965 Net tests** and **18 launcher
tests** pass. All 18 protected personal files and 15 copied guest world files remain
unchanged. [Evidence and fixture limits](BUILDING.md#shared-taxi-luggage-2026-09-14-unreleased-v244).

This supersedes the luggage implementation gaps above. **J06 remains Partial**, and
the inventory remains **82 areas: 22 Candidate / 51 Partial / 6 Missing / 3 Review**.
Full payday through native bank settlement and saved results is the next missing
outcome in the selected fare. Integrated fare, physical trunk input, long road
travel, passenger-held luggage while driving and Steam/two-PC acceptance are still
open. No saved mid-fare luggage state has been introduced.

Reassessment: the bounded luggage dependency is closed. No new report displaces the
unfinished payday outcome; grocery/stutter reports and other gameplay checkpoints
retain their recorded limits. Finish the selected fare, then rotate to another
missing ordinary loop. Optional luggage polish should not extend this category.


## Taxi payday dependency (2026-09-14, v245 unreleased)

The bank balance already replicated native payday credit; the missing guest output
was the salary letter and its eight-row rundown. These now use the service snapshot,
with a separate authenticated report acknowledgment on native guest sheet close.
A native zero-wage branch left the previous net pay on the sheet; the host now copies
clamped Money into the correct report cell on that branch. Native wage calculation,
bank/net-income mutation and save ownership are preserved.

**16 live + four cold-load native checks** passed: meter-off gating; positive and zero
wages; shared balances and report rows; no duplicate credit; native guest reading;
disconnect/rejoin restoration; stale/distant acknowledgment rejection; host save and
cold reload of paid balances, cleared ledgers and unread report. **4,972 Net tests**,
**18 launcher tests**, clean Core/probe builds, and unchanged 18 personal/15 guest
files are recorded. [Evidence, payload distinction and fixture limits](BUILDING.md#shared-taxi-payday-2026-09-14-unreleased-v245).

This supersedes the missing-payday output above. **J06 remains Partial**, and the
inventory remains **82 areas: 22 Candidate / 51 Partial / 6 Missing / 3 Review**.
Integrated fare acceptance, physical controls, a full week of clock progression,
Steam/two-PC and saved mid-fare progress remain open. Guest bank statement history
was not added by this task.

Reassessment: the bounded payday outcome is closed. The required final connection
check for the selected fare comes next, then rotate to **H04 household fuse
installation**, using the shared I05 loose fuses. Existing grocery/stutter evidence
keeps its limits; there is no new report requiring another performance cycle.
Optional taxi presentation polish does not justify staying in this category.


## Connected taxi fare closed; household fuses next (2026-09-14, v245 unreleased)

One host-native call now has connected controlled evidence through its guest answer,
customer/suitcase creation, guest pickup and boarding, loaded short travel, arrival,
quote/cash, requested receipt printing/carrying/handoff, unloading, earned payday,
guest salary reading, host save and cold reload. The 26.509 MK fare and 0.1 receipted
work km feed native payday directly; no separate earnings ledger is seeded. The
resulting 10.6036 MK wage and read report survive reload without another payment.

Log review found a late meter packet reaching a destroyed knob during menu return.
Taxi message handlers now reject native work outside GAME. A deterministic native
callback verifies that a newer meter packet leaves the cached destroyed binding
untouched before world cleanup. **17 live + four cold native checks**, **4,972 Net
tests**, clean Core/probe builds, unchanged 18 personal/15 guest files, and identical
accepted payloads are recorded. [Full evidence and controlled inputs](BUILDING.md#connected-taxi-fare-2026-09-14-unreleased-v245).

This supersedes the open integrated-fare dependency above. **J06 remains Partial**;
**82 areas: 22 Candidate / 51 Partial / 6 Missing / 3 Review** is unchanged. Physical
input and driving, hiring/tutorial/outbound phone, player taxi passengers,
Steam/two-PC and saved mid-fare progress remain open. Existing Corris protection,
missing-template and checksum diagnostics are retained as separate limitations.

Reassessment closes this selected fare journey and rotates to **H04 household fuse
installation**. Start from the already-shared loose fuse and inspect native holder
insertion, tightening, power consequences, removal and save/rejoin. Main-switch and
stove Fuse flags do not implement those holders. No new grocery/stutter report
requires displacing missing household gameplay with another performance cycle;
optional taxi polish and additional synthetic fares should wait.


## Household fuse checkpoint and rotation (2026-09-14, v246)

H04 moves from Missing to Candidate. The seven house and four apartment holders
share persistent identities, inserted fuse condition, native tightness, slot/loose
state and circuit flags. Guests consume one real I05 shared fuse, fit/remove and
turn holders through host-validated intents. The native host writes power and saves;
guest copies cannot write their own holder saves or independently blow fuses.

The **25-assertion native workflow** covers both homes, duplicate/stale/distant/
forged requests, competing pickup, guarded guest save, native host save and rejoin.
**Nine cold-load/regression assertions on the final build** cover saved repairs,
blown and loose holders, consumed fuse retirement, guest originals, correct host
shock routing, native PART pickup in both roles, refused cross-home fitting and
successful subsequent same-home fitting. The cold check exposed and fixed a real
classification bug: vanilla restores a loose holder's old scene parent, so positive
tightness is required before treating it as installed. Builds are clean; **4,988
Net and 18 launcher tests** pass. See [the checkpoint](BUILDING.md#household-fuse-replacement-2026-09-14-unreleased-v246)
for exact payload differences, fixture boundaries, logs and save integrity.

The current inventory is **82 areas: 23 Candidate / 51 Partial / 5 Missing /
3 Review**. Counts are unweighted implementation categories, not a release-readiness
percentage. Physical aiming/input, every circuit and powered consumer, automatic
overload/fatal shock and Steam/two-PC acceptance remain open. Cross-home holder
fitting is intentionally refused because vanilla retains the original database.

At this completed boundary, the earlier bag-duplication and stutter reports have no
new reproduction that displaces missing gameplay. Car assembly, supplies, jobs and
household checkpoints retain their limits; the new fuse ownership path now has
both pickup-order checks. Rotate next to **V13: one shared tractor-trailer coupling
and release journey**. Inspect native connection and persistence before implementing
it, and keep towing ropes/other implements as separate bounded work. Do not extend
fuse polish or test tooling by inertia.

### 2026-09-14 — tractor-trailer coupling checkpoint (v247, unreleased)

V13 moves from Missing to Partial. The selected automatic-attach → drive/delegate →
release/rearm → rejoin → host-save/reload journey has an implementation and
controlled native evidence: 17 warm assertions and 7 cold assertions on matching
production payloads. The existing vehicle registration's mass fallback did include
FLATBED, but only as an independent chassis; it did not synchronize the native
connection or the bed/support graph. That fallback now excludes the dedicated
trailer, and a coupled parked tractor retains host physics authority even when the
host walks away. Guest originals and all 18 protected personal files remain intact.
The native hook's saved boolean, root transform and two-metre rearm behavior are
preserved. [Detailed evidence](BUILDING.md#tractor-trailer-coupling-protocol-247-unreleased).

This is controlled native testing with aligned/velocity-seeded fixtures, not
physical mouse/steering or Steam acceptance. Trailer-load deliveries, loose cargo
throughout the bed, rear-hydraulic/hatch controls, towing ropes and other implements
are still open. The broad counts are now **82: 23 Candidate / 52 Partial / 4 Missing /
3 Review**. Next is **I09**, the missing sausage-package-to-four-items factory,
so ordinary supplies gain coverage before further vehicle polish.


## Taxi human passenger audit (2026-09-14, unreleased v249)

Fresh native extraction of build 23268598 shows taxi `Functions/MassPassenger` at
approximately `(0.38, 0.447, -0.964)`: this is the rear-right fare seat, not shotgun.
`Customer1/TaxiWalker::Logic.CarMassPassenger` references that exact object.
The front-right seat mirrors `Functions/MassDriver` across the cabin; rear-left
mirrors the customer mass point. The front offset matches the existing passenger
camera/body policy. Catalog `taxiPassengers` carries the exact root, mass points,
driver trigger and tutorial path. Missing metadata or anchors leave taxi seats
unavailable without inventing a fallback layout.

Seat indices 0/2 are human; index 1 is always reserved. This permits a driver, two
friends and the customer. The native tutorial temporarily blocks human seats.
The existing host ledger retains authenticated proximity checks, request order,
seat conflicts, moving keepalives, cleanup and late-join replay. Guests do not
become drivers merely by sitting in the taxi. The controlled native journey covers
a real customer boarding beside a human rider, but uses fixtures for proximity,
entry commands and short car displacement; it is not a physical road test.

V04 stays Partial. The selected taxi slice is closed; the subsequent H10 coffee
checkpoint below records the next household slice. Session
reliability and prior bag/performance reports remain on the wider validation list.
No new blocker from them emerged in this bounded run. See the
[validation record](BUILDING.md#taxi-human-passengers-protocol-249-unreleased).


## Home coffee checkpoint — protocol250

The home pot/cup and native grounds packets now have one host authority. Native
water and grounds filling/brewing update shared quantities; cup transfer conserves
water and contents. The host validates the holder, player position and sequence,
then empties the cup once before the accepted drinker runs native personal effects.
Packet identity follows native `groundcoffee0N`; household save tags preserve
identity when carrying/dropping changes the scene path. Vendor cups are excluded.

Twenty controlled native journey checks plus seven cold-reload/host-drink checks
cover the implementation, including reconnect after using the cup and native
persistence of the recipe, partial cup and packet identities/contents. The
[validation record](BUILDING.md#home-coffee-preparation-and-drinking-protocol-250-unreleased)
records cold reload and the difference between native-state fixtures and actual
mouse/hand acceptance. H10 remains Partial for vendor/vending coffee; adding
messages and passing tests does not imply the entire coffee family is complete.

The selected household coffee slice is closed. Next is **W02 train motion and
collision/reset state**: it is the remaining wholly missing shared world hazard.
Partial engine assembly, job pipelines, supply families and ordinary session
validation still dominate the larger backlog. This selection does not imply the
mod is nearly finished or that the 24 Candidate rows measure percentage completion.


## Train motion and shared hazard (2026-09-14, v251 unreleased)

The native audit confirms seven FSMs on one moving train graph: Move, Reset,
Player, TunnelAudio, Whistle, Lights Switch and WhistleTrigger/Raycast. The eleven
colliders belong to one dynamic Rigidbody. Move alternates two travel legs at
30 m/s and two 250-second waits; invisible waits still retain root Coll. The body
reparents between the two spawn nodes, whose child and root share the name TRAIN.
There are no train-owned native save tags.

`TrainSync` supplies host state and reversible guest suppression, leaving native
local-player comparison/death active. Exact root lookup and full Transform/physics
restoration were necessary. Kinematic replicas failed actual guest player contact;
dynamic replicas with native constraints now preserve it. Twenty final controlled
native checks, 5,051 Net tests and 18 launcher tests pass; the
[train validation record](BUILDING.md#shared-train-and-collision-lifecycle-protocol-251-unreleased)
separates actual guest contact, host comparison entry and unverified physical/audio
acceptance. Personal files and guest profiles remain unchanged.

This closes W02's implementation gap, not the broad alpha. **54 Partial rows still
contain missing or unresolved links.** Reassessment selects I05 light-bulb boxes:
shared opening, exact remaining count, generated bulb identities and native host
persistence/rejoin. This extends ordinary supplies after closing the world hazard;
R20 boxes and bulb installation remain separate bounded work. Existing bag/stutter
reports remain acceptance items without a fresh blocker displacing missing gameplay.


## Bulb boxes closed; advert delivery next (2026-09-14, v252 unreleased)

Native audit: `Spawner/CreateItems/LightbulbBox` creates `lightbulbbox0N`, whose
Use FSM holds Quantity=1. Create Plug sends one SPAWNITEM to Lightbulb and transitions
directly to Empty; it has no IntAdd, Check quantity or Delay. Empty writes zero and
native save deletes the consumed box. The loose `lightbulb/Data` prefab randomizes
Wear at initialization, then idles in State 3. Its factory has only CreateObject
and SetFsmFloat; **no ID, counter, load, save or deletion tags exist for loose bulbs**.
The earlier suggestion of persistent bulb contents was incorrect and is superseded.

The host captures exact outputs. A box output uses a box-derived identity, other
loose outputs use session ordinals, and guests bypass condition randomization.
Native movement uses existing item ownership. Join/full/targeted snapshots carry
BulbState, and retirement prevents stale recreation. Guest originals are hidden
and restored; only replicas are destroyed at teardown. Unopened boxes alone
survive cold reload, matching the game. Fitting/removal and installed Wear/light
state are separate work; raw guest ItemDespawn cannot delete host bulbs.

Twenty-one controlled native assertions, 5,063 Net tests, 18 launcher tests and
clean game-reference builds pass. The [validation record](BUILDING.md#light-bulb-boxes-and-shared-contents-protocol-252-unreleased)
separates native-state entry/packet fixtures from physical input acceptance.
I05 stays Partial for R20 batteries; totals remain 25 Candidate / 54 Partial /
0 Missing / 3 Review. These counts do not measure percentage completion.

Reassessment: prior bag/stutter reports still need physical/Steam acceptance,
engine assembly and transfers have missing links, and ordinary jobs still lack
concrete actions. This bounded supplies task is closed. **Next: J09 advert delivery**,
starting with current native reachability and exact mailbox/remaining-sheet/save
accounting. This fills a different gameplay loop instead of expanding bulb work
into fittings, R20 batteries or more soak tests by inertia.


## Advert delivery closed; engine-oil transfer next (2026-09-14, v253 unreleased)

The native job uses Data's 28-entry saved boolean list and 27 reachable mailbox
BoxIndex values; index22 is absent. Identical mailbox paths need native indices,
not scene ordinals. The pile contains 30 sheets. Open decrements once and creates
an FSM-free `advert(Clone)`; mailbox Open destroys that sheet and Close updates
the saved flag plus Delivered. Friday native payment multiplies Delivered by 17,
clears the count and records Jakopalkkio in the bank ledger.

The new adapter captures exact outputs, reserves owned nearby sheets for native
host delivery and shares the resulting list/quantity. Guest native job/reset/pay
writers are paused; presentation keeps native hatch animation/audio without
local destruction/accounting. Native hand ownership/release prevents two holders
and clears consumed sheets. Mailboxes inside house/store LOD are temporarily
parented outside it: the host's camera must not decide whether a distant guest
can deliver. Other house/NPC LOD remains native; original mailbox parents restore.

Controlled native checks cover both delivery roles, guest-only visits to a
previously unloaded mailbox, exhaustion, replay/authentication, held-sheet teardown,
rejoin and host save/cold reload. The native bank ledger distinguishes advert pay
from unrelated Friday benefits and scheduled rent. The
[validation record](BUILDING.md#advert-delivery-and-native-payday-protocol-253-unreleased)
keeps exact evidence, production payload hashes and test limitations. Native save
retains mailbox flags/Delivered/Stage/pile amount/pose, but does not save loose sheets.

**J09 stays Partial.** Native 08231206.CALLED sends JOB to the host job root, but
PhoneSync has only incoming-call relay and no shared guest outbound enrolment.
That is a missing interaction, not merely an untested claim. Controlled fixtures
enter native New job and advance the day; ordinary telephone input and its
600-second scheduling wait, mouse aiming, complete routes and Steam/two-PC have
not been accepted. Totals stay 25 Candidate / 54 Partial / 0 Missing / 3 Review;
they are not a percentage-complete measure.

The selected delivery/payout slice is closed. Reassessment keeps the prior
bag/stutter reports as acceptance items and the shared outbound-phone dependency
in the wider job backlog. **Next is I12 Corris engine-oil refill**: audit one
ordinary source-to-engine transfer and its cap, finite remainder, destination
amount and native persistence. This rotates from jobs to missing maintenance
rather than extending delivery tooling or unrelated performance work.


## Engine-oil containers; required refill dependency remains (2026-09-14, v254)

The native audit found that MotorOil is a separate saved-item factory, not a bag
product manifest. MOil1/2/3 select Type 0/1/2 on one `motormoil1` prefab. Native
materials are motoroil1/2/3 and viscosities 0.5/0.6/0.7 in build23268598; runtime
uses the native material table and host viscosity, not hardcoded grade values.
Bottles hold four litres and save transform/Type/Fluid under their own IDs.
The container foundation now shares those IDs, grades, quantities and emptiness;
guest-local original bottles restore without replaying native startup.
See the [native record](BUILDING.md#motor-oil-containers-protocol-254-unreleased).

**I12's actual refill is still missing.** The native cylinder-head rocker-cover
cap exposes `CapTrigger_MotorOil`, whose trigger resolves a separate removable
oilpan. Filling consumes bottle Fluid at 0.1 litres/second, adds pan Oil at the
same rate up to 3.7 litres, reduces OilContamination at 3.2/second, and moves
OilViscosity towards the bottle by 0.06/second. The persistent part stores
OilLevel/OilDirt/OilViscosity; the mounted proxy uses Oil/OilContamination/
OilViscosity. A shared transfer must validate the attached pan, update both
representations and conserve the source/destination pair. It must not only
replicate an engine gauge or animate the bottle. Guest pour colliders are disabled
until that path exists; host-native pouring remains untouched.

Reassessment keeps this unfinished refill as the next task because it is the
required dependency of the selected ordinary maintenance journey. I04/I12 remain
Partial and overall counts do not change. Other separate shop supplies, outgoing
phone calls, prior bag/stutter acceptance, physical input and Steam/two-PC testing
remain on the wider roadmap.


## Corris engine-oil cap and refill (2026-09-14, v255 unreleased)

The selected I12 path now resolves the **host's installed** Corris block, head,
rocker cover and oilpan. Both players operate the cap. Guest pours require current
item ownership, a nearby living player, fresh approved source pose, native angle
and collider overlap. The host alone updates finite source/pan oil and native
cleanliness/viscosity, including each part's saved mirror. Removed targets advance
an epoch; stale/replayed requests cannot act on the replacement. Guests project
the cap and local gauge, preserve personal parts and rebuild shared results on join.

Repeated native cold loads exposed emptied/partial bottles reverting to four
litres. The host Save getter now reads a live source or retains root Fluid after
trigger destruction, while native save tags and remaining actions stay intact.
That save guard alone did not fix the regression: a nearby child trigger could copy its
default quantity and send GLOBALEVENT during the parent's one-second Load wait;
startup now pauses that child and gates the event until the native handoff finishes.
The oilpan's independent drain plug remains
native: below tightness7 it drains .2 L/s, faster than the .1 L/s refill rate.
That explained an exploratory fixture losing oil during a valid pour.

[Controlled two-game evidence and limits](BUILDING.md#corris-engine-oil-refill-protocol-255-unreleased)
record 45 passing final-payload checks for refill, cap, authority/replay,
removal/rejoin and repeated native save/cold loads. The selected slice is closed. They
do not establish ordinary mouse use, detached engines, a complete physical engine
build or Steam/two-PC acceptance. **I04/I12 stay Partial** for the remaining supply
families and transfers. Counts remain **25 Candidate / 54 Partial / 0 Missing /
3 Review**, not a percentage-complete measure.

Reassessment: completing this finite maintenance journey removes one missing
normal action. The next bounded task is **J09 guest outgoing telephone enrolment**
for the advert job: delivery/pay are shared, but guests still cannot start the job
through its normal call. Prior bag/performance reports remain acceptance gaps;
no fresh failure in those areas displaces this missing gameplay entry point.


## Advert telephone enrolment checkpoint — protocol 256, 2026-09-14

The missing ordinary start action for J09 now has a dedicated host path. Both
players dial 08231206 using the native phone; its ringing ends at a host approval
step. The host requires the original available listing and waiting job, a nearby
caller, an idle line and connected/paid household service. One enrolment is
reserved across the three native phones. The native 72-second speech/subtitle
plays on the caller, while connection and elapsed usage accrue only on the host.
The taxi carphone has no native household charge. Hanging up, losing service,
leaving range or disconnecting cancels enrolment while retaining incurred usage.
Only completion after the host's full duration dispatches the native CALLED/JOB.

Native evidence identified two similarly named old-house handle FSMs: only the
one with the matching Keypad reference controls outgoing calls. A controlled
host/guest test also exposed synchronous host replies racing PlayMaker's queued
state transition. Requests now wait until the native approval/completion state
is entered. A second timing edge required retaining a near-complete native result
until the host's full duration, without allowing an early start. Guest listing availability is not trusted: even a guest whose own
save has already removed the number can use the shared host availability.
Native listing Stage 3/numberdisabled and job stage 1 use existing host save tags;
ongoing calls are cancelled and must be restarted after reconnect. The advert
job keeps its native initial 600-second wait and Tuesday/Friday schedule.

[Validation and remaining limits](BUILDING.md#advert-telephone-enrolment-protocol-256-unreleased)
separate native state/input probes from manual interaction and Steam acceptance.
J09 is now Candidate under the experimental-alpha definition, with no identified
missing action/result/save link in this named slice. This does not establish a
fully playtested delivery route. Counts are 26 Candidate / 53 Partial / 0 Missing /
3 Review, still an unweighted inventory rather than a completion percentage.
W04 incoming-call arbitration and other outgoing numbers remain separate.

Reassessment: prior bag/stutter reports still need tester acceptance, and full
assembly, other supplies, household equipment and several jobs retain missing
links. The next bounded task is **I05 R20 battery boxes and their loose contents**:
it closes the last missing output family in the ordinary shop-box row and rotates
from jobs to supplies. Battery-powered appliances/installation (H12) need a
separate native audit; call polish and long soak tests will not displace missing
normal gameplay.

The v256 carphone integration also restores idle local keypad/hand presentation
without guest writes to native taxi Answer/Occupied. Incoming taxi calls retain
the existing authenticated answer/hangup path and close the outgoing keypad;
loss of taxi-phone availability cancels outgoing use and releases local movement.
This resolves the earlier taxi presenter unconditionally hiding the keypad.

## R20 battery contents checkpoint — protocol 257, 2026-09-14

I05 now has a candidate implementation for all three named contents families.
Native `R20BatteryBox` holds four cells; `R20Battery` assigns persistent
`r20battery0N` identities. Unlike loose bulbs, these cells save their transforms.
They have no native charge scalar. Their single Consumed flag deletes their save
on disposal; fuses instead retain a separate Destroy flag because fitted fuses
must remain saved. The existing supply adapter validates both native layouts.

The shared package ledger ties each extraction to one decrement and one native
output, with existing actor/range/ownership and replay checks. The host owns
creation/disposal and native saving; guest originals restore on disconnect.
**32 controlled two-game checks pass**, including guest shop payment/bag unpacking,
both opening roles, one holder after competing pickup, replay rejection, native
host disposal, reconnect and cold saved identities/quantities. See
[validation and limits](BUILDING.md#r20-battery-boxes-and-persistent-cells-protocol-257-unreleased).
Manual controls, guest disposal, fitting/appliance use and Steam/two-PC remain
unverified here; fitting/appliance implementation is H12, outside this box slice.

Current inventory: **27 Candidate / 52 Partial / 0 Missing / 3 Review**. These
unweighted counts do not measure percentage complete. Reassessment still finds
missing gameplay links in vehicles, survival, supplies and jobs, alongside prior
bag/stutter tester acceptance. The next selected task is **V11 guest window
scraping**: audit native tool/contact behavior and make scraping change the same
vehicle's frost for both players. This rotates from supplies to a missing action
needed to use an iced-over vehicle. Battery appliance use and further box polish
remain queued.
