<div align="center">

# ⚡ Zap

**Fast copy, move and delete for Windows, with live progress.**

[![Latest release](https://img.shields.io/github/v/release/AmarilloNL/Zap?style=for-the-badge&color=f59e0b)](../../releases/latest)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4?style=for-the-badge&logo=windows11&logoColor=white)](#download)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=for-the-badge&logo=dotnet&logoColor=white)](#building)

[**⬇️ Download**](../../releases/latest) · [✨ What it does](#what-it-does) · [💻 Command line](#command-line)

</div>

<a id="what-it-does"></a>

## ✨ What it does

| | |
|---|---|
| 📋 **Copy** | Add files/folders (or drop them), pick a destination, choose what happens with existing files, **Start copy**. |
| 📦 **Move** | Like Copy, but instant on the same drive (folders are renamed or merged). Across drives it copies, then removes the originals. |
| 🗑️ **Delete** | Add files/folders, **Delete…**, then choose **permanent** (fast) or **Recycle Bin** (slower). |

> [!NOTE]
> System folders and whole drives are refused, so a slip of the mouse can't wipe Windows.

<a id="download"></a>

## ⬇️ Download

Grab the latest `.exe` from [**Releases**](../../releases/latest):

| File | Size | Needs |
|---|---|---|
| 📦 `Zap-standalone.exe` | ~75 MB | nothing: runs on any Windows 10/11 PC |
| 🪶 `Zap.exe` | ~260 KB | the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

<a id="command-line"></a>

## 💻 Command line

```powershell
Zap.exe <paths>            # opens with those items in the Copy list
Zap.exe --move <paths>     # ...in the Move list
Zap.exe --delete <paths>   # ...in the Delete list
```

> [!TIP]
> Handy for a **"Send to"** shortcut: point it at `Zap.exe` and send files straight into Zap.

<a id="building"></a>

## 🛠️ Building

```powershell
dotnet publish src/Zap.App -c Release -o publish   # → publish\Zap.exe
dotnet test                                        # run the tests
```
