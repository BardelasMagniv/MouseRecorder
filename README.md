Mouse Recorder Prototype (C# WinForms)

Overview:
- Native Windows WinForms prototype that records mouse movements and events (move, down/up, wheel).
- Two modes: Active (in-app) and Passive (global) using Raw Input.
- Records high-resolution monotonic timestamps anchored to precise UTC and saves one CSV per session (auto-export).
- The prototype spawns a randomized target button (size and margin randomized each spawn); clicking spawns the next target and logs trial info.

Build & Run:
1. Install .NET 7 SDK (or newer) on Windows.
2. Open a command prompt and run:
   cd "<workspace>/MouseRecorder"
   dotnet build
   dotnet run --project MouseRecorder.csproj

Notes:
- CSV files are written to: %USERPROFILE%\Documents\MouseRecorder\ by default.
- Session metadata now includes an explicit `#IS_PASSIVE: True/False` field and the filename encodes the mode: `session_{mode}_{yyyyMMdd_HHmmss}_{sessionId}.csv` (mode is `active` or `passive`).
- This is an early prototype: it uses Raw Input and a buffered background CSV writer. Expect improvements and hardening in the next iteration.