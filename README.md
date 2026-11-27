# RageForge

Independent open-source research and visualization toolkit for Grand Theft Auto VI's RAGE engine files
(`RPF9` archives and `TTPF` texture stores). GUI + CLI, Windows x64.

Designed for local inspection of lawfully accessible files. RageForge does not export, extract or save game resources: everything is decoded in memory for display.

[![Discord](https://img.shields.io/badge/Discord-join-5865F2?logo=discord&logoColor=white)](https://discord.gg/4MGdMd2Jh9)

## Features

- **Explorer**: browse indexed resources by category (vehicles, weapons, characters,
  props, world, audio, video...) or by archive, with search and sortable columns.
- **World**: fly over the map in 3D. Map blocks stream around the camera with the
  game's own LOD hierarchy (HD, LOD, SLOD1-3) up to 6 km, plus sky, time of day,
  fog and sea. Interiors open as full 3D scenes.
- **Peds**: cutscene and story characters, world peds and animals assembled from
  their modular pieces (head, eyes, clothing, base hands and feet) and shown in 3D,
  with the face texture of each head.
- **3D viewer** (GPU): textured models, orbit / pan / zoom.
- **Textures** (`.ttpf`) decoded to images: AMD Gfx10/RDNA2 detiling + BC1–BC7, Xbox BCPack
  BC1 streams, and mip counts recovered from the store layout.
- **Audio** (AWC / Opus): play audio.
- **Video** (Bink 2): header information (resolution, frames, frame rate, duration). Videos are not played.
- **Shaders**: RDNA2 pipeline cache parsed and disassembled (GFX10.3 ISA, pseudo-HLSL).
- **Text / XML / hex** viewers.

## Download

Grab the latest build from [Releases](../../releases):

- `RageForge-gui-win-x64.zip` — the desktop app
- `RageForge-cli-win-x64.zip` — the command line tool

Windows x64, self-contained, nothing to install.

## Usage

### First launch

1. **File › Open game folder** (`Ctrl+O`) and pick the folder that contains the
   game's `.rpf` archives.
2. **Index › Build / update index**. This scans every archive once (a few
   minutes) and is cached, so later launches only need **Index › Load index**.

### Explorer (`Ctrl+1`)

- The tree on the left lists **Categories** (vehicles, weapons, props, audio,
  video...), **Archives** and texture stores. Click a node to list its files.
- Type in the filter box to narrow the current folder; press `Enter` or
  **Search all** to search every archive by name.
- Double-click a file (or `Enter`) to open it in a viewer window:
  - **models**: left drag rotates, right drag pans, wheel zooms, double-click resets;
  - **textures**: shown as images;
  - **audio**: pick a track and **Play**;
  - **video**: header information only (resolution, frames, frame rate, duration);
  - **shaders**: disassembly, tick **Pseudo-HLSL** for a readable version.
- Right-click files for **Open in World**, **Copy name / path** and **Properties**.

### World (`Ctrl+2`)

- **Load world** streams the whole map: detail loads as you get closer.
- To open one place, search it (house, gas, `tw2_20`...), choose **Interiors** or
  **Map** in the filter, select it and press **Load selected**.
- Camera: left drag to look, right drag to pan, wheel to zoom, `WASD` / `ZQSD` to
  move, `E` / `Space` up, `C` down, `Shift` faster, `Ctrl` slower.
- **View › Time of day** and **View › Show water** change the atmosphere.

### Peds (`Ctrl+3`)

- The list shows every character the index knows: cutscene and story characters,
  players, world peds and animals. Search by name, or filter **People** / **Animals**.
- Select one to assemble it: the head, eyes, clothing and the base hands and feet
  of its body type are loaded and put together in their shared bind pose.
- Faces use the head's own face texture when the game links one, otherwise one
  borrowed from a head of the same body type and ethnicity. Clothing is shown in
  grey with its fabric detail: its colours come from the game's runtime materials.
  Hair and beards are not shown (they are not stored as meshes).
- Camera: left drag rotates, right drag pans, wheel zooms, double-click resets.

## CLI

```
rageforge index <gameRoot>                        Index every archive (cached)
rageforge categories <gameRoot>                   Categories with item counts
rageforge archives <gameRoot>                     Archives with item counts
rageforge list <gameRoot> [category] [n]          List indexed files of a category
rageforge search <gameRoot> <text> [n]            Search every archive by name
rageforge show <gameRoot> <name>                  Describe a model, textures, audio, video, place or text
rageforge peds <gameRoot> [filter]                Characters and animals with their pieces
rageforge info <archive.rpf>                      Show an RPF9 header
rageforge ttpf list <store.ttpf>                  List textures in a store
rageforge shaders <shaders.rpf> [n] [--hlsl]      Shader cache summary or one shader's disassembly
rageforge materials <gameRoot> <model>            Shader parameters and textures of a model
```

Run `rageforge` without arguments for the full list.

## Files, network and processes

RageForge opens game archives read-only and never writes to them. It writes only the following,
none of which contains game resources:

| What | Where | Content | Kept |
|---|---|---|---|
| Index cache | `%LOCALAPPDATA%\RageForge\index\` | per archive: offsets, sizes, kinds, categories, names found in resource data or generated, bounds, LOD links | until deleted |
| Settings | `%LOCALAPPDATA%\RageForge\lastfolder.txt`, `theme.txt` | last game folder, theme | until deleted |
| Crash log (GUI) | `%LOCALAPPDATA%\RageForge\crash.log` | exception messages and stack traces, OS and app version; may contain file paths | until deleted |
| Benchmark log (GUI, opt-in) | the file named by the `RAGEFORGE_BENCH` environment variable | GPU name, frame times, draw and memory statistics | until deleted; only when the variable is set |

At startup the GUI also deletes temporary files left by RageForge 0.6 and earlier
(`%TEMP%\RageForge\*.bk2` and `%TEMP%\RageForge-update\`).

**Network.** At startup the GUI sends one HTTPS request to
`https://api.github.com/repos/nullbrik/RageForge/releases/latest` (User-Agent `RageForge-Updater`)
to see whether a newer version exists. No game data, file names or user data are sent. When an
update exists, a button opens the release page in your browser; RageForge does not download or
install anything itself. The CLI makes no network requests.

**Processes.** The GUI starts no other program, except your default browser when you click the
update button.

## Build from source

Requires the .NET 10 SDK.

```
dotnet build RageForge.slnx -c Release
```

## Community

Questions, bug reports and findings: join the [Discord](https://discord.gg/4MGdMd2Jh9).

## Project scope

RageForge is independent and is not affiliated with or endorsed by Rockstar Games or Take-Two Interactive. It does not provide game files or encryption keys, decrypt archives, or modify original game files. Use only lawfully accessible data.

See [LEGAL.md](LEGAL.md) for the project scope and legal framework, [CONTRIBUTING.md](CONTRIBUTING.md) for contribution rules, and [SECURITY.md](SECURITY.md) for security reporting.

## License

RageForge is licensed under the [GNU Lesser General Public License v3.0](LICENSE)
(LGPL-3.0-only). The LGPL supplements the [GNU GPL v3.0](COPYING), included as required.
The BCPack rANS model tables come from Microsoft's MIT-licensed
[silicon-rans](https://github.com/microsoft/silicon-rans); see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Status

The RPF9 table of contents (TOC) is encrypted. RageForge identifies accessible, non-encrypted resources through file carving and builds an independent index without decrypting or reconstructing the TOC. Paths and names from the encrypted TOC are unavailable; displayed names come from resource metadata or are generated. TTPF texture decoding is supported. See
[docs/RPF9.md](docs/RPF9.md) for the format notes.
