# Steam Stats Editor

A Windows tool based on Steam Achievement Manager (SAM), adding bulk editing through plain-text lists, difference previews and scheduled staged submissions.

[Download](https://github.com/wyfang/steam-stats-editor/releases) · [简体中文](./README.md)

## Features

Retains SAM's game picker, achievement editing and statistics editing, with these additions:

| Added feature | Purpose |
| --- | --- |
| Text import and export | Bulk-edit `field=value` lists yourself or with an external AI tool; no AI is built in |
| Validation and difference preview | Check fields, numeric types, ranges and permissions before applying targets to the UI |
| Separate target and readback columns | Distinguish pending targets from values read from Steam |
| Single-step and scheduled submissions | Progress toward targets within field constraints, verify readback, back off on temporary errors and stop on uncertain results |
| Live logs | Save progress and errors automatically to `logs/` beside the application |
| UI and package cleanup | Group related actions, improve button spacing and provide one `SteamStatsEditor.exe` launcher |

## Usage

Requires Windows, .NET Framework 4.8, a running and signed-in Steam client, and network access. Running the application does not require the .NET SDK.

1. Download the v1.0.0 Windows ZIP from [Releases](https://github.com/wyfang/steam-stats-editor/releases), extract it completely to a writable location, open the folder named after the ZIP and run `SteamStatsEditor.exe`. Keep the adjacent `app/` and `licenses/` directories.
2. Select a game and wait for its data. “导出 Steam 已读取值” exports values read from Steam for a backup; “导出界面目标值” exports the pending targets currently in the UI.
3. Edit the target numbers after the equals signs, then use “导入清单” to review differences and apply them to the UI. Removing a line leaves that field unchanged; `0` is a valid target.
4. Choose “提交一步” for one step or “自动分步” for automatic steps. Import changes only the UI; submission sends changes to Steam.

Building from source requires Windows, the .NET 8 SDK, MSBuild from Visual Studio 2022 Build Tools, and the .NET Framework 4.8 targeting pack. Run in Developer PowerShell:

```powershell
./scripts/build-windows.ps1
```

The script runs tests and creates `artifacts/windows-*/steam-stats-editor-windows.zip`. Builds are also available through [GitHub Actions](https://github.com/wyfang/steam-stats-editor/actions/workflows/windows.yml).

## Notes

- Fields come from the data actually read by SAM. Protected, trusted-server and average-rate fields are read-only; staged submission cannot bypass field permissions.
- Automatic submission defaults to 120 seconds between rounds, with a selectable minimum of 60 seconds. Steam does not publish a guaranteed fastest interval. Stopping prevents later rounds and cannot undo requests already sent or changes in Steam's cache.
- Logs are saved in `logs/` beside `SteamStatsEditor.exe`, with a new file for each game editor window. They are never automatically deleted or uploaded. Delete unwanted files after closing the application. An unwritable directory produces a warning; logs can then be saved manually from the log window.
- Exported lists and logs stay local, and personal statistics are not included in the repository. Keep a backup of the original values before editing. Steam and game rules still apply; this project does not guarantee that every game will accept changes or that values can be restored.

## License

Based on Rick (Gibbed)'s [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager), starting at commit `de8b71048a0cee3c3e97cd8535e0f55ca86513e4`. The upstream [zlib License](./LICENSE.txt) and copyright notices are preserved. Original code and documentation added in this project are provided under the same license.

Retained [Fugue Icons](https://p.yusukekamiyamane.com/) are by Yusuke Kamiyamane and licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Third-party dependencies retain their own licenses; see [NOTICE.md](./NOTICE.md) for attribution. Steam, Valve, game names and game assets belong to their respective owners. This project is not affiliated with or endorsed by Valve.
