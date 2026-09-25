# Bundled 7-Zip 26.03 (Windows x64)

Copyright (C) 1999-2026 Igor Pavlov.

The unmodified `win-x64/7z.exe` and `win-x64/7z.dll` are extracted from the official stable Windows x64 package. No installer is executed by the manager. The console executable loads the adjacent codec DLL, including its RAR decoder.

- Official release: https://github.com/ip7z/7zip/releases/tag/26.03
- Binary package: https://github.com/ip7z/7zip/releases/download/26.03/7z2603-x64.exe
- Corresponding source: https://github.com/ip7z/7zip/releases/download/26.03/7z2603-src.7z
- The corresponding source archive is `7z2603-src.7z` in this repository. The packaging script places a copy beside the Windows application archive.
- License and third-party notices: `win-x64/License.txt`.
- GNU LGPL 2.1 text: `copying.txt`. Additional RAR restrictions: `unRarLicense.txt`.

The installer and source archive hashes were verified against the official GitHub release asset digests. Both executable components are unmodified and can be replaced together with a compatible version.

| File | SHA256 |
| --- | --- |
| 7z2603-x64.exe (upstream package) | 0859c524b8a63551848f0c246abddcb1d0b7b656b0fbfe879f8d85e61a9e6edd |
| 7z2603-src.7z | 41e2a7c0e9f838351c625e01f0f581a2188bb3dda10c8e0b4a852da26546ffe2 |
| win-x64/7z.exe | 6ee3c0ed0b27663c1b948ae85a7c0bb073aed1498983182f3f0df1f6a8c30b2f |
| win-x64/7z.dll | 65e4c1f855f9ef6e8f0f5df8e3f27d9eb5f07311408639da0a1ca0b8f4871b0d |
