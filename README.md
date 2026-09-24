# Zap

Fast copy and delete for Windows, with live progress.

- **Copy**: add files/folders (or drop them), pick a destination, choose what happens with existing files, Start copy.
- **Move**: like Copy, but instant on the same drive (folders are renamed or merged); across drives it copies, then removes the originals.
- **Delete**: add files/folders, Delete…, then choose permanent (fast) or Recycle Bin (slower).
  System folders and whole drives are refused.

**Download:** grab the latest `.exe` from [Releases](../../releases):
- `Zap-standalone.exe`: runs on any Windows 10/11 PC, nothing to install (~75 MB).
- `Zap.exe`: tiny (~260 KB), needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Build: `dotnet publish src/Zap.App -c Release -o publish` → `publish\Zap.exe`
Tests: `dotnet test`

Command line: `Zap.exe <paths>` opens with those items in the Copy list; `Zap.exe --move <paths>` or `--delete <paths>` in the Move or Delete list
(handy for a "Send to" shortcut).
