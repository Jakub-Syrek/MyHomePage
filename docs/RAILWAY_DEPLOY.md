# Deploying MyHomePage to Railway

End-to-end deploy guide using **Railway CLI**. Everything (project creation,
volume, env vars, deploy, domain) is done from your local terminal.

## Prerequisites

* Railway account (Pro plan — for 100 GB volume + persistent storage)
* `git` repository already pushed to GitHub
* PowerShell on Windows

## 1. Install Railway CLI

```powershell
# Recommended: official installer
iwr -useb https://railway.app/install.ps1 | iex
```

Or via Scoop / npm:

```powershell
scoop install railway          # Scoop
npm i -g @railway/cli          # npm (any platform)
```

Verify:

```powershell
railway --version
```

## 2. Log in

```powershell
railway login
```

A browser window opens; authenticate with the same account you used for the
Pro plan upgrade.

## 3. Create the project & link

From the **MyHomePage** repo root:

```powershell
cd C:\Users\jaqbs\source\repos\MyHomePage
railway init
```

Choose:

* **Empty Project** (we will deploy from source)
* Name: `myhomepage`

The CLI writes `.railway/` metadata and links the current directory.

## 4. Add a 100 GB volume

```powershell
railway volume add --mount-path /data --size 100
```

This persists `/data/videos` across redeploys.

## 5. Set environment variables

```powershell
railway variables --set "ASPNETCORE_ENVIRONMENT=Production"
railway variables --set "VIDEO_STORAGE_ROOT=/data/videos"
railway variables --set "FFMPEG_PATH=/usr/bin"
```

Verify:

```powershell
railway variables
```

## 6. Deploy

```powershell
railway up
```

The CLI uploads your source, builds the Dockerfile, and starts the container.
Watch the logs:

```powershell
railway logs
```

When you see `Application pipeline built — listening for requests`, it's live.

## 7. Generate a public domain

```powershell
railway domain
```

Railway prints a `*.up.railway.app` URL. Open it — you should see the home page.

Health check:

```powershell
curl https://<your-app>.up.railway.app/health
```

## 8. Upload `credentials.json` for admin login

Credentials live as a file (not env var). The simplest path:

```powershell
# After first deploy, SSH into the container
railway ssh
# (in container)
cat > /app/credentials.json <<'EOF'
{"users":[{"email":"admin@mountains.com","password":"YOUR_STRONG_PASSWORD"}]}
EOF
exit
```

Or commit `credentials.json` to the repo (NOT recommended — it's in `.gitignore`
for a reason). Better long-term: replace `CredentialService` with env-var auth.

---

## Operations cheat sheet

```powershell
railway logs              # tail logs
railway logs --deployment # current deploy logs
railway status            # service status
railway open              # open dashboard in browser
railway up                # redeploy current code
railway down              # stop service
railway run dotnet --info # run a command inside the container env
```

## Updating the app

Just push to GitHub or run `railway up` again. Railway rebuilds the Docker
image and rolls a new deployment.

## Region (latency)

Default region is `us-west`. For PL/EU users, switch in the dashboard:

* Railway dashboard → service → Settings → Region → `europe-west4 (Amsterdam)`

Expect ~30–60 ms latency from Poland to Amsterdam (vs ~150 ms to US).

## Custom domain

```powershell
railway domain add yoursite.com
```

Add the printed CNAME record at your DNS provider. Railway provisions Let's
Encrypt SSL automatically.

## Cost monitoring

```powershell
railway usage
```

Pro plan includes $20 of usage; the dashboard shows real-time breakdown of
CPU, RAM, network, and volume costs.

---

## Troubleshooting

### Build fails: "FFmpeg not found"
The Dockerfile installs FFmpeg via apt. If the build fails on `apt-get`, check
the `runtime` stage — Microsoft's base images include apt.

### Videos missing after redeploy
You forgot to mount the volume, or `VIDEO_STORAGE_ROOT` is not set to a path
under the volume. Run `railway volume list` and `railway variables` to verify.

### 502 / app crashes on startup
Open `railway logs` — most often a missing env var. Check:

* `ASPNETCORE_ENVIRONMENT=Production`
* `VIDEO_STORAGE_ROOT=/data/videos`

### Upload size capped at 30 MB
That's Railway's default request body limit on the **HTTP edge**. Pro plan
allows up to 5 GB; if you still hit limits, contact Railway support — the
underlying Kestrel limit is already raised to 5 GB in `Program.cs`.

### High egress / network cost
Long videos served to many viewers can blow through bandwidth. Solutions:

1. Add Cloudflare (free) in front of the Railway domain — cached MP4 = ~95%
   bandwidth savings.
2. Move videos to S3/R2 (~$0.015/GB egress vs Railway $0.10/GB).

---

## What was changed for Railway readiness

* `Dockerfile` — multi-stage, FFmpeg via apt, non-root user, health check
* `.dockerignore` — excludes bin/obj/videos/scripts from build context
* `railway.toml` — Railway picks this up automatically
* `Program.cs`:
    * `/health` endpoint
    * Brotli + Gzip response compression
    * 7-day cache + range-requests for video files
    * Configurable storage root via `VIDEO_STORAGE_ROOT`
    * Skip HTTPS redirect on Railway (edge already does TLS)
    * Larger upload limits (5 GB)
* `FileStorageService` — resolves storage root from env var first
* `VideoStorageOptions` + `H264CompressionStrategy` — bumped to 1080p / CRF 23
  / slow preset / `tune=film` / 2-second GOP for smooth seeking
* `appsettings.Production.json` — production-tuned defaults
