#!/usr/bin/env bash
# Run ON the VM as root, after the publish tarball is at $TARBALL.
# Usage: TARBALL=/tmp/shelfy-publish.tar.gz EXPECTED_SHA256=<hash> sudo -E bash deploy/deploy.sh
# No secrets here: /etc/shelfy/shelfy.env must already exist (see deploy/README.md).
set -euo pipefail
TARBALL=${TARBALL:?path to tarball}
echo "${EXPECTED_SHA256:?sha256}  $TARBALL" | sha256sum -c -
id shelfy >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin shelfy
[ -f /etc/shelfy/shelfy.env ] || { echo "missing /etc/shelfy/shelfy.env" >&2; exit 1; }
rm -rf /opt/shelfy.new && mkdir -p /opt/shelfy.new
tar -xzf "$TARBALL" -C /opt/shelfy.new
chown -R root:root /opt/shelfy.new; chmod -R go-w /opt/shelfy.new
systemctl stop shelfy 2>/dev/null || true
rm -rf /opt/shelfy.old; [ -d /opt/shelfy ] && mv /opt/shelfy /opt/shelfy.old
mv /opt/shelfy.new /opt/shelfy
systemctl daemon-reload
systemctl enable --now shelfy
systemctl restart shelfy
systemctl is-active shelfy
# Startup takes several seconds on a small VM: poll the health check instead of a fixed sleep.
for i in $(seq 1 30); do
  curl -fsS http://127.0.0.1:5080/health && { echo; exit 0; }
  sleep 2
done
echo "health check failed after 60 s; see: journalctl -u shelfy -n 50" >&2
exit 1
