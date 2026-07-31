# 🎵 My Music (Sangeet) — Music Streaming Platform

A web-based music streaming platform where users register, manage a song library, build
playlists, and stream audio. Backend is an **ASP.NET 8 Web API** built in **Clean Architecture**
over a **PostgreSQL (Supabase)** database accessed with **Dapper + PL/pgSQL functions**.

> **This README is also the project's working-context / handoff document.** It records what was
> decided, what is built, what is deliberately deferred, and what to do next — so anyone (human or
> AI assistant) can resume the work without re-reading everything. **Keep the "Status" and
> "Next steps" sections up to date as work progresses.**

---

## 1. Status at a glance

| Area | State |
|------|-------|
| Backend API (auth, account, users, songs, playlists) | ✅ **Done & verified against the live DB** |
| Database (SQL Server, tables + 24 stored procs) | ✅ Pre-existing; 1 proc added (see §7) |
| Clean Architecture (4 projects) | ✅ Done |
| JWT auth + BCrypt + global error handling + Swagger | ✅ Done |
| **File upload (MP3 / cover images)** | ✅ **Done** — Backblaze B2 (private bucket + presigned URLs), see §8 |
| **Import from YouTube link** | ✅ **Done** — extracts audio-only + thumbnail via `YoutubeExplode`, uploads to B2, saves song (see §8.1) |
| **Duplicate prevention** | ✅ **Done** — exact-file dedup (SHA-256) + one-import-per-YouTube-video (see §8.2) |
| **Priority = up/down voting** | ✅ **Done** — one vote per user per song, search ranks by total (see §8.3) |
| **Roles + permanent song delete** | ✅ **Done** — User/Admin/SuperAdmin, delete removes DB row + cloud file (see §8.4) |
| **Artist, category & lyrics** | ✅ **Done** — all filled in automatically on upload; categories create themselves; lyrics are **looked up, never generated** (see §8.5) |
| Change password | ❌ Not possible yet — no stored proc (see §8) |
| Search by artist | ✅ **Done** in §8.5 — `procSongSearch` matches artist. Album/genre search still needs an `album` column. |
| **Frontend (React + TypeScript)** | ✅ **Built** — Vite PWA in `MusicWebsiteFrontEnd/` (see its README) |

Last verified: full build **0 warnings / 0 errors**; register → login → songs → playlists flows
all pass end-to-end.

---

## 2. Requirements source

Two Word documents in the repo root drive the design:

- `Music Streaming Platform.docx` — Technical Design (stack, data model, endpoints, workflows).
- `Explanation_and_Feasibility_Plan.docx` — Feasibility analysis + phased roadmap.

**Important:** the actual implementation intentionally diverges from these docs in three places —
see §6 "Decisions that differ from the docs".

---

## 3. Tech stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET 8 Web API, C# |
| Data access | **Dapper** calling **PL/pgSQL functions** (Npgsql) |
| Database | **PostgreSQL 17 on Supabase** — project `<project-ref>`, database `postgres` (see §5) |
| Auth | JWT access token (`System.IdentityModel.Tokens.Jwt`) + **BCrypt** password hashing |
| API docs | Swagger / Swashbuckle 6.6.2 (with Bearer support) |
| Frontend (planned) | React + TypeScript, React Router, Axios, HTML5 Audio API |

---

## 4. Solution structure (Clean Architecture)

Dependencies point **inward only**: `Api → Infrastructure → Application → Domain`.

```
Music Website/                         ← git repo root (this README lives here)
├── README.md
├── Music Streaming Platform.docx      ← requirements
├── Explanation_and_Feasibility_Plan.docx
└── MusicWebsite/                      ← solution folder
    ├── MusicWebsite.sln
    │
    ├── MusicWebsite.Domain/           ← entities only, ZERO dependencies
    │   └── Entities/  Account, User, Song, Playlist, PlaylistSong
    │
    ├── MusicWebsite.Application/      ← use-cases + contracts (depends on Domain)
    │   ├── Common/        ApiResponse, AppException
    │   ├── DTOs/          Auth, Users, Accounts, Songs, Playlists
    │   ├── Models/        AccountCredentials, TokenResult, StorageResult
    │   ├── Interfaces/
    │   │   ├── Persistence/  I*Repository (ports implemented by Infrastructure)
    │   │   ├── Security/     IPasswordHasher, ITokenService
    │   │   ├── Storage/      IStorageService
    │   │   └── Services/     IAuthService, IUserService, IAccountService, ISongService, IPlaylistService
    │   ├── Services/      AuthService, UserService, AccountService, SongService, PlaylistService
    │   └── DependencyInjection.cs   → services.AddApplication()
    │
    ├── MusicWebsite.Infrastructure/   ← implements Application ports (depends on Application)
    │   ├── Persistence/
    │   │   ├── NpgsqlConnectionFactory.cs     (PostgreSQL connection)
    │   │   ├── PostgresConnectionString.cs    (accepts the postgresql:// URI form)
    │   │   ├── RepositoryBase.cs              (Dapper helpers + error translation)
    │   │   ├── SqlErrorTranslator.cs          (THROW 50001-50044 → HTTP 400/404/409)
    │   │   ├── StoredProcedures.cs            (all proc names as constants)
    │   │   └── Repositories/  Account, User, Song, Playlist, PlaylistSong
    │   ├── Security/   BcryptPasswordHasher, JwtTokenService, JwtSettings
    │   ├── Storage/    BackblazeB2StorageService (real), StubStorageService (fallback), StorageSettings
    │   └── DependencyInjection.cs   → services.AddInfrastructure(config)
    │
    └── MusicWebsite/                  ← Web API host / composition root (depends on App + Infra)
        ├── Controllers/  Auth, Account, Users, Songs, Playlists
        ├── Middleware/    ExceptionHandlingMiddleware  (AppException → JSON envelope)
        ├── Extensions/    ClaimsPrincipalExtensions  (GetAccountId / GetUserId from JWT)
        ├── Program.cs     (DI, JWT, Swagger, CORS, pipeline)
        └── appsettings.json  (connection string, JWT, CORS origins)
```

**Where to add things:** a new feature normally touches, in order — a Domain entity (if new),
an Application DTO + service interface + service, an Infrastructure repository (if it hits the DB),
and an API controller. Register new services/repos in the relevant `DependencyInjection.cs`.

---

## 5. Database

**PostgreSQL 17 on Supabase.** Project ref `<project-ref>`, region `ap-southeast-1`,
database `postgres`, schema `public`. Schema and functions live in `db/postgres/`; the migration
from the old SQL Server database is documented in **`db/postgres/README.md`**.

> **Connection:** use the **Session Pooler** host
> `aws-0-ap-southeast-1.pooler.supabase.com:5432` with username `postgres.<project-ref>`.
> The direct host `db.<ref>.supabase.co` publishes **only an IPv6 address** and is unreachable from
> an IPv4-only network.

Tables and columns are lower-case (PostgreSQL folds unquoted identifiers). The table below keeps the
original PascalCase names for readability — `Account` is `account`, `SongId` is `songid`, and so on.
Dapper matches columns to C# properties case-insensitively, so the entities were unchanged.

*(The previous SQL Server database — `BC-MANJEETSINGH\SQL25`, `MusicDatabase` — still exists,
untouched, as a fallback.)*

### Tables
| Table | Purpose | Notable columns |
|-------|---------|-----------------|
| `Account` | Login credentials | `AccountId` (PK, GUID), `Email`, `PasswordHash`, `IsActive`, `Created/Updated` |
| `Users` | Profile, 1:1 with Account | `UserId` (PK), `AccountId` (FK), `UserName`, `FullName`, `ProfileImageUrl` |
| `Songs` | **Global** song library | `SongId` (PK), `SongName`, `SongUrl`, `ImageUrl`, `DurationInSeconds`, `Priority`, `IsDeleted` (soft delete), `Artist` (`citext`), `CategoryId` (FK), `Lyrics`, `LyricsSynced` — see §8.5 |
| `Category` | Song categories, **created on demand** | `CategoryId` (PK), `Name` (`citext`, UNIQUE), `Created` — see §8.5 |
| `Playlists` | Per-account playlists | `PlaylistId` (PK), `AccountId` (FK), `PlaylistName` |
| `PlaylistSongs` | Join (many-to-many) | `PlaylistSongId` (PK), `PlaylistId` (FK), `SongId` (FK) |

### Stored procedures (25 total)
All GUIDs are generated by the **caller** (C#), not the DB. Procs use `SET NOCOUNT ON`,
`XACT_ABORT`, transactions, and `THROW` with codes **50001–50044**. Insert/Update procs return the
affected row; Delete/Remove procs return `Success` + `Message`.

- **Account:** `procAccountInsert`, `procAccountUpdate`, `procAccountDelete`, `procAccountGetById`, `procAccountLogin`
- **Users:** `procUserInsert`, `procUserUpdate`, `procUserGetById`, `procUserGetByUserName`, `procUserGetAll`, **`procUserGetByAccountId`** ← *added by us, see §7*
- **Songs:** `procSongInsert`, `procSongUpdate`, `procSongDelete`, `procSongGetById`, `procSongGetAll`, `procSongSearch`, **`procSongFindByTitleArtist`**, **`procSongSetLyrics`** ← *added by §8.5*
- **Categories:** **`procCategoryGetOrCreate`**, **`procCategoryGetAll`** ← *added by §8.5*
- **Playlists:** `procPlaylistInsert`, `procPlaylistUpdate`, `procPlaylistDelete`, `procPlaylistGetById`, `procPlaylistGetByAccountId`
- **PlaylistSongs:** `procPlaylistSongAdd`, `procPlaylistSongRemove`, `procPlaylistSongGetByPlaylistId`

**Login flow:** `procAccountLogin` returns the `PasswordHash`; BCrypt verification happens in C#
(`AuthService`), not in SQL.

---

## 6. Decisions that differ from the docs (READ THIS before changing scope)

1. **Database is PostgreSQL on Supabase** (migrated from SQL Server on 2026-07-29). The original
   build used SQL Server with T-SQL stored procedures; those 32 procedures are now PL/pgSQL
   functions with identical behaviour and error codes. See `db/postgres/README.md` for the mapping
   decisions (citext for case-insensitive columns, SQLSTATE-based error codes, named-argument calls).
2. **Songs are a global library, searchable by name only.** No owner column, no Artist/Album/Genre.
   User explicitly chose *"build to the DB as-is"* over the docs' per-user + rich-search design.
3. **File storage is Backblaze B2, not Supabase.** Private bucket `sangeet-audio` via B2's
   S3-compatible API. The DB `SongUrl`/`ImageUrl` columns store the **object key** (not a full URL);
   the API turns keys into short-lived **presigned URLs** on read so the browser streams directly
   from B2 without the bucket being public. Values that are already full http(s) URLs (e.g. the old
   "URL only" path) are passed through unchanged — both styles coexist.

---

## 7. The one database change we made

Added **`procUserGetByAccountId`** (mirrors `procUserGetById` but filters by `AccountId`, joins
`Account` for `Email`). Required so **login** and **`GET /api/users/me`** can resolve a user's
profile from the account id embedded in the JWT. This is the *only* schema/proc change made.

---

## 8. File storage — Backblaze B2 (DONE)

Files live in the **private** B2 bucket `sangeet-audio` (region `us-east-005`), reached through B2's
**S3-compatible API** using the AWS SDK (`AWSSDK.S3`).

- **Upload:** `POST /api/songs/upload` (multipart) → `BackblazeB2StorageService.UploadAsync` puts the
  object under `songs/<guid>.<ext>` (covers under `covers/<guid>.<ext>`) → the returned **object key**
  is saved in `Songs.SongUrl` / `Songs.ImageUrl` via `procSongInsert`.
- **Read/stream:** on every read, `ResolveReadUrl(key)` generates a **presigned GET URL**
  (expiry = `Storage:B2:PresignExpiryMinutes`, default 120 min). The HTML5 player streams straight
  from B2. Bucket stays private.
- **Config:** all storage config — including the B2 `KeyId` / `ApplicationKey` — lives in
  `appsettings.json` under `Storage:B2`. **`appsettings.json` is gitignored**, so it is never
  uploaded to GitHub; a committed `appsettings.example.json` documents the structure with
  placeholders. (Earlier this used .NET user-secrets; we moved it into `appsettings.json` per the
  owner's preference for a single editable file. Note: in Development, user-secrets *override*
  `appsettings.json`, so don't re-add these keys there or they'll shadow the file.)
- **Provider switch:** `Storage:Provider` = `BackblazeB2` (real) or anything else → `StubStorageService`
  (upload throws 501, URLs pass through). Selection logic in `Infrastructure/DependencyInjection.cs`.
- **Validation:** audio limited to `.mp3/.m4a/.aac/.wav/.ogg/.flac`, covers to
  `.jpg/.jpeg/.png/.webp/.gif`; request size capped at 100 MB. If DB insert fails after upload, the
  uploaded blob(s) are deleted (no orphans).
- **Verified:** upload → presigned URL → download returns byte-identical file (MD5 match).

### Deleting really deletes — the B2 versioning trap

> ⚠️ **A plain S3 `DeleteObject` does not free any space on a B2 bucket.** B2 buckets keep **all
> versions** by default, and on a versioned bucket a keyless delete only writes a **delete marker**:
> the file vanishes from listings and from the app, while the bytes stay and keep costing storage.

This bit us for real. A SuperAdmin deleted a song from the UI, the database row was hard-deleted
correctly — and the file was still in the bucket. An audit found **15.4 MB held in 16 hidden
versions behind 17 delete markers**, going back weeks.

`BackblazeB2StorageService.DeleteAsync` therefore **enumerates every version of the key and deletes
each one by version id**, rather than issuing one keyless delete. It behaves identically on an
unversioned bucket (which simply reports a single version), so nothing depends on the bucket's
setting. If listing versions is refused — the B2 application key needs the *listFiles* capability —
it logs a warning and falls back to the old single delete, which is no worse than before and never
blocks the user's delete.

**Verified end-to-end:** upload → probe B2 (`1 version, 84,697 bytes`) → `DELETE /api/songs/{id}` →
probe again (`0 versions, 0 bytes, no delete marker`).

A B2 lifecycle rule of *"Keep only the last version"* is still worth setting in the console — it
mops up any historical versions that predate this fix.

> ⚠️ **Known gap — deleting a *user* still orphans their files.** `AdminService.DeleteAccountAsync`
> calls `procAccountCascadeDelete` directly and never touches `IStorageService`, so an account
> deletion removes that user's song rows while leaving every audio file and cover in the bucket
> (8 files / 9.82 MB found orphaned this way). `DELETE /api/songs/{id}` is clean; account deletion
> is not. Fix: collect the account's songs and delete their blobs before the cascade.

> ⚠️ The B2 application key was shared in plaintext during setup — consider rotating it in Backblaze,
> then edit `KeyId` / `ApplicationKey` in `MusicWebsite/appsettings.json` and restart the API.

## 8.1 Import from YouTube (DONE)

Users can add a song by pasting a **YouTube link** instead of a file. Two interchangeable extractors
(picked by `Youtube:Provider`): **`yt-dlp`** (default, robust — needs `yt-dlp.exe`, downloads audio-only
`.m4a`, **no ffmpeg**) or **`YoutubeExplode`** (pure-.NET fallback, no binaries but less reliable). See
DOCUMENTATION §9.3.

- **Flow:** `Upload` page → "Import from YouTube" panel → paste link → **Process** (preview) →
  **Extract audio & upload**.
- **`POST /api/songs/youtube/preview`** `{ url }` → fast metadata only (title, author, duration,
  thumbnail URL) — no download. Used to confirm the right video.
- **`POST /api/songs/youtube`** `{ url, songName?, priority? }` → downloads **only the audio-only
  stream** (never the full video) to a **temp file**, uploads it to B2 as `.m4a` (plays natively in
  browsers) + the highest-res thumbnail as the cover, saves the song, then **deletes the temp file**.
  Rolls back uploaded blobs if the DB insert fails.
- **Server footprint:** the temp file is disposed at the end of the request (`IAsyncDisposable`), so
  nothing accumulates on disk — same as the normal upload path.
- **Layers:** `IYoutubeAudioExtractor` (Application port) → `YoutubeExplodeAudioExtractor`
  (Infrastructure) → `SongService.ImportFromYoutubeAsync` / `GetYoutubePreviewAsync`.
- ⚠️ **Requires the server to reach `youtube.com`.** Many **corporate/office networks block YouTube**
  → extraction returns HTTP **502** "Couldn't reach YouTube…". It works fine on any network where
  YouTube is reachable. (Pulling audio from YouTube may also conflict with YouTube's ToS — fine for a
  personal library, note it before going public.)

## 8.2 Duplicate prevention (DONE)

Stops the same audio being stored twice (saves B2 cost). Keyed on **content**, not name (names get
misspelled or legitimately repeat).

- **File uploads** — the API computes a **SHA-256** of the audio bytes, checks `Songs.ContentHash`
  *before* uploading to B2, and returns **409 "This exact audio file is already in the library."**
  if it exists. No wasted upload.
- **YouTube imports** — the canonical **video id** is parsed locally and stored as
  `Songs.SourceKey = 'youtube:<id>'`. If that video was already imported, the existing song is
  returned and the whole download+upload is **skipped** — one import per video, regardless of title.
- Enforced by unique **filtered** indexes (`UX_Songs_ContentHash`, `UX_Songs_SourceKey`, only over
  live rows). Only catches byte-identical files; a different encoding of the same song has a
  different hash (audio-fingerprinting would be a later upgrade).

## 8.3 Priority = community up/down voting (DONE)

`Songs.Priority` is a **community score**, not a manual field. Each user casts **one** vote per song
(up = +1, down = −1); the higher the total, the higher the song ranks in search/lists.

- **`SongPriority`** table = the collection of votes, `UNIQUE(SongId, AccountId)` enforces one value
  per user per song. Re-tapping your current vote clears it.
- `procSongVoteSet` upserts the vote and recomputes `Songs.Priority = SUM(votes)` transactionally;
  `procSongGetAll` / `procSongSearch` / `procSongGetById` now `ORDER BY Priority DESC` and return the
  caller's own vote as `MyVote` (they take an optional `@AccountId`).
- **Endpoint:** `POST /api/songs/{songId}/vote` body `{ value: 1 | -1 | 0 }`.
- **Frontend:** up/down control (`VoteControl`) on Home cards and Search rows; the manual Priority box
  was removed from Upload (priority is earned, not typed).
- **Migration:** `db/2026-07-24_dedup_and_voting.sql` (idempotent). ⚠️ Run it with `sqlcmd -I`
  (filtered indexes need `QUOTED_IDENTIFIER ON`; the app's SqlClient already sets this).

## 8.4 Roles + permanent song delete (DONE)

Role-based access control on `Account.Role`, carried in the JWT (`role` claim) and enforced with
`[Authorize(Roles=...)]` + service checks.

- **Roles:** `User` (listen + build playlists, **cannot delete songs**), `Admin` (upload + **delete
  songs they uploaded**), `SuperAdmin` (**delete any song/account**, manage roles, view any user's
  playlists).
- **Default role for new sign-ups** is config-driven: `Roles:DefaultRole` in `appsettings.json`
  (`"Admin"` by default; `"User"` also allowed). **SuperAdmin is NEVER granted through the app** —
  set it by hand: `UPDATE Account SET Role='SuperAdmin' WHERE Email='you@example.com';`
- **Song ownership:** every upload/import/create stamps `Songs.UploadedByAccountId`. Legacy songs
  (no owner) can only be deleted by a SuperAdmin.
- **Delete is permanent + frees cloud space:** `DELETE /api/songs/{id}` removes the **B2 audio +
  cover**, then hard-deletes the row and its playlist links + votes (`procSongHardDelete`).
  (This replaced the old soft-delete.)
- **SuperAdmin console** (`/admin` in the UI, `api/admin/*` on the server): list users, change a
  role (User/Admin only), delete an account + all its data (`procAccountCascadeDelete`), view any
  user's playlists. Guardrails: can't change/delete **yourself** or another **SuperAdmin** via the API.
- **Migration:** `db/2026-07-24c_roles_and_hard_delete.sql`.
- ⚠️ JWT gotcha: `options.MapInboundClaims = false` (Program.cs) is required so the short `role`
  claim isn't remapped — otherwise `[Authorize(Roles=...)]` silently 403s. See HANDOFF §5.9.

## 8.5 Artist, category & lyrics (DONE)

Songs now carry a **performer**, a **category** and **lyrics** — all worked out automatically when
the song is uploaded, not typed in by hand.

### Where the metadata comes from

Four sources, layered cheapest-and-most-trustworthy first. Each one only fills the gaps the
previous one left, so a good source is never overwritten by a worse guess:

| # | Source | Supplies | Applies to |
|---|--------|----------|------------|
| 1 | What the uploader typed | Always wins | Both |
| 2 | **Embedded tags** — ID3v1/v2, MP4 atoms, Vorbis comments (TagLibSharp) | Artist, title, album, genre, **duration** | File upload |
| 3 | **YouTube's structured music fields** (`artist`/`track`/`album`/`genre` from yt-dlp) | Same, from the label's own metadata rather than the uploader | YouTube import |
| 4 | **Title parser** (`TrackTitleParser`) | Cleans the title, and the artist when the channel reveals it | YouTube import |

Source 2 also fixes a long-standing bug: uploaded songs showed **no duration** because nothing was
measuring it. TagLib reads the real length off the file.

### The title parser, and what it deliberately won't do

YouTube titles bury the song name under the film, cast and label:

```
"Kahi Door Jab with Lyrics | Anand | Rajesh Khanna, Sumita Sanyal | Saregama Music"
        →  title: "Kahi Door Jab",  artist: (none)
```

It keeps everything before the first `|`, strips bracketed asides (`(Official Video)`, `[4K]`),
promo phrases (`with Lyrics`, `Full Video Song`) and quality markers, and reads the artist from a
`"<Artist> - Topic"` channel — those are auto-generated by YouTube from the label's metadata and are
the single most reliable artist signal available. Ordinary channel names are rejected: `T-Series`
and `Saregama Music` are labels, not artists.

> ⚠️ **It does NOT split `"A - B"` into artist and title when nothing else identified the artist.**
> `"Arijit Singh - Tum Hi Ho"` and `"Tum Hi Ho - Aashiqui 2"` are the same shape and mean opposite
> things, so guessing is wrong about as often as it's right. An early version did guess, and turned
> `"Tum Hi Ho - Aashiqui 2"` into artist `"Tum Hi Ho"`, title `"Aashiqui 2"` — exactly backwards.
> A fabricated artist is the expensive mistake: it's written to the row, it blocks the real song
> from being uploaded later via the duplicate check below, and it sends the lyrics lookup after a
> performer who doesn't exist. A slightly noisy title costs nothing by comparison and can be edited
> on the confirm screen. **This is the one place where an LLM would genuinely help** — see
> "Why lyrics aren't AI-generated" below for where it would *not*.

### Categories create themselves

There's no list to seed or administer. The first song that declares `"Bollywood"` creates that
category; everything after reuses it. `proccategorygetorcreate` is case-insensitive (`citext`), so
`"bollywood"` and `"Bollywood"` are one category, not two. A blank category → `Uncategorized`.

### Duplicate prevention by title + artist

Complements the byte-hash and video-id checks in §8.2, which only catch the *identical* file or the
*identical* video. Re-uploading the same song from a different source is now refused:

**409 `"Tum Hi Ho" by Arijit Singh is already in the library.`**

**SuperAdmin is exempt** — a cover, a remix, a live cut and a re-recording are all legitimately
"the same title by the same artist", and someone has to be able to add them. Matching is trimmed
and case-insensitive; a null artist matches a null artist, so untagged uploads still dedupe on
title alone.

### Lyrics — looked up, never generated

Two free sources, **no API key or account for either**, tried in order and stopping at the first
real answer:

| Order | Source | Gives | Notes |
|-------|--------|-------|-------|
| 1 | **LRCLIB** (`lrclib.net`) | Plain **+ synced `.lrc`** | The only one with timestamps that scroll with playback. Matches on artist+track+album+duration. |
| 2 | **lyrics.ovh** (`api.lyrics.ovh`) | Plain text only | Needs an artist name. Fallback for LRCLIB misses and outages. |

Because it's a chain, the feature keeps working when one service is down and **quietly upgrades back
to synced lyrics when LRCLIB recovers — no code change or redeploy.**

> ⚠️ **LRCLIB's API was returning `504` from its own gateway when this was built** (its website
> answered `200`; the API did not). That's the same failure behind the earlier `408`s — not a blip.
> Until it recovers you get plain lyrics from lyrics.ovh, not scrolling ones.

**A circuit breaker** (`LyricsSourceCircuitBreaker`, singleton) skips a source for 10 minutes after
3 consecutive failures, then probes it once. Without it every upload paid LRCLIB's full timeout
before falling through to the fallback — **measured at 7.5 s per upload, now 1.5 s**. A `404` is a
*healthy* "not in our catalogue" and never counts against a source; a `5xx` or timeout does.

The lookup runs **after** the song row, audio and cover are already saved, is capped by a total
wall-clock budget, and swallows every failure — **it can never fail an upload**. A song that isn't
catalogued simply gets no lyrics.

#### Why lyrics aren't AI-generated

Asked directly, and worth writing down. An LLM **cannot** reproduce a copyrighted song's words: it
either refuses, or invents plausible, well-formed verses that are *not* the song. For a player
that's the worst possible failure — wrong lyrics scrolling against a song the listener knows, with
no way to detect it, because the output looks perfect. It also has never heard the audio, so it
can't produce the `[mm:ss.xx]` timestamps synced lyrics need. Real lyrics come from a real database
or they don't come. (Speech-to-text on the actual audio — Whisper — *is* a legitimate future
option, but it needs roughly a CPU-minute per song, which the Render free tier can't provide.)

### Config — `appsettings.json`

```jsonc
"Lyrics": {
  "Enabled": true,             // master switch; false = no lookup at all
  "UseLrcLib": true,           // skip one source without disabling the feature
  "UseLyricsOvh": true,
  "TimeoutSeconds": 6,         // per source
  "TotalTimeoutSeconds": 15    // ceiling across the whole chain
}
```

### Schema & code

- **Migration:** `db/postgres/03_artist_category_lyrics.sql` (idempotent, wrapped in a transaction).
  Adds the `category` table; `songs.artist` (`citext`), `songs.categoryid`, `songs.lyrics`,
  `songs.lyricssynced`; and indexes on `(songname, artist)` and `categoryid`.
  **Every change is additive**, and Dapper ignores columns it doesn't recognise, so the previously
  deployed API kept running against this schema until the matching build rolled out.
- **New functions:** `proccategorygetorcreate`, `proccategorygetall`, `procsongfindbytitleartist`,
  `procsongsetlyrics`. Eight song functions were **dropped and recreated** — PostgreSQL refuses
  `CREATE OR REPLACE` when a function's return columns change.
- **Endpoint:** `GET /api/songs/categories` → `[{ categoryId, name, totalSongs }]`.
- **Lyrics are returned by `GET /api/songs/{id}` only** — list endpoints omit them so a song list
  doesn't carry a few KB per row for something almost never displayed.
- **Frontend:** artist on rows, cards and the player bar; a full-screen **lyrics panel** behind the
  mic button (active line highlighted and scrolled to, click a line to seek); artist + category
  fields on the upload form; the YouTube panel prefills with the *cleaned* title and artist so it
  can be corrected before importing rather than discovered afterwards.

> 💡 **PL/pgSQL gotcha, cost two failed migrations.** A function with an `OUT` column named `name`
> can't use `ON CONFLICT (name)` — PL/pgSQL can't tell the variable from the table column and errors
> with *"column reference is ambiguous"*. Use a bare `ON CONFLICT DO NOTHING`, or qualify every
> reference. The same applies to `ON CONFLICT (songid, accountid)` in `procsongvoteset`.

## 9. Deferred / not-yet-possible (the backlog)

| Item | Why | What's needed to do it |
|------|-----|------------------------|
| **Change password** | No stored proc exists (`procAccountUpdate` only changes email/isactive) | Add `procAccountChangePassword`, an `IAccountService` method + endpoint. |
| ~~**Search by artist/album/genre**~~ | **Done in §8.5** — `artist` + `categoryid` exist and `procSongSearch` matches artist. `album` is read but not yet stored. | Add `songs.album` if album search is wanted. |
| **Category browsing in the UI** | `GET /api/songs/categories` exists and returns counts; nothing consumes it yet | Add a category filter/page to the frontend. |
| **Backfill existing songs** | Songs uploaded before §8.5 have no artist, category or lyrics | Re-derive from their titles and run the lyrics lookup — no audio re-upload needed. |
| **Account delete orphans B2 files** | `AdminService.DeleteAccountAsync` never calls `IStorageService` (see §8) | Delete the account's song blobs before the DB cascade. |
| **Clean up the historical B2 backlog** | ~25 MB of hidden versions + orphans predate the delete fix in §8 | One-off sweep, or a "keep only the last version" lifecycle rule in the B2 console. |
| **AI metadata cleanup** | The title parser can't resolve `"A - B"` without an artist hint (§8.5) | An LLM extracting title/artist from messy titles, behind a config flag like `Youtube:UseProxy`. Only for *metadata* — never for lyrics. |
| **Refresh tokens** | Flagged "future" in the docs | Add refresh-token table + rotation. |
| **Upload validation / malware scan, rate limiting, CDN** | Feasibility doc flags these before public launch | Address before opening uploads beyond trusted users. |

---

## 10. Running the backend

**Prerequisites:** .NET 8 SDK, internet access to the Supabase database (see §5).

```bash
cd "Music Website/MusicWebsite"
dotnet build MusicWebsite.sln
dotnet run --project MusicWebsite/MusicWebsite.csproj
```

- **Host + port come from `MusicWebsite/MusicWebsite/.env`** (not from code):
  `BACKEND_HOST=0.0.0.0`, `BACKEND_PORT=5000` → `http://0.0.0.0:5000`, LAN-accessible.
  Change the port there and restart — no rebuild. Swagger UI: `http://localhost:5000/swagger`.
  Other keys in that file: `FRONTEND_ORIGINS` (CORS), `SERVE_FRONTEND`, `ASPNETCORE_ENVIRONMENT`.
  See §10a below and `.env.example`.
  > Precedence: an explicit `ASPNETCORE_URLS` (Visual Studio's "https" profile, IIS, Azure) beats
  > `.env`; the default `http` launch profile no longer sets a URL, so `.env` wins there.
- **Config** lives in `MusicWebsite/appsettings.json` (holds all secrets: DB connection, JWT key,
  B2 credentials):
  - `ConnectionStrings:MusicDatabase` — PostgreSQL/Supabase connection (URI or Npgsql key=value).
  - `Jwt` — ⚠️ **change `Jwt:Key` to a long random secret** before any real use (current value is a placeholder).
  - `Storage:B2` — Backblaze credentials + bucket (see §8).
  - `Cors:AllowedOrigins` — currently allows `http://localhost:5173` and `:3000` for the future frontend.
- 🔒 **`appsettings.json` is gitignored** — it is NOT committed or pushed. `appsettings.example.json`
  (committed) shows the structure. **On deploy**, since the file isn't in source control, you must
  supply it separately on the server — either copy an `appsettings.json` onto the host, or set the
  same values as environment variables (e.g. `Storage__B2__ApplicationKey=...`). Env vars override
  the file, which is the recommended way to inject secrets in production.

---

## 10a. Two apps, two ports (frontend ≠ backend)

The frontend and the API are **separate apps** and can be deployed to **different servers**. Nothing
is hard-coded — every port lives in a `.env` file.

| | Config file | Keys | Default |
|---|---|---|---|
| **Backend** (ASP.NET API) | `MusicWebsite/MusicWebsite/.env` | `BACKEND_HOST`, `BACKEND_PORT` | `0.0.0.0:5000` |
| **Frontend** (React/Vite) | `MusicWebsiteFrontEnd/.env` | `VITE_PORT` (dev), `VITE_PREVIEW_PORT` (build) | `5173` / `4173` |

They are wired together by exactly two settings — **keep them in sync**:

- `VITE_API_URL` (frontend) → the address the **browser** uses to reach the API.
- `FRONTEND_ORIGINS` (backend) → the frontend origins allowed through CORS.
  localhost and private-LAN addresses on any port are already allowed automatically; add the
  **public** frontend URL here when deploying.

**Run them**
```powershell
./serve-all.ps1            # both, each in its own window (dev)
./serve-all.ps1 -Prod      # both, frontend built then served
./start-backend.ps1        # API only
./start-frontend.ps1       # frontend only  (-Prod = build + preview)
```

**Deploying to two servers**
1. Backend: publish it, copy `.env` next to the exe, set `BACKEND_PORT` and
   `FRONTEND_ORIGINS=https://your-frontend-url`.
2. Frontend: set `VITE_API_URL=https://your-api-url` in `.env.production`, run `npm run build`,
   and serve `dist/` with any static host (IIS, nginx, Netlify, `npm run preview`).
   ⚠️ `VITE_*` values are **baked in at build time** — change one, then rebuild.
3. `.env` files are gitignored (per-machine); `.env.example` is committed as the template.

> Single-origin fallback: set `SERVE_FRONTEND=true` in the backend `.env`, copy the frontend `dist/*`
> into `MusicWebsite/MusicWebsite/wwwroot`, and build the frontend with an empty `VITE_API_URL`.
> The API then serves both from one port again.

---

## 11. API reference

Base URL: `http://localhost:5000`. All responses use the envelope
`{ "success": bool, "message": string?, "data": T? }`.
All routes except register/login require `Authorization: Bearer <token>`.

### Auth — `/api/auth`
| Method | Route | Body | Notes |
|--------|-------|------|-------|
| POST | `/register` | `{ email, password, userName, fullName, profileImageUrl? }` | Creates Account + User, returns JWT. `409` if email/username taken. |
| POST | `/login` | `{ email, password }` | Returns JWT + profile. `401` if invalid. A Google-created account has no password, so it answers `401` with "This account signs in with Google". |
| **POST** | **`/google`** | `{ idToken }` | **Sign in with Google.** `idToken` is the `credential` from the Google button. Returns the same JWT as `/login`. Creates the account on first use, or links to the existing account with that verified email. `401` if the token fails verification, `501` if `Google:ClientId` isn't configured. |
| POST | `/logout` | — | Stateless (client discards token). |

#### Setting up Sign in with Google
Off until a client id is configured — the endpoint answers `501` and the frontend hides the button,
so nothing breaks if you skip this.

1. **Google Cloud Console** → *APIs & Services* → *Credentials* → *Create credentials* →
   *OAuth client ID* → **Web application**. Under **Authorised JavaScript origins** add every
   origin the app is served from — e.g. `http://localhost:5173` and `https://sangeet-web.onrender.com`.
   (No redirect URI is needed: the button uses the ID-token flow, not a redirect.)
2. **Backend**: put the client id in `Google:ClientId` (`appsettings.json`) or the
   `Google__ClientId` environment variable on Render.
3. **Frontend**: put the *same* id in `VITE_GOOGLE_CLIENT_ID` and rebuild — `VITE_*` values are
   baked in at build time.
4. **Database**: run `db/postgres/04_google_sso.sql` once.

The two ids must match exactly: the backend verifies each token's *audience* against its copy, so a
mismatch rejects every sign-in with a `401`. The client id is **not** a secret — the browser
receives it either way.

**How accounts are matched.** A returning Google user is found by Google's `sub` claim (not the
email, which people can change). An unrecognised `sub` whose **verified** email already has an
account is linked to it, so someone who registered with a password can then use either route — this
is only safe because Google asserts the address is verified. Anything else creates a new
password-less account, inventing a free username from the email's local part.

### Account — `/api/account` (current user)
| Method | Route | Notes |
|--------|-------|-------|
| GET | `/` | Get own account (email, status). |
| PUT | `/` | `{ email, isActive }`. |
| DELETE | `/` | Delete own account. |

### Users — `/api/users`
| Method | Route | Notes |
|--------|-------|-------|
| GET | `/me` | Current user's profile. |
| PUT | `/me` | `{ userName, fullName, profileImageUrl? }`. `409` if username taken. |
| GET | `/` | All users. |
| GET | `/{userId}` | User by id. |

### Songs — `/api/songs`
| Method | Route | Notes |
|--------|-------|-------|
| GET | `/?search=term` | List all, or filter by song name **or artist**. `songUrl`/`imageUrl` come back as presigned B2 URLs. Lyrics are **not** included. |
| GET | `/{songId}` | Song by id — **the only endpoint that returns `lyrics` / `lyricsSynced`**. |
| **GET** | **`/categories`** | `[{ categoryId, name, totalSongs }]`. Categories are created by uploads, so this list grows on its own (§8.5). |
| POST | `/` | JSON `{ songName, songUrl, imageUrl?, durationInSeconds?, priority? }` — create from a direct URL (no file). |
| **POST** | **`/upload`** | **multipart/form-data**: `audioFile` (required), `coverImage?`, `songName`, `artist?`, `category?`, `durationInSeconds?`, `priority?` → uploads to B2, saves song. Blank `artist`/`category`/`durationInSeconds` are read from the file's own tags (§8.5). `409` if the same title+artist already exists (SuperAdmin exempt). |
| **POST** | **`/youtube/preview`** | JSON `{ url }` → `{ title, author, durationInSeconds, thumbnailUrl, suggestedSongName, suggestedArtist, suggestedCategory }`. The `suggested*` fields are what the import will actually save (§8.5). Metadata only, no download. |
| **POST** | **`/youtube`** | JSON `{ url, songName?, artist?, category?, priority? }` → extracts audio + thumbnail from YouTube, uploads to B2, saves song. `502` if the server can't reach YouTube. |
| **POST** | **`/{songId}/vote`** | JSON `{ value: 1 \| -1 \| 0 }` → sets the user's single up/down vote; returns the song with new `priority` + `myVote`. |
| PUT | `/{songId}` | Update song. |
| DELETE | `/{songId}` | **Permanent delete** (DB + cloud files). User → 403, Admin → own uploads only, SuperAdmin → any. |

### Admin — `/api/admin` (SuperAdmin only)
| Method | Route | Notes |
|--------|-------|-------|
| GET | `/users` | All accounts with role + profile. |
| PUT | `/users/{accountId}/role` | `{ role: "User" \| "Admin" }`. Can't target yourself or a SuperAdmin. |
| DELETE | `/users/{accountId}` | Permanently delete an account + its data. Can't target yourself or a SuperAdmin. |
| GET | `/users/{accountId}/playlists` | View any user's playlists. |

### Playlists — `/api/playlists` (ownership enforced: non-owner → `404`)
| Method | Route | Notes |
|--------|-------|-------|
| GET | `/` | Current user's playlists (with `totalSongs`). |
| GET | `/{playlistId}` | Playlist by id. |
| POST | `/` | `{ playlistName }`. `409` if duplicate name. |
| PUT | `/{playlistId}` | `{ playlistName }`. |
| DELETE | `/{playlistId}` | Delete playlist. |
| GET | `/{playlistId}/songs` | Songs in playlist. |
| POST | `/{playlistId}/songs/{songId}` | Add song. `409` if already present. |
| DELETE | `/{playlistId}/songs/{songId}` | Remove song. |

### Error status mapping (from SQL `THROW` codes)
`SqlErrorTranslator` maps DB error codes to HTTP: **409** (email/username/playlist/song already
exists), **404** (account/user/song/playlist not found), **400** (validation / bad reference).
Auth failures → **401**; unhandled → **500**.

---

## 12. How to verify a change works (quick smoke test)

Start the API, then (bash/curl):
```bash
BASE=http://localhost:5000
# register → capture token
TOKEN=$(curl -s -X POST $BASE/api/auth/register -H "Content-Type: application/json" \
  -d '{"email":"a@b.com","password":"Passw0rd!","userName":"user1","fullName":"User One"}' \
  | grep -o '"accessToken":"[^"]*"' | sed 's/"accessToken":"//;s/"//')
# authenticated call
curl -s $BASE/api/users/me -H "Authorization: Bearer $TOKEN"
```
Expected: register returns `success:true` + token; `/me` returns the profile.

---

## 13. Next steps (in priority order)

1. ~~Build the React + TypeScript frontend~~ ✅ **Done** — see `MusicWebsiteFrontEnd/` (Vite + React
   PWA: auth, home, search, player, playlists, upload, profile; responsive + installable).
   Run: `cd MusicWebsiteFrontEnd && npm install && npm run dev` → http://localhost:5173.
2. Add **change-password** (proc + endpoint) and, if wanted, **artist/album/genre** columns + search.
3. Harden for release: rotate `Jwt:Key`, **rotate the B2 app key**, add refresh tokens, rate limiting,
   optional server-side MP3 duration extraction (e.g. TagLibSharp).

---

## 14. Conventions & notes for future work

- **Never bypass the layers:** controllers call Application services; only Infrastructure touches
  Dapper/SQL. Keep `Npgsql`/Dapper out of Application & API.
- **All DB access goes through a PL/pgSQL function** and `RepositoryBase` (which routes database
  errors through `PostgresErrorTranslator`). Add new function names to `StoredProcedures.cs`.
- **New function parameters must be named `p_<lowercase C# property>`** — `RepositoryBase` builds
  `SELECT * FROM fn(p_x => @X)` from the parameter object, so the names must line up.
- **GUIDs are generated in C#** (`Guid.NewGuid()`) and passed into insert procs.
- **Business errors** → throw `AppException(message, statusCode)`; the middleware formats them.
- Swagger is pinned to **Swashbuckle 6.6.2** (v10 pulls OpenApi 2.x with a breaking namespace change).
