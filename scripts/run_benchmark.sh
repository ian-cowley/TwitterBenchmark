#!/usr/bin/env bash
set -euo pipefail

PROVIDER=${1:-"postgres"} # "postgres" or "sqlite"
TARGET_USERS=${2:-7000}

echo "=========================================================="
echo " Running TwitterBenchmark with Provider: ${PROVIDER}      "
echo " Target Concurrent Users: ${TARGET_USERS}                 "
echo "=========================================================="

# Kill any existing instances
pkill -f TwitterBenchmark || true
sleep 1

# Launch backend
export DB_PROVIDER="${PROVIDER}"
if [ "${PROVIDER}" = "glacier" ]; then
    export DB_CONNECTION_STRING=""
elif [ "${PROVIDER}" = "sqlite" ]; then
    export DB_CONNECTION_STRING="Data Source=twitter_bench.db;Mode=ReadWriteCreate;Cache=Shared;Pooling=True;"
else
    export DB_CONNECTION_STRING="Host=localhost;Port=5432;Database=twitter_bench;Username=benchuser;Password=benchpass;Pooling=true;Minimum Pool Size=10;Maximum Pool Size=10;Connection Idle Lifetime=0;No Reset On Close=true;Max Auto Prepare=50;Auto Prepare Min Usages=2;"
fi

./publish/TwitterBenchmark --urls http://127.0.0.1:5000 &
PID=$!

echo "[*] Waiting for backend to be healthy..."
for i in {1..30}; do
    if curl -s http://127.0.0.1:5000/health | grep -q "healthy"; then
        echo "[✓] Backend is healthy!"
        break
    fi
    sleep 0.5
done

# Phase 1: Warmup (1,000 users for 30s)
echo "=== Phase 1: Warmup (1,000 users) ==="
k6 run -e BASE_URL=http://localhost -e USERS=1000 -e DURATION=30s k6/k6_benchmark.js

# Phase 2: Raw throughput test (bombard /feed)
echo "=== Phase 2: Raw Throughput Test (/feed) ==="
k6 run --vus 200 --duration 30s - <<'EOF'
import http from 'k6/http';
import { check } from 'k6';
export default function () {
  const res = http.get('http://localhost/feed');
  check(res, { 'status is 200': (r) => r.status === 200 });
}
EOF

# Phase 3: Peak Confirmation Test (5-minute sustained test)
echo "=== Phase 3: 5-Minute Sustained Load Test (${TARGET_USERS} users) ==="
k6 run -e BASE_URL=http://localhost -e USERS="${TARGET_USERS}" -e DURATION=5m k6/k6_benchmark.js

echo "[*] Benchmark complete. Stopping backend (PID: ${PID})..."
kill "${PID}" || true
