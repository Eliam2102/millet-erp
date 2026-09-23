# Cuidados de infraestructura — Submódulo Órdenes de Compra (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 0.3),
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 2),
> [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2).
>
> **Hereda contexto de:** [`docs/modulos/compras-requisiciones/04-cuidados-infra.md`](../compras-requisiciones/04-cuidados-infra.md)
> (Rev. 1). Los cuidados de plataforma compartida (interceptors,
> auditoría, soft delete, multi-empresa, Money, IClock, idempotencia
> HTTP, etc.) son los mismos. Este documento **enfatiza lo nuevo de
> OC** y, donde aplique, ajusta umbrales o casos al modelo de OC.
>
> **Estado:** propuesta para revisión con el equipo.
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer

Cada cuidado tiene cuatro líneas:

- **Qué**: la regla en una frase.
- **Por qué importa**: una o dos frases que justifican el costo.
- **Cómo lo verificamos**: test, gate de CI, checklist de PR, o
  revisión manual concreta.
- **Qué pasa si lo ignoramos**: el modo de falla.

Los cuidados se agrupan por tema y se priorizan dentro de cada
bloque (P0 = bloqueante para release; P1 = importante; P2 = nice-to-
have trazable).

> **Reuso primero (regla del proyecto):** muchos cuidados ya están
> verificados en RQ. Donde aplique, este doc dice "ver §X del 04 de
> RQ" en lugar de duplicar. Solo se expande lo específico de OC.

---

## 1. Migraciones EF Core

> Ref: ADR-0005, plan §2 audit, F1-PR1, F2-PR1, F2-PR4, F3-PR1,
> **F4-PR1** (ALTER de `requisiciones`), F5-PR3, F6-PR1/PR2, F9-PR1.

### 1.1 [P0] Cada migración debe revisarse como SQL, no como C#

Mismo cuidado que §1.1 del 04 de RQ. Aplica a las **15+ migraciones**
que introduce OC. Reviewer rechaza el PR si el `dotnet ef migrations
script` no está adjunto.

### 1.2 [P0] **ALTER sobre `compras.requisiciones` en F4-PR1: coordinación cross-agregado**

- **Qué**: F4-PR1 agrega la columna `comprometida_en_oc_id uuid REFERENCES
  compras.ordenes_compra(id)` + 2 índices parciales sobre
  `compras.requisiciones`. Es aditiva (nullable) y no toca datos
  existentes, pero **toca la tabla central del agregado de RQ**.
  Coordinar con dueño de RQ: mergear cuando no haya PRs abiertos que
  toquen `requisiciones` o `RequisicionConfiguration.cs`.
- **Por qué importa**: si dos PRs concurrentes agregan migraciones
  sobre la misma tabla, el orden de timestamp en el nombre de archivo
  decide quién gana; el segundo se queda con migración inconsistente.
- **Cómo lo verificamos**: checklist del PR. Reviewer revisa el
  estado de `compras-requisiciones` PRs abiertos antes de aprobar.
  Adicionalmente, el `dotnet ef migrations script` adjunto se aplica
  sobre un clon de prod (staging) y se valida con `pg_dump --schema-only`.
- **Qué pasa si lo ignoramos**: dev branch que parece estar bien;
  conflicto detectado al mergear o, peor, migración aplicada con
  orden inverso en CI.

### 1.3 [P0] NOT NULL en columnas con datos: pasos atómicos con default

Ver §1.3 del 04 de RQ. Aplica especialmente cuando se agregue
`id_externo_sap` post-MVP en migración aditiva (no aplica en MVP).

### 1.4 [P0] Esquemas separados — no FK física cross-schema

Ver §1.4 del 04 de RQ. **Excepción específica de OC**: las FKs a
`compartido.proveedores`, `compartido.articulos`,
`compartido.sucursales`, `compartido.almacenes`, `compartido.incoterms`,
`compartido.transportistas`, `compartido.regimenes_fiscales` son
**lógicas** (solo `Guid` referenciando, sin `REFERENCES` físico
cross-schema). El test que escanea `ComprasDbContext` y falla si
encuentra FK cross-schema debe pasar.

### 1.5 [P1] El módulo Compras (incluyendo OC) NO escribe en otros esquemas

Ver §1.5 del 04 de RQ. **Confirmar específicamente** que los nuevos
catálogos seed que OC necesita (Incoterm, Transportista, RegimenFiscal,
CondicionesPago) viven en `compartido` y sus configuraciones EF
quedan en el `CompartidoDbContext`, NO en `ComprasDbContext`. OC los
referencia con `ExcludeFromMigrations()` si necesita exponerlos en
sus configuraciones.

### 1.6 [P1] Migraciones grandes (Fase 9 v1) son aditivas y rápidas

- **Qué**: en MVP, Fase 9 sólo crea tablas catálogo nuevas + seeds via
  `HasData`. No hay migraciones de datos masivas, no hay scripts T-SQL,
  no hay reconstrucción de estado.
- **Por qué importa**: la complejidad de "migraciones grandes" (lotes,
  tabla de progreso, restart-safe) descrita en §1.6 del 04 de RQ
  **no aplica al MVP de OC** porque la migración SAP está diferida
  post-MVP. Cuando se reabra, este cuidado se reactiva.
- **Cómo lo verificamos**: cada migración aditiva en F9-PR1 corre en
  < 5 segundos sobre dataset seed.
- **Qué pasa si lo ignoramos**: irrelevante hasta que se reabra la
  migración SAP post-MVP.

---

## 2. Concurrencia optimista (ADR-0012)

> Ref: ADR-0012, diseño §3.bis.6, **§3.bis.1 (compromiso exclusivo)**,
> F1-PR1, F3-PR3 (soft lock wireup), **F4-PR2 (cross-agregado)**.

### 2.1 [P0] `Version` se incrementa SOLO vía `BaseDbContext`

Mismo cuidado que §2.1 del 04 de RQ. Aplica a `OrdenCompra`,
`LineaOrdenCompra`, `AutorizacionOC`, `AdjuntoOC`.

### 2.2 [P0] `DbUpdateConcurrencyException` traduce a HTTP 409

Mismo cuidado que §2.2 del 04 de RQ. El frontend usa el mismo
`<ConflictResolutionDialog>` que para RQ; sin código adicional.

### 2.3 [P0] **Concurrencia cross-agregado: OC + Requisicion**

- **Qué**: F4-PR2 (crear OC desde RQ) y F4-PR3 (liberar RQ al cancelar
  OC) tocan **dos agregados** en una sola transacción de aplicación:
  `OrdenCompra` y `Requisicion`. La concurrencia tiene que cubrir los
  dos. Ejemplo: dos compradores intentan crear OC desde la misma RQ
  al mismo tiempo. Ambos cargan `Requisicion` con `Version=N`. Solo
  uno gana (el otro recibe 409); la `Requisicion` queda comprometida
  exactamente con la OC del ganador.
- **Por qué importa**: sin esto, dos OCs apuntando a la misma RQ
  comprometida → invariante de exclusividad rota.
- **Cómo lo verificamos**: test de integración paralelo: 2 tasks
  ejecutando `CrearOrdenCompraDesdeRequisicionCommand` contra la misma
  `requisicionId`. Exactamente uno persiste; el otro 409. La RQ queda
  comprometida con el ganador.
- **Qué pasa si lo ignoramos**: doble compromiso silencioso; un
  comprador trabaja una OC sin saber que el saldo de la RQ ya está
  en otra OC.

### 2.4 [P0] **Duplicación cross-agregado: trazabilidad firme**

- **Qué**: `DuplicarOrdenCompraCommand` (F6-PR2) lee una OC origen
  en estado terminal (Cancelada o Rechazada) y crea una OC nueva en
  Borrador. La nueva persiste `oc_origen_id` referenciando la origen
  **en la misma transacción** que inserta cabecera y líneas. La OC
  origen permanece en estado terminal — no se modifica.
- **Por qué importa**: si `oc_origen_id` se setea en una segunda
  transacción, hay una ventana donde la OC nueva existe sin vínculo
  a su origen. KPIs de duplicación fallan al detectarla.
- **Cómo lo verificamos**: test que mata el proceso entre el insert
  de la OC nueva y la actualización de `oc_origen_id` (simulado con
  falla inyectada): la BD queda íntegra (OC nueva con vínculo o
  ninguna OC).
- **Qué pasa si lo ignoramos**: OCs duplicadas que no se vinculan
  al origen; KPIs de adherencia al workflow mienten.

### 2.5 [P1] Frontend recibe `ETag` y envía `If-Match`

Ver §2.4 del 04 de RQ. Aplica a todos los endpoints `PATCH` de OC.

---

## 3. Outbox + Service Bus (ADR-0009)

> Ref: ADR-0009, diseño §8.5, §12, F8-PR1.

### 3.1 [P0] Atomicidad: outbox + entidades en la misma transacción

Mismo cuidado que §3.1 del 04 de RQ. Para OC, los eventos críticos
son: `OrdenCompraAutorizadaEvent` (dispara PDF), `OrdenCompraCanceladaEvent`
(libera RQs), `OrdenCompraCerradaEvent` (notifica a RQ y Recepción).

### 3.2 [P0] Worker publisher: SELECT FOR UPDATE SKIP LOCKED

Ver §3.2 del 04 de RQ. Heredado de la infra de RQ (cuando se wirea
en F6 de su breakdown).

### 3.3 [P1] Retry exponencial + dead-letter en `intentos > 10`

Ver §3.3 del 04 de RQ.

### 3.4 [P0] **Idempotencia del consumer interno (listeners de OC)**

- **Qué**: los listeners de OC consumen eventos cross-BC desde RQ y
  desde futuros módulos (Almacén, CxP, Tesorería). Cada listener
  debe ser **idempotente** porque Outbox garantiza at-least-once.
  Concretamente: `OcRecepcionRegistradaListener` recibe el payload
  `{ lineaId, cantidadRecibidaAcumulada }` — la línea se actualiza
  con un **set** al valor del payload, no un incremento.
- **Por qué importa**: si el listener increment-and-saves dos veces
  por el mismo evento, `cantidad_recibida > cantidad` (rompe CHECK
  constraint). Si pasa la CHECK, el sub-estado es incorrecto.
- **Cómo lo verificamos**: test por listener que entrega el mismo
  evento dos veces y verifica que el efecto es 1×.
- **Qué pasa si lo ignoramos**: sub-estados incorrectos, OCs que
  cierran prematuramente o se quedan abiertas para siempre.

### 3.5 [P2] Métricas de outbox como gates blandos

Ver §3.5 del 04 de RQ.

---

## 4. Idempotencia HTTP (ADR-0020)

> Ref: ADR-0020, F10-PR1.

### 4.1 [P0] `[RequireIdempotencyKey]` en POST que crean recursos

Mismo cuidado que §4.1 del 04 de RQ. **Endpoints de OC que deben
llevarlo**: `POST .../ordenes` (crear), `.../lineas` (agregar),
`.../adjuntos` (adjuntar), `.../transmitir`, `.../autorizaciones`,
`.../rechazar`, `.../cancelar`, `.../duplicar`. Aplicar **desde el
inicio de cada PR** (decisión cerrada Rev. 2 del 04); F10-PR1 se
reduce a auditoría de cobertura.

### 4.2 [P0] TTL del cache y limpieza

Ver §4.2 del 04 de RQ. Heredado.

### 4.3 [P1] Replay tests por cada endpoint idempotente de OC

Convención de naming `*_Idempotent` por endpoint de OC en
`Compras.IntegrationTests/Oc/`. Cada endpoint con `[RequireIdempotencyKey]`
tiene su test.

---

## 5. Stubs y NoOp (ADR-0031)

> Ref: ADR-0031, diseño §12, F2-PR4 (blob filesystem), F5-PR2
> (listeners stub), **F5-PR3 (reemplazo del stub OcBorradorStub —
> punto de no retorno)**, F6-PR1 (PDF stub), F10-PR3 (cleanup blob real).

### 5.1 [P0] Stubs NUNCA son default en producción

Mismo cuidado que §5.1 del 04 de RQ. OC introduce **5 stubs** que
deben fallar ruidosamente en `Production` si quedan activos:

- `LocalPdfOrdenCompraStub` (F6-PR1) — reemplazado por
  `QuestPdfOrdenCompraImpl` en F6-PR3.
- `LocalFilesystemBlobStub` (F2-PR4) — reemplazado por
  `AzureBlobAdjuntoImpl` en F10-PR3.
- Listeners stub de CxP, Tesorería, Almacén-Devolución (F5-PR2) —
  reciben eventos mock para tests pero **no se disparan en producción**
  hasta que los módulos reales emitan.
- `TipoCambio` manual (sin puerto, captura libre) — solo se reabre
  como stub si C1 se decide automatizar post-v1.

Cada uno requiere: flag de habilitación (`Compras:Oc:Stubs:Pdf=true`,
etc.), fallo ruidoso al arrancar en `Production` si activo.

### 5.2 [P0] **`PLATFORM-TODO(<id>)` en cada NoOp de OC**

- **Qué**: cada implementación temporal lleva
  `// PLATFORM-TODO(<id>): ...`. Para OC v1 los ids son:
  `<PdfService>`, `<BlobStorageAzure>`, `<Almacen.Recepcion>`,
  `<Almacen.Devolucion>`, `<CxP.Factura>`, `<CxP.NotaCredito>`,
  `<Tesoreria.Pago>`, `<Notificaciones>`, `<TipoCambio>`.
- **Por qué importa**: `rg "PLATFORM-TODO" backend/src/Compras` debe
  listar todo el debt. Sin el comentario, queda invisible.
- **Cómo lo verificamos**: revisión de PR; mensualmente, `rg
  "PLATFORM-TODO" backend/src --no-heading | sort | uniq -c` cruzado
  contra §12 del 01-diseño.
- **Qué pasa si lo ignoramos**: el debt se vuelve invisible. Cuando
  el ticket de plataforma cierra, nadie wirea OC y se cree que está
  conectado.

### 5.3 [P0] **F5-PR3 es punto de no retorno: validación en staging obligatoria**

- **Qué**: F5-PR3 reemplaza el `InMemoryGenerarSolicitudCompraPort` /
  `OcBorradorStub` que hoy usa el flujo de bifurcación de RQ. Hasta
  este PR, RQ podía operar con stubs si OC no existía; después, RQ
  depende de OC real. **Antes de mergear a `main`**, validación
  obligatoria en staging con datos reales: crear RQ con bifurcación,
  verificar que la OC real aparece en la bandeja, no en
  `compras.oc_borrador_stub`.
- **Por qué importa**: si F5-PR3 rompe la integración, los flujos de
  RQ en producción dejan de bifurcar correctamente.
- **Cómo lo verificamos**: checklist del PR. Reviewer firma
  explícitamente "validado en staging" antes de aprobar. Code review
  por dos personas.
- **Qué pasa si lo ignoramos**: RQs que parecen bifurcadas pero la
  OC real nunca se creó.

### 5.4 [P1] Tabla §12 del diseño se mantiene actualizada

Ver §5.3 del 04 de RQ. Aplica a la tabla §12 de OC.

---

## 6. Auditoría (ADR-0008)

> Ref: ADR-0008, diseño §4, `AuditSaveChangesInterceptor`.

### 6.1 [P0] No bypass de interceptors con SQL crudo o bulk

Ver §6.1 del 04 de RQ.

### 6.2 [P0] **`OrdenCompra`, `LineaOrdenCompra`, `AutorizacionOC`, `AdjuntoOC` declaran `IAuditable`**

- **Qué**: los 4 tipos del agregado implementan `IAuditable`. Los
  catálogos (`TipoDocumentoOc`, etc.) también si pueden cambiar. La
  tabla `compras.orden_compra_pdf` se marca `INotAudited` porque su
  contenido es derivado del agregado.
- **Por qué importa**: si una entidad no declara `IAuditable`, el
  interceptor la ignora silenciosamente. ADR-0008 §"opt-in con
  enforcement" lo exige.
- **Cómo lo verificamos**: test que escanea el modelo EF de
  `ComprasDbContext` y falla si encuentra una `BaseEntity` de OC que
  no implementa ni `IAuditable` ni `INotAudited`.
- **Qué pasa si lo ignoramos**: cambios sin pista en producción.
  Crítico para OC porque cancelaciones, duplicaciones y autorizaciones
  son fiscalmente sensibles.

### 6.3 [P1] `audit_log` en `core` particionado mensual

Ver §6.3 del 04 de RQ. Heredado.

---

## 7. Soft delete y query filters (ADR-0008, ADR-0011)

> Ref: ADR-0008, ADR-0011, `BaseDbContext.ApplyQueryFilters`.

### 7.1 [P0] `IFiscalmenteRelevante` aplicado a `OrdenCompra`

- **Qué**: `OrdenCompra` implementa `IFiscalmenteRelevante`. El
  estado `Cancelada` **no es** soft delete (es estado terminal del
  agregado, visible en bandejas de auditoría). El soft delete físico
  vía `DeletedAt` solo aplica si se admin-borra una OC borrador
  pre-transmisión, lo cual **no está expuesto en v1** — todas las
  cancelaciones son via `CancelarOrdenCompraCommand` con motivo.
- **Por qué importa**: una OC, incluso cancelada, debe ser evidencia
  de auditoría; no se borra físicamente nunca.
- **Cómo lo verificamos**: test que verifica que `CancelarOrdenCompraCommand`
  resulta en `Estado = Cancelada` con `DeletedAt = NULL`. Gate de CI:
  ningún `RemoveRange` o `DELETE FROM` sobre `OrdenCompra`.
- **Qué pasa si lo ignoramos**: OCs físicamente borradas; imposibilidad
  de auditar cancelaciones; problema con el SAT.

### 7.2 [P1] Queries de auditoría con `IgnoreQueryFilters`

Ver §7.2 del 04 de RQ.

### 7.3 [P1] Multi-empresa filter no se evade

Ver §7.3 del 04 de RQ.

### 7.4 [Pendiente] Segmentación por sucursal (ADR-0051)

Ver §7.4 del 04 de RQ — mismo gap y mismo mecanismo pendiente para `OrdenCompra`.

---

## 8. Permisos y doble autorización (ADR-0007)

> Ref: ADR-0007, diseño §9, F0-PR1, **F5-PR4 (cancelar con recepciones)**.

### 8.1 [P0] No filtrar existencia vía 404 vs 403

Ver §8.1 del 04 de RQ. Aplica a todos los endpoints de OC.

### 8.2 [P0] Tests negativos de permisos por endpoint

Ver §8.2 del 04 de RQ. Convención `*_Returns403_When_PermissionMissing`
en `Compras.IntegrationTests/Oc/`.

### 8.3 [P0] **Doble autorización verifica los 3 permisos en el mismo request**

- **Qué**: el comando que requiere doble autorización (`CancelarOrdenCompra`
  con recepciones parciales en F5-PR4) recibe en el payload dos
  `usuarioId` (N1 + N2). El validator verifica que **cada uno tiene
  su permiso correspondiente** (`autorizar.nivel1` y `autorizar.nivel2`)
  **además** del permiso base de la acción (`cancelar.doble`). Los
  dos usuarios deben ser distintos.
- **Por qué importa**: si solo se valida un usuario, una sola persona
  con ambos permisos puede ejecutar la acción de "doble firma" sola,
  burlando el control.
- **Cómo lo verificamos**: tests parametrizados:
  - Mismo usuarioN1 y usuarioN2 → 422 `MISMA_FIRMA_INVALIDA`.
  - usuarioN1 sin permiso `autorizar.nivel1` → 403.
  - usuarioN2 sin permiso `autorizar.nivel2` → 403.
  - Solo usuarioN1 con `autorizar.nivel1` (sin N2) → 422.
  - Ambos con sus permisos correctos → 200/204.
- **Qué pasa si lo ignoramos**: control de doble firma burlado.
  Hallazgo en auditoría.

### 8.4 [P1] Cache de permisos: invalidación al cambiar roles

Ver §8.3 del 04 de RQ.

---

## 9. Folio: secuencia atómica PostgreSQL

> Ref: diseño §4.3, §10 (`compras.folio_secuencias_oc`), F1-PR1.

### 9.1 [P0] `nextval` en transacción del comando

Ver §9.1 del 04 de RQ. Aplica a OC con la secuencia
`compras.folio_secuencias_oc` (separada de la de RQ).

### 9.2 [P1] Folio único por (empresa, sucursal, año)

- **Qué**: la columna `folio` en `compras.ordenes_compra` tiene
  `UNIQUE (empresa_id, sucursal_destino_id, folio)` (diseño §10.1).
  Si dos compradores en la misma sucursal disparan creación
  simultánea, ambos toman folios consecutivos vía `nextval`; la
  UNIQUE es doble protección.
- **Por qué importa**: la UNIQUE protege contra bugs futuros en la
  generación. Por ahora la secuencia ya garantiza unicidad.
- **Cómo lo verificamos**: revisión del DDL en F1-PR1 + test que
  inserta dos OCs en la misma sucursal+año intencionalmente con el
  mismo folio (skipping `nextval`) y verifica `DbUpdateException`.
- **Qué pasa si lo ignoramos**: si se rompe la secuencia, BD no
  detecta el bug.

---

## 10. IClock, UTC, Money y `decimal`

> Ref: ADR-0013, ADR-0014, `BannedSymbols.txt`, `Money.cs`.

### 10.1 [P0] Banned symbols ya gateados

Ver §10.1 del 04 de RQ. **Aplica también a fechas de OC**:
`FechaDocumento`, `FechaContabilizacion`, `FechaEntregaEsperada`,
`FechaEntregaLinea`, `FechaCarga` (adjunto), `FechaHora`
(autorización), `FechaSnapshot` (versión).

### 10.2 [P0] `Money` cruza la capa de dominio, NO `decimal`

Ver §11.1 del 04 de RQ. **Casos críticos en OC**: `PrecioUnitario`,
`SubtotalLinea`, `IvaImporte`, `RetencionIsr`, `DescuentoGlobal`,
`GastosAdicionales`, `TotalAPagar`. Todos `Money`, no `decimal`.

### 10.3 [P1] Precisión `decimal(15,4)` para cantidades, `decimal(15,2)` para totales

- **Qué**: el schema fija `numeric(15,4)` para `cantidad`,
  `cantidad_recibida`, `cantidad_facturada`, y `precio_unitario`; y
  `numeric(15,2)` para totales (`iva_importe`, `retencion_isr`,
  `gastos_adicionales`, `redondeo`). EF Core con `HasPrecision`
  alineado.
- **Por qué importa**: precisión insuficiente trunca centavos en
  totales grandes; precisión excesiva infla el almacenamiento.
- **Cómo lo verificamos**: revisión del DDL en F1-PR1 y F2-PR1.
- **Qué pasa si lo ignoramos**: precision drift entre cálculo en
  C# (`decimal`) y storage en BD.

---

## 11. Cross-module ports: dónde vive cada cosa

> Ref: diseño §8, F2-PR4 (blob), F5-PR2 (CxP, Tesorería, Almacén-Dev),
> F6-PR1 (PDF), F8-PR1 (Outbox).

### 11.1 [P0] Las interfaces viven en `Compras.Domain.Ports`

Mismo cuidado que §12.1 del 04 de RQ. **Puertos nuevos de OC**:

| Puerto | Ubicación | Implementador real |
|---|---|---|
| `IGenerarPdfOrdenCompraPort` | `Compras.Domain.Ports.Pdf/` | Compras (QuestPdf en F6-PR3) |
| `IAlmacenarBlobPort` | `Compras.Domain.Ports.Blob/` | Compras → Azure Blob (F10-PR3) |
| `FacturaProveedorRegistradaEvent` | `Compras.Domain.Ports.Cxp/` | CxP (cuando exista) |
| `NotaCreditoProveedorRegistradaEvent` | `Compras.Domain.Ports.Cxp/` | CxP |
| `PagoFacturaProveedorEvent` | `Compras.Domain.Ports.Tesoreria/` | Tesorería |
| `OcDevolucionRegistradaEvent` | `Compras.Domain.Ports.Almacen/` | Almacén-Recepción |

**Compras es dueño** de las interfaces. CxP, Tesorería y Almacén las
implementan/emiten cuando existan, no las definen.

### 11.2 [P0] Stubs viven en `Compras.Infrastructure.Stubs` (o `.Oc.Stubs`)

Mismo cuidado que §12.2 del 04 de RQ. Para no contaminar el patrón de
RQ, los stubs específicos de OC viven en `Compras.Infrastructure.Oc.Stubs/`.

### 11.3 [P1] Reemplazo de stubs requiere PR de Compras

Ver §12.3 del 04 de RQ. **Específicos de OC**:

- **F6-PR3** (PDF): reemplaza `LocalPdfOrdenCompraStub` por
  `QuestPdfOrdenCompraImpl`, borra `PLATFORM-TODO(<PdfService>)`.
- **F10-PR3** (Blob): reemplaza `LocalFilesystemBlobStub` por
  `AzureBlobAdjuntoImpl`, borra `PLATFORM-TODO(<BlobStorageAzure>)`.

---

## 12. State machine y sub-estados independientes

> Ref: diseño §5, §3.bis.2, F1-PR1, F3-PR1/PR2/PR3, F5-PR1/PR2.

### 12.1 [P0] Transiciones inválidas devuelven 422, no 500

Ver §13.1 del 04 de RQ. Aplica a las 8 transiciones del diagrama
§5.3 del 01-diseño.

### 12.2 [P0] Solo el agregado decide transiciones

Ver §13.3 del 04 de RQ. `OrdenCompra.Estado`, `SubEstadoRecepcion`,
`SubEstadoFacturacion`, `SubEstadoPago` tienen setters `private` o
`protected internal` solo para EF.

### 12.3 [P0] **Sub-estados materializados se recalculan en cada cambio de líneas**

- **Qué**: el método `RecalcularSubEstados()` del agregado se invoca
  **inmediatamente después** de cualquier modificación a las cantidades
  de las líneas (`cantidad`, `cantidad_recibida`, `cantidad_facturada`).
  Cualquier endpoint o listener que toque líneas debe llamarlo antes
  de `SaveChanges`.
- **Por qué importa**: si se toca una línea sin recalcular, los
  sub-estados quedan inconsistentes con la realidad. El reporte de
  partidas abiertas miente.
- **Cómo lo verificamos**: test parametrizado que escanea handlers
  y listeners de OC y verifica que cualquiera que mute líneas
  llama `RecalcularSubEstados`. Alternativa pragmática: invariante
  del agregado en `OrdenCompra.AntesDeGuardar()` que dispara recálculo
  si detecta líneas modificadas (más caro pero más seguro).
- **Qué pasa si lo ignoramos**: OCs que aparecen `Cerrada` cuando
  todavía falta recibir; o `Autorizada` cuando ya está completa.

### 12.4 [P0] **Cierre automático: la transición a `Cerrada` solo dispara desde listener**

- **Qué**: la transición `Autorizada → Cerrada` no se ejecuta desde
  ningún endpoint manual. Solo se dispara dentro de `RecalcularSubEstados`
  cuando las 3 dimensiones están en Completa/Completa/Pagada **al
  mismo tiempo**. El método verifica el estado antes/después y emite
  `OrdenCompraCerradaEvent` exactamente una vez.
- **Por qué importa**: cerrar manualmente desde un endpoint salta el
  invariante (¿realmente están las 3 dimensiones cumplidas?). Cerrar
  desde dos lugares emite el evento dos veces.
- **Cómo lo verificamos**: gate de CI: `rg "transicionarA.*Cerrada"
  backend/src/Compras/Application/Oc/` debe acotarse a los listeners
  (`OcRecepcionRegistradaListener`, `FacturaProveedorRegistradaListener`,
  `PagoFacturaProveedorListener`). Tests parametrizados verifican
  que cada listener dispara el cierre cuando completa la última
  dimensión.
- **Qué pasa si lo ignoramos**: dobles eventos de cierre o cierres
  prematuros.

### 12.5 [P1] Cobertura por arista del diagrama

Ver §13.2 del 04 de RQ. OC tiene **7 transiciones principales** +
las del sub-estado (recepción, facturación, pago). El test
parametrizado del 12.1 debe recorrer las 7 + las inválidas relevantes
(autorizar desde Borrador, cancelar con recepciones sin doble auth,
duplicar desde Borrador, etc.).

---

## 13. Duplicación de OC (C4 cerrado como cancelar + recrear)

> Ref: diseño §3 (C4), §6.1 (comando `DuplicarOrdenCompra`), F6-PR2.

### 13.1 [P0] **Solo se duplica desde estados terminales**

- **Qué**: `DuplicarOrdenCompraCommand` valida que la OC origen esté
  en `Cancelada` o `Rechazada`. Duplicar desde `Borrador`, `Autorizada`,
  `EnAutorizacion*` o `Cerrada` retorna 422.
- **Por qué importa**: duplicar una OC activa crea una OC paralela
  que confunde el flujo (¿cuál es la "real"?). Solo después de cerrar
  la original (cancelando o rechazando) tiene sentido duplicar.
- **Cómo lo verificamos**: tests parametrizados por estado de la OC
  origen.
- **Qué pasa si lo ignoramos**: OCs duplicadas que coexisten con la
  original; confusión operativa y posibles compras duplicadas al
  proveedor.

### 13.2 [P0] **`DuplicarOrdenCompra` NO copia adjuntos, autorizaciones, ni vínculos a RQs**

- **Qué**: la OC nueva hereda cabecera + líneas (como líneas manuales,
  sin FK a `requisicion_id`). **No copia**: adjuntos (la cotización
  debe re-adjuntarse — el flujo de aprobación necesita evidencia
  fresca), autorizaciones (cada OC tiene su propio ciclo de firma),
  eventos, sub-estados (la nueva arranca en `SinRecepcion/SinFactura/SinPago`),
  ni FKs a RQs (las RQs originales fueron liberadas al cancelar; el
  comprador re-selecciona si quiere consolidar).
- **Por qué importa**: copiar adjuntos arrastra cotizaciones vencidas;
  copiar autorizaciones rompe el invariante de "doble firma por OC";
  copiar FKs a RQs duplicaría compromisos (RQ comprometida con OC
  origen cancelada + OC nueva = inconsistencia).
- **Cómo lo verificamos**: tests post-duplicación verifican que
  `Adjuntos == empty`, `Autorizaciones == empty`,
  `lineas.requisicion_id == null`.
- **Qué pasa si lo ignoramos**: cotizaciones obsoletas en OCs nuevas;
  autorizaciones implícitas sin firma real; RQs en doble compromiso.

### 13.3 [P0] **Vínculo bidireccional via `oc_origen_id`**

- **Qué**: la OC nueva persiste `oc_origen_id` apuntando a la OC
  cancelada/rechazada. La OC origen NO tiene contracampo — la
  navegación inversa se hace via query `WHERE oc_origen_id = :ocId`.
  Una OC origen puede tener N OCs duplicadas (si se duplica más de
  una vez); pero por C4 esto es excepcional.
- **Por qué importa**: el árbol de documentos (§8.4 mapa funcional)
  y el KPI de duplicaciones (§13.4 de este doc) dependen de este
  vínculo.
- **Cómo lo verificamos**: test que duplica → verifica
  `nueva.oc_origen_id == origen.id` y `query(origen.id) == [nueva]`.
- **Qué pasa si lo ignoramos**: pérdida de trazabilidad; el reporte
  KPI no detecta las duplicaciones.

### 13.4 [P1] KPI de duplicaciones como gate cultural

- **Qué**: F7-PR3 incluye `ListarOrdenesCompraDuplicadasQuery` con
  filtro por comprador + período (OCs con `oc_origen_id != NULL`).
  Si las duplicaciones suben (proxy: >10/mes por comprador), revisar
  si están usándolas como atajo para evitar planificación.
- **Por qué importa**: cancelar + duplicar es una vía operativa,
  pero abusarla degrada la trazabilidad y desperdicia firmas N1/N2.
- **Cómo lo verificamos**: dashboard mensual de App Insights con
  la query.
- **Qué pasa si lo ignoramos**: cultura operativa que normaliza
  cancelar + duplicar como flujo de cambios menores.

---

## 14. Compromiso exclusivo de RQ — cross-agregado in-process

> Ref: diseño §3.bis.1, F4-PR1/PR2/PR3.

### 14.1 [P0] Compromiso y liberación viven en la **misma transacción**

- **Qué**: `CrearOrdenCompraDesdeRequisicionCommand` y
  `AgregarLineaDesdeRequisicionCommand` (F4-PR2) comprometen una RQ
  **dentro de la misma transacción** que crea la OC / agrega líneas.
  El listener `RqComprometidaEnOcListener` es in-process MediatR
  `INotification`, no Outbox. `EliminarLineaCommand` y
  `CancelarOrdenCompraCommand` (F4-PR3) liberan **dentro de la misma
  transacción**.
- **Por qué importa**: si el compromiso vive en otra transacción, hay
  una ventana donde la OC existe y la RQ todavía está disponible. Otro
  comprador la toma; doble compromiso silencioso.
- **Cómo lo verificamos**: test paralelo (ver §2.3) + test que mata
  el proceso entre creación de OC e invocación del listener: la BD
  queda consistente (ambos o ninguno).
- **Qué pasa si lo ignoramos**: race condition con doble compromiso.

### 14.2 [P0] **Restricción de sucursal validada en el agregado, no solo en el validator**

- **Qué**: `AgregarLineaDesdeRequisicionCommand` valida en el handler
  que `linea.requisicion.sucursalId == oc.sucursalDestinoId`. Esa
  validación vive **además** como invariante del agregado
  `OrdenCompra.AgregarLineaDesdeRequisicion(...)`, no solo en el
  FluentValidator del comando.
- **Por qué importa**: bypass del comando (vía test seed, migración
  o flujo cross-BC futuro) puede saltarse el FluentValidator. El
  invariante del agregado es la última línea de defensa.
- **Cómo lo verificamos**: test unitario del agregado que intenta
  agregar línea de RQ de otra sucursal directamente → lanza
  `BusinessRuleException`.
- **Qué pasa si lo ignoramos**: OCs con líneas de sucursales distintas;
  PDF al proveedor con direcciones de entrega ambiguas.

### 14.3 [P1] Liberación parcial al cancelar con recepciones

- **Qué**: F5-PR4 (cancelar con recepciones parciales) libera RQs
  **por cantidad no recibida**, no por línea completa. El evento
  `LineaRqLiberadaEvent` lleva `cantidad_liberada: decimal`. El
  listener actualiza la RQ con `LiberarDeOc(cantidad)`. Si
  `cantidad_no_recibida == 0`, no se emite evento por esa línea.
- **Por qué importa**: liberar cantidad ya recibida implicaría
  perder trazabilidad contable (la línea ya tiene movimiento de
  inventario asociado).
- **Cómo lo verificamos**: test con 5 líneas, 3 con recepción parcial
  + 2 sin recepción: cancelar libera por las 5 con cantidades
  correctas; las cantidades recibidas siguen asociadas a la OC
  cancelada.
- **Qué pasa si lo ignoramos**: inventario fantasmal o liberación
  duplicada.

---

## 15. PDF generation y cohesión con state machine

> Ref: diseño §8.3, §3.bis.4, F6-PR1/PR3.

### 15.1 [P0] PDF se genera **dentro del handler de Autorizar N2**, no en background

- **Qué**: el `OrdenCompraAutorizadaListener` (F6-PR1) ejecuta
  síncronamente la generación del PDF y persiste el blob URL en
  `compras.orden_compra_pdf` **dentro de la misma transacción** que
  cambia el estado a `Autorizada`. Si la generación de PDF falla,
  falla la autorización.
- **Por qué importa**: una OC autorizada sin PDF asociado es
  inutilizable (no se le puede enviar al proveedor). Diferir a
  background crea un estado intermedio "autorizada pero sin PDF" que
  confunde a los compradores.
- **Cómo lo verificamos**: test que inyecta excepción en el servicio
  PDF → autorización falla con 500 (el agregado queda en
  `EnAutorizacionDireccion`).
- **Qué pasa si lo ignoramos**: OCs autorizadas sin PDF; operaciones
  manuales para regenerarlos.

### 15.2 [P0] **Agrupación por artículo es transformación de presentación, no del modelo**

- **Qué**: el modelo de datos preserva líneas separadas por
  requisición (§3.bis.4 del 01-diseño). El `GenerarPdfOrdenCompraService`
  ejecuta `GROUP BY articulo_id` con `SUM(cantidad)` **solo al
  renderizar el PDF**. Los datos en BD no se modifican.
- **Por qué importa**: si la agrupación se hiciera en el modelo, se
  perdería la trazabilidad línea-a-línea con la RQ origen. La
  recepción atribuiría mal a los requisitantes.
- **Cómo lo verificamos**: test con OC consolidada de 3 RQs (mismo
  artículo, cantidades 5+3+2) → BD muestra 3 líneas separadas; PDF
  muestra 1 línea con cantidad 10.
- **Qué pasa si lo ignoramos**: pérdida de trazabilidad cross-RQ.

### 15.3 [P1] Cleanup de blob al cancelar OC en Borrador

- **Qué**: si una OC en Borrador tenía adjuntos (cotización, etc.) y
  se cancela, los blobs **no se borran físicamente** del storage.
  Quedan asociados al registro cancelado (la cancelación es soft, ver
  §7.1).
- **Por qué importa**: borrar blobs físicamente al cancelar pierde
  evidencia documental. Si se cancela por error, no hay forma de
  recuperar.
- **Cómo lo verificamos**: test que cancela OC con 3 adjuntos →
  registros en `compras.orden_compra_adjuntos` siguen ahí, blobs
  siguen accesibles via Azure SDK.
- **Qué pasa si lo ignoramos**: pérdida de evidencia.

---

## 16. Información logística estructurada (vs texto libre legacy)

> Ref: diseño §3.bis.3, §5.5 (mapa funcional), F2-PR3.

### 16.1 [P0] **Campos estructurados son la única vía para logística — NO texto libre embebido**

- **Qué**: la información logística (transportista, guía, contenedor,
  ruta, semana, pedimento, país de origen) vive en columnas
  estructuradas (`info_logistica_*`, `info_import_*`). El campo
  `observaciones` (texto libre) **no debe contener** estos datos.
  Validator del comando rechaza cualquier observación que detecta
  patrones tipo `RUTA:`, `CONTENEDOR:`, `SEMANA:`, `CLIENTE:`
  (heurística simple) sugiriendo el campo correcto.
- **Por qué importa**: el mapa funcional explícita esto como mejora
  vs SAP legacy. Si el comprador vuelve al texto libre, perdemos
  filtros, reportes y consistencia de captura.
- **Cómo lo verificamos**: test parametrizado con observaciones que
  contienen los patrones legacy → 422 con mensaje sugiriendo el
  campo correcto. Reporte mensual de OCs con `observaciones` que
  contienen patrones de logística (proxy: bug de proceso si > 0).
- **Qué pasa si lo ignoramos**: regresión al patrón SAP legacy; los
  campos estructurados quedan vacíos y los reportes mienten.

### 16.2 [P1] `NumeroPedimento` editable post-autorización sin re-auth

- **Qué**: `ActualizarInformacionLogisticaCommand` permite cambiar
  `NumeroGuia`, `Transportista`, `NumeroPedimento` (en importaciones)
  en estado `Autorizada` sin disparar re-autorización. Otros campos
  de logística (Incoterm, PaisOrigen, NumeroContenedor, CodigoRuta,
  SemanaEmbarque) **no son editables** post-aut.
- **Por qué importa**: el pedimento se conoce al recibir, no al
  autorizar. Forzarlo a cambiar via "cancelar + duplicar" agrega
  fricción operativa innecesaria.
- **Cómo lo verificamos**: tests parametrizados por campo y estado.
- **Qué pasa si lo ignoramos**: o se bloquea innecesariamente la
  captura del pedimento, o se permite cambiar campos críticos sin
  re-auth.

---

## 17. Otros

### 17.1 [P1] Versionado de eventos de integración

Ver §14.1 del 04 de RQ. Eventos de OC: `compras.orden-compra.<accion>.v1`.

### 17.2 [P1] Catálogos seed read-only en MVP

- **Qué**: los endpoints de catálogos de OC (`GET /api/v1/catalogos/...`)
  son **read-only** en MVP. No hay POST/PATCH/DELETE. Si alguien
  intenta agregarlos por error, fallar el build (gate de revisión).
- **Por qué importa**: la decisión de "catálogos read-only + seeds"
  es del mapa funcional §10 (cerrada). Implementar POST/PATCH/DELETE
  prematuramente es scope creep.
- **Cómo lo verificamos**: revisión de PR. `rg "Endpoints/Catalogos"
  backend/src/Api` debe mostrar solo `MapGet`. Convención.
- **Qué pasa si lo ignoramos**: scope creep silencioso; APIs CRUD a
  medio terminar que confunden al frontend.

### 17.3 [P1] Cobertura de integración en CI antes de merge a main

Ver §14.3 del 04 de RQ. Cuando OC arranca, esta brecha ya debería
estar resuelta por el trabajo de RQ (F8-PR3 de su breakdown). Si no,
priorizar antes de Fase 2 de OC.

---

## Hallazgos para revisión

Discrepancias detectadas entre diseño, plan y estado real del repo:

1. **Conflicto de versión: 412 vs 409.**
   Heredado de RQ. El frontend usa 409 con `<ConflictResolutionDialog>`
   reusado. Sin acción específica para OC.

2. **"Cancelar + duplicar" sin precedente en RQ.**
   El comando `DuplicarOrdenCompra` con campo `oc_origen_id` es **nuevo
   en OC** (cierre de C4 como cancelar + recrear). RQ no lo tiene. Si
   surge necesidad equivalente en CxP o Activos Fijos, evaluar promover
   el patrón a `SharedKernel` (`IDuplicableAgregado<T>` + servicio de
   copia parametrizada por whitelist de campos).

3. **Sub-estados materializados como decisión local.**
   C7 (columnas materializadas vs derivar por agregación) es una
   decisión local de OC. Si el reporte de partidas abiertas degrada
   bajo carga real (Fase 7), evaluar vista materializada (alternativa
   ya documentada).

4. **Librería PDF cerrada: QuestPDF** (decisión Rev. 3 del 02-plan).
   F6-PR3 implementa `QuestPdfOrdenCompraImpl` directamente. NuGet
   `QuestPDF`, community edition (gratis para Millet por revenue).

5. **Compromiso exclusivo asume migración aditiva en RQ existente.**
   F4-PR1 agrega `comprometida_en_oc_id` a `compras.requisiciones`.
   **Coordinar con dueño de RQ** antes de mergear (ver §1.2 de este
   doc). Si RQ tiene PRs abiertos que tocan la misma tabla, esperar
   o coordinar conflict resolution.

6. **Listeners cross-BC son stubs in-process hasta que módulos reales existan.**
   F5-PR2 define los listeners pero no se disparan en producción
   hasta que CxP/Tesorería/Almacén-Devolución emitan eventos reales.
   Los tests usan eventos mock disparados manualmente. Documentar
   claramente en operación.

7. **Decoración de endpoints idempotentes desde el inicio (cerrado
   Rev. 2 del 04).** Cada PR que agregue un POST de mutación incluye
   `[RequireIdempotencyKey]` desde el momento. F10-PR1 se reduce a
   auditoría de cobertura.

8. **OpenAPI sin descripciones hasta F10-PR2.**
   Mismo patrón heredado de RQ. Si se documenta endpoint por endpoint
   desde F1, F10-PR2 se reduce a auditoría.

---

## Cambios respecto a versiones previas

### Rev. 2 — cierre de decisiones backend (2026-05-11)

Tras 3 rondas de decisiones con owner, alineado a 01 Rev. 0.4,
02 Rev. 3 y 03 Rev. 3:

- **§13 reescrita**: de "Reapertura post-autorización (C4)" a
  "Duplicación de OC (C4 cerrado como cancelar + recrear)". 4 reglas
  nuevas sobre el comando `DuplicarOrdenCompra` (estados válidos,
  qué NO copia, vínculo `oc_origen_id`, KPI).
- **§2.4 reescrita**: "Reapertura post-autorización" → "Duplicación
  cross-agregado: trazabilidad firme" (verifica que `oc_origen_id`
  se persiste en misma transacción).
- **§6.2**: eliminada `VersionOC` de la lista de entidades `IAuditable`
  (de 5 a 4).
- **§8.3**: simplificada — doble autorización solo aplica a "cancelar
  con recepciones parciales" (F5-PR4); eliminada referencia a
  reapertura.
- **§15.3**: eliminada (PDF v1 conservado al reabrir — N/A).
- **§16.2**: quitada mención a `EnReautorizacion`.
- **§12.5**: 8 transiciones → 7 transiciones principales.
- **Hallazgos #2, #4, #7**: actualizados con decisiones cerradas
  (duplicación en lugar de reapertura, QuestPDF cerrada, idempotency
  desde el inicio).

### Rev. 1 — versión inicial (2026-05-11)

Primer corte de cuidados de infra para OC. 17 bloques (vs 14 en el
de RQ). Calibrado contra el diseño Rev. 0.3, plan Rev. 2, PR
breakdown Rev. 2, y los cuidados ya validados en RQ.

**Filosofía**: enfatiza lo nuevo de OC (sub-estados materializados,
reapertura, compromiso exclusivo cross-agregado, PDF, logística
estructurada). Donde el cuidado es heredado, apunta al §X
correspondiente del 04 de RQ en lugar de duplicar.
