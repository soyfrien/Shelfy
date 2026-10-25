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
