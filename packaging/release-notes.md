Mac Song Manager

Unsigned build for Apple silicon (`osx-arm64`) and Intel (`osx-x64`). Each zip contains `Mac Song Manager.app`.

The app is not notarized. After download, macOS may block the first open. Right-click the app, choose Open, and confirm. Or run:

```
xattr -dr com.apple.quarantine "/Applications/Mac Song Manager.app"
```

Version 0.2.0 reads CamelCase and toolkit-style file names, stores edited artist and title tags beside the library, can ask MusicBrainz to fix spelling, and renames files to `Artist - Song Title_m.psarc`.

Convert PC songs writes a new Mac file under `dlc/converted-from-pc/` and leaves the `_p.psarc` original in place. That conversion is adapted from glebb/rocksmithconvert (GPL-3.0-or-later) and pyrocksmith by 0x0L (MIT). See NOTICE.

The app is GPL-3.0-or-later.
