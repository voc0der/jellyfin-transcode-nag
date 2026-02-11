# Jellyfin Transcode Nag Plugin

[![Star History Chart](https://api.star-history.com/svg?repos=voc0der/jellyfin-transcode-nag&type=Date)](https://star-history.com/#voc0der/jellyfin-transcode-nag&Date)

A Jellyfin plugin that intelligently nags users when they're transcoding due to **unsupported formats or codecs**, while allowing bitrate-based transcoding to pass through without harassment.

## Why This Plugin?

Transcoding uses server CPU/GPU resources and can reduce quality. However, not all transcoding is bad:
- **Bad transcoding** (this plugin nags): User's client doesn't support the video codec, container, or audio format
- **OK transcoding** (this plugin allows): User manually reduced bitrate for bandwidth reasons

This plugin only bothers users when they could fix the issue by using a better client (like mpv, VLC, or Jellyfin Media Player).

## Features

### Real-Time Playback Monitoring
- Monitors active playback sessions in real-time
- Detects transcoding reasons using Jellyfin's `TranscodeReasons` API
- Select exactly which transcoding reasons should trigger nags
- Defaults to format/codec compatibility reasons (same behavior as previous versions)
- Nags once per video (not per session)
- Configurable delay, message text, and timeout

### Login Nag System
- Tracks transcode history persistently (stored in plugin data directory)
- Nags users on login if they've exceeded a configurable threshold
- Configurable time window (week or month)
- Customizable message with `{{transcodes}}` and `{{timewindow}}` placeholders
- Can be enabled/disabled independently from playback nags
- Auto-cleanup of events older than 30 days

### User Exclusions
- Exclude specific users from **all** nag messages (both playback and login)
- Manage exclusions via the plugin config page (Dashboard → Plugins → Transcode Nag → Manage Excluded Users)
- Unchecked users are excluded; checked users receive nags as normal

### General
- Web UI configuration page
- Optional sidebar page ("Transcode Nag Live") that lists active sessions matching nag criteria
- Logging support for debugging
- Event-driven architecture (no polling)

## Transcoding Reasons

By default, the plugin nags when transcoding is caused by:
- Container not supported
- Video/Audio codec not supported
- Video profile, level, resolution, bit depth, or framerate not supported
- Audio channels, profile, or sample rate not supported
- Subtitle codec not supported
- Anamorphic or interlaced video not supported
- Reference frames not supported
- Video range type not supported

You can now customize this list in the plugin configuration page and choose exactly which reasons should generate alerts.

Reasons that are not selected will not trigger nag events.

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

<details>
<summary>Method 3: Build from Source</summary>

1. Clone this repository
2. Build the plugin:
   ```bash
   dotnet build --configuration Release
   ```
3. Create a versioned folder in your Jellyfin plugins directory (e.g., `Transcode_Nag_1.0.0.0`)
4. Copy `bin/Release/net8.0/Jellyfin.Plugin.TranscodeNag.dll` into that folder
5. Restart Jellyfin

</details>

## Configuration

1. Navigate to **Dashboard** → **Plugins** → **Transcode Nag**
2. Configure the settings:

### Playback Nag Settings
- **Nag Message**: Customize the message shown during playback when transcoding is detected
- **Delay Before Check**: How long to wait after playback starts before checking (1-30 seconds, default: 5)
- **Message Timeout**: How long the message displays in milliseconds (3000-30000 ms, default: 10000)
- **Enable Logging**: Log when nag messages are sent (helpful for debugging)
- **Enable Live Sidebar Page**: Show/hide the "Transcode Nag Live" entry in the left sidebar (refresh dashboard after saving)
- **Playback Trigger Reasons**: Choose which transcoding reasons trigger nag events
  - Use quick actions: **Select All**, **Reset Defaults**, or **Clear All**

### Login Nag Settings
- **Enable Login Nag**: Toggle to enable/disable the login nag feature
- **Login Nag Threshold**: Number of bad transcodes before nagging (1-100, default: 5)
- **Login Nag Time Window**: Check history for the last week or month (dropdown)
- **Login Nag Message**: Customize the message shown on login
  - Use `{{transcodes}}` placeholder for the transcode count
  - Use `{{timewindow}}` placeholder for "week" or "month"
  - Default: "You've transcoded {{transcodes}} videos in the last {{timewindow}} due to unsupported formats. Consider switching to mpv, VLC, or Jellyfin Media Player to improve quality and reduce server load!"

### User Exclusions
- Click **Manage Excluded Users** to open the exclusion modal
- All users are **checked** (included) by default
- **Uncheck** a user to exclude them from all nag messages
- Use quick actions in the modal: **Select All**, **Reset Defaults**, or **Clear All**
- Click **Save** in the modal to apply changes

### Live Sidebar Page
- A **Transcode Nag Live** page can appear in the left sidebar under Plugins
- Visibility is controlled by **Enable Live Sidebar Page** in plugin settings
- It refreshes every 15 seconds and shows only active sessions matching current nag rules
- It respects your selected playback trigger reasons and excluded users

## How It Works

### Playback Monitoring
1. Plugin listens for `PlaybackStart` events from Jellyfin
2. When playback starts, waits `DelaySeconds` for transcoding info to populate
3. Checks the session's transcoding status:
   - Examines `TranscodeReasons` flags
   - Matches those flags against the configured trigger-reason selection
   - If at least one selected reason is present, sends a nag message and records the event
   - If no selected reason matches, does nothing
4. Tracks which videos have been nagged per session (nags once per video, not per session)
5. When playback stops, clears the nag tracking for that video

### Login Nag System
1. Plugin listens for `SessionStarted` events (user login)
2. Queries the persistent event store for the user's transcode history
3. Calculates days based on time window setting (7 for week, 30 for month)
4. If transcode count >= threshold, sends a login nag message
5. Tracks which users have been nagged to avoid duplicate messages during the same session
6. Events older than 30 days are automatically cleaned up to save storage

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT License
