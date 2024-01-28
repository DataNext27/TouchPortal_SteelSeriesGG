# SteelSeries GG Plugin for Touch Portal
This plugin allows you to control to control SteelSeries GG Sonar with Touch Portal<br>
/!\ This plugin allows you to control Sonar and only Sonar for the moment!

- [SteelSeries GG Plugin for Touch Portal](#steelseries-gg-plugin-for-touch-portal)
  - [Installation](#installation) 
  - [Plugin Capabilities](#plugin-capabilities)
    - [Actions](#actions)
    - [Sliders](#sliders)
    - [States](#states)
  - [Settings](#settings)
  - [Changelog](#changelog)
  - [Dependencies](#dependencies)
  - [Authors](#authors)

## Installation
1. Download and install [Npcap](https://npcap.com/#download) (Used by the plugin to discuss with Sonar)
2. Download the [latest version](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/tag/1.0.0) of the plugin 
3. Open Touch Portal
   - Click the settings button
   - Click import plugin
   - Find the plugin file you've just downloaded and open it
4. Wait a bit till it finish loading
5. Now start setting up buttons or sliders

## Plugin Capabilities
### Actions
 - Change Config
 - Change Streamer mode
 - Mute / Unmute
 - Change Redirections Devices

### Sliders
 - Change Volume
 - Change ChatMix Balance
   
### States
 - Mode
   - Values: Classic/Streamer mode
 - Volume
   - Values: Chosen virtual device volume
   - Note: Volume is from 0 to 100
 - Mute
   - Valid Values: Muted, Unmuted
   - Note: Can be changed in settings
 - ChatMix Balance
   - Values: -1 to 1
 - ChatMix State
   - Values: True/False
   - Note: True if game and chat redirections devices are the same
 - Redirections Devices
   - Value: Name of the selected redirection device of the chosen virtual device
- Configs
  - Values: Chosen virtual device selected config
 
## Settings
 - Muted States Names
   - Values: text
   - Default: Muted,Unmuted
   - How To Use: {Muted Text},{Unmuted text} (the "," is required)
   - Note: Just for customize state in button text
  
## ChangeLog
```
v1.0.0
  - Control Volumes
  - Mute virtual devices
  - Control ChatMix
  - Change Profiles
  - Change redirections devices
  - Change Mode
```

## Dependencies
 - [TouchPortal-CS-API](https://github.com/mpaperno/TouchPortal-CS-API)
 - [Npcap](https://npcap.com/)
 - [sharppcap](https://github.com/dotpcap/sharppcap)
 - [Packet.Net](https://github.com/dotpcap/packetnet)

## Authors
 - Made by DataNext

Thanks to:
 - Touch Portal Creators for Touch Portal App
 - [mpaperno](https://github.com/mpaperno) for the Touch Portal C# API
 - [dotpcap](https://github.com/dotpcap) team for sharppcap and Packet.Net
