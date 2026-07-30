# 🚀 Deploying Sangeet

Step-by-step guide to putting this project online. Follow it in order — several steps depend on a
URL produced by an earlier one.

> Companion docs: `README.md` (overview + API reference) · `HOSTING.md` (local hosting, tunnels,
> port config) · `db/postgres/README.md` (database migration).

---

## 1. What gets deployed where

Sangeet is **three separate services**:

| Piece | Where it runs | Region | Notes |
|-------|---------------|--------|-------|
| **API** (ASP.NET 8) | Render — Docker web service | **Singapore** | Sleeps after 15 min idle on the free plan |
| **Frontend** (React/Vite) | Render Static Site, Cloudflare Pages, or Netlify | none (CDN) | Never sleeps |
| **Database** (PostgreSQL) | Supabase | `ap-southeast-1` (Singapore) | Already set up |
| **Audio files** | Backblaze B2 | `us-east-005` (Virginia) | Already set up |

### Why the API goes in Singapore

Every API request makes 1–3 database round trips, so the API must sit next to the **database**.
Backblaze B2 is in Virginia and can't follow — B2 has no Asia-Pacific region — but that costs
nothing on reads: playback URLs are signed **locally** (no network call to B2) and the browser
streams straight from B2. Only uploads cross the Pacific.

Putting the API in a US region instead would add roughly 230 ms × 2–3 queries to *every* request.

---

## 2. Before you start

- A **GitHub** account with two empty repositories:
  `Sangeet_Backend` and `Sangeet_Frontend`.
- A **Render** account (free plan is fine).
- Your secret values, which live in `MusicWebsite/MusicWebsite/appsettings.json`
  (gitignored — never committed).
- Confirm the browser is signed into the **correct** GitHub account before pushing.

---

## 3. Push the code

### 3a. Backend

From the repository root (the folder containing this file):

```bash
git push -u origin main
```

If the repo isn't initialised yet:

```bash
git init
git config user.name  "Your Name"
git config user.email "you@example.com"     # per-repo, so it doesn't inherit a work address
git add -A
git commit -m "Sangeet backend"
git branch -M main
git remote add origin https://github.com/<user>/Sangeet_Backend.git
git push -u origin main
```

### 3b. Frontend

The frontend is a **separate repository**. It lives inside this folder for convenience but is
gitignored here, so a nested repo is safe.

```bash
cd MusicWebsiteFrontEnd
git init
git config user.name  "Your Name"
git config user.email "you@example.com"
git add -A
git commit -m "Sangeet frontend: React + TypeScript Vite PWA"
git branch -M main
git remote add origin https://github.com/<user>/Sangeet_Frontend.git
git push -u origin main
```

> Skip GitHub's suggested `echo "# Sangeet_Frontend" >> README.md` — a README already exists there
> and appending would corrupt it.

**Check before pushing:** `git config user.email` is set **per repository**. Without it git falls
back to your global config, and that address is permanently embedded in public commit history.

---

## 4. Deploy the API on Render

Render has no native .NET runtime, so the API ships as a **Docker image** (`Dockerfile` at the repo
root).

### Option A — Blueprint
**New → Blueprint**, select `Sangeet_Backend`. Render reads `render.yaml` and prompts for the four
secrets. Delete the `repo:` and `branch:` lines from `render.yaml` first — they're only for
deploying by URL.

### Option B — Manual (more predictable)
**New → Web Service**, connect `Sangeet_Backend`, then:

| Setting | Value |
|---|---|
| Language / Runtime | **Docker** |
| Region | **Singapore** |
| Branch | `main` |
| Dockerfile path | `./Dockerfile` |
| Instance type | Free |
| **Health check path** | **`/health`** |

The health check path matters: `/` returns 404 by design, and Render reads that as unhealthy and
fails the deploy.

### Environment variables

`appsettings.json` is gitignored, so the container has **no secrets** — every value comes from here.

**Secrets** — copy from `MusicWebsite/MusicWebsite/appsettings.json`:

| Key | Value |
|---|---|
| `ConnectionStrings__MusicDatabase` | `postgresql://postgres.<project-ref>:<password>@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres` |
| `Jwt__Key` | 32+ character signing secret |
| `Storage__B2__KeyId` | Backblaze key id |
| `Storage__B2__ApplicationKey` | Backblaze application key |

**Non-secret:**

| Key | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Jwt__Issuer` | `MusicWebsite` |
| `Jwt__Audience` | `MusicWebsiteClient` |
| `Jwt__AccessTokenMinutes` | `120` |
| `Storage__Provider` | `BackblazeB2` |
| `Storage__B2__ServiceUrl` | `https://s3.us-east-005.backblazeb2.com` |
| `Storage__B2__Region` | `us-east-005` |
| `Storage__B2__BucketName` | `sangeet-audio` |
| `Storage__B2__PresignExpiryMinutes` | `120` |
| `Storage__B2__MaxUploadMegabytes` | `100` |
| `Youtube__Provider` | `YtDlp` |
| `Youtube__YtDlpPath` | `yt-dlp` |
| `Youtube__TimeoutSeconds` | `90` |
| `Roles__DefaultRole` | `Admin` |
| `FRONTEND_ORIGINS` | set in step 6 |

`__` (double underscore) is .NET's separator for nested config sections: `Storage__B2__KeyId`
maps to `Storage:B2:KeyId`.

> **Do not set `BACKEND_PORT`.** Render assigns the port and injects it as `PORT`, which the app
> prefers. Overriding it makes the health check hit a closed port and the deploy fail.

The first build takes 5–10 minutes — it pulls the .NET SDK image and compiles from source.

### Important — the Supabase connection string

Use the **Session Pooler** host, not the direct one:

```
postgresql://postgres.<project-ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres
```

`db.<project-ref>.supabase.co` publishes **only an IPv6 address** and fails from IPv4-only
networks. Note the username is `postgres.<project-ref>`, not plain `postgres`. Port 5432 is
session mode; 6543 is transaction mode and doesn't support every feature.

Find it at: Supabase → Project Settings → Database → Connection string → **Session pooler**.

---

## 5. Deploy the frontend

### On Render
**New → Static Site**, connect `Sangeet_Frontend`:

| Setting | Value |
|---|---|
| Build command | `npm ci && npm run build` |
| Publish directory | `dist` |
| `VITE_API_URL` | the API's URL, **including `https://`** |

### On Cloudflare Pages or Netlify
Same build command and output directory. `public/_redirects` and `public/_headers` already
configure SPA routing and cache headers, so no extra setup is needed.

⚠️ **Two things that silently break the frontend:**

1. `VITE_*` values are compiled into the bundle at **build** time. Changing `VITE_API_URL` requires
   a **redeploy**, not a restart.
2. `VITE_API_URL` must include the scheme. `sangeet-api.onrender.com` without `https://` is treated
   by axios as a *relative path*, so every request goes to the wrong place with no obvious error.

---

## 6. Connect the two (the step everyone forgets)

Go back to the **API** service and set:

```
FRONTEND_ORIGINS = https://<your-frontend-url>
```

Then redeploy. Without it, CORS rejects every browser request — the app looks completely broken
while the API is perfectly healthy. Comma-separate multiple origins, no trailing slash.

localhost and private-LAN addresses are always allowed, so this only matters in production.

---

## 7. Verify

```
https://<api>/health       →  {"status":"ok", ...}          liveness
https://<api>/health/db    →  {"database":"reachable"}      database connectivity
```

Then open the frontend and log in with an existing account. If `/health` works but `/health/db`
returns 503, the connection string is wrong — check the pooler host and username format.

---

## 8. Security tasks

| Task | Why |
|---|---|
| **Rotate the Supabase database password** | If the repo is public, the pooler hostname and `postgres.<ref>` username pattern are guessable. The password is the only thing protecting a publicly reachable database — it must not be a name or dictionary word. |
| **Rotate the Backblaze B2 application key** | It was shared in plaintext during development. |
| **Tighten CORS** | `Program.cs` allows any localhost/private-LAN origin as a development convenience. For a public deployment, list only the real frontend origin. |
| **Add rate limiting** | There is none yet. Registration and upload are open to anyone with the URL. |

After rotating either credential, update the Render environment variable **and** your local
`appsettings.json`.

---

## 9. Free-tier behaviour worth knowing

- **The API sleeps after 15 minutes of inactivity**; waking it takes about a minute, plus .NET
  startup. The first request after a quiet spell is slow. Static sites never sleep.
- **512 MB RAM / 0.1 CPU.** Viable because nothing transcodes — yt-dlp downloads
  `bestaudio[ext=m4a]` as-is.
- **Ephemeral filesystem.** Fine here: audio lives in B2 and yt-dlp only uses `/tmp` as scratch.
  Anything written locally disappears on redeploy.
- **YouTube import may fail intermittently.** YouTube blocks datacenter IP ranges with "confirm
  you're not a bot". This affects any extractor — it's IP reputation, not a library problem. Direct
  MP3 upload is unaffected. Setting `Youtube__Provider=YoutubeExplode` will *not* fix it.
- **Audio streaming does not consume Render bandwidth** — presigned URLs mean the browser fetches
  audio directly from B2. Watch B2 egress instead, which is what actually scales with listening.

---

## 10. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Deploy fails, logs say config is missing | `StartupConfigCheck` lists every missing variable by name. Add them and redeploy. |
| Deploy "live" but health check fails | Health check path isn't `/health`; `/` is a 404 by design. |
| App starts then exits immediately | `BACKEND_PORT` was set and is fighting Render's `PORT`. Remove it. |
| Every request fails in the browser, API healthy | `FRONTEND_ORIGINS` doesn't include the frontend's exact origin (scheme + host, no trailing slash). |
| "Cannot reach the server" on first load | The free instance is waking up. Wait ~1 minute. |
| `/health/db` returns 503 | Wrong connection string — check the pooler host, and that the username is `postgres.<project-ref>`. |
| Requests go to the wrong URL | `VITE_API_URL` is missing `https://`, so axios treats it as a relative path. |
| Direct links like `/login` return 404 | SPA fallback missing. `public/_redirects` (Cloudflare/Netlify) or the rewrite rule in `render.yaml` (Render). |
| Frontend updates don't appear | Service worker cache. `public/_headers` sets `no-cache` on `sw.js` and `index.html`; hard-refresh, or unregister the service worker. |
| YouTube import fails on the server but works locally | Datacenter IP blocking (see §9). Not fixable by switching extractor. |
| Docker build fails restoring packages | Stale `bin/`/`obj/` reached the build context. `.dockerignore` excludes them — don't remove those lines. |
