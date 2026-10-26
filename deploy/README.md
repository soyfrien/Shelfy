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

## 6. Web UI (wwwroot)

The API serves a static mobile web UI from `wwwroot` (published with the app, so a normal deploy ships it). `/` is the upload page and
`/s/{shelfId}` the shareable shelf page; `/health` and `/api/*` are unchanged. HTML, JS and CSS are sent with `Cache-Control: no-cache`,
so a redeploy shows up on phones at once. nginx must allow bodies of at least 20 MB for the shelfy server block (`client_max_body_size 25m`,
already in `deploy/nginx-shelfy.ppdac.ltd.conf`); the browser downscales photos before upload, so typical uploads are 1-3 MB.
`Program.cs` trusts `X-Forwarded-Proto` from the local nginx so absolute URLs in link-preview tags use https.
The UI needs no database change; it creates anonymous guest users through `POST /api/guests` (temporary until ADR-006).

UI-only deploy (cosmic2, 2026-10-25): the 114 KB delta (the four `Shelfy.*.dll` and `wwwroot/`) was uploaded in two `az vm run-command` chunks,
overlaid on a copy of `/opt/shelfy`, tarred and installed with `deploy.sh`. Pin `Microsoft.EntityFrameworkCore.Design` (done: 8.0.32),
because a floating version changes the EF Core DLLs and makes the delta several MB.
End-to-end check after deploy: `python3 tools/make-test-shelf.py`, then guest, shelf, upload, poll, open `/s/{id}`; delete the test rows afterwards.


## 7. Wide-photo tiles and batches (ADR-009)

Adds the migration `AddUploadBatches` (three columns and an index on `RecognitionJobs`). It is backward compatible, so apply
`db/schema.idempotent.sql` first and then deploy the new build; the old build keeps working against the new columns.
Delta: the four `Shelfy.*.dll`, `Shelfy.Api.deps.json` and `wwwroot/` (`app.js`, `index.html`, `style.css`, new `tiles.js`), 134 KB, uploaded in two `az vm run-command` chunks.
No new configuration is required. Optional: `AzureVision__MinIntervalMilliseconds` (default 3200; set `0` on a paid Vision tier) and `AzureVision__Max429Retries` (default 2).
End-to-end check: `python3 tools/make-test-shelf.py --wide --tiles` writes `wide-shelf.jpg`, `wide-shelf.json` (expected books) and the four tiles;
upload the tiles with the same `batchId`, `part` and `parts` (or use the web UI with `wide-shelf.jpg`) and poll `GET /api/shelves/{id}/batches/{batchId}`.
Delete the test rows afterwards (`RecognitionJobs`, `Shelves`, `Users` and the photo folder of that shelf under `/var/lib/shelfy/photos`).

## 8. Clear shelf: hide and delete (ADR-010)

Adds the migration `AddShelfHidden` (`Shelves.IsHidden boolean NOT NULL DEFAULT false`, `Shelves.HiddenAt timestamptz NULL`), additive and
harmless to the previous build. Order: apply `db/schema.idempotent.sql` first, then deploy the new build (delta: the four `Shelfy.*.dll`,
`Shelfy.Api.deps.json` and `wwwroot/` `app.js`, `index.html`, `style.css`, 139 KB, two `az vm run-command` chunks). No new configuration.
Rollback: `/opt/shelfy.old` still works with the new columns.

Verify with a TEST shelf only (never a real one): create guest and shelf, upload `python3 tools/make-test-shelf.py` output, `POST /api/shelves/{id}/hide`
(then `/s/{id}`, `GET /api/shelves/{id}` and uploads answer 404; the rows and the photo stay), then a second shelf with `DELETE /api/shelves/{id}`
(the shelf, `ShelfBooks`, `RecognitionJobs`, `DetectedSpines` and `/var/lib/shelfy/photos/shelves/<id>` are gone; `Books` stays). Delete the hidden test shelf
afterwards with `DELETE` too. To restore a hidden shelf by hand: `UPDATE "Shelves" SET "IsHidden" = false, "HiddenAt" = NULL WHERE "Id" = '<id>';`.

Deployed on cosmic2 on 2026-10-25 and verified live: hide (API and the mobile UI) gives 404 on `/api/shelves/{id}`, `/s/{id}`, job status and uploads while rows and the photo stay;
delete (API and UI) leaves no `Shelves`, `ShelfBooks`, `RecognitionJobs`, `DetectedSpines` rows and no photo folder; `Books` rows stay; hide and delete are idempotent
(204 twice); deleting a hidden shelf works. Test data was removed afterwards.

## 9. Shelf search (web UI)

No migration and no new configuration. Adds the read-only endpoint `GET /api/shelves/{id}/unmatched` and the static `wwwroot/search.js`
(plus changes to `shared.js`, `app.js`, `shelf.js`, `style.css`, `index.html`, `shelf.html`). Delta: the four `Shelfy.*.dll`, `Shelfy.Api.deps.json`, `wwwroot/`.
Rollback: `/opt/shelfy.old` (the old UI simply has no search box; nothing else depends on it). Note `deploy.sh` keeps only one old release, so a second quick UI-only deploy overwrites it.

Checks: `node tools/test-search.js`; then, with a TEST shelf only (`python3 tools/make-test-shelf.py --wide`, upload, wait for the job),
`node tools/ui-check-search.js https://shelfy.ppdac.ltd <testShelfId>` (needs `puppeteer-core` and Chrome; it only reads the shelf it is given).
Delete the test shelf afterwards with `DELETE /api/shelves/{id}`.

## 10. Tap to fix, remembered corrections, noise filter (ADR-011)

Adds the migration `AddUserCorrections` (`DetectedSpines.Dismissed boolean NOT NULL DEFAULT false` and the `UserCorrections` table with a unique index on `TextKey`),
additive and harmless to the previous build. Order: apply `db/schema.idempotent.sql` first, then deploy the new build (delta: the four `Shelfy.*.dll`,
`Shelfy.Api.deps.json` and `wwwroot/` `app.js`, `shared.js`, `index.html`, `style.css` and the new `fix.js`). No new configuration. The VM needs the same outbound access as the
recognition pipeline (OpenLibrary, Google Books) for suggestions. Rollback: `/opt/shelfy.old` still works with the new column and table (`deploy.sh` keeps one old release).

Checks: `dotnet test` (includes the endpoint, correction-memory and noise-filter tests); `node tools/ui-check-fix.js --mock` (headless mobile Chrome against a mock API);
then live, on a TEST shelf only: `python3 tools/make-test-shelf.py --garble` writes `test-shelf-garbled.jpg` with one spine deliberately misspelled, upload it, and run
`node tools/ui-check-fix.js https://shelfy.ppdac.ltd <testShelfId> <fragment> <query> <title>` (it changes that shelf). Upload the same photo to a second test shelf and
the garbled spine matches by itself (`matchSource` `Memory`, no catalog call). Afterwards delete the test shelves with `DELETE /api/shelves/{id}` and the test rows from
`UserCorrections` (`DELETE FROM "UserCorrections" WHERE "BookId" IN (...)` for the test books, or by `TextKey`); the test books in `Books` may stay or be deleted if nothing else uses them.
To list fixes: `SELECT "TextKey", b."Title", "UseCount" FROM "UserCorrections" c JOIN "Books" b ON b."Id" = c."BookId";`

Deployed 2026-10-25 (box/NY time): migration first (4 to 5 migrations), then the new build as a delta overlay, then a second one-DLL delta that ranks suggestions by similarity to the whole spine text
(live: "Nuromanzir | William Gibson" now suggests Neuromancer first, 0.885, instead of any Gibson book). Live result on a TEST shelf: the headless-Chrome check tapped the garbled spine, picked
Neuromancer, the book joined the shelf and the spine left the list. The same photo on a second test shelf then auto-matched with `matchSource` `Memory` and an empty unmatched list.
Both test shelves, both test guests, the two `UserCorrections` rows and the test Neuromancer book were removed afterwards. Suggestions take about 8 seconds live (up to five catalog lookups in sequence).
