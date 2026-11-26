Mac Song Manager

Unsigned build for Apple silicon (`osx-arm64`) and Intel (`osx-x64`). Each zip contains `Mac Song Manager.app`.

The app is not notarized. After download, macOS may block the first open. Right-click the app, choose Open, and confirm. Or run:

```
xattr -dr com.apple.quarantine "/Applications/Mac Song Manager.app"
```

This build reads file names and file bytes only. It does not open song packages.
