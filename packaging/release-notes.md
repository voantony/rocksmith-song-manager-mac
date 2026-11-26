Mac Song Manager 0.3.0

Artist and title now come from the song manifest inside each package. Short official names such as `bach_m.psarc` show the real artist and title. Album, year, arrangements, and tuning are columns. Your own tag edits still win. A rescan reuses the manifest cache.

Rename to Artist - Title uses those names. The status column is wide enough to read Enabled, Disabled, and PC format.

Unsigned builds for Apple silicon (`osx-arm64`) and Intel (`osx-x64`). Each zip contains `Mac Song Manager.app`.

The app is not notarized. After download, macOS may block the first open. Right-click the app, choose Open, and confirm. Or run:

```
xattr -dr com.apple.quarantine "/Applications/Mac Song Manager.app"
```

The app is GPL-3.0-or-later. PC-to-Mac conversion remains the C# port of glebb/rocksmithconvert and pyrocksmith. See NOTICE.
