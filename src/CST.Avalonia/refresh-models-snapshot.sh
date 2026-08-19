#!/usr/bin/env bash
# Refresh the bundled models.dev catalogue snapshot. (#733, #736)
#
# The snapshot is the LAST FALLBACK behind the runtime cache and the network, so it does not need to be
# current — its job is that a fresh install with no network still has providers to offer. Run it before a
# release; a forgotten run degrades gracefully.
#
# Deliberately NOT part of package-macos.sh or package-windows.ps1. Each packager runs its own `dotnet
# publish`, so a fetch inside one of them would force an ordering between two otherwise independent scripts
# (macOS first, or Windows ships a stale copy) with nothing saying so. It would also make a build
# non-reproducible and turn a models.dev outage into a release failure. A committed file has none of those
# properties: it runs once because it is in git, and both packagers just consume it.
#
# Output is pretty-printed with sorted keys ON PURPOSE. A 4 MB minified diff is unreadable, and the whole
# point of committing this is that a refresh can be reviewed — you should be able to see which providers
# appeared or disappeared since the last release.
set -euo pipefail

SOURCE="${MODELS_DEV_URL:-https://models.dev/api.json}"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$DIR/Resources/Ai/models-dev-snapshot.json"
META="$DIR/Resources/Ai/models-dev-snapshot.meta.json"
TMP="$(mktemp)"
trap 'rm -f "$TMP" "$OUT.tmp"' EXIT

echo "Fetching $SOURCE"
curl -fsSL --max-time 60 "$SOURCE" -o "$TMP"

# Validate before replacing anything. A truncated or HTML error page must never overwrite a good snapshot —
# this file is the fallback for readers with no network, so a corrupt one fails where nothing else can help.
if ! python3 - "$TMP" <<'PY'
import json, sys
try:
    doc = json.load(open(sys.argv[1]))
except json.JSONDecodeError as e:
    sys.exit(f"not JSON ({e}) - the URL probably returned an error page rather than the catalogue")
if not isinstance(doc, dict) or len(doc) < 50:
    sys.exit(f"refusing: expected a provider-keyed object with many entries, got {type(doc).__name__} of {len(doc)}")
missing = [k for k, v in doc.items() if not isinstance(v, dict) or "id" not in v]
if missing:
    sys.exit(f"refusing: {len(missing)} records lack an id, e.g. {missing[:3]}")
PY
then
    echo "Refusing to write: the fetched document did not validate." >&2
    echo "The existing snapshot is untouched - validation runs on a temp file for exactly this reason." >&2
    exit 1
fi

mkdir -p "$(dirname "$OUT")"
# Written to a temp file and moved into place: a 5.7 MB dump interrupted by Ctrl-C or a full disk would
# otherwise leave a truncated snapshot, contradicting the guarantee printed above. (fable review)
python3 -c '
import json, sys
doc = json.load(open(sys.argv[1]))
with open(sys.argv[2], "w") as f:
    json.dump(doc, f, indent=1, sort_keys=True, ensure_ascii=False)
    f.write("\n")
' "$TMP" "$OUT.tmp"
mv "$OUT.tmp" "$OUT"

PROVIDERS=$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$OUT")
WITH_API=$(python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(sum(1 for v in d.values() if v.get("api")))' "$OUT")
SHA=$(shasum -a 256 "$OUT" | cut -d" " -f1)

python3 - "$META" "$SOURCE" "$PROVIDERS" "$WITH_API" "$SHA" <<'PY'
import json, sys, datetime
meta = {
    "source": sys.argv[2],
    "fetchedUtc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "providers": int(sys.argv[3]),
    "providersWithApiUrl": int(sys.argv[4]),
    "sha256": sys.argv[5],
    "license": "MIT (models.dev)",
}
json.dump(meta, open(sys.argv[1], "w"), indent=2)
open(sys.argv[1], "a").write("\n")
PY

echo "Wrote $(basename "$OUT")  providers=$PROVIDERS  with api URL=$WITH_API"
echo "Review the diff before committing — it shows which providers changed since the last release."
