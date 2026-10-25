#!/usr/bin/env bash
# Regenerates db/schema.idempotent.sql from the EF Core migrations (BOM-free, LF endings).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore >/dev/null
tmp="$(mktemp)"
dotnet ef migrations script --idempotent \
  --project src/Shelfy.Infrastructure --startup-project src/Shelfy.Api -o "$tmp" >/dev/null
# `dotnet ef -o` writes a UTF-8 BOM; strip it so psql and diffs stay clean.
sed '1s/^\xEF\xBB\xBF//' "$tmp" | tr -d '\r' > db/schema.idempotent.sql
rm -f "$tmp"
echo "wrote db/schema.idempotent.sql"
