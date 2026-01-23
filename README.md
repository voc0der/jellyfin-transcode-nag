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

### Method 1: Add Plugin Repository (Recommended - Auto-Updates!)

1. In Jellyfin, navigate to **Dashboard** → **Plugins** → **Repositories**
2. Click the **+** button to add a new repository
3. Enter:
   - **Repository Name**: `Transcode Nag`
   - **Repository URL**: `https://raw.githubusercontent.com/voc0der/jellyfin-transcode-nag/main/manifest.json`
4. Click **Save**
5. Go to **Catalog** tab, find "Transcode Nag" and click **Install**
6. Restart Jellyfin

Now you'll get automatic updates whenever a new version is released!

### Method 2: Manual ZIP Installation

1. Download the latest `Transcode_Nag_X.X.X.X.zip` from the [Releases page](https://github.com/voc0der/jellyfin-transcode-nag/releases/latest)
2. Extract the ZIP to your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/`
   - Windows: `%AppData%\Jellyfin\Server\plugins\`
   - Docker: `/config/plugins/`
3. The extracted folder should be named like `Transcode_Nag_1.0.0.1`
4. Restart Jellyfin

### Method 3: Build from Source

1. Clone this repository
2. Build the plugin:
   ```bash
   dotnet build --configuration Release
   ```
3. Create a versioned folder in your Jellyfin plugins directory (e.g., `Transcode_Nag_1.0.0.0`)
4. Copy `bin/Release/net8.0/Jellyfin.Plugin.TranscodeNag.dll` into that folder
5. Restart Jellyfin

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
2. Auto-increments the version (4-part semantic: 1.0.0.X)
3. Generates a changelog from commit messages
4. Creates ZIP with proper Jellyfin folder structure (`Transcode_Nag_X.X.X.X/`)
5. Updates `manifest.json` for plugin repository auto-updates
6. Creates a GitHub release with the ZIP attached

Check the [Releases page](https://github.com/voc0der/jellyfin-transcode-nag/releases) for all versions and changelogs.

**Plugin Repository URL**: `https://raw.githubusercontent.com/voc0der/jellyfin-transcode-nag/main/manifest.json`

## License

MIT License

## Contributing

Issues and pull requests welcome!
