# Uncycled: project brief (paste this into any chatbot)

This one file is the context for chat assistants (Claude.ai, ChatGPT, Gemini...) that cannot
open the repo. Coding agents read `AGENTS.md` instead.

## The event
Green Hackathon, Swedish-Tunisian Games and Innovation Days, El Ghazala, Tunisia,
**Nov 21-22, 2026**. Theme: recycling and waste management. Deliverable: a playable game
prototype and a pitch.

## The team
"Uncycled", 5 people: 3 developers (server and integration, gameplay, AI features; no lead, the team decides together) and 2 designers
(all art, animation, UI visuals; no code).

## The game
An original multiplayer social-deduction game about recycling.

- Players are **citizens** in a neighbourhood. They walk around and complete short eco
  mini-games (sorting waste, fixing a compost bin...). Finishing all tasks wins.
- 8 seats per round; **2 are secretly polluters**. They fake tasks, **dump waste** (raises the
  shared pollution meter and leaves a pile with clues: zone, time, waste type; anyone who sees
  it becomes a witness), **poison** citizens (each hit adds 34%, so 3 hits; at 100% the citizen becomes
  pollution and is out) and **sabotage** (smog that shrinks vision, a landfill fire that two
  players must put out).
- Anyone can report a pile or press the emergency button to call a **meeting**: 60 s of talking
  out loud, then 30 s to vote. The most-voted player is kicked out.
- Citizens win by finishing tasks or voting out both polluters. Polluters win when the pollution
  meter fills, when they equal the citizens still in, or when the 10-minute match deadline hits.
- Citizens can clean dump piles to lower the meter. Each polluter has a secret "signature" waste
  type, so matching piles point to the same polluter.
- AI bots speak in meetings with short phrases based on what they saw; polluter bots lie.
- After the round, a **debrief** shows what the polluter did and one real recycling fact.
- Empty seats are filled by **AI players**, so 1 to 8 humans can play.

The judges play **together in one round** from their own phones and laptops, joining by room
code or QR.

## Platforms
One Godot project exported three ways: **web game** (browser, phone or PC), **Windows app**,
**Android app**.

## Tech
- Client: Godot 4 (GDScript). Server: ASP.NET Core on .NET 10 (LTS) with WebSockets and JSON messages.
- The server owns the rules. They come from our own C# rules library, built during an earlier
  reverse-engineering study of how social-deduction games work (rules and pacing only; no
  names, art, maps or text from any existing game).
- AI runs only on the server, always with an offline fallback, never blocking the game.

## AI features
In progress: a computer-vision waste scanner (photo -> material and the right bin), built by the AI
developer. Other candidates: smart AI bots, AI-voiced bot answers in meetings, an AI eco-coach debrief.

## Phases
P0 platforms talk to a hosted server -> P1 rooms and movement -> P2 full round with bots ->
P3 recycling mechanics (dumps, pollution, clues) -> P4 AI feature -> P5 art and polish ->
P6 event ready (hosted builds, Wi-Fi fallback, demo script).

## Not decided yet
Stretch AI feature, map setting, default language (FR/EN), hosting. The team builds the full design as a complete game (a smaller 28-hour cut was considered and dropped). Godot 4.7.2. See `PROGRESS.md` in the repo for the latest.
