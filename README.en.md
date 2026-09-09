# Steam Stats Editor

A Windows project based on Steam Achievement Manager, planned to support bulk editing of game statistics through plain-text lists and staged submission within field constraints.

[Requirements](./docs/requirements.md) · [简体中文](./README.md)

## Features

**This project is at the design stage. The additions below are not implemented.** The application source remains at the upstream baseline. This stage establishes the repository and design only; no build artifacts with the proposed features are provided.

- Export UTF-8 text from field definitions and values actually read by SAM, including types, ranges and constraints.
- Let users or AI edit `field_id = target_value`, preview differences and then load targets into the statistics interface. Importing alone will not submit anything to Steam.
- Validate the game App ID, field names, duplicates, numeric types and actual constraints. Unknown or invalid content will block the entire import.
- Design staged submission for targets constrained by `maxchange` and other conditions, with callback and readback verification, progress, logs and a stop control.

## Usage

### Read the design

- [Requirements and acceptance scope](./docs/requirements.md): user flow, feature boundaries and future validation.
- [Text format](./docs/text-format.md): syntax, exported content and import validation.
- [Staged submission design](./docs/submission-design.md): constraints, state machine, timing and failure handling.
- [Steam submission research](./docs/steam-submission-research.md): official frequency guidance, result codes, corrected-value readback and adaptive retries.

The target platform is Windows. The plan retains upstream C#, .NET Framework 4.8 and Windows Forms. Future use will require a signed-in Steam client and network access. Instructions for using and building the added features will follow implementation and validation.

Personal statistics lists are requirements references only and remain on the user's machine, outside this repository. The design uses the game's actual fields read through SAM each time. It does not fix a CS2 field count or treat a manually compiled example as the complete field list.

### Future validation

Implementation must be followed by offline tests for parsing, constraints and step planning, plus Windows compilation and interface checks. Before small-scope read/write validation on a real Steam account, the proposed fields, current values, targets and expected effects must be shown to the user for confirmation. None of those checks is claimed as completed at this stage.

## Notes

Exporting, editing and import previews are planned to operate locally. Only a separate submission action will request writes to Steam. `0` is a valid target; an omitted field remains unchanged. Protected, permission-restricted, trusted-server-set and average-rate fields are planned to be read-only. Editing comments will not relax actual field constraints.

Steam describes `maxchange` in terms of changes between adjacent `SetStat` calls. It must not be assumed to grant a fresh, indefinitely repeatable allowance after every successful store. The staged strategy needs validation against actual definitions, submission callbacks and readback values; acceptance of the final target cannot be promised.

An interval of 120 seconds is a provisional design starting point. The reviewed official documentation recommends minute-scale calls but does not publish a maximum rate or fixed minimum interval. The plan will examine minute-scale intervals, error-specific backoff and replanning from readback values. A guaranteed minimum interval is currently unknown. Duration estimates will use the actual difference, feasible step size and interval. Stopping will prevent future rounds; an already-issued request may still complete, and written data will not be undone automatically.

The project will not bypass server validation. It does not guarantee recovery of values or compatibility with future Steam or game interface changes. Steam and game rules still apply. See the staged submission design for details.

## License

This project is based on [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager), originally by Rick (Gibbed), at commit `de8b71048a0cee3c3e97cd8535e0f55ca86513e4`. The application source currently retains that upstream baseline; the added project documents explicitly describe planned changes. The upstream [zlib License](./LICENSE.txt) and copyright notices are preserved. Original documentation in this project is provided under the same license.

Retained [Fugue Icons](https://p.yusukekamiyamane.com/) are by Yusuke Kamiyamane and licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). See [NOTICE.md](./NOTICE.md) for attribution. Steam, Valve, game names and game assets belong to their respective owners. This is not official Valve software and is not affiliated with or endorsed by Valve.
