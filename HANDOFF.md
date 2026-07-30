# 🧭 HANDOFF / CONTINUITY NOTES — My Music (Sangeet)

**Purpose of this file:** a detailed reference so work can resume even if the chat/session is lost.
It records the full state of the project, every important decision, how to run everything, and — most
usefully — **every bug we already hit and how we fixed it**, so we don't re-diagnose them.

> Companion docs: repo-root `README.md` (backend overview + API reference), `MusicWebsiteFrontEnd/README.md`
> (frontend), `HOSTING.md` (expose the app to the internet). This file is the "why + history + gotchas" layer.
>
> **Split hosting (current, 2026-07-29):** frontend and backend are **separate apps on separate
> ports** and can be deployed to **different servers**. No port is hard-coded any more:
> the API reads `BACKEND_HOST`/`BACKEND_PORT` from `MusicWebsite/MusicWebsite/.env` (loaded by
> `Extensions/DotEnv.cs` before the host builder), and Vite reads `VITE_PORT`/`VITE_PREVIEW_PORT`
> from `MusicWebsiteFrontEnd/.env`. They are linked by `VITE_API_URL` (frontend → API address) and
> `FRONTEND_ORIGINS` (backend → CORS allow-list). Run both with `./serve-all.ps1`, or one at a time
> with `./start-backend.ps1` / `./start-frontend.ps1`. See `HOSTING.md`.
>
> *Previous single-origin mode* (backend served the built frontend from `wwwroot`) is still available:
> set `SERVE_FRONTEND=true` in the backend `.env`, build the frontend with an empty `VITE_API_URL`,
> and copy `dist/*` into `wwwroot`.
>
> **Database migrated to PostgreSQL/Supabase (2026-07-29):** the app no longer uses SQL Server.
> The 6 tables and all 32 stored procedures were ported to PostgreSQL (`db/postgres/01_schema.sql`,
> `02_functions.sql`) and all 40 rows copied with `tools/DbMigrator`. Only the Infrastructure layer
> changed — `NpgsqlConnectionFactory`, `PostgresErrorTranslator`, and a `RepositoryBase` that calls
> functions as `SELECT * FROM fn(p_x => @X)`. Read `db/postgres/README.md` before touching the data
> layer; it explains citext (case-insensitivity), SQLSTATE error codes, and the pagination split.
>
> **Deployed to Render (2026-07-30):** API at `sangeet-api-9m9d.onrender.com` (Docker, Singapore,
> health check `/health`), frontend at `sangeet-web.onrender.com` (static site). Two GitHub repos:
> `Sangeet_Backend` and `Sangeet_Frontend`. All secrets live in Render environment variables —
> `appsettings.json` is gitignored and is NOT in the image. See `DEPLOYMENT.md`.
>
> **YouTube import requires a proxy — read `DEPLOYMENT.md` §5a before touching it.** YouTube blocks
> datacenter IPs, so the hosted server fails on *every* video with "Sign in to confirm you're not a
> bot", including public ones; the same link works from a home connection. Neither updating yt-dlp
> (it was 18 months stale) nor installing Deno (its JS runtime) fixed it on their own — both were
> necessary but not sufficient. A free datacenter proxy also failed (`429`, then blocked). What
> works is `Youtube__UseProxy=true` plus a `Youtube__ProxyUrl` on a non-blocked IP. When it breaks
> again, change the proxy endpoint first; bump `YTDLP_VERSION` second. Switching to
> `YoutubeExplode` does **not** help — the block is about the IP, not the extractor.
>
> `Jwt:Key` has been set to a strong random secret. To expose online use a **tunnel** (Cloudflare
> quick tunnel / ngrok) — see HOSTING.md.
> **Keep the "Current state" and "Backlog" sections updated as work continues.**

Last updated: 2026-07-29.

---

## 0. TL;DR — where we are right now

- **Backend**: ASP.NET 8 Web API, Clean Architecture (4 projects), **Dapper + PostgreSQL PL/pgSQL functions** (Supabase), JWT+BCrypt auth, **Backblaze B2** file storage. ✅ Fully working, verified end-to-end.
- **Frontend**: React + TypeScript **Vite PWA** in `MusicWebsiteFrontEnd/`. ✅ Builds clean; all features implemented. Not yet visually screenshotted by me (the connected Chrome was on another device).
- **Runs on**: backend `http://localhost:5000` (+ LAN IP), frontend `http://localhost:5173`.
- **Everything a user does works**: register, login, upload MP3 → B2, list/search songs, playlists, playback.

---

## 1. Repository layout

```
Music Website/                         ← git repo root
├── README.md                          ← user-facing overview + full API reference
├── HANDOFF.md                         ← THIS FILE (continuity notes)
├── .gitignore                         ← ignores appsettings.json, bin/obj, node_modules, etc.
├── Music Streaming Platform.docx      ← original requirements (Technical Design)
├── Explanation_and_Feasibility_Plan.docx
│
├── MusicWebsite/                      ← BACKEND solution folder
│   ├── MusicWebsite.sln
│   ├── MusicWebsite.Domain/           ← entities only (no deps)
│   ├── MusicWebsite.Application/      ← DTOs, interfaces (ports), services, use-cases
│   ├── MusicWebsite.Infrastructure/   ← Dapper repos, SQL, JWT, BCrypt, B2 storage
│   ├── MusicWebsite/                  ← Web API host (controllers, Program.cs, appsettings)
│   └── publish/                       ← Release build output (dotnet publish -c Release)
│
└── MusicWebsiteFrontEnd/              ← FRONTEND (Vite React TS PWA)
    ├── src/  (api, context, components, pages, ...)
    ├── .env  (VITE_API_URL)
    └── README.md
```

Clean-architecture dependency rule (backend): **Api → Infrastructure → Application → Domain** (inward only).

---

## 2. Environment & credentials (the concrete facts)

| Thing | Value |
|-------|-------|
| **Database** | **PostgreSQL 17 on Supabase**, project `<project-ref>`, region `ap-southeast-1`, DB `postgres`. Connect via the **Session Pooler** `aws-0-ap-southeast-1.pooler.supabase.com:5432`, user `postgres.<project-ref>` (the direct `db.<ref>.supabase.co` host is IPv6-only). Old SQL Server DB still exists untouched as a fallback. |
| **Backend port** | **5000** by default, plain HTTP, all interfaces — set by `BACKEND_HOST`/`BACKEND_PORT` in `MusicWebsite/MusicWebsite/.env` (nothing hard-coded in `Program.cs`) |
| **Frontend port** | **5173** dev / **4173** preview by default — set by `VITE_PORT`/`VITE_PREVIEW_PORT` in `MusicWebsiteFrontEnd/.env` |
| **How they connect** | frontend `VITE_API_URL` → API URL; backend `FRONTEND_ORIGINS` → CORS allow-list |
| **This PC's LAN IP** | `192.168.5.172` (may change via DHCP) |
| **Backblaze B2 bucket** | `sangeet-audio`, **private** (`allPrivate`) |
| **B2 region / S3 endpoint** | `us-east-005` / `https://s3.us-east-005.backblazeb2.com` |
| **B2 account id** | `4c5aed6f43d0`, bucketId `a4ec35eaee0d86ef94f30d10` |
| **Secrets location** | ALL secrets (DB conn, JWT key, B2 KeyId/ApplicationKey) live in `MusicWebsite/MusicWebsite/appsettings.json`, which is **gitignored** |
| **B2 key note** | The B2 app key was pasted in plaintext during setup → **should be rotated** in Backblaze, then update `appsettings.json` |

**Config precedence reminder:** in Development, .NET user-secrets *override* `appsettings.json`. We deliberately
moved B2 creds OUT of user-secrets INTO `appsettings.json` and cleared user-secrets — do **not** re-add
them to user-secrets or they'll shadow the file.

---

## 3. Database — what actually exists

5 tables (all PK = `uniqueidentifier`, GUIDs generated in **C#**, not SQL):

- **Account** — credentials: AccountId, Email, PasswordHash, IsActive, Created/Updated
- **Users** — profile (1:1 with Account): UserId, AccountId(FK), UserName, FullName, ProfileImageUrl
- **Songs** — **global** library: SongId, SongName, SongUrl, ImageUrl, DurationInSeconds, Priority, IsDeleted (soft delete)
- **Playlists** — per-account: PlaylistId, AccountId(FK), PlaylistName
- **PlaylistSongs** — join: PlaylistSongId, PlaylistId(FK), SongId(FK)

25 stored procedures (`procAccount*`, `procUser*`, `procSong*`, `procPlaylist*`, `procPlaylistSong*`).
They use `THROW` with codes **50001–50044**; Infrastructure's `SqlErrorTranslator` maps those to HTTP
**409/404/400**. Login proc returns the hash; **BCrypt verify happens in C#** (`AuthService`).

**The ONE proc we added:** `procUserGetByAccountId` (mirrors `procUserGetById` but filters by AccountId).
Needed so login / `/api/users/me` can resolve the profile from the JWT's accountId. Only DB change made.

**Key storage detail:** `Songs.SongUrl` / `ImageUrl` store the **B2 object key** (e.g. `songs/<guid>.mp3`),
NOT a full URL. On read, the API converts the key → a short-lived **presigned URL**. Values that are already
`http(s)://…` are passed through unchanged (backward-compatible with the old "paste a URL" path).

---

## 4. Big decisions (and why they differ from the .docx)

1. **PostgreSQL on Supabase** (migrated 2026-07-29 from the original SQL Server build). The 32 T-SQL
   stored procedures are now PL/pgSQL functions with the same names, behaviour and error codes;
   only the Infrastructure layer changed. See `db/postgres/README.md`.
2. **Songs are global + name-search only.** No owner column, no Artist/Album/Genre. Owner chose
   "build to the DB as-is" over the docs' per-user + rich search.
3. **File storage = Backblaze B2** (not Supabase). Private bucket + presigned URLs (see §3).
4. **Secrets in `appsettings.json` (gitignored)**, per owner's preference for one editable file. Template =
   `appsettings.example.json` (committed).
5. **No HTTPS redirection**, LAN-friendly CORS — see the gotchas below.

---

## 5. ⚠️ GOTCHAS WE ALREADY HIT (don't re-diagnose these)

### 5.1 "Page not loading" on `http://IP:5000` → was HTTPS redirect
`app.UseHttpsRedirection()` was 307-redirecting every request to `https://…:7062` (port from the old
launchSettings HTTPS profile), but nothing listens on 7062 → connection refused.
**Fix:** removed `app.UseHttpsRedirection()` from `Program.cs`. The app serves plain HTTP on 5000 on purpose.
(Swagger appeared to work because `UseSwagger` sits before the redirect in the pipeline.)

### 5.2 An API has **no homepage**
`http://localhost:5000/` returns **404 — that's normal.** Things to open instead:
- `http://localhost:5000/swagger` → API test UI (Development env only)
- `http://localhost:5173` → the actual app (frontend)

### 5.3 "Port 5000 already in use"
Frontend (5173) and backend (5000) do **NOT** clash. This error = a **second backend** was started while
an old one still held 5000. Only run one backend at a time.
**Free the port:**
```
netstat -ano | findstr :5000
taskkill /F /IM dotnet.exe          # if started via `dotnet run`
taskkill /F /IM MusicWebsite.exe    # if started via the built exe / Visual Studio
```
Note: the running backend may be named **`MusicWebsite.exe`** (app host), NOT `dotnet.exe` — kill the right one.

### 5.4 Build fails: "file is locked by MusicWebsite (PID)"
You can't rebuild while the backend is running (it locks `MusicWebsite.exe`).
**Order matters:** stop the backend FIRST, then build, then run.

### 5.5 Frontend "Cannot reach the server. Is the API running?" → was CORS
Backend was up; the browser was blocked by **CORS** because the app was opened at an origin
(`127.0.0.1:5173` or the LAN IP) that wasn't in the allowed list. A CORS block surfaces in axios as a
network error, which looks like "server down."
**Fix:** `Program.cs` CORS now uses a predicate (`IsOriginAllowed`) that allows any `localhost`,
`127.0.0.1`, or private-LAN (`192.168/10/172.16-31`) origin on any port. Verified all three origins return
the `Access-Control-Allow-Origin` header.

### 5.6 Using the app from a phone
`VITE_API_URL` defaults to `http://localhost:5000` — fine on the PC, but on a phone `localhost` = the phone.
For phone use: set `VITE_API_URL=http://192.168.5.172:5000` in `MusicWebsiteFrontEnd/.env`, restart
`npm run dev`, and allow inbound ports in Windows Firewall (run as admin):
```
New-NetFirewallRule -DisplayName "Sangeet 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
New-NetFirewallRule -DisplayName "Sangeet 5173" -Direction Inbound -Protocol TCP -LocalPort 5173 -Action Allow
```

### 5.7 YouTube import returns 502 "Couldn't reach YouTube" → network blocks youtube.com
The **Import from YouTube** feature (Upload page) needs the **server** to reach `youtube.com`.
On this dev machine (a `@bettercommerce.io` corporate network) YouTube is **firewall-blocked**:
`curl https://www.youtube.com` returns `000` / TLS reset (`SocketException 10054`), while Google,
Backblaze, example.com all work. So extraction fails **here** with HTTP **502** — this is a
**network block, not a code bug** (verified: the request reaches YouTube's servers via
`YoutubeExplode` and is only reset at the TLS layer). It works on any network where YouTube is
reachable (home Wi-Fi, most cloud hosts). Nothing to fix in code. Uses pure-.NET `YoutubeExplode`
(no `yt-dlp`/`ffmpeg` needed). See README §8.1.

### 5.8 `sqlcmd` fails "incorrect QUOTED_IDENTIFIER" on the Songs table
After the dedup migration, `Songs` has **filtered unique indexes** (`UX_Songs_ContentHash`,
`UX_Songs_SourceKey`). SQL Server then requires `SET QUOTED_IDENTIFIER ON` for **any** INSERT/UPDATE/
DELETE on that table. `sqlcmd` defaults it **OFF** → error 1934. **Fix:** always pass **`-I`** to
sqlcmd (`SQLCMD -S ... -I -Q "..."`). The running app is fine — `Microsoft.Data.SqlClient` sets
QUOTED_IDENTIFIER ON by default. Migration script: `db/2026-07-24_dedup_and_voting.sql`.

Added by that migration: `Songs.ContentHash`, `Songs.SourceKey`, table `SongPriority`, procs
`procSongFindDuplicate` / `procSongVoteSet`, and `@AccountId` params on Song get/search procs (now
`ORDER BY Priority DESC`). See README §8.2–8.3.

### 5.10 YouTube import failures → we switched to yt-dlp (robust); YoutubeExplode was flaky
History: the original extractor was `YoutubeExplode` (pure .NET). It works only on a **subset** of
videos and breaks whenever YouTube changes (seen: `GetPlayerResponseAsync 400`, and
`VideoUnavailableException` on plainly-public videos like "Me at the zoo"). Upgrading the package
helped temporarily but the flakiness is inherent to anonymous scraping.

**Current state (2026-07-24):** we added **`YtDlpAudioExtractor`** and made it the default via config
(`Youtube:Provider = "YtDlp"`, `Youtube:YtDlpPath` → `tools/yt-dlp.exe`, gitignored). yt-dlp imports
videos YoutubeExplode couldn't (both "Me at the zoo" and the user's `081bLdQKX-Q` now succeed). It
downloads the audio-only stream (format 140 → `.m4a`, **no ffmpeg needed**). `YoutubeExplode` remains
as a no-binary fallback (`Provider = "YoutubeExplode"`).

- **If imports fail after a future YouTube change:** update yt-dlp — `tools/yt-dlp.exe -U` (or
  re-download the latest release). yt-dlp warns a JS runtime (deno) boosts reliability for a few
  videos; optional — it works without one for nearly all.
- **yt-dlp not found error:** check `Youtube:YtDlpPath` points to the real `yt-dlp.exe`.

### 5.9 `[Authorize(Roles=...)]` always returns 403 → JWT claim remapping
Symptom: a SuperAdmin token gets **403** on `/api/admin/*` even though the login response shows
`"role":"SuperAdmin"`. Cause: `JwtSecurityTokenHandler` **remaps** the short `role` claim to the
long `ClaimTypes.Role` URI on the way in, so `RoleClaimType="role"` no longer matches. **Fix (in
place):** `options.MapInboundClaims = false;` in `AddJwtBearer` (Program.cs) keeps claims exactly as
emitted (`role`, `accountId`, `userId`). Don't remove it.

### RBAC — how to grant SuperAdmin
The app never grants SuperAdmin. Do it directly in SQL, then that user must log in again (the role is
baked into the JWT at login):
```
UPDATE Account SET Role='SuperAdmin' WHERE Email='you@example.com';
```
New sign-ups get `Roles:DefaultRole` from appsettings.json (default `Admin`). Migration for all of
this: `db/2026-07-24c_roles_and_hard_delete.sql` (added `Account.Role`, `Songs.UploadedByAccountId`,
`procSongHardDelete`, `procAccountGetAllWithRole/SetRole/CascadeDelete`). Song delete is now a
**permanent** hard-delete that also removes the Backblaze B2 files. See README §8.4.

---

## 6. How to run everything

### Backend (dev)
```
cd "Music Website/MusicWebsite"
dotnet build MusicWebsite.sln
dotnet run --project MusicWebsite/MusicWebsite.csproj      # → http://localhost:5000
```
- Swagger (Development only): `http://localhost:5000/swagger`
- Set `ASPNETCORE_ENVIRONMENT=Development` to get Swagger; default when published is Production.

### Backend (Release build / publish)
```
cd "Music Website/MusicWebsite"
dotnet publish MusicWebsite/MusicWebsite.csproj -c Release -o publish
cd publish && dotnet MusicWebsite.dll                       # → http://localhost:5000 (Production)
```
⚠️ `publish/` contains `appsettings.json` **with secrets**. Don't zip/share it as-is; for a real deploy,
remove that file and pass values via env vars (`Storage__B2__ApplicationKey`, `ConnectionStrings__MusicDatabase`, etc.).

### Frontend
```
cd "Music Website/MusicWebsiteFrontEnd"
npm install        # first time
npm run dev        # → http://localhost:5173   (open EXACTLY this URL on the PC)
npm run build      # production build → dist/
```

**Golden path to demo:** start backend (5000) → `npm run dev` (5173) → open `http://localhost:5173` →
register → upload an MP3 → play it.

---

## 7. Backend API surface (quick list; full details in README §11)

- `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`
- `GET/PUT/DELETE /api/account`
- `GET /api/users/me`, `PUT /api/users/me`, `GET /api/users`, `GET /api/users/{id}`
- `GET /api/songs?search=`, `GET /api/songs/{id}`, `POST /api/songs` (URL), **`POST /api/songs/upload`** (multipart → B2), `PUT/DELETE /api/songs/{id}`
- `GET/POST /api/playlists`, `GET/PUT/DELETE /api/playlists/{id}`, `GET /api/playlists/{id}/songs`, `POST/DELETE /api/playlists/{id}/songs/{songId}`

All responses use the envelope `{ success, message, data }`. All routes except register/login need
`Authorization: Bearer <jwt>`. Playlist routes enforce ownership (non-owner → 404).

---

## 8. Frontend surface

- **Pages:** Login, Register, Home, Search, Library, PlaylistDetail, Upload, Profile.
- **Contexts:** Auth (JWT + session restore), Player (single `<audio>`, queue, shuffle/repeat/seek/volume,
  auto-refresh of expired presigned URLs), Library (playlists + add-to-playlist modal), Toast.
- **Responsive:** desktop sidebar ↔ mobile bottom-nav; player collapses on phones. **PWA installable.**
- **API layer:** `src/api/client.ts` (axios + JWT interceptor, unwraps envelope, 401 → login),
  `src/api/endpoints.ts`.

---

## 9. Backlog / next steps

1. **Visually verify the frontend in a browser** on this PC (I couldn't — the connected Chrome was on
   another device). Just open `http://localhost:5173`.
2. **Rotate the B2 application key** (it was shared in plaintext) → update `appsettings.json`.
3. **Change-password** — needs a new stored proc (`procAccountUpdate` only changes email/IsActive).
4. Optional: **Artist/Album/Genre** columns + richer search (schema change to `Songs` + procs).
5. Hardening for public deploy: rotate `Jwt:Key`, add refresh tokens, rate limiting, tighten CORS,
   server-side MP3 duration extraction (e.g. TagLibSharp), real HTTPS + re-enable redirect.
6. Optional cleanup: `Program.cs` sets the port two ways (`UseUrls` + `ListenAnyIP`) → harmless
   "Overriding address(es)" warning; can drop one line.

---

## 10. Command cheat sheet

```bash
# Which processes hold the ports?
netstat -ano | findstr ":5000 :5173"

# Stop backend (try both names)
taskkill /F /IM MusicWebsite.exe ; taskkill /F /IM dotnet.exe

# Rebuild + run backend (stop it FIRST if running)
cd "Music Website/MusicWebsite" && dotnet build MusicWebsite.sln
dotnet run --project MusicWebsite/MusicWebsite.csproj

# Frontend
cd "Music Website/MusicWebsiteFrontEnd" && npm run dev

# Inspect the SQL DB (Windows auth)
sqlcmd -S "BC-MANJEETSINGH\SQL25" -E -d MusicDatabase -Q "SELECT name FROM sys.procedures ORDER BY name;"

# Check a B2 upload landed (from the app) — the presigned URL in the API response should download the file.
```
