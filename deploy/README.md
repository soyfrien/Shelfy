# Deploying Shelfy to a single Linux VM

Shelfy runs as a systemd service behind localhost only (`127.0.0.1:5080`), talking to a local PostgreSQL.
It is **not** exposed publicly; add a reverse proxy only deliberately.

## 1. Publish (on a dev machine)

```bash
dotnet publish src/Shelfy.Api -c Release -r linux-x64 --self-contained false \
  -p:DebugType=none -p:DebugSymbols=false -o publish
rm -f publish/appsettings.Development.json
tar -czf shelfy-publish.tar.gz -C publish . && sha256sum shelfy-publish.tar.gz
```

## 2. One-time VM setup

```bash
# ASP.NET Core 8 runtime, isolated from any system dotnet
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --runtime aspnetcore --channel 8.0 --install-dir /opt/dotnet-shelfy --no-path

# Database schema (idempotent)
psql -h localhost -U shelfy -d shelfy -f db/schema.idempotent.sql

# Secrets: root-only env file. NEVER commit this.
install -d -m 700 /etc/shelfy
umask 077
echo 'ConnectionStrings__ShelfyDb=Host=localhost;Port=5432;Database=shelfy;Username=shelfy;Password=<PASSWORD>' > /etc/shelfy/shelfy.env

install -m 644 deploy/shelfy.service /etc/systemd/system/shelfy.service
```

## 3. Deploy / redeploy

Copy the tarball to the VM, then:

```bash
TARBALL=/tmp/shelfy-publish.tar.gz EXPECTED_SHA256=<hash> sudo -E bash deploy/deploy.sh
```

The previous release is kept in `/opt/shelfy.old` for rollback.

## Operate

```bash
systemctl status shelfy          # status
journalctl -u shelfy -n 50       # logs
systemctl stop shelfy            # stop
systemctl disable --now shelfy   # stop and don't start at boot
curl http://127.0.0.1:5080/health
```

Limits: `MemoryMax=300M`. Swagger is only served when `ASPNETCORE_ENVIRONMENT=Development`.

## 4. Public HTTPS (shelfy.ppdac.ltd)

`deploy/nginx-shelfy.ppdac.ltd.conf` is a standalone nginx server block (install as
`/etc/nginx/sites-available/shelfy.ppdac.ltd`, symlink into `sites-enabled`, `nginx -t`, then reload).
It proxies to `127.0.0.1:5080`, allows 25 MB bodies and rate-limits to 10 req/s per IP.
Port 80 listens on the VM's private IPs (matching the other sites). After the DNS A record points
at the VM's public IP, issue the certificate with:

```bash
certbot --nginx -d shelfy.ppdac.ltd --non-interactive --agree-tos -m louis@ppdac.ltd --redirect
```

## 5. Recognition pipeline (after pulling the commit that adds it)

The photo-recognition worker runs inside the API process. **Deployed on cosmic2 (2026-10-25)** and verified end to end
with Azure AI Vision (F0). Steps:

1. Apply the new schema: `psql -h localhost -U shelfy -d shelfy -f db/schema.idempotent.sql` (idempotent; adds `RecognitionJobs` and `DetectedSpines`).
2. Re-install the unit (`install -m 644 deploy/shelfy.service /etc/systemd/system/shelfy.service && systemctl daemon-reload`).
   It now sets `StateDirectory=shelfy` and `Storage__LocalPath=/var/lib/shelfy/photos`; photos persist across deploys.
3. Add Azure AI Vision settings to `/etc/shelfy/shelfy.env` (root-only, never commit):
   `AzureVision__Endpoint=https://<resource>.cognitiveservices.azure.com` and `AzureVision__Key=<key>`.
   Optional: `Resolution__GoogleBooksApiKey=<key>`, `Resolution__UserAgent=Shelfy/1.0 (+https://shelfy.ppdac.ltd; mailto:<you>)`.
   Without the Azure settings jobs fail fast with a message that says what is missing.
4. The VM needs outbound HTTPS to `openlibrary.org`, `www.googleapis.com` and the Azure Vision endpoint.
5. Redeploy with `deploy/deploy.sh`, then check `journalctl -u shelfy -n 50` for "Recognition worker started" and upload a test photo.

Memory: expect roughly 100-200 MB resident while processing a photo, within `MemoryMax=300M`. Raise it if you see OOM kills.
Disk: photos are up to 20 MB each and are kept; monitor `/var/lib/shelfy/photos`.
The upload and job endpoints have no authentication yet (ADR-006), and the nginx rate limit is the only abuse control.

### Deployment notes (cosmic2, 2026-10-25)

- Uploads through `az vm run-command` are limited to ~256 KB per call, so a full publish (3 MB) is slow. Upload only the
  files that changed (compare `sha256sum` manifests of `/opt/shelfy` and the new publish), rebuild the full tree on the VM
  from `/opt/shelfy` plus the delta, verify it against the manifest, tar it, then run `deploy.sh`.
- `run-command` has no protected parameters: a script that writes a secret leaves a copy under
  `/var/lib/waagent/run-command/download/<n>/script.sh`. Delete it afterwards (`shred -u`). Prefer a dedicated
  Vision key (the VM uses `key2`; `key1` stays with the developer).
- `Google Books` without an API key can answer 429 from the VM's shared egress IP. OpenLibrary matches first, so this does
  not block recognition, but set `Resolution__GoogleBooksApiKey` in `/etc/shelfy/shelfy.env` for a reliable fallback.
- Azure AI Vision F0: about 20 calls/min and 5,000/month.
- `deploy.sh` polls `/health` for up to 60 s (the service needs about 6-10 s to start on this VM).
