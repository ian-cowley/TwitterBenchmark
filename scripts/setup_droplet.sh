#!/usr/bin/env bash
set -euo pipefail

echo "=========================================================="
echo " TwitterBenchmark C# (.NET 8/9) Droplet Setup Script      "
echo " Target: Ubuntu 22.04 / 24.04 ($12 DigitalOcean Droplet)  "
echo "=========================================================="

# 1. System limits & TCP Tuning
echo "[*] Tuning OS limits and sysctl..."
sudo tee -a /etc/security/limits.conf > /dev/null <<EOT
* soft nofile 65535
* hard nofile 65535
root soft nofile 65535
root hard nofile 65535
EOT

sudo tee -a /etc/sysctl.conf > /dev/null <<EOT
net.core.somaxconn = 65535
net.ipv4.tcp_max_syn_backlog = 65535
net.ipv4.ip_local_port_range = 1024 65535
net.ipv4.tcp_tw_reuse = 1
net.ipv4.tcp_fin_timeout = 15
EOT
sudo sysctl -p

# 2. Update and install prerequisites
echo "[*] Installing dependencies..."
sudo apt-get update
sudo apt-get install -y wget curl git build-essential nginx postgresql postgresql-contrib sqlite3 python3 python3-pip

# 3. Install .NET SDK 8 & 9
echo "[*] Installing .NET SDK..."
sudo apt-get install -y dotnet-sdk-8.0 || {
    wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
    ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
    sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
}

# 4. Install k6
echo "[*] Installing k6 load testing tool..."
if ! command -v k6 &> /dev/null; then
    sudo gpg -k
    sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D34EE73D
    echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
    sudo apt-get update
    sudo apt-get install -y k6
fi

# 5. Configure PostgreSQL
echo "[*] Configuring PostgreSQL database..."
sudo systemctl start postgresql
sudo -u postgres psql -c "CREATE USER benchuser WITH PASSWORD 'benchpass';" || true
sudo -u postgres psql -c "CREATE DATABASE twitter_bench OWNER benchuser;" || true
sudo -u postgres psql -c "GRANT ALL PRIVILEGES ON DATABASE twitter_bench TO benchuser;" || true

# Seed PostgreSQL
echo "[*] Seeding PostgreSQL (50k users, 500k posts, 2M likes)..."
PGPASSWORD=benchpass psql -h localhost -U benchuser -d twitter_bench -f scripts/seed_postgres.sql

# 6. Seed SQLite
echo "[*] Seeding SQLite database..."
python3 scripts/seed_sqlite.py

# 7. Configure Nginx
echo "[*] Setting up Nginx..."
sudo cp scripts/nginx.conf /etc/nginx/nginx.conf
sudo nginx -t
sudo systemctl restart nginx

# 8. Build C# project in Release mode
echo "[*] Building C# benchmark in Release mode..."
dotnet publish -c Release -o ./publish

echo "=========================================================="
echo " Setup complete! Ready to run benchmarks.                 "
echo "=========================================================="
