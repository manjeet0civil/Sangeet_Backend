# 🌐 Hosting Sangeet (frontend and backend on separate servers)

The app is **two independent apps** that talk over HTTP:

| | What it is | Port config file | Key | Default |
|---|---|---|---|---|
| **Backend** | ASP.NET API + DB + storage | `MusicWebsite/MusicWebsite/.env` | `BACKEND_HOST`, `BACKEND_PORT` | `0.0.0.0` : `5000` |
| **Frontend** | React (Vite) static site | `MusicWebsiteFrontEnd/.env` | `VITE_PORT` (dev), `VITE_PREVIEW_PORT` (prod) | `5173` / `4173` |

No port is hard-coded in the source any more — edit the `.env` file and restart.

---

## Run both locally

```powershell
./serve-all.ps1            # dev:  API on 5000 + Vite dev server on 5173, two windows
./serve-all.ps1 -Prod      # prod: API on 5000 + built frontend served on 4173
```

Or one at a time:

```powershell
./start-backend.ps1        # API only
./start-frontend.ps1       # frontend dev server only
./start-frontend.ps1 -Prod # build the frontend, then serve dist/
```

Open the **frontend** URL (e.g. http://localhost:5173) — not the API port.

## Change a port

| Want | Edit | Then |
|---|---|---|
| API on 8080 | `BACKEND_PORT=8080` in the backend `.env` | restart the API **and** set `VITE_API_URL=http://…:8080` in the frontend `.env`, restart/rebuild it |
| Frontend on 3000 | `VITE_PORT=3000` in the frontend `.env` | restart the dev server; add `http://localhost:3000` to `FRONTEND_ORIGINS` if it isn't localhost/LAN |

⚠️ `VITE_*` values are compiled into the bundle at **build** time. After changing `VITE_API_URL`
you must restart `npm run dev` or re-run `npm run build`.

---

## The two settings that connect them

1. **`VITE_API_URL`** (frontend `.env` / `.env.production`) — the API address **the browser** will
   call. It must be reachable from the user's device, so `localhost` only works when the API runs on
   that same device. Use the LAN IP or a public URL otherwise.
2. **`FRONTEND_ORIGINS`** (backend `.env`) — comma-separated frontend origins allowed by CORS.
   localhost and private-LAN addresses (`192.168.*`, `10.*`, `172.16–31.*`) on any port are already
   allowed automatically, so this only matters for a public deployment.

If these disagree the browser console shows a **CORS error** or "Cannot reach the server".

---

## Deploying the API to Render (Docker)

Render has no native .NET runtime, so the API deploys as a **Docker image**. `Dockerfile` and
`render.yaml` at the repo root do this; `.dockerignore` keeps secrets and build output out.

**Region: `singapore`** — the same region as the Supabase database. Every request makes 1–3 database
round trips, so being next to the database is what matters. Backblaze B2 stays in `us-east-005`
(Virginia) and can't follow — B2 has no Asia-Pacific region — but that costs nothing on reads,
because playback URLs are signed locally and the browser streams straight from B2. Only uploads
cross the Pacific.

1. Push the repo to GitHub.
2. Render ➜ **New ➜ Blueprint**, select the repo. It reads `render.yaml`.
3. Fill in the four secrets it prompts for (`sync: false` in the blueprint):
   `ConnectionStrings__MusicDatabase`, `Jwt__Key`, `Storage__B2__KeyId`, `Storage__B2__ApplicationKey`.
4. After the frontend is deployed, set `FRONTEND_ORIGINS` to its public URL and redeploy.

### Things that would otherwise break it

| Problem | Handled by |
|---|---|
| Render assigns the port via `$PORT`; a fixed port fails the health check | `Program.cs` prefers `PORT` over `BACKEND_PORT`, and forces `0.0.0.0` |
| `/` returns 404, which Render reads as unhealthy | `healthCheckPath: /health` — a liveness probe that does **not** touch the database |
| `appsettings.json` is gitignored, so the image has no secrets | `StartupConfigCheck` fails with a list of exactly which env vars are missing |
| `yt-dlp.exe` is a Windows binary on a `C:\` path | The image installs the **Linux** yt-dlp; `Youtube__YtDlpPath=yt-dlp` resolves it from `PATH` |
| Stale Windows `obj/` files break the container restore | `.dockerignore` excludes `**/bin/`, `**/obj/` |

Config precedence, highest first: **`ASPNETCORE_URLS`** ➜ **`PORT`** ➜ `BACKEND_PORT` from `.env` ➜ `5000`.

### Free-tier realities

- **Spins down after 15 minutes idle**, ~1 minute cold start — plus .NET startup. First request after
  a quiet spell is slow.
- **512 MB RAM / 0.1 CPU.** No transcoding happens (yt-dlp grabs `bestaudio[ext=m4a]` as-is), which
  is what keeps this viable.
- **Ephemeral filesystem** — fine here: audio lives in B2, and yt-dlp only uses `/tmp` as scratch.
- **YouTube import may fail intermittently.** YouTube blocks datacenter IPs with "confirm you're not
  a bot". This affects both extractors — it's an IP-reputation problem, not a library one. Direct MP3
  upload is unaffected.

Test the image locally before pushing:
```powershell
docker build -t sangeet-api .
docker run --rm -p 5000:5000 -e PORT=5000 `
  -e ConnectionStrings__MusicDatabase="postgresql://..." `
  -e Jwt__Key="<32+ chars>" -e Jwt__Issuer=MusicWebsite -e Jwt__Audience=MusicWebsiteClient `
  sangeet-api
# then: curl http://localhost:5000/health   and   curl http://localhost:5000/health/db
```

---

## Deploying to two different servers

### Backend server
1. `dotnet publish -c Release` (the `.env` is copied next to the exe).
2. Edit that `.env` on the server:
   ```
   BACKEND_HOST=0.0.0.0
   BACKEND_PORT=5000
   FRONTEND_ORIGINS=https://music.yourdomain.com
   ASPNETCORE_ENVIRONMENT=Production
   ```
3. Supply `appsettings.json` (DB connection, JWT key, B2 keys) — it is gitignored, so copy it onto
   the server or pass the values as environment variables (`Storage__B2__ApplicationKey=…`).
4. Run it behind a reverse proxy (IIS / nginx / Caddy) to add HTTPS. The app itself serves plain
   HTTP on `BACKEND_PORT`.

### Frontend server
1. Set the API address in `MusicWebsiteFrontEnd/.env.production`:
   ```
   VITE_API_URL=https://api.yourdomain.com
   ```
2. `npm ci && npm run build` → produces `dist/`.
3. Serve `dist/` as static files from **any** host — IIS, nginx, Apache, Netlify, Vercel, S3+CDN, or
   `npm run preview` for a quick self-hosted option.
4. Configure the host to fall back to `index.html` for unknown paths (SPA routing), e.g. nginx:
   ```nginx
   location / { try_files $uri /index.html; }
   ```

> `.env` files are gitignored (each machine gets its own). `.env.example` is committed as the
> template; `.env.production` is committed because it describes the deployment.

---

## Exposing a local PC to the internet (testing)

Because there are now **two ports**, a tunnel must expose **both**, and the frontend must be built
with the tunnel's API URL.

1. Start both apps: `./serve-all.ps1 -Prod`
2. Tunnel the API:
   ```
   cloudflared tunnel --url http://localhost:5000
   ```
   → gives e.g. `https://api-xyz.trycloudflare.com`
3. Put that URL in `MusicWebsiteFrontEnd/.env.production` as `VITE_API_URL`, then `npm run build`.
4. Tunnel the frontend:
   ```
   cloudflared tunnel --url http://localhost:4173
   ```
   → share that URL.
5. Add the frontend tunnel URL to `FRONTEND_ORIGINS` in the backend `.env` and restart the API.

(ngrok works the same way: `ngrok http 5000` and `ngrok http 4173`.)

**Simpler alternative for a quick demo — one tunnel instead of two:** set `SERVE_FRONTEND=true` in
the backend `.env`, build the frontend with an empty `VITE_API_URL`, copy `dist/*` into
`MusicWebsite/MusicWebsite/wwwroot`, and expose only the API port. Everything is one origin again.

### Port forwarding (advanced, less safe)
Open **both** ports on the firewall and forward them on the router:
```powershell
New-NetFirewallRule -DisplayName "Sangeet API 5000"      -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
New-NetFirewallRule -DisplayName "Sangeet Frontend 4173" -Direction Inbound -Protocol TCP -LocalPort 4173 -Action Allow
```
Caveats: many ISPs use **CGNAT** (incoming connections never arrive), your public IP usually
**changes**, and it is plain **HTTP**. A tunnel avoids all three.

---

## ⚠️ Security — read before going public
Anyone with the URL can use the app, so:

- ✅ **Done:** the `Jwt:Key` has been replaced with a strong random secret.
- ⏳ **Rotate the Backblaze B2 key** (it was shared in plaintext), then update `appsettings.json`.
- **Tighten CORS** for a public deploy: the localhost/private-LAN allowance in `Program.cs` is a
  development convenience. For production, list only your real frontend origin in `FRONTEND_ORIGINS`.
- **Open registration + upload** means strangers could create accounts and upload files to your B2
  bucket (storage cost / abuse). While testing, share the URL only with people you trust, and stop
  the tunnel when you're done.
- There's **no rate limiting** yet. Add that + refresh tokens before leaving it exposed.
- Your **local SQL Server** is reachable through the API while it's online.

**Bottom line:** use a **tunnel**, expose it **only while you need it**, and rotate the B2 key.
For a permanent deployment, put the API behind HTTPS on a small cloud VM and the frontend on any
static host.
