# Cuidados de infraestructura — Módulo Almacén (`Millet.Almacen`)

> **Construido sobre:** [01-diseno.md](01-diseno.md), [02-plan-implementacion.md](02-plan-implementacion.md), [03-pr-breakdown.md](03-pr-breakdown.md).
>
> **Hereda contexto de:** [`docs/modulos/compras-ordenes-compra/04-cuidados-infra.md`](../compras-ordenes-compra/04-cuidados-infra.md) y [`docs/modulos/cuentas-por-pagar/04-cuidados-infra.md`](../cuentas-por-pagar/04-cuidados-infra.md). Los cuidados de plataforma compartida no se duplican.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

Cada cuidado tiene cuatro líneas (qué / por qué importa / cómo verificamos / qué pasa si lo ignoramos). Prioridades: **P0** bloqueante · **P1** importante · **P2** trazable.

---

## 1. Migraciones EF Core

### 1.1 [P0] `dotnet ef migrations script` adjunto al PR

Ver §1.1 del 04 de Compras-OC. Aplica a las **~12 migraciones** de Almacén.

### 1.2 [P0] Multi-DbContext registrado

- **Qué**: `AlmacenDbContext` se registra en `MigrationsHealthCheckOptions.ContextTypes` y en `.github/workflows/deploy-app-dev.yml` con `dotnet ef database update --context AlmacenDbContext`. Memoria explícita.
- **Por qué importa**: sin esto, deploy falla con `/health/ready 503`.
- **Cómo lo verificamos**: smoke test post-deploy.
- **Qué pasa si lo ignoramos**: rollback manual.

### 1.3 [P0] Re-localización del placeholder `Almacen` (F1)

- **Qué**: F1-PR1 hace `copy-from` de `compartido.almacenes` a `almacen.almacenes`. F1-PR3 hace `DROP` del placeholder. Coordinación con módulo Administración: validar que ningún módulo lee directo de `compartido.almacenes` antes de mergear F1-PR3.
- **Por qué importa**: DROP irreversible. Si Compras o CxP consultan directo a `compartido.almacenes`, se rompen.
- **Cómo lo verificamos**: pre-DROP, ejecutar `rg "compartido\.almacenes" backend/src/` → debe retornar 0 hits. Después del DROP, smoke tests de todos los módulos.
- **Qué pasa si lo ignoramos**: regresion masiva al mergear F1-PR3.

### 1.4 [P0] Trigger BEFORE INSERT sobre `movimientos_inventario` para actualizar `saldos_inventario`

- **Qué**: F2-PR2 introduce trigger PostgreSQL que actualiza `saldos_inventario` en la misma transacción que `INSERT INTO movimientos_inventario (..., estado='Registrado')`. CHECK `cantidad >= 0` previene saldos negativos.
- **Por qué importa**: la materialización del saldo es la base de toda consulta de stock. Si la lógica no es transaccional, hay race conditions.
- **Cómo lo verificamos**: F2-PR2 incluye test concurrente (100 inserts paralelos → saldo final correcto + 0 negativos). Test de rollback (transacción aborta → saldo intacto).
- **Qué pasa si lo ignoramos**: saldos desincronizados; inventario teórico vs real divergen sin conteo.

### 1.5 [P0] `pg_trgm` no se usa en Almacén

Aclaración para evitar confusión: el módulo CxP usa `pg_trgm` para fuzzy match de merchants de TC. Almacén **NO** lo necesita en MVP. Si se agrega vNext (búsqueda fuzzy de artículos), evaluar.

### 1.6 [P1] CHECKs en tablas de inventario

- **Qué**: `lineas_movimiento.cantidad > 0`, `saldos_inventario.cantidad >= 0`, `lineas_conteo` con unicidad `(conteo_id, articulo_id, sub_almacen_id)`.
- **Por qué importa**: defensiva contra bugs de lógica.
- **Cómo lo verificamos**: tests integración intentan violar cada CHECK; deben fallar con `check_violation`.
- **Qué pasa si lo ignoramos**: datos inconsistentes.

---

## 2. Outbox + Service Bus

### 2.1 [P0] Outbox de Almacén independiente

- **Qué**: tabla `almacen.outbox_messages` con `OutboxPublisherWorker<AlmacenDbContext>`. Aislado de Compras/CxP.
- **Por qué importa**: aislamiento de fallas.
- **Cómo lo verificamos**: F0-PR2.
- **Qué pasa si lo ignoramos**: cross-module coupling.

### 2.2 [P0] Idempotencia en consumidores

- **Qué**: tabla `almacen.eventos_procesados` (`(evento_id, evento_tipo)`). Wrapper genérico de listener (A12 del 01-diseno).
- **Por qué importa**: at-least-once delivery.
- **Cómo lo verificamos**: F3-PR2 incluye test.
- **Qué pasa si lo ignoramos**: doble conciliación; saldos incorrectos.

### 2.3 [P0] Race condition con `OrdenCompraAutorizadaEvent`

- **Qué**: si `OcRecepcionRegistradaEvent` se publica antes de que Almacén procese `OrdenCompraAutorizadaEvent` (race condition), el listener debe poder manejarlo. Patrón: si OC no conocida → guardar en estado pendiente, procesar al recibir el evento de autorización.
- **Por qué importa**: Service Bus no garantiza orden cross-topic.
- **Cómo lo verificamos**: F3-PR2 incluye test con eventos invertidos.
- **Qué pasa si lo ignoramos**: recepciones rechazadas espuriamente.

### 2.4 [P1] Naming canónico `{Agregado}{Verbo}Event`

Convención compartida. Code review rechaza nombres que no la sigan.

### 2.5 [P1] Lecturas de presentación hacia CxP — solo etiquetas, nunca lógica

- **Qué**: `ICxpDocumentosReadPort` (fix/guids-almacen-cxp) es el primer puerto de
  lectura Almacén → CxP. Resuelve **solo etiquetas de presentación** (folio de
  factura/NC del proveedor, UUID fiscal del CFDI) para no pintar GUIDs internos.
  Adapter hosteado en `CuentasPorPagar.Infrastructure.PublicAdapters` (bypass de
  empresa, state-agnostic, molde `CxpFacturasTrazabilidadProvider`).
- **Por qué importa**: la conciliación de la triada (variante B, NC fiscal 8.B)
  viaja **exclusivamente por eventos** (§2.2). Si un handler empieza a usar este
  puerto para decidir lógica de negocio, se rompe la frontera del bounded context.
- **Nota de contrato**: `RecepcionDetalle` ganó `cfdiUuidFiscal`/`facturaFolio`,
  `DevolucionProveedorDetalle` ganó `notaCreditoFolio`/`proveedorNombre`,
  `DevolucionProveedorListItem` ganó `proveedorNombre` y
  `DevolucionProveedorLineaItem` ganó `articuloClave`/`articuloDescripcion`
  (ADR-0042). `IProveedorReadPort` ganó `ObtenerPorIdsAsync` batch. Cambios
  **aditivos** (campos `null` si el puerto no resuelve; el FE cae al id truncado).

---

## 3. Saldo materializado — el cuidado más crítico de Almacén

### 3.1 [P0] Actualización transaccional con el movimiento

- **Qué**: cuando un movimiento pasa a `Registrado`, el trigger o handler actualiza `saldos_inventario` en la misma transacción. **NO** hay job batch que recalcula.
- **Por qué importa**: cualquier asincronía abre ventana de inconsistencia.
- **Cómo lo verificamos**: code review rechaza patrones async. Tests de integración validan rollback.
- **Qué pasa si lo ignoramos**: stock disponible mostrado distinto al real; sobre-venta.

### 3.2 [P0] Lock por `(sub_almacen, articulo)` durante salida

- **Qué**: el comando `RegistrarSalidaCommand` hace `SELECT ... FOR UPDATE` sobre `saldos_inventario` para la combinación `(sub_almacen, articulo)` antes de validar stock + insertar movimiento. Previene race entre dos salidas concurrentes que dejarían saldo negativo.
- **Por qué importa**: dos almacenistas surtiendo simultáneamente con stock 5, ambos validan 3, ambos surten 3 → stock final -1.
- **Cómo lo verificamos**: F4-PR1 + test concurrente. PostgreSQL advisory locks pueden ser alternativa.
- **Qué pasa si lo ignoramos**: stock negativo en producción; deuda de reconciliación.

### 3.3 [P0] Costo promedio ponderado actualizado al recibir

- **Qué**: al registrar entrada, actualizar `saldos_inventario.costo_promedio_mxn` con la fórmula del §10.5 del levantamiento. Operación atómica.
- **Por qué importa**: salidas posteriores se valoran al costo promedio actual; si el cálculo está mal, contabilidad arrastra el error.
- **Cómo lo verificamos**: F2-PR2 incluye tests con casos de borde (saldo inicial 0, recepciones consecutivas).
- **Qué pasa si lo ignoramos**: estados financieros incorrectos.

### 3.4 [P0] Snapshot de costo en líneas de salida (para devolución posterior)

- **Qué**: `lineas_movimiento.costo_unitario_mxn` se snapshottea al registrar la salida. Las devoluciones internas usan este snapshot (A9 del 01-diseno).
- **Por qué importa**: si una salida fue al costo 100 y devuelven un mes después con costo promedio actual 120, la diferencia distorsiona contabilidad.
- **Cómo lo verificamos**: F4-PR1 valida que el snapshot se persiste; F5-PR4 valida que la devolución lo usa.
- **Qué pasa si lo ignoramos**: ajustes contables manuales recurrentes.

### 3.5 [P1] Validación pre-salida vs `saldos_inventario`

- **Qué**: antes de validar la salida, leer `saldos_inventario.cantidad` con `FOR UPDATE`. Si `cantidad < solicitado` → 422.
- **Por qué importa**: feedback rápido al usuario.
- **Cómo lo verificamos**: F4-PR1.
- **Qué pasa si lo ignoramos**: salidas fallan en el commit con mensaje técnico.

---

## 4. Inventario físico (F7) — caso especial

### 4.1 [P0] Snapshot inmutable al iniciar conteo

- **Qué**: al pasar `ConteoInventario.estado = EnCurso`, capturar snapshot de `cantidad_teorica` por línea en transacción. Snapshot **no se actualiza** durante el conteo.
- **Por qué importa**: si el snapshot cambia durante el conteo, el aprobador compara contra blanco móvil.
- **Cómo lo verificamos**: F7-PR2 incluye test con movimientos durante el conteo.
- **Qué pasa si lo ignoramos**: aprobación incorrecta; ajustes erróneos.

### 4.2 [P0] Captura sin sesgo — endpoint nunca devuelve cantidad teórica

- **Qué**: F7-PR3 endpoint `GET /conteos/{id}/lineas-para-capturar` filtra el campo `cantidad_teorica` del DTO. **Solo el aprobador** lo ve via endpoint distinto (`GET /conteos/{id}/lineas/{linea_id}/comparacion`).
- **Por qué importa**: la "captura sin sesgo" no funciona si el contador puede llamar al endpoint del aprobador.
- **Cómo lo verificamos**: F7-PR3 incluye test E2E con permiso de contador (sin acceso a comparison endpoint).
- **Qué pasa si lo ignoramos**: la mejora de proceso (§7.6 del levantamiento) queda sin efecto.
- **Nota de contrato (fix/conteo-operable-y-legible)**: ambos DTOs de líneas ganaron
  campos opcionales `articuloClave`/`articuloDescripcion`/`subAlmacenClave` (ADR-0042,
  batch vía `IArticuloReadPort` + JOIN local) y `ConteoDetalle` ganó
  `subAlmacenClave`/`responsableNombre`/`aprobadorNombre` (`IUsuarioReadPort`). Cambio
  **aditivo** (los campos llegan `null` si el puerto no resuelve; el FE cae al id
  truncado). La descripción del artículo **no** compromete A6: no revela teórico. Las
  líneas se ordenan por clave de artículo + rack, ya no por GUID.

### 4.3 [P0] Bloqueo de salidas durante inventario anual

- **Qué**: F7-PR7 valida `bloqueos_inventario` en `RegistrarSalidaCommand`. Si hay bloqueo activo para el sub-almacén → 422 con mensaje claro.
- **Por qué importa**: si una salida se cuela durante el conteo, el snapshot queda inválido.
- **Cómo lo verificamos**: test integración.
- **Qué pasa si lo ignoramos**: conteo anual con margen de error inaceptable.

### 4.4 [P1] Bulk approve para variaciones bajo umbral

- **Qué**: pantalla de aprobación permite aprobar N líneas con un click si todas están bajo umbral.
- **Por qué importa**: con 10K artículos por sub-almacén, aprobar uno por uno es prohibitivo.
- **Cómo lo verificamos**: F7-PR5 incluye endpoint `POST /conteos/{id}/aprobar-bulk` con filtro.
- **Qué pasa si lo ignoramos**: el aprobador no usa el sistema; vuelve a Excel.

---

## 5. Concurrencia (heredado, refuerzo)

Ver §5 del 04 de CxP. Aplica idéntico. Adicional para Almacén:

### 5.1 [P0] Soft lock para captura simultánea de conteo

- **Qué**: `ConteoInventario` se declara en soft lock. Dos contadores capturando el mismo conteo se avisan mutuamente.
- **Por qué importa**: pisar capturas perdidas.
- **Cómo lo verificamos**: test E2E.
- **Qué pasa si lo ignoramos**: trabajo perdido.

---

## 6. Periodos contables y cierre de mes

### 6.1 [P0] Validación de periodo cerrado en TODOS los comandos con `fecha_movimiento`

- **Qué**: cualquier comando que cree un movimiento con `fecha_movimiento` debe validar contra `almacen.periodos_cerrados` o `IPeriodoContableReadPort`. Si periodo cerrado → 422.
- **Por qué importa**: movimientos retroactivos a periodos cerrados distorsionan reportes históricos firmados.
- **Cómo lo verificamos**: F8-PR5 valida; tests de cada comando.
- **Qué pasa si lo ignoramos**: cierre de mes inutilizable; auditoría externa rechaza.

### 6.2 [P0] No cerrar mes con conteos pendientes

- **Qué**: `EjecutarCierreMensualCommand` valida que no haya `ConteoInventario.estado IN ('EnConciliacion', 'Aprobado')` con fecha del mes. Si los hay → 422.
- **Por qué importa**: cerrar mes con ajustes pendientes deja saldos incorrectos.
- **Cómo lo verificamos**: F8-PR4 test.
- **Qué pasa si lo ignoramos**: re-cierres y ajustes manuales.

---

## 7. Migración SAP

### 7.1 [P0] Criterio "no migrar artículos sin movimiento en 2 años"

- **Qué**: script previo al go-live identifica artículos sin movimientos en 24 meses; **no** se migran. Reporte de "artículos a archivar" para validación del área.
- **Por qué importa**: catálogo limpio = bandejas limpias = adopción fácil.
- **Cómo lo verificamos**: F1-PR4 incluye script + reporte de validación.
- **Qué pasa si lo ignoramos**: catálogo con miles de items obsoletos; búsqueda lenta.

### 7.2 [P0] Validación de campos obligatorios en master de Artículos

- **Qué**: pre-migración, validar que cada artículo migrado tiene: UM válida, conversión UM (si aplica), sub-almacén default, costo unitario inicial. Reporte de "artículos incompletos" para que el área llene antes de aplicar.
- **Por qué importa**: artículos sin sub-almacén default rompen el flujo de captura.
- **Cómo lo verificamos**: F1-PR4.
- **Qué pasa si lo ignoramos**: bloqueo operativo al go-live.

### 7.3 [P0] Saldos iniciales aplicados como movimiento tipo `SaldoInicial`

- **Qué**: los saldos al go-live se aplican como un movimiento especial tipo `SaldoInicial` (puede no existir como tipo formal; se modela como `AjustePositivo` con flag `es_saldo_inicial=true`). Auditoría preserva la traza.
- **Por qué importa**: trazabilidad. Sin esto, los saldos aparecen "de la nada".
- **Cómo lo verificamos**: F1-PR4 — saldos iniciales tienen movimiento generado.
- **Qué pasa si lo ignoramos**: imposible auditar; primer cierre de mes confuso.

---

## 8. Seguridad

### 8.1 [P0] Datos sensibles en logs

Heredado: Serilog masking activo. Almacén no maneja PAN ni datos bancarios, pero sí RFC de proveedores via puertos.

### 8.2 [P0] Adjuntos (packing list, vales, evidencias) en Blob privado

Heredado: SAS tokens cortos. Ver §6.2 del 04 de CxP.

---

## 9. Performance

### 9.1 [P0] Índices del §5.2 obligatorios

- **Qué**: la migración base de cada tabla incluye los índices del §5.2. Especialmente: `ix_saldos_con_stock` (parcial) y `ix_movimientos_recepciones` (parcial).
- **Por qué importa**: con 5K movimientos/mes y 10K artículos, queries de bandeja sin índice se degradan rápido.
- **Cómo lo verificamos**: F2-PR1, F2-PR2 incluyen `EXPLAIN ANALYZE` con datos sintéticos.
- **Qué pasa si lo ignoramos**: bandejas inutilizables en 6 meses.

### 9.2 [P1] Bandejas paginadas

Heredado. Default `page_size=50`, max `500`.

### 9.3 [P2] Particionamiento de `movimientos_inventario` (post-MVP)

Si supera 1M filas (a 5+ años del go-live), particionar por trimestre.

---

## 10. Cross-module integration

### 10.1 [P0] Solo lectura cross-módulo vía puertos

Heredado: cero `SELECT ... FROM compras.*` o `cuentas_por_pagar.*` desde Almacén.

### 10.2 [P0] Stubs `NoOp*` con `PLATFORM-TODO`

Heredado.

### 10.3 [P1] Versioning de contratos de eventos

Heredado.

---

## 11. Observabilidad

### 11.1 [P0] Logs estructurados con `correlation_id`

Heredado.

### 11.2 [P1] Métricas custom

- **Qué**: contador "Vales sin regularizar > 48h" (alerta si > 5); "Recepciones sin factura > 30 días" (alerta si > 10); "Saldos negativos detectados" (alerta inmediata si > 0).
- **Por qué importa**: visibilidad proactiva.
- **Cómo lo verificamos**: dashboard App Insights documentado en runbook.
- **Qué pasa si lo ignoramos**: incidentes en silencio.

### 11.3 [P1] Tracing distribuido

Heredado.

---

## 12. Despliegue

### 12.1 [P0] `deploy-app-dev.yml` actualizado en F0-PR2

Ver §1.2.

### 12.2 [P0] Workers se inician en `Millet.Api`

`OutboxPublisherWorker<AlmacenDbContext>`, `RegularizacionValeSlaWorker`, `InventarioRotativoCalendarioWorker`. Smoke test post-deploy.

### 12.4 [P0] ReordenWorker — doble interruptor con precedencia

- **Qué**: el motor de reorden tiene dos interruptores. (1) **Kill-switch de
  infraestructura**: `ReordenWorker:Disabled` (appsettings/KV, default `true`) — si está
  apagado, el servicio termina al arrancar y **no consulta nada más**; cambiarlo requiere
  config + reinicio. (2) **Interruptor operativo**: `almacen.settings.reabasto_automatico_activo`
  (BD, por empresa, default `false`) — editable desde la pantalla de Reabasto
  (gate `almacen.reorden.administrar`, GET con `almacen.reorden.leer`), el worker lo consulta
  **en cada ciclo** antes del advisory lock, para la **empresa del usuario de servicio del
  motor** (`IUsuarioServicioReadPort`, mono-empresa).
- **Semántica**: apagar NO cancela un barrido en curso (la TX termina) — surte efecto al
  siguiente tick; prender espera el próximo tick (hasta `ReordenWorker:Interval`, default 1 h).
  Los borradores ya generados quedan vivos como RQs normales.
- **Por qué importa**: encender el motor en un ambiente requiere AMBOS: config habilitada
  (opt-in por ambiente, como siempre) y el flag de BD encendido por un administrador. La
  config sigue siendo el freno duro de infra.
- **Cómo lo verificamos**: units del gate (kill-switch no toca BD; flag off no invoca el
  command) + integración PG (flag on atraviesa lock y barre). Auditoría del flip automática
  (IAuditable → audit_log, ADR-0008).
- **Qué pasa si lo ignoramos**: RQs automáticas generándose "solas" sin que Operación sepa
  quién/cuándo encendió el motor, o un motor que nadie puede apagar sin redeploy.

### 12.3 [P1] Política de operación a 2 turnos

- **Qué**: el módulo opera 19h/día (8am-6pm + 11pm-8am). Ventana de mantenimiento entre 6pm-11pm (5h). Anuncio del downtime al equipo de Almacén con 24h de antelación.
- **Por qué importa**: dos turnos = poca tolerancia a downtime no planeado.
- **Cómo lo verificamos**: SLA documentado en runbook.
- **Qué pasa si lo ignoramos**: usuarios bloqueados; queja del área.

---

## 13. Pendientes para `08-operacion-y-runbook.md`

A documentar en F9-PR4:

- Procedimiento de recalcular saldos manualmente (si trigger falla por bug).
- Procedimiento de re-procesar evento del outbox manualmente.
- Procedimiento de "cancelar" un movimiento firmado (vía contramovimiento, no DELETE).
- Procedimiento de reabrir un conteo aplicado (si se detecta error post-aplicar).
- Procedimiento de gestión de bloqueos de inventario anual atorados.
- FAQs operativas comunes.

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Cuidados de infra del módulo Almacén. Enfatiza el saldo materializado (lo más crítico), captura sin sesgo, bloqueos de inventario, re-localización del placeholder.
