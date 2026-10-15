# MicLock

A small Windows tray app that locks your microphone's input level. If anything changes it, MicLock sets it back right away.

Some apps lower your mic level on their own while you talk. Chrome, Edge and Electron apps do this through WebRTC auto gain, and turning it off in each app isn't always possible. MicLock keeps the level where you set it, for every app at once.

## Requirements

- Windows 10 or 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (to run), or the .NET 10 SDK (to build)

## Build

```
git clone https://github.com/XxVoidicxX/miclock.git
cd miclock\src
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\dist
```

This creates `dist\MicLock.exe`. Put it wherever you want to keep it.

## Use

1. Run `MicLock.exe`. A mic icon shows up in the tray. It may be hidden under the `^` arrow.
2. Click the icon to open the menu.
3. Under **Microphone**, pick the mic to lock. On first run it picks a Realtek mic if there is one, otherwise your default mic.
4. Under **Level**, pick a level. To use your own value, pause the lock, set the slider in Sound settings, then choose **Use current level**.

| Icon | Meaning |
| --- | --- |
| Green mic | Locked |
| Gray mic | Paused |
| Gray mic with red slash | The chosen mic isn't connected |

The lock works both ways, so raising the level by hand snaps back too. Pause it first if you want to change it. Mute is never touched.

Settings are saved in `%APPDATA%\MicLock\settings.json`.

## Start with Windows

Click the tray icon and check **Start with Windows**. MicLock adds itself to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it starts when you sign in. No admin rights needed.

If you move `MicLock.exe` later, uncheck and check the option again so it points to the new location.

You can also turn it off under Task Manager > Startup apps.

## Self-test

Close the running copy first, then:

```
MicLock.exe --selftest out.txt
```

It writes the menu to `out.txt`, moves the level to 40% and 100%, and records whether it was set back.
