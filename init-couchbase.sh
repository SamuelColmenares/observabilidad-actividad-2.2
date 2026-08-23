#!/bin/sh

echo "===================================================="
echo " Starting Couchbase Auto-Initialization Script"
echo "===================================================="

# Read credentials from environment variables (injected via GitHub Secrets in CI/CD)
# Falls back to defaults for local docker-compose usage
CB_ADMIN_USER="${CB_ADMIN_USER:-Administrator}"
CB_ADMIN_PASSWORD="${CB_ADMIN_PASSWORD:-password}"

echo "Using Couchbase admin user: ${CB_ADMIN_USER}"

# Helper function to check if Couchbase HTTP port is up
wait_for_couchbase() {
    echo "Waiting for Couchbase Server (couchbase:8091) to start..."
    while true; do
        if command -v curl >/dev/null 2>&1; then
            if curl -s http://couchbase:8091/ui/index.html >/dev/null 2>&1; then
                break
            fi
        elif command -v wget >/dev/null 2>&1; then
            if wget -qO- http://couchbase:8091/ui/index.html >/dev/null 2>&1; then
                break
            fi
        fi
        echo "Couchbase not responding yet. Retrying in 3 seconds..."
        sleep 3
    done
    echo "Couchbase Server HTTP interface is active."
}

wait_for_couchbase

echo "Waiting an extra 5 seconds for background node initialization..."
sleep 5

# Locate couchbase-cli executable
CLI="/opt/couchbase/bin/couchbase-cli"
if [ ! -f "$CLI" ]; then
    CLI="couchbase-cli"
fi

echo "Attempting cluster initialization with couchbase-cli..."

# 1. Initialize cluster 'airline'
$CLI cluster-init -c couchbase:8091 \
    --cluster-name airline \
    --cluster-username "${CB_ADMIN_USER}" \
    --cluster-password "${CB_ADMIN_PASSWORD}" \
    --cluster-ramsize 512 \
    --cluster-index-ramsize 256 \
    --services data,index,query || echo "Notice: cluster-init returned non-zero (cluster may already be initialized)."

sleep 3

# 2. Create bucket 'checkin_bucket'
echo "Attempting to create bucket 'checkin_bucket'..."
$CLI bucket-create -c couchbase:8091 \
    -u "${CB_ADMIN_USER}" -p "${CB_ADMIN_PASSWORD}" \
    --bucket checkin_bucket \
    --bucket-type couchbase \
    --bucket-ramsize 256 \
    --wait || echo "Notice: bucket-create returned non-zero (bucket may already exist)."

echo "===================================================="
echo " Couchbase Auto-Initialization Completed Successfully!"
echo " Cluster: airline"
echo " User: ${CB_ADMIN_USER}"
echo " Bucket: checkin_bucket"
echo "===================================================="
