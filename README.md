# SteelSeries GG Plugin for Touch Portal
![](https://img.shields.io/github/downloads/DataNext27/TouchPortal_SteelSeriesGG/total) <a href='https://ko-fi.com/M4M2VL6WW' target='_blank'><img height='20' style='border:0px;height:20px;' src='https://storage.ko-fi.com/cdn/brandasset/kofi_button_dark.png' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a> </br> 
This plugin allows you to control to control SteelSeries GG Sonar with Touch Portal<br>
/!\ This plugin allows you to control Sonar and only Sonar for the moment! </br>
/!\ May not be supported on Linux and Mac

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
   - If not already installed on your pc, install [.NET Runtime 7.0](https://dotnet.microsoft.com/en-us/download/dotnet/7.0/runtime?cid=getdotnetcore&os=windows&arch=x64)
2. Download the [latest version](https://github.com/DataNext27/TouchPortal_SteelSeriesGG/releases/latest) of the plugin 
3. Open Touch Portal
   - Click the settings button
   - Click import plugin
   - Find the plugin file you've just downloaded and open it
4. Wait a bit till it finish loading
5. Restart Touch Portal
6. Now start setting up buttons or sliders

## Plugin Capabilities
### Actions
 - Change Config
 - Change Streamer mode
 - Mute / Unmute
 - Change Redirections Devices
 - Enable / Disable Monitoring and Steaming Redirections for Streamer mode

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
- Redirections State
  - Values: Enabled, Disabled
  - Note: Can be changed in settings
 
## Settings
 - Muted States Names
   - Values: text
   - Default: Muted,Unmuted
   - How To Use: {Muted Text},{Unmuted Text} (the "," is required)
   - Note: Just for customize state in button text
 - Redirection States Names
   - Values: text
   - Default: Enabled,Disabled
   - How To Use: {Enabled Text},{Disabled Text} (the "," is required)
   - Note: Just for customize state in button text
  
## ChangeLog
```
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
