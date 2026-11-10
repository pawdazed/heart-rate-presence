<p align="center"><img src="assets/hr_high.png" width="110" alt=""></p>

<h1 align="center">Heart Rate Presence</h1>

<p align="center">Show your live Apple Watch heart rate on your Discord profile.<br>
The text and icon change with your BPM. Free, no account, no API key.</p>

<p align="center"><a href="https://github.com/pawdazed/heart-rate-presence/releases/latest"><b>⬇ Download for Windows</b></a></p>

---

```
Apple Watch ──(free HeartCast app)──► Bluetooth ──► Heart Rate Presence ──► your Discord status
```

| BPM     | Your profile shows                      |
|---------|-----------------------------------------|
| 0–59    | ❤️ 55 BPM · *basically asleep*           |
| 60–89   | ❤️ 72 BPM · *vibing*                     |
| 90–119  | ❤️ 104 BPM · *something's going on*      |
| 120–149 | ❤️ 131 BPM · *heart going brrr*          |
| 150+    | ❤️ 162 BPM · *send help*                 |

<p align="center">
<img src="assets/hr_resting.png" width="56" alt="resting">
<img src="assets/hr_normal.png" width="56" alt="normal">
<img src="assets/hr_elevated.png" width="56" alt="elevated">
<img src="assets/hr_high.png" width="56" alt="high">
<img src="assets/hr_max.png" width="56" alt="max">
</p>

## Setup

1. **On your iPhone:** install [HeartCast: Heart Rate Monitor](https://apps.apple.com/us/app/heartcast-heart-rate-monitor/id1499771124) (free).
   On your watch, open HeartCast and press **Start**.
2. **On your PC:** download `HeartRatePresence.exe` from [Releases](https://github.com/pawdazed/heart-rate-presence/releases/latest),
   open it and click **Set up and start**.

That's it. It installs itself (no admin needed), adds itself to the Start menu, opens with Windows if you want, and connects to your watch over Bluetooth.

**Windows says "Windows protected your PC"?** Click **More info → Run anyway**. That warning appears for apps that aren't code-signed (signing costs money). The source code is all here.

**Needs:** Windows 10 or 11 with Bluetooth, the Discord desktop app, an Apple Watch and iPhone. A Bluetooth chest strap works too, instead of the watch.

## Keep it running all day

1. Connect once with HeartCast open on your iPhone.
2. Settings → **Pair with Windows** (tap **Pair** when your iPhone asks).

After that you can put the phone away. If the connection drops (PC sleeps, you walk out of range), it reconnects by itself as soon as you're back,
even with HeartCast in the background. While it's disconnected, your profile shows "💤 heart rate paused" (you can change that text, or hide the status instead).
It keeps going until you press **Stop**.

The one real limit: an Apple Watch only streams heart rate continuously while a workout is running (that's how HeartCast works), and that uses
more battery than normal. Charge the watch when it's low and restart HeartCast on it.

## Customize it

Open the app (Start menu → Heart Rate Presence):

* **BPM ranges tab:** change the thresholds, the two lines of text, the icons and hover text. Add as many ranges as you want.
  Use `{bpm}` anywhere for your live heart rate, plus `{name}`, `{min}` and `{max}`.
* **Your own icons:** paste a direct `https://` link to any PNG or GIF (animated GIFs work). Use links that don't expire,
  like imgur or a GitHub repo. Discord attachment links stop working after a while.
* **Settings tab:** Playing / Listening to / Watching / Competing in, what the member list shows, the elapsed timer,
  up to 2 link buttons on your profile, and start-with-Windows.
* **Test with a fake BPM** on the Live tab lets you try your ranges without the watch.

Everyone using the built-in Discord app shows the same name after "Playing". To change that name, make your own app at
[discord.com/developers](https://discord.com/developers/applications) and paste its Application ID into Settings → Own Application ID.

## Troubleshooting

* **"Looking for a Bluetooth heart rate monitor" forever:** bring HeartCast to the front **on your iPhone**. iPhones hide the
  heart-rate signal from Windows while the app is in the background, unless you've paired (see above). Once it's connected, it keeps going in the background.
  Still nothing? Type part of the device's name (for example `HeartCast`) into Settings → Bluetooth device name.
* **Updating:** download the new `HeartRatePresence.exe` and open it. It offers to close the running copy and updates it in place. Your settings are kept.
* **Nothing shows on Discord:** in Discord, go to User Settings → Activity Privacy and turn on "Share my activity". Also make sure you're not set to Invisible.
* **The number lags a bit:** Discord allows about one status update every 4 seconds.
* **Other heart rate sources:** Settings → Source also supports HypeRate (free API key, which takes a couple of days), Pulsoid (paid BRO plan),
  and Local HTTP (anything on your network can send `http://<pc-ip>:8765/?bpm=80`).

## Uninstall

Settings → **Uninstall…** removes the app, its shortcut, the startup entry and its settings.

## Build it yourself

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

`src/` is the whole app (C#, .NET Framework 4.8, which every Windows 10/11 PC has). The first build downloads Microsoft's C# compiler
package from nuget.org into `%LOCALAPPDATA%\HeartRatePresenceBuild`. Pushing a `v*` tag makes GitHub Actions build the exe
and attach it to a release.

## License

MIT
