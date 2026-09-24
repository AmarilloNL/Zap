# Zap

Fast copy and delete for Windows, with live progress.

- **Copy**: add files/folders (or drop them), pick a destination, choose what happens with existing files, Start copy.
- **Delete**: add files/folders, Delete…, then choose permanent (fast) or Recycle Bin (slower).
  System folders and whole drives are refused.

Requires the .NET 10 Desktop Runtime.

Build: `dotnet publish src/Zap.App -c Release -o publish` → `publish\Zap.exe`
Tests: `dotnet test`

Command line: `Zap.exe <paths>` opens with those items in the Copy list, `Zap.exe --delete <paths>` in the Delete list
(handy for a "Send to" shortcut).
