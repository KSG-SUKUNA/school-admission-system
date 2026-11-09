#!/usr/bin/env bash
set -euo pipefail

API_A="http://localhost:8080"
API_B="http://localhost:8081"
CAPACITY=5
REQUESTS=25
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

for _ in $(seq 1 30); do
  if curl -fsS "$API_A/health" >/dev/null && curl -fsS "$API_B/health" >/dev/null; then break; fi
  sleep 2
done

CLASS_JSON="$(curl -fsS -X POST "$API_A/classes" -H 'Content-Type: application/json' -d "{\"name\":\"Bonus concurrency $(date +%s)\",\"totalSeats\":$CAPACITY}")"
CLASS_ID="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<< "$CLASS_JSON")"

echo "Created $CLASS_ID with capacity $CAPACITY. Sending $REQUESTS simultaneous requests to two API processes..."

for i in $(seq 1 "$REQUESTS"); do
  STUDENT_ID="$(printf '%017d' "$((10000000000000000 + i))")"
  if (( i % 2 == 0 )); then API="$API_A"; else API="$API_B"; fi
  (
    curl -sS -w '%{http_code}' \
      -X POST "$API/classes/$CLASS_ID/holds" \
      -H 'Content-Type: application/json' \
      -H "X-Student-Id: $STUDENT_ID" \
      -o "$TMP_DIR/$i.body" > "$TMP_DIR/$i.code"
  ) &
done
wait

CREATED="$(grep -l '^201$' "$TMP_DIR"/*.code | wc -l | tr -d ' ')"
CONFLICTS="$(grep -l '^409$' "$TMP_DIR"/*.code | wc -l | tr -d ' ')"
FINAL_JSON="$(curl -fsS "$API_A/classes/$CLASS_ID")"
python3 - "$FINAL_JSON" "$CREATED" "$CAPACITY" "$CONFLICTS" <<'PY'
import json, sys
item = json.loads(sys.argv[1])
created, capacity, conflicts = map(int, sys.argv[2:])
print(f"Successful holds: {created}; conflicts: {conflicts}")
print(f"Database seat counts: held={item['heldSeats']}, confirmed={item['confirmedSeats']}, total={item['totalSeats']}")
assert created == capacity, f"Expected exactly {capacity} successful holds, got {created}"
assert item['heldSeats'] + item['confirmedSeats'] <= item['totalSeats'], "Seat limit was exceeded"
print("PASS: the two backend processes respected the shared database seat limit.")
PY
