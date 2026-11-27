# Third-party notices

RageForge is licensed under LGPL-3.0-only (see [LICENSE](LICENSE) and [COPYING](COPYING)).
The release packages bundle the third-party components listed below. Full licence and notice
texts are in the [`licenses/`](licenses) folder, which the release workflow copies into both
packages next to this file.

Package versions are those pinned in `Directory.Packages.props` and resolved for `win-x64`;
licences were read from each package's `.nuspec` and licence files, and from the upstream
repositories where the package only gives an SPDX expression.

## Code included in the RageForge sources

### Microsoft silicon-rans

`src/RageForge.Core/Ttpf/BcPackModels.cs` holds the five rANS model tables (`base_table`,
`freq_table`, `rf_table`, `rs_table`, `rn_table`, 9 x 16 x 10 entries each) of
`mdu_minirans.v` from https://github.com/microsoft/silicon-rans, as the arrays `Base`, `Freq`,
`Reciprocal`, `Shift` and `Offset`. The 7,200 values were compared one by one with the upstream
file and are identical. Used under the MIT License ([licenses/silicon-rans-LICENSE.txt](licenses/silicon-rans-LICENSE.txt)):

```
MIT License

    Copyright (c) Microsoft Corporation.

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE
```

## Components bundled in the release packages

| Component | Version | Shipped in | Licence | Copyright | Text |
|---|---|---|---|---|---|
| .NET runtime (self-contained) | 10.0.x | GUI, CLI | MIT, plus third-party notices | © .NET Foundation and Contributors | [licence](licenses/dotnet-runtime-LICENSE.txt), [notices](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) |
| Avalonia (Avalonia, Desktop, Skia, HarfBuzz, Win32, X11, FreeDesktop, Native, Themes.Fluent, Remote.Protocol...) | 12.1.6 | GUI | MIT | © AvaloniaUI OÜ | [licence](licenses/Avalonia-LICENSE.txt) |
| ANGLE (`av_libglesv2.dll`, from Avalonia.Angle.Windows.Natives) | 2.1.27548.20260419 | GUI | BSD-3-Clause | © The ANGLE Project Authors | [licence](licenses/ANGLE-LICENSE.txt) |
| SkiaSharp (managed + `libSkiaSharp.dll`) | 3.119.4 | GUI | MIT | © Xamarin, Inc.; © Microsoft Corporation | [licence](licenses/SkiaSharp-LICENSE.txt) |
| Skia and its native components (FreeType, libpng, libjpeg-turbo, libwebp, expat, ICU, zlib, DNG SDK, piex, sfntly, SPIR-V...) inside `libSkiaSharp.dll` | as built by SkiaSharp 3.119.4 | GUI | BSD-3-Clause and the licences listed in the notice file | see notice file | [notices](licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt) |
| HarfBuzzSharp (managed + `libHarfBuzzSharp.dll`) | 8.3.1.3 | GUI | MIT (wrapper); HarfBuzz under the "Old MIT" licence | © Microsoft Corporation; HarfBuzz: © Google, Inc., Mozilla Foundation, Codethink Limited and others | [licence](licenses/SkiaSharp-LICENSE.txt), [notices](licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt) |
| Inter typeface (embedded in Avalonia.Fonts.Inter 12.1.6) | Inter 3.019 | GUI | SIL Open Font License 1.1 | © 2020 The Inter Project Authors (https://github.com/rsms/inter) | [licence](licenses/Inter-OFL-1.1.txt) |
| MicroCom.Runtime | 0.11.6 | GUI | MIT | © 2021 Nikita Tsukanov | [licence](licenses/MicroCom-LICENSE.txt) |
| Tmds.DBus.Protocol | 0.94.1 | GUI (Linux code path, unused on Windows) | MIT | © 2006 Alp Toker; © 2010 Other Contributors; © 2016 Tom Deseyn | [licence](licenses/Tmds.DBus-LICENSE.txt) |
| CommunityToolkit.Mvvm | 8.4.2 | GUI | MIT | © .NET Foundation and Contributors | [licence](licenses/CommunityToolkit-LICENSE.txt) |
| CommunityToolkit.HighPerformance | 8.4.0 | GUI, CLI | MIT | © .NET Foundation and Contributors | [licence](licenses/CommunityToolkit-LICENSE.txt) |
| NAudio.Core, NAudio.WinMM | 2.2.1 | GUI | MIT | © Mark Heath | [licence](licenses/NAudio-LICENSE.txt) |
| SharpZipLib | 1.4.2 | GUI, CLI | MIT | © SharpZipLib Contributors | [licence](licenses/SharpZipLib-LICENSE.txt) |
| BCnEncoder.Net | 2.2.0 | GUI, CLI | MIT OR Unlicense (used under the Unlicense) | Nominom and contributors | — |
| Concentus (Opus) | 2.2.2 | GUI, CLI | BSD-3-Clause | see below | [licence](licenses/Concentus-LICENSE.txt) |

Concentus / Opus licence:

```
Copyright (c) by various holding parties, including (but not limited to): 
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


This repository and its redistributable packages contain independently compiled
versions of the Opus C reference library, which is maintained by Xiph.org and the
Opus open-source contributors. The source code for these libraries is freely available
at https://gitlab.xiph.org/xiph/opus/-/tags/v1.5.2, and all binaries are being
redistributed to you under the same terms of the general Opus license dictated above.
```

## Not distributed

Test-only packages (xUnit, Microsoft.NET.Test.Sdk,
coverlet) and `AvaloniaUI.DiagnosticsSupport` (Debug builds only) are not distributed.
