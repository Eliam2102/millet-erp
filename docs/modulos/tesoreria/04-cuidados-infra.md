# Cuidados de infraestructura — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> Prioridades: **[P0]** rompe el deploy o corrompe datos si se omite; **[P1]** deuda operativa.

---

## 0. Cómo leer

Checklist por área, mismo formato que
[`cuentas-por-cobrar/04-cuidados-infra.md`](../cuentas-por-cobrar/04-cuidados-infra.md).
Los incidentes citados son reales del proyecto; no re-aprenderlos.

---

## 1. Migraciones EF Core

### 1.1 [P0] DbContext nuevo — checklist completo en PR-1
`Program.cs` (AddDbContext + `MigrationsHealthCheckOptions`) **y**
`deploy-app-dev.yml` en el mismo PR. Sin esto el deploy falla con
`/health/ready` 503 (incidente documentado del repo).

### 1.2 [P0] Migration en `IdentidadDbContext` al tocar `PermisosCanonicos`
`HasData(PermisosCanonicos.Todos)` afecta al modelo; sin migration el
deploy aborta con `PendingModelChangesWarning`. Aplica a PR-1 y a
cualquier permiso agregado después.

### 1.3 [P0] Enums persistidos con `HasCheckConstraint`
Los 10 enums del esquema (§5.2 del diseño). Nuevo valor = constraint +
migration + mirror FE (incidente FacturaAnticipo 2026-07-11).

### 1.4 [P1] Revisar migrations como SQL, no como C#
`dotnet ef migrations script` antes de mergear PRs con índices parciales
(el de RN-2 es fácil de generar mal desde anotaciones).

## 2. Outbox + Service Bus (ADR-0009)

### 2.1 [P0] Topics y subscriptions por Bicep, nunca por portal
Subscriptions nuevas de Tesorería en `cuentas-por-pagar-events`,
`cuentas-por-cobrar-events` y `facturacion-events`, con filtro SQL por
`EventType`. **`what-if` antes de aplicar**; recordar que what-if no
detecta cambios de password de Postgres (incidente 2026-07-07) — no
tocar parámetros ajenos en el mismo deploy.

### 2.2 [P0] Payloads espejo byte-compatibles
El publisher de PR-4 serializa exactamente lo que deserializan los
records de `ContratosEspejo.cs:97-141` en CxP (deserialización
case-insensitive, pero nombres y tipos deben coincidir). Test de
contrato obligatorio en PR-4: round-trip evento publicado → record
espejo.

### 2.3 [P0] Idempotencia en los 3 listeners
Dedupe por `evento_procesado (evento_id, evento_tipo)`. El `aplicado.v1`
duplicado del lado CxP ya lo absorbe su propio dedupe, pero el nuestro
evita dobles aplicaciones locales.

### 2.4 [P1] Orden de eventos no garantizado
`recibo-pago.timbrado.v1` puede llegar antes de que la confirmación
exista localmente (o un `pasivo.autorizado` re-emitido tras el pago).
Patrón CxC: `EntityNotFoundException` → abandonar mensaje para retry
(`MaxDeliveryCount=5`); nunca completar y perder.

### 2.5 [P1] Monitoreo de dead-letter
Las 3 subscriptions nuevas entran a la misma vigilancia que las
existentes (alertas App Insights sobre DLQ > 0).

## 3. Backfill y arranque

### 3.1 [P0] Pasivos autorizados ANTES de la subscription
Una subscription nueva solo recibe eventos posteriores a su creación.
Todo pasivo que CxP autorizó antes del deploy de PR-3 **no aparecerá en
la bandeja**. Decidir en PR-3: (a) backfill one-shot leyendo facturas
`Autorizada` con saldo > 0 desde CxP (query dirigida, patrón runbook
ADR-0044), o (b) re-emisión de eventos desde CxP. Recomendada (a) —
menor acoplamiento.

### 3.2 [P0] Saldos iniciales de cuentas [T-G8]
Conciliación no arranca sin saldo inicial por cuenta a fecha de corte.
Seed one-shot documentado en runbook; sin histórico de movimientos SAP.

### 3.3 [P1] Seed de cuentas bancarias
Números de cuenta/CLABE reales **no van en el repo** (seed con
placeholders + carga por script en dev/prod, o valores vía Key Vault).
Mismo criterio que secretos operativos (ADR-0037).

## 4. Seguridad y datos sensibles

- **[P0]** CLABE y número de cuenta enmascarados en logs (enricher
  Serilog existente cubre CLABE — verificar que cubra los nuevos campos)
  y en DTOs por defecto; des-enmascarar solo con
  `tesoreria.movimientos.ver-cuenta-completa`.
- **[P0]** El payload de `pago-cliente.confirmado.v1` no incluye datos
  bancarios del cliente — solo referencias.
- **[P1]** XML de REPP y archivos de extracto a Blob privado (ADR-0024),
  nunca a disco del App Service.

## 5. Concurrencia

- Optimista vía `xmin` en corrida, conciliación y depósito (ADR-0012);
  `VersionEsperada` en comandos de confirmación/autorización.
- El gate RN-2 se respalda con índice parcial único (no confiar solo en
  la validación del command ante dobles submit).

## 6. Performance

Volúmenes triviales (§1.3 del diseño). Único punto de atención: el
matching de conciliación sobre extractos grandes (miles de líneas) debe
correr en SQL (join por referencia/monto/fecha), no en memoria N×M.

## 7. Observabilidad

- Correlation IDs propagados por los 3 listeners y el outbox (ADR-0006).
- Métricas mínimas: pasivos en bandeja, pagos a cuenta abiertos (>N días
  = warning), depósitos pendientes de confirmar, DLQ por subscription.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
