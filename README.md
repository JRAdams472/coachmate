# CoachMate

A chess coach for Windows that doesn't just grade your moves — it explains *why* they were wrong.

![status](https://img.shields.io/badge/status-in%20development-orange)

## Features

- **Play against Stockfish** at four strength levels (Master / Club / Casual / Beginner) — grading and hints always run at full strength
- **Coach grading on every move** — `!`/`✓`/`?!`/`?`/`??` with plain-English explanations: hanging pieces, missed tactics, forks, pins, bad trades, mate threats, and the engine's refutation line
- **"Ask coach" AI narration** — optional on-demand prose explanations via a local [Ollama](https://ollama.com) instance (default `qwen2.5:7b` at `http://localhost:11434`)
- **Cloud DB panel** — candidate moves with win rates from chessdb.cn's database of millions of games
- Click-to-move **and** drag & drop, board coordinates, hint arrows, takebacks, play as White or Black, promotion picker, live eval bar

## Stack

- **UI**: WinUI 3 (unpackaged, self-contained single-file exe)
- **Backend**: C# / .NET 9
- **Rules**: [Rudzoft.ChessLib](https://github.com/rudzen/ChessLib) (MIT)
- **Engine**: [Stockfish](https://stockfishchess.org) (GPL-3.0) — bundled binary, driven over UCI
- **Tests**: xUnit + coverlet, 80% floor / 90% target coverage on logic assemblies

## Build & run

```powershell
dotnet restore
dotnet build -r win-x64
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None -p:IncludeNativeLibrariesForSelfExtract=true
```

`CoachMate.exe` runs standalone — no .NET install required. The bundled Stockfish binary extracts to `%LOCALAPPDATA%\CoachMate\engine\` on first launch.

> Requires Windows 10 1809+ (x64). Some antivirus tools flag apps that self-extract bundled binaries — this is a known false-positive pattern for sideloaded apps.

## License

**GPL-3.0** — this project bundles the Stockfish engine (GPL-3.0). Stockfish source: https://github.com/official-stockfish/Stockfish

Rudzoft.ChessLib is MIT-licensed. chessdb.cn data is used per its public API.
