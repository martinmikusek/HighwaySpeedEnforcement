#!/usr/bin/env bash
# Usage: run.sh <default|tuned> <order|redeliver|queue|slow>
# Starts a fresh Mosquitto container with the chosen config, runs one spike scenario, removes the container.
set -e
conf="$1"; scenario="$2"
here="$(pwd -W 2>/dev/null || pwd)"
docker rm -f mosq-spike >/dev/null 2>&1 || true
MSYS_NO_PATHCONV=1 docker run -d --name mosq-spike -p 1883:1883 \
  -v "$here/mosquitto-$conf.conf:/mosquitto/config/mosquitto.conf:ro" eclipse-mosquitto:2 >/dev/null
sleep 2
dotnet run --project MqttSpike --no-build -- "$scenario" "(broker config: $conf)"
docker rm -f mosq-spike >/dev/null
