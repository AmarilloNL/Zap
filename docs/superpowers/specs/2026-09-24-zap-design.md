# Zap — fast copy & delete for Windows (design)

## Goal

A small Windows GUI app that copies and deletes files/folders much faster than
Explorer, with clear progress, without the user touching PowerShell or robocopy.

Version 1 scope: **Copy** and **Delete**. Out of scope for v1: mirror/sync,
Explorer right-click integration, scheduling, HDD-specific tuning.

## Tech

- C# / .NET 10, WPF, Windows only.
- Own engine (no robocopy). Single file copies use Win32 `CopyFileEx`
  (native speed, keeps timestamps/attributes, gives byte progress + cancel).
- Output: framework-dependent single-file `Zap.exe` (the machine already has
  the .NET 10 Desktop Runtime).

## Layout

```
Zap/
  Zap.slnx
  src/Zap.Engine/         class library, no UI code
  src/Zap.App/            WPF app
  tests/Zap.Engine.Tests/ xUnit tests against real temp folders
```

## Engine

### Progress
A `JobProgress` object with thread-safe counters the UI reads on a timer
(~4×/second): `TotalFiles`, `TotalBytes`, `FilesDone`, `BytesDone`,
`FilesSkipped`, `CurrentItem`, and an `Errors` list of `(path, message)`.
The UI computes speed and time remaining from `BytesDone` over time.

### Scanner
Walks the chosen items and returns every file (path, size, last-write time)
and every directory. It **never descends into reparse points**
(junctions/symlinks): they are reported as links, not followed. This matters
for Delete (following a junction would delete the target's contents) and Copy
(junction loops, e.g. in AppData). Unreadable folders go to `Errors`, the
scan continues.

### Copier
Input: source items (files and/or folders), destination folder, overwrite mode,
cancellation token, `JobProgress`.

- Folder `X` copied to `D` becomes `D\X` (like Explorer); file `f` becomes `D\f`.
- Creates the directory tree first, then copies files with 8 in parallel.
- Overwrite mode (dropdown in UI):
  - **Skip identical** (default): skip when destination exists with the same
    size and last-write time; otherwise overwrite.
  - **Always overwrite**.
  - **Never overwrite**: skip whenever destination exists.
- Skipped files count toward progress (`FilesSkipped`, bytes counted done).
- Before overwriting, clears read-only/hidden attributes on the destination
  (CopyFileEx refuses otherwise).
- Links found by the scanner are skipped and listed as "skipped (link)".
- Per-file failures (locked, access denied, disk full) go to `Errors`; the job
  continues with the next file.
- Cancel: stops starting new files and aborts in-flight copies via the
  CopyFileEx callback; a partially written file is deleted.
- Rejected up front: destination equal to or inside a source folder.
- Long paths (>260 chars) work (.NET handles them).

### Deleter
Input: items, mode (**Permanent** or **RecycleBin**), cancellation token,
`JobProgress`.

- **Permanent**: scan, delete all files in parallel (8), clearing read-only
  attributes when needed, then remove directories deepest-first. Links are
  removed as links (the link itself), never followed. Failures go to `Errors`,
  the job continues. Cancel stops between files.
- **Recycle Bin**: uses the Windows shell (`Microsoft.VisualBasic.FileIO`)
  per top-level item. No file-level progress (UI shows an indeterminate bar)
  and no cancel.
- **Protected paths** (checked for both modes, before anything happens):
  refuse a drive root; refuse any path that is equal to or an ancestor of
  `C:\Windows`, `Program Files`, `Program Files (x86)`, `ProgramData`,
  `C:\Users` or the current user profile; refuse anything inside `C:\Windows`,
  `Program Files` or `Program Files (x86)`. Folders inside the user profile
  (e.g. Downloads\junk) are allowed.

## App (WPF)

- One window, two tabs: **Copy** and **Delete**. Follows the Windows light/dark
  setting at startup.
- **Copy tab**: list of source items (Add files…, Add folder…, drag-and-drop,
  Remove, Clear), destination folder (Browse…, or drop a folder on the
  destination box), overwrite dropdown, **Start copy**.
- **Delete tab**: list of items (same add/drop controls), **Delete…** button.
- **Delete confirmation dialog**: after scanning shows "N files, X GB in M
  items", states permanent delete cannot be undone, buttons:
  **Delete permanently (fast)** (default, red), **Move to Recycle Bin
  (slower)**, **Cancel**. Protected-path violations show an error instead.
- **While running**: overall progress bar, "files done / total", bytes done /
  total, speed ("412 MB/s"), time left, current file, **Cancel** button. Input
  controls disabled.
- **When done**: summary line (e.g. "Copied 12,400 files (4.1 GB) in 0:32 ·
  3 skipped · 2 errors") and an error list (path + reason) if any.

## Testing

xUnit tests in real temp folders:
- Copy: nested tree copied with correct contents and timestamps; folder lands
  at `D\X`; each overwrite mode; read-only destination overwritten; locked
  source file → error recorded, other files copied; cancel stops early;
  destination-inside-source rejected; junction not followed.
- Delete: nested tree with read-only files fully removed; junction removed
  without touching its target's contents; locked file → error, rest deleted;
  protected-path rules (root, Windows, ancestors of profile, allowed child
  of profile).
- Scanner: counts and sizes correct.

The GUI is verified manually by running the app.
