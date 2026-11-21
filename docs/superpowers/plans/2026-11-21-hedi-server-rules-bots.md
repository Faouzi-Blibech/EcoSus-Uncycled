# Server rules and bots for Cleaners vs Trashers: implementation plan (Hedi)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the .NET server and bots play the v2 rules: room sizes, spots, bags, machine, upgrades, re-pollute,
machine jam, virus, gas, new wins, protocol 2.

**Architecture:** rules go into the engine-free `server/Sim` (new `Spots.cs`, `VirusRule.cs`, `MachineSystem`
in `Sabotage.cs`, new commands and events), wired through `Codec`/`Wire`/`Room` in `server/Uncycled.Server`.
Old systems are reused, not deleted (spec section 3). Bots in `bots/Uncycled.Bots` learn the new actions last.

**Tech Stack:** .NET 10, C# (Sim is netstandard2.1), xunit, WebSocket JSON.

**Spec:** `docs/design/2026-11-21-cleaners-vs-trashers.md` (read sections 3 to 12 first). Team plan:
`docs/team/README.md`.

## Global Constraints

- `Sim` stays engine-free: no ASP.NET, no real clocks (time comes in as `dt`), randomness only via `IRandom`.
- No rule checks "is this a bot".
- Every number comes from `content/tuning.json` through `ContentLoader` into `MatchConfig` (spec section 11).
- Code keeps `Citizen`/`Polluter` names; wire keeps `"citizen"`/`"polluter"` (spec section 2).
- Protocol: `docs/protocol.md` + `PROTOCOL_VERSION = 2` in `Codec`; the client bumps in the same merge.
- Branches `server/<short-name>`, PR into `main`, `main` runnable at every merge. Agents commit only when Hedi asks.
- Tests: table-driven xunit; run the narrowest filter while iterating: `dotnet test server --filter <Name>`.
  Full suites at the end of each task: `dotnet test server` and `dotnet test bots`.
- Out-of-play v1 actions (`hit`, `dump`, `clean_start`, `task_step`, fire) return `NotAllowed`. Their old tests
  get `[Fact(Skip = "v1 rule, out of play in v2")]` (or `[Theory(Skip = ...)]`); list them in the PR.

## Review Focus

1. Rejoin mid-match: `welcome.state` must carry the new `me` fields (bags, items, infected countdown, ability), or
   a reconnecting player loses them. Test in Task 3 and Task 6 (`WelcomeStateTests`).
2. A meeting starting during a clean session, gas warning or virus countdown: session dropped, gas and countdown
   frozen (they run on active time). Tests in Task 3, 6, 7.
3. Two Cleaners sending `spot_done` for the same spot in one tick: first gets the bag, second gets `spot_clean`
   and nothing. Test in Task 3.
4. Hidden info: a planted virus never appears in any seat's snapshot; Trasher fields never reach Cleaners;
   `infected_s` only reaches the victim. Tests in Task 5 and 6 (`SnapshotLeakTests`).
5. An infected Cleaner voted out, or the planting Trasher voted out: countdown cleared for the first; the planted
   virus stays deadly for the second. Test in Task 6.

---

### Task 1: protocol.md v2 (30 min, do first, merge at once)

**Files:**
- Modify: `docs/protocol.md`

- [ ] **Step 1:** Copy spec section 12 into `docs/protocol.md`: new rows in sections 2-6, snapshot fields in 4.1,
  reasons in 7, header `PROTOCOL_VERSION = 2` with a one-line changelog. Seat ids become 0-9.
- [ ] **Step 2:** Open a PR with only this file so Faouzi and Louay code against it. Do not bump the constant in
  code yet (Task 2 does, together with the client).

### Task 2: room size and Trasher count (45 min)

**Files:**
- Modify: `content/tuning.json` (add `rooms`), `server/Uncycled.Server/Content/ContentLoader.cs`,
  `server/Sim/Match/MatchConfig.cs`, `server/Uncycled.Server/Rooms/Room.cs` (lobby `set_size`, `full` check,
  `HandleStart`, dev switch), `server/Sim/Uncycled/RoleAssigner.cs` (optional forced seat),
  `server/Uncycled.Server/Rooms/Wire.cs` (`Lobby`, `Welcome`), `server/Uncycled.Server/Net/Codec.cs`
  (`PROTOCOL_VERSION = 2`)
- Test: `server/Uncycled.Server.Tests/RoomSizeTests.cs` (new), `TuningTests.cs`, `server/Sim.Tests/RulesTests.cs`

**Interfaces:**
- Produces: `MatchConfig.RoomSizes : int[]` (`{6, 8, 10}`), `MatchConfig.DefaultSize : int` (8),
  `MatchConfig.TrashersFor(int size) : int`; the room allocates `RoomSizes.Max()` = 10 seats; `Room.Size : int`.
- Wire: `set_size {size}` in; `lobby`/`welcome` gain `size`, `sizes`.
- Dev switch (testing only, off unless set): env `UNCYCLED_DEV_HOST_ROLE=polluter|citizen` gives the host that
  role at match start (the server reads it, passes the forced seat to `RoleAssigner.Assign` as an optional
  `byte? forcedPolluter` / `byte? forcedCitizen`). Faouzi and Louay need it to test Trasher and Cleaner screens.

- [ ] **Step 1: Write the failing tests** in `RoomSizeTests.cs` with `RoomRig`:
  - `SetSize_OnlyHostInLobby`: non-host `set_size` changes nothing.
  - `SetSize_BelowHumans_Rejected`: 7 humans, `set_size 6` -> `target_invalid`, size stays 8.
  - `Join_FullAtSize`: size 6 with 6 humans, a 7th `join` -> `join_failed {reason: "full"}`.
  - `Start_FillsBotsToSize` theory `(6,1) (8,2) (10,2)`: after `start`, active seats == size and polluters == n.
  - `RoleAssigner_ForcedPolluter_IsPolluter` (Sim test, 20 seeds): the forced seat is always a polluter and
    the polluter count is unchanged.
- [ ] **Step 2:** Run `dotnet test server --filter RoomSizeTests`. Expected: FAIL (no `set_size`).
- [ ] **Step 3:** Implement: parse `rooms` in `ContentLoader`; `Room` keeps `Size` (default 8), handles
  `set_size` in lobby, rejects `join` at `Size` humans, fills bots up to `Size` and passes `Seats = Size`,
  `Polluters = TrashersFor(Size)` to the engine at start. `Wire.Lobby`/`Welcome` write `size` and `sizes`.
  Bump `PROTOCOL_VERSION` to 2.
- [ ] **Step 4:** Run `dotnet test server`. Expected: PASS (fix `TuningTests` for the new keys).
- [ ] **Step 5:** Commit on `server/room-size`; merge together with Faouzi's Task F1 (both sides on protocol 2).

### Task 3: spots, bags, cleaning (1 h 30)

**Files:**
- Create: `server/Sim/Uncycled/Spots.cs`
- Modify: `server/Sim/Core/Enums.cs`, `server/Sim/Core/PlayerState.cs`, `server/Sim/Match/Commands.cs`,
  `server/Sim/Match/Events.cs`, `server/Sim/Match/MatchEngine.cs` (build the board, meter starts 0),
  `server/Sim/Match/MatchEngine.Actions.cs` (`ApplyUse` spot path, `ApplySpotDone`, v1 actions -> `NotAllowed`),
  `server/Sim/Match/SeatView.cs` + `MatchEngine.View.cs` (spots, bags), `MatchConfig.cs`, `ContentLoader.cs`
  (`spots`, `bags`), `content/tuning.json` (`spots`, `bags`, `tasks` all 0, `meter.passive_per_s` 0,
  `match.deadline_s` 420), `Codec.cs` (`spot_done`), `Wire.cs` (`use_ok` kind `spot`, `spot_cleaned`, snapshot
  `spots` + `me.bags/cap`, welcome state)
- Test: `server/Sim.Tests/SpotTests.cs` (new), `server/Uncycled.Server.Tests/WelcomeStateTests.cs`

**Interfaces:**
- Produces:
  - `enum SpotState { Dirty, Clean }`
  - `sealed class Spot { string Id; Vec2 Position; float Range; string TaskType; float MinSeconds; SpotState State; byte? VirusBy; float VirusPlantedAt; }`
  - `sealed class SpotBoard { static SpotBoard FromMap(MapData map, IReadOnlyList<TaskTypeDef> types); IReadOnlyList<Spot> All; Spot? Find(string id); }`
    (one spot per `ConsoleKind.Task` console; `TaskType`/`MinSeconds` from the task type that lists the console)
  - `MatchEngine.Spots : SpotBoard`
  - `PlayerState.Bags : int`, `BagCap : int`, `HasTool : bool`
  - `SpotDoneCommand(string spotId)`; `SpotCleanedEvent(byte seat, string spot, int bags)`
  - `RejectReason.BagsFull, SpotClean, SpotDirty` -> wire `bags_full`, `spot_clean`, `spot_dirty`
  - `MatchConfig.BagCap = 1`, `BackpackCap = 3`, `ToolTimeMult = 0.5f`, `BagMeterAdd = 10f`, `RepolluteMeterSub = 10f`

- [ ] **Step 1: Write the failing tests** in `SpotTests.cs` (test map spots are `bins`, `compost`, `tap`):

```csharp
[Fact]
public void SpotDone_GivesBag_AndCleansSpot()
{
    var e = TestWorld.Playing();
    byte c = TestWorld.Citizen(e);
    TestWorld.StandAt(e, c, "bins");
    Assert.True(e.Apply(c, new UseCommand("bins")).IsOk);
    TestWorld.Advance(e, 4.1f);
    Assert.True(e.Apply(c, new SpotDoneCommand("bins")).IsOk);
    Assert.Equal(1, TestWorld.P(e, c).Bags);
    Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
}
```

  Plus, same style:
  - `AllSpotsStartDirty_MeterStartsZero`.
  - `SpotDone_BeforeMinSeconds_OnCooldown`; with `HasTool`, `MinSeconds * 0.5` is enough.
  - `Use_WithBagsFull_BagsFull`; `Use_CleanSpot_SpotClean`; `SpotDone_WithoutUse_NotAllowed`.
  - `Polluter_Use_Spot_NotAllowed` (Trashers fake clean on the client only).
  - `TwoCleaners_SameSpotSameTick_SecondGetsSpotClean` (Review Focus 3).
  - `Meeting_DropsCleanSession` (Review Focus 2).
  - `Hit_And_Dump_NotAllowedInV2`.
  - Add `TestWorld.StandAt(MatchEngine e, byte seat, string consoleId)` (moves the seat onto the console).
- [ ] **Step 2:** Run `dotnet test server --filter SpotTests`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the types above and the rules in spec section 5 ("Spots", "Cleaning", "Bags").
  Reuse `TaskSession` for the open session. Skip obsolete v1 tests as stated in Global Constraints.
- [ ] **Step 4:** Wire it: `Codec` parses `spot_done {spot}`; `Wire` writes `use_ok` kind `"spot"` with `type`,
  `spot_cleaned`, snapshot `spots` (every spot, every seat) and `me.bags`, `me.cap`, `me.tool` (Cleaners only),
  and the same `me` fields in `welcome.state`. Add `WelcomeStateTests.Rejoin_KeepsBags` (Review Focus 1).
- [ ] **Step 5:** Run `dotnet test server`. Expected: PASS. Commit on `server/spots`.

### Task 4: machine, upgrades, meter, win (1 h)

**Files:**
- Modify: `Enums.cs` (`ConsoleKind.Machine`, `UpgradeKind`, `GameOverReason.MeterFull`, `RejectReason.NoBags,
  MachineJammed, NoItem`), `PlayerState.cs` (`Heals`, `Masks`, `UpgradeOfferOpen`), `Commands.cs`, `Events.cs`,
  `MatchEngine.Actions.cs`, `server/Sim/Uncycled/WinEvaluator.cs`, `ContentLoader.cs` (special kind `machine`),
  `Codec.cs` (`pick_upgrade`), `Wire.cs` (`use_ok` kind `machine`, `delivered`, `upgrade_offer`, `upgrade_ok`,
  `game_over` reason `meter_full`, snapshot `me.heals/masks`)
- Test: `server/Sim.Tests/MachineTests.cs` (new), `server/Sim.Tests/RulesTests.cs` (win table)

**Interfaces:**
- Produces: `enum UpgradeKind { Backpack, Tool, Heal, Mask }`; `PickUpgradeCommand(UpgradeKind item)`;
  `DeliveredEvent(byte seat, int bags, float meter)`; `UpgradeOfferEvent(byte seat, IReadOnlyList<UpgradeKind> items)`;
  `UpgradeOkEvent(byte seat, UpgradeKind item)`;
  `WinEvaluator.Evaluate(int activePolluters, int activeCitizens, float meter, bool deadlineReached) : WinResult?`
  (order: polluters 0 -> `PollutersVotedOut`; meter >= 100 -> Citizens `MeterFull`; deadline; parity).
- The test map needs a machine: add `new ConsoleDef("machine", ConsoleKind.Machine, MachinePos, 1.2f)` to
  `TestWorld.Map()`.

- [ ] **Step 1: Write the failing tests** in `MachineTests.cs`:
  - `Deliver_AddsBagMeterAddPerBag`: 2 bags (cap 3) -> meter 20, bags 0, `UpgradeOfferEvent` with 4 items.
  - `Deliver_NoBags_NoBags`.
  - `Offer_OmitsOwnedBackpackAndTool`.
  - `PickUpgrade_Backpack_CapThree`; `PickUpgrade_Heal_MaxOne`; `PickUpgrade_WithoutOffer_NotAllowed`.
  - `SecondDeliveryWhileOfferOpen_AddsMeter_NoSecondOffer`.
  - In `RulesTests`, a theory over `WinEvaluator.Evaluate`: `(0,4,50,false) -> Citizens/PollutersVotedOut`,
    `(1,4,100,false) -> Citizens/MeterFull`, `(1,4,99,true) -> Polluters/Deadline`, `(2,2,50,false) -> Polluters/Parity`,
    `(1,4,99,false) -> null`.
- [ ] **Step 2:** Run `dotnet test server --filter "MachineTests|RulesTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement spec section 5 ("Recycling Machine", "Upgrades") and section 7. `use` on the machine
  console delivers. Drop `TasksDone` and `PollutionFull` from evaluation.
- [ ] **Step 4:** Wire the messages listed above. Run `dotnet test server`. Expected: PASS. Commit on `server/machine`.

### Task 5: re-pollute, unlock, machine jam (1 h 15)

**Files:**
- Modify: `Enums.cs` (`CueKind.Repollute`, `SabotageKind.Machine`, `TrasherAbility { None, Virus, Gas }`,
  `RejectReason.NotUnlocked`), `PlayerState.cs` (`Repollutes`, `Ability`, `AbilityChoiceOpen`,
  `RepolluteReadyAt`), `Commands.cs`, `Events.cs`, `server/Sim/Uncycled/Sabotage.cs` (`MachineSystem`),
  `MatchEngine.Actions.cs`, `MatchConfig.cs` + `ContentLoader.cs` (`trasher`, `machine`), `Codec.cs`
  (`repollute`, `pick_ability`, `sabotage` kind `machine`, `fix` console `machine`), `Wire.cs`
- Test: `server/Sim.Tests/TrasherTests.cs` (new), `server/Uncycled.Server.Tests/SnapshotLeakTests.cs`

**Interfaces:**
- Produces: `RepolluteCommand(string spotId)`; `PickAbilityCommand(TrasherAbility ability)`;
  `RepolluteOkEvent(byte seat, float cooldown, int count)`; `AbilityUnlockedEvent(byte seat)`;
  `AbilityOkEvent(byte seat, TrasherAbility ability)`;
  `MachineSystem { bool Jammed; byte? RepairSeat; float RepairElapsed; }` (same shape as `SmogSystem`);
  config `RepolluteCooldownS = 20`, `UnlockAfter = 3`, `TrasherStartCooldownS = 10`, `MachineFixS = 4`.

- [ ] **Step 1: Write the failing tests** in `TrasherTests.cs`:
  - `Repollute_CleanSpot_DirtyAndMeterDown`: meter 30 -> 20, spot `Dirty`, witness in sight gets
    `CueEvent` kind `Repollute`, a seat behind a wall does not.
  - `Repollute_DirtySpot_SpotDirty`; `Repollute_OnCooldown`; `Citizen_Repollute_NotAllowed`.
  - `ThirdRepollute_SendsAbilityUnlocked`; `PickAbility_Twice_NotAllowed`; `PickAbility_BeforeUnlock_NotUnlocked`.
  - `SabotageMachine_DeliveryRejected_MachineJammed`; `FixMachine_HoldFourSeconds_Unjams`;
    `FixMachine_LeaveRange_RepairDropped`.
  - `SnapshotLeakTests`: a Cleaner's snapshot has no `repollutes`, `ability`, `cd.repollute`.
- [ ] **Step 2:** Run `dotnet test server --filter TrasherTests`. Expected: FAIL.
- [ ] **Step 3:** Implement spec section 6 ("Re-pollute", "Sabotage", "Unlock"). Reuse `DumpReadyAt` timing
  pattern and the dump `cue` path; reuse `SabotageCooldown` shared with smog. Remove fire: delete the
  `FireA`/`FireB` markers from `client/world/maps/greybox_01.tscn`, re-export the map
  (`godot --headless --path client --script res://tools/export_map.gd -- --map greybox_01`), then
  `FireSystem.Available` is false. The F0 map kept them because 12 bot tests walk to the fire consoles
  (`FixRoundTests`, `ActionTests.Executor_FireHoldReleasedWhenLeaving`, `PolluterTacticsTests.Fire_*`,
  `ObservationTests`, `MaskMutationTests`, `NavigatorTests` console count 16 -> 14, `PolluterUnchangedTests`
  golden): skip or update them in the same commit. `ConsoleKind.Machine` and the loader's `"machine"` kind
  already exist (F0).
- [ ] **Step 4:** Wire it (snapshot `machine`, Trasher `me` fields). Run `dotnet test server`. Expected: PASS.
  Commit on `server/trasher-kit`.

### Task 6: virus, heal, infected meeting (1 h 15)

**Files:**
- Create: `server/Sim/Uncycled/VirusRule.cs`
- Modify: `Enums.cs` (`OutCause.Virus`, `TimelineKind.Infected`), `PlayerState.cs` (`InfectedDeathAt : float?`,
  `InfectedMeetingUsed`, `VirusReadyAt`), `Commands.cs`, `Events.cs`, `MatchEngine.cs` (tick the countdowns on
  active time), `MatchEngine.Actions.cs` (`ApplyPlantVirus`, `ApplyHeal`, `spot_done` triggers the virus,
  `call_meeting` from anywhere when infected), `MatchEngine.Meeting.cs` (ejected infected -> clear), `ContentLoader.cs`
  (`virus`), `Codec.cs` (`plant_virus`, `heal`), `Wire.cs` (`infected`, `healed`, `virus_planted`, `you_are_out`
  cause `virus`, snapshot `me.infected_s`, welcome state)
- Test: `server/Sim.Tests/VirusTests.cs` (new), `SnapshotLeakTests.cs`, `WelcomeStateTests.cs`

**Interfaces:**
- Produces: `static float VirusRule.Countdown(float age, float startS, float fatalS, float growS)`;
  `PlantVirusCommand(string spotId)`; `HealCommand(byte target)`; `InfectedEvent(byte seat, float countdown)`;
  `HealedEvent(byte seat, byte by)`; `VirusPlantedEvent(byte seat, string spot)`.

- [ ] **Step 1: Write the failing tests** in `VirusTests.cs`:

```csharp
[Theory]
[InlineData(0f, 60f)]
[InlineData(45f, 35f)]
[InlineData(90f, 10f)]
[InlineData(300f, 10f)]
public void Countdown_GetsDeadlierWithAge(float age, float expected) =>
    Assert.Equal(expected, VirusRule.Countdown(age, 60f, 10f, 90f), 3);
```

  Plus:
  - `PlantVirus_WithoutVirusAbility_NotUnlocked`; `PlantVirus_CleanSpot_SpotClean`; `PlantVirus_SecondWhilePlanted_OnCooldown`.
  - `CleaningPlantedSpot_Infects_WithAgedCountdown` (plant, advance 45 s, clean -> `InfectedEvent` countdown 35 +- 0.1).
  - `Infected_ReachesZero_OutWithBodyPile` (cause `Virus`, a `Body` pile at the position).
  - `Countdown_FrozenDuringMeeting` (Review Focus 2).
  - `Heal_Self_Cures_UsesHeal`; `Heal_OutOfRange_OutOfRange`; `Heal_NotInfected_TargetInvalid`.
  - `Infected_CallMeetingAnywhere_OnceOnly`.
  - `InfectedVotedOut_CountdownCleared`; `PlanterVotedOut_VirusStaysDeadly` (Review Focus 5).
  - `SnapshotLeakTests`: no seat's snapshot contains the virus spot; only the victim gets `me.infected_s`.
  - `WelcomeStateTests.Rejoin_KeepsInfectedCountdown`.
- [ ] **Step 2:** Run `dotnet test server --filter VirusTests`. Expected: FAIL.
- [ ] **Step 3:** Implement spec section 6 ("Virus"). The old `ApplyHit`/`PoisonRule` stay unused.
- [ ] **Step 4:** Wire it. Run `dotnet test server`. Expected: PASS. Commit on `server/virus`.

### Task 7: toxic gas (45 min) - first cut if behind at Sun 09:00

**Files:**
- Modify: `server/Sim/World/MapData.cs` (`GasRooms : IReadOnlyList<ZoneDef>`), `ContentLoader.cs` (map
  `gas_rooms`), `Enums.cs` (`OutCause.Gas`), `PlayerState.cs` (`GasReadyAt`), `Commands.cs`, `Events.cs`,
  `MatchEngine.cs` (pending gas resolves on active time), `MatchEngine.Actions.cs`, `Codec.cs` (`gas`), `Wire.cs`
  (`gas_warning`, `gas_done`, `mask_used`, snapshot `gas`)
- Test: `server/Sim.Tests/GasTests.cs` (new); add two gas rooms (`gas_w`, `gas_e`) to `TestWorld.Map()`

**Interfaces:**
- Produces: `GasCommand(string roomId)`; `GasWarningEvent(string room, float inS)`; `GasDoneEvent(string room)`;
  `MaskUsedEvent(byte seat)`; config `GasWarningS = 3`, `GasCooldownS = 90`.

- [ ] **Step 1: Write the failing tests** in `GasTests.cs`:
  - `Gas_AfterWarning_UnmaskedCitizensInRoomOut` (cause `Gas`, body piles), citizens outside the rect untouched.
  - `Gas_MaskedCitizen_Survives_MaskUsed`; `Gas_PollutersImmune`.
  - `Gas_WithoutAbility_NotUnlocked`; `Gas_UnknownRoom_TargetInvalid`; `Gas_OnCooldown`.
  - `Gas_WarningFrozenDuringMeeting`.
- [ ] **Step 2:** Run `dotnet test server --filter GasTests`. Expected: FAIL.
- [ ] **Step 3:** Implement spec section 6 ("Toxic Gas").
- [ ] **Step 4:** Wire it. Run `dotnet test server`. Expected: PASS. Commit on `server/gas`.

### Task 8: bots play v2 (2 h)

**Files:**
- Modify: `bots/Uncycled.Bots/BotAction.cs` (append new actions at the **end**: the neural brain depends on the
  existing order), `ActionExecutor.cs`, `ActionQuery.cs`, `ObservationBuilder.cs`, `ScriptedCitizen.cs`,
  `ScriptedPolluter.cs`, `PolluterTactics.cs`, `server/Uncycled.Server/Bots/BotBrains.cs`
- Test: `bots/Uncycled.Bots.Tests/V2PlayTests.cs` (new), using `TestMatch`/`BotDriver`

**Interfaces:**
- Consumes: the commands of Tasks 3-7 and the new `SeatView` fields.
- Produces: `BotAction.GoDirtySpot, FinishSpot, GoMachine, Deliver, PickUpgrade, Repollute, PickAbility,
  PlantVirus, GasRoom, HealSelf, FixMachine` (appended).

- [ ] **Step 1: Write the failing tests** in `V2PlayTests.cs`:
  - `ScriptedCitizens_RaiseMeter`: 8 scripted bots, 180 s simulated, meter > 0 and at least one delivery.
  - `ScriptedPolluter_Repollutes_WhenUnwatched`: a Trasher bot re-pollutes at least once in 180 s.
  - `ScriptedPolluter_UsesUnlockedAbility`: after 3 re-pollutes it picks an ability and uses it once.
  - `InfectedCitizenBot_HealsSelf_WhenHoldingHeal`.
- [ ] **Step 2:** Run `dotnet test bots --filter V2PlayTests`. Expected: FAIL.
- [ ] **Step 3:** Cleaner bot: nearest dirty spot -> `use` -> wait `MinSeconds` -> `spot_done`; full -> machine
  -> `use` -> random offered upgrade; heal self when infected; fix a jammed machine when within 6 tiles.
  Trasher bot: walk spot to spot (fake clean = stand there `MinSeconds`), re-pollute clean spots when no Cleaner
  sees it (existing witness logic in `PolluterTactics`), random ability, plant on dirty spots, gas a room with a
  Cleaner in sight. Hit/dump actions stay in the enum but are never chosen.
- [ ] **Step 4:** Run `dotnet test bots` and `dotnet test server`. Expected: PASS. Commit on `server/bots-v2`.

---

## Done means

- `dotnet test server` and `dotnet test bots` pass.
- A local server (`dotnet run --project server/Uncycled.Server`) plus the Godot client: a 6-player room with 5 bots
  plays a full match to a `game_over` with the new reasons.
