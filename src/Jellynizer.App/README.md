# Jellynizer App

Flutter companion app for the Jellynizer API.

It lets you:
- Configure and persist the API URL
- Check API health continuously
- Trigger an organize run
- Open or share a `.torrent` file or magnet link with the app and confirm it before downloading
- View all torrents with live download progress
- Browse, rename, move, and delete files in the source folder
- View and manage the organized media library
- Forget move history (movies, shows, seasons, episodes, or batch)
- View disk storage usage
- View live server logs through SSE (`/logs/stream`)

## Requirements

- Flutter SDK (stable)
- Dart SDK compatible with this project (`^3.7.0`)
- A running Jellynizer backend

## Run locally

From [src/Jellynizer.App](src/Jellynizer.App):

1. Install dependencies
	- `flutter pub get`
2. Run the app
	- `flutter run`

## Opening torrents

Android registers the app for `.torrent` files (`application/x-bittorrent` and
`application/octet-stream`), for **magnet links** (`magnet:` scheme) and for shared links
(`text/plain`). So you can:

- open a `.torrent` from a file manager or the browser download notification,
- tap a magnet link in a browser and pick **Jellynizer**,
- or share a `.torrent` file / magnet link into the app.

Either way an in-app confirmation dialog shows the name with a **Cancel** / **Download**
choice. Nothing is sent until you confirm; on confirm the file or link is uploaded to
`POST /torrents/add` or `POST /torrents/add-magnet` and qBittorrent starts downloading it
immediately. The organize job is not triggered by a torrent download.

Magnet links have no file name until metadata is fetched, so the dialog shows the `dn`
(the display name inside the link) or the info hash instead.

Because `application/octet-stream` and `text/plain` are generic types, Android may also
offer Jellynizer for other files and link shares. The app filters these out and only
acts on `.torrent` files and `magnet:` links.

## Viewing torrent progress

**Torrents** in the top-right menu lists every torrent with a progress bar, percentage,
status, transferred size, speed and ETA. The list refreshes every few seconds while the
screen is open; pull down or tap the refresh icon to refresh manually.

## Older servers

If the server runs an older Jellynizer build that predates these features, the app
detects the missing endpoint (HTTP 404) and tells you to update the server instead of
showing a raw error. The same applies when qBittorrent is not configured on the server
(HTTP 503).

## First-time setup

On first launch, enter your Jellynizer API address, for example:
- `192.168.50.200:45263`
- `http://192.168.50.200:45263`

The app stores this value in local preferences. You can clear it via **Reset API URL** in the top-right menu.

## Backend endpoints used

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Connectivity check |
| GET | `/storage-info` | Disk storage info |
| GET | `/logs/stream?tail=...` | Live log streaming via SSE |
| GET | `/library` | Organized media library structure |
| GET | `/browse` | Source folder directory listing |
| POST | `/trigger-job` | Trigger organize job |
| GET | `/torrents` | List torrents with progress |
| POST | `/torrents/add` | Upload a `.torrent` file and start the download |
| POST | `/torrents/add-magnet` | Add a magnet link and start the download |
| POST | `/rename` | Rename file or directory |
| POST | `/move` | Move file or directory |
| POST | `/delete` | Delete files or directories |
| POST | `/forget-movie` | Forget movie history |
| POST | `/forget-show` | Forget all history for a show |
| POST | `/forget-show-season` | Forget history for a show season |
| POST | `/forget-episode` | Forget history for a specific episode |
| POST | `/forget-batch` | Forget history for multiple items |

## App icon

The launcher icon is generated from code, so there is no binary artwork to lose and the
colours/proportions stay editable:

```bash
python3 -m pip install pillow      # once
python3 tool/generate_icon.py      # draws assets/icon/*.png (1024x1024)
dart run flutter_launcher_icons    # writes android/ ios/ web/ icons
```

`tool/generate_icon.py` draws a Jellyfin-style rounded "J" on a teal gradient with a purple
puzzle-piece badge (the puzzle piece marks this as a companion/plugin app). It emits three
sources:

| File | Purpose |
|---|---|
| `assets/icon/app_icon.png` | full icon, teal gradient, RGB with no alpha |
| `assets/icon/app_icon_foreground.png` | Android adaptive foreground layer |
| `assets/icon/app_icon_monochrome.png` | Android 13+ themed (monochrome) icon |

`assets/icon/` is a build input only — it is deliberately **not** listed under `flutter: assets:`
so it never ships inside the app bundle. The `flutter_launcher_icons` section in `pubspec.yaml`
holds the paths and the `#00695C` background colour.

To change the icon, edit the constants at the top of `tool/generate_icon.py` (colours, mark
fractions, badge size) and re-run both commands above.

## Notes

- If no scheme is provided, the app assumes `http://`.
- The app polls health every second while active.
- Log streaming reconnect attempts are throttled to avoid rapid retries.
