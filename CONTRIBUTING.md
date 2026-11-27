# Contributing

Thanks for helping. Please read [LEGAL.md](LEGAL.md) first: contributions must keep the project
free of game content and within its scope.

## Never include

- Game files, assets or excerpts of them: archives, textures, models, audio, video, maps, text,
  or raw bytes copied from game files, including in tests.
- Screenshots, renders or recordings of game content, in the repository, issues or pull requests.
- Encryption keys, key material, or code that decrypts game archives or bypasses any protection.
- Code copied from other projects unless its licence allows it and it is credited in
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Independent implementations based on lawfully obtained information are welcome; comply with all applicable third-party licence conditions.
- Executables or code from the game, decompiled or otherwise.
- Features for exporting or extracting game resources, porting content into other games, or integrating with multiplayer platforms.
  No code path may write game resources, or parts of them, to disk.

## Tests and research tools

The public repository contains the application only: `RageForge.Core`, the CLI and the GUI. The
test suite and the research tools are kept by the maintainer outside the repository.

The tests use synthetic data built in code, never bytes taken from game files. When you report a
format finding or send a change to a parser, describe the format fact in your own words so a
synthetic test can be written for it.

## Code style

- C# / .NET 10, nullable enabled, warnings as errors.
- No comments in code except the banner at the top of each file: `/* RAGEFORGE - <title> */`.
- Binary parsing with `BinaryPrimitives` over spans.
- Commit messages: one line, in English.
- A new dependency that ships in the release packages must be listed in
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), with its licence text in `licenses/`.

## Reporting format findings

Describe findings in your own words (offsets, field meanings, algorithms). Do not attach game
files or extracted content.
