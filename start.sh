#!/bin/bash

# Start FC Engine Admin and Portal

trap 'kill 0; exit' SIGINT SIGTERM

echo "Starting FC Engine Admin (http://localhost:5001) and Portal (http://localhost:5003)..."

ASPNETCORE_URLS="http://localhost:5001" dotnet run --project "FC Engine/src/FC.Engine.Admin" &
ASPNETCORE_URLS="http://localhost:5003" dotnet run --project "FC Engine/src/FC.Engine.Portal" &

wait
