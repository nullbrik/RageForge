# Removed features

Features that existed in earlier versions and are still visible in the Git history and in the
tags `v0.1.0` to `v0.6.0`. None of them is part of the current code.

| Feature | Added | Removed | Notes |
|---|---|---|---|
| TOC cipher interface (`ITocCipher`, `AesCbcTocCipher`, `Rpf9Archive.DecryptToc`) | `v0.1.0` (`ebbeb7d`) | `e6515f7` (after `v0.6.0`) | AES-CBC wrapper around .NET's `Aes` class that took the key and IV as parameters. No key, key material or key-recovery code was ever committed. Present in every tag from `v0.1.0` to `v0.6.0`. |
| Known TOC preamble (`Rpf9Toc.CipherPreamble`, `HasKnownPreamble`) | `v0.1.0` (`ebbeb7d`) | after `v0.6.0` | The 32 constant bytes found at the start of every TOC, used only to recognise archives. |
| Asset export (GUI menus and buttons, CLI `export`, `export-all`, `export-area`, `export-ped`) | `v0.1.0` | after `v0.6.0` | Wrote PNG, GLB, WAV, BK2, XML, TXT and BIN files. Code: `RageForge.Core/Exporters/AssetExporter.cs`, `Model/GltfExport.cs`, `World/SceneExport.cs`, `Imaging/Png.cs`, `AwcDecoder.WriteWav`, `AssembledPed.WriteGlb`. |
| Raw extraction (GUI "Extract", CLI `extract`, `ttpf dump`, `ttpf dump-all`) | `v0.1.0` | after `v0.6.0` | Wrote resources or textures as stored. |
| Probe research commands that wrote files (`ped-parts`, `find-u32`, `pso-find`, `txd-probe`, `audio-export`, `export-glb`) | `v0.6.0` | after `v0.6.0` | Developer tool, never shipped in release packages. |
| Video playback through RAD `binkplay` | `v0.3.0` | after `v0.6.0` | Wrote a temporary copy of the video to `%TEMP%\RageForge`. Current versions delete leftover copies at startup. |
| Automatic update install | `v0.3.0` | after `v0.6.0` | Downloaded the release zip to `%TEMP%\RageForge-update` and replaced the application files without checking a signature. Current versions only check for a newer release and open its page; leftover folders are deleted at startup. |

The test suite (`tests/`) and the research tool `tools/RageForge.Probe` were part of the public
repository up to `v0.7.271126`. They are now kept by the maintainer outside the repository; the
release packages never included them.

The README of versions before `e6515f7` referenced two preview images (`docs/preview.png`,
`docs/preview-world.png`); those images were never committed to the repository.
