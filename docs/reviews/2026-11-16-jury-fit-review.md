# Jury-fit review of Uncycled (2026-11-16)

A review of the whole repo against the organisers' rules and jury grid (`../HACKATHON.md`).
Opinion, not a decision: the team decides. Five days before the event the repo holds 19 design
documents and no code.

## 1. Verdict

The engineering thinking in the docs is good. The plan they describe cannot win as it stands,
for four reasons that have nothing to do with code quality:

1. **It conflicts with the rules.** The plan builds the game before the event and freezes
   features on the day. Art. 8 requires a project conceived during the hackathon and every
   pre-existing piece declared to the jury.
2. **It is far too big.** The MUST tier alone is weeks of work for three developers. There are
   about 23 build hours.
3. **It scores low on originality.** The design maps a well-known social-deduction game onto
   recycling, mechanic by mechanic. Originality is 20% and a Swedish games-industry founder is
   on the jury.
4. **It is built for a demo that will probably not happen.** It needs 4 to 8 jurors on their own
   devices for 6 to 10 minutes. The pitch slot is "adjusted to the number of teams".

The fix is not more features. It is a much smaller game with one original idea at its centre
(section 5), built honestly inside the 28 hours.

## 2. Scoring estimate

My judgment, not a measurement.

| Criterion | Weight | Full design, attempted in 28 h | Proposed 28-hour cut |
|---|---|---|---|
| Fun | 25% | Low. Fun in this genre comes after several rounds with friends; a juror gets one confused round, long meetings, and a blank screen once out | High if the game-feel pass happens: a clear verb in 10 seconds, a reveal every 40 seconds, no dead time |
| Technical | 20% | Low to medium. Ambitious, but the grid says functionality and stability first; a half-working authoritative server shows worse than a small solid one | Medium to high. Real-time multiplayer on phones by QR, bots, hidden roles, and it does not crash |
| Originality | 20% | Low. Recognisable in seconds | High. The deduction runs on recycling mistakes, which no known game does |
| Pitch | 15% | Unowned. No deck, no presenter, no script | High if owned from hour one |
| Theme | 10% | Medium. Recycling is the setting; knowing how to recycle does not help you win | High. Sorting knowledge is the skill, and contamination is a real recycling problem |
| Mystery | 10% | Near zero. "Feature freeze holds" leaves no room | Medium to high. A slot is reserved for it |

## 3. What is right (keep all of it)

- Hidden roles live only on the server and are never sent to a client that may not know them.
- Bots fill every empty seat and use the same commands as humans, so a round works with one human.
- One Godot project, Compatibility renderer, web export without threads, 640x360 landscape,
  touch first with 48 px targets.
- Tunables and content in JSON. This is also what makes a surprise challenge absorbable.
- AI never blocks the game and always has an offline fallback.
- Join by room code or QR with nothing to install.
- A debrief that shows what really happened plus one real recycling fact.
- The demo-day runbook: warm the server, spare phones, hotspot plan B.
- The polluter's "signature waste type". It is the most original idea in the design; the cut
  below is built on the same instinct.
- No violence: poison and vote-outs instead of kills suit an institutional jury.

## 4. What is wrong

### 4.1 Blockers

**A. Rules (Art. 8, Art. 12).** `PROGRESS.md` and the design doc plan P0 to P6 before the event
and "Event: bug fixes only". The schedule has an ideation hour and a mentor check that validates
the idea at 16:30 on Saturday; mentors will see how far along each team is. Separately,
`server/Sim` would be vendored from `reference`, which is pre-existing code and would have to
be declared, to a jury scoring originality, as a rules engine that came from studying another
studio's game. I could not inspect `reference` (it is not on this machine). If any of it
derives from decompiled code, Art. 5 and Art. 12 apply as well.
*Do:* ask the organisers in writing what preparation is allowed. Do not vendor `Sim`; the cut
needs about 300 lines of rules, written fresh. Start a new repo when the countdown starts and
show a declaration slide ("pre-existing: Godot, .NET, these CC0 sounds, this font, a design study
from 13 Nov; everything else was made here"). Declared openly, this builds trust. Hidden, it is
a disqualification risk.

**B. Scope.** MUST currently includes rooms, movement validation, roles, two task types, dumps,
poison, reports, meetings, votes, win order, bots with path-finding, reconnect and three exports,
on top of per-seat line-of-sight snapshot filtering. No toolchain is confirmed installed, the
host and the Godot version are still open decisions.

**C. Originality.** The design doc's own rule-mapping table lists majority/saboteur roles, kill, lights,
the two-console hold, emergency meetings and confirm-ejects, each with a recycling name.
Mechanics are not protected, but the jury scores "original thinking" and this reads as a reskin.

**D. Demo format.** A 60 s discussion plus 30 s vote is dead air on a stage. Jurors who are
ministry, telecom and embassy representatives will mostly watch a screen. Anything a juror
cannot understand in the first 30 seconds of watching does not score.

### 4.2 Design problems in the current rules

| Problem | Why it matters |
|---|---|
| **The deadline can be farmed.** The 10:00 deadline runs on `MatchTime`, which includes meetings; any active seat may report; reports are not capped; cooldowns reset after meetings | A polluter dumps, reports their own pile, sits through a 65-95 s meeting, repeats every 40 s of play. The match reaches 10:00 with roughly 3 minutes of real play and polluters win. Citizens are punished for reporting, the main thing citizens are meant to do. If the full design is ever built: run the deadline on `ActiveTime` |
| Out players get a "please stay quiet" screen with no map | A juror poisoned out at minute 2 watches nothing for the rest of the demo |
| Discussion is by voice, bots answer with quick-chat lines | Bots cannot hear humans. With 1-3 humans and 5-7 bots the social half of the game is reading bot phrases |
| Recycling knowledge never changes the outcome | Theme reads as decoration |
| Task bar hidden except at meetings | Citizens get almost no feedback that they are winning |
| No tutorial, no solo play, nothing works offline | A "mobile game" that needs a server, Wi-Fi and eight seats cannot be handed to a juror on its own |
| The AI feature is still undecided, and positioned as the stand-out | The grid has no AI criterion. Its originality line is wary of generative AI. Smart bots count as engineering; LLM-written text does not score |
| Windows export is a MUST | It scores nothing. The challenge says mobile |
| Colour is the only way to tell players apart | Colour-blind jurors; also unreadable on a projector |
| Nobody owns the pitch, sound, playtests or the submission | 15% of the grade has no owner |

### 4.3 Small errors in the docs

- `10-testing.md` says poison goes `0->25->...->Out`; the rule is 34% per hit.
- Position send rate: design doc 4.1 says about 15/s; `01` and `protocol.md` say 10 Hz.
- Design doc 4.5 lists `ready` and `task_complete`; `protocol.md` has neither (`task_step`), and
  puts the version check in `hello`, not `welcome`.
- Design doc section 5 puts match code in `Uncycled.Server/Match`; section 12 and `02` say `Sim/Match`.
- `PROGRESS.md` says "~6 min round"; the deadline is 10:00 and each meeting adds up to 95 s.
- Emergencies are "2 per match, shared" but the data model keeps `RemainingEmergencies` per player.

Not worth fixing if the scope changes, which I recommend.

## 5. Proposed 28-hour cut: "sort right or look guilty"

Same name, same heart (a hidden polluter among recyclers, multiplayer, bots, QR join, pollution
meter), a different engine.

**The idea.** In real recycling, one wrong item spoils a batch. So the polluter's weapon is
*contamination*, and the only evidence against them is *who put what in which bin*. An honest
player who does not know the sorting rules looks exactly like the saboteur. Players learn to
sort in order to prove they are innocent.

**One round (about 90 seconds):**

1. One street that fits on one screen. Everyone sees everyone (no fog). 4-5 bins, each with a
   colour and an icon.
2. Waste keeps appearing. Walk over an item to pick it up; it floats above your head where all
   can see it. Walk to a bin, tap Drop. Nobody is told whether it was right.
3. Every 40 seconds the truck arrives and opens the bins one by one. Clean bin: the meter drops.
   Contaminated bin: the wrong items are shown with one line on why ("grease ruins paper
   fibres") and the pollution meter jumps.
4. One hidden polluter (two at 7-8 players) wants the meter high. Their mis-sorts look like
   anyone's mistakes.
5. After the last truck, a 15-second vote: tap who you think it was. Catching the polluter
   removes their contamination from the meter; voting out an innocent adds to it.
6. One number decides: meter under the line, citizens win; over it, the polluter wins.
7. Debrief: who it was, a replay of their drops, each player's accuracy, and the rules *you*
   got wrong.

Bots are simple here: go to the nearest item, carry it to the right bin with a set error rate,
show a "👀" over their head when they see a wrong drop nearby. A polluter bot mis-sorts when
nobody is close. No path-finding is needed on an open street.

What this drops from the current decisions: poison and elimination, meetings and voice
discussion, vision and line of sight, sabotage consoles, task mini-games, reconnect, the Windows
build. The existing design docs become the "full game" roadmap slide for the incubation question.

### Scope tiers for the event

| Tier | Contents |
|---|---|
| MUST (playable by Sat 24:00) | Offline sorting practice (also the tutorial); join one room by QR; move; pick up and drop; truck reveal; meter; one hidden polluter; vote; result screen; citizen and polluter bots; Android APK and web build |
| SHOULD (Sun morning) | Game-feel and sound pass; debrief with personal mistakes; 20+ items including tricky ones; big-screen spectator view; EN and FR strings; the mystery challenge |
| COULD | Polluter "dump" and citizen "clean"; bin inspection; emotes; round modifiers; a second, Swedish-rules street |
| Not at the event | LLM features, camera scanner, accounts, extra maps, CI, anti-cheat, Windows build |

Fallback ladder if things slip: multiplayer with polluter, then multiplayer co-op sorting without
a polluter, then the offline practice mode with local bots. Build the offline practice mode
first so the bottom rung always exists. If two phones cannot see each other move through the
hosted server by Saturday 23:00, stop and ship the lower rung well.

## 6. Making it more fun (ordered by effect per hour)

1. **Game feel.** Squash and stretch on pick-up and drop, a sound per material (glass clinks,
   cans crunch), phone vibration, screen shake when the truck rejects a bin, confetti on a clean
   one. Two hours here is worth more than any feature.
2. **The truck reveal as the big beat.** Drum roll, bins open one at a time, the room reacts.
3. **Tricky items that create arguments.** Greasy pizza box, broken mirror, receipt, yoghurt pot,
   battery. Every item needs a sourced rule; a wrong fact in front of this jury costs theme points.
4. **Streaks** for correct sorts, and a "perfect bin" bonus.
5. **End-of-round awards.** Sharp Eye (voted right first), Clean Hands (100%), Wishcycler (most
   honest mistakes). Funny, and they teach.
6. **A big-screen view.** A laptop joins as a spectator showing the whole street, the meter and
   the QR. Roles are the only secret, so it leaks nothing. This is also the pitch demo.
7. **A demo mode.** Fixed seed, 60-second round, a bot polluter that mis-sorts in plain view at
   second 10, so the live demo always shows every beat.
8. **A place that is ours.** A Tunisian neighbourhood on collection day; the barbécha, the
   informal collector, as the guide character (about 10,000 of them recover some 200,000 tonnes
   a year, per [ANGED via Webdo](https://www.webdo.tn/fr/actualite/national/tunisie-10-000-barbechas-valorisent-200-000-tonnes-de-dechets-chaque-annee/218981/)).
   A second street with a Swedish deposit machine for bottles and cans (Sweden's return rate was
   88.4% in 2025, per [Packaging Europe](https://www.packagingeurope.com/over-3-billion-pet-bottles-and-aluminium-cans-returned-through-swedish-drs-in-2025.md))
   fits the event's theme.
9. **Readable for everyone.** Icon plus colour on every bin and player, large text, joystick side swap.

## 7. Technology for 28 hours

- Keep the split that matches the team: Godot client (gameplay dev), small .NET server (lead),
  bots on the server (AI dev). Shrink the server to rooms, a 15 Hz broadcast of the whole public
  state, role assignment, drop validation by distance, the truck timer, votes. No per-seat
  filtering is needed when only roles are secret.
- Keep `tuning.json` and an `items.json` (item, correct bin, why, source). Round modifiers as
  data are the cheapest way to host the mystery challenge.
- A handful of xunit tests on scoring, vote tally and the win line. "Code quality" is in the grid.
- Default language English with a French toggle: the grid is in English and at least one juror
  is Swedish. Confirm the pitch language with the organisers.
- Pin now: Godot version, host, one demo room with a fixed QR.
- Risk to accept knowingly: only the lead can fix the server at 4 am. Everyone should at least be
  able to run it locally.

## 8. Before Saturday (preparation that is clearly allowed)

1. Email the organisers the six questions in `../HACKATHON.md` section 5. Today.
2. Team decides: the cut or the full design; what is brought and how it is declared.
3. Every machine: pinned Godot plus export templates, .NET 10 SDK, Android export proven on a real
   phone, web export proven on Android Chrome and iPhone Safari. On the machine this review ran
   on, neither Godot nor a .NET SDK was found.
4. A host account with a hello `wss://` endpoint reachable from a phone on mobile data.
5. One timed practice jam (6-8 hours, a different theme) through the full pipeline: join, move,
   export APK and web, deploy. Throw the code away or keep it as a declared starter kit, whichever
   the organisers allow.
6. Designers: agree palette, proportions and sprite sizes, and rehearse the export pipeline. Final
   art is made at the event.
7. Content: the item and rule table with a source for each line; ten facts with sources.
8. Name owners: pitch and deck, sound, playtests, build and submission.
9. Kit: two hotspots on different operators, spare Android phones, cables, HDMI adapter, a way to
   mirror a phone to the projector, power strips.

## 9. The 28 hours

| When | Goal |
|---|---|
| Sat 14:30-15:30 | Note the mystery challenge and deliverables word for word |
| Sat 15:30-16:30 | Fit the mystery challenge into the modifier slot. One-page scope. Server hello deployed |
| Sat 16:30-17:00 | Mentor check: one-sentence idea, ask what the jury values |
| Sat 17:00-20:00 | Offline sorting toy on a phone (gameplay); two clients moving in one room (lead); bots walking (AI dev); player, bins, 8 items (designers) |
| Sat 21:00-24:00 | **Milestone: one complete ugly round on two phones plus bots.** First APK on a real phone tonight, not tomorrow |
| Sun 00:00-03:00 | Roles, vote, results, polluter bot, 20 items, first sounds |
| Sun 03:00-07:00 | Sleep in shifts, 3 hours each minimum. No new systems. Mystery challenge and polish only |
| Sun 07:00-08:30 | Three full playtests; list the top five problems; show mentors a playable build |
| Sun 08:30-11:30 | Fix the five. 10-second how-to-play card. Game-feel pass. Record the backup video at 11:00 |
| Sun 11:30-12:30 | Mentors and other teams play it cold. Record their sorting accuracy round by round |
| Sun 12:30-13:30 | Lunch. Deck finished |
| Sun 13:30-14:30 | Bug fixes only. Final APK and web build by 14:15 |
| Sun 14:30-15:30 | Deposit build, link, source, deck, video, declaration. Rehearse twice with a timer |

## 10. The pitch (15%, and the lens for every other score)

Follow the organisers' own outline. For a 5-minute slot:

1. **Problem, 20 s.** Tunisia recycles at most 10% of its waste ([Ministry of Environment via La Presse, July 2026](https://www.lapresse.tn/2026/07/12/seulement-10-des-dechets-sont-recycles-en-tunisie/)).
   Part of the problem is what goes in the bin: one wrong item spoils the batch.
2. **Concept, 30 s.** One sentence, then the twist: not knowing how to recycle makes you look guilty.
3. **Live demo, 90 s.** Demo mode on the big screen, two or three phones, one juror invited to
   scan the QR. Backup video ready on the same laptop.
4. **How it is built, 40 s.** One architecture picture. What was made here, what was not.
5. **Impact, 40 s.** Real numbers from Sunday's playtests ("accuracy went from X% to Y% in three
   rounds"). Who it is for: schools, municipalities, awareness campaigns.
6. **Mystery challenge, 15 s.** Say plainly where it is in the game.
7. **Close, 15 s.** What incubation would build next.

End on a slide that lists the six criteria with one proof each, so scoring is easy. Prepare
answers for: "how is this different from existing social-deduction games?", "what did you build
during the 28 hours?", "what does a player actually learn?", "how would this reach people?".

## 11. Decisions the team needs to make

1. Adopt the 28-hour cut, or keep the full design?
2. What do we bring on Saturday, and how do we declare it?
3. Is `reference/sim` entirely our own writing, or does any of it come from decompiled code?
4. Who owns the pitch?
5. Godot version, host, default language.
