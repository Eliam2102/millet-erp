# Operación y Runbook — Módulo Almacén

> Construido sobre [01-diseno.md](01-diseno.md), [04-cuidados-infra.md](04-cuidados-infra.md).
>
> **Estado:** v1 (F9-PR1, 2026-05-23).

---

## 1. Despliegue

### 1.1 CI/CD

El despliegue a `dev` lo dispara `.github/workflows/deploy-app-dev.yml` automáticamente en cada push a `main` que toque `backend/` o `frontend/`. Migraciones se aplican en el step `run-migrations` antes del swap del App Service.

`AlmacenDbContext` está registrado en el bucle de migraciones (F0-PR1). El smoke test post-deploy valida `/health/ready` que ejercita `MigrationsAppliedHealthCheck` con el contexto incluido en `MigrationsHealthCheckOptions`.

### 1.2 Ventana de mantenimiento

El módulo opera 19h/día (8am-6pm + 11pm-8am MX). Ventana de mantenimiento entre 6pm-11pm (5h). Para despliegues fuera de la ventana:

- Comunicar al equipo de Almacén con 24h de antelación.
- Confirmar con Eduardo Paredes que no hay conteo Anual en curso (un conteo anual bloquea salidas durante todo el periodo de captura).

---

## 2. Observabilidad

### 2.1 Logs estructurados

Todos los handlers y workers emiten Serilog estructurado con `correlation_id` propagado desde el request. Dimensiones custom relevantes:

- `empresa_id`, `usuario_id` (transversales)
- `movimiento_id`, `recepcion_id`, `salida_id`, `devolucion_id`, `conteo_id` (según contexto)
- `oc_id`, `rq_id`, `proveedor_id` (correlación cross-módulo)

### 2.2 Métricas custom (App Insights)

Métricas a configurar en el dashboard de Almacén:

- **`almacen.vales_sin_regularizar`** — gauge: count de movimientos `SalidaPorVale` con `pendiente_regularizacion=true` y `fecha_limite < now`. Alerta si > 5.
- **`almacen.recepciones_sin_factura`** — gauge: count de movimientos `EntradaCompra` variante B con `factura_id IS NULL` y `fecha_registro` > 30 días. Alerta si > 10.
- **`almacen.saldos_negativos`** — counter: increments cada vez que el trigger PG aborta una salida por saldo insuficiente. Alerta inmediata si > 0.
- **`almacen.devoluciones_pendientes_nc_fiscal`** — gauge: count de `DevolucionAProveedor` en `Registrada` con > 15 días sin conciliar. Alerta si > 3.
- **`almacen.eventos_outbox_pendientes`** — gauge: count de filas con `published_at IS NULL` y `attempts >= 5`. Alerta inmediata.

### 2.3 Tracing distribuido

`AlmacenActivitySource` (Millet.Almacen) propagado por el Azure Monitor OTel Distro. Buscar:

- `OcRecepcionRegistradaEvent` correlation entre Almacén → CxP.
- `OcDevolucionRegistradaEvent` correlation entre Almacén → CxP (8.B).
- `NotaCreditoProveedorRegistrada` correlation desde CxP → Almacén (cierre del ciclo).

---

## 3. Procedimientos operativos

### 3.1 Recalcular saldos manualmente (si trigger falla por bug)

**Cuándo:** el monitor `almacen.saldos_negativos` dispara, o un audit detecta divergencia entre `saldos_inventario.cantidad` y la suma algebraica de movimientos firmes del par (sub_almacen, articulo).

**Procedimiento:**

1. Conectar al Postgres con cuenta admin.
2. Calcular saldo "real" desde movimientos:
   ```sql
   SELECT
       m.sub_almacen_id,
       l.articulo_id,
       SUM(CASE WHEN m.tipo IN (0, 3, 5, 9) THEN l.cantidad ELSE -l.cantidad END) AS cantidad_calculada
   FROM almacen.lineas_movimiento l
   JOIN almacen.movimientos_inventario m ON m.id = l.movimiento_id
   WHERE m.estado = 2 -- Registrado
   GROUP BY m.sub_almacen_id, l.articulo_id;
   ```
3. Comparar con `saldos_inventario.cantidad`. Si diverge: investigar primero (puede haber un bug del trigger; capturar el contexto del INSERT que falló en logs).
4. Si el saldo materializado está MAL, actualizar manualmente y registrar en bitácora:
   ```sql
   UPDATE almacen.saldos_inventario
   SET cantidad = <valor_correcto>, ultima_actualizacion_at = NOW()
   WHERE sub_almacen_id = '<>' AND articulo_id = '<>';
   ```

### 3.2 Re-procesar evento del outbox manualmente

**Cuándo:** el monitor `almacen.eventos_outbox_pendientes` dispara — un evento llegó a `attempts >= 5` y está en dead-letter pasivo.

**Procedimiento:**

1. Inspeccionar `last_error` de la fila:
   ```sql
   SELECT id, event_type, attempts, last_error
   FROM almacen.integration_events_outbox
   WHERE published_at IS NULL AND attempts >= 5;
   ```
2. Si el error es transitorio (Service Bus throttling, network), resetear attempts:
   ```sql
   UPDATE almacen.integration_events_outbox
   SET attempts = 0, last_error = NULL
   WHERE id = '<>';
   ```
3. Si el error es permanente (payload corrupto), exportar el evento para forensics y descartar manualmente con `UPDATE ... SET published_at = NOW()` (después de validar que el consumer ya lo procesó por otra vía).

### 3.3 Cancelar un movimiento firmado (vía contramovimiento)

**Importante:** los movimientos en `Registrado` son INMUTABLES (invariante del dominio). La cancelación se hace por contramovimiento.

**Procedimiento:**

- Para una `EntradaCompra` mal capturada: usar la API de devolución a proveedor (sub-flujo 8.B) si el material fue devuelto físicamente, o `AjusteNegativo` por la cantidad equivocada (requiere conteo formal en F7).
- Para una `SalidaConsumo` mal capturada: usar `DevolucionInterna` (8.A) si el material físicamente regresó, o `AjustePositivo` (conteo).
- NUNCA hacer `UPDATE` directo al campo `estado` ni `DELETE` — rompe la trazabilidad.

### 3.4 Reabrir un conteo aplicado (si se detecta error post-aplicar)

**No soportado en MVP.** Un conteo `Aplicado` es terminal y los movimientos generados son inmutables. Si se detecta error:

1. Generar un nuevo conteo del mismo sub-almacén/familia con la cantidad real correcta.
2. Aprobar y aplicar — generará `AjustePositivo`/`AjusteNegativo` para revertir el error.
3. Anotar en bitácora cruzando los dos `conteo_id` (el erróneo y el corrector).

### 3.5 Gestión de bloqueos de inventario anual atorados

**Cuándo:** un conteo `Anual` quedó en `EnConciliacion` o `Aprobado` por > 7 días, manteniendo el bloqueo de salidas y bloqueando operación.

**Procedimiento:**

- Si la captura está completa pero falta aprobación: contactar al aprobador para acelerar.
- Si la captura falló (bug, datos perdidos): rechazar el conteo. El handler de Rechazar libera los bloqueos.
- En situación de emergencia operativa, un superusuario puede:
  ```sql
  UPDATE almacen.bloqueos_inventario
  SET activo = false, hasta = NOW()
  WHERE conteo_id = '<>' AND activo = true;
  ```
  con bitácora obligatoria.

---

## 4. FAQs operativas

**P: ¿Por qué la cantidad disponible es 0 si hay stock?**
R: Probablemente todo está `cantidad_reservada` (A19). Consultar `almacen.reservas_stock` por `(articulo, sub_almacen, estado=Activa)`.

**P: ¿Por qué una salida marca SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL?**
R: Hay un conteo `Anual` activo. Esperar a que el conteo termine o pasarlo a `Rechazado` (libera bloqueos).

**P: ¿Por qué una recepción marca PERIODO_CERRADO?**
R: La `fecha_movimiento` cae en un mes ya cerrado (`almacen.periodos_cerrados`). El operador debe corregir la fecha o solicitar reabrir el mes (no soportado en MVP).

**P: ¿Por qué el contador no ve la cantidad teórica?**
R: Por diseño (A6). El endpoint del contador (`/lineas-para-capturar`) NO devuelve `cantidad_teorica`. Solo el aprobador la ve vía `/comparacion`.

**P: ¿Por qué una devolución a proveedor sigue en `Registrada` y no en `ConciliadaConNcFiscal`?**
R: CxP aún no ha emitido la NC fiscal tipo CFDI 03 vinculada. El listener `NotaCreditoProveedorRegistradaHandler` matchea por `(proveedor, factura_origen)`. Si CxP la emitió pero no se concilió, revisar `almacen.eventos_procesados` para confirmar que el evento llegó.

---

## 5. PLATFORM-TODOs pendientes al go-live

Inventario completo de stubs `NoOp*` cableados al arranque. Cada uno con su issue/ticket si aplica:

- `<AlmacenCrossModulePorts>` — 9 puertos cross-module (NoOp por defecto).
- `<ComprasOcReadAdapter>` — adapter real cuando Compras lo cablée.
- `<ComprasRqReadAdapter>` — adapter real para validar RQ aprobada.
- `<ArticuloReadAdapter>` — adapter real para tolerancia A5 + UM canónico.
- `<ProveedorReadAdapter>` — RFC + datos para devolución a proveedor.
- `<SucursalReadAdapter>` — jerarquía Sucursal → Almacén.
- ~~`<EmpleadoReadAdapter>`~~ — ✅ cerrado en ADM-PR2: adapter real sobre `compartido.empleados` registrado en `Program.cs`.
- `<TipoCambioReadAdapter>` — multimoneda real (recepciones USD).
- `<ConceptoContableReadAdapter>` — Contabilidad (futuro).
- `<PeriodoContableReadAdapter>` — Finanzas (futuro). `almacen.periodos_cerrados` es la fuente local mientras tanto.
- `<NotificacionService>` — email/SignalR via módulo Notificaciones.
- `<AlmacenCxpSubscription>` — subscripción `almacen-subscription` en topic `cuentas-por-pagar-events` debe crearse en Bicep para que `CxpEventListenerWorker` consuma.

Búsqueda: `rg "PLATFORM-TODO" backend/src/Almacen`.

---

## Rev.

- **2026-05-23 — v1** — Runbook inicial con procedimientos operativos, FAQs y inventario de PLATFORM-TODOs.
