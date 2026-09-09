# Steam Stats Editor

A Windows project based on Steam Achievement Manager for bulk editing of game statistics through plain-text lists.

[简体中文](./README.md)

## Features

The first version under development implements statistics export, validated text import with a difference preview, UI target values, single-step and automatic staged submission, and live logs. Values are read again after the stored callback; temporary errors have bounded backoff, and uncertain results stop the operation.

Local logic tests and Windows x86 cross-compilation pass. Windows UI execution and real Steam writes have not been verified, and no verified release with the new features is available.

## Usage

The target environment is Windows with .NET Framework 4.8. A running, signed-in Steam client and network access are required.

Select a game in `SAM.Picker.exe` and wait for its data. Use “导出清单” to export, edit the target numbers after the equals signs, then use “导入清单” to review differences and apply targets to the UI. Import does not write to Steam. Choose “提交一步” for one step or “自动分步” for automatic steps afterward. Removing a line leaves that field unchanged; `0` is a valid target.

Building requires Windows, the .NET 8 SDK, MSBuild from Visual Studio 2022 Build Tools, and the .NET Framework 4.8 targeting pack. Run in Developer PowerShell:

```powershell
./scripts/build-windows.ps1
```

The script runs logic tests, builds x86 applications, renders import previews with synthetic data, and creates a ZIP in `artifacts/windows-*/`. UI checks do not connect to Steam. Extract all dependency files alongside `SAM.Picker.exe`.

## Notes

Game statistics fields come from the data actually read by SAM. Personal lists and account data are not included in the repository. Steam and game rules still apply; this project does not guarantee that servers will accept changes or that values can be restored.

Protected, trusted-server and average-rate fields are read-only. Staged submission defaults to 120 seconds, with a selectable minimum of 60 seconds; Steam does not publish a guaranteed fastest interval. Stopping prevents later rounds and cannot undo requests already sent or changes in Steam's cache. Logs stay in `%LOCALAPPDATA%/SteamStatsEditor/logs/` and are not automatically uploaded.

## License

This project is based on [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager), originally by Rick (Gibbed), at commit `de8b71048a0cee3c3e97cd8535e0f55ca86513e4`. The upstream [zlib License](./LICENSE.txt) and copyright notices are preserved. Original code and documentation added in this project are provided under the same license.

Retained [Fugue Icons](https://p.yusukekamiyamane.com/) are by Yusuke Kamiyamane and licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). See [NOTICE.md](./NOTICE.md) for attribution. Steam, Valve, game names and game assets belong to their respective owners. This is not official Valve software and is not affiliated with or endorsed by Valve.
