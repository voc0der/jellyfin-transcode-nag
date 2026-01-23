# Jellyfin Transcode Nag Plugin

A Jellyfin plugin that intelligently nags users when they're transcoding due to **unsupported formats or codecs**, while allowing bitrate-based transcoding to pass through without harassment.

## Why This Plugin?

Transcoding uses server CPU/GPU resources and can reduce quality. However, not all transcoding is bad:
- **Bad transcoding** (this plugin nags): User's client doesn't support the video codec, container, or audio format
- **OK transcoding** (this plugin allows): User manually reduced bitrate for bandwidth reasons

This plugin only bothers users when they could fix the issue by using a better client (like mpv, VLC, or Jellyfin Media Player).

## Features

- Monitors active playback sessions
- Detects transcoding reasons using Jellyfin's `TranscodeReasons` API
- Only nags when transcoding is due to format/codec incompatibility
- Configurable check interval, message text, and timeout
- Web UI configuration page
- Logging support for debugging

## Transcoding Reasons That Trigger Nags

The plugin nags when transcoding is caused by:
- Container not supported
- Video/Audio codec not supported
- Video profile, level, resolution, bit depth, or framerate not supported
- Audio channels, profile, or sample rate not supported
- Subtitle codec not supported
- Anamorphic or interlaced video not supported
- Reference frames not supported
- Video range type not supported

## Transcoding Reasons That DON'T Trigger Nags

- Bitrate limiting (user choice)
- Unknown/unspecified reasons (to avoid false positives)

## Installation

### Method 1: Download Latest Release (Recommended)

1. Download the latest `Jellyfin.Plugin.TranscodeNag.dll` from the [Releases page](https://github.com/voc0der/jellyfin-transcode-nag/releases/latest)
2. Create a folder called `TranscodeNag` in your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/TranscodeNag/`
   - Windows: `%AppData%\Jellyfin\Server\plugins\TranscodeNag\`
   - Docker: `/config/plugins/TranscodeNag/`
3. Copy the DLL into that folder
4. Restart Jellyfin

### Method 2: Build from source

1. Clone this repository
2. Build the plugin:
   ```bash
   dotnet build --configuration Release
   ```
3. Copy `bin/Release/net8.0/Jellyfin.Plugin.TranscodeNag.dll` to your Jellyfin plugins folder (paths above)
4. Restart Jellyfin

## Configuration

1. Navigate to **Dashboard** → **Plugins** → **Transcode Nag**
2. Configure:
   - **Nag Message**: Customize the message shown to users
   - **Check Interval**: How often to check sessions (default: 60 seconds)
   - **Message Timeout**: How long the message displays (default: 10000 ms)
   - **Enable Logging**: Log when nag messages are sent

## How It Works

1. Every `CheckIntervalSeconds`, the plugin checks all active playback sessions
2. For each session that's transcoding video:
   - Checks the `TranscodeReasons` flags
   - If any "NotSupported" flags are set, sends a nag message
   - If only bitrate limiting (no flags), does nothing
3. Tracks which sessions have been nagged to avoid spamming

## Development

Built with:
- .NET 8.0
- Jellyfin.Controller 10.9.0
- Jellyfin.Model 10.9.0

### Automated Releases

Every commit to `main` automatically:
1. Builds the plugin
2. Auto-increments the version (semantic versioning)
3. Generates a changelog from commit messages
4. Creates a GitHub release with the DLL attached

Check the [Releases page](https://github.com/voc0der/jellyfin-transcode-nag/releases) for all versions and changelogs.

## License

MIT License

## Contributing

Issues and pull requests welcome!
