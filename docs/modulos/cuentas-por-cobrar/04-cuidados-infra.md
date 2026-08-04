# Cuidados de infraestructura — Módulo Cuentas por Cobrar (`Millet.CuentasPorCobrar`)

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Prioridades:** [P0] bloquea merge/deploy · [P1] antes de go-live · [P2] post-MVP

---

## 0. Cómo leer

Checklist operativo por área, con los incidentes previos del proyecto como
referencia. Todo cambio a `infra/` pasa por `what-if` antes de `create`.

## 1. Migraciones EF Core

### 1.1 [P0] DbContext nuevo — checklist completo en PR-1
`CuentasPorCobrarDbContext` exige, **en el mismo PR**: `Program.cs`
(`AddDbContext` + registrar el contexto en `MigrationsHealthCheckOptions.ContextTypes`)
y `deploy-app-dev.yml`. Sin esto el deploy falla con `/health/ready` 503
(incidente documentado del proyecto).

### 1.2 [P0] Migration en `IdentidadDbContext` al tocar `PermisosCanonicos`
El seed `HasData(PermisosCanonicos.Todos)` afecta el modelo; sin migration el
deploy aborta con `PendingModelChangesWarning`. Aplica a PR-1 y a cualquier
permiso posterior.

### 1.3 [P0] Enums persistidos con `HasCheckConstraint`
Todos los `estado/resultado/canal/comportamiento/tipo` del esquema (incidente
`FacturaAnticipo` 2026-07-11, #526/#528). Al agregar un valor: constraint +
migration + mirror FE en el mismo PR. `grep HasCheckConstraint` antes de tocar
un enum.

### 1.4 [P1] Revisar migrations como SQL, no como C#
`dotnet ef migrations script` en el PR cuando la migration no sea trivial
(índices parciales del diseño §5.1, uniques condicionales de `linea_credito`).

## 2. Outbox + Service Bus (ADR-0009)

### 2.1 [P0] Topic y subscriptions por Bicep, nunca por portal
- Topic `cuentas-por-cobrar-events`: llega con el primer PR publicador (PR-4 o PR-7).
- Subscription `cuentas-por-cobrar-subscription` en `facturacion-events`: PR-3.
- Subscription futura `cuentas-por-cobrar-tesoreria-sub` en `tesoreria-events`: cuando se cierre `<PagoClienteConfirmado>`.
Cualquier setting nuevo de `Program.cs` (nombres de topic/subscription, toggles
de workers) lleva su entrada en `appservice.bicep` en el mismo PR — la lista
declarativa pisa lo manual del portal en cada deploy (#463, #467).

### 2.2 [P0] Idempotencia en `FacturacionEventListenerWorker`
Correlación por id del comprobante origen (`FacturaVentaId`, `ReciboPagoId`,
`NotaCreditoId`, `ComprobanteId`, `CobroMostradorId`). Replays no duplican filas
de `factura_cartera` ni acumulados (`monto_pagado`, `monto_nc`). Test de replay
obligatorio en PR-3.

### 2.3 [P1] Orden de eventos no garantizado
Un `ReciboPagoTimbrado` puede llegar antes que su `FacturaVentaTimbrada` (o la
factura puede ser previa al arranque del módulo). El listener debe tolerar
pago-sin-factura: encolar/persistir como pendiente de correlación, no
dead-letter inmediato. Definir política de reintento antes de PR-3.

### 2.4 [P1] Monitoreo de dead-letter
Alerta sobre DLQ de `cuentas-por-cobrar-subscription`: cada mensaje muerto es
una factura/pago que la cartera no refleja (cartera inflada o saldo errado).

## 3. Backfill y arranque de la proyección

### 3.1 [P0] Cartera solo-ERP desde el arranque (CXC-3)
`factura_cartera` inicia vacía y se llena por eventos. Las facturas timbradas
**antes** del deploy de PR-3 no tienen evento que las materialice: definir
backfill one-shot (script desde el esquema `facturacion`, ejecutado una vez,
fuera del código del módulo) o aceptar el corte. Decidir con Eduardo antes de
mergear PR-3.

### 3.2 [P1] Migración histórica SAP
Extractor one-shot (ADR-0044) separado del módulo; nunca vistas vivas contra
SAP. Alcance pendiente de Prida (saldos abiertos vs histórico).

## 4. A+W / on-prem (solo PR-9)

- [P0] El ERP no escribe a A+W: solo tabla-puente `MILLET_INTEGRACION` (ADR-0048).
- [P0] SER-DATA es SQL Server 2016 RTM: sin `CREATE OR ALTER` ni `STRING_AGG`; patrón stub+`ALTER`, `FOR XML PATH`.
- [P0] Cambios a `03_create_views.sql` se **re-corren a mano** en SER-DATA (incidente 2026-07-08: ingesta en 0 por vista desactualizada). Coordinar la corrida en el PR.
- [P1] Hybrid Connection usa hostname sintético `aw-sql-onprem` (no IP literal, no hostname propio — incidente IPv6).

## 5. Concurrencia

- [P0] Optimistic concurrency (`version` + ETag/If-Match, ADR-0012) en `LineaCredito` y `PropuestaAplicacionPago`.
- [P0] `DecidirLiberacionCommand` consume la `AutorizacionCredito` en la **misma transacción** que persiste la decisión (evitar doble consumo bajo carrera).
- [P1] Dos usuarios proponiendo aplicación sobre el mismo depósito: unique sobre `deposito_ref` activo o soft-lock; decidir en PR-7.

## 6. Seguridad y datos sensibles

- [P0] Límites de crédito y estados de bloqueo son datos comerciales sensibles: lectura tras `cartera.leer`/`lineas-credito.leer`, sin endpoints anónimos, sin exponer en logs.
- [P0] Overrides auditados por diseño (quién otorgó, quién consumió, motivo) — nunca aceptar override sin `AutorizacionCredito` persistida.
- [P1] Remittances adjuntos (si se suben archivos) van a Azure Blob privado con SAS de corta vida, patrón evidencias de CxP.

## 7. Performance

- [P1] Bandejas paginadas server-side siempre (cartera puede crecer sin límite temporal).
- [P1] Índices del diseño §5.1 desde la migration que crea cada tabla, no después.
- [P2] Si la antigüedad se vuelve pesada, vista materializada refrescada por el worker — no optimizar antes.

## 8. Observabilidad

- [P0] Serilog estructurado con `correlation_id` en listener y workers (patrón de la triada).
- [P1] Métricas mínimas: eventos consumidos/descartados por tipo, propuestas por estado, alertas generadas, lag del listener.
- [P1] El `AlertaCarteraWorker` loggea cada corrida (evaluadas/generadas) para auditar el "riesgo #1 de Prida" (SOLUNION 90d).

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
