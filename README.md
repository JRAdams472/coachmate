# CoachMate

A chess coach for Windows that doesn't just grade your moves — it explains *why* they were wrong.

![status](https://img.shields.io/badge/status-v1%20milestone-orange)

## Features

- **Play against Stockfish** at four Elo-mapped levels (Beginner ~900 / Casual ~1400 / Club ~1900 / Master) — grading, hints, and the eval bar always run at full strength
- **Coach grading on every move** — `!`/`✓`/`?!`/`?`/`??` with plain-English explanations: hanging pieces, missed tactics, forks, pins, bad trades, king exposure, trapped pieces, mate threats, plus the engine's annotated refutation line
- **"Ask coach" AI narration** — on-demand prose explanations per note via a local [Ollama](https://ollama.com) instance (default `qwen2.5:7b` at `http://localhost:11434`); the model narrates computed engine facts, it can't invent lines
- **Cloud DB panel** — candidate moves with win rates from chessdb.cn's database of millions of games
- **Piece styles** — Classic (white outline / black filled), 3D (all filled), or Wireframe (all outline)
- Click-to-move **and** drag & drop, board coordinates, hint arrows, takebacks, play as White or Black, promotion picker, live eval bar

## Stack

- **UI**: WinUI 3 (unpackaged, self-contained single-file exe)
- **Backend**: C# / .NET 9
- **Rules**: [Rudzoft.ChessLib](https://github.com/rudzen/ChessLib) (MIT)
- **Engine**: [Stockfish](https://stockfishchess.org) (GPL-3.0) — bundled binary, driven over UCI
- **AI narration**: [Ollama](https://ollama.com) (optional, localhost)
- **Tests**: xUnit + coverlet, 80% floor / 90% target coverage on logic assemblies (models/tests exempt)

## Build & run

```powershell
dotnet restore
dotnet build -r win-x64
# single self-contained exe (~318MB) → dist-single/
dotnet publish src/CoachMate -c Release -r win-x64 -o dist-single `
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
# or the faster-starting multi-file folder (~238MB) → dist/
dotnet publish src/CoachMate -c Release -r win-x64 -o dist
```

`CoachMate.exe` runs standalone — no .NET install required. The bundled Stockfish binary extracts to `%LOCALAPPDATA%\CoachMate\engine\` on first launch.

> Requires Windows 10 1809+ (x64). The exe is unsigned — Windows SmartScreen may warn on first launch ("More info" → "Run anyway"), and some antivirus tools flag apps that self-extract bundled binaries; both are known false-positive patterns for sideloaded apps.

## AI narration (optional)

Point the app at any running Ollama instance:

```powershell
docker run -d -p 11434:11434 --name ollama ollama/ollama
docker exec -it ollama ollama pull qwen2.5:7b
```

Then set the endpoint/model in the sidebar and click **Ask coach** on any graded note. Everything else works with Ollama off — narration is purely additive.

## Tests

```powershell
dotnet test tests/CoachMate.Tests -r win-x64
```

## Roadmap

- **Puzzles** ([#11](https://github.com/JRAdams472/coachmate/issues/11), [#12](https://github.com/JRAdams472/coachmate/issues/12)) — bundled curated subset + optional full Lichess puzzle DB download, Play/Practice toggle
- **Estimative rating** ([#20](https://github.com/JRAdams472/coachmate/issues/20)) — Glicko-2 rating over rated games vs. the Elo-mapped engine levels

## License

**GPL-3.0** — this project bundles the Stockfish engine (GPL-3.0). Stockfish source: https://github.com/official-stockfish/Stockfish

Rudzoft.ChessLib is MIT-licensed. chessdb.cn data is used per its public API.
