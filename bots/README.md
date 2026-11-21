# Uncycled bots

Bots for Uncycled: a scripted floor and (later) trained networks, all on top of `server/Sim`
(referenced read-only). Bots read only `engine.ViewFor(seat)`, `engine.Witnesses.SeenBy(seat)` and
their own memory, and send the same commands as humans.

| Folder | What |
|---|---|
| `Uncycled.Bots/` | netstandard2.1 library: memory, navigation, action catalogue and masks, executor, observation builder, suspicion model, scripted brains |
| `Uncycled.Bots.Tests/` | xunit tests, run on real matches built from `content/` |
| `bots.json` | bot settings (brain, decision interval, reaction delay, skill) |
| `train/` | Python trainer (separate owner) |

## Build and test

.NET runs only in Docker. From the repo root (Git Bash), with `<WT>` the absolute repo path:

```bash
MSYS_NO_PATHCONV=1 docker run --rm --cpus 4 -v "<WT>:/repo:ro" -v uncycled-nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c \
  "cp -r /repo /work && cd /work/bots && dotnet test Uncycled.Bots.sln 2>&1 | grep -E 'Passed!|Failed!|Failed |error '"
```

Training: see `train/` (added with the training host).

## Observation and actions

`ObservationBuilder.SchemaHash` fingerprints the observation fields, their count, the `BotAction`
names and the `SeatView` shape. A trained model stores it; a model with another hash is refused.
`BotAction` is append-only once a model is trained.

## Training host

`Uncycled.Train` runs many matches and talks to the trainer over stdin/stdout (binary, little-endian;
layout in `Uncycled.Train/PipeProtocol.cs`; logs go to stderr). Publish (same container as the tests,
add `-v "<WT>/bots/.build:/out"`, body `cp -r /repo /work && cd /work && dotnet publish bots/Uncycled.Train -c Release -r win-x64 --self-contained -o /out/train-win`), then from `bots/`:

```bash
.build/train-win/Uncycled.Train.exe --bench 60 --content ../content          # decisions per second on stderr
.build/train-win/Uncycled.Train.exe --eval --content ../content --seeds 1-20  # one JSON line per match on stdout
```

## Bots on the server

The server reads `bots.json` and `models/` from `UNCYCLED_BOTS_DIR` (the image: `/app/bots`). The image
ships `"brain": "scripted"`. To play the trained networks, put `citizen.onnx`/`citizen.json` and
`polluter.onnx`/`polluter.json` in `models/` (rebuild the image, or mount a folder over `/app/bots/models`)
and set `UNCYCLED_BOTS_BRAIN=neural`, which overrides the file's `brain` without a rebuild. The models are
loaded once when the server starts; a role whose model is missing, corrupt or of another schema plays the
scripted brain, and the reason is logged once at startup.
