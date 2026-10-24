# Third-party components

| Component | License | How it ships |
|---|---|---|
| [mpv](https://github.com/mpv-player/mpv) as `libmpv-2.dll` (build of [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake), release `20261021`) | GPL-2.0-or-later (mpv built with its `gpl` option) | embedded into the exe (GZip), written to `retake_data\bin\` at first start |
| [FFmpeg](https://ffmpeg.org/) and the codec and rendering libraries linked into that DLL (libx264, libx265, dav1d, libplacebo, zimg and others of the build recipe) | GPL (FFmpeg configured with GPL components) and the libraries' own licences | inside `libmpv-2.dll` |
| [Sora](https://github.com/sora-xor/sora-font) (weights 450, 550, 600, 650, latin subset) | SIL Open Font License 1.1, `src\Retake\fonts\LICENSE-Sora.txt` | embedded into the exe |
| [Geist Mono](https://github.com/vercel/geist-font) (weight 450, latin subset) | SIL Open Font License 1.1, `src\Retake\fonts\LICENSE-GeistMono.txt` | embedded into the exe |
| .NET Framework 4.8 | Microsoft licence | part of Windows; not shipped |
| [7-Zip](https://www.7-zip.org/) | LGPL-2.1-or-later with the unRAR restriction | build tool only (unpacks the libmpv archive); not shipped |

## libmpv

- Build: `shinchiro/mpv-winbuild-cmake`, release `20261021`, asset `mpv-dev-x86_64-20261021-git-e471496c56.7z`
  (<https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/mpv-dev-x86_64-20261021-git-e471496c56.7z>,
  31,622,190 bytes, SHA-256 `efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633`).
- `libmpv-2.dll`: 121,108,480 bytes, SHA-256 `04e4d27dc385dc18ddd93fbe69c75e635b864f3c6ec53a9122df54c2f7842f4a`;
  mpv `v0.41.0-1227-ge471496c5`, FFmpeg `N-127893-g9a861e968`, client API 2.6, x86_64.
- Licence: mpv is built with its default `gpl` option and the FFmpeg inside it carries GPL components, so the DLL is
  distributed under the GPL. Retake is licensed under GPL-3.0, which is compatible with it; the exe as a whole is
  distributed under GPL-3.0.
- Corresponding source: mpv at commit `e471496c5` (<https://github.com/mpv-player/mpv>), FFmpeg at `9a861e968`
  (<https://git.ffmpeg.org/ffmpeg.git>), and the build recipe with the exact versions of every library at the
  release tag `20261021` of <https://github.com/shinchiro/mpv-winbuild-cmake>.
- Embedding: the build packs the DLL with GZip and embeds it with its SHA-256 into `Retake.exe`. At start the exe
  writes it to `retake_data\bin\libmpv-2.dll` when it is missing or its hash differs, verifies it and loads it from
  that path. The DLL is never downloaded at runtime.
