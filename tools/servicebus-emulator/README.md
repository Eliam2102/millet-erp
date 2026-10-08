# Emulador local de Service Bus

Sirve para que, en una sola computadora, los módulos del ERP se pasen sus
eventos (por ejemplo: Almacén registra una recepción → CxP y Compras se
enteran). Sin él, en local el ERP usa `NoOpIntegrationEventBusSender` y
descarta los eventos.

Solo es para desarrollo y demostraciones locales. En Azure se usa el Service
Bus real de `infra/modules/servicebus.bicep`, que no cambia.

## Cómo levantarlo (cualquier Mac)

Requisitos: Docker Desktop u OrbStack, y python3.

```bash
./tools/servicebus-emulator/levantar.sh
```

El script:
1. Genera `Config.json` desde el Bicep (`generar-config.py`): mismos temas,
   suscripciones y filtros SQL que en Azure.
2. Descarga y levanta las dos imágenes de Microsoft. La primera vez descarga
   unos 750 MB y acepta sus licencias de uso para desarrollo.
3. Espera hasta que el emulador confirma que está listo (`localhost:5672`).

## Cómo conectar el backend

El interruptor es `ServiceBus:Modo`:

| Valor | Qué hace |
|---|---|
| `Azure` (o vacío) | Comportamiento de siempre: usa `ServiceBus:ConnectionString`; si está vacía, descarta los eventos |
| `EmuladorLocal` | Usa el emulador local. Solo se permite en `Development` |

Por variable de entorno, al arrancar la API:

```bash
ServiceBus__Modo=EmuladorLocal dotnet run --project backend/src/Api/Millet.Api.csproj
```

O de forma permanente en la máquina, con user-secrets:

```bash
dotnet user-secrets set "ServiceBus:Modo" "EmuladorLocal" --project backend/src/Api/Millet.Api.csproj
```

Para volver al comportamiento anterior: `dotnet user-secrets remove "ServiceBus:Modo" --project backend/src/Api/Millet.Api.csproj`.

## Si cambia el Bicep

Vuelve a correr `python3 tools/servicebus-emulator/generar-config.py` y
reinicia el emulador (`docker compose -f tools/servicebus-emulator/docker-compose.yml restart`).

## Apagar

```bash
docker compose -f tools/servicebus-emulator/docker-compose.yml down
```

## Avisos

- Con el emulador activo, al confirmar Tesorería un pago de cliente,
  Facturación emite el complemento de pago automáticamente
  (`TesoreriaEventListenerWorker`). La regla de Millet es que sea manual;
  lo corrige U1.1.
- Azure SQL Edge (base interna del emulador) fue retirada por Microsoft en
  septiembre de 2025. Funciona para uso local; si deja de estar disponible,
  cambia la imagen `sqledge` por `mcr.microsoft.com/mssql/server:2022-latest`.
