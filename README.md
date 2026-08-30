# SteelSeries GG Plugin for Touch Portal
[![GitHub Downloads](https://img.shields.io/github/downloads/DataNext27/TouchPortal_SteelSeriesGG/total?style=for-the-badge&color=6fca00)](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases)
[![GitHub Version](https://img.shields.io/github/v/tag/DataNext27/TouchPortal_SteelSeriesGG?style=for-the-badge&label=Version)](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest)
[![.NET Version](https://img.shields.io/badge/.NET-10.0-512cd4?style=for-the-badge)](https://dotnet.microsoft.com)
[![Ko-fi](https://img.shields.io/badge/Support_me_on-Ko--fi-FF6433?style=for-the-badge&logo=ko-fi)](https://ko-fi.com/M4M2VL6WW)
> As for the V2 Complete Rework Update, you will need to recreate all of your buttons/sliders/states. I'm already sorry about it 😓.

This plugin allows you to control SteelSeries GG Sonar with Touch Portal<br>
⚠️ This plugin allows you to control Sonar and only Sonar for the moment! </br>
⚠️ Windows only (Sonar itself is Windows only)

- [SteelSeries GG Plugin for Touch Portal](#steelseries-gg-plugin-for-touch-portal)
    - [Installation](#installation)
    - [Plugin Capabilities](#plugin-capabilities)
        - [Actions](#actions)
        - [Sliders](#sliders)
        - [States](#states)
        - [Events](#events)
    - [Settings](#settings)
    - [Changelog](#changelog)
    - [FAQs](#faqs)
    - [Dependencies](#dependencies)
    - [Authors](#authors)

## Installation
1. Download the [latest version](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest) of the plugin
    - No need to install .NET anymore, everything is included in the plugin
2. Open Touch Portal
   - Click the Quick Actions button
   - Click import plugin
   - Find the plugin file you've just downloaded and open it
3. Wait a bit till it finish loading
4. Restart Touch Portal
5. Now start setting up buttons or sliders

## Plugin Capabilities
### Actions
- Switch mode (Classic ↔ Streamer)
- Set mode
- Mute/Unmute/Toggle a channel (Classic)
- Mute/Unmute/Toggle a channel on a mix (Streamer)
- Set channel config (the audio configs list follows the chosen channel)
- Set device (Classic) (the devices list follows the chosen channel)
- Set device (Streamer) (Personal Mix, Stream Mix, or the Streamer Mic)
- Toggle channel on mix (enable/disable a channel on the Personal or Stream mix)
- Toggle audience monitoring ("hear what your audience hears")
- Route active window (sends the audio of the focused window to a channel)
- Route application (same, by picking the app in a list)

### Sliders
- Volume (Classic)
    - Note: also works as a button (set a fixed volume)
- Volume (Streamer)
    - Note: per mix (Personal/Stream), also works as a button
- ChatMix Balance
    - Note: sliders follow changes made from Sonar, hardware wheels or Windows volume keys in real time

### States
States are sorted by groups in the Touch Portal state selector:
- Volumes / Mutes (one state per channel, per mix)
    - Groups: Classic, Personal Mix, Stream Mix
    - Values: volume is from 0 to 100; mute text can be changed in settings
- General
    - Values: Mode (Classic/Streamer mode), ChatMix Balance (-100 to 100), ChatMix State, Last Audio App, Connection
- Configs
    - Values: the selected audio config of each channel
- Devices
    - Values: the device name of each classic channel, of both streamer mixes and of the streamer mic
- Mixes
    - Values: whether each channel is enabled on the Personal/Stream mix, plus Audience Monitoring
- Audio Activity
    - Values: whether audio is playing on each channel, plus the apps routed to each channel (e.g. "Brave, Spotify")
- Internal (event triggers)
    - ⚠️ These states are plumbing for the events below, don't use them directly

### Events
There are three ways to react to Sonar changes, from simplest to most powerful:
1. **Plugin events** (in the Events tab): "On volume changed", "On channel muted/unmuted", "On mode changed", "On config changed", "On device changed", "On channel enabled/disabled on mix", "On audio started/stopped playing", "On audience monitoring changed", "On connection changed". Each has a dropdown to pick which channel/mix you care about, or "Any" to react to all of them.
2. **Touch Portal's native "When the plug-in state changes" event**, pointed at any state above, if you want to compare a state to a precise value.
3. **"On ChatMix changed"** carries the new balance and state as *local states*: inside the event, click the tag icon of a text field (or use the "If Statement (Extended)" action) to access "ChatMix balance" and "ChatMix state" values directly.

## Settings
All settings customize how states are displayed in your button texts. Each takes two values separated by a comma: `{On text},{Off text}` (the "," is required).
- Mute States Text
    - Default: Muted,Unmuted
- Mix States Text
    - Default: On,Off
- ChatMix State Text
    - Default: Enabled,Disabled
- Audience Monitoring Text
    - Default: Enabled,Disabled
- Audio Activity Text
    - Default: Playing,Silent
- Connection Text
    - Default: Connected,Disconnected

Note: events always compare against the default (internal) values, so changing these settings never breaks your existing events.

## Changelog
```
v2.0.0
  - Complete rework of the plugin (you will need to recreate your buttons/sliders)
  - No more admin rights, no more Npcap, no network tricks
  - .NET is now included in the plugin, nothing to install
  - New actions: route an app or the active window to a channel, control the
    Streamer Mic device, toggle channels on the streamer mixes
  - Real two-way sync: sliders and states follow Sonar, hardware wheels and
    Windows volume keys in real time
  - 13 events to react to any Sonar change (volume, mute, mode, config,
    device, mixes, audio activity, connection...)
  - Devices plugged/unplugged while Touch Portal runs appear in the lists
    automatically
  - The plugin notifies you in Touch Portal when a new version is available
  - New log file (log.txt) next to the plugin executable to help solving issues
v1.2.2
  - Added support for latest .NET version (Roll Forward)
v1.2.1
  - Added new logging system
v1.2.0
  - New system to communicate with SteelSeries
  - Npcap is now deprecated to use with the plugin
v1.1.5
  - Fixed a crash that sometimes happened when opening SteelSeries GG
v1.1.4
  - Bug fixes
  - Added more states for Monitoring and Streaming
  - Revamped some states names because it wasn't readble
v1.1.3
  - Attempt to fix crashes
  - Revamped the States names
v1.1.2
  - Fixed bugs causing plugin to crashs
v1.1.1
  - Fixed some bugs
  - Added a toggle button for the Streamer mode to listen what your audience hears
  - Added states for Audience Monitoring
  - Added settings for Audience Monitoring
v1.1.0
  - Fixed severals bugs
  - Added more control for Streamer mode
    - Enable/Disable Monitoring and Steaming Redirections
    - New states for the redirections
    - New settings for the redirections
v1.0.2
  - Fixed a bug for streaming mute
v1.0.1
  - Fixed error E3081
v1.0.0
  - Control Volumes
  - Mute virtual devices
  - Control ChatMix
  - Change Profiles
  - Change redirections devices
  - Change Mode
```

## FAQs
- **I'm coming from V1, why are my buttons broken?**</br>
  V2 is a complete rework: everything (actions, states, sliders) has new identifiers, so Touch Portal can't match your old buttons to the new plugin. You will have to recreate them. In exchange you get events, real-time sync, app routing and a plugin that doesn't need admin rights.

- **Does it still ask for admin rights (UAC)?**</br>
  No. V2 talks to Sonar through its local API, the same way the SteelSeries GG window does. No admin, no Npcap, no network capture.

- **Why some actions doesn't seem to work/update on Sonar?**</br>
  This is probably a Sonar graphical bug. Actually, there are lot of graphical bugs on Sonar which I can't fix. But you should be able to use the plugin like it is intended.</br>
  To verify if the plugin is working, you can either push one of your buttons and close the SteelSeries window and then reopening it to see if it actually worked/updated.

- **I got an error, what should I do?**</br>
  You can try restarting Touch Portal, if it doesn't fix the problem, go [check the issues](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/issues?q=is%3Aissue) or [create an issue](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/issues/new).</br>
  To create an issue, make sure to give enough informations such as describing the problem, windows version, plugin version, and **attach the `log.txt` file** (and `log-old.txt` if present) found in the plugin folder: `%APPDATA%\TouchPortal\plugins\TPSteelSeriesGG\`</br>
  You can also go on the [Touch Portal Discord](https://discord.gg/MgxQb8r) to ask for help

- **How do I know when an update is available?**</br>
  The plugin checks once at startup and shows a Touch Portal notification when a new version is out, with a button to open the download page.

## Dependencies
- [TouchPortal-CS-API](https://github.com/mpaperno/TouchPortal-CS-API)
- [SteelSeries-NET-API](https://github.com/DataNext27/SteelSeries-NET-API)

## Authors
- Made by DataNext

Thanks to:
- Touch Portal Creators for Touch Portal App
- [mpaperno](https://github.com/mpaperno) for the Touch Portal C# API