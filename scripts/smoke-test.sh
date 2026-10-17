#!/usr/bin/env bash
# Docker 冒煙測試（architecture-plan.md §0 v1.13）：用 compose 起整組服務，打一遍真實的使用流程。
#   CI：    cp .env.example .env && bash scripts/smoke-test.sh
#   本機：  COMPOSE_PROJECT=medconnect-smoke ENV_FILE=/path/to/file.env MYSQL_PORT=23306 API_PORT=18080 bash scripts/smoke-test.sh
# 本機請用獨立的專案名稱（COMPOSE_PROJECT），結束時 `down -v` 只會清掉這個專案自己的容器與 volume。
#
# 刻意沒有 `set -x`，而且絕不印出 Login 的回應內容：token 只存在 shell 變數裡，在 Actions 裡拿到的下一行立刻 ::add-mask::。
set -euo pipefail

COMPOSE_PROJECT="${COMPOSE_PROJECT:-medconnect-smoke}"
ENV_FILE="${ENV_FILE:-.env}"
API_PORT="${API_PORT:-8080}"
export API_PORT
BASE_URL="http://127.0.0.1:${API_PORT}"
HEALTH_TIMEOUT_SECONDS="${HEALTH_TIMEOUT_SECONDS:-120}"

# 示範帳號（Database:SeedDemoData）。密碼是公開的示範值（見 architecture-plan.md §0 v1.13），不是機密。
DEMO_EMAIL="test-patient@medconnect.local"
DEMO_PASSWORD="Test1234!"

# 找「真的跑得起來」的 python：Windows 的 python3 常常只是 Microsoft Store 的空殼（存在但執行會失敗）。
PYTHON=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import sys' >/dev/null 2>&1; then
    PYTHON="$candidate"
    break
  fi
done
[ -n "$PYTHON" ] || { echo "python is required to parse JSON but was not found"; exit 1; }
compose() { docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" "$@"; }

STEP="start"
WORKDIR="$(mktemp -d)"

cleanup() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    echo "::error::Docker smoke test failed at step: ${STEP}"
    echo "---- compose logs (api) ----"
    compose logs --no-color --tail=200 api || true
    echo "---- compose logs (mysql, tail) ----"
    compose logs --no-color --tail=30 mysql || true
    # 完整的 job log 不登入讀不到，但 annotation 讀得到：把 API 的 Fatal 日誌（若有）濃縮成一行 annotation。
    compose logs --no-color api 2>/dev/null | "$PYTHON" -c '
import json, sys
for line in sys.stdin:
    line = line[line.find("{"):] if "{" in line else ""
    try:
        event = json.loads(line)
    except ValueError:
        continue
    if event.get("@l") == "Fatal":
        first = (event.get("@x") or "").splitlines()[0] if event.get("@x") else ""
        print("::error::API startup failed (Fatal): " + event.get("@m", "") + " | " + first)
        break
' || true
  fi
  echo "---- cleaning up (down -v, project ${COMPOSE_PROJECT}) ----"
  compose down -v --remove-orphans || true
  rm -rf "$WORKDIR"
  exit "$status"
}
trap cleanup EXIT

# 注意：JSON 一律用 stdin 餵給 python（不是檔案路徑）——Windows 的 python 打不開 Git Bash 的 /tmp 路徑。

# 回傳 HTTP 狀態碼，body 寫進檔案（呼叫端決定要不要看）。
http() { # method path outfile [extra curl args...]
  local method="$1" path="$2" out="$3"
  shift 3
  curl -sS -o "$out" -w '%{http_code}' -X "$method" "${BASE_URL}${path}" "$@"
}

STEP="compose up --build"
compose up -d --build

STEP="wait for /health"
deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
until [ "$(curl -s -o /dev/null -w '%{http_code}' "${BASE_URL}/health" || true)" = "200" ]; do
  if [ -z "$(compose ps --status running --quiet api)" ]; then
    echo "api container is not running (it exited during startup)"
    exit 1
  fi
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "/health did not return 200 within ${HEALTH_TIMEOUT_SECONDS}s"
    exit 1
  fi
  sleep 2
done
echo "health: 200"

STEP="login as the demo patient"
login_status="$(http POST /api/v1/auth/login "$WORKDIR/login.json" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${DEMO_EMAIL}\",\"password\":\"${DEMO_PASSWORD}\"}")"
[ "$login_status" = "200" ] || { echo "login returned HTTP ${login_status}"; exit 1; }
TOKEN="$("$PYTHON" -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])' < "$WORKDIR/login.json")"
# ::add-mask:: 是 GitHub Actions 的 workflow command：runner 會吃掉這一行（不顯示）並遮蔽之後所有輸出裡的這個值。
# 只在 Actions 裡送出——本機執行時送出的話，token 會被直接印在終端機上。
if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
  echo "::add-mask::${TOKEN}"
fi
rm -f "$WORKDIR/login.json"
echo "login: 200 (token masked)"

STEP="query demo schedule slots"
slot_date="$(date -u -d '+1 day' +%F)"
slots_status="$(http GET "/api/v1/schedule-slots?doctorId=1&date=${slot_date}" "$WORKDIR/slots.json")"
[ "$slots_status" = "200" ] || { echo "schedule query returned HTTP ${slots_status}"; exit 1; }
slot_count="$("$PYTHON" -c 'import json,sys; print(len(json.load(sys.stdin)))' < "$WORKDIR/slots.json")"
[ "$slot_count" -ge 1 ] || { echo "expected at least one demo slot for doctor 1 on ${slot_date}, got ${slot_count}"; exit 1; }
slot_id="$("$PYTHON" -c 'import json,sys; print(json.load(sys.stdin)[0]["id"])' < "$WORKDIR/slots.json")"
echo "schedule query: 200, ${slot_count} slot(s) on ${slot_date}"

STEP="book a slot with the token"
book_status="$(http POST /api/v1/appointments "$WORKDIR/book.json" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer ${TOKEN}" \
  -d "{\"slotId\":${slot_id}}")"
[ "$book_status" = "201" ] || { echo "booking returned HTTP ${book_status}"; exit 1; }
echo "booking: 201"

STEP="done"
echo "smoke test passed"
