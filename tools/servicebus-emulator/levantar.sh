#!/usr/bin/env bash
# Levanta el emulador local de Service Bus con los temas del Bicep.
# Requisitos: Docker Desktop (o OrbStack) y python3.
# Al correrlo se aceptan las licencias de uso de las imágenes de Microsoft
# (emulador de Service Bus y Azure SQL Edge), solo para desarrollo local.
set -euo pipefail
cd "$(dirname "$0")"

python3 generar-config.py
SB_ACCEPT_EULA=Y docker compose up -d

echo "Esperando a que el emulador termine de arrancar..."
for _ in $(seq 1 60); do
  if docker logs millet-servicebus-emulator 2>&1 | grep -q "Emulator Service is Successfully Up"; then
    echo "Emulador listo en localhost:5672."
    echo "Arranca el backend con: ServiceBus__Modo=EmuladorLocal (ver README.md)."
    exit 0
  fi
  sleep 5
done
echo "El emulador no confirmó el arranque en 5 minutos. Revisa: docker logs millet-servicebus-emulator" >&2
exit 1
