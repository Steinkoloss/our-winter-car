# COVERAGE-ROADMAP.md — closing every unsynced vanilla system

This is a **work queue for an AI agent**. Its original tasks decompose the gaps found in
the 2026-07-21 full-coverage audit (all 8,615 game FSMs vs. the mod's sync surface) into
ordered tasks. The current scope audit and user objective below determine priority;
reassess at task boundaries and select one bounded player outcome per task.

`PLAN.md` §4–5 remains the architecture + milestone source of truth. This doc is the
*detailed decomposition of its "gameplay long tail + M11 polish + parity backlog"* — the
concrete, per-system tasks PLAN doesn't spell out. When a task lands, tick it in the
**Progress** list and, if it changes a PLAN §4.4 status, update PLAN in the same PR.

**Current objective (2026-09-14): a broad experimental alpha where every normal
shared gameplay loop has a plausible implementation, with bugs expected.** Read the
[full sync-scope audit](SYNC-SCOPE-AUDIT.md) before choosing work. It checks the
current code against the native inventory and separates candidate implementations,
missing links, unclassified native content and later validation. It supersedes
older whole-feature completion claims below. The ordinary-session checkpoint is
retained evidence; it is not the whole release scope. The bounded home-stove and
Corris ATF refill slices have controlled native evidence through protocol235.
The bounded chips listing/sale journey also has native evidence through v237.
Fuse-box buying/opening, shared loose fuses and remaining contents now have
native save/rejoin evidence through v238; the Corris ignition-wire journey has
evidence through v239. Shared taxi calls and customer presentation are implemented
in v240, duty/meter in v241, and arrival/quote/cash with collected-income save/reload
in v242. Protocol243 adds shared receipt printing/carrying/handoff. The selected
complete fare now also has shared luggage in v244 and native payday/report/save
evidence in v245. The connected fare-to-earned-payday/save journey now has controlled
native acceptance evidence on v245. Protocol246 now adds household fuse replacement
in both homes, including host-native circuit state, exclusive pickup and save/rejoin.
H04 is Candidate. Protocol247 closes the selected tractor-trailer coupling journey
with 24 controlled native checks, including save/rejoin and host/guest physics
handoff. V13 is now Partial; towing ropes, other implements and physical/Steam
acceptance remain open. Protocol248 now adds I09: eight sausage conversion graphs,
four shared outputs, host food state and native package persistence. I09 is
Candidate; unopened food freshness keeps I08 Partial. Protocol249 adds V04 taxi
human passengers: front right and rear left, with rear right reserved for fares.
The selected taxi seating task is closed; V04 remains Partial for other appropriate
vehicles. Protocol250 now adds the H10 household coffee slice: pot/grounds,
brewing, conserved cup filling, personal drinking and reconnect. Vendor coffee
keeps H10 Partial. Protocol251 now closes **W02 train motion and shared collision/
reset state**, including native guest-player contact. The train slice is Candidate.
Protocol252 closes the selected **I05 light-bulb box/contents** slice, with 21
controlled native checks. Unopened boxes persist; loose bulbs are session-only
in vanilla. I05 remains Partial for R20 batteries. Protocol253 adds the selected
**J09 advert-delivery/payout slice**: exact mailbox results, shared remaining sheets,
host day/pay routines, reconnect and native saved progress. Its missing telephone enrolment is supplied by v256 below. Protocol254 adds the necessary engine-oil
container foundation: all three grades, saved identities, quantities/materials,
exclusive pickup, guest purchase and reconnect. Protocol255 adds **I12 Corris
engine-oil cap and conserved refill**, including the mounted/saved pan's oil,
contamination and viscosity. Native empty-bottle saving is protected after trigger
destruction. Its 45 controlled native checks close this selected slice. Protocol 256
adds **J09 guest outgoing telephone enrolment** and shared call charges. J09 is now
Candidate; v257 below now closes **I05 R20 battery-box contents**.
Household coffee variants, bulb fitting and further train polish remain queued.
Consult the checkpoints below for the remaining limits.


**R20 battery checkpoint (v257, local/unreleased):** four-cell boxes and persistent
loose batteries now use the shared package/supply path. Native IDs, exact remaining
quantities, guest purchase/bag unpacking, both opening roles, pickup arbitration,
host disposal, reconnect and cold save/reload pass **32 controlled two-game checks**.
R20 disposal deletes the native Consumed item; fitted fuses retain their separate
Destroy semantics. Loose cells have no native charge field. Core/probe builds
cleanly; **5,117 Net tests and 18 launcher tests pass**.
[Evidence and limits](BUILDING.md#r20-battery-boxes-and-persistent-cells-protocol-257-unreleased).
I05 advances to Candidate: **27 Candidate / 52 Partial / 0 Missing / 3 Review**.
Manual controls, guest disposal, battery fitting/appliance operation and Steam/two-PC
remain unverified. Fitting/appliance implementation remains H12.
**Next: V11 guest window scraping**, starting with its native tool/contact behavior
and frost ownership. This addresses another missing ordinary action after closing
the shop-box contents group; other supplies and long soak tests remain queued.

**Advert telephone checkpoint (v256, local/unreleased):** the apartment, old-house
and taxi phones now route advert enrolment through a single host reservation.
The caller hears the native 72-second speech; only the host bills household calls
and starts the job. Early hangup, expired connection, service loss, distance,
replay and impersonation cannot enrol. Existing native save tags and shared
advert state retain the result; unfinished calls restart after reconnect.
[Evidence and limits](BUILDING.md#advert-telephone-enrolment-protocol-256-unreleased).
J09 advances to Candidate: **26 Candidate / 53 Partial / 0 Missing / 3 Review**.
Manual keys/mouse, full delivery routes and Steam/two-PC still need acceptance.
The selected next task, **I05 R20 battery boxes and loose contents**, is now
implemented in v257 above.
This closes the selected job-start task; incoming calls and arbitrary outgoing
numbers remain separate work.

**Engine-oil refill checkpoint (v255, local/unreleased):** host and guest cap
controls, owned tilted pours, finite source/capacity, native contamination/viscosity,
removal epochs, replay/sender rejection, teardown and rejoin now have controlled
two-game evidence. Mounted and saved pan scalars update together. A repeated
save/cold-load regression exposed bottle quantities reverting to four litres.
The startup guard prevents source defaults interrupting the native load handoff;
the save hook also retains the root remainder after trigger destruction.
[Evidence and limits](BUILDING.md#corris-engine-oil-refill-protocol-255-unreleased).
The actual drain plug remains native, including its .2 L/s open-plug drainage.
I04/I12 remain Partial; inventory totals at v255 were 25 Candidate / 54 Partial / 0 Missing /
3 Review. The 45 controlled native checks, including repeated cold saves, close this
selected maintenance journey. Its selected next task, **J09 guest outgoing
telephone enrolment**, is now implemented in v256 above. Other resource variants, prior bag/stutter acceptance, full assembly,
physical input and Steam tests remain queued; they do not displace this next missing
player action without a new concrete blocker.

**Motor-oil container checkpoint (v254, local/unreleased; refill superseded by v255):** the separate native MotorOil
factory now supplies one durable bottle identity and the exact Type/material,
viscosity, fluid and empty state. Native host changes, guest shop checkout,
exclusive pickup, stale-state rejection, retirement and reconnect are covered.
Guest original bottles pause and restore their identity/grade/quantity; activation
must precede restoring RestartOnEnable or native Use overwrites ID.
[Evidence and limits](BUILDING.md#motor-oil-containers-protocol-254-unreleased).
This closes the container foundation, **not the selected engine-refill journey**.
I04 and I12 stay Partial; counts remain 25 Candidate / 54 Partial / 0 Missing /
3 Review. Reassessment still finds the missing cap/transfer a required dependency,
so the next selected task was **I12**: the actual head cap, correct attached oilpan, finite bottle and
pan quantities, OilContamination and OilViscosity, and native saved pan fields.
Guest pouring was unavailable in v254; the v255 checkpoint records the authority
path and its tests. Supply variants, performance and soak tests did not displace it.

**Advert checkpoint (v253, local/unreleased):** both players extract and carry
identified sheets from the finite native pile; host Open/Close consumes one sheet,
marks one of 27 mailbox indices and increments Delivered once. Native flags, partial
pile, reset/pay and save/rejoin are shared. The mailbox roots bypass local-camera
house LOD, fixing guest-visible targets being unloaded on the host. The native list
has 28 flags but no mailbox22; loose sheets have no native persistence.
[Evidence and limits](BUILDING.md#advert-delivery-and-native-payday-protocol-253-unreleased).
This closes the selected delivery/payout slice. J09 remains Partial: the native
08231206 enrolment call is reachable on the host, but PhoneSync handles incoming
calls and has no guest outbound enrolment request. Normal telephone/mouse use,
full routes and Steam/two-PC remain open. Counts remain 25 Candidate / 54 Partial /
0 Missing / 3 Review. After reviewing missing gameplay, prior bag/stutter reports,
reliability and usability, **next is I12 Corris engine-oil refill**. It is a missing
ordinary maintenance transfer; outbound calls are a separate cross-job task.

**Bulb-box checkpoint (v252, local/unreleased):** guest purchase/bag unpacking,
host and guest opening, matching one-bulb outputs/condition, native guest pickup,
replay/retirement, guest-original restoration and reconnect/save/reload pass 21
controlled checks. Native `LightbulbBox` contains one bulb and enters Empty directly;
loose `Lightbulb/Data` has no native save routine. Do not add invented persistence.
[Evidence and limits](BUILDING.md#light-bulb-boxes-and-shared-contents-protocol-252-unreleased).
I05 remains Partial for R20 batteries; installed bulbs and actual mouse/Steam
acceptance remain separate. After reviewing bugs, missing actions, reliability,
usability and validation gaps, the next selected task was J09 advert delivery.
The newer advert checkpoint above closes that delivery slice and records the
remaining enrolment/input limits; supply polish and soak tests remain queued.

**Train checkpoint (v251, local/unreleased):** host route/pose, eleven collision
shapes, hidden waits, lights and horn counters now reconstruct on guests and rejoin.
Twenty controlled native checks pass on the final payload, including actual guest
player contact/death, host native death comparison, stale recovery and restoration.
A kinematic replica failed native player collision and was corrected to a dynamic
body with native track constraints. [Evidence and limits](BUILDING.md#shared-train-and-collision-lifecycle-protocol-251-unreleased).
W02 is Candidate; physical crossing/car crashes, audible horns, full respawn and
Steam/two-PC acceptance remain open. No native train save tags exist. The scope is
now 25 Candidate / 54 Partial / 0 Missing / 3 Review; absent actions within Partial
rows still block the broad alpha. The subsequent bulb-box checkpoint closes the selected I05 slice; R20 boxes
remain queued separately.

**Home coffee checkpoint (v250, local/unreleased):** the home pot/cup and native
`groundcoffee01`, `groundcoffee02`, … packets share contents and identities.
Twenty controlled two-game journey checks and seven cold-reload/host-drink
checks pass, including native preparation,
conserved filling, guest effects, replay/sender rejection, teardown/rejoin and
native saving. Cold-reload evidence and limits are in the
[coffee record](BUILDING.md#home-coffee-preparation-and-drinking-protocol-250-unreleased).
H10 is Partial because vendor coffee remains absent. Physical input, tap/stove
placement, varied recipes and Steam/two-PC are unverified. The subsequent W02 train slice is now closed by the checkpoint above.
Prior bag/performance reports remain validation items; they do not displace
this missing gameplay work without a fresh concrete blocker.

**Taxi passenger checkpoint (v249, local/unreleased):** both human seats use
native taxi anchors, while the customer's rear-right seat remains reserved even
when empty. Host/guest driver swaps, short controlled motion, native customer
boarding alongside a human, occupied-seat rejection, reconnect replay, tutorial
availability and exits now have native two-game evidence. Sorbet/Corris retain
three seats. The host checks seat availability on every accepted keepalive; a
host-only tutorial change can take up to that interval to correct a guest.
[Evidence and limits](BUILDING.md#taxi-human-passengers-protocol-249-unreleased).
Physical input, camera comfort, long powered drives, Steam/two-PC and other
vehicles remain open. The newer H10 checkpoint above records the household coffee slice.

**Sausage checkpoint (v248, local/unreleased):** host and guest opening produce
four matching loose sausages and consume one exact native package. Successive
packages use their native IDs, so retirement of one does not delete the next.
Host condition, cooked/burnt/spoiled appearance, eating, retirement replay and
rejoining are shared. Native saves retain the unopened package; consumed packages
stay absent, while loose sausages disappear on cold reload as they do in vanilla.
All eight conversion graphs bind; controlled interactions exercise the apartment
stove. Mouse/hand play, the other seven spots and Steam/two-PC remain unverified.
[Evidence and limits](BUILDING.md#sausage-package-conversion-protocol-248-unreleased).
This closes I09; the newer taxi passenger checkpoint above closes the next selected slice.

**Tractor-trailer checkpoint (v247, local/unreleased):** native proximity attachment,
guest/host release, three-body motion, parked host authority, accepted guest driving,
disconnect/restoration and rejoin now share one connection lifecycle. The host saves
the native attached flag and trailer transform; cold reload restores the joint.
Tests caught competing generic chassis ownership and unowned parked tractor physics;
both are fixed. Seventeen journey checks and seven cold checks pass on the same
production DLLs/catalog, plus 4,998 Net tests and 18 launcher tests. The fixture
aligns native bodies, enters native interaction states and seeds velocity; it is
not a physical steering/mouse test. Loaded road travel, rear-hydraulic/hatch controls,
loose cargo throughout the extended bed, ropes and other implements remain open.
[Evidence and limits](BUILDING.md#tractor-trailer-coupling-protocol-247-unreleased).
The selected coupling task is closed; I09 sausage contents is covered by the newer checkpoint above.

**Taxi receipt checkpoint (v243, local/unreleased):** guests can print, take and
carry the single native paper, then give it to a requesting customer. The host owns
receipt accounting and native handoff/return; nearby guests now keep the departing
customer visible long enough for that return to finish. **28 controlled native
two-player assertions**, **4,951 Net tests** and **18 launcher tests** pass, including
loose-paper rejoin, exclusive pickup, duplicate rejection and cold native reload of
nonzero income/receipt/distance totals. Core/probe build with zero warnings/errors;
18 personal files and 15 guest world files remain unchanged.
[Evidence and limits](BUILDING.md#shared-taxi-receipt-2026-09-14-unreleased-v243).
**Next: luggage identity and transport, then full payday and integrated fare
persistence.** These remain dependencies of the selected one-fare journey; J06 stays
Partial. Physical input, role reversal, Steam/two-PC and saved mid-fare paper remain
unverified or absent. Finish this loop before rotating; optional taxi polish does
not displace other missing ordinary gameplay.


**Taxi luggage checkpoint (v244, local/unreleased):** the five usable native pieces
(three suitcases, beer case and mattress) share host selection, identities, carrying,
vehicle cargo, reset and rejoin. The unused rifle-bag reference has no Rigidbody;
this corrects the earlier six-piece inventory assumption. Native count draws are
bounded at the five valid choices. Reset replaces identities, releases hands/cargo
and restores collision settings. **17 controlled native two-player checks**,
**4,965 Net tests** and **18 launcher tests** pass; Core/probe builds have zero
warnings/errors. Personal and guest world files remain unchanged.
[Evidence and limits](BUILDING.md#shared-taxi-luggage-2026-09-14-unreleased-v244).
**Next: full payday through native bank settlement and saved results, then integrated
fare acceptance.** J06 remains Partial. Loading and the short loaded-car move are
controlled fixtures; physical trunk input, long travel, Steam/two-PC and mid-fare
luggage persistence remain open. Close the selected fare, then rotate to another
missing ordinary gameplay loop.

**Taxi payday checkpoint (v245, local/unreleased):** host-native wages reach the
shared bank/net income once, clear settled fare/receipt/distance totals, and retain
the salary report through save/reload. Guests can read the same native sheet and
acknowledge the current report. A zero-pay native report bug no longer displays the
previous wage. **16 live + four cold-load native checks**, **4,972 Net tests** and
**18 launcher tests** pass. Builds are clean; personal and guest world files remain
unchanged. [Evidence and limits](BUILDING.md#shared-taxi-payday-2026-09-14-unreleased-v245).
This supersedes the payday gap in earlier checkpoints. The integrated acceptance
dependency is now closed by the next checkpoint. J06 remains
Partial. Payday inputs and native state entries were controlled fixtures; physical
input, a week of clock simulation, Steam/two-PC and saved mid-fare progress remain
open. Optional taxi polish must not prolong this category.

**Connected taxi fare checkpoint (v245, local/unreleased):** the same incoming call
produces the customer and suitcase, native arrival and cash, requested receipt, and
the earned ledger that pays the shared bank. The guest reads that salary report;
native save/cold reload retains the wage and read flag without paying again.
**17 live + four cold native checks** and **4,972 Net tests** pass; Core/probe builds
are clean. Log review found and fixed late taxi packets reaching destroyed objects
during menu return; a deterministic native check covers that exact timing.
[Evidence and fixture limits](BUILDING.md#connected-taxi-fare-2026-09-14-unreleased-v245).
The selected controlled fare integration task is closed. **Next: H04 household fuse
installation, tightening, circuit effects, removal and host save/rejoin.** J06 stays
Partial: physical driving/input, Steam/two-PC, hiring/tutorial, passenger seats and
saved mid-fare state remain gaps. More synthetic fares or optional taxi polish do
not displace this rotation to missing household gameplay.

**Household fuse checkpoint (v246, local/unreleased):** both homes now share loose
fuse consumption, holder installation/removal, tightening and native circuit flags.
Guests keep their own saved holders isolated, and shock checks run on the player
turning the screw. **25 native workflow assertions + nine cold-load/regression
assertions**, **4,988 Net tests** and **18 launcher tests** pass. Cold testing caught
and fixed a removed holder retaining its old scene parent: zero tightness remains
loose after loading. The final native PART pickup gate and both roles' refused
cross-home fitting are covered by the cold run.
[Evidence, payload differences and fixture limits](BUILDING.md#household-fuse-replacement-2026-09-14-unreleased-v246).
H04 is Candidate, not physical/Steam acceptance. **Next: V13 tractor-trailer
attachment and release**, auditing the native connection/save behavior first.
Towing ropes and other implements remain separate slices; optional fuse polish,
more synthetic repair cycles and performance work do not displace this rotation.

---

## 0. How to use this (read once)

**Loop:** reassess priorities → select one observable player outcome → check the
existing behavior → implement only the missing behavior or demonstrated fix → run
the required checks → record evidence and remaining limits → clean up and reassess.
Commit or publish only when requested. Do **not** batch unrelated tasks.

At each completed task boundary, review user-reported bugs, missing gameplay,
reliability, usability and acceptance gaps across the project. Rotate topics instead
of extending the last category by inertia. Shared-state corruption still takes
priority; continue in the same area only for an explicit user request or a concrete
blocker, dependency or failing check, and explain that choice. An unchecked item can
need gameplay verification rather than more implementation; select accordingly.

### Current playable checkpoint — one ordinary co-op session (2026-09-12)

Two players join, buy and unpack groceries, drive together, sleep, then save and
resume the host's world. This is a small acceptance step toward PLAN's M7, **not**
completion of its four-player winter/soak gate or of the mod as a whole.

| Step | Pass condition | Evidence so far / remaining work |
|---|---|---|
| Join and retry | Guest joins through Steam; a failed attempt permits a successful retry without restarting or damaging their save. | Local native save/join probe: 32 checks passed, including a silent UDP host and retry. Real Steam/two-PC and physical friend-picker checks remain open. |
| Shop and unpack | Both roles can buy, exclusively hold and open a bag; both see each item once and the same payment. | Local two-game test: 19 checks passed with 9 groceries retained. v214 also verifies saved groceries and a partly full bag after restart (see Save and resume). Real Steam acceptance remains open. |
| Drive together | Driver and passenger travel together, swap seats/driver, and exit with consistent car position and seat occupancy. | v212 fixes the blocked seat after exit. v213 restores Sorbet engine speed, gear and exact temperature on driver handoff, and rejects stale RPM after stopping. **24 local two-game checks pass:** powered travel with a passenger in both roles, stable idle, running handoff both ways, OFF/ACC-only takeover and clean exits. This closes the local Sorbet handoff task; physical controls, other cars, long drives and Steam/two-PC acceptance remain open. [Evidence and limits](BUILDING.md#sorbet-running-engine-handoff-2026-09-13-unreleased-v213). |
| Sleep and time | Agreed sleep completes with consistent world time and each player's expected rest; stay-awake/cancel/disconnect paths leave usable controls and no stuck wait. | v211 fixes cancellation, departing guests and answer membership. Local two-game run passed 12 checks, including native sleep from 10:00 to 13:00 on both clocks. Physical bed/prompt input, other beds and real Steam/two-PC acceptance remain open. |
| Save and resume | After host save/quit/reload and guest rejoin, the purchased items, remaining bag contents, money and player profiles resume correctly; the guest's personal world is untouched. | **19 local two-game checks pass** across native save and restart/rejoin stages (v214): shared cash, saved loose groceries, a partly full bag, remaining-only opening, both returning spawn choices and unchanged guest world files. Loading/save teardown no longer overwrites the guest profile; saved loose products reconstruct on peers. Separate disposable profiles and a probe-only stable UDP identity were used. [Evidence and limits](BUILDING.md#save-and-resume-2026-09-13-unreleased-v214). The combined v220 local journey is recorded below; real Steam/two-PC and physical input remain open. |

Checkpoint acceptance requires the whole journey on one identified build with two
PCs/Steam accounts and a disposable host save, including reversing bag-holder and
driver roles. Record PASS / FAIL / NOT TESTED per step, actual build/protocol and
platforms, plus the reproduction and both peers' diagnostics for a failure.
Local probes, automated tests and real Steam playtests are separate evidence;
none silently substitutes for another. Previous local evidence is in
`build/join-recovery-audit/`, `build/shop-bag-live-audit/` and
`build/sleep-consent-audit/`, `build/shared-drive-audit/` and
`build/engine-handoff-audit/` and `build/persistence-audit/` (ignored local artifacts).

**Cylinder-head placement checkpoint (v216, local/unreleased):** host fitting now
moves the guest's persistent head to the block and host removal restores loose
item motion. All 21 child mount positions agree, and all eight valves accept
nearby guest turns at the fitted head. Saved guest AssemblyID, condition and valve
values stay protected. Validation: **4,365 protocol tests, 140 native checks and
25 local two-game assertions**. Native checks include restoring originally loose
and fitted guest heads. This closes the placement blocker found in v215; guest
head-install/remove intents, guest fastening bolts, missing/different heads and
full child-part parity remain open. [Evidence and limits](BUILDING.md#cylinder-head-placement-2026-09-13-unreleased-v216).

**Firewood payment checkpoint (v217, local/unreleased):** one prepared 500 mk
offer paid 5,000 mk when the original host received ten quick guest requests.
The host now reserves an available native offer before collection, refuses repeat
or inactive requests, and alone adds cash and net income. Guest native payment
entries cannot add either balance, and snapshots never replay payment actions.
**4,381 Net tests, 61 native checks and 9 final local two-game assertions pass.**
This closes payment duplication; it does **not** establish the full firewood job
loop. Buyer visibility, pending-offer amounts, delivery/completion and signed
penalty reconciliation still need work. The local driver seeds an offer and enters
the native collection state; physical input and Steam acceptance remain open.
[Evidence and limits](BUILDING.md#firewood-payment-2026-09-13-unreleased-v217).

**Milk condition checkpoint (v218, local/unreleased):** the same saved carton
previously decayed independently on each peer. The host now owns its condition
and native spoiled phase; guests suppress warm/fridge decay, wait for the host
before drinking and apply the native spoiled label. Host decay remains native,
including the slower fridge rate. Periodic state, join snapshots and targeted
resync repair drift. **4,393 Net tests, 40 native checks and 14 final local two-game
assertions pass**, including disconnect/reconnect restoration. This is milk-only; other foods, cooking and changes to fridge
electricity remain open. [Evidence and limits](BUILDING.md#milk-condition-2026-09-13-unreleased-v218).

**Guest head interaction checkpoint (v219, local/unreleased):** guests can now
request fitting/removal of the persistent VIN1110 head. Requests use the existing
part receipt ledger and message 207 revision; the host validates ownership,
proximity, tightness and the native mount prerequisite chain. Guest saved Data
and fastening bolts stay protected. Native pickup, host fitting/removal and
observer placement pass **4,408 Net tests, 34 native checks and 12 local two-game
assertions**; physical mouse prompts and Steam acceptance
remain open. [Evidence and limits](BUILDING.md#guest-cylinder-head-fitting-2026-09-13-unreleased-v219).

**Firewood buyer checkpoint (v220, local/unreleased):** all four buyers now mirror
host visibility, world pose and pending native payment labels/colliders. Host LOD
and departure checks include nearby living guests. Canonical identity survives
customer 1's native relocation. Guest decisions are paused and v217 collection
still pays once. **4,436 Net tests, 109 native checks and 28 local two-game
assertions pass**, including all four customers with the host away, stale-state
rejection and reconnecting to relocated customer 1. Actual firewood delivery,
signed penalties, customer 1's car purchase and physical/Steam input remain open.
[Evidence and limits](BUILDING.md#firewood-buyer-visibility-and-offers-2026-09-13-unreleased-v220).

**Combined journey checkpoint (v220, local/unreleased):** one unchanged payload
completed join → host checkout/guest unpacking → powered Sorbet driving with both
driver/passenger roles → declined and agreed sleep → native save/quit → process
restart and guest rejoin. **38 distinct local observations and 4,436 Net tests
pass.** Both bag-holder roles stay exclusive; the saved bag yields only its two
remaining purchases. Shared money, groceries, the actual pre-save car position,
advanced time and returning guest needs/position survive. Personal files and the
guest's native world files remain unchanged. This was scripted native interaction
with separate disposable profiles and a probe-only stable UDP identity; physical
input, guest checkout and Steam/two-PC acceptance were not exercised in this run.
[Evidence and limits](BUILDING.md#combined-local-journey-2026-09-13-unreleased-v220).

**Sorbet parking brake checkpoint (v221, local/unreleased):** native timed lever
replay gave the host 82.1% braking while the guest had 100%. The existing vehicle
climate/control stream now carries the exact normalized lever value, including
reliable ownership release, host fallback and snapshots. Generic brake FSM
replay is suppressed. Nearby users can claim an unowned car; another simulator's
lease blocks the adjustment. **4,451 Net tests, 30 distinct local gameplay
assertions and three motion checks pass.** Full/partial settings, driver handoff,
reconnect and native save-and-quit were exercised; personal and guest native world
files remain untouched. [Evidence and limits](BUILDING.md#sorbet-parking-brake-setting-2026-09-13-unreleased-v221).

The original exact 13.718 m displacement was not reproduced. Tracing did reproduce
sharp movement when the fixture left the guest inside the cabin, even with matching
brakes. Repeating from the original save with the guest moved clear removed those
impulses: both cars crept together under native physics, staying within 3 cm.
This supports cabin contact as a contributor; it is not proof of every cause of
the original movement. Physical exit/parking and Steam acceptance remain open.

**Cylinder-head fastening checkpoint (v222, local/unreleased):** guests can tighten
and loosen all ten fasteners through native tool events. Indexed requests use the
current head revision and existing operation ledger; the host validates the fitted
mount, nearby living guest, bounds and cooldown before running the native turn.
Message 207 carries the exact array and aggregate with placement. Guest saved
Bolts, Tightness and AssemblyID remain untouched. **4,463 Net tests and 40 local
two-game checks pass**, including all ten slots, both turn directions, rejected
stale/distant requests, bounds, removal/refit, reconnect, native save/quit and full
process restart. Personal files and all copied guest native text files remain
unchanged. [Evidence and limits](BUILDING.md#guest-cylinder-head-fastening-2026-09-13-unreleased-v222).
Physical tool selection/mouse input, originally fitted guest saves, missing or
different heads, full child-part parity and Steam/two-PC acceptance remain open.

**Electricity/fridge checkpoint (v223, local/unreleased):** a native bill cutoff
left MainSwitch on, so the old stream incorrectly told guests that power remained
available. Electricity now shares effective supply, the independent switch, exact
debt and bill visibility. Guest electricity timers pause after loading and restore
on disconnect. **4,476 Net tests and 46 local two-game checks pass** across both
homes, warm/cooled milk, guest door cycles, cutoff/restoration, differing guest
power states on rejoin, and terminal spoilage. Core/probe and both Net targets build
cleanly; 18 personal files and 12 copied guest native text files are unchanged.
The native fridge cooling-area latch after power loss is preserved. Other food,
individual fuses, bill payment through physical controls, native cutoff save/reload,
and Steam/two-PC acceptance remain open.
[Evidence and limits](BUILDING.md#electricity-cutoff-and-fridge-milk-2026-09-13-unreleased-v223).

**Missing head save-record checkpoint (v223, local/unreleased):** all six
VIN1110 records were removed from a copied guest carparts file before loading.
Native Init takes its no-save branch and initializes the existing scene head;
the mod then reuses it for host attachment, fasteners and valves. **24 local
two-game checks pass**: one head throughout, all 21 child mounts, eight valve
displays, guest loosening/removal/refitting/tightening/adjustment, resync and rejoin.
Original guest arrays stay local and all six records remain absent. All 18 personal
files and 12 prepared guest native text files are unchanged. Production code,
DLLs, catalog and protocol were unchanged; no extra spawn path was needed.
[Evidence and limits](BUILDING.md#missing-cylinder-head-save-records-2026-09-13-protocol-v223).
This closes absent native save records, not reconstruction of an externally
destroyed/corrupt scene head or alternate unsupported head variants.

**Moose-meat output checkpoint (v224, local/unreleased):** native corpse chopping
now publishes each exact factory output with its persistent ID. Guests materialize
separate replicas, retain host food appearance/condition, and hide their own native
meat until disconnect. **4,492 Net tests and 20 final local two-game checks pass**:
four root cuts and its limit, a spine cut while disconnected, five-piece late join,
repeated snapshots, raw/grilled/rotten/charred/spoiled presentation, guest eating,
retirement, restoration of an overlapping native ID and replica-remnant cleanup.
Core/probe build cleanly; 18 personal files and 12 guest native text files are
unchanged. Guest axe-chop intents, physical inputs, native meat save/reload,
natural cooking/spoilage timing and Steam/two-PC acceptance remain open.
[Evidence and limits](BUILDING.md#moose-meat-output-2026-09-13-unreleased-v224).

**Electricity-payment checkpoint (v225, local/unreleased):** the old guest Pay
request is rejected when the host's inactive bill sheet is unregistered; its menu
position also cannot validate mailbox proximity. Both electricity bills now use
host invoice revisions and acknowledged, once-only shared cash settlement at the
actual envelope reference. Native Pay bills clears debt/cutoff timing and restores
supply; guest forms never replay payment. **4,505 Net tests and 20 final local
two-game checks pass.** Core/probe build cleanly; personal files remain unchanged
and test cleanup is complete. Automated and local acceptance evidence
is recorded in [BUILDING.md](BUILDING.md#electricity-bill-payments-2026-09-13-unreleased-v225).
The installed game ships both envelope Use components disabled; tests explicitly
enable the copied controls. Ordinary envelope access, physical payment input,
phone charges, native cutoff/payment save-reload and Steam/two-PC remain open.

**Passenger recovery checkpoint (2026-09-13, unreleased v226):** native two-game
acceptance reproduced delayed seat release and a false respawn after 120 seconds
at the newspaper. The inactive death graph now binds before activation; State 3
retires seats before native controller destruction. Death survives scene changes;
only active movement and completed guest spawn selection permit a respawn report.
**18 final two-game checks, 4,505 Net tests and 18 launcher tests pass**, with
additional newspaper observations beyond 135 seconds on the same Core/Net payload.
Both host and guest recover through native MainMenu/load, walk using native
movement and make fresh accepted seat claims. Personal files and 12 copied guest
native text saves remain unchanged. [Evidence and limits](BUILDING.md#native-passenger-recovery-2026-09-13-unreleased-v226).

**Permadeath setting checkpoint (2026-09-13, unreleased v227):** the host now
reads native `savefile.txt?tag=PlayerPermaDeath` or the loaded GAME variable.
Connected guests override the matching native LoadBool before it advances and
apply the host's runtime setting without writing their own save. Native host
load/character-save changes publish SessionSettings 213. **21 local two-game
setting checks, 4,515 Net tests and 18 launcher tests pass**, including both values,
conflicting guest state/save, native LoadBool/SaveBool, reconnects and full native
Continue loading on both roles. All 18 personal files and 12 copied guest native
text files remain unchanged. The binding mismatch is closed;
[evidence and limits](BUILDING.md#native-permadeath-settings-2026-09-13-unreleased-v227)
keep actual group death/save deletion and Steam acceptance separate.

**Guest moose chopping checkpoint (v228, local/unreleased):** native death was
shown to detach the corpse and destroy the streamed mover, leaving the guest's
corpse inactive. Independent corpse state now carries the ragdoll and both piece
counts. Guests keep native axe checks, send count-bound intents, and receive the
host's exact meat outputs; concurrent/repeated requests and reconnect cannot
restore consumed pieces. The corpse is excluded from vehicle ownership. **24
final local checks and 4,559 automated tests pass.** [Evidence and limits](BUILDING.md#guest-moose-chopping-2026-09-13-unreleased-v228).

**Permadeath group wipe checkpoint (v229, local/unreleased):** the existing rule
is one death ends everyone's run. Native activation now runs once; duplicate
reports, stale respawns and menu loading cannot revive an ended session, and
reconnect is refused. **26 final local checks and 4,563 automated tests pass**,
including deaths initiated by either role and ordinary recovery as a control.
Seven copied host world files are deleted as native permadeath requires; all 12
copied guest native text saves and all 18 personal files remain unchanged.
[Evidence and limits](BUILDING.md#native-permadeath-group-wipe-2026-09-13-unreleased-v229).

**Phone bill settlement checkpoint (v230, local/unreleased):** the v229 guest
saw 128 mk while the host saw 140 mk and guest payment cleared only its local
meter. Host usage/tariffs now supply both invoice columns and total; one receipt
settles native host debt/line/usage once. **25 final local checks and 4,575 automated
tests pass**, including both bills/roles, competing requests, repeats, insufficient
funds, distance, reconnect, guest restoration, electricity controls and host-alone
payment. All 18 personal files and 12 copied guest native text saves are unchanged.
[Evidence and limits](BUILDING.md#shared-phone-bill-payments-2026-09-13-unreleased-v230).

**Hockey reload checkpoint (2026-09-13, local/unreleased, still v230):** the
restore warning was caused by querying proxies after native scene destruction.
Cleanup now skips destroyed objects while retaining in-game restoration of all
nine lists, six tables and guest settings. **13 local two-game checks and 4,557
Net tests pass**, including two native menu/Continue cycles, fresh host updates,
resync and restoration of the newly loaded guest board. All 18 personal files and
both profiles' 12 native text saves are unchanged. Generic native index messages
remain separate; this is not a clean-log or complete hockey acceptance claim.
[Evidence and limits](BUILDING.md#hockey-scene-cleanup-2026-09-13-unreleased-v230).

**Firewood unloading checkpoint (v231, local/unreleased):** the guest now requests
unloading of the host's existing flatbed load. The native host alone consumes it,
creates ground piles and calculates the buyer's offer; both peers share load,
mass, pile geometry, delivered amount and signed early/late adjustment. Partial
unloading resumes into the same pile. **26 final local two-game checks, 4,582 Net
and 18 launcher tests pass**, including payment by either role, repeated/stale/
distant requests, unpaid reconnect and fresh unloading after rejoin. All 18
personal files and each copied profile's 12 native saves remain unchanged.
This closes unloading through collection from a host-supplied load. Guest
woodcutting/loading, tractor transport, delivery save/reload and physical/Steam
acceptance remain open. [Evidence and limits](BUILDING.md#shared-firewood-unloading-2026-09-13-unreleased-v231).

**Guest Corris puncture checkpoint (v232, local/unreleased):** a native guest
puncture previously reverted to healthy before reaching the host. Request 218 now
lets the host record damage; independent tyre epochs in message 205 prevent old
requests undoing repairs. **18 local two-game checks, 4,592 Net and 18 launcher
tests pass**, including controlled rolling, host handoff, parked rejoin/resync,
concurrent punctures and invalid/replayed requests. Guest saved part health stays
unchanged. Native prefab tyres, prepared mounts/seat, nearby entry events and
matching disposable worlds were used; powered driving, physical controls and
tyre save/reload remain open. [Evidence and limits](BUILDING.md#guest-corris-punctures-2026-09-13-unreleased-v232).

**Corris join stability checkpoint (still v232, local/unreleased):** the native
world-connected parking joint pulled the guest car back toward its saved pose
after a host snapshot, producing runaway motion. The catalog-bound adapter now
rebuilds that parking lock at the accepted pose before dynamic physics resumes,
preserving native settings, FSM references and connected part joints. **15 local
two-game checks, 18 native checks, 4,600 Net and 18 launcher tests pass.** Original
differing disposable worlds remain unchanged; no wheel/seat fixture was needed.
This closes the reproduced join instability, not full Corris assembly/driving.
[Evidence and limits](BUILDING.md#corris-join-stability-2026-09-13-unreleased-v232).

**Home-stove checkpoint (v233, local/unreleased):** both homes accept guest knob
turns with the host away. The host supplies full native heat, grill/burn triggers,
light/smoke and cooked moose-meat state; guest controls/simulation restore on
cleanup. **29 final local two-game assertions, 4,625 Net and 18 launcher tests
pass**, with clean Release builds and normal game exits.
[Native evidence and limits](BUILDING.md#guest-home-stove-cooking-2026-09-13-unreleased-v233)
record the controlled two-game cooking/reconnect work and remaining acceptance.
Sausage conversion, all other food and complete house fires remain open.

**Corris ATF checkpoint (v235, local/unreleased):** guest cap/refill, finite bottle
remainder/empty state, host gearbox gain, local gauge and native save/rejoin are
implemented. Host-relative filler projection fixes different engine positions;
latest accepted bottle poses determine contact. **39 final two-game assertions,
4,755 Net and 18 launcher tests pass**, with clean builds, normal exits and
unchanged protected personal/guest saves. The native fixture stabilizes the
incomplete host engine and controls hand/cap placement. Physical/Steam use,
assembled-car handling and other maintenance fluids remain open; I12/V10 stay
Partial. [Evidence and limits](BUILDING.md#guest-corris-atf-refill-2026-09-13-unreleased-v235).

**Flea finance checkpoint (v236, local/unreleased):** paid rental/proceeds and
guest cleanup pass **33 controlled two-game checks**, including cold save/reload
and host-away use. **4,792 Net and 18 launcher tests pass**; personal/guest native
saves remain unchanged. The fixture stages proceeds, so this does not validate an
actual item sale. [Evidence](BUILDING.md#flea-rental-and-proceeds-2026-09-13-unreleased-v236).

**Closed bounded checkpoint (2026-09-13, protocol237):** a guest lists native chips
packets through the price sheet; distinct saved IDs/prices survive rejoin and cold
reload. Native sales retire the exact packet once, rental expiry releases unsold
stock, and actual proceeds collect once and persist. **21 final native assertions**
(17 journey + 4 collected cold-load), **4,814 Net tests** and **18 launcher tests**
pass; the Core/probe Release build is clean against installed build23268598.
Personal files and guest native saves remain unchanged. Intermediate failures,
fixture timing corrections and final payload hashes are in
[BUILDING.md](BUILDING.md#shared-flea-listings-and-sales-2026-09-13-unreleased-v237).
J04 stays Partial: other item families, legacy random listings, early manual
withdrawal and physical/Steam/two-PC acceptance remain open.

**Fuse-box checkpoint closed (2026-09-14, v238 unreleased):** the guest can buy and
unpack a five-fuse box, either player can extract one shared fuse at a time, and
repeated/stale requests cannot create extras. Native host save/reload preserves
partly used boxes and loose fuse IDs; emptied boxes and discarded fuses stay gone.
Guest saved originals are hidden/restored, and the existing spark-plug box remains
usable. **22 distinct native assertions** cover the journey, cold continuation and
empty-box cold load; the final build repeats the four final cold-load assertions.
The Core/probe Release build, **4,839 Net tests** and **18 launcher tests** pass.
[BUILDING.md](BUILDING.md#shared-fuse-boxes-and-loose-fuses-2026-09-14-unreleased-v238)
records native graphs, run artifacts, fixture corrections, hashes and limits.
I05 moves from Missing to Partial: light-bulb/R20 boxes and household fuse
installation/tightening/blowout/electrical effects remain open.

**Ignition-wire checkpoint closed (2026-09-14, v239 unreleased):** either player
can complete the native ignition-to-fuse-box connection, with one host saved result
and matching cable/endpoint presentation. The guest preserves its own saved wire,
including a conflicting installed original. Native host DESTROY removes the cable;
old accepted/stale requests cannot reinstall it, and installation/destruction both
survive native save/reload. The audited steering-column mount only gates the
Ignition endpoint; electrical fire is the identified native wire-destruction path.
No new manual removal control was added.

**22 distinct controlled native assertions** cover the live journey, two cold loads,
and binding-failure containment/restoration. The final build repeats four cold
checks and adds four failure/recovery checks. Core/probe Release builds against the
installed game with zero warnings/errors; **4,864 Net tests** and **18 launcher
tests** pass. All 18 protected personal files and all 15 copied guest-profile files
are unchanged. [BUILDING.md](BUILDING.md#shared-corris-ignition-wire-2026-09-14-unreleased-v239)
records exact evidence, fixture corrections and hashes. V05/V06 stay Partial:
this is one of 35 native wiring connections; manual aiming, full column fitting,
other wires, fire/shock effects and a complete running engine remain open.

**Taxi pickup prerequisite closed (2026-09-14, still v239 unreleased):** the native
host customer now recognizes an accepted guest taxi driver. **9 controlled native
assertions** cover the failing baseline, successful guest pickup with the host
elsewhere, driver exit, host handoff, changed-binding containment and restoration.
Core/probe builds are clean; **4,880 Net tests** and **18 launcher tests** pass.
Personal files and all 15 copied guest-profile files are unchanged.
[Evidence and limits](BUILDING.md#guest-taxi-pickup-prerequisite-2026-09-14-unreleased-still-protocol239).

The [fresh taxi lifecycle audit](SYNC-SCOPE-AUDIT.md#taxi-native-lifecycle-audit-and-pickup-prerequisite-2026-09-14-still-v239)
records all discovered dependencies and corrects older fare claims. **The selected
one-fare journey and J06 remain open.** Native customer Cost is the finalized
offer; live Tripmeter.Price and IncomeTotal/receipts precede later bank wages.
The test explicitly enabled both worlds and placed the host customer for pickup;
activation, guest call/meter/terminal actions, shared customer presentation, luggage,
receipt identity and full persistence have not been completed or accepted.

**Taxi service dependency implemented (2026-09-14, v240 unreleased):** host job
activation and the three native availability colliders now reach guests. Incoming
calls are generated and completed only by the native host; authenticated guests
can answer/hang up the current call. Pickup/destination text, customer visibility,
walking/boarding pose, seat parenting and native animations have a dedicated
ordered presentation stream. Guest job/customer/ringing decisions pause and restore.
Last-guest departure releases its handset directly, and reconnect restores the
host state. [Evidence and limits](BUILDING.md#shared-taxi-calls-and-customer-presentation-2026-09-14-unreleased-v240).
This supersedes the preceding pickup-only checkpoint's activation/call/visual gaps;
**J06 and the complete one-fare journey remain Partial/open**.

**Taxi duty/meter dependency implemented (2026-09-14, v241 unreleased):** guests
can select ordinary meter modes, start/pause the fare and request the native total
reset. The host owns tariff/time/distance calculations and guests share the LCD,
knob, lights and native values. Accepted guest gauge speed feeds host distance
charging; expired telemetry contributes no new distance. Revision/sequence checks
reject duplicates and stale/distant controls. Guest calculation/display/terminal
FSMs pause and restore; rejoin restores the current fare without restarting it.
[Evidence and limits](BUILDING.md#shared-taxi-duty-and-meter-2026-09-14-unreleased-v241).
**J06 and the full fare remain Partial/open.** Auxiliary knob mode 6 is host-operated.

**Taxi arrival/payment dependency implemented (2026-09-14, v242 unreleased):**
guests can quote the paused host meter after native arrival and collect the
customer's shared cash offer. Native host customer logic credits IncomeTotal once;
it does not immediately pay the wallet or bank. Duplicate/stale/distant requests
are rejected. Rejoin restores an unpaid offer; collected income survives native
host save and cold reload to both players. **17 controlled two-player checks**,
**4,944 Net tests** and **18 launcher tests** pass. Physical mouse/keyboard, full
road travel and role reversal remain untested for this slice.
[Evidence and limits](BUILDING.md#shared-taxi-arrival-quote-and-cash-2026-09-14-unreleased-v242).
**J06 and the full fare remain Partial/open.**

**Next required bounded task:** receipt printing, shared physical identity and
customer handoff. Then close luggage identity/transport and complete payday,
receipt/odometer save/reload. Collected taxi-income persistence is now verified;
saved mid-fare progress is not added. Host-only tutorial/outbound phone,
player passenger seats and real two-PC input remain listed gaps. Continue the taxi
journey only for these concrete dependencies; reassess the whole project after the
fare closes, then rotate toward another missing ordinary gameplay loop.

Reassessment: reported grocery duplication has not reappeared locally; performance,
launcher, head fitting/fastening, brake scalar, effective supply, electricity
settlement, passenger recovery, native permadeath settings, guest chopping, group wipe, phone settlement and hockey scene cleanup
checkpoints are closed within their documented limits, as is host-loaded firewood
unloading through collection. Physical/simultaneous death causes, interrupted group wipes, death while driving,
physical controls/cameras, uninterrupted host-world operation, intermittent
interaction readiness, natural bill availability, phone call accrual and native bill save/reload,
guest firewood cutting/loading and tractor travel, native delivery save/reload,
full Corris assembly/driving, powered tyre failures/repairs and save/reload,
unattributed native index logs, Steam/two-PC and a four-player soak
remain open. No complete-mod or full M7 acceptance is claimed.

### Keep each development cycle finite

1. Pick **one active outcome** and state its pass condition before editing. A
   reported regression or save/state corruption outranks the suggested next step.
2. Reproduce or inspect the native flow first. If it already meets the condition,
   record that result and move on. Add test tooling only when needed to answer the
   current question; stop expanding it once it can do so.
3. Finish the fix, relevant checks and cleanup together. Report implementation
   status separately from gameplay acceptance. If a required test cannot run,
   record the exact missing evidence and prerequisite; do not mark it passed or
   keep adding adjacent code while waiting.
4. Reassess the whole project and select the next outcome. Continue in the same
   topic only for an identified blocker, dependency, failing check or user request.
   End each cycle with what changed, what was observed, what is still open and why
   the next task matters. Update this checkpoint instead of adding a second queue.
5. Prepare reviewable change groups and a short tester brief at playable
   checkpoints. Commit/release only when requested. Each test release identifies
   the exact build, changes and a small set of checks; carry forward unresolved
   failures. Keep the guide tied to its actual package: `docs/TESTING.md` now
   describes the unreleased local 0.1.33 / protocol261 package workflow, not the
   historical protocol120 public kit. Read the fresh attempt's `validation.json`
   for artifact/build checks and missing dependencies. Historical join/shop/sleep
   checks do not establish what testers experience on this package.

**Every task's Definition of Done (DoD):**
1. Follows the **host-authoritative** rule (guests send intents; host validates + broadcasts). Host always wins.
2. Wrapped for **crash containment** (`Util/SafeApp.cs`); a fault disables *that* subsystem, never the game.
3. If it touches the wire: **bump `ProtocolInfo.Version`** (`src/WinterMP.Net/Protocol.cs`), append the new id in the reserved range, and **update `protocol/PROTOCOL.md` in the same change** (see §5 discipline in `AGENTS.md`). Never reuse a retired id.
4. Protocol logic lands with a **round-trip test** in `WinterMP.Net.Tests` (the CI loop). `AllMessagesRoundTripTests` auto-covers any new `IMessage` — keep it green.
5. `dotnet build -c Release` of `WinterMP.Core` is clean (0 err) and `dotnet test src/WinterMP.Net.Tests` is green.
6. Add a `catalog/` rule instead of hardcoding when the interaction is a plain button/purchase.
7. Note the **2-player check** the task needs (most of this is not unit-verifiable — it feeds the playtest list).

**How-to references (don't re-derive):** adding a message / catalog rule / FSM hook →
`docs/AGENT-RECIPES.md`. Where a subsystem's code lives → `docs/CODEMAP.md`. Query the
game dump → `python3 tools/extract_fsm_details.py catalog/dump-23268598.json show:<path>`.

**Reuse these existing patterns — do not invent new ones:**

| Pattern | Use it for | Reference implementation |
|---|---|---|
| **Host scalar-state + guest intent** (State broadcast on change/join; Intent validated by fresh-pose proximity + monotonic per-player sequence) | gambling, bills, sauna, appliances | `HeatSourceSync` (`HeatSourceState`/`HeatSourceIntent`) |
| **Capture-and-pair before payment** (host captures the guest's exact generated record, pairs it to that player's next `PurchaseIntent(PAYMENT)`) | repair-shop order, any "configure then pay" flow | `MailOrderSync` (`OrderAMIS`/`OrderYP`) |
| **Host-validated record from guest observation** (guest reports a local outcome; host re-derives/validates, owns the durable record, broadcasts + retires it) | wanted level, race results, fines | `PoliceSync` (fines), `RallySync` (stage) |
| **Host-authoritative transform streaming** (host sims, streams pose over `NpcTransform`; guest freezes local AI while a stream is live) | moose corpse, crime NPCs, opponent cars | `NpcTrafficSync` (`ScriptedMover` + rigidbody paths) |
| **Anyone-triggers control / buy** (catalog rule replays a button/purchase reliably on the host) | simple switches, one-shot buys | `catalog/sync-catalog.json` `controls[]` / `buys[]` |
| **Runtime item spawn manifest** (host mints ids; peers materialize) | moose-meat, any `Spawner/*` output | `ItemWorldSync.Spawn` (`ItemSpawn`/`SpawnIntent`) |

**Recurring bug classes to check in ANY new sync subsystem** (each has burned us before):
- **A** — optimistic send-time dedup latch with no ack → lost on a transient host reject. Advance the baseline only on the host's accepting echo.
- **B** — a join-snapshot path mutating the periodic delta baseline → strands connected guests. Snapshot must pass `advanceBaseline:false`.
- **C** — a host-owned/observed record with no terminal-state broadcast → phantom on late-join, or never clears. Broadcast the retire/finish edge.
- **D** — `FindFsmFloat` vs `FindFsmInt` type mismatch → silent null bind kills the whole subsystem. Verify var *type* against the dump, not just the name.
- **E** — remote-apply write onto a locally-owned object (ownership contamination). Guard every game-state write with `!LocallyOwned` / owner check.
- **F** — sequence reset on a reused/path-derived id. Handle re-baseline (see `NpcTrafficSync` stale-run detection).

**Reserved protocol id ranges** (current version **v257**; allocate the next free id in-range):
`63–79` vehicles · `93–99` economy/appliances · `101–119` NPCs/jobs · `126–139` snapshot/bulk.
Ranges are tight — if a range fills, extend it in `IMessage.cs` and document it.
The current next free ID is **258**, after v257 AdvertPhoneResult (257). Earlier allocations are recorded in `protocol/PROTOCOL.md`.
The economy overflow currently uses 160–183; 164–166 are the v92 slot ledger messages,
167–169 are v93 VideoPoker, 170–172 are v94 debt-letter quotes/payments, 173 is v97
Ventti property state, and 174 is v98 Ventti table observations (retiring 93).
175–177 are the v99 Ventti ledger/commands/receipts (retiring 94), and 178–179
are v100 Ventti NPC/furniture poses and live sound cues.
v101 appends rally report tokens/acknowledgments and per-player revisions to 86/87;
it allocates no new ids. v102 adds complete Lotto draw state 180, retiring incorrect 96.
v103 adds Lotto ticket requests/receipts/state 181–183.
v104 appends complete hockey betting collections to 160 without allocating a new id.
v105 strengthens item despawn/replay/resync semantics without adding fields or IDs.
v106 adds native trophy factory semantics to ItemSpawn flag bit 1 without new fields or IDs.
v107 appends full native part condition floats to 45/124 and changes part-report/CRC semantics.
v108 changes standard-package item identity/disposal semantics without adding fields or IDs.
v109 adds standard box identity/quantity/creation state 184.
v110 changes native part body/FSM identity semantics; no new fields or message IDs.
v111 adds loose replacement-part creation state 185 and echoes accepted removals to requesters.
v112 adds acknowledged box opening requests/receipts 186–187.
v113 corrects native part lifetime and the Installed semantics in 185; no new IDs.
v114 appends native parent tightness to 44/123 and makes bolt intents/results host-authoritative; no new IDs.
v115 appends parent identity/path and local pose/scale to replacement state 185; no new IDs.
v116 adds replacement fitting requests/receipts 188–189; next free ID is 190.
v117 appends install/remove operation to 188–189 and removal readiness to 185;
next free ID remains 190.
v118 appends SlotIndex to 188–189 for native piston/main-bearing/rocker installation;
next free ID remains 190.
v119 adds alternator adjustment operations to 188–189.
v120 adds shared bags 190–192 and retires guest SpawnIntent 53; next free ID is 193.
v121 adds direct bag engine-part semantics to replacement state 185 without new IDs.
v122 adds oilfilter hand-tighten/loosen operations 4/5 to 188–189; next free ID remains 193.
v123 appends PresentationRevision and optional host fanbelt presentation to state 185;
next free ID remains 193.
v124 makes VehicleDamage 64 host-authoritative regardless of driver; guests retain
passive condition without native damage replay. No new fields or IDs; next free ID remains 193.
v125 adds distributor timing semantics to operations 2/3 in 188–189, selected by the
catalogued part family; state 185 already carries SparkAngle. No new fields or IDs.
v126 binds VehicleState 60 to current physics ownership, orders final ignition
state before release, and guards handoff/replay sequences. No new fields or IDs.

v150 allocates engine wiring state 193; v151 adds starter flywheel input semantics.
v152 allocates battery engine-input state 194; v153 allocates block engine-input
state 195; v154 allocates gearbox starter-input state 196. v155 adds the cylinder-head
installation flag to 195 without new fields or IDs. v156 appends carburettor
inputs to 195; v157 adds intake performance/filter installation, and v158 adds
four exhaust performance triplets and section flags to the same state. Next free
ID remains 197. v159 appends the eight cylinder-head valve settings and availability
to 195 without allocating an ID. v160 appends oilpan installation and five mounted
condition/fluid inputs to 195. v161 appends rocker-cover installation and mounted
tightness to 195. v162 appends radiator installation and four mounted cooling
inputs to 195. v163 appends four hose installation bits/tightness values and
carburettor tightness to 195. v164 appends four airflow installation bits and
three modifiers to 195 (204-byte payload). v165 appends cooling ambient
availability/temperature to 195 (209 bytes); next free ID remains 197.
v166/v167 add guest-driver RPM semantics to host pressure/mechanical wear and oil
contamination through VehicleState 60; its layout and the next free ID are unchanged.
v168 appends native torque availability/value to 60 (22 bytes including ID) for
host heating; no new ID is allocated.
v169 appends movement-speed availability/value to 60 (25 bytes including ID)
for native host cooling; wheel-speed presentation and the next free ID remain unchanged.
v170 adds host cooling pump/fan/leak RPM semantics to 60; its 25-byte layout
and the next free ID remain unchanged.
v171 adds host coolant dashboard state 197 for Corris, independent of driver
ownership; state 60 stays 25 bytes. Next free ID is 198.
v172 appends host engine degrees to 197 (19 bytes including ID) for guest fuel
and oil inputs; the next free ID remains 198.
v173 supplies host engine/coolant heat to native cabin/heater reads; state 197
remains 19 bytes and the next free ID remains 198.
v174 connects three guest battery/charging temperature reads to host heat;
state 197 remains 19 bytes and the next free ID remains 198.
v175 supplies guest-driver RPM to all three native host Electrics reads for charging
and battery drain; state 60 remains 25 bytes and the next free ID stays 198.
v176 guards external native scalar writes to the guest's saved battery mount,
including consumer target caching and movement. Wire layouts and next free ID 198
remain unchanged; guest electrical demand is not reported to the host by this change.
v177 appends host battery ChargeMax to state 194 (15 bytes including ID) and supplies
Installed/Charge/ChargeMax to guest accessory/fan/wiring/current reads without saved
source writes. The next free ID stays 198; guest starter/accessory demand remains open.
v183 appends independent rear-window element flags to HeaterState 200 (12 bytes).
Guest native rear defrosting consumes the host body option; next free ID stays 201.
v184 appends five independent window cutoffs and availability to VehicleClimate 61
(23 bytes), and restores the taxi climate catalog paths; next free ID remains 201.
v185 changes climate ownership, sequence, snapshot and final-release semantics
without changing the v184 layout or allocating an ID.
v191 makes death/respawn retire passenger seats while retaining claim history;
dead seat claims remain rejected. No layout or ID changes; next free stays 201.
v190 adds available cabin-temperature flag 8 to VehicleClimate 61 (still 23 bytes).
Seated passengers use current cabin inputs in native body reads; missing/expired
data falls back to native heat lookup. No new IDs; next free stays 201.
v189 corrects body warmth to PlayerTemp, appends availability to report 25 (44 bytes)
and assigns bit 4 in GuestSpawn 24 (still 95 bytes). Legacy ambient profile samples
are retired; new warmth is independent column 17. No new IDs; next free stays 201.
v188 appends sweat availability/value to PlayerTransform 22 (39 bytes). Fresh
accepted passengers contribute to the climate owner's native fogging rate with
the existing native ceiling; no new IDs, next free remains 201.
v187 applies condensation through native/material alpha, retires Fog in place and
preserves native sweat/defrosting scratch. Layouts and next free ID remain unchanged.
v186 separates shared cabin occupancy from native local PlayerIn and includes accepted
passenger seats; reused slots clear old seats. Layouts and next free ID stay unchanged.

v182 supplies native heater inlet/outlet Installed reads from existing state-195
hose bits 2/3; layouts and next free ID 201 remain unchanged.

v181 extends WiringState 193 to heater/defroster circuit sources 9–11 and native
guest Installed reads. Existing layouts stay unchanged; next free ID remains 201.

v180 adds host-only HeaterState 200 (11 bytes) for settled heater installation/wear,
with saved guest mount/external-write protection. Existing layouts stay unchanged;
next free ID is 201.

v179 adds reliable native starter-wear duration request 199 (13 bytes). Host native
rate and mounted durability own wear; existing message layouts remain unchanged.
Next free ID is 200.

v178 adds authenticated native starter draw request 198 (12 bytes), reliable ordered.
The host applies its own loaded/unloaded rate after ownership, replay and native
validation. Existing state layouts stay unchanged; next free ID is 199.

**Do NOT work on (verified out-of-scope — see §Appendix):** the in-game computer + its
fishing minigame, host migration, water wells/taps (per-player thirst), map clock/weather
(already synced), jukebox/CD-track/cloud-sprite (cosmetic). Food spoilage was reopened
by the live milk audit; see the Appendix.

---

## 1. Progress

Tick when merged. Ordered by priority (shared-state corruption first).

> **The first pass ticked these 35 tasks, but later audits reopened incomplete work.**
> These tasks were the *2026-07-21 audit's* decomposition and remain largely
> **unplaytested**. A later re-audit found a whole unsynced background-economy
> cluster plus several subsystems that bind the wrong FSM variable and silently no-op.
> **Read §1b before concluding anything here is finished.**

**Phase 0 — Housekeeping (do first; cheap, unblocks others)**
- [x] 0.1 Correct the 4 `PLAN.md` coverage overstatements
- [x] 0.2 Fix the vehicle-registration `RequireRoot` gate (unblocks 2.4, 3.2) — structural `Simulation/Engine` check; adds only the taxi

**Session recovery, 2026-09-09 (development):** guest transport and handshake
waits each have a 60-second deadline; failure cleans up before restoring the
launcher friend picker, with the reason and guest save protection retained.
4,300 protocol tests and 32 native save/join checks pass, including a silent
real UDP host and successful retry in the same game process. This closes the
indefinite handshake wait; Steam/two-PC and physical picker acceptance remain
open. See BUILDING.md's join timeout section. No protocol bump (still 210).

**Phase 1 — Economy integrity** *(original shared-wallet / ownership gaps)*
- [x] 1.1 Gambling: pub & station slot machines — `GamblingSync` (164–166, v92), host ledger and seeded local reels; two-player verification pending
- [x] 1.2 Gambling: Ventti blackjack (incl. car wager) — `VenttiSync` reuses 93/94 (Kind=Ventti), host deals
- [x] 1.3 Electricity + phone bills → power cutoff / blackout — `UtilityBillState` (95, v225) + electricity payment intent/result (211/212). Effective supply and electricity settlement are validated with explicitly enabled copied controls; v230 phone settlement also passes 25 local checks; natural envelope access, physical inputs, call accrual and native cutoff/payment save-reload remain open.
- [ ] 1.4 Lotto draw (180, v102) and Lotto ticket purchases/claims (181–183, v103) implemented; two-player/save validation and Megaveto transactions remain incomplete (R2.21/R2.26).

**Phase 2 — Shared-car integrity** *(the project car is THE shared object)*
- [ ] 2.1 Engine damage — host authority and passive guest condition implemented; native guest damage is paused. v168–v170 supply guest RPM/torque to native host heating and movement/RPM to host cooling, alongside pressure/mechanical wear and oil contamination. v171 supplies host coolant to the Corris dashboard, including the guest driver; v172 supplies native engine heat to six guest fuel/oil reads; v173 connects cabin limits and heater calculations to host engine/coolant heat; v174 supplies host heat to guest battery cold penalties and charging limits; v175 connects guest RPM to native host charging and battery drain. Full engine operation, shared thermal consumers, remaining wear/damage paths and physical failure presentation still need acceptance.
- [ ] 2.2 Drivetrain wear + tire pressure/puncture — pressure units, ownership/order and host wheel-health inputs are implemented. Delegated tyre wear, parked pressure publication and complete physical tyre/failure transitions remain incomplete (see §2.2).
- [ ] 2.3 Repair-shop (Fleetari) service results + order record — order/payment capture and record broadcast exist; completed paint/bodywork/tuning/spring/tyre results are not a complete shared car outcome. See scope audit V12.
- [x] 2.4 Register the taxi (MACHTWAGEN) as a vehicle — done by 0.2's structural `Simulation/Engine` check (no taxi-specific code)
- [x] 2.5 Gearbox / clutch state *(lower)* — `Gear` byte appended to `VehicleState` (id 60, v63)

**Phase 3 — Income & jobs** *(full guest actions and once-only shared outcomes)*
- [ ] 3.1 Flea-market selling — v237 closes the bounded chips listing/sale/expiry/collection journey with native persistence and cleanup evidence. Other item families, legacy listings and early manual withdrawal remain (scope J04).
- [ ] 3.2 Taxi job lifecycle *(dep 2.4)* — activation/calls, guest pickup, meter, arrival/quote/cash and collected-income save/reload have bounded evidence through v242; receipt handoff, luggage and complete payday remain open (J06).
- [ ] 3.3 Kilju — four fermentation fields exist; ingredients, complete quality, bottle transfers and authoritative sale remain incomplete (J05).
- [ ] 3.4 Hitchhiker rides — scalar state/body motion exist; boarding references, mass/story and suitcase payment/output remain incomplete (J07).
- [ ] 3.5 Farm job — stage/Done and farmer pose exist; guest hay/machinery/delivery are incomplete, and legacy feature reachability needs review (J08).
- [ ] 3.6 Kela welfare — host benefit/rent scalars exist; guest application/contract choices are not a complete shared paperwork workflow (E07).

**Phase 4 — Crime & consequence**
- [ ] 4.1 PlayerWanted — shared group counters exist; originating incident/victim validation and complete consequences remain incomplete (W07).
- [ ] 4.2 Arrest/jail — current JailState relays one guest-owned countdown slot, not host-owned multi-player arrest/confinement/release or persistent sentences (W08).
- [ ] 4.3 Impound/tow — native host relocation can stream, but guest offense-to-impound authority and towing connection lifecycle remain incomplete (W07, V13).
- [ ] 4.4 Pursuit/DUI — sirens and observed flags exist; shared pursuit targets and authoritative escalation/arrest remain incomplete (W07).

**Phase 5 — Racing completeness**
- [ ] 5.1 Rally results/enrollment/parc-fermé — selected result scalars exist; full native result lists and guest registration action remain incomplete (R02).
- [x] 5.2 Rally PartsSalesman vendor — catalog `buys[]` entry (Purchase/Money flow); spawned parts via 7.2
- [ ] 5.3 JOKKIS lifecycle — host lap/time/checkpoint broadcast exists; guest crossing authority, enrollment and complete result/reward path remain missing (R04).

**Phase 6 — Home & survival depth**
- [ ] 6.1 Oven/stove cooking/fire — protocol233 implements both homes' knobs, full heat, triggers, light/smoke and one shared moose-meat result (H06 Candidate). Sausage conversion (I09), other food condition (I08) and full fire lifecycle (H11) remain open; see the bounded stove checkpoint for acceptance limits.
- [ ] 6.2 Electric home sauna — heat source exists, but native timer controls and nested steam binding are incomplete; finite water also needs authority (H08).
- [ ] 6.3 Phone calls — one first-found phone/topic event exists; both-home identity, active-call joining and answer/consequence authority remain incomplete (W04).
- [ ] 6.4 Fridge chilling · per-appliance consumption · individual fuses *(dep 1.3)* — consumption/fuse support folded into 6.1 + 1.3; the live milk audit proved time-based decay and a distinct fridge rate. v218 closes loose milk condition/Bad-phase divergence; v223 verifies milk across power/door changes in both homes and fixes effective supply. Other food, fuse changes and full cooling-area reconstruction on join remain open (see Appendix).

**Phase 7 — World & wildlife**
- [ ] 7.1 Moose death → corpse → meat chain — death/guest death reports already implemented; v224 adds host factory output and late-join meat replicas with 20 local checks. v228 adds independent corpse state and validated guest chopping. Physical/Steam and native meat save/reload acceptance remain open.
- [ ] 7.2 Spawner completeness — initial 14-subroot audit is done; bags, trophies, packages and replacement-part families have adapters. v224 adds native moose-meat output/state (210); remaining factories and full gameplay acceptance are tracked below.
- [ ] 7.3 Crime/reaction NPCs — Reijo movement and pub body streaming exist; reaction/combat state and dance-hall fighter coverage remain incomplete. Train is an additional omitted shared hazard (W01, W02, W05).

**Phase 8 — Player polish**
- [x] 8.1 PlayerAlco (BAC) reconnect persistence — extended PlayerNeedsReport+GuestSpawn (v78) mirroring the Dirtiness add across 5 files
- [ ] 8.2 Winter garments — wire value exists, but avatar consumes only stage/type and clothing has no guest-profile resume path (S06).
- [ ] 8.3 Yard stains — host scale broadcast exists; guest-created marks lack a host contribution intent (H15).
- [x] 8.4 In-car radio / CD power+channel *(cosmetic-adjacent; lowest)* — `CarRadioState` (66, v80) host-owned channel/volume; the station half was a silent no-op until **v81** (see §1b R2.5)

---

## 1b. Round 2 — what the v80 "complete" claim missed

Every box in §1 had originally been ticked, but a 2026-07-22 re-audit (and a
2026-07-24 dump-verification pass) showed §1 was **not** the same thing as
"every shared-state gap is closed". The tasks
above decomposed *interactive* systems; they missed the passive daily-tick `Systems/*`
economy, and several shipped subsystems bind the wrong FSM variable and silently no-op.

Work this list the same way as §1. **Priority order — the top group corrupts shared state.**

**Local verification, 2026-09-05 (0.1.31 tester package, v94):** 335 protocol/catalog tests,
18 launcher tests and 5 evidence-tool tests pass. Net (both targets), Core, Tools,
FastBoot and the launcher build against the installed game DLLs. Static extraction
of build 23268598 verifies the configured economy globals, all five ATM mutations
and 14 concrete damage mappings. Both slot machines' seven control bindings, typed
variables, weighted reels and all 729 payout combinations match installed action data.
VideoPoker’s 44 native visual assets, 12 input hooks and required scene objects also
match the installed game; tests cover all 2,598,960 five-card hands, and an independent
execution of the extracted classifier states agrees on 4,800 ordered hands. The debt
letter's 15 native calculation/request/payment actions, typed variables and scene/text
targets match the installed game; payment tests cover concurrency, retries, changing
quotes and fractional charges. No two-player gameplay test has run for this pass.
An isolated Wine startup check loads Core 0.1.31 in the current Unity/Mono runtime,
reaches MainMenu and starts a local UDP host; native Steam-dependent menu actions
report missing-Steam errors in that offline profile. It does not exercise the shared world.
The slot half of R2.12, VideoPoker (R1.3) and debt payments (R1.6) are implemented;
Ventti settlement remains open (R2.12); property/table replication (R2.11/13) and
vehicle CRC coverage (R2.4b) are implemented with two-player checks still pending.

**R1 — Never synced at all** (verified: zero references anywhere in `src/WinterMP.Core`)
- [x] R1.1 `Systems/BankAccount` — implemented 2026-09-05 (**v90**), runtime validation
      pending. Static extraction of installed build 23268598 establishes the globals
      `PlayerMoney`, `PlayerBankAccount`, `PlayerNetIncome`. This also exposed and fixed
      WalletSync's incorrect `Money` binding (there is no such global). WalletState now
      carries all three balances with optional-field flags, retained state for late binds,
      sequence ordering, and expanded wallet CRC/resync. Guests suppress the bank's own
      interest/ledger FSM, restored on disconnect/scene change/destruction. ATM transfers
      use host-validated requests + exact-once acknowledgments (162/163); guest deposits
      settle per inserted note and withdrawals on cash collection, preserving local ATM UI.
      Globals and ATM mutation bindings live in the catalog's `banking` section; hooks
      verify each action's target balance before installing.
      Bank statement history remains host-local. **Two-player check:** start from different
      cash/bank balances, deposit/withdraw concurrently, retry a request, reconnect, cross an
      interest day, and verify a later singleplayer session still runs the bank/ATM.
- [x] R1.2 `Systems/Expenses::Rent` + `::Livingsupport` — fixed 2026-07-27 (**v84**).
      `WelfareSync` now owns the whole Expenses object: `WelfareState` grew rentDebt /
      rentPerWeek / asumistukiPerWeek + an Evicted flag. Guests suppress their own
      Rent/Livingsupport FSMs for the session (`FsmSuppressor`; a guest plays in the host's
      world — its local weekly ticks were throwaway divergence) and replay the terminal
      `Kick out` state once when the host's eviction flag appears, so the furniture
      destruction/relocation happens everywhere. Eviction detection polls
      `ActiveStateName == "Kick out"` — safe because that state is *terminal* (unlike the
      R2.3 one-frame `Fire` transient). Money movement itself still lands in the bank
      balance → converges only once R1.1 ships.
- [x] R1.3 VideoPoker / Rami-Pokeri — implemented 2026-09-05 (**v93**, 167–169),
      two-player verification pending. `PokerSync` pauses native resolution on both peers;
      `PokerLedger` owns betting, two-draw hands, holds, the separate high/low deck,
      win collection and shared-wallet cash-out. The native physical controls, screen,
      card art, balances and sounds present authoritative state even with the host outside
      the town LOD. All bindings/assets and payout multipliers live in `videoPoker`.
      Receipts prevent repeat debits/payouts; control leases prevent concurrent hands.
      Abandoning/rejoining keeps the existing hand and secret double. Teardown settles
      once and refunds machine balances, with native-bank handoff if cash is imprecise.
      **Two-player check:** join mid-hand with the host outside town; hold/redraw together;
      collect a win, then cash out; double low/high including seven and the ≥500 automatic
      collection; retry a payment; reconnect during a hand/double; end the session and play
      the native machine again. Verify money conservation and native achievements.
- [ ] R1.4 `Systems/HockeyGames` — **v104 complete board implemented 2026-09-06;
      reload/restoration verified locally 2026-09-13, full runtime acceptance open.** The v88 scalar broadcaster did not synchronize
      the actual betting data: GameIndex/team IDs/odds/Result are loop scratch.
      Installed build 23268598 proves six live Hashtables (0–5, keys 1/X/2),
      ResultsGame/ResultsOdds lists, upcoming/previous pairings, scores and standings
      display lists. They are now appended to `HockeyBettingState` 160 as one complete
      snapshot. Host capture waits for both season and odds generation to finish;
      join snapshots do not consume ordinary broadcasts. Guests retain pending state,
      pause both local generators after loading, update live collections and refresh
      teletext 240/241/302. Disconnect restores their original collections and values
      before resuming native FSMs. No ROUND/ODDS/CHECKMEGAVETO or save event is replayed.
      The old claim that standings require ES2-level sync was incorrect: their display
      lists are live proxies too. Individual-player scoring (Pisteporssi) is still
      outside this snapshot. The v230 scene-cleanup check passes two native
      menu/Continue cycles, host odds changes, resync and complete original guest
      board restoration. Destroyed scene proxies are skipped on cleanup; surviving
      ones retain the normal restore path. Natural round generation and visible
      pages remain unverified. Use the v104 BUILDING.md checklist for loading, live
      rounds, visible pages, guest restoration and reconnect. Megaveto tickets still
      need their own host transaction ledger (R2.21); board sync alone does not settle
      purchases or claims.
- [x] R1.5 `Systems/ScrapMetalPrice` — the *price* half fixed 2026-07-27 (**v86**,
      `WorldScalarsState` 104 / `WorldScalarsSync`): the host's daily `ScrapPriceMKkg` +
      `Change` are broadcast and guests' own re-rolls get stomped. Still open: the
      `GarbageTrigger` selling *payout* is a money-gain button — same class as the lottery
      win (R2.21), needs a catalogued rule from the fresh dump (R3.1b).
- [x] R1.6 `Sheets/DebtLetter` — implemented 2026-09-05 (**v94**, 170–172),
      two-player verification pending. `WelfareSync.DebtLetter` supplies host quotes
      and acknowledged shared-cash payments through `DebtPaymentLedger`. Installed
      build 23268598 establishes the exact calculation: `Rent/Debt` × the sheet's
      `Interest` (default 1.29), then `Cost1` (59) and `Cost2` (864), with native float
      precision. The bank prime rate is unrelated. Catalogued actions/variables are
      validated before replacing the calculation and payment actions on both peers.
      Payments compare the displayed quote revision, validate proximity to `Rent/Letter`,
      debit shared cash, clear rent debt and hide the envelope once, even with the
      host's sheet inactive. Revision changes and cached receipts prevent stale or
      concurrent duplicate debits. The Letter reference survives eviction's mailbox
      relocation; payment does not undo eviction. Disconnect closes pending UI and
      restores native actions; binding failure disables payment while welfare continues.
      **Two-player check:** open the bill with the host away; pay concurrently; test
      insufficient funds then another press after adding cash; change rent debt while
      the bill is open and confirm the stale quote is declined; retry/reconnect; pay
      after the mailbox moves on eviction; Escape while payment is pending; disconnect
      and pay a later letter in singleplayer. Check cash, debt and envelope convergence.
- [x] R1.7 `Database/Keys::PlayerKeys` — fixed 2026-07-27 (**v86**, rides
      `WorldScalarsState`): `UncleStage`, the GIFU key bool and `ConlineNMBRint` (the
      randomly-generated phone number — now identical on every client) are host-broadcast;
      guests write them back. Physical key items ride the normal item sync.
- [x] R1.8 Non-project vehicle cabin climate — fixed 2026-07-27. Dump-verified the premise
      first: only THREE vehicles carry a `CarTemp*` cabin sim (SORBET, CORRIS, and the taxi
      `JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi`); GIFU/KEKMET/BACHGLOTZ/JONNEZ have no
      CarTemp/HeaterUnit subtree, so they never diverged — nothing to sync. Added the taxi to
      `VehicleClimateConfig` (path prefix + CarTemp marker; all bound var names verified
      identical on the taxi FSMs). Taxi heater buttons have no `Set`/`Set angle` commit state —
      `FireKnobCommitState` degrades to writing Setting/Angle vars only (knob visual may not
      animate remotely; heat output itself is host-streamed via `HeaterUnit`).

**R2 — Synced but wrong** (shipped code that silently does nothing, or the wrong thing)
- [x] R2.1 **`VenttiSync` guest hooks are observers, not suppressors** — fixed 2026-07-24.
      `FsmHook.OnStateEnter` *prepends* a callback but lets the state's own actions run, so a
      guest's Bet/Hit/Stand resolved on local RNG; money reconverged via `WalletState` but the
      SATSUMA wager diverged permanently. Fix: new `FsmSuppressor` primitive in `FsmHook.cs`
      (disable the component, pinning `Fsm.RestartOnEnable = false` so it freezes and resumes
      in place), applied to `Table/GameManager :: Use` on guests — the sole owner of
      `Win car`/`Lose car`/`Win house`/`Lose house`. Verified against the decompiled
      `PlayMaker.dll`: `Fsm.Active` gates on `owner.enabled` and `Fsm.ProcessEvent`
      early-returns when inactive, so a disabled FSM cannot be driven by Update, a targeted
      `SendEvent`, a global transition, or a broadcast. Slots now gate every local money
      control before native mutation and use seeded animation only (v92, R2.12 below).
      Also fixed alongside: the duplicate-hook latch in both classes (`HooksInstalled` was
      all-or-nothing, so one un-Awake FSM made the 5 s probe re-prepend hooks to already-hooked
      states → N duplicate intents per press), and `FleaSaleSync`'s suppression leak (it
      disabled `Sell` and never restored, so a player who guested once could not sell at the
      flea market again *even in singleplayer* until they restarted the game).
- [x] R2.1b Slot keepalives rewriting reels mid-spin — fixed 2026-09-05 using the
      installed asset's action data. Guest retains only the newest state until **both**
      machines are idle; the verified idle states are `Wait player` / `Wait button`.
      `Reset game` is still a mutating state and is treated as busy. Pending state survives
      until a later idle tick, even without another packet. **Two-player check:** spin on
      a guest during host keepalives and confirm symbols/locks settle without mid-spin jumps.
- [x] R2.2 Moose killed by a *guest* never dies for anyone else — fixed 2026-07-27
      (protocol **v82**, `NpcDeathReport` 110). Guest polls its mover corpses (1 s) and
      reports; host validates fresh reporter pose ≤60 m of its own copy, then replays the
      vanilla `Mesh/Collider :: CarHit` death entry (`State 2`) so the game's own death
      actions run host-side and `FlagDead` streams to everyone. Host is idempotent and the
      guest re-sends every 3 s until FlagDead echoes — no send latch (class-A safe). Also
      fixed alongside: a mover that dies while its stream is idle (at rest) now emits a
      one-shot death announce, so at-rest kills reach guests too.
- [x] R2.3 Appliance house-fire never propagates — fixed 2026-07-27 (**v85**). `FlagFire`
      sampled `ActiveStateName == "Fire"`, a one-frame transient, so it was almost never
      true. The dump shows the actual ignition commits are the per-plate `Start fire{,2,3,4}`
      states (`Fire N --FIRE--> Start fire N`); the host now edge-hooks all four via
      `FsmHook.OnStateEnter`, bumps a wrapping `FireCount` (+ `FirePlate`) on
      `ApplianceState`, and guests replay exactly that plate's commit state once per bump —
      role checked at fire time so a guest's replayed entry can't echo. First received count
      only seeds the guest baseline (no re-igniting fires that predate the join). Residuals:
      extinguishing/burn-out still runs per-client, and a fire actively burning at join time
      is not re-ignited for the joiner — both documented, both smaller wrongs than the gap.
- [x] R2.4a Vehicle late-join was climate-only — fixed 2026-07-24. The join snapshot called
      `BuildJoinClimateSnapshots()`, so a late joiner started with whatever engine/fuel/gear/
      damage/tire state its *own* save held. It now sends `BuildJoinVehicleSnapshots()`, which
      reuses the existing full `BuildVehicleResyncMessages()` set (VehicleState + climate).
- [ ] R2.4b Parked-vehicle CRC — implemented 2026-09-05 (**v96**, unreleased),
      **two-player verification remains required before shipping**. `VehicleChecksum` now
      folds concrete damage, tire pressure, drivetrain damage, each wheel's health and
      puncture/rim flags alongside id/flags/fuel. The v91 live fitted-part reader reconciles
      `LiveDamageMask | AppliedDamageMask` against current wear: a bare union would retain
      stale failures after repair. Since v124 only the host reads native engine damage;
      guests checksum accepted host damage, preserving isolated local mount data. Neither
      checksum read advances send sequences or change baselines. Tire pressure rounds to hundredths; truncating a decoded
      `n / 100f` could lose a unit on re-encoding and cause permanent mismatches. Tests cover
      all 256 pressure values across repeated handoffs, role bookkeeping, repairs, every
      concrete damage bit and every condition field through the wire/snapshot path. Dynamic
      wear/climate/RPM and actively owned cars remain excluded. **Two-player check:** let a
      damaged/flat parked car settle, repair it, hand ownership between peers, reconnect,
      then idle for several checksum periods. Confirm a deliberately divergent condition
      requests a vehicle resync, heals and stops requesting; driving/cooling/frost changes
      must not introduce recurring vehicle mismatch logs. See `docs/BUILDING.md`.
- [x] R2.5 `CarRadioSync` station never synced — fixed 2026-07-24 (protocol **v81**). No radio
      Knob FSM has a *float* named `Channel` (the only `Channel` in the subtree is a string on
      the CD player), so `FindFsmFloat("Channel")` bound null and only `Volume` worked — bug
      class **D**. The station value is `Tune`. Also fixed a second fault: `Locate()` took the
      *first* FSM named "Knob" in the subtree, but each radio has three (tuner, radio volume,
      CD volume), so hierarchy order decided which was bound; it now targets
      `StockRadio0/ButtonsRadio/Volume :: Knob` by path, the one FSM carrying both `Tune` and
      `Volume`. Wire field `byte Channel` → `float Tune`, unquantized (the per-station windows
      are not knowable from a dump without action data).
- [x] R2.6 Guest-initiated sleep is ungated — fixed 2026-07-27. `PlayerSleepHook` now probes
      on both roles and decides at fire time (the hook outlives a session, so role is resolved
      live via `SessionManager.Instance`). Host keeps the consent flow; a guest entering
      `Confirm` is bounced straight back to `State 3` (Confirm's own "didn't confirm" route) with
      a rate-limited chat hint to ask the host. Vanilla sleep still works when disconnected
      (`PlayerCount == 0` guard).
- [x] R2.7 Ice race broadcasts no standings for host-driven races — fixed 2026-07-28 (no
      wire change; the state messages existed, only host records were never produced).
      `IceRaceSync.ObserveHost` mirrors `RallySync.ObserveHostStage`: the same FSM edges the
      guest path reports feed `TryAdvance` directly, with the host's own pose plumbed via
      the new `ItemWorldSync.TryGetLocalPlayerPosition` (the missing piece the old
      known-limitation comment named). Mode detection uses the exact local pose — stricter
      input than the shipped guest path's network pose; still on the playtest list like all
      of §1b.
- [ ] R2.8 Taxi fare for guests — **reopened by native audit 2026-09-14**.
      v87 added customer pose, the PayMoney control and finalized customer Cost.
      It did not share live Tripmeter.Price or the full fare/payment lifecycle.
      Native PayMoney sets Paid; the customer credits Tripmeter.IncomeTotal, and
      employment wages later reach the bank. v241/v242 now share the live meter,
      arrival, quote and cash collection with native collected-income save/reload;
      receipt/luggage/payday dependencies keep this open. See J06 and the taxi audit.
- [x] R2.9 `JOBS/Farm/Farmer` proximity gate — fixed 2026-07-28. The farmer's Walker
      (which carries the Logic walking AI and the hand-bone PayMoney button — same
      static-root/moving-child shape as the hitchhiker) is now a `ScriptedMoverDef`, so
      the guest's farmer stands where the host's does and the payday press passes the
      host's proximity gate. Self-limiting if the transform choice were wrong (a still
      mover sends one final and the guest AI is restored). The hitchhiker half needed no
      change: its pose was already streamed, and the host overwriting `Timer.Money` is
      the *correct* host-authoritative flow — a guest's replayed payday press pays the
      host's amount.
- [ ] R2.10 Rally opponent cars run per-client — **do not "fix" this by streaming them.**
      Unlike ICERACE (16 opponents partitioned into four disjoint per-venue sets), RALLY has
      exactly **one** fleet, `RACES/RALLY/RallyCars/RALLYCAR{1,2,3}`, multiplexed across all
      three special stages by `RallyCars::AIdrivers` (int `Stage`, events SS1/SS2/SS3), which
      nothing syncs — while `RallySync` deliberately tracks stage progress *per player*. So
      "host on SS1, guest on SS3" is a supported state, and host-streaming those three bodies
      would freeze the guest's AI and teleport the guest's opponents onto the host's stage,
      hijacking the guest's own rally. Attempted and reverted 2026-07-24. Any real fix must
      sync `AIdrivers` `Stage` first and gate the stream on stage agreement.
      **Installed-action evidence (2026-09-05):** `AIdrivers.Stage` is a sampled
      copy of `RACES/RALLY :: Reset/CurrentStage`, not a durable fleet assignment.
      Each car's Navigation starts also reparent the car, select stage-specific
      waypoint ranges, toggle physics/drive FSMs and wait on local start timers.
      A future stream must carry stage context atomically with poses, preserve ids
      across reparenting, and restore those local lifecycle states on disagreement
      or timeout. Merely adding these bodies to `NpcTrafficSync` remains unsafe.

- [ ] R2.11 Host Ventti property transfers — implemented 2026-09-05 (**v97**, 173),
      **two-player verification pending**. Installed actions set the global ints
      `PlayerKeyRuscko`, `PlayerKeySatsuma`, `PlayerKeyHome` and toggle cabin sleep,
      woodstove hatch and logging access. These native legacy names are not aliases for
      the Corris/Sorbet. `VenttiSync.Properties` broadcasts that state on change/keepalive
      and join, independently of the table observations and with inactive-object lookup.
      The native home-door close edges already use catalogued door sync; subsequent
      opening checks `PlayerKeyHome`. Guests retain packets across late bindings, keep
      their wager resolver disabled, and restore original local keys/access at teardown.
      Missing access bindings remain unknown; explicit false states revoke old access.
      No money, stress, speech or wager actions are replayed; save-point activation stays
      host-local (only the host saves). Catalog bindings and packet/replica tests pass.
      **Two-player check:** host wins/loses each stake with the guest away from the table;
      compare keys, home door access, cabin sleep/stove/logging; join after a transfer,
      reload inactive cabin LOD, then disconnect and verify the guest's original local
      access returns. Inactive-host Ventti play remains R2.12; full-width table state
      is implemented in R2.13 below.
- [ ] R2.12 **Ventti guest actions while the host is away** — host-ledger integration
      implemented 2026-09-05 (**v99**, 175–177); **two-player verification pending**.
      Both peers cut native bet/deal actions and use acknowledged, authenticated
      commands with one-player leases. Host draws a private 52-card deck, escrows
      bets, resolves cash/property hands and publishes concrete card ids. Both peers
      use native textures and pick targets; extra plain meshes support hands beyond
      nine cards. The host's loaded manager keeps native save handling and runs each
      catalog-checked property/NPC outcome once with duplicate accounting actions
      removed. Actor stress is applied once from the matching receipt. Teardown
      refunds an undealt stake or stands on the committed deck; an unrepresentable
      final wallet return is retained as paid native stake. Guest variables, action
      flags, materials and visibility are restored. Legacy control replay (94) is
      retired. Net tests include 500 conservation hands plus 900 wire/replica hands.
      **Still verify in game:** simultaneous bettors, host away with table controls
      inactive, LOD changes, native hand already in progress, all property outcomes,
      actor stress, save/load, late join, repeated reconnect and teardown during a
      committed hand. Guest NPC/furniture pose and sound mirroring is implemented
      in **v100** (178–179): host-observed bones, visibility and thrown furniture;
      exact host-selected sound variations, with bounded/expiring live cues and no
      historical speech in snapshots. Guest physics/AI/animations stay paused and
      originals return on teardown. Verify throws, crawl/walk, audio, late joins
      and missing assets on two peers. Do not mark R2.12 done until the runtime
      matrix passes.
- [ ] R2.13 **Ventti's wire fields are lossy** — observation fix implemented
      2026-09-05 (**v98**, 174), **two-player verification pending**. `VenttiTableState`
      replaces retired 93 with the native float stake, int hand totals, a sequence and
      six explicit native result codes. `LoseText.Status` supplies the result; an empty
      status clears it, and an unknown nonempty status is diagnosed, not guessed.
      Native status can announce a win/loss before settlement: this is an observation,
      never a payout or permission to replay car/house transfers. Catalog `venttiTable`
      owns paths, typed variable names and result strings. Guests retain complete state
      across late bindings, reject stale/invalid packets and restore their original
      variables on teardown. Inactive discovery does not activate host gameplay.
      Join snapshots leave the connected-guest broadcast baseline untouched. In v99,
      observation 174 yields to full ledger state 175 once the table is adopted.
      **Still verify:** >255 mk and fractional stakes, all six result codes and reset,
      late table binding, join after a result, inactive LOD and repeated reconnect.
      Leased controls, host settlement and native card/result presentation now have
      a v99 implementation in R2.12; runtime verification remains required.

**Round 3 (2026-07-27) — full class-A/B/C/D/E sweep over the v57–v81 subsystems** (they
were written *after* the 2026-07-20 sweeps, so those lessons had never been checked here;
class B and D came back clean everywhere, the rest did not):

- [x] R2.14 **Vehicle damage/condition: cross-owner sequence blackout, join gap, wheel
      drift** — fixed 2026-07-27 (**v82**). (a) Receivers deduped `VehicleDamage`/
      `VehicleCondition` sequences per *vehicle* while every client counts per *sender*, so
      each ownership handoff had every receiver rejecting the new owner's low counter for
      minutes (`LastDamageSequenceOwner`/`LastConditionSequenceOwner` rebase, mirroring
      `ItemWorldSync`'s `LastRemoteSequenceOwner`). (b) Neither message was in the join
      snapshot despite the R2.4a comment claiming so — a parked seized car stayed healthy for
      late joiners forever (no owner to keepalive it); the host now snapshots its best-known
      mask (`Live ∪ Applied`) + live condition FSM reads for every vehicle. (c)
      `ApplyWheelDiscrete` diffed against apply *bookkeeping* instead of the wheel FSM's
      actual state, so a locally-drifted (or save-borne) flat could never be healed by
      keepalives — now diffs `ReadWheelState`. Also: a fresh claimer now seeds
      `LiveDamageMask` from `AppliedDamageMask` (no more mask-0 "un-breaking" + re-claim
      re-fire storm).
- [x] R2.15 **WantedSync guest crimes were erased** — fixed 2026-07-27. Reports latched
      `_reportedUpTo` at *send* with no re-send; a transiently rejected report (host FSM not
      bound yet) was then made unrecoverable by `Apply` overwriting the local counter *and*
      re-baselining. Evidence now lives in a pending-delta accumulator the broadcast cannot
      stomp; the host queues reports that arrive before its FSM binds (reliable channel +
      queue = exact-once without an ack protocol); first observation seeds the baseline so a
      save's pre-existing counters are not replayed as fresh crimes.
- [x] R2.16 **JailSync guest-offender inversion** — fixed 2026-07-27 (**v83**,
      `JailState` + `JailedPlayerId`). The countdown runs only on the jailed client (the
      file's own doc said so), yet the host keepalived its idle `DaysLeft` = 0 over the
      jailed guest every ≤20 s. Now the jailed client owns the record and reports it; the
      host adopts (30 s TTL) + relays; the jailed client ignores broadcasts about itself.
- [x] R2.17 **Gambling/HeatSource intent latches keyed per player only** — fixed
      2026-07-27. Guests count intents per *machine/source*, so after using machine A,
      machine B's fresh low counter read as stale and the second slot machine (or a second
      stove) went permanently dead for that guest. Latches now key (player, machine).
- [x] R2.18 **RepairShopSync shared one guest-intent latch + one pending-order slot across
      all guests** — fixed 2026-07-27; per-player dicts mirroring `MailOrderSync` (guest B's
      first Fleetari order was silently rejected while guest A had ever ordered, and
      concurrent confirms evicted each other).
- [x] R2.19 **Per-player host latches survived reconnects** (systemic, 14 subsystems) —
      fixed 2026-07-27. A rejoining guest keeps its PlayerId but restarts every counter, so
      each subsystem's stale latch rejected everything it sent as "stale" until it
      out-counted its previous life. Every handshake admission now calls
      `WorldSyncManager.OnPlayerAdmitted` → `ForgetPlayer` on all per-player dedup state
      (wanted, jail, gambling, ventti, fleaSale, repairShop, mailOrders, heat, police,
      rally, iceRace, homeStereo, purchases, item spawns, passenger seats).
- [x] R2.20 **Kilju: a guest could not brew** — fixed 2026-07-27. Guest brew edits streamed
      only for *held* buckets; a bucket resting on the floor is never motion-claimed, so the
      host's 3 s keepalive reverted every lid flip. A local lid flip (the one purely
      player-driven flag — the floats advance by per-client simulation and must NOT trigger
      claims) on an unowned resting bucket now claims it via
      `ItemWorldSync.TryClaimForInteraction`; the at-rest release hands it back. Receivers
      that own a bucket now also ignore remote `BrewState` for it.
- [ ] R2.21 **Lottery ticket purchases and claims lack proven host authority** — reopened
      2026-09-06 after installed action inspection. **Form isolation fixed 2026-09-06,
      runtime verification pending:** removed the three erroneous `buys[]` rules for
      Lotto docked-ticket inspection, BuyLotto and BuyMegaveto. Their guard states
      Wait button/Wait button 2 are entered on hover and still wait for
      GetMouseButtonDown/GetButtonDown to emit USE. The old entry hooks sent repeated
      requests before a press, aborted the guest's wait, and broadcast native form
      opening as a shared purchase result. These three Use FSMs now remain local;
      actual Sheets/LottoTicket/Pay and Sheets/MegavetoTicket/Pay bindings remain.
      No wire layout/meaning changed; the existing catalog hash rejects mixed catalogs.
      `check_fsm_bindings.py --purchase-catalog` now detects this input-wait guard
      mistake from action evidence; incomplete dumps/templates remain unverified.
      **Lotto transaction implementation 2026-09-06 (v103, 181–183), runtime
      verification pending:** dedicated `LottoTicketSync` replaces generic Lotto Pay
      replay. It captures completed paid rows, validates current round and fresh
      player proximity, and instantiates the host factory's actual prefab with a
      host-issued persistent ID. Partial unpaid rows are cleared; an open host form
      is preserved. Host native ticket calculation/save handling stays authoritative.
      The actual claim path is VoittousArea/TrashTrigger: under 1,000 mk goes to
      shared cash, at/above 1,000 mk to shared bank. Claims identify a host ticket,
      require the host body near the box, wait for complete native calculation and
      retire the ticket before crediting money. Copied request receipts and lifetime
      retirement prevent repeated/concurrent claims and guest reconnect duplicates.
      Host ticket metadata joins/resyncs with stable item IDs; movement uses normal
      item authority. Guests hide their local saved tickets and create native replicas
      with load/save/delete/calculation disabled, restoring local tickets on teardown.
      The offline extractor can now select `--asset sharedassets3.assets`, so the
      spawned prefab was inspected alongside the scene instance and claim actions.
      **Still open:** two-player form/purchase/claim timing, host-away behavior,
      saved ticket reload/deletion, late binding and repeated reconnect (BUILDING.md).
      An uninitialized host factory waits without charging; first-visit/loading
      behavior needs a runtime check. Bank statement/achievement presentation is
      still local-only/unimplemented for the new claim path. Megaveto selections,
      ticket identity and claims still need their own host ledger; its generic
      Pay rule and native collection path remain, so R2.21 and 1.4 stay unchecked.
      **Megaveto prerequisite (v104):** complete host odds/results/pairings and
      standings are now synchronized (R1.4), replacing the insufficient one-match
      scratch values. Guest season calculation is paused; a future ticket ledger must
      use host ticket winnings rather than reintroduce guest CHECKMEGAVETO replay.
- [x] R2.22 **A repeat phone call with the same Topic may never broadcast** — fixed
      2026-07-28. The ringing object deactivates once a call ends (`Disable phone`
      terminals under `FunctionsDisable`), so the host now re-arms `_lastTopic` whenever
      the phone object is inactive — safe regardless of whether vanilla clears `Topic`.
      Known residual (pre-existing, unchanged): a guest whose own phone object is idle when
      the event arrives fires `SendEvent` at an inactive FSM (inert, R2.12 class); making
      that ring for real needs the activation flow from the fresh dump.
- [ ] R2.23 **Engine damage authority and saved-part integrity** — v91 added known
      wear and concrete failure masks, but the v124 audit found damage references still
      point to mount Data with a retained guest-local ActivePart. Guest driving could
      publish stale saved wear; remote replay could mutate that original and reroll
      PISTON's OILPAN/BLOCK consequences. **v124 remediation implemented:** only the host
      publishes VehicleDamage, independent of driver; guest reports are rejected. Guests
      retain accepted condition/checksum state while native Damages is paused until
      restart, including after disconnect, without replay or mount Wear writes. Forced
      discovery precedes saved-part isolation; failed suppression defers moving originals.
      Other vehicle streams keep their existing authority.
      **Still verify:** healthy/repaired state, join/resync and driver handoffs with
      different guest saves; preserved originals; host wear under guest driving. Full
      guest engine operation and visible physical failure remain unestablished. At v124,
      1,242 protocol tests, 18 launcher tests and 177 isolated native checks pass,
      including 23 damage checks. Both Net targets, Core (Debug/Release) and the probe
      build cleanly. [Native evidence](../build/vehicle-damage-smoke/result.json) records
      zero failures and matching tested Debug hashes; see BUILDING.md. The vehicle
      CRC acceptance gap R2.4b remains open.

**Round 3 self-review (2026-07-28)** — an adversarial agent pass over everything round 3
itself wrote (v82–v88); 13 findings, 10 fixed the same day:

- [x] Kilju lid-flip was deterministically lost over Steam: the claim rides the unreliable
      channel, `BrewState` the reliable one, and the receive pump drains reliable FIRST —
      the host rejected the one-shot state before the claim registered. `TryAcceptGuestState`
      now accepts `RemoteOwner == NoOwner` (the vehicle damage/condition precedent).
- [x] JailSync could adopt a jailed guest's record but never RELAY it: `JAIL/Functions` is
      inactive until an arrest, `GameObject.Find` can't see inactive objects, and the
      broadcast was gated on Ready. Locate now scans `FindObjectsOfTypeAll`, and a live
      guest record broadcasts/snapshots without needing the host's own FSM. Two
      concurrently-jailed players also no longer stomp each other (a serving client ignores
      foreign jail broadcasts).
- [x] PhoneSync could never make a guest's idle phone ring (same inactive-object Find gap +
      SendEvent at a deactivated FSM is a silent no-op): Locate scans all FSMs, Apply
      activates the ringing object before writing Topic. Whether the conversation routes
      fully vanilla needs R3.1b action data — but ringing at all is strictly better than
      never.
- [x] Per-ITEM same-sender sequence latches survived a rejoin (the OnPlayerAdmitted reset
      only covered per-player dicts): a crashed-and-rejoined guest's fresh counters were
      stale-dropped per item on EVERY client. `ItemWorldSync.ForgetPlayerItemSequences`
      downgrades all six latch families to the owner-change sentinel, and OnPlayerAdmitted
      now also runs on guests when the relayed `PlayerSpawn` arrives.
- [x] Scripted movers lacked the sequence-reset escape rigidbody NPCs have (a late joiner
      meeting a >32767-packet moose stream dropped it, corpse announcements included) —
      `StaleStreak` fallback added; dead movers are also now in the join snapshot (the
      one-shot death announce predates a late joiner).
- [x] Moose death-report radius 60→150 m (validation runs against the reporter's CURRENT
      pose; by the 3 s retry a highway-speed car had left the radius → permanent rejection).
- [x] WorldScalars Ready is all-of-three (a partial bind broadcast UncleStage=0 over real
      progression; 0 is a valid value so no field guard can help) + PrimeInterest/Conline
      joined the change predicate.
- [x] Kilju/FluidContainer pending-state now clears on level change (path-derived ids
      repeat after a reload).
- [x] R2.24 **A guest's own oven fire was invisible to the host** — fixed 2026-07-28
      (**v89**, `ApplianceFireReport` 161). The guest's `Simulation::Data` rolls its own
      `FireHazard` RNG even over synced heats; a guest ignition is now reported (paced 30 s
      per oven, per-player seq with rejoin reset) and the host replays the plate's commit
      state, single-sourcing the house fire — the moose-report pattern. The guest's replay
      of a HOST ignition is suppressed from re-reporting (1 s echo window around
      `ReplayIgnition`).
- [x] R2.25 **Rally crossing reports were lost after transient rejection** — implemented
      2026-09-06 (**v101**, extended 86/87), **two-player verification pending**.
      Guest crossing edges remain queued in checkpoint order until an exact host
      acknowledgment; retries reuse their connection token and sequence. Host rejects
      malformed/out-of-order reports without advancing dedup state, and a duplicate
      start cannot reset elapsed time. A short history of host-observed marker/driver
      proximity handles report/pose ordering delays without trusting guest clocks or
      coordinates. Evidence expires from the original pose timestamp. Complete native
      stage binding prevents partial scans from finishing a race early. Installed
      actions also exposed an unused `Checkpoint` bool in every marker: completion
      now observes the catalogued terminal `Idle` state, which survives Timing's
      flag reset at finish. Bindings and the 4/6/4 marker layout are catalog data.
      Same-connection scene rebinds preserve report identity/counters. Initial save
      flags are baselines, so reconnect does not invent a new start; admission preserves
      the host race while clearing old report identity. Per-player uint revisions
      prevent one driver's stream from aging another's state out; final times freeze
      and join snapshots preserve pending host finish broadcasts. The Net suite covers
      300 seeded races with rejection/ack-loss, plus malformed data, wrap, reconnect,
      evidence expiry and queue timeout/recovery. No opponent-car synchronization or
      native result/prize mutation is added; R2.10 remains open. Verify native trigger
      timing, delayed poses, reconnect with matching/mismatching native progress and
      SS1/SS2/SS3 under two-player gameplay (BUILDING.md).
- [x] R2.26 **Lotto sent a save key instead of winning numbers** — implemented
      2026-09-06 (**v102**, `LottoDrawState` 180; 96 retired), **two-player verification
      pending**. `UTNational7` contains `Lotto7`, the save tag for NationalLine7.
      Actual results are four live integer ArrayLists: Results (7), ResultsBonus (3),
      ResultsLinesWinnings (5), ResultsLinesWon (5). Complete stable-state snapshots
      now carry them with round/ticket round, all three pots, DrawDone and teletext
      visibility. Guest Numbers FSM pauses after native loading; pending snapshots
      survive late binding, and teardown restores the local draw before resuming.
      Both join and FSM-group resync include the draw; same-round list/prize changes
      publish too. Display refresh avoids freezing the native Reset points hide.
      Net tests cover wire sizes, partial/type-invalid native lists, invalid numbers,
      full integer values, sequence wrap/order, copied pending state and change
      detection. Native ticket events are not replayed; R2.21 remains open.
- [ ] R2.27 **Shared-item removal and replay recovery** — implemented 2026-09-06
      (**v105**, runtime verification pending). Live removal previously disappeared
      if the guest had not materialized the body yet; deferred spawning could then
      resurrect it. Removal now establishes a terminal session ID, checked by
      scanning, offered-clone binding, deferred creation and replay. Item-group
      resync now includes removal chunks and fresh live spawn manifests after poses.
      Replays repair missing bodies even when the spill was previously received;
      existing bodies are not duplicated or teleported. Repeated pending replays
      coalesce, invalid manifests cannot consume dedup keys, and destroyed host
      bodies/retired IDs are excluded from replays. Session teardown clears lifecycle
      state. `ItemSpawnLifecycleTests` includes 200 seeded loss/removal/replay runs.
      Use BUILDING.md for two-player delayed spawn, consumed item, missing template,
      repeated resync and reconnect verification. The excluded SPAWNITEM factories
      in 7.2 still need separate real-time materialization hooks.
- [ ] R2.28 **Part-settle sync turned healthy parts into almost worn-out parts** —
      implemented 2026-09-06 (**v107**, native checks pending). Actual VIN133 Data
      initializes Wear with RandomFloat(90,99); ReadPartVars clamped every value above
      1 to byte 255, and ApplyPartState decoded that as 1. PartState and part snapshots
      now append and apply full native floats; legacy bytes remain wire hints only.
      World CRCs distinguish native wear values instead of saturating them. All
      readable parts, including zero values, are snapshotted; deferred values retry
      when registered FSMs become available, and newer applies clear older pending
      data. Object-state replies include native scalars alongside FSM state. Guest
      reports request a deferred host reading and cannot alter its wear, installed
      flag or tightness; accepted install/bolt events and vehicle damage retain their
      existing authority paths. Protocol tests cover native values, legacy framing,
      chunk pairing, malformed data, host authority and checksum convergence.
      **Still verify:** guest/host bolt turns, late joins, delayed FSM activation,
      repair and replacement, guest driving wear, corrupted-replica resync, and saved
      condition after reconnect. Package spawning/opening is still unfinished.
- Accepted residuals (documented, not bugs to fix): a crime incremented in the same frame a
      host broadcast applies can be missed (sub-frame window); a SleepTrigger variant
      without "State 3" would hint-but-not-block a guest (all dump variants have it);
      `PaidAmount`/`Weekly`/`KMsDriven` ride keepalives only (delayed, never lost).

**R3 — Tooling debt blocking the above**
- [x] R3.1a Dumper emits `globalTransitions` (2026-07-24, tools 0.2.0). Per-state
      `actionTypes` was already emitted — it just post-dates the dump on disk.
- [ ] R3.1b **Take a fresh F9 dump.** `catalog/dump-23268598.json` is from 2026-06-13 at
      `toolsVersion 0.1.0`, so it has *no* action data and *no* global transitions — 0 of its
      8,615 records carry `actionTypes`. That is why so much of §R2 reads "unverifiable":
      you cannot tell from the repo whether a state charges money, whether
      `SendEvent("NORMAL")` is accepted from a resting state, or where a global-only event
      like `SaleTable::Logic`'s `RENT` lands. Needs the game running, so it is a **human
      step**: launch with WinterMP.Tools deployed, press F9, commit the new dump.
      **Do this before R2.1/R2.3** — both hinge on questions only a richer dump can answer.
      2026-07-27, tools **0.3.0**: the dump now also carries a top-level `globalVariables`
      section (PlayMakerGlobals names + scalar values) — the fresh dump will additionally
      answer *which global is the bank balance* (R1.1) and make global bindings checkable
      by `tools/check_fsm_bindings.py`.
      **2026-09-05:** `tools/extract_fsm_assets.py` can now decode the installed Unity 5
      assets offline, including typed globals, global transitions, action field names,
      scalar arguments and referenced scene objects. This removes the *static-evidence*
      blocker for banking, debt-letter calculation, gambling and damage mapping. It is
      not a runtime dump: initialization, save-loaded values and multiplayer effects still
      require the game. `check_fsm_bindings.py --globals <extract.json>` now checks literal
      global bindings separately from local variables (a local `Money` can no longer hide
      a missing global `Money`). See `docs/BUILDING.md` for reproduction.

---

## 2. Task specs

Template — **Impact · Game truth · Model · Protocol · Touch · Done when · Watch · Deps.**

### Phase 0 — Housekeeping

#### 0.1 · Correct PLAN.md coverage overstatements  `TODO`
- **Impact** Docs claim coverage that doesn't exist → future agents skip real gaps.
- **Do** Edit `PLAN.md` §4.4: (a) Death row — remove "moose-hit death already in DeathSync" (0 refs in `DeathSyncManager`); point moose death at task 7.1. (b) Car-assembly row — "wear/tuning as synced FSM vars ✅" → only bolt-settle wear is synced; driving wear/breakage → tasks 2.1–2.2. (c) Jobs/flea row — "flea market" is buy-side only; selling → 3.1. (d) Racing row — "enroll" is ice-race only; rally enroll → 5.1.
- **Protocol** none. **Touch** `PLAN.md`. **Done when** the four claims match reality. **Watch** — **Deps** none.

#### 0.2 · Fix the vehicle-registration RequireRoot gate  `TODO`
- **Impact** Any drivable whose rigidbody has a parent is silently dropped — the taxi today, any future nested drivable tomorrow.
- **Game truth** `JOBS/TAXIJOB/MACHTWAGEN` is a full drivable sim car nested at depth ≥2.
- **Model** `VehicleCatalogConfig.IsVehicleRoot` rejects `transform.parent != null` *before* the mass≥150 / name-prefix test. Relax so a body that carries the vehicle-simulation subtree (Engine/Electricity/Steering FSMs) registers regardless of nesting — OR add an explicit allow-list entry for the taxi and gate on "has drivable subtree." Prefer a structural check (has `Simulation/Engine`) over a name list so it generalizes.
- **Protocol** none (registration only). **Touch** `src/WinterMP.Core/Catalog/VehicleCatalogConfig.cs`, `SyncCatalog.cs`.
- **Done when** MACHTWAGEN registers as `IsVehicle` and streams via the existing `VehicleWorldSync` path; the 7 existing families still register unchanged. **Watch** E (don't let a parented job-vehicle's stream contaminate an owned car — the existing owner guards apply). **Deps** none. Unblocks 2.4, 3.2.

### Phase 1 — Economy integrity

#### 1.1 · Slot machines  `TODO`
- **Impact** HIGH. Stake, win RNG, and cashout all mutate the single shared `Money` global with no intent → a guest's win/loss flip-flops when the host's ~2 s `WalletState` overwrites it; reels resolve on per-peer RNG so even the displayed outcome differs.
- **Game truth** `STORE_AREA/Stuff/LOD/GFX_Pub/SlotMachinePub/Buttons/{PayMoney,Start,LockRoll1-3}::Use`; `PERAPORTTI/…/SlotMachine/Buttons/PayMoney::Use`. Reels `Roll/Roll1-3`.
- **Model** Host-authoritative machine: the **host** runs the spin RNG and resolves the payout; the guest sends a *play intent* (stake + which machine, proximity-validated) and applies the host's result. Follow `HeatSourceSync` intent shape; route the wallet delta through the existing host wallet (never a direct guest `Money` write). Reuse `PurchaseIntent`/guarded buy path for the stake if it maps cleanly; otherwise a small `SlotMachineIntent`/`SlotMachineResult` pair.
- **Protocol** new pair in `93–99`; +version; PROTOCOL.md rows. **Touch** new `Sync/SlotMachineSync.cs`; wire in `WorldSyncManager` + `SessionManager.Messages`.
- **Done when** two players at one machine see identical reels + one authoritative balance; a guest win survives the next `WalletState`. **Watch** A (result latch on host ack), C (no phantom pending spin). **Deps** none.

#### 1.2 · Ventti blackjack (incl. car wager)  `TODO`
- **Impact** HIGH. Money betting **and** wagering the Satsuma car — resolves on per-client RNG; car stake can desync ownership.
- **Game truth** `PERAJARVI/Kunnalliskoti/Functions/RoomVenttiPig/Ventti/Table/GAME/Gamestuff/{Bet,HitPlayer,HitHouse,Stand}::Use`, `Table/GameManager::Use`; `Bet` exposes `Bet/BetMax/BigBetLimit`, a `Cars` state + `SATSUMA` event.
- **Model** Host owns the deal: host shuffles/draws and decides win/lose; guest sends bet/hit/stand intents; host applies wallet delta and, if a car is staked, drives the ownership transfer through the existing vehicle-ownership model (host reassigns `RemoteOwner`/registration — never a local guest write). Same State+Intent shape as 1.1.
- **Protocol** reuse the 1.1 pair if generalizable to "table game," else a `CardGame*` pair in `93–99`; +version; PROTOCOL.md. **Touch** `SlotMachineSync` (rename to `GamblingSync`) or a sibling `Sync/VenttiSync.cs`.
- **Done when** identical hands both sides; wallet + (if wagered) car ownership converge on the host result. **Watch** A, C, E (car-ownership write is host-only). **Deps** none (do after 1.1 to share the model).

#### 1.3 · Electricity + phone bills → cutoff / blackout  `TODO`
- **Impact** HIGH. Native `CUTOFF` writes the effective `HouseElectricity` global false every frame while leaving `MainSwitch` unchanged. v223 fixes the resulting missed blackout and pauses loaded guest electricity timers. v225 replaces broken generic electricity purchases with acknowledged invoice payments; natural envelope access, phone and save-reload acceptance remain outstanding.
- **Game truth** `Systems/ElectricityBills1·2::Data` (`NextBill/NextCutoff/UnpaidBills/Price/MainSwitch`; events `BILL/CUTOFF/ELEC_CUTOFF`) consumed by ~16 appliance FSMs (`HouseElectricity::Status` Blackout/Lamp, radiators, fridge, oven, TVs, sauna). `Systems/PhoneBills1·2::Data` (`PhonePaid`). Pay buttons `Sheets/{ElectricityBill,PhoneBill}*/Pay` (`Check money → BUY → Date`).
- **Model** Host owns the bill ledger: broadcast `UnpaidBills` / cutoff state on change + join; suppress the guest's local bill FSM from acting (drive it from the host stream). Electricity Pay uses `utilityPayments`, a revisioned invoice and a receipt; validate proximity at the native envelope, debit once and execute host Pay bills without opening its sheet. v230 gives phone meters host usage/tariffs and the same revisioned receipts, with validated native charge/reset actions. Broadcast effective supply separately from `MainSwitch` so a bill cutoff cannot be mistaken for available power.
- **Protocol** `UtilityBillState` (95, v225), supply + switch/bill flags + invoice revision; electricity uses `UtilityPaymentIntent` (211) / `UtilityPaymentResult` (212). **Touch** `Sync/UtilityBillSync.cs` / `.Payments.cs` / `.PaymentBindings.cs`, `UtilityPaymentLedger`, catalog `utilityPayments`.
- **Done when** bills, payment, and blackout are identical on both machines; unpaid electricity cuts *both* homes' power. **Watch** B (join snapshot must not advance the delta baseline), C (retire a paid bill), D (verify `MainSwitch` is a Bool). **Deps** none. Pairs with 6.4.

#### 1.4 · Lottery + gambling tickets  `PARTIAL`
- **Implemented** v102 `LottoDrawState` (180): complete host Lotto arrays, rounds, pots, winner counts/prizes, guest draw suppression, late binding, join/resync and teardown restoration (R2.26). Native two-player validation is still pending.
- **Game truth** `Systems/Lottery::Numbers` stores Results/ResultsBonus/ResultsLinesWinnings/ResultsLinesWon as live integer ArrayLists. `UTNational7` is a save tag. Megaveto uses hockey data rather than these Lotto lists.
- **Implemented** v103 Lotto selected rows, native host-issued persistent tickets, acknowledged purchases and one-time cash/bank claims (`LottoTicketSync`, 181–183). Guest replicas use the native prefab and regular item transforms; native host save handling persists outstanding tickets.
- **Implemented** v104 complete hockey betting collections, pairings/results and standings (160), with guest generator suppression and local restoration. This supplies Megaveto's shared betting data; it does not implement its tickets.
- **Remaining** R2.21: runtime/save verification of Lotto and the separate Megaveto purchase/claim ledger. Bank statement/achievement presentation for Lotto claims is not implemented.
- **Done when** both peers see the same draw, purchased tickets belong to the correct player/round, duplicate claims never credit twice, and reconnect preserves outstanding tickets. **Watch** A, C. **Touch** `LotterySync.cs`, ticket FSM/catalog bindings, wallet/host ledger; bump protocol again for ticket semantics.

### Phase 2 — Shared-car integrity

#### 2.1 · Engine damage authority and guest engine behavior  `PARTIAL`
- **Passenger vehicle discovery recovery (local/unreleased, still v191)** Cached seats now follow the current registered Corris/Sorbet body. Replacement/removal releases local riders, replaces remote anchors while preserving accepted seat history, and refreshes host validation before entry proximity or continuing-seat existence checks. Validation passes 3,509 Net tests, 18 launcher tests and 3,613 native checks (16 new; all 3,597 previous retained). Full scene reconstruction, physical seat/door and live two-player acceptance remain open; taxi player seating is not enabled. [Validation and limits](BUILDING.md#passenger-vehicle-discovery-recovery-still-protocol-191-unreleased).
- **Passenger death and respawn (local/unreleased, v191)** Death releases local seat parenting and retires host/observer occupancy without resetting claim history. Dead keepalives stay rejected; respawn requires a fresh entry, and group death clears every seat. Native controller destruction and new respawn parenting are preserved. Validation passes 3,509 Net tests, 18 launcher tests and 3,597 native checks (17 new; all 3,580 previous retained). Full native death/save/load, physical seat/door and live two-player survival acceptance remain open. [Validation and limits](BUILDING.md#passenger-death-and-respawn-protocol-191-unreleased).
- **Passenger cabin heating (local/unreleased, v190)** Available current cabin temperature now feeds both native body-temperature reader branches for local seated passengers. The native warmth calculation keeps control; missing/expired data, exits and ownership changes restore native lookup. VehicleClimate adds availability flag 8 without changing its 23-byte layout. Validation passes 3,503 Net tests, 18 launcher tests and 3,580 native checks (72 new; all 3,508 previous retained). Full physical seat/door, loading/respawn and live two-player winter-survival acceptance remain open. [Validation and limits](BUILDING.md#passenger-cabin-heating-protocol-190-unreleased).
- **Native body warmth persistence (local/unreleased, v189)** Reports and last-position reconnect restoration now use native PlayerTemp, with explicit availability and known-zero/late-binding support. Legacy air-temperature samples are retired while other needs and poses survive; the pure profile codec keeps dirtiness, BAC and warmth independently. Validation passes 3,467 Net tests and 3,508 native checks (13 new; all 3,495 previous retained). Passenger heat delivery, full spawn/seat/door flow and live winter-survival acceptance remain open. [Validation and limits](BUILDING.md#native-body-warmth-persistence-protocol-189-unreleased).
- **Passenger condensation inputs (local/unreleased, v188)** Player poses carry finite available sweat. Fresh, living remote passengers accepted in the car contribute to its current climate owner's native fogging rate, retaining the dry minimum and cabin ceiling without writing global sweat/local entry or granting ownership. Validation passes 3,429 Net tests, 18 launcher tests and 3,495 native checks (56 new; all 3,439 previous retained). Passenger body warmth, full seat/door flow and live rendered thermal/ownership acceptance remain open. [Validation and limits](BUILDING.md#passenger-condensation-inputs-protocol-188-unreleased).
- **Native condensation presentation (local/unreleased, v187)** Frost now drives material alpha; white tint cannot become full fog or corrupt native sweat-rate scratch. Cabin degrees no longer overwrite the divided defrosting rate. Fog is retired in place; tint, shader cutoff and exterior panes remain independent. Validation passes 3,391 Net tests, 18 launcher tests and 3,439 native checks (45 new; all 3,394 previous retained). Passenger sweat inputs follow in v188; warmth and live rendered climate acceptance remain open. [Validation and limits](BUILDING.md#native-condensation-presentation-protocol-187-unreleased).
- **Vehicle climate occupancy isolation (local/unreleased, v186)** Received occupancy no longer writes native local entry, preventing an observer from claiming a car after the climate hold expires. Reports include accepted passenger seats; guests filter departed players and clear old seats on slot readmission. Native entry/reset and driver parenting remain local. Validation passes 3,391 Net tests, 18 launcher tests and 3,394 native checks (51 new; all 3,343 previous retained). Passenger sweat/fog/warmth, full seat/door flow and live multiplayer acceptance remain open. [Validation and limits](BUILDING.md#vehicle-climate-occupancy-isolation-protocol-186-unreleased).
- **Vehicle climate ownership (local/unreleased, v185)** Climate follows the established vehicle owner, with parked host fallback. Passengers/nearby guests cannot publish competing state; stale reports cannot relay; per-sender history survives handoff. Reliable final climate precedes release, snapshots copy accepted guest state, and obsolete holds stop on ownership changes. Validation passes 3,381 Net tests, 18 launcher tests and 3,343 native checks (22 new; all 3,321 prior retained). Full native climate behavior, passenger-only occupancy, scraping intents and live driving/parking/join/reconnect acceptance remain open. [Validation and limits](BUILDING.md#vehicle-climate-ownership-protocol-185-unreleased).
- **Independent window ice (local/unreleased, v184)** VehicleClimate 61 carries six separate exterior cutoffs and availability (23 bytes), preserving individual scraping and rear-heat results during capture, snapshots and remote presentation. The loaded catalog now includes the verified taxi climate paths. Validation passes 3,337 Net tests, 18 launcher tests and 3,321 native checks (66 new; all 3,255 prior retained). Full climate authority, new scraping intents and live driving/join/handoff acceptance remain open. [Validation and limits](BUILDING.md#independent-window-ice-protocol-184-unreleased).
- **Guest rear-window element (local/unreleased, v183)** State 200 appends independent host body HeatingSprites flags (12 bytes). The native rear defroster now combines host wiring and element presence with the existing switch before consumption and heat additions. Body configuration remains untouched; load readiness, revision/copy handling, keepalive and teardown are covered. Validation passes 3,305 Net tests, 18 launcher tests and 3,255 native checks (46 new; all 3,209 prior retained). Full climate authority, physical strip presentation and live driving/reconnect/handoff acceptance remain open. [Validation and limits](BUILDING.md#guest-rear-window-heating-element-protocol-183-unreleased).
- **Guest heater hose inputs (local/unreleased, v182)** Existing state-195 hose bits 2/3 supply native inlet/outlet Installed reads. Native Cooling already consumes host radiator coolant; its clamp/removal logic and the heater's 0.5 threshold/two-hose gate now work together with host inputs. Saved guest hoses/radiator stay intact. Validation passes 3,279 Net tests, 18 launcher tests and 3,209 native checks (35 new; all 3,174 prior retained). Rear heating-element inputs, physical replication, full climate and live driving/reconnect/handoff acceptance remain open. [Validation and limits](BUILDING.md#guest-heater-hose-inputs-protocol-182-unreleased).
- **Guest heater/defroster wiring (local/unreleased, v181)** State 193 now supplies host heater-control, heater-unit and rear-window circuit connections (source IDs 9–11). Native guest read helpers preserve saved wiring, callback timing and source caching. The native heater requires both supplies plus installation; rear defroster demand retains its own circuit gate. Validation passes 3,269 Net tests, 18 launcher tests and 3,174 native checks (34 new; all 3,140 prior retained), with clean builds. Hose/heating-element inputs, physical replication and live driving/handoff acceptance remain open. [Validation and limits](BUILDING.md#guest-heater-and-rear-defroster-wiring-protocol-181-unreleased).
- **Guest heater condition (local/unreleased, v180)** Host state 200 supplies settled heater installation/wear to native guest readers. Saved heater Data stays paused and external scalar writes stay blocked; native blower arithmetic and the Wear<3 broken decision use host condition. State is independent of physics ownership, with join/resync, revision checks, keepalive and cleanup. Validation passes 3,243 Net tests, 18 launcher tests and 3,140 native checks (48 new; all 3,092 prior retained), with clean builds. Heater wiring/hose inputs, physical heater/battery replication, remaining accessory loads and live driving/handoff acceptance remain open. [Validation and limits](BUILDING.md#guest-heater-condition-protocol-180-unreleased).
- **Guest starter wear (local/unreleased, v179)** Native Fuel Mixture entry/update duration reaches the host in reliable request 199 while saved guest wear stays unchanged. Ownership, sequence/time budget and native battery/part/wiring checks gate the host's native rate and current mounted durability. Final release flushes pending time; rejected work cannot replay after repair. Native mounted publication passes wear to physical part Data. Validation passes 3,213 Net tests, 18 launcher tests and 3,092 native checks (37 new; all 3,055 prior retained), with clean builds. Other accessory loads, physical battery replication, complete start/drive/handoff and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-starter-wear-protocol-179-unreleased).
- **Guest starter battery draw (local/unreleased, v178)** Five native cranking callbacks count guest loaded/unloaded draw while preserving the guest battery. Reliable request 198 carries bounded counts; the host validates ownership, replay/work limits and its own battery/starter/wiring/flywheel before native drain. Final ownership release flushes pending draw, and rejected native work cannot replay after repair. Existing states retain their layouts; next ID is 199. Validation passes 3,182 Net tests, 18 launcher tests and 3,055 native checks (39 new; all 3,016 prior retained). Other accessory demand, starter wear, physical battery replication and live handoff/electrical acceptance remain open. [Validation and limits](BUILDING.md#guest-starter-battery-draw-protocol-178-unreleased).
- **Guest accessory battery inputs (local/unreleased, v177)** Host BatteryState 194 appends mount ChargeMax (15 bytes including ID). Connected guest lights, fan, wiring and current/power calculations read host Installed/Charge/ChargeMax into local outputs, preserving the saved battery. The audit covers 22 native reads, 19 beyond existing engine proxies. Unseeded/removal and reconnect behavior, cache/fallback, native voltage comparisons and charge clamping are checked. Validation passes 3,154 Net tests, 18 launcher tests and 3,016 native checks (71 new; all 2,945 previous retained), with clean builds. Guest starter/accessory demand and live electrical acceptance remain open. [Validation and limits](BUILDING.md#guest-accessory-battery-inputs-protocol-177-unreleased).
- **Guest battery external-write protection (local/unreleased, v176)** The battery mount now blocks external native float writes from any consumer, covering 31 audited charge/ChargeMax/terminal writes, including 21 outside the previous writer list. Other destinations and consumer calculations remain native. Target identities persist through movement, metadata loss and disconnect, and new targets are recognized before discovery. Battery projection/admission requires the catalog flag. Validation passes 3,134 Net tests, 18 launcher tests and 2,945 native checks (83 new; all 2,862 previous retained). Guest accessory/starter demand reaching the host and live electrical acceptance remain open. [Validation and limits](BUILDING.md#guest-battery-external-writes-protocol-176-unreleased).
- **Host electrical RPM inputs (local/unreleased, v175)** All three native Electrics RPM reads use the current guest driver's accepted RPM, retaining the native running threshold, charging/drain calculations and host charge/wear writers. Host updates bind before the first report; missing/stale/mismatched input uses native zero RPM, and scoped reads restore on nesting/errors. State 60 stays 25 bytes. Validation passes 3,125 Net tests, 18 launcher tests and 2,862 native checks (111 new; every previous 2,751 retained), including native battery Charge/ChargeMax and alternator Wear writes. Electrical load/starter draw, full physical/thermal handoff and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-electrical-rpm-inputs-protocol-175-unreleased).
- **Guest electrical temperature inputs (local/unreleased, v174)** Main and interior-light battery cold penalties and guest charging temperature limits now use host engine heat. The three scoped readers validate/recover separately from fuel/oil and cabin groups; state 197 stays 19 bytes. Validation passes 3,101 Net tests, 18 launcher tests and 2,751 native checks (89 new; every previous 2,662 retained). Protocol 175 adds host charging/drain from guest-driver RPM; remaining thermal consumers and live electrical/engine acceptance remain open. [Validation and limits](BUILDING.md#guest-electrical-temperature-protocol-174-unreleased).
- **Guest cabin/heater temperature inputs (local/unreleased, v173)** Host engine heat supplies the native cabin limit; host coolant supplies the native heater calculation, including while a guest drives. State 197 remains 19 bytes. Validation passes 3,074 Net tests, 18 launcher tests and 2,662 native checks (66 new; all 2,596 previous retained). Existing climate reporting is unchanged; full frost/cabin authority, other thermal consumers and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-cabin-and-heater-temperature-protocol-173-unreleased).
- **Guest engine-temperature inputs (local/unreleased, v172)** State 197 appends native engine degrees (19 bytes). Six guest fuel/mixture/oil/pressure reads consume host temperature with native arithmetic and scoped restoration. Validation passes 3,049 Net tests and 2,596 native checks (55 new; every prior 2,541 retained). Remaining thermal consumers/writers and live two-player acceptance stay open. [Validation and limits](BUILDING.md#guest-engine-temperature-inputs-protocol-172-unreleased).
- **Host coolant dashboard (local/unreleased, v171)** State 197 publishes the host’s native Corris coolant degrees independently of the driver, including late joins, source unavailability and guest-owned dashboards. Validation passes 3,014 Net tests, 18 launcher tests and 2,541 native checks (23 new; all 2,518 previous retained). Physical thermal consumers and engine-temperature handoff remain open. [Validation and limits](BUILDING.md#host-coolant-dashboard-protocol-171-unreleased).
- **Host cooling RPM inputs (local/unreleased, v170)** Accepted guest RPM supplies native pump circulation, fan speed and the running leak check. They join movement in one five-reader cooling group (`vehicleCooling`), retaining host part/wear/efficiency gates and native thresholds. VehicleState remains 25 bytes including ID. Validation passes 2,984 Net tests, 18 launcher tests and 2,518 native checks (77 new; all 2,441 previous labels/counts retained), with clean builds. Shared thermal state, complete handoff and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-cooling-rpm-inputs-protocol-170-unreleased).
- **Host cooling movement speed (local/unreleased, v169)** VehicleState 60 appends movement availability/value (25 bytes including ID), preserving separate dashboard wheel speed. Validated native Corris Measurements captures actual movement; the host scopes it to Cooling airflow and the stationary hot-engine check. Native rates/temperature writers remain authoritative, with stationary fallback and ownership/freshness gates. Validation passes 2,968 Net tests, 18 launcher tests and 2,441 native checks (34 new; all 2,407 previous labels/counts retained), with clean builds. Thermal replication, complete handoff and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-cooling-movement-speed-protocol-169-unreleased).
- **Host engine heating under delegated RPM/torque (local/unreleased, v168)** VehicleState 60 appends finite native torque/availability and becomes 22 bytes including ID. Scoped native heating operands and start/stop comparisons use one accepted sample while the host retains friction, rate limits and its per-second temperature writer. Heat/wear Harmony state is separated after a native crash exposed their shared declaring-type key. Validation passes 2,944 Net tests, 18 launcher tests and 2,407 native checks (34 new; every previous 2,373 label/count retained). Cooling/speed authority, guest temperature replication, complete thermal handoff and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-engine-heating-inputs-protocol-168-unreleased).
- **Host oil contamination under delegated RPM (local/unreleased, v167)** The seventh scoped reader supplies guest RPM to native Oil contamination while preserving host filter condition, minimum rate, three persistent writes and the native wait. All seven readers validate together; protected guest writers remain blocked through disconnect. Validation passes 2,918 Net tests, 18 launcher tests and 2,373 native checks (21 new; all previous 2,352 labels/counts retained), with clean builds. The native HeatGeneration audit identifies torque/friction and start/stop dependencies; full heat, thermal handoff, remaining wear/damage and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-oil-contamination-input-protocol-167-unreleased).
- **Host oil-pressure/mechanical wear under delegated RPM (local/unreleased, v166)** Six scoped native Pressure/Wearing operands use accepted guest RPM on the host, preserving its heat, oil and fourteen native part writers. Missing/stale/wrong-owner telemetry yields zero RPM while delegated; local takeover/disconnect restore native reads. Any changed binding disables the complete group before calculation. VehicleState layout stays 17 bytes including ID, with a semantic protocol bump. Validation passes 2,916 Net tests, 18 launcher tests and 2,352 native checks (21 new; all 2,331 previous labels/counts retained), with clean builds. Full heat progression/thermal handoff, other wear/damage paths and live two-player acceptance remain open. [Validation and limits](BUILDING.md#host-oil-pressure-and-wear-inputs-protocol-166-unreleased).
- **Vehicle temperature sources and gauges (local/unreleased, still v165)** VehicleState 60 reads native coolant degrees before dashboard clamps for Corris/Sorbet/Machtwagen and uses their native per-car division/clamp mappings. Scoped GetFsmFloat projection retains observer temperature between packets, yielding to local seating/ownership, handoff, expiry and disconnect without changing cooling state. Validation passes 2,883 Net tests, 18 launcher tests and 2,331 native checks (57 new, every previous 2,274 label/count retained), with clean builds. Existing 0–120 °C wire units remain unchanged. Full thermal handoff and guest-driven host wear remain open. [Validation and limits](BUILDING.md#vehicle-temperature-sources-and-gauges-still-protocol-165-unreleased).
- **Cooling ambient inputs (local/unreleased, v165)** EngineBlockState 195 supplies host RoofCheck temperature to Cooling/Reset while preserving native air-cooling math and guest shelter behavior. Missing data pauses Cooling; safe pending input permits admission without admitting malformed bindings, and proxies remain stable while waiting. Validation passes 2,855 Net tests, 18 launcher tests and 2,274 native checks (49 new; all 2,225 previous labels/counts retained), with clean builds. All Cooling GetFsm reads are now projected; dynamic temperatures, physical reconstruction, guest-driven host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-cooling-ambient-inputs-protocol-165-unreleased).
- **Cooling airflow inputs (local/unreleased, v164)** EngineBlockState 195 supplies grille, winter cover, stock bonnet and fiberglass bonnet installation and mounted modifiers to seven native Cooling reads. Native cover conditionality and stock-bonnet priority remain intact; four saved mount Data graphs stay paused through disconnect. Validation passes 2,813 Net tests, 18 launcher tests and 2,225 native checks (125 new; all 2,100 earlier checks retained), with clean builds. Physical body assembly, RoofCheck temperature/dynamic thermal state, guest-driven host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-cooling-airflow-inputs-protocol-164-unreleased).
- **Coolant hose leak inputs (local/unreleased, v163)** EngineBlockState 195 supplies all four hose installation bits/clamp totals and carburettor tightness to six native Cooling reads. The native missing-bottom-hose and combined leak decisions use host values while preserving scratch and saved guest Data. Four saved hose mounts stay paused through disconnect, preventing wear copying, clamp resets and detachment. Validation passes 2,746 Net tests, 18 launcher tests and 2,100 native checks (121 new; all 1,979 earlier checks retained), with clean builds. Physical hose reconstruction, clamp/leak presentation, grille/hood airflow, remaining thermal inputs, guest-driven host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-coolant-hose-leak-inputs-protocol-163-unreleased).
- **Radiator cooling inputs (local/unreleased, v162)** EngineBlockState 195 supplies radiator installation, mounted wear/coolant, cap pressure and electric-fan efficiency to five native Cooling reads. All three radiator variants are supported independently of block/head state. Native coolant clamps, pressure thresholds and fan hysteresis retain host inputs; saved radiator Data protection blocks active coolant/wear copies and removal through disconnect. Validation passes 2,672 Net tests, 18 launcher tests and 1,979 native checks (74 new; all 1,905 earlier checks retained), with clean builds. Physical radiator reconstruction, filling/cap controls, hose and airflow inputs, remaining thermal dependencies, guest-driven host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-radiator-cooling-inputs-protocol-162-unreleased).
- **Rocker-cover leak input (local/unreleased, v161)** EngineBlockState 195 supplies host cover installation and mounted bolt tightness to Oil/Valve Cover. Native leak arithmetic and shared scratch remain intact; saved cover Data protection follows the head and blocks native wear copying, cap hiding and detachment through disconnect. Validation passes 2,618 Net tests, 18 launcher tests and 1,905 native checks (47 new; all 1,858 earlier checks retained), with clean builds. Physical cover reconstruction, oil-cap/filling controls, remaining engine dependencies and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-rocker-cover-leak-input-protocol-161-unreleased).
- **Oilpan engine inputs (local/unreleased, v160)** EngineBlockState 195 supplies condition, tightness, oil quantity, contamination and viscosity from the host's settled oilpan independently of the cylinder head. Seven native reads in Oil/Wearing/Cylinders preserve scratch and saved guest Data; mount protection follows the block and survives disconnect. Validation passes 2,570 Net tests, 18 launcher tests and 1,858 native checks (67 new; all 1,791 earlier checks retained), with clean builds. Physical oilpan reconstruction, filling/draining controls, remaining engine dependencies, delegated-driving host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-oilpan-inputs-protocol-160-unreleased).
- **Valve adjustment inputs (local/unreleased, v159)** EngineBlockState 195 supplies all eight settings from the host head to native Valves ArrayListGet readers. Native arithmetic and tolerance decisions use host tuning while saved guest settings and scratch remain untouched by arrival. Missing/malformed arrays clear atomically; repair, disconnect and destroyed consumers restore owners safely. Validation passes 2,511 Net tests, 18 launcher tests and 1,791 native checks (55 new; all 1,736 earlier checks retained), with clean builds. Physical adjustment controls, remaining engine dependencies, delegated-driving host wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-valve-adjustment-inputs-protocol-159-unreleased).
- **Exhaust performance inputs (local/unreleased, v158)** EngineBlockState 195 supplies independent headers/front/rear/muffler DataPower, DataTorque and DataPowerAdd. Twelve native Valves reads preserve the normal additions; header availability follows the installed head while fixed pipes remain independent of the engine assembly. All four saved mounts stay protected after movement/disconnect. The profile has 87 sources/155 reads and 11 paused graphs. Validation passes 2,456 Net tests, 18 launcher tests and 1,736 native checks (98 new; all 1,638 earlier checks retained), with clean builds. Physical exhaust reconstruction/sound, separate racing-front/sidepipe/tip routing, remaining engine dependencies, delegated-driving host fuel/wear and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guest-exhaust-inputs-protocol-158-unreleased).
- **Intake filtration and performance (local/unreleased, v157)** Existing EngineBlockState 195 carries the host's air-cleaner installation and the independent carburettor/filter DataPower, DataTorque and DataPowerAdd contributions. Seven native reads in FuelLine/Valves preserve the filtration decision and normal power/torque additions. The saved filter mount follows head movement and stays protected through disconnect. The profile has 83 sources/143 reads and seven paused graphs. Validation passes 2,337 Net tests, 18 launcher tests and 1,638 native checks (48 new; all 1,590 earlier checks retained), with clean builds. Physical intake reconstruction, exhaust/other engine inputs, host fuel/wear while a guest drives and live two-player starting remain open. [Validation and limits](BUILDING.md#guest-intake-inputs-protocol-157-unreleased).
- **Carburettor fuel and mixture inputs (local/unreleased, v156)** Existing EngineBlockState 195 carries host carburettor installation, FuelChamber, CarbReserve and SettingMixture from the settled mount beneath its installed head. Stock, two-barrel and four-barrel native identities are accepted; invalid or detached assemblies clear dependent fields. Four reads in FuelLine/Mixture retain native clamps and arithmetic. Guest mount protection follows the saved head and drains already-active work through disconnect. The profile has 80 sources/136 reads and six paused graphs. Validation passes 2,252 Net tests, 18 launcher tests and 1,590 native checks (54 new; all 1,536 earlier checks preserved), with clean builds. Physical carburettor reconstruction, air-filter/other engine inputs, host fuel/wear while a guest drives and live two-player starting remain open. [Validation and limits](BUILDING.md#guest-carburettor-inputs-protocol-156-unreleased).
- **Cylinder-head combustion input (local/unreleased, v155)** Existing EngineBlockState 195 carries HeadInstalled=8 from the settled head mounted on the host's installed block. The native Cylinders Powertrain gate uses this flag; missing/invalid heads remain absent without discarding valid block state. Guest head Data protection follows the native block after movement/renaming, preventing removal wear copies and detachment through disconnect. The profile has 78 sources/132 reads, nine consumers and five paused graphs. Validation passes 2,194 Net tests, 18 launcher tests and 1,536 native checks (39 new; all previous 1,497 checks retained), with clean builds. Physical head/block reconstruction, valve arrays, thermal/oil dependencies, remaining engine inputs and live two-player starting remain open. [Validation and limits](BUILDING.md#guest-cylinder-head-input-protocol-155-unreleased).
- **Gearbox starter input (local/unreleased, v154)** Host-only GearboxState 196 supplies the native gearbox Type to Starter Check automatic, preserving the driver's local selector and actual P/N decisions. Missing or transitional host state pauses Starter; protected deferred-entry recovery resumes a blocked attempt after validation. Guest gearbox Data stays paused through disconnect, preventing native wear/oil/integer-damage copies and detachment. The profile has 77 sources/131 reads, nine consumers and four paused graphs. Validation passes 2,154 Net tests, 18 launcher tests and 1,497 native checks (48 new; all 1,449 prior checks retained). Physical transmission reconstruction, ratios and other gearbox inputs, head/other engine dependencies, guest-driving host wear and live two-player starting remain open. [Validation and limits](BUILDING.md#guest-gearbox-starter-input-protocol-154-unreleased).
- **Engine block inputs (local/unreleased, v153)** Host-only EngineBlockState 195 supplies installation to both native Starter checks, condition to Oil and the native damage flag to Cooling. Direct Starter object targets retain their native cadence, including the running check every frame. Guest block Data stays paused through disconnect, protecting removal wear writes and detachment; its disabled continuous wear action stays disabled. The profile has 76 sources/130 reads across nine consumers and three paused graphs. Validation passes 2,117 Net tests, 18 launcher tests and 1,449 native checks (53 new; all 1,396 earlier checks retained). Physical block reconstruction, gearbox/head inputs, guest-driving host wear and live two-player operation remain open. [Validation and limits](BUILDING.md#guest-engine-block-inputs-protocol-153-unreleased).
- **Battery engine inputs (local/unreleased, v152)** Host-only BatteryState 194 supplies installed/charge to three native Electrics reads. Host capture waits for stable mount/part attachment; revisions, keepalives and join/vehicle resync preserve updates. Guest battery Data is paused and ten Starter/Electrics battery writes are guarded, preserving saved charge and degradation fields. The profile now has 73 sources/126 reads across nine consumers, with 75 protected scalar writes and two paused graphs. Validation passes 2,076 Net tests, 18 launcher tests and 1,396 native checks (39 new; all 1,357 earlier checks retained). Physical battery changes, guest-driver draw reaching the host, other electrical writers, remaining engine dependencies and two-player acceptance stay open. [Validation and limits](BUILDING.md#guest-battery-engine-inputs-protocol-152-unreleased).
- **Impact** HIGH. Guest damage can operate on retained local saved parts rather than the host replacement, and native event replay can reroll secondary failures.
- **Game truth** `CORRIS/Simulation/Systems/PartBreakages::Damages` resolves mount Data/ActivePart references. PISTON failure includes random OILPAN/BLOCK consequences; copying wear or replaying the event on guests is unsafe for isolated originals.
- **Implemented (v124)** VehicleDamage 64 is host-authoritative regardless of driver. Guests accept passive damage state for checksums; their native Damages FSM is paused until restart, including after disconnect, with no PartBreakages replay or mount Wear writes. Forced discovery precedes saved-part isolation. Host rejects guest reports. VehicleCondition and other owner streams retain their existing authority.
- **Engine handoff prerequisite (v126, local/unreleased)** VehicleState 60 belongs to the current physics owner, with host fallback only when unowned. Parked ignition keeps its simulator streaming while leaving the seat available; a fresh nearby seated driver can take over. Reliable final engine state precedes the final pose, and the host rejects stale reports before relay. Per-sender history survives ownership changes; snapshots use fresh accepted driver state and preserve current drivers/pose ownership. At v126, 1,377 protocol/catalog/policy tests, 18 launcher tests and 253 isolated game checks pass, including 33 vehicle-state checks. Both Net targets, Core (Debug/Release) and the probe build without warnings/errors. [Native results](../build/vehicle-state-smoke/result.json) record zero failures and Wine exit 0. Actual two-player handoff, full engine operation and native wear remain unverified.
- **Readiness and simulation boundary (v126)** Native RPM wins over stale dashboard RPM; cold snapshots bind gear first, and late Power bindings retry ON/OFF without another packet. Remote timeout cannot affect a newly seated local player, and ownership expiry cannot cause the host to republish remote ACC. The [corrected native engine audit](../build/vehicle-rpm-smoke/native-audit.json) identifies Starter's enabled global RPM outputs; copying dashboard values cannot safely drive engine wear or combustion. Native producer ownership and safe part references need a separate implementation.
- **Native reachability audit (build 23268598)** FuelLine's `Fuel line` state has no incoming or global transition in the extracted graph, and `db_FuelLine` has a null default. The live fuel loop goes directly from Fuel tank to Fuel Pump. Treat this read as an unconnected native placeholder until a runtime binding/entry is demonstrated; it does not currently justify a new shared input. [Audited graph](../build/rocker-cover-input-smoke/fuel-line-reachability-audit.json).
- **Remaining** Verify host wear while a guest drives, full guest engine operation and separate guest physical failure presentation. The distributor, starter, water/oil pumps, stock/racing fuel pump, all five camshafts and all eight rocker slots have bounded runtime input projections through v133; v134/v135 add stock/upgraded alternator mechanical and electrical inputs; v136 adds six primary fan-belt installation reads, and v137 adds timing-belt combustion installation/wear; v138 adds crankshaft, crank pulley and auxiliary shaft/sprocket inputs; v139 adds head gasket, thermostat, housing and oil-filter inputs; v140 adds wear inputs for all five main-bearing slots; v141 adds piston installation/wear for all four slots in Cylinders and Mixture; v142 adds radiator-fan installation in Valves and Cooling. Other native engine readers still require projection. Saved mounts retain guest ActivePart/Installed/scalars. v148/v151 cover all four flywheel/flexplate families in combustion and starting; v150 supplies engine wiring; v152 supplies battery installed/charge inputs with saved-battery protection. v153 supplies host block installation/condition/damage inputs to Starter, Oil and Cooling. v154 supplies host gearbox Type to the native starter interlock while preserving the local selector. v155 supplies cylinder-head installation to the native combustion gate. v156 supplies stock/two-barrel/four-barrel carburettor installation, fuel reserve/chamber and mixture settings to FuelLine/Mixture. v157 supplies air-cleaner filtration and both intake performance contributions to FuelLine/Valves. v158 supplies four independent native exhaust performance groups to Valves with saved-mount protection. v159 supplies the eight native head valve settings to Valves. v160 supplies oilpan condition/tightness and oil quantity/contamination/viscosity to Oil, Wearing and Cylinders. v161 supplies rocker-cover tightness to the native oil-leak calculation. v162 supplies radiator installation, mounted coolant/wear, cap pressure and electric-fan efficiency to Cooling. v163 supplies all four coolant hose clamps/installation and the carburettor clamp to the native leak calculation. v164 supplies grille/cover/stock/fiberglass bonnet installation and mounted airflow modifiers to Cooling. v165 supplies RoofCheck::Raycast.TempCar at Reset #1 with safe pending admission; all Cooling GetFsm reads now have projections. Physical block/head/transmission reconstruction, valve-adjustment controls, remaining thermal/fluid inputs, oil filling/draining controls, ratios and other gearbox inputs, physical battery changes, guest-driven electrical load and other engine dependencies still need implementation. Test different guest saves, repair, handoff, late join/resync and saved-original preservation; see BUILDING.md.
- **Local write protection (unreleased, still v126)** `guestEngineProtection` selects 65 persistent scalar actions across 11 native FSMs and the distributor mesh write, preserving calculations and delegated fuel/electrical state. PartFallings is paused because it mutates saved bolt arrays and can BREAKOFF parts. Admission immediately guards active writers after setting the save latch; failure retains protection. Guards persist after disconnect, and incomplete bindings defer isolation/ignition wake. Repaired bindings complete only the blocked entry, without replaying stale exits/events; inactive graphs wait for activation. Malformed metadata leaves unrelated catalog rules available. All 1,428 protocol/catalog/policy tests, 18 launcher tests and 276 isolated game checks pass, including 51 new catalog cases and 23 new native checks. Both Net targets, Core (Debug/Release) and the probe build without warnings/errors; the native run has zero failures and Wine exit 0. Actual two-player behavior, full guest engine operation and host wear progression remain unverified. [Native writer audit](../build/engine-write-audit/audit-summary.json).
- **Touch** `VehicleWorldSync.Damage.cs`, `.Engine.cs`, vehicle snapshot/checksum paths, session damage dispatch and `VehicleDamagePolicy`. Protocol semantics changed in v124 without new fields or IDs.
- **Protection validation evidence** [Final native results and tested hashes](../build/guest-engine-smoke/result.json), including immediate admission guards and inactive blocked-entry recovery without stale exit/event replay.
- **Corris RPM source (local/unreleased, still v126)** `vehicleEngineRpm` validates eight enabled Starter outputs against the registered vehicle's native CarDrivetrain and global RPM, including StarterSpeed cranking. Missing/changed references retry; foreign components and local shadows are rejected. Inactive, disabled or unstarted sources read zero. Dashboard alias writes stay blocked after binding failure, and active ACC retains ownership during discovery. This fixes readiness and RPM publication without simulation writes or guest mount projection. All 1,459 protocol/catalog/policy tests, 18 launcher tests and 301 isolated game checks pass (31 new catalog cases and 25 new RPM checks). Both Net targets, Core (Debug/Release) and the probe build without warnings/errors; the native run has zero failures and Wine exit 0. [Final native results and tested hashes](../build/vehicle-rpm-smoke/result.json) record the controlled run. Full guest operation, host wear progression and two-player acceptance remain open.

- **Distributor engine inputs (local/unreleased, v127)** Four Cylinders reads for VIN131 Installed/Wear/Tightness/SparkAngle use an owned inert Data proxy. Only an accepted, fully applied, uniquely attached host replica supplies an installed input, with latest bolt receipt ordering. Missing/pending/loose/retired/conflicting inputs stay absent. Action-local targets preserve shared native references, saved Data and scratch calculations. Exact signature guards share scoped pause/recovery; whole-object rebuild handles native target caches. Preparation precedes admission resume, isolation and ignition wake. Native running checkpoints and normal ignition restart govern when new input takes effect. Full engine behavior remains PARTIAL.

- **v127 validation** All 1,505 Net tests, 18 launcher tests and 334 isolated native checks pass (46 new catalog cases and 33 native input checks; all 301 earlier native checks preserved). Net, Core Debug/Release, the probe and Launcher Debug build cleanly. [Native results and tested hashes](../build/guest-engine-input-smoke/result.json) distinguish the tested Debug payload from the separately built Release assembly. No release/deployment; two-player acceptance remains open.

- **Starter engine inputs (local/unreleased, v128)** VIN130 appends actual host Durability to state 185. Three native Starter readers use a separate proxy for Installed/Wear/Durability after the same current applied identity and unique attachment gates as the distributor. Native wiring, the Wear > 25 decision and durability arithmetic remain active; saved-part writes stay suppressed. Removal, delayed materialization and disconnect remain absent. No synthetic ignition replay or scratch refresh. Battery/wiring/block/flywheel/gearbox readers and broader engine behavior remain open.
- **Water-pump engine inputs (local/unreleased, v129)** VIN126 adds actual host Durability/Efficiency to state 185. Seven native Oil/Cooling reads use independent proxies for the same accepted applied identity and unique attachment. Native seizure, wear arithmetic, circulation, belt/RPM gates and bolt-receipt leak math remain active; saved guest Data/write targets stay protected. Pending/removal/disconnect closes both; changed readers pause only their consumer. Other cooling/engine inputs and two-player acceptance remain open.
- **Stock/racing fuel-pump inputs (local/unreleased, v130)** VIN125/FUELPUMP0 append actual Durability/OutputRate to state 185. Four native reads across FuelLine/Wearing accept either factory at the same live mount, with independent stable proxies and unique current applied identity gates. Pending competing variants close input; removal cannot expose an unapplied replacement. Native installation, starvation/capacity and durability math remain active while saved Data/write targets stay protected. Other fuel/engine inputs, guest-driving host wear and two-player acceptance remain open.
- **Oil-pump inputs and shared consumers (local/unreleased, v131)** VIN132 appends actual host Durability to state 185. Three native Oil/Wearing reads share their graphs with existing water/fuel pump sources. Eight required source entries span six consumers; each retains its own proxy/identity/attachment gates, while native scratch is shared only through normal reads. All sources must validate before the graph resumes. Selective proxy recovery and partial cleanup preserve scoped protection. Native starvation/circulation and durability math remain active; broader engine work and two-player acceptance remain open.
- **v131 validation** All 1,563 Net tests, 18 launcher tests and 436 isolated native checks pass (12 new catalog cases and 27 oil-pump checks; all 409 previous checks preserved). Net, Core Debug/Release, the probe and Launcher Debug build cleanly. [Native results and tested hashes](../build/oilpump-engine-input-smoke/result.json) distinguish tested Debug and separately built Release. Two-player acceptance remains open; changes are local and unreleased.
- **v130 validation** All 1,551 Net tests, 18 launcher tests and 409 isolated native checks pass (23 new catalog cases and 31 fuel-pump checks; all 378 previous checks preserved). Net, Core Debug/Release, the probe and Launcher Debug build cleanly. [Native results and tested hashes](../build/fuelpump-engine-input-smoke/result.json) distinguish tested Debug and separately built Release. Two-player acceptance remains open; changes are local and unreleased.
- **v129 validation** All 1,528 Net tests, 18 launcher tests and 378 isolated native checks pass (14 new catalog cases and 27 water-pump checks; all 351 previous checks preserved). Net, Core Debug/Release, the probe and Launcher Debug build cleanly. [Native results and tested hashes](../build/waterpump-engine-input-smoke/result.json) distinguish tested Debug and separately built Release. Two-player acceptance remains open; changes are local and unreleased.
- **v128 validation** All 1,514 Net tests, 18 launcher tests and 351 isolated native checks pass (nine new catalog cases and 17 starter checks; all 334 previous native checks preserved). Net, Core Debug/Release, the probe and Launcher Debug build cleanly. [Native results and tested hashes](../build/starter-engine-input-smoke/result.json) distinguish the tested Debug payload from the separately built Release assembly. Two-player acceptance remains open; changes are local and unreleased.

- **Camshaft inputs (local/unreleased, v132)** All five stock/tuned cams publish actual Durability/ValveTolerance plus a trailing CamProfile string in state 185. Six native reads across Cylinders/Wearing/Valves use complete variant coverage and the real nested cylinder-head attachment. Profile parsing, wear/broken-belt thresholds and valve tolerance run natively, with saved Data protected. Eleven sources span seven consumers; missing/pending/conflicting parts remain absent. All 1,597 Net tests, 18 launcher tests and 477 native checks pass (34 new Net cases, 41 new native checks, all 436 previous checks preserved). [Results and tested hashes](../build/camshaft-engine-input-smoke/result.json) record the isolated Debug run. Other engine inputs, host wear progression and two-player acceptance remain open.

- **Rocker inputs (local/unreleased, v133)** Eight VIN117 sources provide native cylinder Bolted checks through the real Rockers slot array. Every slot requires matching accepted attachment, host/applied assembly index, replica identity and array family. Latest bolt receipts supply Tightness; validated native producer rules derive Bolted at >=1. Nineteen sources span seven consumers. Missing/pending/conflicting assignments stay absent; malformed tables or changed signatures pause only the affected graph. All 1,630 Net tests, 18 launcher tests and 509 native checks pass (33 new Net cases, 32 new rocker checks, all 477 earlier native checks preserved). [Results and tested hashes](../build/rocker-engine-input-smoke/result.json) record the controlled Debug run and matching launcher payload. Net, Core Debug/Release, probe and launcher builds have no warnings or errors. Other engine inputs, host wear progression and two-player acceptance remain open.

- **Alternator mechanical inputs (local/unreleased, v134)** Stock VIN133 and upgraded ALTERNATOR0 append actual Friction after Wear/Tightness/SettingRotation. Three Oil reads use the uniquely attached, applied host variant; twenty sources span seven consumers. Native absent/seized/running decisions, current-based resistance, saved-part protection and independent scratch remain intact. All 1,647 Net tests, 18 launcher tests and 530 native checks pass (17 new catalog cases, 21 new alternator checks; all 509 previous native checks preserved). [Results and tested hashes](../build/alternator-mechanical-input-smoke/result.json) record the controlled Debug run and matching launcher payload. Net, Core Debug/Release, probe and launcher builds have no warnings or errors. Electrical Efficiency/Durability and the mount-owned Damaged flag remain unprojected; belt/wiring dependencies, full engine integration, host wear while guests drive and two-player acceptance remain open.

- **Alternator electrical inputs (local/unreleased, v135)** Stock/upgraded alternators append Durability/Efficiency and actual fitted-mount Damaged to state 185. Damage/repair advances gameplay revision; unsupported, loose and unresolved parts cannot carry a stale mount flag. Seven native Electrics readers project current applied host inputs, including repeated Installed/Damaged checks, through one typed value per field. Twenty-one sources span eight consumers. All 1,676 Net tests, 18 launcher tests and 556 native checks pass (29 new Net cases, 26 electrical checks; all 530 earlier native checks preserved). [Results and tested hashes](../build/alternator-electrical-input-smoke/result.json) record the controlled Debug run and matching launcher payload. Net, Core Debug/Release, probe and launcher builds have no warnings or errors. Battery/belt/wiring integration, other engine inputs, host wear while guests drive and two-player acceptance remain open.

- **Fan-belt inputs (local/unreleased, v136)** Six native bool reads across Oil, Valves, Cooling and Electrics follow the applied host FANBELT0. Four independent sources bring the profile to twenty-five across eight consumers. Native load/power, circulation, fan cooling and charging gates retain their original behavior; appearance receipts do not interrupt a ready belt. Pending gameplay/repair, missing bodies, changed identities and attachments still block readiness. All 1,693 Net tests, 18 launcher tests and 612 native checks pass (17 catalog cases, 56 belt checks; all 556 prior native checks preserved). [Results and tested hashes](../build/fanbelt-engine-input-smoke/result.json) record the tested Debug payload and clean builds. TimingData/RotateEngine belt readers, the separate timing belt, radiator-fan installation, battery/wiring, remaining engine inputs, host wear while guests drive and two-player acceptance remain open.

- **Timing-belt combustion inputs (local/unreleased, v137)** Native Cylinders installation and wear reads consume the unique applied host VIN107 through a bool/float proxy. Existing Wear/Tightness state order remains unchanged. Native Powertrain, strict Wear <1 failure and cam ValveTolerance <=1 decisions are retained; saved belt/distributor wear writers stay protected. Twenty-six sources span eight consumers. All 1,706 Net tests, 18 launcher tests and 637 native checks pass (13 new catalog cases, 25 timing-belt checks; all 612 prior native checks preserved). [Results and tested hashes](../build/timingbelt-engine-input-smoke/result.json) record the tested Debug payload and clean builds. TimingData/RotateEngine, physical failure presentation, radiator-fan inputs, battery/wiring, remaining engine inputs, host wear while guests drive and two-player acceptance remain open.

- **Radiator-fan power and cooling inputs (local/unreleased, v142)** Two VIN137 sources (corrected from VIN127 in v210) supply installation to Valves/Radiator fan and Cooling/Fan through independent bool proxies. The native factory reference, nested mount, unique accepted/applied attachment and actual replica parent must agree. Native fan/belt load and cooling decisions use host parts while guest saved Data remains intact. Fifty-three sources contain eighty-eight reads across nine consumers. All 1,824 Net tests, 18 launcher tests and 958 native checks pass (16 catalog cases and 38 fan checks added; all 920 prior checks preserved). [Results and tested hashes](../build/radiator-fan-engine-input-smoke/result.json) record the isolated Debug payload. Complete cooling/overheating, fan presentation, remaining engine inputs and two-player acceptance remain open.

- **Spark-plug boxes and individual outputs (local/unreleased, v143)** sparkplugbox0 supplies host-owned fixed quantity (four) through PackageState and acknowledged openings. Its local Sparkplug.SpawnPoint assignment and literal contents factory reference are validated. Individual SPRKPLUG0 outputs retain native IDs and Wear/Tightness/Durability in replacement state; duplicate requests create no extra plug, and guest replicas preserve local box saves. There are 31 package and 33 replacement families. Guarded synchronous box startup also fixes a newer-quantity update being cleared by delayed Empty initialization. All 1,834 Net tests, 18 launcher tests and 976 native checks pass (10 new Net cases, 18 native checks; all 958 previous checks preserved). [Results and tested hashes](../build/sparkplug-opening-smoke/result.json) record the isolated Debug run. Native Screw/tool controls, complete slotted fitting/removal, cylinder firing/misfires and two-player acceptance remain open.

- **Flywheel/flexplate inputs (local/unreleased, v147)** Four direct native factories (VIN120, FLYWHEELa0, FLYWHEELb0, VIN138) publish Wear/Tightness/InertiaFactor. Native Installed/inertia reads at the shared mount use unique applied host state; drivetrain inertia and shake follow host values. Invalid inertia cannot enter the engine, absent parts stop starting, and saved Data remains untouched. There are 58 sources/106 reads across nine consumers. All 1,935 Net tests, 18 launcher tests and 1,200 native checks pass (40 new Net and 53 new native cases; all 1,147 prior checks retained). Core Debug/Release, both Net targets, probe and Launcher build cleanly. Native factory, actual guest materialization, fit/loose presentation, variant, cache and recovery checks are included in [the local validation record](../build/flywheel-engine-input-smoke/result.json). Full host fitting/removal physics, player controls, full engine operation and two-player acceptance remain open.
- **Rev-limiter engine inputs (local/unreleased, v148)** Native Cylinders Installed/SettingRPM reads now follow the unique applied REVLIMITER0 on its registered vehicle body. Existing state 185 Tightness/SettingRPM layout and all factories stay unchanged. Native absence disables the limiter without overwriting maxRPM; host knob values reach the next native read without changing saved guest mount Data. The profile has 59 sources/108 reads across nine consumers. Native knob calculation/capture, real guest materialization, vehicle fit/removal, attachment conflicts, cache recovery and disconnect checks are included. All 1,961 Net tests, 18 launcher tests and 1,237 native checks pass (26 new Net and 37 new native cases; all 1,200 previous checks retained). Core Debug/Release, both Net targets, probe and Launcher build cleanly; [the local validation record](../build/revlimiter-engine-input-smoke/result.json) identifies the tested Debug payload and matching launcher files. Guest knob requests/presentation, full host fit physics, road-driving behavior and live two-player acceptance remain open.
- **Starter flywheel prerequisite (local/unreleased, v151)** Starter Check Flywheel #0 now projects host installation for VIN120, FLYWHEELa0, FLYWHEELb0 and VIN138 through the same live block-relative mount and applied-state checks as Cylinders. Cold, pending, removed, conflicting or mismatched copies cannot borrow saved guest installation. Native Prepare starting/No Flywheel decisions, read timing and saved Data are preserved. The profile has 72 sources/123 reads across nine consumers; factories remain 38/31. [Validation and limits](BUILDING.md#guest-starter-flywheel-input-protocol-151-unreleased) cover the native decision, all variants and agreement between consumers. Battery charge/draw, block/gearbox prerequisites, cylinder-head placement and live two-player starting remain open. All 2,040 Net tests, 18 launcher tests and 1,357 native checks pass (19 new Net and 32 new native cases; all 1,325 previous native checks preserved). Builds are clean and launcher files match the tested Debug payload. [Result record](../build/starter-flywheel-engine-input-smoke/result.json).
- **Engine wiring inputs (local/unreleased, v150)** Host-only state 193 publishes eight native wiring sources to thirteen protected engine reads, with exact identities, independent Installed/Bolted flags, native load readiness, per-source revisions, ordered keepalive, join/vehicle resync and saved-wire-safe proxies. The profile has 71 sources/122 reads across nine consumers; replacement/package factories remain 38/31. Native ignition, starter and charging decisions use host records. [Validation and limits](BUILDING.md#guest-engine-wiring-inputs-protocol-150-unreleased) distinguish these inputs from the 35-connection wiring system: guest tools, cable/bolt presentation, battery state, shock/fire, cylinder-head placement and two-player acceptance remain open. All 2,021 Net tests, 18 launcher tests and 1,325 native checks pass (41 new Net, 47 new native; all 1,278 earlier native checks preserved). Builds are clean, and the launcher payload matches the tested Debug files. [Result record](../build/wiring-engine-input-smoke/result.json).
- **Ignition-coil replicas/input (local/unreleased, v149)** VIN212 now has a native factory profile with Wear/Tightness in state 185 and a vehicle-relative installation input for Cylinders Ignition #0. Only the unique applied host copy supplies installation; native coil/distributor/wiring decisions remain active without saved guest mount writes. Native fresh creation and identity, host wear, real guest materialization, fitting/removal presentation, all eight prerequisites, conflicts, vehicle addressing, cache repair and disconnect restoration are covered. There are 60 sources/109 reads across nine consumers, 38 replacement factories and 31 unchanged package factories. All 1,980 Net tests, 18 launcher tests and 1,278 native checks pass (19 new Net and 41 new native cases; all 1,237 previous checks retained). Core Debug/Release, both Net targets, probe and Launcher build cleanly. [The local validation record](../build/ignition-coil-engine-input-smoke/result.json) identifies the tested Debug payload and matching launcher files. Wiring authority, full native host fitting physics, physical controls, cylinder-head placement and two-player acceptance remain open.
- **Cylinder-head assembly dependency (audited, not implemented)** VIN1110 has no replacement factory and owns nested mounts and a saved Valves array. The leaf-only replacement isolation path must not clone or hide it as an ordinary replacement. Cylinders Powertrain #0 still reads Installed from VIN1010/VINP_Cylinderhead. Implement authoritative native assembly placement/restoration before projecting this input. [Native head/mount evidence](../build/cylinderhead-audit/scene.json) and [complete native/aftermarket factory audit](../build/flywheel-engine-input-smoke/factory-audit.json) preserve the prerequisite. v150 covers eight engine-wiring sources; v148 covers rev-limiter input and v149 covers ignition-coil replicas/input.
- **Spark-plug cylinder inputs (local/unreleased, v146)** Four native array slots now supply Installed/Wear/Tightness/Durability to Cylinders through independent proxies. Slots 1–4 correctly map to cylinders 4–1. Only unique accepted/applied host attachments supply condition; pending, removed, conflicting or mismatched replicas supply inert values. Native firing/efficiency, misfire eligibility and durability arithmetic follow host condition while saved wear writers remain disabled. There are 57 sources and 104 reads across nine consumers; message 185 keeps its layout and scalar order. All 1,895 protocol tests, 18 launcher tests and 1,147 native checks pass (29 new catalog/protocol cases, 71 native cases; all 1,076 prior native checks retained). Core Debug/Release, both Net targets, probe and Launcher build cleanly. [Results and tested hashes](../build/sparkplug-engine-input-smoke/result.json) record native tests and build validation. Random outcomes remain local native calculations; physical tool selection, complete engine operation, host wear during guest driving, cylinder-head/other inputs and live two-player acceptance remain open.

- **Spark-plug full physics lifecycle (local/unreleased, protocol 145 unchanged)** The isolated Unity probe now executes fitting, tightening, loosening and removal across real frames in all four sockets. It verifies deferred Rigidbody destruction/recreation, mass balance, wear, collider/layer/tag, world pose, car velocity and fresh loose ownership. Recorded host states run through actual guest materialization and updates; one replica survives all cycles and rejects delayed fitting states. All 1,076 native checks and 1,866 protocol tests pass; the 44 new checks preserve all 1,032 previous native cases. Core Debug/probe builds are clean, and runtime/catalog/wire behavior is unchanged. [Results and tested hashes](../build/sparkplug-lifecycle-smoke/result.json) record the isolated Debug run. Functions sound/UI, physical tool selection, cylinder effects and live two-player acceptance remain outside this fixture.

- **Spark-plug wrench controls (local/unreleased, v145)** Added host-validated tool operations 6/7 with revision, native socket, mount, proximity, tightness and cooldown checks. Native wrench/ratchet sends create guest intents; safe replica Screw graphs cannot write local tightness or BOLTING. Registered identity recognition fixes the old clone-name check while keeping native wrench-size gating. Saved plugs use their array slot, independent of stale installer scratch. All 1,866 Net tests, 18 launcher tests and 1,032 native checks pass (21 Net and 31 native checks added; all 1,001 previous native checks retained). [Results and tested hashes](../build/sparkplug-tool-smoke/result.json) record the isolated Debug checks. The later lifecycle probe above covers fitting/removal physics; physical tool selection, cylinder effects and live two-player acceptance remain open.

- **Spark-plug socket/removal readiness (local/unreleased, v144)** Fixed the shared removal validator rejecting fitted SPRKPLUG0 plugs because native Screw.Set and mouse picks use tool layer 12. Factory `removalLayer` defaults to 19 for earlier families; host availability and guest obstruction checks respect it. All four reversed native socket addresses and mount entry graphs are checked. Native tightening withdraws removal availability; loosening restores it, with collider/parent/occupant and native assembly prerequisites still required. The fixture verifies selection and controlled fitted readiness, not the complete install/remove physics lifecycle or guest wrench controls. All 1,845 Net tests, 18 launcher tests and 1,001 native checks pass (11 Net and 25 native checks added; all 976 earlier native checks preserved). [Results and tested hashes](../build/sparkplug-fitting-smoke/result.json) record the isolated Debug run. Guest wrench input, complete lifecycle, engine inputs and two-player acceptance remain open.

- **Spark-plug fitting and engine prerequisite audit (partially implemented)** The individual family is `SPRKPLUG0`, created by `Spawner/CreateItems::Sparkplug`; its box is `sparkplugbox0::Use`, produced by `CreateItems::Sparkplugs`. Opening decrements Quantity, assigns the individual factory's `SpawnPoint`, then sends its native event. Fresh individual creation uses IntAdd/CreateObject/SetFsmGameObject/SetVelocity/ConvertIntToString/BuildStringFast/SetName; saved creation uses CreateObject/SetFsmGameObject/SetName. v143 implements an explicit individual factory profile and authoritative fixed-capacity box opening; the unchanged standard and ShoppingBagSpawn-specific paths remain distinct. v145 adds guarded Screw/tool requests. The later protocol-145 lifecycle probe accepts native slotted fitting/removal physics and guest state replay in isolation. v146 binds and tests the cylinder inputs below. The later cylinder-head audit below identifies the nested assembly dependency; v147 covers flywheel/flexplate inputs. Remaining engine dependencies, physical tool selection, full engine operation and live two-player acceptance remain open. Native part Data has AssemblyID, Wear, Tightness and Durability (no Installed bool); its InstallPoint is assigned through the Sparkplugs array. Slots 1/2/3/4 map to VINP_Sparkplug4/3/2/1 under VIN1110. Each cylinder reads Installed at CylinderN #3, Wear at Reset #9–12, Tightness at Add to power N #2, and Durability at Plug data #7–10. Mount Install2 reads both Wear and Durability; Remove and Update2 write part Wear. Preserve guest writer protection and verify slot mapping before implementing firing/misfire behavior. [Scene](../build/sparkplug-audit/scene-audit.json), [box](../build/sparkplug-audit/prefab-audit.json), [part](../build/sparkplug-audit/part-audit.json) and [slot-array](../build/sparkplug-audit/slot-installer-audit.json) audits preserve the inspected native evidence; these are ignored local files, not shipped assets.

- **Piston combustion and smoke inputs (local/unreleased, v141)** All four VIN103 slots supply installation/wear to Cylinders and wear to Mixture through eight independent sources. Native slotted Data has no Installed bool; the input uses accepted/applied attachment and assembly identity. Wear/Tightness arrays are unchanged. Native cylinder firing, efficiency and blue-smoke decisions follow host wear, while guest oil/contamination writers remain blocked. Fifty-one sources span nine consumers. All 1,808 Net tests, 18 launcher tests and 920 native checks pass (31 catalog cases and 50 piston checks added; all 870 prior checks preserved). [Results and tested hashes](../build/piston-engine-input-smoke/result.json) record the isolated Debug payload. Cylinder-head/spark-plug and remaining inputs, smoke/failure presentation, full engine operation, host wear during guest driving and two-player acceptance remain open.
- **Main-bearing oil-pressure inputs (local/unreleased, v140)** Five VIN104 Wear sources use distinct MainBearings slots in Wearing. Exact native slot tables and matching accepted/applied assembly identities prevent missing, conflicting or mismatched parts from borrowing guest wear or another slot. Shared slot validation now supports wear-only bearings alongside derived rocker Bolted inputs. Native crank/bearing pressure arithmetic, RPM clamp and final PressureLeak output run with host state. Existing scalar order is unchanged; forty-three sources span eight consumers. All 1,777 Net tests, 18 launcher tests and 870 native checks pass (23 catalog cases and 44 bearing checks added; all 826 prior checks preserved). [Results and tested hashes](../build/bearing-engine-input-smoke/result.json) record the isolated Debug payload. Cylinder-head/other component inputs, RedLining/failure effects, downstream pressure/flow behavior, host wear during guest driving and two-player acceptance remain open.
- **Head gasket, thermostat and oil-filter inputs (local/unreleased, v139)** Five required sources add eight native reads for VIN134, VIN129, VIN128 and OILFILTR0. Native gasket failure, thermostat wear/temperature, housing leak and filter leak/contamination decisions use applied host parts. Existing Wear/Tightness and Dirt/Tightness arrays remain unchanged; saved Data and writers stay protected. Thirty-eight sources span eight consumers. All 1,754 Net tests, 18 launcher tests and 826 native checks pass (23 new catalog cases, 99 fluid-input checks; all 727 earlier native checks preserved). [Results and tested hashes](../build/fluid-engine-input-smoke/result.json) record the isolated Debug payload. Full downstream cooling/oil behavior, physical failure effects, remaining engine inputs, host wear during guest driving and two-player acceptance remain open.
- **Crankshaft and auxiliary-drive inputs (local/unreleased, v138)** Four existing families add eight native reads through seven independent sources: installation/crank wear in Cylinders, crank condition in Oil/Wearing, and auxiliary-shaft wear in FuelLine. Wear/Tightness scalar order remains unchanged. Native wear thresholds, pressure arithmetic and installation gates use applied host state while saved writers stay protected. Thirty-three sources span eight consumers. All 1,731 Net tests, 18 launcher tests and 727 native checks pass (25 catalog cases, 90 powertrain checks; all 637 prior checks preserved). [Results and tested hashes](../build/powertrain-engine-input-smoke/result.json) record the tested Debug payload and clean builds. Cylinder-head and main-bearing inputs, rotation/failure presentation, radiator-fan/battery/wiring, remaining engine inputs, host wear while guests drive and two-player acceptance remain open.

#### 2.2 · Drivetrain wear + tire pressure/puncture  `PARTIAL`
- **Guest native punctures (local/unreleased, v232)** Request 218 reports a validated local driver flat-state entry; host ownership/freshness/living-player/sequence and per-wheel epoch checks gate the native saved-health zero write. Message 205 appends four epochs; repair, replacement and availability changes retire old events without invalidating other tyres. Guest saved writes stay blocked. 18 local two-game checks include prepared rolling, handoff, parked rejoin, concurrent damage, native flat-to-healthy repair and stale/replay refusal. Full assembly/powered driving, ordinary wear, tyre save/reload and Steam acceptance remain open; the separate differing-native-world parking-joint instability is now locally closed (see the current checkpoint). [Evidence and limits](BUILDING.md#guest-corris-punctures-2026-09-13-unreleased-v232).
- **Host wheel-health inputs (local/unreleased, v209)** Message 205 delivers exact native host mount health for FL/FR/RL/RR, with independent availability and revisions outside driver leases. Read-only capture covers parked host changes, joins and vehicle resync; snapshots do not consume pending live publication. Guarded guest healthy/flat readers and grip arithmetic use host values while driving, observing and parked. Missing input, metadata outages and temporary unregistration pause/recover affected consumers without reading personal health or writing guest saves. The old condition health bytes no longer supply registered native readers. Durable tyre wear under guest driving, physical repair/type/grip reconciliation, pressure refills and full failure/lifecycle/live acceptance remain open. Next free ID is 206. [Validation and limits](BUILDING.md#host-wheel-health-inputs-protocol-209-unreleased).
- **Wheel rim presentation (local/unreleased, protocol v208 unchanged)** Healthy and flat observers now enter the audited two-action rim state directly through an authenticated condition application, including native radius/friction and puncture-sound cleanup. This avoids selecting the observer's saved tyre type or entering the host's saved-health puncture writer. Late/changed bindings retry accepted state; keepalives repair physical drift. All four wheels and the live host saved-health writer are checked. Rim-to-tyre repairs, rim-to-flat transitions, saved tyre wear, parked repair publication and live driving acceptance remain open. No new message; next free ID stays 205. [Validation and limits](BUILDING.md#wheel-rim-presentation-protocol-208-unreleased).
- **Guest gearbox failure wear (local/unreleased, v208)** Message 204 reports actual guest driver native kick-out callbacks. The host requires current ownership and an installed saved gearbox with DamageType 1–3, then applies the native 0.0525 wear subtraction once and publishes message 202. Duplicates, stale events, changed targets and competing host callbacks cannot double-charge; repair cannot replay rejected use. Full physical failures, fitted-part lifecycle, tyre wear, parked wheel repair and live two-player save/reload acceptance remain open. Next free ID is 205. [Validation and limits](BUILDING.md#guest-gearbox-failure-wear-protocol-208-unreleased).
- **Guest automatic gearbox oil use (local/unreleased, v207)** Message 203 reports actual guest driver State 1/3 native callbacks. The host validates ownership, ordering, a shared vehicle budget and the mounted automatic gearbox, then applies its current-wear native oil calculation/subtraction once. Competing host callbacks are suppressed; message 202 publishes the saved result. Guest oil writes remain protected. Full automatic physics/failures, fitted-part lifecycle and live two-player save/reload acceptance remain open. Next free ID is 204. [Validation and limits](BUILDING.md#guest-automatic-gearbox-oil-use-protocol-207-unreleased).
- **Host gearbox oil input (local/unreleased, v206)** Message 202 appends exact native oil and availability (28 bytes total). Host capture uses the validated gearbox mount; invalid oil withdraws independently of valid wear. Parked refills and oil-only changes advance the existing host revision through live broadcasts, joins and resync. The guarded automatic Set stall speed reader uses host oil while native clamping, shift RPM and stall calculations remain active. Missing oil pauses only automatic; saved guest oil drains stay blocked. Full automatic driving/failure, delegated host oil loss, fitted-part lifecycle and live two-player acceptance remain open. Next free ID remains 203. [Validation and limits](BUILDING.md#host-gearbox-oil-input-protocol-206-unreleased).
- **Guarded guest drivetrain wear consumers (local/unreleased, v205)** Four native Transmission/automatic gearbox readers use exact host wear through driver changes, guest driving and parking. Guest DamageType assignment, driveshaft BREAKOFF and both automatic OilLevel writers are blocked, with external saved-gearbox integer/float destination guards. Missing/withdrawn results, missing metadata and changed bindings pause/recover affected consumers without saved writes. Host/solo lookup remains native and guest save guards survive disconnect. Message 202 and next free ID 203 are unchanged. Complete physical failure effects, fitted-part lifecycle, tyre wear, ordinary parked wheel repair and live two-player acceptance remain open. [Validation and limits](BUILDING.md#guarded-guest-drivetrain-wear-consumers-protocol-205-unreleased).
- **Host drivetrain wear publication (local/unreleased, v204)** Reliable message 202 carries exact saved driveshaft/gearbox/rear-axle wear and availability to every guest. Host revision history is independent of drivers, parked condition and native body replacement. Read-only full-graph capture covers live polls, joins and targeted resync; failures/removal withdraw all values and repaired sources recover. Guests retain copied results before discovery and across ownership changes, without saved writes or failure replay. Native Transmission/automatic-gearbox consumers still need protection: the audit identifies saved DamageType assignment, BREAKOFF dispatch and OilLevel subtraction on those paths. Complete consumer/fitted-part lifecycle and two-player acceptance remain open. Next free ID 203. [Validation and limits](BUILDING.md#host-drivetrain-wear-publication-protocol-204-unreleased).
- **Host periodic drivetrain wear (local/unreleased, v203)** Fresh accepted driver differential speed supplies only the audited native comparison and three divisions; native host saved writes and the two-second wait remain responsible for wear. Whole-graph, canonical target and writer-cache validation pauses this FSM on unsafe bindings and retries. Stale/unavailable inputs contribute zero; a mod validation budget rejects any input costing more than one Wear point on any target per cycle. Local driving, repaired bindings, body/scalar replacement and teardown preserve native ownership. Wire layout and IDs are unchanged. Result publication, complete fitted-part lifecycle, tyre wear, gearbox failure wear and two-player acceptance remain open. [Validation and limits](BUILDING.md#host-periodic-drivetrain-wear-protocol-203-unreleased).
- **Native differential-speed telemetry (local/unreleased, v202)** VehicleState 60 appends availability plus the signed native differentialSpeed float (30 bytes total). Exact Wear.State 1 GetProperty metadata validates a unique drivetrain on the same body; capture reads its current field rather than two-second-old wear scratch. Disabled/unready/changed sources withdraw availability and recover. Live/final sends, accepted copies and fresh guest-owned host snapshots retain the field; receipt does not write native fields or wear. Native host wear consumption, mounted-target authority and result publication remain open. Next free ID remains 202. [Validation and limits](BUILDING.md#native-differential-speed-telemetry-protocol-202-unreleased).
- **Periodic drivetrain saved writes (local/unreleased, v201)** Three additional guest writes are protected at `Drivetrain::Wear`, state Wear #1/3/5: driveshaft, gearbox and rear axle Data.Wear. Native rate calculations and two-second Wait remain active, and host/solo writers remain native. The fresh 12-FSM drivetrain audit identifies signed differentialSpeed → abs → >1 → divisors 42300/31500/22800. Shared host wear requires that exact driver input and safe native target/result authority; RPM and road speed are not substitutes. No wire layout changes; next free ID 202. [Validation and limits](BUILDING.md#guest-periodic-drivetrain-write-protection-protocol-201-unreleased).
- **Host-confirmed condition release (local/unreleased, v200)** Message 201 confirms the accepted guest final condition to its sender. Matching body/player/final-report/release sequences become approved parked inputs after actual local release; new authority and stale/forged messages cannot revive a past lease. Host snapshots and condition checksums retain that approved result, preserving late joins and former-driver continuity. Guest native drift still requests reconciliation. No pose/save writes or existing layout changes; next free ID 202. New wear, ordinary parked native repair publication, initial missing-input claims and full flat/rim effects remain open. [Validation and limits](BUILDING.md#host-confirmed-condition-release-protocol-200-unreleased).
- **Guest condition claim inputs (local/unreleased, v199)** An actual guest physics claim retains eligible copied tyre-health and gearbox-damage input before the old owner is cleared. Native protected reads and the first outgoing capture use the approved values instead of local saved data; partial/withdrawn saved-part fields stay unavailable in reports. Inputs belong to the claimant and body until release, accepted competing motion, body replacement or teardown. Pressure and discrete transitions remain native; layouts/IDs are unchanged. Claims without initial shared input, pre-claim lookup, post-release host confirmation, new durable wear and complete flat/rim repair remain open. [Validation and limits](BUILDING.md#guest-condition-claim-inputs-protocol-199-unreleased).
- **Tyre-reader safety (local/unreleased, still v198)** Cached healthy/flat readers revalidate before preparation can recover them. Changed operands, missing/aliased outputs and moved/renamed identities pause only the affected consumer. Native fallback is checked even without shared state; capture and application exclude Health aliased to the referenced saved tyre. This repairs protection under the existing protocol; driver bootstrap, durable host wear and flat/rim/repair remain open. [Regression evidence and limits](BUILDING.md#tyre-reader-safety-still-protocol-198-unreleased).
- **Observer gearbox condition input (local/unreleased, v198)** The native DamageType reader now selects damage branches from available accepted current-owner or approved parked state on registered guest observer cars. Native source/cache and saved damage/wear are preserved. Exact reader validation is paired with the Reverse saved-wear guard; invalid signatures, aliases and changed identities pause and recover. Validation passes 3,842 Net tests, 18 launcher tests and 3,806 native checks (29 new; all 3,777 previous retained). Driver bootstrap, durable host wear and flat/rim/repair behavior remain open. [Validation and limits](BUILDING.md#observer-gearbox-condition-input-protocol-198-unreleased).
- **Condition availability and late discovery (local/unreleased, v197)** Finding pressure no longer stops wheel discovery. State 65 appends six availability bits (17-byte packet), distinguishing missing native inputs from known zero. Binding/readiness changes retry and reconcile eligible accepted or approved parked state without another packet. Withdrawn fields stop shared reads and pressure replay; guest checksums ignore undeclared native fields while detecting declared drift. Ownership and sequence history remain intact. Validation passes 3,807 Net tests, 18 launcher tests and 3,777 native checks (30 new; all 3,747 prior retained). Driver bootstrap, durable host wear, full flat/rim/repair, physics and ordinary parked publication remain open. [Validation and limits](BUILDING.md#condition-availability-and-late-discovery-protocol-197-unreleased).
- **Native wheel pressure application (local/unreleased, v196)** Accepted pressure reaches all four Wheel components through the audited TIRES property writes. The catalog records native enabled flags; shared application briefly enables the six disabled FR/RL/RR writes and restores flags/active scheduling afterward. Complete source/target/action validation precedes dispatch; late bindings and pressure drift recover without another packet. Local/pre-claim drivers remain excluded and simulation enablement is unchanged. Validation passes 3,667 Net tests, 18 launcher tests and 3,747 native checks (28 new; all 3,719 previous retained). Full physics/stiffness behavior, driver bootstrap, tyre wear, flat/rim/repair and ordinary parked publication remain open. [Validation and limits](BUILDING.md#native-wheel-pressure-application-protocol-196-unreleased).
- **Parked observer continuity (local/unreleased, v195)** Accepted final release retains a copied, body-bound condition for guest wheel reads after ownership clears. Host updates and new claims supersede it; departure preserves the approved result and session teardown clears it. Timeout, unowned finals and rejected poses cannot approve a parked result. New local claims publish a fresh baseline without resetting sequence history. Validation passes 3,630 Net tests, 18 launcher tests and 3,719 native checks (18 new; all 3,701 previous retained), including replay of actual sender packets. Parked host publication, driver bootstrap, durable host wear and wheel physics remain open. [Validation and limits](BUILDING.md#parked-observer-tyre-health-protocol-195-unreleased).
- **Observer tyre health inputs (local/unreleased, v194)** The eight healthy/flat native GetFsmFloat readers now use accepted current-owner condition on registered guest observer cars, including synchronous PUNCTURE entry before accepted-state commit. Native grip arithmetic consumes shared health; saved tyre values and native source/cache remain intact. Admission validates both readers alongside existing writer guards; missing/changed readers pause and recover after repair. Drivers/pre-claim players, host operation, missing state, ownership changes and disconnected sessions retain native lookup. Validation passes 3,602 Net tests, 18 launcher tests and 3,701 native checks (26 new; all 3,675 previous retained). Driver bootstrapping, authoritative wear, parked publication, gearbox inputs and full physical flat/rim/repair behavior remain open. [Validation and limits](BUILDING.md#observer-tyre-health-inputs-protocol-194-unreleased).
- **Guest saved-part writers (local/unreleased, v193)** The catalog adds all eight Corris wheel TireHealth writers (continuous wear and flat-entry zero) and GearboxDamage Reverse Wear subtraction. Native admission and state-entry guards retire ongoing writes, preserve reader siblings and pause changed signatures until repaired. Solo/host writes remain enabled. Validation passes 3,575 Net tests, 18 launcher tests and 3,675 native checks (28 new; all 3,647 previous retained). This closes those nine known guest mutations; accepted host condition still needs safe native inputs, authoritative wear under guest driving and physical flat/rim/pressure handling. Parked publication and two-player/save acceptance remain open. [Validation and limits](BUILDING.md#guest-tyre-and-gearbox-write-protection-protocol-193-unreleased).
- **Condition stream ownership (local/unreleased, v192)** Reports now require the authenticated current driver; unowned/previous-owner reports, duplicate zero, stale packets and contradictory flat/rim flags are rejected before native writes or relay. Per-sender history survives handoffs; host snapshots use copied accepted state for guest-owned cars and reserve sequence 65535 without consuming live baselines. Final condition precedes final item release reliably. Validation passes 3,570 Net tests, 18 launcher tests and 3,647 native checks (21 new; all 3,626 previous retained). Native consumer/writer isolation and parked publication remain open. [Validation and limits](BUILDING.md#condition-stream-ownership-protocol-192-unreleased).
- **Native pressure correction (local/unreleased, still v191)** Installed TirePressure.Data stores Pressure/PressureOptimal as 190, passed directly to Wheel.pressure/optimalPressure. The mod previously treated 190 as bars, saturated the wire byte to 255, and wrote 2.55 into the native FSM. Capture/apply now convert explicitly between native kPa and the existing bar×100 byte. Live owner updates, snapshots and recapture share this boundary. Wire layout/meaning is unchanged. Validation passes 3,527 Net tests, 18 launcher tests and 3,626 native checks (13 new; all 3,613 previous retained). [Validation and limits](BUILDING.md#native-tyre-pressure-units-still-protocol-191-unreleased).
- **Reopened native gaps** The fresh audit proves Condition.Health is scratch read every frame from ThisTire.Data.TireHealth; direct writes do not establish shared durable wear. RIM is a local Check rim transition, not a global event, so sending it from a healthy/flat state cannot guarantee rim presentation. Entering Flat friction writes the referenced TireHealth to zero; v193 suppresses this and continuous tyre wear on guests. GearboxDamage.DamageType is also scratch, read from the fitted gearbox, and Reverse subtracts native Wear (also guarded on guests by v193). TirePressure's TIRES copies pressure into wheel properties, while ENABLEPRESSURE flips Enabled; neither may be replayed indiscriminately. The stream still stops on parked unowned cars. The v192 change above fixes unowned guest acceptance and dedup/handoff ordering. Implement safe authority and native consumer/writer isolation before marking 2.2 complete; pressure units alone do not satisfy it.
- **Impact** HIGH. Continuous wear + tire flats diverge per client.
- **Game truth** `…/Systems/Drivetrain::Wear` (`RateGearbox/RateDriveshaft/RateRearAxle`; `Transmission Damage`/`WORN`); `…/TirePressure::Data` (`Pressure`); per-wheel `WHEELc_XX::Condition` (`PUNCTURE/RIM`, `Health/FinalWear/Friction/GripReduction`, `TireType`).
- **Model** The host owns saved wear; guests supply validated simulation inputs and receive revisioned host results independently of driving leases. Native CORRIS tyre-health inputs now come from host message 205 independently of the driver. The existing `VehicleCondition` stream still delegates pressure, flat/rim flags and the gearbox damage category to the driver. Finish native consumer/writer isolation and concrete failure authority before treating that delegated stream as complete.
- **Protocol** `VehicleCondition` (owner→host→peers) in `63–79`; +version; PROTOCOL.md. **Touch** `VehicleWorldSync.*`. **Done when** wear + a flat tire agree across peers + join. **Watch** E, B, F (per-vehicle wrap-aware seq). **Deps** none.

#### 2.3 · Repair-shop (Fleetari) service results + order record  `TODO`
- **Impact** HIGH. Engine tune, gear ratios, paint color, bodywork, tire/spring work — the outputs you pay for — never propagate.
- **Game truth** `REPAIRSHOP/Jobs/*::Work` FSMs (unhooked; scanner only handles Use/Screw/Buy/Data/Button). Outputs: `SettingAFR/SparkAngle/Valve*/OilViscosity`, `FinalGear`, spring stiffness, tire condition, bodywork, `PagePaintCar` color. `OrderFleetari` record (`Order`, `UTJobs`, `PaintCode/TireCode/AxleCode`, `JobTotalCost`).
- **Model** **Capture-and-pair** (mirror `MailOrderSync`): the guest configuring the brochure has the host capture the exact `OrderFleetari` record and pair it to that guest's next `PurchaseIntent(PAYMENT)`; the host applies the service authoritatively and broadcasts the resulting car settings (tune/gears/paint) as vehicle state so peers see the painted/tuned car.
- **Protocol** `FleetariOrderState`/`Intent` in `93–99` (like `MailOrderState`/`Intent`); +version; PROTOCOL.md. **Touch** new `Sync/RepairShopSync.cs`; catalog the `REPAIRSHOP/Jobs/*::Work` hooks; extend vehicle snapshot with paint/tune. **Done when** a paint/tune bought by one player shows on the shared car for all. **Watch** A/C (order retire), E. **Deps** 2.1/2.2 help (shared condition surface).

#### 2.4 · Register the taxi (MACHTWAGEN)  `TODO`
- **Impact** MEDIUM. The taxi is a full drivable sim car that never syncs — peers see it frozen; collisions invisible.
- **Model** After 0.2 relaxes `RequireRoot`, confirm MACHTWAGEN registers and streams through the generic `VehicleWorldSync` discovery (engine/electricity/fuel/lights/climate by child-FSM name). No taxi-specific code should be needed beyond registration.
- **Protocol** none. **Touch** verify only; add a catalog/name entry if 0.2 used an allow-list. **Done when** driving the taxi streams pose/engine/lights to peers. **Watch** E, F. **Deps** 0.2.

#### 2.5 · Gearbox / clutch state  `TODO`  *(lower priority)*
- **Impact** LOW. Selected gear/clutch is driver-local; observers see wrong gear indicator, minor.
- **Game truth** `Drivetrain::Gears` (`Gear` int, `IsManual/InstalledAuto`), `Transmission AutoClutch`, `GearboxManual/Clutch`.
- **Model** Add a gear field to `VehicleState` (owner→peers). **Protocol** extend `VehicleState` (id 60) — +version; PROTOCOL.md. **Touch** `VehicleWorldSync.Engine.cs`, `WorldMessages.cs`. **Done when** gear indicator agrees. **Watch** E. **Deps** none.

### Phase 3 — Income & jobs

> Shared pattern for 3.x: the payout must reach the **host wallet** (never a local `Money` write), and any host-owned job/record state broadcasts + retires. Route cash-in through the existing guarded purchase/wallet path or a host-validated payout record (mirror `PoliceSync`'s validate-and-own). Most also need the paying/earning player's **fresh-pose proximity** to the payer NPC/table.

#### 3.1 · Flea-market selling  `PARTIAL — active`
- **Payment dependency (v236)** Native rent selection only builds a cart; checkout charges once. Dedicated host receipts replace the old unpaid RENT hook and generic checkout/envelope replay. Guests pause table Logic/Sell/DayChanger and restore native finances/cart/envelope on cleanup. Host mixed merchandise keeps its native flow; guest checkout currently requires an otherwise empty rental basket.
- **Remaining outcome** List/price one supported shared native item, observe unsold retrieval or native host sale, retire the exact object, and preserve/rejoin listings through native host saves. Identical item names must never identify the sold object by themselves.
- **Native evidence** [Fresh table, price-entry, checkout and save findings](SYNC-SCOPE-AUDIT.md#flea-market-native-audit-and-payment-dependency-2026-09-13-v236-unreleased). Native rental day 0 is active; collection appears after expiry. Unsold retrieval still needs inspection.
- **Protocol/code** `FleaSaleState/Intent` 101/102 and `FleaSaleResult` 223; next free ID 224. `FleaSaleSync`, `FleaSaleBinding`, `FleaSalePolicy`, catalog `fleaSale`. Payment tests stage proceeds, so they do not establish a completed item sale.
- **Reassessment** Stay on J04 to finish the selected earning journey. After closure, rotate to ordinary supply creation. No broader jobs/performance expansion.

#### 3.2 · Taxi job lifecycle  `TODO`
- **Impact** HIGH (job) — depends on 2.4 for the vehicle.
- **Game truth** `JOBS/TAXIJOB` (`Logic` Activate/Disable-car), `TaxiFunctions::Payments` (`Money/Employed/KMsDriven/Phonebill`→Bank), `PaymentTerminal/Payment::Use`, `Customer1/TaxiWalker PayMoney/Receipt`, `Taximeter/Tripmeter`.
- **Model** Host owns the job: employment/fare/payment state broadcast; fare payout → shared wallet via host validation; passenger NPC (the fare) streams like an NPC. Job enable/disable of the taxi vehicle is host-owned world state.
- **Protocol** `TaxiJobState`/`Intent` in `101–119`; +version; PROTOCOL.md. **Touch** new `Sync/TaxiJobSync.cs`; reuse `JobSiteSync` shape. **Done when** a completed fare pays the shared wallet and both see taxi enabled/positioned. **Watch** A/B/C, E. **Deps** 2.4.

#### 3.3 · Kilju: fermentation + selling  `TODO`
- **Impact** MEDIUM. Bucket ferments over days (host time); selling to the buyer NPC pays into the shared wallet on un-synced quality.
- **Game truth** `EQUIPMENTS/bucket(itemx)::Use` (`Alcohol/Sweetness/Vinegar/BrewTime/Yeast/Sugar/Water`, `KiljuFinished/LidOn`), `TriggerIngredients` (`SUGAR/YEAST`), `TriggerFill`; sell at `JOBS/JOKKE/HouseDrunkNew/KiljuBuyer/CanTrigger::Logic` (`KiljuValue/Price`) + `PayMoney::Use`.
- **Model** The bucket's brew state is host-owned world state (it advances on the host clock): stream it as tracked-item scalar state (like `FluidContainerState`) so both see the same brew; ingredient adds are anyone-triggers/ownership intents; selling routes payout through the host wallet and derives price from the *agreed* brew state.
- **Protocol** extend `FluidContainerState` or add `BrewState` in `101–119`; +version; PROTOCOL.md. **Touch** new `Sync/KiljuSync.cs`; catalog ingredient triggers. **Done when** brew quality + sale payout agree. **Watch** B/C, D, E. **Deps** none.

#### 3.4 · Hitchhiker rides  `TODO`
- **Impact** MEDIUM. Ride-for-money payout is local; the passenger NPC riding one player's car is invisible to others; dangerous variants (KiljuMurderer) branch per-client.
- **Game truth** `JOBS/KILJUGUY/HikerPivot/Hitchhiker::Logic/Timer` (`Money/Paid`), `suitcase(itemx)/Money::Use`, `KiljuMurderer` spawn, `Hitchhiker::Save` (persistent stage: DrunkStage/Story).
- **Model** Host owns the hitchhiker's state + spawn (which variant, story stage) and streams it; boarding a car is a passenger-NPC event (reuse `PassengerController`/NPC streaming); payout → host wallet.
- **Protocol** reuse `NpcTransform` for the body + a small `HitchhikerState` in `101–119` for stage/payout; +version; PROTOCOL.md. **Touch** `NpcTrafficSync` (register the hiker) + new state. **Done when** the same hiker/variant rides + pays on both. **Watch** C, E, F. **Deps** none.

#### 3.5 · Farm job (hay / combine)  `TODO`
- **Impact** MEDIUM. Job stage + payout local.
- **Game truth** `JOBS/Farm/Farmer/Walker::Speak` (`JobStage`, `HAYBALE/COMBINE/PAYMENT`), `Farmer/…/PayMoney::Use` (`Money/RandomCash`).
- **Model** Mirror `JobSiteSync` (host-owned job stage + payout to wallet). **Protocol** extend `JobSiteState` (id 56) with a farm kind, or a new job state in `101–119`; +version; PROTOCOL.md. **Touch** `JobSiteSync`. **Done when** stage + payout agree. **Watch** A/B/C. **Deps** none.

#### 3.6 · Kela welfare / unemployment income  `TODO`
- **Impact** MEDIUM. Recurring benefit income keyed to per-client claim status.
- **Game truth** `Systems/Expenses::Kela`; `Sheets/{UnemployPaper,UnemployApplication,JobContract,UnemployCancelPaper}` (BUY-guarded repayment).
- **Model** Host owns claim status + benefit calc; benefit payment/repayment route through the shared wallet; broadcast claim state. **Protocol** `WelfareState` in `101–119` + catalog the pay buttons; +version; PROTOCOL.md. **Touch** new `Sync/WelfareSync.cs`. **Done when** benefit income agrees. **Watch** A/C. **Deps** none.

### Phase 4 — Crime & consequence

#### 4.1 · PlayerWanted crime counters + wanted level  `TODO`
- **Impact** MEDIUM–HIGH. `ManSlaughter/AttemptedManslaughter/PoliceEvasion/TrafficFatality/DaysFines` accrue per-client; only the fine byte reaches the host.
- **Game truth** `Systems/PlayerWanted::Activate` (int counters, `Sentence/DaysInJail`), crime entry via `HumanTriggerCop/HumanTriggerCrime::CarHit:Crime`.
- **Model** Host-validated record (mirror `PoliceSync`): the guest reports a locally-observed crime (validated by fresh pose + the exact known crime trigger); the host owns the wanted-level counters and broadcasts them. This is the foundation for 4.2–4.4.
- **Protocol** `WantedState`/`CrimeReport` in `93–99`; +version; PROTOCOL.md. **Touch** extend `PoliceSync` or new `Sync/WantedSync.cs`. **Done when** wanted level converges. **Watch** A (advance on host ack), C (retire on served), E. **Deps** none.

#### 4.2 · Arrest + jail sentence  `TODO`
- **Impact** HIGH. Whole arrest→jail flow is offender-local: one player jailed while the other roams free; the JAIL interior (day-countdown, confinement, cousin/food/sleep) runs only on the jailed client.
- **Game truth** `Systems/PlayerWanted` `Check wanted→Activate cops` spawns `COPS/CopHome*`/`PoliceCarPSK`; `JAIL/Functions::Time` (`DaysLeft/Sentence`), `MAP/JailSafeTrigger`, `GUI/HUD/Jailtime`.
- **Model** Host owns the sentence: when the host's wanted logic decides an arrest, broadcast a jail record (who, sentence, release day); the jailed player's confinement is host-driven; peers see them removed from the world for the duration (like a scoped despawn) and returned on release. Arrest is a host-owned flow (PLAN already names arrest/impound as needed).
- **Protocol** `JailState` in `93–99`; +version; PROTOCOL.md. **Touch** `Sync/WantedSync.cs`. **Done when** a jailed player is jailed on all machines and released together. **Watch** C (release edge), E. **Deps** 4.1.

#### 4.3 · Vehicle impound / tow  `TODO`
- **Impact** MEDIUM. Car relocated during arrest has no host-owned flow; the car is a shared object.
- **Model** Host performs the impound (moves/locks the vehicle) and broadcasts the relocation via the existing vehicle ownership/transform path. **Protocol** likely none (reuse vehicle reconciliation) or a flag; PROTOCOL.md if wire changes. **Touch** `VehicleWorldSync`, `WantedSync`. **Done when** an impounded car is relocated for all. **Watch** E. **Deps** 4.2.

#### 4.4 · Police chase + DUI arrest path  `TODO`
- **Impact** MEDIUM. Chase trigger/target/siren decided per-client; DUI escalation-to-arrest unsynced (only the fine byte).
- **Game truth** `TargetDispatch1/2::Check` (`Chase`), `POLICECAR*/Navigation::Chase`, `Sirens`, `CheckPoint::Traps`; `CopAlc1/2` (Alcometer→Fine→ArrestWarrant).
- **Model** Host decides + owns the pursuit (whom it targets, siren/trap state) and streams the cop car (already NPC-streamed) plus the chase-active flag; DUI stop escalates through the 4.1/4.2 wanted/arrest record, not just the fine bit.
- **Protocol** small `PursuitState` in `93–99` or fold into `WantedState`; +version; PROTOCOL.md. **Touch** `WantedSync`, `NpcTrafficSync`. **Done when** a chase triggers on all peers against the same target. **Watch** C, E. **Deps** 4.1.

### Phase 5 — Racing completeness

#### 5.1 · Rally results/reward + enroll + parc-fermé  `TODO`
- **Impact** HIGH. `RallySync` syncs only stage timing; the scoring/reward ledger, enroll, and penalties are unsynced.
- **Game truth** `RALLY/ResultsWeekend::Data` (registration, class, `TimeSS1-3`, placement, `GOLD/SILVER/BRONZE→Money`, `PriceMoney`, `Winner/RaceOver`, ~30 `UniqueTag*`); `RALLY/Scenery/RegisterRally::Activate` (`Registered`); `RALLY/ParcFerme::Logic` (day-gate + `TimePenalty`).
- **Model** Host owns the results ledger + enroll + penalties (mirror `IceRaceResultsSync`/`RallySync`): the host computes placement/reward from the agreed stage times it already owns and broadcasts the result board + reward (reward via wallet). Enroll is a host-owned record.
- **Protocol** `RallyResultsState` + enroll in `93–99` (near ice-race results 91); +version; PROTOCOL.md. **Touch** `RallySync`, new results sync. **Done when** both see the same standings + payout, and enroll agrees. **Watch** B/C. **Deps** none.

#### 5.2 · Rally PartsSalesman vendor  `TODO`
- **Impact** MEDIUM. Sells 12 rally parts for cash + SPAWNITEMs them; its `Buy` FSMs use non-standard state names (`Wait button 2`/`Wait`/`Purchase`/`Money`, no `Check money`) so the generic `shopBuy` catalog rule doesn't match (exact-match `FsmHook.FindState`).
- **Model** Add explicit catalog `buys[]` entries for each `Buy*` FSM with its actual state/entry-guard names; the spawned parts go through the item-spawn manifest (see 7.2). **Protocol** none (catalog + existing purchase/spawn). **Touch** `catalog/sync-catalog.json`. **Done when** buying a rally part debits the shared wallet and spawns for all. **Watch** spawn manifest (7.2). **Deps** 7.2 helps.

#### 5.3 · JOKKIS banger-race lifecycle  `TODO`
- **Impact** MEDIUM. The JOKKIS car is vehicle-synced, but its race lifecycle isn't — lap/time/result run per-client.
- **Game truth** `JOKKIS/DB/RaceTrigger::Data` (identical LAP/TIME/checkpoint/ENDRACE structure to CORRIS, which `IceRaceSync` hooks).
- **Model** Reuse `IceRaceSync`/`RallySync` directly — register the JOKKIS race markers and drive host-validated progress + result. **Protocol** reuse ice-race/rally messages if shape matches, else extend; +version if wire changes; PROTOCOL.md. **Touch** `IceRaceSync` (add JOKKIS markers) or a thin sibling. **Done when** both see the same JOKKIS standings + payout. **Watch** the rally/ice-race rejected-checkpoint stall (known limitation — edge-triggered crossings). **Deps** none.

### Phase 6 — Home & survival depth

#### 6.1 · Oven/stove cooking + fire hazard  `PARTIAL`
- **Current** Protocol233 implements both homes' knobs, full native heat, grill/burn triggers, light/smoke and reversible guest simulation. One shared moose-meat cooking result uses state210. Complete house fires, sausage conversion and other food state remain separate gaps; see H06/I08/I09/H11.
- **Impact** MEDIUM. Cooking and unattended-stove hazards must have one shared outcome.
- **Game truth** `…/OvenStove/Simulation::Data` (Grill/Fire/Smoke), `KnobPower1-4/KnobTempOven/KnobModeOven::Screw`, `SausageTrigger`.
- **Model** Host-owned appliance state (knob settings + cooking/fire sim broadcast; knobs as anyone-triggers controls). Cooked-food item via spawn manifest.
- **Protocol** `ApplianceState`99 and `StoveKnobIntent`219, v233; catalog `stoves`. **Touch** `Sync/ApplianceSync.cs`, `.Stoves.cs`, `.StoveBindings.cs`. **Done when** remaining food conversions and a complete house-fire lifecycle agree, beyond the bounded stove candidate. **Watch** B/C, D. **Deps** 1.3 (shares electricity/blackout).

#### 6.2 · Electric home sauna  `TODO`
- **Impact** MEDIUM. Only the cottage wood sauna is registered in `HeatSourceSync`; the home electric sauna isn't.
- **Game truth** `YARD/Building/SAUNA/Sauna/Simulation::Time` (`SaunaHeat/StoveHeat/Power/Fuse/ELEC_CUTOFF`), `Kiuas/ButtonTime::Screw`, `Kiuas/StoveTrigger::Steam`.
- **Model** Add it to `HeatSourceSync`'s registration list (its 5-entry hardcoded set) — same State/Intent path as the other heat sources; it also consumes `ELEC_CUTOFF` (ties to 1.3).
- **Protocol** none (reuses `HeatSourceState/Intent`). **Touch** `HeatSourceSync.cs` registration. **Done when** the electric sauna heats identically on peers. **Watch** D (verify var types), E. **Deps** 1.3 for cutoff.

#### 6.3 · Incoming phone-call events  `TODO`
- **Impact** LOW–MEDIUM. Which topic rings + when is per-client RNG/time.
- **Game truth** `HOMENEW/…/Telephone/Logic/PhoneLogicNEW::Ring`, `RingingNEW::Ring` (per-client `JokeEvent`, `RingTimes`).
- **Model** Host decides the call (topic + time) and broadcasts a ring event; guests replay it (audio/subtitle local). **Protocol** small `PhoneCallEvent` in `101–119`; +version; PROTOCOL.md. **Touch** new sync or fold into an events sync. **Done when** both phones ring for the same call. **Watch** C. **Deps** none.

#### 6.4 · Fridge chilling · appliance consumption · fuses  `PARTIAL`
- **Impact** LOW–MEDIUM. Fridge chilling gates food freshness (elec-aware); per-appliance consumption feeds the bill; individual fuses (per-room) — only the aggregate MainSwitch is cataloged (PLAN defers individual fuses).
- **v223 evidence:** 46 local checks cover electricity cutoff/restoration, main switch state, guest fridge doors, warm/cooled milk rates and spoilage, plus differing guest power on reconnect. Native closed-fridge cooling remains latched on power loss until a door cycle. Chilling internals are not directly broadcast; arbitrary prior cooling-area states on join and fuse changes remain unverified.
- **Model** Fold into the `ApplianceState`/`UtilityBillSync` from 6.1/1.3: host-owned consumption + fridge chilling; per-room fuse bools as anyone-triggers if pursued (else keep deferred). **Protocol** extend `ApplianceState`; +version if wire changes; PROTOCOL.md. **Touch** `ApplianceSync`, `UtilityBillSync`, catalog fuses. **Done when** fridge/consumption/fuses agree. **Watch** B/C. **Deps** 1.3, 6.1.

### Phase 7 — World & wildlife

#### 7.1 · Moose death → corpse → meat chain  `PARTIAL`

v228 repairs native CarHit detachment/destruction being missed by the old mover
stream and adds independent host corpse poses/counts (214), validated guest chops
(215), pre-destruction guest death reports, and disconnect restoration of the
original guest animal. Native axe comparison → Sound → Pieces is exercised by
local fixtures; count-bound host acceptance produces v224 exact meat IDs once.
Retries, concurrent host/guest chops, both native four-piece limits and reconnect
are covered. [Evidence](BUILDING.md#guest-moose-chopping-2026-09-13-unreleased-v228).

Physical axe/vehicle collisions, native meat save/reload, natural cooking/spoilage,
physical eating/pickup and Steam/two-PC acceptance remain open. The complete chain
is still PARTIAL until those gameplay checks pass.

#### 7.2 · Spawner manifest completeness  `TODO`

- **v125 guest distributor timing implemented; multiplayer checks pending:**
  VIN131 uses a separate distributorTiming profile for root HandRotate and the
  Pivot/mesh pose. Empty-hand input within 1 m requires Tightness below 8; the host
  validates revision, mount, proximity, readiness and native 0.01-second cooldown
  before applying ±0.2 degrees within 0–20 through operations 2/3. Arbitrary
  fractional SparkAngle baselines remain valid; wheel down increases and wheel up
  decreases timing. Host entry seeds mesh rotation
  from Data, then verifies saved part, mount and pose; native guest HandRotate
  remains disabled. Only the owned child mesh rotates. Fresh loose presentation
  stays native; fitting applies the host angle and removal retains it. Existing
  alternator profiles and all 32 factory identities remain unchanged. Controls
  wait for the latest accepted revision to finish applying to the visible part;
  the distributor's removal box still blocks controls behind it. At v125,
  1,328 protocol/catalog/policy tests, 18 launcher tests and 220 isolated native
  checks pass, including 43 distributor checks. Both Net targets, Core (Debug/Release)
  and the probe build cleanly. [Native evidence](../build/distributor-timing-smoke/result.json)
  records zero failures and matching tested Debug hashes. **Next:** actual two-player
  scroll, bolt/tool gates, fit/remove, save/rejoin and guest engine effects; see BUILDING.md.

- **v123 fitted fanbelt presentation implemented; multiplayer checks pending:**
  optional BeltVisual in state 185 carries host visibility/running, flutter scale,
  pitch/volume and native BeltAnimation RPM × AnimMultiplier scroll speed. A separate
  PresentationRevision orders cosmetic packets without staling physical fitting requests. Guests
  own a skinned mesh with internal bones, material and audio; only UV/flutter phase
  is local. Native Jumping wear/breakoff is paused, renderer hidden and audio muted
  independently of saved-part isolation, including empty guest mounts. No original
  hierarchy or mount fields change, and cleanup restores flags after copies are
  removed. Failed visual binding preserves an existing fitted guest belt. The owned
  loose mesh switches with fitting; latest loose/absent visuals or changed parents
  immediately stop old views during deferred creation. At v123, both Net targets,
  Core (Debug/Release) and probe built cleanly; 1,205 protocol tests, 18 launcher tests
  and 154 isolated native checks passed, including 34 belt checks.
  **Next:** two-player fit/remove, healthy/damaged squeal,
  visibility, save/rejoin checks; rotating pulleys and full guest engine behavior
  are not established. See BUILDING.md's v123 checklist.

- **v122 oilfilter hand tightening implemented; multiplayer checks pending:**
  empty-handed guests scroll within 1 m of a fitted filter. Dedicated operations
  4/5 in 188–189 share the immutable part-operation receipt ledger. The host checks
  revision, fresh proximity, settled mount, native readiness and 0.2-second
  cooldown before executing one native Screw/BOLTING step in integer Tightness
  0–8. Raw Tightness refresh prevents reuse of native pose scratch data. Guest
  Screw stays disabled; resolved host Tightness sets the filter pose. Both Net
  targets, Core and probe built cleanly at v122; 1,145 protocol tests, 18 launcher
  tests and 120 isolated native checks passed (26 hand-control checks). **Next:**
  actual two-player scroll/tool/fit/removal, competing inputs, save/rejoin acceptance
  and remaining engine behavior; see BUILDING.md's v122 checklist.

- **v121 direct bag parts implemented; multiplayer checks pending:**
  `Spawner/CreateItems::Fanbelt` and `::Oilfilter` now use replacement state 185,
  preserving native `FANBELT0` / `OILFILTR0` save identities, Wear/Dirt and Tightness.
  Mixed bag capture counts these outputs without assigning transient grocery IDs;
  publication waits for the exact initialized Data object and retains identity after
  fitting destroys its Rigidbody. Retired parts remain terminal; a failed part
  factory fails that opening without locking unrelated bags. All 30 boxed factory
  identities remain unchanged. Fanbelt fitting retains the native alternator
  SettingRotation > 6 prerequisite. Both new families use existing guest-save
  isolation and replacement fitting/presentation. The protocol-121 suite passed
  94 isolated native checks. Oilfilter hand controls are implemented in v122 above;
  fanbelt presentation is implemented in v123. Full guest engine behavior and
  two-player bag/fit/save/reconnect acceptance remain pending. See BUILDING.md's
  combined checklist.

- **v119 guest alternator adjustment implemented; multiplayer checks pending:**
  both VIN133 and ALTERNATOR0 expose scroll adjustment on owned guest copies with
  a freshly seeded, loosened adjusting bolt. Requests share the part-operation
  ledger, so retries and stale competing clicks cannot apply another turn. The
  host validates native mount/part ownership, fresh nearby player, bolt/pick/hand
  readiness and 0.1-second cooldown, then uses native 0.5-degree turns within 0–7.
  Acceptance verifies the saved part, engine mount and pivot agree; state 185
  updates guest presentation. Native guest HandRotate remains disabled. 1,036
  protocol/catalog/policy tests, 18 launcher tests and 46 isolated Unity/Wine checks
  pass; Core and Net builds are clean. **Next:**
  two-player adjustment/bolt/fit/reconnect checks, other continuous adjustments,
  operational guest engine references and non-box families; see BUILDING.md.

- **Guest part/mount isolation implemented after 0.1.32; multiplayer checks pending:**
  the 30 catalogued boxed-part families retain saved originals in inactive storage
  and release their IDs for host copies. Existing same-ID originals no longer
  silently bypass host state. Settled native mounts pause with their original
  references intact; fitting previews use host occupancy, including pending copies.
  Saved FSMs/item motion are excluded while loading/isolation is pending. Passive
  PartState views keep host checksum data without native action replay. Cleanup
  restores original parent/pose/physics/FSM state after owned copies are removed;
  unexpected nested/unsupported occupants remain preserved. The 30 prefab leaf
  structures are verified; 1,014 automated tests and 28 isolated Unity/Wine checks
  (7 restoration, 21 save-library) pass. **Next:** full two-player
  mount/identity/checksum/reconnect checks, engine references/continuous adjustments,
  and non-box families. Save protection still requires restart before personal play.

- **Guest save protection implemented after 0.1.32; native gameplay checks pending:**
  Core guards native ES2 persistence before admitting guests, covering normal saves,
  LowMemory temporary files, whole-file/tag/folder deletion, renames and direct
  stream storage. Protection lasts until restart, including shutdown and reconnect;
  protected processes cannot host. Host/solo persistence and ordinary PlayerPrefs
  settings remain available. All 21 isolated Unity/Wine save-library checks and
  1,011 protocol/catalog/policy tests pass; Core and Net builds are clean. No wire
  change (protocol 118). Part/mount isolation is now implemented above; continuous
  adjustments/engine behavior and non-box creation remain open. Actual native save/quit,
  permadeath and two-player checks remain pending; see BUILDING.md.

- **Guest replacement bolt controls implemented after 0.1.32; gameplay checks pending:**
  55 integer-step controls across 24 replacement families reuse the native spanner/
  ratchet interface and the existing protocol-118 bolt contract. Owned input/pose
  graphs send host intents without guest native deltas or engine/timing actions.
  Fitted trigger picks require fresh host bolt state after each attachment; stale
  parked observations cannot enable a refitted part. Absolute replies update arrays,
  poses and ordered parent totals; teardown removes owned registries and triggers.
  1,008 protocol/catalog tests pass and Core/Net builds are clean. **Next:** guest
  mount isolation, continuous adjustments/engine behavior and non-box creation.
  Native two-player/LOD/save validation remains pending; see BUILDING.md.

- **v118 guest array-slot fitting implemented, gameplay checks pending:**
  pistons, main bearings and rockers now use the native shared Installer and their
  catalog-bound arrays. The click's slot index is immutable and must match host
  nearest-slot selection; equal distances choose the later index and occupied
  nearest slots are rejected rather than skipped. Native selection and mount
  prerequisite guards confirm only the same candidate/slot in the same frame.
  Unconfirmed previews cancel immediately; committed installation remains native
  and must settle at that slot before acceptance. Asset evidence validates all 17
  mounts, completing fitting adapters for the 30 boxed replacement families.
  Their replacement creation no longer requires the absent Installed scratch flag;
  positive native AssemblyID remains authoritative. 983 protocol/catalog and 18
  launcher tests pass; Core and both Net targets build cleanly. **Next:** guest mount/save
  isolation and operational replica bolt/engine graphs. Native two-player/save
  checks and non-box part creation remain pending; see the v118 checklist in BUILDING.md.

- **v117 guest replacement removal implemented, gameplay checks pending:**
  empty-handed guests can right-click a fitted replica to request native removal.
  The host checks revision, fresh living-player proximity, tightness below one,
  enabled native removal collider and actual mount ownership, then guards the
  native Remove entry and waits for the settled loose body. Installation/removal
  share one immutable receipt ledger; duplicates cannot reexecute and stale clicks
  cannot remove a refitted part. State 185 includes revisioned RemovalAllowed;
  guest ray selection reads disabled BoxColliders without enabling guest physics.
  Native bindings validate all 30 replacement families, including dynamic fitted
  piston/bearing/rocker mounts. 960 protocol/catalog and 18 launcher tests pass;
  Core and both Net targets build cleanly. v118 adds multi-slot
  installation selection. **Next:** guest mount/save isolation and operational replica
  bolt/engine graphs. Native two-player/save checks remain pending; see the v117
  checklist in BUILDING.md.

- **v116 fixed-mount guest replacement fitting implemented, gameplay checks pending:**
  guest-created loose copies offer the normal left click when held at a known free
  mount. The host validates observed revision, fresh living-player proximity, item
  authority and native distance, then executes ASSEMBLING and the native mount's
  prerequisite checks. Requests receive immutable receipts; retries cannot repeat
  fitting, and Busy requires another click. Native installation and settled state
  185 own the result. Generic guest replacement-part state replay is refused.
  Extracted bindings validate 27 fixed-mount families; 930 protocol/catalog and 18
  launcher tests pass. v117 adds guest removal. **Next:**
  guest mount/save isolation and
  operational replica bolt/engine graphs. Two-player/save checks remain pending;
  see the v116 checklist in BUILDING.md. v118 adds piston/main-bearing/rocker selection.

- **v115 fitted replacement presentation implemented, gameplay checks pending:**
  missing boxed-content copies can attach to a ready, unoccupied mount on a known
  native part or vehicle and follow its movement. Host publication waits for the
  mount reference and actual parent to agree. Relative pose/scale and parent identity
  participate in revision validation; stale replays cannot reattach a removed part.
  Fitted copies are non-colliding/kinematic and excluded from item/cargo authority;
  removal restores loose tracking and the current host pose. Unready/occupied mounts
  defer, preserving existing guest-save occupants. Owned child copies detach before
  parent retirement or reconnect cleanup. 896 protocol/catalog and 18 launcher tests
  pass; Core and both Net targets build cleanly. Native action/reference evidence
  covers the 30 replacement families, including the dynamic piston/bearing/rocker
  mounts. v116 adds fixed-mount installation requests; v117 adds removal. **Still open:** guest mount
  isolation, followed by operational engine/bolt graphs for these copies. They remain
  presentation-only while fitted. Non-box parts and native two-player/save checks
  are also unfinished; see the v115 checklist in BUILDING.md.

- **v114 existing native bolt reconciliation implemented, gameplay checks pending:**
  guests send nearby turns to the host; their scalar reports cannot overwrite host
  state. Absolute results restore Bolts[Index], scaled TightnessF and parent total,
  including zero. Join/targeted resync and delayed binding use the same application;
  local receipt ordering stops an older deferred sibling or part report from rolling
  back the latest total. Derived Bolted/Unbolted/Stop replay is removed. Persistent
  identities now include 36 parts without Installed, covering 192 native Data prefabs.
  The asset audit validates 489 ordinary bolt bindings; MUDFLAPa0's ThisPart is
  unresolved, and three drain/alignment controls need separate adapters. Crank-pulley
  and camshaft ordinary turns work through this adapter; guest extra-turn timing
  adjustment is guarded pending separate replication. 872 protocol/catalog and 18
  launcher tests pass; Core builds cleanly. **Next:** construct missing fitted guest
  graphs and mount references; then complete separate adjustments and original
  guest-save isolation, and run the v114 two-player/save checklist. This does not
  enable bolt interaction on the isolated loose replacement copies from v111.

- **v113 native fitting lifetime fixed, gameplay checks pending:** native fitting
  removes Rigidbody while preserving Data/save identity. This now removes loose-item
  authority without broadcasting permanent disposal. Saved fitted parts are discovered
  through Data; removal rebinds their new body. Fitted state keeps publishing and
  answering targeted queries without an item-table entry. Item transforms, cargo and
  loose pose snapshots cannot move a fitted/transitioning part. Message 185 Installed
  means positive AssemblyID; the native bool is occupancy-query scratch data. Body
  replacement also marks publication dirty. 838 protocol/catalog and 18 launcher
  tests pass; installed action evidence verifies the alternator body lifecycle and
  all 30 replacement assembly-ID transitions. **Still open:** complete guest fitting,
  mount/bolt reconstruction, existing guest-save isolation and two-player verification.

- **v112 guest box opening implemented, native checks pending:** guests request
  openings using a session token, request sequence, stable box ID and observed
  revision. The host validates fresh proximity/ownership, current quantity and a
  ready one-output factory. A guard serializes native host/guest box openings before
  quantity or PartSpawnPoint changes. Retries cannot rerun a reserved operation;
  acceptance verifies the exact next native part ID and one quantity decrement.
  Box/part state (or retirement) is replayed with receipts. Guest interaction never
  calls its own contents factory; audio plays once on acceptance. An already-disposed
  box or output does not cause a completed creation to repeat. 822 protocol/catalog
  tests and 18 launcher tests pass, including contention on the last part, lost
  receipts, stale requests, sequence wrap and reconnect tokens. All 30 opening
  action/variable/target bindings match installed build 23268598. **Still open:**
  two-player opening/disposal/save tests, complete fitted-part/bolt reconstruction
  and the other spawner types below.

- **v111 loose replacement implementation, native checks pending:** all 30 box-content
  factories capture every native output, including saved loads and multi-product
  loops. ReplacementPartState (185) publishes fresh identity/pose, assembly status
  and native saved floats (wear, tightness and type-specific adjustments). Missing
  loose parts are created on guests with isolated persistence, normal item carrying
  and no native assembly authority. Existing saved parts are not replaced. A host
  installation hides/unregisters the loose copy; removal exposes it at the fresh
  host pose. Installed missing parts remain pending. Disposal preserves host save
  cleanup and waits for an accepting host echo on guests. Join/resync reads do not
  consume delta baselines. Static extraction verifies all 30 factory/ref/initialization
  chains; 797 protocol/catalog and 18 launcher tests pass. **Still open:** guest box
  opening, full installed-part/mount/bolt reconstruction and native two-player checks.

- **v110 native part identity implementation, native checks pending:** root bodies
  use native Data/ID with matching assembly/position save keys. Part, bolt and child
  control FSMs use that part identity plus a relative child path, independent of
  root rename, reparenting and peer scan order. Initialization waits for the native
  save identity; invalid/duplicate owners are rejected. Grocery spill cloning cannot
  capture or clone a native part graph. Generic FSM teardown now removes owned
  hooks and registration marks, allowing reconnect and retry without duplicate
  callbacks. Static evidence confirms the save-key pattern across 156 part prefabs;
  tests cover 4,500 body/Data/bolt IDs, native zero-counter IDs, shuffled packet
  routing and root rename/reparent. **Still open:** materializing missing part
  contents, guest opening, complete install/bolt/save replication and native checks.

- **v109 package creation/quantity implementation, native checks pending:** all 30
  native factories capture the exact New box and register it after initialization.
  Saved and newly created boxes share stable factory/Use.ID identities, quantities
  and exact cataloged prefabs. PackageState (184) supports late join, item resync,
  targeted replies and missing-replica repair; equal-revision refresh cannot change
  contents or resurrect retired boxes. Snapshot reads cannot swallow broadcasts to
  existing peers. Guests preserve local boxes separately and pause native factories;
  replica startup skips load and disables persistence. Host disposal retains native
  save cleanup; guest disposal removes the replica. Guest opening shows a local
  request to ask the host instead of spawning unshared parts. Automated tests cover
  identities, capacities, wire framing, stale states, replay, retirement and session
  reset. **Still open:** guest opening intents, exactly-once contents creation,
  persistent replacement-part identity/installation/bolts, and two-player/save tests.

- **v106 implementation, runtime verification pending:** the 15 trophy factories
  under Amateur/Junior/Icerace/RallyAMA/RallyJR now capture the exact native `New`
  output after creation/name assignment, then wait for item initialization. Stable
  factory + native item IDs distinguish awards with identical visible names. Native
  saved awards are also discovered and included in join/resync manifests. Guests
  create the exact factory prefab with persistence disabled, pause their factory
  after load, and hide/preserve/restore local saved trophies across disconnect.
  Per-factory binding failures are contained and logged. The installed factory and
  prefab action layouts were verified; protocol/catalog identity and replay tests
  pass. Follow `BUILDING.md`'s v106 checklist for the outstanding native checks.
  **Still open:** physical moose axe/vehicle collisions, native meat save/reload and natural cooking/spoilage acceptance (v224 output/state; v228 guest chopping implemented), parts packages (contents
  and opening), spray cans (paint/remaining contents), missing race outcome authority,
  and two-player/save-reload validation. The old claim that item-position snapshots
  alone created these missing items for late joiners was incorrect and is removed.

- **Package recon (v107):** 30 standard package factories reference a boxed prefab
  and a `CARPARTS/PARTSYSTEM/SPAWNERS_*` contents factory. `BrakeBiasRegulator` is a
  direct part, and `Plugwires` has null prefab/spawner references in this build.
  Standard `Use/Create Plug` decrements Quantity, sets global PartSpawnPoint and
  sends SPAWNITEM to its referenced part factory; Empty retains a native save-delete
  lifecycle. Part factories mint native IDs, assign installation references and
  initialize wear. These cannot safely use a display-name-only box clone. This trace
  exposed the now-fixed scalar corruption in R2.28; contents replication
  remains the next dependency, with actual prefab evidence in BUILDING.md.
- **Impact** MEDIUM (correctness of item spawning at large). The `Spawner` root (101 FSMs) is the item factory; `CreateMooseMeat` was unregistered in the initial audit and now has a dedicated v224 state/creation adapter; the remaining factory list still needs acceptance.
- **Model** Systematically diff every `Spawner/*` SPAWNITEM child against `ItemWorldSync.Spawn`'s registered-container list; register the missing ones (parts packages, bag contents, trophies, moose meat) so any host-minted spawn materializes on peers. Log any deliberately-excluded spawner.
- **Protocol** `ItemSpawn` factory flag in v106; `PackageState` 184 in v109; `SpawnIntent` remains for bags. **Touch** `ItemWorldSync.Spawn`, `.Factories`, `.PackageFactories`, `.PackageReplicas`, catalog `trophyFactories` / `partsPackages`. **Done when** every gameplay spawner is registered or explicitly excluded with a reason. **Watch** the spawn-sync design notes ([[spawn-sync-gap]] equivalent). **Deps** none. Unblocks 5.2, 7.1.

#### 7.3 · Crime / reaction NPCs  `TODO`
- **Impact** LOW. `SOCCER/Janitor "Reijo"` (anger accumulator, `CRIME/PLAYER/SHOOT`, can shoot) and `HUMANS/FighterPub/Fighter2` react to player/car crime; only pose (if streamed) syncs.
- **Model** Register these NPCs in `NpcTrafficSync` (pose) and drive their reaction state (anger/fight/shoot) host-authoritatively; crime they register feeds 4.1. **Protocol** reuse `NpcTransform` + a small reaction flag; +version if wire changes; PROTOCOL.md. **Touch** `NpcTrafficSync`. **Done when** Reijo/fighter react identically. **Watch** C, F. **Deps** 4.1 helps.

### Phase 8 — Player polish

#### 8.1 · PlayerAlco (BAC) reconnect persistence  `TODO`
- **Impact** LOW. The mod restores `DrunkCurrent` (visual) on rejoin but not the underlying persistent `PlayerAlco` BAC that drives DUI checkpoints + sobering.
- **Model** Append `PlayerAlco` to the needs report + guest profile (mirror the Dirtiness v55 add: wire field + `HasX` gate + sidecar column). **Protocol** extend `PlayerNeedsReport` (id 25) — +version; PROTOCOL.md + round-trip test. **Touch** `PlayerNeedsSync.cs`, `PlayerProfileMessages.cs`, `GuestProfileStore.cs`. **Done when** BAC survives rejoin. **Watch** the needs-report coupling class (don't block other needs on a late bind). **Deps** none.

#### 8.2 · Winter jacket / coverall worn state  `TODO`
- **Impact** LOW. `EQUIPMENTS/winter jacket(itemx)`/`winter coverall(itemx)` use a separate `ClothType` int from `ClothingSync`'s `ClothingStage/ClothingType`; the worn garment shows only on the wearer.
- **Model** Extend `PlayerClothingState` to carry the worn winter-garment (owner→host→peers) and render it on remote avatars. **Protocol** extend id 32; +version; PROTOCOL.md. **Touch** `ClothingSync.cs`, avatar. **Done when** a worn jacket shows on peers + affects the warmth tier. **Watch** E (per-player guard). **Deps** none.

#### 8.3 · Yard piss-stains  `TODO`
- **Impact** LOW. `YARD/PissAreas::Logic` scales persistent world-visible snow stains (`UTPissArea1-5`) on the PISS action; host-saved, shared-visible, unsynced.
- **Model** Host-owned world state: broadcast the stain level on change + join (small scalar state or an anyone-triggers event). **Protocol** small `PissAreaState` in `101–119` or a catalog event; +version if wire changes; PROTOCOL.md. **Touch** small sync or catalog. **Done when** stains agree. **Watch** B/C. **Deps** none.

#### 8.4 · In-car radio / CD  `TODO`  *(lowest; cosmetic-adjacent)*
- **Impact** LOW. In-car radio/CD power + channel/track selection unsynced (home stereo IS synced). Track *content* is cosmetic; power/channel is minor shared state.
- **Model** Mirror `HomeStereoSync` scalar-state model for the in-car radio power/channel (skip the track audio — cosmetic). **Protocol** reuse the home-stereo shape or a small `CarRadioState` in `63–79`; +version if wire changes; PROTOCOL.md. **Touch** `VehicleWorldSync` or a sibling. **Done when** radio power/channel agree. **Watch** A/E. **Deps** none.

---

## Appendix — verified out of scope (do not build)

These were audited and are correctly *not* gaps — don't spend tasks on them:

- **In-game computer** + its fishing minigame (`COMPUTER/*`, `Kaappis-Fishgame`) — MSC-save-import toy, explicit post-1.0 parity backlog.
- **Water wells / kitchen taps** (`WaterWell`, `KitchenWaterTap`, jail `wcbowl`) — infinite fixtures feeding the per-player **Thirst** need; divergence is tolerable by the needs model.
- **Map clock + weather forecast** — already host-synced (snow/temp/hour via `TimeWeatherSync`); only the cosmetic cloud-sprite selection is re-homed by hour.
- **Jukebox track / CD track / weather cloud sprite** — cosmetic-only presentation; no gameplay desync.
- **Food spoilage — reopened:** the live grocery audit on build 23268598 found milk's
  `Use::Spoil 2` and `In Fridge` reducing Condition at different rates. Authoritative
  milk condition and Bad-phase replication now pass local v218 checks. Other food
  still need acceptance. v223 verifies loose milk across both homes' power/door changes and fixes missed guest blackouts; individual fuses and complete fridge cooling-area reconstruction on join remain open. Spoilage is not an absent feature.
- **Host migration** — out of scope by design (host quit ends the session).

When the game updates (Early Access), re-dump the catalog (F9) and re-run the coverage
audit against the new build before trusting this list — MWC patches break FSM names/paths.

#### Shopping bag duplication regression (v120, shipped in 0.1.33)

Player report on v0.1.32: host and guest could hold separate bags; the host's spill
was invisible until the guest opened theirs, then both sets appeared. Fixed the
peer-local bag identities/independent inventories, scanner capture race, missing
native-name template lookup and discarded failed materialization. New bag states
and atomic opening requests use 190–192; old guest SpawnIntent 53 is retired. Exact
factory outputs are reserved before scanning and split into 32-item packets.
Native pickup guards release losing/removed copies. The 2026-09-09 local UDP test
with two real game processes exposed two further failures: the terminal contents
`Garbage` state never completed publication, and milk's numbered startup state
was mistaken for consumption. Both are corrected in development (still protocol
210). All 19 live checks passed across host/guest pickup and opening, partial and
last-item opening, three checkouts and nine matching groceries, including milk.
See BUILDING.md's live grocery section. Steam/two-PC, save/rejoin and mixed native
part acceptance remain open; this does not mark all shopping coverage complete.
