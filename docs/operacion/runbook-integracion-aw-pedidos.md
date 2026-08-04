# Runbook — Integración de pedidos A+W → Facturación (DDL on-prem + E2E)

> **Audiencia:** owner ERP (Eduardo) + DevOps Tiglass.
> **Versión:** 1.0.0 · 2026-07-05
> **Contexto:** [ADR-0048](../decisiones/0048-bd-integracion-aw-pedidos.md) ·
> [doc 04](../integration/04-ingesta-pedidos-facturacion.md) ·
> [spec customización A+W](../integration/runbooks/aw-solicitud-pedido-customizing.md)
> **Scripts:** [`aw-integracion-scripts/`](aw-integracion-scripts/)

Este runbook cubre los pasos **manuales on-prem/Azure** (M1–M3 del plan). El
código del ERP (adapters, masters, write-back) va por PRs normales.

---

## M1 — Preparar SER-DATA (una vez, antes de PR3)

En SSMS/sqlcmd contra la instancia `SERDATA\AWBUSINESS`, **como sysadmin**.
El engine es **SQL Server 2016 RTM Standard sin SP1 (13.0.1601.5)** — los
scripts evitan `CREATE OR ALTER` y `STRING_AGG` por eso; cualquier SQL nuevo
para SER-DATA debe respetar esa restricción:

1. **`01_create_database.sql`** — crea `MILLET_INTEGRACION` +
   `MILLET_INTEGRACION_DEV` con la colación de `MILMAIN` (la BD de A+W).
2. **`02_create_table.sql`** — tabla `aw_solicitud_pedido` + índices.
   ⚠️ Ejecutar **dos veces** (cambiar `USE` a `_DEV` la segunda).
3. **`03_create_views.sql`** — vistas (borrador con marcas `-- VALIDAR:`; se
   afinan en la fase guiada, ver M2.3). También en ambas BDs.
4. **`04_create_login.sql`** — login `millet_erp_integracion` + grants
   mínimos (incluye SELECT sobre las tablas fuente en `MILMAIN`).
   La contraseña se pasa en el momento (`:setvar Password …`), **no queda en
   ningún archivo**.
5. **Secreto KV (dev):**

   ```bash
   az keyvault secret set \
     --vault-name <kv-millet-dev> \
     --name aw-integracion-connection-string \
     --value "Server=SER-DATA,1433;Database=MILLET_INTEGRACION_DEV;User ID=millet_erp_integracion;Password=<pwd>;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10;"
   ```

   (`Encrypt=False` mientras siga vigente `<SqlTlsHardening>` — mismo criterio
   que `aw-sql-connection-string`.)

6. **Entregar la spec al equipo A+W** —
   [`aw-solicitud-pedido-customizing.md`](../integration/runbooks/aw-solicitud-pedido-customizing.md)
   con los gaps G1–G13 (§7). Su trabajo corre en paralelo; nuestro E2E no
   depende de ellos (usamos seeds).

7. **Nudge de baja latencia (PR7, opcional pero recomendado):**
   - Generar la key: `openssl rand -base64 32`.
   - Cargarla en Key Vault (NO como app setting manual — el deploy de infra
     borra los app settings no declarados en Bicep; incidente 2026-07-07):
     `az keyvault secret set --vault-name <kv-millet-dev> --name aw-pedido-nudge-api-key --value '<key>'`
     seguido de `az webapp restart`. La KV ref ya está declarada en
     `appservice.bicep`; sin el secreto, el endpoint /pedidos/nudge responde
     401 y el worker sigue barriendo por intervalo.
   - Publicar el exe: `dotnet publish on-prem/aw-pedido-notify -c Release -r win-x64 --self-contained false`,
     copiar el publish a SER-DATA (`C:\Millet\aw-pedido-notify\`) y editar su
     `appsettings.json` (NudgeUrl del ambiente + la misma key).
   - Smoke: `MilletAwPedidoNotify.exe TEST alta` → exit 0 y en el log del
     App Service aparece el tick inmediato del `AwSolicitudesWorker`.
   - La customización A+W lo llama tras cada INSERT (spec §3.bis).

## M2 — Encender dev y validar vistas (después de PR3)

1. **Deploy infra** (agrega el app setting `ConnectionStrings__AwIntegracionDb`
   desde KV): `what-if` primero, SIEMPRE:

   ```bash
   az deployment sub what-if --location mexicocentral \
     --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam
   az deployment sub create  --location mexicocentral \
     --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam
   ```

2. **Smoke de conectividad:** con el App Service dev arriba, verificar en logs
   que el health check `aw-integracion-sql` reporta OK (o tick del worker sin
   errores de conexión).
3. **Fase guiada de vistas:** ejecutar cada `SELECT TOP 20` de las vistas
   contra pedidos reales; corregir las marcas `-- VALIDAR:` de
   `03_create_views.sql` (nombres de columnas de catálogos, firma de
   `DEVUELVE_IMPORTE_Y_DESCTO_N`, RtfToText, domicilio en KU_KUNDEN, estatus
   del pedido). Cada corrección se commitea al script (fuente de verdad).
4. **Worker habilitado en dev:** app settings del App Service dev →
   `Facturacion__Workers__AwSolicitudes__EmpresaId = <guid empresa dev>`
   (con `Guid.Empty` el worker no procesa — es el candado #2).
5. **Relaciones A+W ↔ ERP** (rediseño 2026-07-07, doc 04 §4 — NO son app
   settings):
   - **Sucursal:** en `/admin/empresas` → sucursales, capturar la **Clave
     A+W** de cada sucursal que recibe pedidos (CIRCUITO, CHICHI SUAREZ,
     CANCUN, CONKAL). Sin clave A+W o sucursal inactiva → el pedido cae a
     la bandeja de excepciones.
   - **Canal / comportamiento fiscal:** los traduce la vista
     `vw_erp_pedido_cabecera` a nombres de enum del ERP; el CASE vive en
     `03_create_views.sql` (fuente de verdad, se corrige por script + git).
     Un GRUPPE sin rama en el CASE pasa crudo y cae a la bandeja con el
     valor en el log del reader. Pendientes: GRUPPE reales restantes (gap
     G6) y regla canal→comportamiento con el equipo fiscal (gap G14).
   - Los diccionarios `IntegracionesAw__Pedidos__MapeoCanalVenta__*` /
     `__MapeoComportamiento__*` existen solo como **override de emergencia**
     (llave sin espacios) — el camino normal es corregir la vista.

## E2E "nuestro lado" (repetible)

1. **`05_seed_dev.sql`** en `MILLET_INTEGRACION_DEV` — editar antes los
   `<<PEDIDO_REAL_N>>` con pedidos reales representativos (el propio script
   sugiere el query para elegirlos). Arma 6 escenarios: alta, modificación,
   duplicado, huérfana, cancelación, fuera-de-orden.
2. Esperar un tick del `AwSolicitudesWorker` (o bajar `IntervalSeconds` en dev).
3. **Verificar en el ERP (Postgres dev):**
   - `facturacion.pedido_facturable` + `_linea`: pedidos creados, importes y
     líneas correctos, `bom_json` poblado.
   - `facturacion.pedido_facturable_snapshot`: payload JSON íntegro.
   - `facturacion.ingesta_control`: una fila por pedido, `ultima_version_aplicada`
     correcta.
   - `facturacion.bandeja_excepcion_importacion`: el escenario D (huérfana) y
     los clientes/artículos no provisionables.
4. **Verificar write-back en SQL Server** (query al final de `05_seed_dev.sql`):
   claim + resultado por solicitud.
5. **Idempotencia:** re-ejecutar el seed → mismas versiones → el ERP responde
   Aplicada sin reprocesar (no se duplican pedidos).
6. **Ciclo fiscal:** emitir una factura de prueba sobre uno de los pedidos →
   verificar que `WriteBackResultadoWorker` escribe `uuid` +
   `estado_facturacion='Facturado'` en la tabla-puente.

## M3 — Encender producción (go-live)

Pre-requisitos: E2E verde en dev, customización A+W validada (smoke §8 de su
spec), UI de clientes/productos-aw desplegada, gaps G1–G13 cerrados.

1. Secreto KV **prod**: `aw-integracion-connection-string` →
   `Database=MILLET_INTEGRACION` (la de prod).
2. Deploy infra prod (`what-if` + segundo par de ojos, regla del repo).
3. App setting prod: `Facturacion__Workers__AwSolicitudes__EmpresaId`.
4. Activar la customización A+W (INSERT reales).
5. Monitorear la primera semana: bandeja de excepciones
   (`/facturacion/excepciones`), logs del worker, y
   `SELECT resultado, COUNT(*) FROM aw_solicitud_pedido GROUP BY resultado`.

## Rollback / apagado de emergencia

- **Apagar la ingesta:** app setting `Facturacion__Workers__AwSolicitudes__Disabled = true`
  (o vaciar `EmpresaId`). Las solicitudes se acumulan en la tabla-puente y se
  procesan al reactivar — no se pierde nada (doble candado).
- **Apagar solo el write-back:** `Facturacion__Workers__AwWriteBack__Disabled = true`
  (queda pendiente en `ingesta_control` y se drena al reactivar).
- **Revertir DDL:** bloques de rollback comentados al final de cada script.
