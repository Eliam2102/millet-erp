# Diseño — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Construido sobre:** [00-levantamiento.md](00-levantamiento.md) (Rev. 1, 2026-05-22).
>
> **Anexos:**
> - [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) — diseño técnico detallado del sub-módulo Tarjetas de Crédito Empresariales (el más complejo del módulo).
>
> **Hereda contexto de:**
> - [`docs/modulos/compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) — submódulo OC ya implementado; define los puntos de integración OC → CXP (lectura de OCs autorizadas, eventos `FacturaProveedorRechazadaPorToleranciaEvent`, `FacturaProveedorRegistradaEvent`, etc.).
> - [`docs/modulos/compras-requisiciones/01-diseno.md`](../compras-requisiciones/01-diseno.md) — patrón de hexagonal + CQRS + matriz de autorización que CXP reutiliza para el workflow de revisión.
> - [`docs/modulos/administracion/01-diseno.md`](../administracion/01-diseno.md) — dueño del master de `Proveedor`, `Sucursal`, `Empleado`, `ConceptoContable` (futuro), `TipoCambio`. CXP los consume vía read ports.
>
> **Estado:** propuesta de diseño v1 para revisión con el owner. Las decisiones marcadas como `[Asunción]` requieren confirmación antes de implementar; están listadas en §3. Los puntos cross-módulo con Almacén **están cerrados** en [`docs/modulos/almacen/00-levantamiento.md`](../almacen/00-levantamiento.md) (v0.1, 2026-05-22).
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer este documento

- `[Decidido]` — fijado por ADR existente, por el mapa funcional §13 ya cerrado, o por sesión con el cliente.
- `[Asunción]` — propuesta del diseñador. Razonable pero pendiente de confirmación. Está claramente listada en §3.
- `[Diferido]` — fuera de alcance de v1; se anota para no perderlo.
- `[Pendiente]` — la decisión existe pero se cierra antes del 02-plan o antes de implementar el bloque correspondiente.
- `[Pendiente — Almacén]` — **histórico**: punto cerrado en `almacen/00-levantamiento.md` v0.1 (2026-05-22). El marcador se conserva para trazabilidad.

Este documento describe **qué construir y por qué**, no el código. La implementación seguirá las convenciones del repo (hexagonal, CQRS con MediatR, EF Core, FluentValidation, Mapster, Serilog, records, sealed por defecto, nullable reference types).

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

- **Módulo:** `CuentasPorPagar`. Módulo de back-office independiente, no submódulo de Compras (a diferencia de OC y Requisiciones que sí comparten módulo).
- **Esquema PostgreSQL:** `cuentas_por_pagar` (ADR-0030 — un esquema por módulo).
- **Bounded context:** `CuentasPorPagar`. Dueño de pasivos con proveedores y de los CFDIs recibidos. Comparte ubicuamente con `Compras` los conceptos `Proveedor`, `OC`, `Recepcion` pero los **consume vía puerto de lectura**, no comparte tablas.
- **Proyecto .NET:** `backend/src/CuentasPorPagar/` (nuevo). Namespaces: `Millet.CuentasPorPagar.Domain`, `Millet.CuentasPorPagar.Application`, `Millet.CuentasPorPagar.Infrastructure`, `Millet.Api.Endpoints.CuentasPorPagar`.
- **DbContext:** `CuentasPorPagarDbContext` (ADR-0030). Se registra en `MigrationsHealthCheckOptions.ContextTypes` y en `deploy-app-dev.yml` (memoria `feedback_dbcontext_nuevo_checklist`).

### 1.2 Alcance funcional v1 (MVP)

**Dentro:**

1. **Recepción y normalización de CFDIs** — entidad `CfdiRecibido` (§4.1 del levantamiento). Tres canales: descarga SAT vía FiscalAPI, mailbox dedicado, carga manual. Pre-carga al capturar.
2. **Captura de factura con OC** (flujo estándar). Conciliación con tolerancia por proveedor; rechazo con devolución a Compras vía evento.
3. **Captura de factura sin OC** — 4 variantes (Caja Chica, Viáticos, TC Empresarial, Otros).
4. **Aduanales con doble autorización** (Comercio Exterior + Dirección).
5. **Notas de crédito del proveedor** — vinculación a factura origen o a anticipo (relación CFDI tipo 01, 03, 07).
6. **Anticipos a proveedores** — registro del CFDI (serie FANT), amortización contra facturas finales, contra NC por amortización (relación 07).
7. **Notas de cargo** — documento interno, formalización fiscal opcional vía NC del proveedor.
8. **Comprobaciones de gastos** — entidad agrupadora para Caja Chica, Viáticos, TC Empresarial.
9. **Tarjetas de Crédito Empresariales** — `MovimientoTarjetaCredito` + `EstadoCuentaTC`, modelo de pasivo agregado contra el banco.
10. **Workflow de revisión** — flag `en_revision` a nivel de factura y de proveedor, con catálogo unificado de motivos y SLA de 5 días hábiles.
11. **Autorizaciones informales con evidencia** — `EvidenciaAutorizacion` polimórfica con upload de capturas, audios, emails.
12. **Bandejas y reportes** — antigüedad de saldos, antigüedad de anticipos, cartera por categoría × revisión, CFDIs por capturar, pasivos en revisión, TC empresarial.
13. **Eventos de integración** — publicar a Tesorería (`PasivoAutorizadoParaPagoEvent`), a Compras (`FacturaProveedorRegistradaEvent`, `FacturaProveedorRechazadaPorToleranciaEvent`), a Almacén (`DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`); suscribir de Tesorería (`PagoFacturaProveedorEvent`, `PagoFacturaProveedorRevertidoEvent`, `ReppProveedorRecibidoEvent`, `CancelacionPasivoSolicitadaEvent`); suscribir de Almacén (`OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`). Naming canónico alineado con Compras-OC §8.6.
14. **Workers en-proceso** — `CfdiMailboxIngestionWorker`, `CfdiDescargaMasivaSatWorker`, `CfdiEstadoSatRefreshWorker`, `OutboxPublisherWorker<CuentasPorPagarDbContext>` (ADR-0022).
15. **RBAC granular** — permisos canónicos `cuentas_por_pagar.*` (ADR-0007).
16. **Notificaciones de SLA** — emails al gerente del área en día 3 y 5; escalamiento al director en día 10 (ADR-0026).
17. **Auditoría** — framework centralizado (ADR-0008), bitácora de cambios de estado en cada entidad.

**Fuera (cerrado en §13 del levantamiento):**

- **Portal propio de proveedores** — `[Diferido]` post-MVP. Modelo de `CfdiRecibido.canal_origen` ya lo contempla con `PortalProveedor`.
- **Ejecución de pagos** — vive en Tesorería; CXP solo entrega pasivos autorizados vía evento.
- **Registro del REPP recibido** — vive en Tesorería; CXP solo lo refleja como informativo.
- **Master de Proveedor** — vive en `DatosMaestros` del área Administración. CXP solo extiende con los atributos operativos que requiere (tolerancia, en_revision, etc. — ver §5.1 del levantamiento).
- **Préstamos a empleados (códigos Axxxx)** — viven en RH / Tesorería; CXP los referencia.
- **CRUD de catálogos** — `[Diferido]` post-MVP, seeds versionados + endpoints `GET` read-only en MVP.
- **Importación inicial de CFDIs históricos masiva** — `[Diferido]` post-MVP. MVP arranca con fecha de corte para la descarga inicial.
- **Sync automatizado del estado de cuenta del banco (TC Empresarial)** — `[Diferido]`. MVP es captura manual.
- **Workflow engine genérico** — `[Asunción A4]`: máquina de estados ad-hoc dentro del agregado, igual que Requisiciones.

**Cerrado con Almacén (2026-05-22)** — ver [`almacen/00-levantamiento.md`](../almacen/00-levantamiento.md):

- ✅ Contrato `OcRecepcionRegistradaEvent` que CXP suscribe (variantes A insumos / B materiales directos). Detalle en §5 de Almacén y §11.6 del 00-levantamiento de CxP.
- ✅ Contrato `OcDevolucionRegistradaEvent` que CXP suscribe (sub-flujo 8.B de Almacén §8.4).
- ✅ Contratos bidireccionales para devoluciones: CxP publica `NotaCreditoFiscalDevolucionRecibidaEvent` cuando llega la NC fiscal del proveedor; Almacén la usa para cerrar la devolución con flag `ConciliadaConNcFiscal`.
- ✅ Contrato `FacturaProveedorRegistradaEvent` (CxP publica para variante B también; Almacén concilia recepción pendiente).
- ✅ Contrato `DiferenciaPrecioFacturaDetectadaEvent` (CxP publica para variante B; Almacén ajusta costo de inventario remanente).
- Three-way match (OC + Recepción + Factura): el flujo "factura con OC" v1 sigue validando solo OC + Factura en el momento de captura. Almacén notifica recepción por evento; CxP la consume como insumo de conciliación pero no la bloquea para casos en que la factura llegue antes que la recepción.

### 1.3 Volúmenes esperados

`[Asunción A1]` basada en lo observado en el reporte SAP actual:

- ~500 CFDIs recibidos / mes (mix factura + NC + anticipo).
- ~30 movimientos de TC empresarial / mes (un solo titular Amex en operación hoy).
- ~5-10 comprobaciones de viáticos / mes.
- ~10-15 reembolsos de caja chica / mes.
- ~50 anticipos a proveedores / año.
- 2-3 usuarios concurrentes en CXP (Auxiliar + responsables de revisión).

**Implicación de diseño:** sin requerimientos especiales de escala. Un solo nodo .NET sirve. Los workers de FiscalAPI son los únicos componentes que pueden generar carga por descarga masiva — se diseñan con backoff y schedule nocturno.

---

## 2. Decisiones de diseño y ADRs aplicados

Hereda los ADRs transversales del proyecto. La tabla resume los aplicados; el detalle vive en cada ADR.

| Tema | Decisión | Referencia |
|---|---|---|
| Identidad y autorización | Entra ID + RBAC granular por permisos | [ADR-0003](../../decisiones/0003-autenticacion-entra-id.md), [ADR-0007](../../decisiones/0007-autorizacion-rbac-granular.md) |
| Concurrencia | Optimista por `Version` `IsConcurrencyToken` (Capa 1) + Soft lock vía SignalR (Capa 2) | [ADR-0012](../../decisiones/0012-concurrencia-hibrida.md) |
| Auditoría | Framework centralizado, no columnas duplicadas en cada tabla | [ADR-0008](../../decisiones/0008-estrategia-auditoria.md) |
| Migraciones | EF Core, esquema por módulo | [ADR-0005](../../decisiones/0005-migraciones-ef-core-esquema-por-modulo.md) |
| Multi-DbContext | Cada módulo con su DbContext + esquema | [ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md) |
| Validación | FluentValidation por comando | [ADR-0018](../../decisiones/0018-validacion-fluentvalidation.md) |
| Eventos de integración | Outbox + Service Bus | [ADR-0009](../../decisiones/0009-outbox-pattern-eventos-integracion.md) |
| Errores HTTP | Problem Details (RFC 7807) | [ADR-0010](../../decisiones/0010-manejo-errores-problem-details.md) |
| Versionado API | URL versioned (`/api/v1/...`) | [ADR-0021](../../decisiones/0021-versionado-api-rest.md) |
| Idempotencia HTTP | Header `Idempotency-Key` en POST que crean recursos | [ADR-0020](../../decisiones/0020-idempotencia-http.md) |
| Notificaciones email | Vía módulo Notificaciones | [ADR-0026](../../decisiones/0026-notificaciones-email.md) |
| Multi-empresa | `EmpresaId` en agregados con datos por sociedad | [ADR-0011](../../decisiones/0011-multi-empresa-empresa-id.md) |
| Tiempo / zonas | UTC en BD, conversión en presentación | [ADR-0013](../../decisiones/0013-tiempo-zona-horaria.md) |
| Multimoneda | Value object `Money`. v1 fija MXN; importaciones capturan T/C manual | [ADR-0014](../../decisiones/0014-money-multimoneda-tipos-de-cambio.md) |
| Adjuntos | Blob storage (no `image` en BD) | [ADR-0024](../../decisiones/0024-almacenamiento-documentos.md) |
| Generación PDF | Servicio compartido | [ADR-0025](../../decisiones/0025-generacion-pdfs.md) |
| Integración PAC | FiscalAPI como PAC del proyecto | [ADR-0027](../../decisiones/0027-integracion-pac-onefactura.md) |
| Background jobs | `IHostedService` dentro de `Millet.Api` | [ADR-0022](../../decisiones/0022-background-jobs.md) |
| Stubs cross-module y deuda de plataforma | Sección "Dependencias de plataforma pendientes" + `PLATFORM-TODO` en código | [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md) |

> Los ADRs son la fuente de verdad. Si este documento contradice un ADR, gana el ADR.

---

## 3. Asunciones que deben confirmarse

| # | Asunción | Default propuesto | Si el cliente dice "no" |
|---|---|---|---|
| A1 | **Volumetría** | ~500 CFDIs/mes, 2-3 usuarios concurrentes. Un solo nodo .NET, sin sharding ni cache distribuido para v1. | Si el volumen real es 10x mayor, evaluar particionamiento de `CfdiRecibido` por mes y vista materializada para bandejas. |
| A2 | **Multimoneda en MVP** | `Money` soporta otras monedas; v1 calibra MXN. Importaciones capturan moneda extranjera con T/C manual. Reporteo agregado en MXN al T/C del día de captura. | Si se exige integración con Banxico/DOF, se agrega puerto `ITipoCambioPort` con stub. |
| A3 | **Formato del folio interno de notas de cargo** | `NCG-{año}-{secuencial:6}` (ej. `NCG-2026-000001`). Generación atómica vía secuencia PostgreSQL por año. | Si el área prefiere folio por sucursal o por proveedor, se ajusta el VO y la secuencia. |
| A4 | **Workflow de revisión: máquina de estados ad-hoc** | Misma estrategia que Requisiciones — máquina de estados dentro del agregado, sin engine genérico. Reusa matriz de autorización de Requisiciones extendida con `dependencia_revisora_id`. | Si el área pide flexibilidad de re-configurar flujos en vivo, evaluar workflow engine. Implica retrabajo. |
| A5 | **Estados del pasivo** | 5 estados: `Capturada`, `EnRevision`, `Autorizada`, `Pagada`, `Cancelada` (con `motivo_cancelacion`). Saldo parcial se ve por `saldo_pendiente`, no por estado. | Si se exigen estados separados Parcial/Total y un estado `Rechazada` explícito, se revierte al modelo de v0.1. Decisión cerrada por el área en el mapa v0.2. |
| A6 | **Captura de TC Empresarial sin CFDI** | Cuando el movimiento de TC no tiene CFDI (ticket no fiscal), **no genera `FacturaProveedor`**. Solo afecta gasto (sin IVA acreditable) y queda colgado del `MovimientoTarjetaCredito`. | Si el área quiere generar `FacturaProveedor` con UUID null aún sin CFDI, se ajusta el esquema y se relaja la unicidad. |
| A7 | **Inmutabilidad post-autorización** | Una factura `Autorizada` no se puede modificar. Si hay error, se cancela con motivo y se recaptura. Mismo patrón que OC (decisión C4 de compras-ordenes-compra). | Si el área quiere edición con re-aprobación, se agrega estado `EnReautorizacion` con bitácora de versiones. |
| A8 | **Pre-carga del XML** | Parser de CFDI 4.0 en el adapter de Infrastructure. Extrae proveedor (por RFC emisor), conceptos, montos, impuestos, retenciones. Si el parser falla, el `CfdiRecibido` queda en `PorProcesar` con bandera "Parser fallido". | Si el área pide parseo más fino (ej. extracción de pedimento del addenda), se amplía el parser. |
| A9 | **Match automático CFDI ↔ OC** | Heurística: RFC + total ± tolerancia del proveedor + (folio si viene en `numCfdi` o referencia). Devuelve top 3 candidatos para que el Auxiliar elija. | Si la heurística es demasiado ruidosa, se cambia a "sugiere solo si match exacto, deja en blanco si no". |
| A10 | ✅ **Catálogo de aprobadores con límites** (Decidido 2026-05-22, §13.1 punto 2 del levantamiento) | Tabla `cuentas_por_pagar.aprobadores_limites` con `(empleado_id, tipo_gasto, monto_maximo, vigencia_desde, vigencia_hasta)`. **Granular por usuario** (no por puesto). El catálogo lo llena RH o el responsable de CxP. | — |
| A11 | **Idempotency-Key obligatorio en POST de captura** | El cliente debe enviar `Idempotency-Key` único por captura. Repetir el mismo key con el mismo body retorna el mismo resultado (ADR-0020). | Si UX prefiere no exigirlo, el servidor genera key derivado de `(usuario, uuid_cfdi)` y la idempotencia degrada a "última escritura gana en colisión". |
| A12 | **ETag/If-Match en mutaciones de cabecera** | Concurrencia optimista (ADR-0012). Toda PATCH/PUT requiere `If-Match` con el ETag actual. Conflicto → 412. | Estándar del proyecto; no abierto a ajuste. |
| A13 | **PDFs adjuntos en blob, no en BD** | Storage account de Azure (ADR-0024). `CfdiRecibido` guarda referencia blob, no el binario. | Estándar; no abierto a ajuste. |
| A14 | **Cancelación de CFDI por el proveedor** | Worker `CfdiEstadoSatRefreshWorker` corre cada 6h y refresca el estado de los CFDIs autorizados. Si detecta cancelación, mueve la factura a `Cancelada` con motivo "CFDI cancelado por el proveedor en el SAT", notifica a CXP. | Si el área pide validación al momento de pago en lugar de polling, se delega a Tesorería como check pre-pago. |
| A15 | ✅ **Tarjetas de crédito como master local** (Decidido 2026-05-22, §13.1 punto 4 del levantamiento) | Catálogo `cuentas_por_pagar.tarjetas_credito` vive en el módulo. Modelo: **varias TC corporativas con titular fijo por tarjeta** (no por sucursal). Atributos: `(emisora, numero_enmascarado, titular_id, banco_proveedor_id, limite, fecha_corte, dia_pago, estado)`. | — |
| A16 | **Almacén — flujos diferidos** | El three-way match completo (OC + Recepción + Factura) y la sincronización de NC fiscal con salida física se difieren hasta levantar Almacén. v1 valida solo OC + Factura y aplica NC inmediatamente al saldo (sin bloqueo por salida física). | Cuando Almacén se levante, se agrega la dependencia bidireccional como migración aditiva no-breaking del esquema. |
| A17 | ✅ **Cadena de suplencias DG/DF/DG-otra-área** (Decidido 2026-05-22, §13.1 punto 1) | Cadena de 3 niveles. Tercer nivel (DG de otra área) se configura como registros en `aprobadores_limites` con vigencia. RH mantiene. La UI lee la cadena al presentar la pantalla de autorización y muestra el actor activo según `vigencia_desde <= hoy <= vigencia_hasta`. | — |
| A18 | ✅ **Política de viáticos por puesto + tipo de destino** (Decidido 2026-05-22, §13.1 punto 3) | Tabla `cuentas_por_pagar.politicas_viaticos` con `(puesto_id, tipo_destino, monto_max_dia, dias_max, moneda)`. `tipo_destino ∈ { Nacional, Internacional }`. Validación al solicitar anticipo de viáticos; si excede → segunda firma de DF obligatoria. Requiere catálogo `puestos` en `DatosMaestros` (RH lo llena). | — |
| A19 | ✅ **NC pre-factura como excepción `EnEspera`** (Decidido 2026-05-22, §13.1 punto 5) | Operativamente raro. Modelo: `NotaCreditoProveedor` con `factura_origen_id = null` y estado `EnEspera`. Worker `NotaCreditoEnEsperaMatchWorker` cada 24h intenta hacer match contra facturas nuevas del proveedor por UUID de relación CFDI. Si pasan 30 días sin match → alerta al Auxiliar. Si volumen >5/mes → bandeja dedicada en v1.1. | — |
| A20 | ✅ **Conciliación de TC por carga Excel/CSV del portal del banco** (Decidido 2026-05-22, §13.1 punto 6) | Auxiliar descarga el estado de cuenta del portal del banco como Excel/CSV y lo sube al sistema. Parser hace match por `fecha + monto + merchant` (similitud). Movimientos no conciliados quedan en bandeja. Configuración por banco emisor (nombre de columnas, formato de fecha, separador). Integración API del banco diferida a vNext. | — |
| A21 | ✅ **Catálogo de dependencias revisoras en `Administracion`/`DatosMaestros`** (Decidido 2026-05-22, §13.1 punto 7) | Catálogo compartido para reuso por Notificaciones, RH, Obras. Naming exacto del schema/tabla (`datos_maestros.dependencias` o `administracion.areas_organizacionales`) pendiente de coordinación con el módulo Administración antes de implementar. CxP consume vía `IDependenciaRevisoraReadPort`. | Si Administración decide que el catálogo es local de cada módulo, se mueve a CxP sin breaking change. |
| A22 | ✅ **SLA único 5 días + excepción 15d para disputas contractuales** (Decidido 2026-05-22, §13.1 punto 8) | SLA único en MVP. Notificaciones día 3 / día 5 / día 10 con escalamiento. Excepción configurada como flag `sla_dias` por motivo de revisión (defecto 5; "Disputa contractual" tiene 15). Instrumentación permite diferenciar en v1.1 si los reportes muestran patrones. | — |

---

## 3.bis Conceptos derivados de las decisiones

### 3.bis.1 Separación `CfdiRecibido` ↔ `FacturaProveedor`

Decisión cerrada en el mapa v0.2. Razón de diseño:

- Un CFDI puede llegar y **nunca convertirse en pasivo** (duplicado, descartado, capturado por error). Su ciclo de vida es independiente.
- La precarga al capturar es más rápida si los datos del XML ya están extraídos y validados.
- La deduplicación por UUID se hace una sola vez en el ingreso, no en cada captura.
- El histórico de CFDIs (incluso los no convertidos) sirve para auditoría y reporteo.

Implicación: el agregado raíz es `FacturaProveedor` (con FK opcional a `CfdiRecibido`). `CfdiRecibido` es un agregado independiente — su ciclo no depende de `FacturaProveedor`. Cuando se convierte, se actualiza el campo `estado` a `ConvertidoEnPasivo` con FK al pasivo creado.

### 3.bis.2 Modelo de TC Empresarial en dos pasos

El movimiento de TC tiene dos pasivos lógicos en momentos distintos:

1. **Al capturar el movimiento:** el proveedor del gasto ya está saldado (el banco le pagó). El gasto se contabiliza inmediatamente.
2. **Al cerrar el periodo:** el `EstadoCuentaTC` genera un `FacturaProveedor` contra el banco emisor (proveedor especial — el banco está en el master de Proveedores con un tipo "BancoEmisorTC").

Esto resuelve la incompatibilidad de "cada CFDI individual debe afectar gasto/IVA/DIOT" (anotación del área) vs. "el pasivo agregado es con el banco" (modelo financiero real).

Implicación: la `FacturaProveedor` del banco no tiene OC. Su autorización es por Dirección de Finanzas (no por la OC). Su validación es contra el total del estado de cuenta, no contra OC.

### 3.bis.3 Tolerancia por proveedor

Reemplaza el global de $0.99 MXP de v0.1. Atributos en el master de Proveedor:

```
Proveedor:
  tolerancia_tipo     enum { MONTO_ABSOLUTO, PORCENTAJE }
  tolerancia_valor    decimal  // MXP si MONTO_ABSOLUTO, % si PORCENTAJE
```

Si `tolerancia_valor` está null, se aplica el default global del módulo (parámetro de Administración: `cuentas_por_pagar.tolerancia_default_mxp = 0.99`).

Implementación: el snapshot de tolerancia se toma al momento de captura y se persiste en `FacturaProveedor` para que el cálculo histórico sea estable aunque el master se modifique.

### 3.bis.4 Polimorfismo de `EvidenciaAutorizacion`

`EvidenciaAutorizacion` apunta polimórficamente a `FacturaProveedor`, `AnticipoProveedor`, `NotaCargo` o `ComprobacionGastos`. Modelo en PostgreSQL: tabla única con `(tipo_documento enum, documento_id uuid)` + check constraint que valida que el FK exista según el `tipo_documento`. No se usa herencia EF — es una relación polimórfica explícita.

Razón: el comportamiento de evidencia es idéntico en los 4 tipos de documento; duplicar la tabla 4 veces es overhead innecesario.

### 3.bis.5 Compromiso exclusivo OC ↔ Factura

Una OC puede tener N facturas (recepciones parciales pueden generar facturas parciales). Una factura tiene exactamente una OC (o ninguna si es directa). La OC pasa a `Cerrada` cuando el sub-estado `Facturacion` llega a `Completa` (ver `compras-ordenes-compra/01-diseno.md` §3.2).

CXP emite `FacturaRegistrada(oc_id, monto, factura_id)` que Compras consume para actualizar el sub-estado. Compras no consulta a CXP — la sincronización es event-driven.

---

## 4. Modelo del dominio

### 4.1 Agregados raíz

| Agregado | Entidades hijas | Esquema | Razón de ser agregado raíz |
|---|---|---|---|
| `CfdiRecibido` | `ValidacionCfdi` (resultado de validación contra catálogos SAT) | `cuentas_por_pagar` | Ciclo independiente del pasivo; ingresa por workers y se convierte por captura. |
| `FacturaProveedor` | `LineaFacturaProveedor`, `BitacoraEstadoFactura`, `AplicacionAnticipo` (M2M con AnticipoProveedor), `AplicacionNotaCredito` (M2M con NotaCreditoProveedor) | `cuentas_por_pagar` | Entidad central del módulo. Concentra estado, saldo, evidencias. |
| `NotaCreditoProveedor` | `LineaNotaCredito` | `cuentas_por_pagar` | CFDI fiscal independiente; tiene su propio ciclo de captura y aplicación. |
| `AnticipoProveedor` | `AmortizacionAnticipo` (vinculación con `NotaCreditoProveedor` por amortización) | `cuentas_por_pagar` | Tiene saldo amortizable propio, estado independiente. |
| `NotaCargo` | `LineaNotaCargo`, `AdjuntoNotaCargo` | `cuentas_por_pagar` | Documento interno con su propio ciclo: borrador → autorizada → aplicada → formalizada. |
| `ComprobacionGastos` | `LineaComprobacion` (referencia a `FacturaProveedor` hija) | `cuentas_por_pagar` | Agrupador de proceso de Caja Chica, Viáticos, TC Empresarial. |
| `MovimientoTarjetaCredito` | — | `cuentas_por_pagar` | Movimiento individual de TC; agrupa por `EstadoCuentaTC`. |
| `EstadoCuentaTC` | — (M2M con `MovimientoTarjetaCredito`) | `cuentas_por_pagar` | Cierre periódico de la TC; genera `FacturaProveedor` contra el banco. |
| `EvidenciaAutorizacion` | — | `cuentas_por_pagar` | Polimórfica. Vive como agregado pequeño independiente para simplicidad. |

### 4.2 Value Objects

| VO | Propósito | Validaciones |
|---|---|---|
| `UuidCfdi` | Identidad fiscal del CFDI | Formato GUID v4, único |
| `RfcMexicano` | RFC validado | Estructura SAT (12 o 13 chars), validación contra LRFC vía FiscalAPI al alta |
| `FolioInternoNotaCargo` | Folio de nota de cargo | Formato `NCG-{año}-{secuencial:6}`, único por año |
| `MotivoRevision` | Catálogo del §5.3 del levantamiento | Enum + SLA asociado |
| `MotivoCancelacion` | Catálogo de motivos de cancelación de factura | Enum: `RechazadaPorTolerancia`, `CfdiCanceladoEnSat`, `ErrorCaptura`, `OtroConTexto` |
| `Tolerancia` | Tolerancia de conciliación | Tipo (MontoAbsoluto/Porcentaje) + valor |
| `Money` | Importe + moneda | Reutiliza VO compartido (ADR-0014) |
| `PeriodoCorte` | Periodo del estado de cuenta TC | Fecha desde + fecha hasta, validación coherencia |

### 4.3 Invariantes principales

- `FacturaProveedor.saldo_pendiente >= 0` siempre. Una aplicación de NC o anticipo que llevaría a saldo negativo es rechazada.
- `AnticipoProveedor.monto_amortizado <= monto_entregado` siempre.
- `FacturaProveedor` en estado `Pagada` no permite aplicar NC nueva — primero se reabre vía `EnRevision`.
- `FacturaProveedor.total = sum(lineas.total) + impuestos - descuentos + redondeo`.
- `MovimientoTarjetaCredito` solo puede vincularse a un `EstadoCuentaTC` cuya tarjeta coincida.
- `EstadoCuentaTC.total = sum(movimientos.monto)` — discrepancia bloquea el cierre.
- `NotaCargo.estado = Formalizada` requiere FK no-null a `NotaCreditoProveedor`.
- `CfdiRecibido.estado = ConvertidoEnPasivo` requiere FK no-null al pasivo destino (factura, NC o anticipo, según `tipo_cfdi`).

---

## 5. Esquema PostgreSQL

Esquema `cuentas_por_pagar`. Tablas principales (sin detalle de columnas auditoría y concurrencia que vienen del framework — ADR-0008, ADR-0012):

```
cuentas_por_pagar.cfdis_recibidos
cuentas_por_pagar.cfdi_validaciones
cuentas_por_pagar.facturas_proveedor
cuentas_por_pagar.lineas_factura_proveedor
cuentas_por_pagar.bitacora_estado_factura
cuentas_por_pagar.notas_credito_proveedor
cuentas_por_pagar.lineas_nota_credito
cuentas_por_pagar.anticipos_proveedor
cuentas_por_pagar.amortizaciones_anticipo
cuentas_por_pagar.aplicaciones_anticipo_factura
cuentas_por_pagar.aplicaciones_nc_factura
cuentas_por_pagar.notas_cargo
cuentas_por_pagar.lineas_nota_cargo
cuentas_por_pagar.adjuntos_nota_cargo
cuentas_por_pagar.comprobaciones_gastos
cuentas_por_pagar.lineas_comprobacion
cuentas_por_pagar.movimientos_tarjeta_credito
cuentas_por_pagar.estados_cuenta_tc
cuentas_por_pagar.estado_cuenta_tc_movimientos       -- M2M
cuentas_por_pagar.tarjetas_credito                    -- master local (A15) — varias TC corporativas, titular fijo
cuentas_por_pagar.estado_cuenta_tc_lineas_banco       -- líneas parseadas del Excel/CSV del banco (A20)
cuentas_por_pagar.estado_cuenta_tc_archivos           -- ref. al blob del archivo del banco cargado (A20)
cuentas_por_pagar.evidencias_autorizacion             -- polimórfica (A17 cadena de suplencias incluida)
cuentas_por_pagar.aprobadores_limites                 -- catálogo de aprobadores con límite (A10, A17)
cuentas_por_pagar.politicas_viaticos                  -- catálogo por puesto + destino (A18)
cuentas_por_pagar.notas_credito_proveedor_en_espera   -- vista o índice parcial para NC pre-factura (A19)
cuentas_por_pagar.motivos_revision                    -- seed del §5.3 del levantamiento; columna `sla_dias` (A22)
cuentas_por_pagar.outbox_messages                     -- patrón outbox (ADR-0009)

-- Catálogo de dependencias revisoras (A21):
-- NO vive en este schema — vive en datos_maestros o administracion.
-- CxP lo consume vía IDependenciaRevisoraReadPort.
```

### 5.1 Índices críticos

```sql
-- Bandeja de CFDIs por capturar
CREATE INDEX ix_cfdis_recibidos_estado_fecha
  ON cuentas_por_pagar.cfdis_recibidos (estado, fecha_recepcion DESC)
  WHERE estado = 'PorProcesar';

-- Unicidad de UUID a través de canales
CREATE UNIQUE INDEX ux_cfdis_recibidos_uuid
  ON cuentas_por_pagar.cfdis_recibidos (uuid_cfdi);

-- Bandeja de facturas en revisión por área
CREATE INDEX ix_facturas_revision_dependencia
  ON cuentas_por_pagar.facturas_proveedor (dependencia_revisora_id, fecha_entrada_revision)
  WHERE en_revision = true;

-- Antigüedad de saldos
CREATE INDEX ix_facturas_proveedor_saldo
  ON cuentas_por_pagar.facturas_proveedor (proveedor_id, fecha_vencimiento)
  WHERE estado IN ('Autorizada', 'Capturada') AND saldo_pendiente > 0;

-- Match CFDI ↔ OC
CREATE INDEX ix_cfdis_proveedor_total
  ON cuentas_por_pagar.cfdis_recibidos (rfc_emisor, total)
  WHERE estado = 'PorProcesar';

-- Movimientos de TC pendientes de conciliar
CREATE INDEX ix_movimientos_tc_pendientes
  ON cuentas_por_pagar.movimientos_tarjeta_credito (tarjeta_id, fecha_movimiento)
  WHERE estado_cuenta_tc_id IS NULL;
```

### 5.2 Particionamiento

`cuentas_por_pagar.cfdis_recibidos` puede crecer rápido (descarga SAT). En v1 **no se particiona** (memoria `feedback_pr_granularidad` — no premature optimization). Se monitorea volumen; si supera ~1M registros, se evalúa partición por `fecha_recepcion` por trimestre.

### 5.3 Migraciones

EF Core migrations (ADR-0005). Cada PR introduce su set de migraciones. La migración inicial crea el esquema completo + seeds de:

- `motivos_revision` con columna `sla_dias` (A22).
- `tarjetas_credito` (vacío, lo llena el área con sus 2–5 TC corporativas — A15).
- `aprobadores_limites` (vacío, RH lo llena al go-live — A10, A17).
- `politicas_viaticos` (vacío, RH+Dirección lo llenan al go-live — A18).
- El **banco emisor** como `Proveedor` especial (tipo `BancoEmisorTC`) en `DatosMaestros`, uno por cada tarjeta corporativa (coordinación con módulo Administración).

El catálogo de dependencias revisoras (A21) **no se crea en este schema** — vive en `DatosMaestros`/`Administracion`. La migración de CxP incluye solo el `IDependenciaRevisoraReadPort` y sus tests.

---

## 6. Puertos y adaptadores

### 6.1 Puertos de lectura (CXP consume)

| Puerto | Quién implementa | Uso |
|---|---|---|
| `IComprasOcReadPort` | Adapter en `Compras.Infrastructure` (read-only query sobre `compras.ordenes_compra`) | Listar OCs autorizadas del proveedor; obtener detalle de OC para conciliar |
| `IProveedorReadPort` | Adapter en `DatosMaestros.Infrastructure` | Obtener master de proveedor (RFC, datos bancarios, tolerancia, subcategoría, `en_revision`, adjuntos) |
| `ISucursalReadPort` | Adapter en `Administracion.Infrastructure` | Catálogo de sucursales |
| `IEmpleadoReadPort` | ✅ `EmpleadoReadPortAdapter` sobre `compartido.empleados` (ADM-PR2, doc 10 de Administración) | Catálogo de empleados (titulares de TC, autores de comprobaciones). Atributo `puesto_id` requerido para política de viáticos (A18) |
| `IPuestoReadPort` | ✅ `PuestoReadPortAdapter` sobre `compartido.puestos` (ADM-PR2) | Catálogo de puestos (A18) |
| `IDependenciaRevisoraReadPort` | Adapter en `Administracion.Infrastructure` (stub `NoOpDependenciaRevisoraReadPort` con seed local hasta que Administración exponga el catálogo — A21) | Catálogo compartido de dependencias / áreas organizacionales para asignar revisión |
| `IConceptoContableReadPort` | Adapter en `Contabilidad.Infrastructure` (stub hoy) | Mapeo `ConceptoContable` → `CuentaContable` |
| `ITipoCambioReadPort` | Adapter en `Administracion.Infrastructure` | T/C manual del día (ADR-0014) |
| `IFiscalApiClient` | Adapter en `CuentasPorPagar.Infrastructure` | Descarga SAT, validación RFC, consulta estado CFDI (ADR-0027) |
| `IEstadoCuentaTcParserPort` | Adapter en `CuentasPorPagar.Infrastructure` (A20) | Parser configurable de Excel/CSV del portal del banco. Perfil por banco emisor (columnas, formato fecha, separador). |
| `IAlmacenRecepcionReadPort` | Adapter en `Almacen.Infrastructure` (cuando el módulo esté en runtime; stub `NoOpAlmacenRecepcionReadPort` hasta entonces) | Verificar si una OC tiene recepción registrada (proyección local de eventos `OcRecepcionRegistradaEvent`), incluyendo flag `factura_pendiente` para variante B |

### 6.2 Puertos de escritura (CXP expone)

| Puerto | Consumidores | Uso |
|---|---|---|
| `ICfdiIngestionPort` | `CfdiMailboxIngestionWorker`, `CfdiDescargaMasivaSatWorker`, endpoint `POST /api/v1/cuentas-por-pagar/cfdis/cargar` (carga manual) | Punto único de ingreso de CFDIs al sistema. Dedupe + validación + persistencia. |
| `IFacturaProveedorCommandPort` | Endpoints HTTP, handlers MediatR | Capturar, modificar (pre-autorización), cancelar, aplicar NC, aplicar anticipo |
| Eventos del Outbox | Tesorería (futuro), Compras, Contabilidad | Publicación de eventos de integración (§8) |

### 6.3 Adapters de Infrastructure

- `FiscalApiClient` — HTTP client con Polly (retry + circuit breaker). Cliente HTTP tipado, registrado con `AddHttpClient<IFiscalApiClient, FiscalApiClient>()`.
- `XmlCfdiParser` — Parser de CFDI 4.0. Extrae cabecera, líneas, impuestos, complementos relevantes (Pagos, CartaPorte). Implementación stateless, testeada con corpus de XMLs reales.
- `BlobAdjuntosStorage` — Almacenamiento de XMLs, PDFs y evidencias en Azure Blob (ADR-0024). Convención de path: `cxp/{año}/{mes}/{tipo}/{uuid_o_id}.{ext}`.
- `MailboxImapClient` — Cliente IMAP (Microsoft Graph si el mailbox es M365) que poll-ea el buzón, descarga adjuntos, los pasa a `ICfdiIngestionPort`.
- `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<CuentasPorPagarDbContext>` — patrón Outbox (ADR-0009), reutilizado.

---

## 7. Comandos y queries (CQRS)

Patrón MediatR del proyecto. Lista no exhaustiva pero canónica.

### 7.1 Comandos (escritura)

```
Cfdi:
  IngresarCfdiCommand
  MarcarCfdiDuplicadoCommand
  DescartarCfdiCommand

FacturaProveedor:
  CapturarFacturaConOcCommand
  CapturarFacturaSinOcCommand
  EnviarFacturaARevisionCommand
  LiberarRevisionFacturaCommand
  CancelarFacturaCommand
  AplicarNotaCreditoAFacturaCommand
  AplicarAnticipoAFacturaCommand

NotaCreditoProveedor:
  CapturarNotaCreditoCommand
  VincularNotaCreditoAFacturaCommand
  VincularNotaCreditoAAnticipoCommand

AnticipoProveedor:
  CapturarAnticipoCommand
  CancelarAnticipoCommand

NotaCargo:
  CrearNotaCargoCommand
  AdjuntarEvidenciaNotaCargoCommand
  AutorizarNotaCargoCommand
  AplicarNotaCargoCommand
  FormalizarNotaCargoConNcCommand
  CancelarNotaCargoCommand

ComprobacionGastos:
  CrearComprobacionGastosCommand           // Caja Chica, Viáticos, TC, Otros
  AgregarLineaComprobacionCommand
  EnviarComprobacionARevisionCommand
  AutorizarComprobacionCommand
  AplicarComprobacionCommand

Viaticos:
  SolicitarAnticipoViaticosCommand
  AutorizarAnticipoViaticosNivel1Command
  AutorizarAnticipoViaticosNivel2Command
  ComprobarViaticosCommand

TarjetaCredito:
  RegistrarMovimientoTcCommand
  CerrarEstadoCuentaTcCommand
  GenerarPasivoEstadoCuentaTcCommand

Provedor (extensión de DatosMaestros):
  PonerProveedorEnRevisionCommand
  LiberarProveedorDeRevisionCommand
  AjustarToleranciaProveedorCommand        // restringido por permiso

Evidencias:
  AdjuntarEvidenciaAutorizacionCommand
  MarcarFirmaFisicaRecibidaCommand
```

### 7.2 Queries (lectura — vista materializada vs. SQL)

```
Bandejas:
  CfdisPorCapturarQuery
  FacturasEnRevisionPorAreaQuery
  FacturasAutorizadasPorPagarQuery     // para Tesorería (también vía evento)
  ComprobacionesEnCapturaQuery
  AutorizacionesConFirmaPendienteQuery
  MovimientosTcPendientesConciliarQuery
  EstadosCuentaTcConsolidadoQuery

Detalle:
  GetFacturaProveedorByIdQuery
  GetCfdiRecibidoByIdQuery
  GetAnticipoProveedorByIdQuery
  GetNotaCargoByIdQuery
  GetComprobacionGastosByIdQuery
  GetEstadoCuentaTcByIdQuery

Reportes:
  AntiguedadSaldosProveedoresQuery
  AntiguedadAnticiposProveedoresQuery
  CarteraPorCategoriaRevisionQuery
  PasivosObrasQuery                     // expone para módulo Obras
```

---

## 8. Eventos de integración

### 8.1 Eventos publicados por CXP

Patrón Outbox + Service Bus (ADR-0009). Todos llevan `correlation_id`, `timestamp`, `version`, `tenant`.

> **Convención de naming canónica:** `{Agregado}{Verbo}Event` con sufijo `Event` y prefijo del agregado de origen. Alineado con Compras-OC §8.5–8.6 (primer doc de diseño cerrado). Ver también CLAUDE.md "Triada Compras ↔ Almacén ↔ CxP" para la tabla maestra.

| Evento | Trigger | Consumidor primario | Payload mínimo |
|---|---|---|---|
| `CfdiRecibidoIngresadoEvent` | Worker ingresa CFDI | Informativo (auditoría, reporting) | `uuid_cfdi`, `rfc_emisor`, `tipo`, `total`, `canal_origen` |
| `FacturaProveedorRegistradaEvent` | Captura de factura con OC en estado `Capturada` | **Compras** (actualiza `CantidadFacturada` y `SubEstadoFacturacion` de OC); **Almacén** (variante B: concilia recepción pendiente) | `factura_id`, `oc_id`, `recepcion_id?`, `total`, `lineas[]`, `lineas_acumuladas_oc[]` (PR #288 — `{ linea_oc_id, cantidad_acumulada }` calculado en CxP sumando facturas previas vigentes + delta de la factura corriente; Compras lo aplica directo en `RegistrarFacturacionLinea`) |
| `FacturaProveedorRechazadaPorToleranciaEvent` | Conciliación con OC falla por exceder tolerancia | **Compras** (regresa OC a revisión / corrección) | `oc_id`, `factura_id`, `total_factura`, `total_oc`, `diferencia`, `tolerancia_aplicada` |
| `FacturaProveedorAutorizadaEvent` | Pasa a `Autorizada` | Compras (informativo), Contabilidad | `factura_id`, `oc_id?`, `fecha_autorizacion` |
| `FacturaProveedorCanceladaEvent` | Pasa a `Cancelada` | **Compras** (decrementa `CantidadFacturada`), Contabilidad | `factura_id`, `motivo_cancelacion`, `oc_id?` |
| `PasivoAutorizadoParaPagoEvent` | `FacturaProveedorAutorizadaEvent` y datos bancarios completos | **Tesorería** | `factura_id`, `proveedor_id`, `monto`, `vencimiento`, `datos_bancarios`, `evidencias_autorizacion[]` |
| `NotaCargoAutorizadaEvent` | `NotaCargo` autorizada por Dirección | Compras (informativo) | `nota_cargo_id`, `proveedor_id`, `monto`, `factura_origen?` |
| `NotaCreditoProveedorRegistradaEvent` | Captura de NC del proveedor (relación CFDI tipo 01, 03 o 07) | **Compras** (decrementa `CantidadFacturada`), Contabilidad | `nc_id`, `proveedor_id`, `factura_origen_id?`, `tipo_relacion`, `monto` |
| `AnticipoProveedorCapturadoEvent` | Captura del CFDI de anticipo (serie FANT) | Compras (vincula a su solicitud) | `anticipo_id`, `proveedor_id`, `monto_entregado`, `oc_id?` |
| `MovimientoContableGeneradoEvent` | Cualquier movimiento que afecta contabilidad | **Contabilidad** (futuro) | `concepto_contable`, `cargo`, `abono`, `referencia_documento` |
| `DiferenciaPrecioFacturaDetectadaEvent` | Conciliación con OC dentro de tolerancia pero con diferencia relevante en variante B | **Almacén** (ajusta costo de inventario remanente o registra variación); **Compras** (informativo) | `recepcion_id`, `oc_id`, `factura_id`, `precio_oc`, `precio_factura`, `diferencia`, `por_linea[]` |
| `NotaCreditoFiscalDevolucionRecibidaEvent` | Captura de NC fiscal del proveedor (relación CFDI tipo 03) que cierra una devolución a proveedor previa (Almacén sub-flujo 8.B) | **Almacén** (marca devolución como `ConciliadaConNcFiscal`) | `proveedor_id`, `uuid_nc`, `devolucion_a_proveedor_id?`, `nota_cargo_id`, `monto` |

> **Nota sobre `FacturaProveedorRegistradaEvent` vs `FacturaProveedorAutorizadaEvent`:** Compras-OC suscribe el evento de **Registrada** (estado `Capturada`) y a partir de ahí actualiza `CantidadFacturada`. El de **Autorizada** es informativo. La razón es que Compras necesita conocer el monto facturado en cuanto se captura (para reportes de partidas abiertas), no esperar a que pase a `Autorizada`. Si la factura se rechaza por tolerancia (`FacturaProveedorRechazadaPorToleranciaEvent`) o se cancela (`FacturaProveedorCanceladaEvent`), Compras decrementa `CantidadFacturada`.

### 8.2 Eventos suscritos por CXP

| Evento | Origen | Acción |
|---|---|---|
| `OrdenCompraAutorizadaEvent` | Compras | Habilita la OC para conciliar facturas (proyección local opcional). |
| `OrdenCompraCanceladaEvent` | Compras | Si hay facturas en captura asociadas, se notifica al Auxiliar. |
| `PagoFacturaProveedorEvent` | Tesorería (futuro) | Actualiza `importe_pagado` y mueve estado a `Pagada` si saldo = 0. |
| `PagoFacturaProveedorRevertidoEvent` | Tesorería | Resta del `importe_pagado`; si quedó parcial, vuelve a `Autorizada`. |
| `ReppProveedorRecibidoEvent` | Tesorería | Informativo — actualiza flag `repp_recibido` y libera motivo "Falta complemento de pago" si aplica. |
| `CancelacionPasivoSolicitadaEvent` | Tesorería | Marca factura para revisión con motivo "Tesorería solicita cancelar". |
| `PeriodoContableCerradoEvent` | Finanzas (futuro) | Bloquea capturas con fecha del periodo cerrado. |
| `OcRecepcionRegistradaEvent` | **Almacén** | Habilita conciliación factura + recepción (three-way match). Si trae flag `factura_pendiente=true` (variante B), espera a que CxP capture la factura. Si recepción reporta merma → entrada automática a revisión con motivo "Daños o defectos". |
| `OcDevolucionRegistradaEvent` | **Almacén** (sub-flujo 8.B) | Dispara creación de `NotaCargo` borrador con proveedor, recepción origen, factura origen, líneas y evidencias. |
| `RecepcionRegistrada` | **Almacén** | Habilita conciliación factura + recepción (three-way match). Si trae flag `factura_pendiente=true` (variante B), espera a que CxP capture la factura. Si recepción reporta merma → entrada automática a revisión con motivo "Daños o defectos". |
| `SalidaPorDevolucionAProveedorRegistrada` | **Almacén** (sub-flujo 8.B) | Dispara creación de `NotaCargo` borrador con proveedor, recepción origen, factura origen, líneas y evidencias. |

---

## 9. Workers en-proceso (`IHostedService`)

ADR-0022. Todos viven en `Millet.Api` y siguen el patrón de `OutboxPublisherWorker` ya implementado en Compras.

| Worker | Schedule | Responsabilidad | NoOp condicional |
|---|---|---|---|
| `CfdiMailboxIngestionWorker` | Cada 5 min (configurable) | Poll del mailbox dedicado; descarga adjuntos (XML+PDF); pasa a `ICfdiIngestionPort` | Si la credencial del mailbox está vacía |
| `CfdiDescargaMasivaSatWorker` | Nocturno (2 AM) + intra-día configurable | Consulta a FiscalAPI todos los CFDIs nuevos para los RFCs receptores de Millet; ingresa via `ICfdiIngestionPort` | Si la cs de FiscalAPI está vacía |
| `CfdiEstadoSatRefreshWorker` | Cada 6h | Refresca el estado de CFDIs autorizados; si detecta cancelación en SAT, mueve factura a `Cancelada` | Si la cs de FiscalAPI está vacía |
| `OutboxPublisherWorker<CuentasPorPagarDbContext>` | Cada 5s | Publica eventos pendientes del outbox a Service Bus | Si la cs de Service Bus está vacía |
| `RevisionSlaNotificacionWorker` | Diario (8 AM) | Detecta facturas en revisión que cumplen el SLA por motivo (día 3, 5, 10 para SLA 5d; día 8, 12, 20 para disputas contractuales SLA 15d — A22); emite notificaciones vía `INotificacionService` | Si `INotificacionService` no está configurado |
| `NotaCreditoEnEsperaMatchWorker` (A19) | Diario (4 AM) | Intenta hacer match de NCs en estado `EnEspera` contra facturas nuevas del proveedor por UUID de relación CFDI. Si pasan 30 días sin match → notifica al Auxiliar. | Sin condición externa |

---

## 10. RBAC — permisos canónicos

Patrón ADR-0007. Permisos del módulo:

```
cuentas_por_pagar.facturas.read
cuentas_por_pagar.facturas.capturar
cuentas_por_pagar.facturas.editar
cuentas_por_pagar.facturas.cancelar
cuentas_por_pagar.facturas.enviar_revision
cuentas_por_pagar.facturas.liberar_revision        // por área
cuentas_por_pagar.facturas.autorizar               // override manual (raro)

cuentas_por_pagar.notas_credito.read
cuentas_por_pagar.notas_credito.capturar

cuentas_por_pagar.notas_cargo.read
cuentas_por_pagar.notas_cargo.crear
cuentas_por_pagar.notas_cargo.autorizar            // Dirección
cuentas_por_pagar.notas_cargo.aplicar

cuentas_por_pagar.anticipos.read
cuentas_por_pagar.anticipos.capturar

cuentas_por_pagar.comprobaciones.read
cuentas_por_pagar.comprobaciones.capturar          // empleado y auxiliar
cuentas_por_pagar.comprobaciones.aprobar_nivel1
cuentas_por_pagar.comprobaciones.aprobar_nivel2

cuentas_por_pagar.tc.read
cuentas_por_pagar.tc.registrar_movimiento
cuentas_por_pagar.tc.cerrar_estado_cuenta

cuentas_por_pagar.proveedores.poner_revision
cuentas_por_pagar.proveedores.liberar_revision
cuentas_por_pagar.proveedores.ajustar_tolerancia   // restringido

cuentas_por_pagar.reportes.cartera
cuentas_por_pagar.reportes.antiguedad
cuentas_por_pagar.reportes.diot
cuentas_por_pagar.reportes.tc

cuentas_por_pagar.cfdis.read
cuentas_por_pagar.cfdis.cargar_manual              // respaldo
cuentas_por_pagar.cfdis.descartar
```

Roles operativos (asignación inicial a discutir con el área):

- **AuxiliarCxp** — captura, edita, envía a revisión, aplica NC y anticipos, registra TC, captura comprobaciones de viáticos al regreso. NO autoriza.
- **JefeCxp** — todo lo de AuxiliarCxp + libera revisión de cualquier área + ajusta tolerancia + cancela facturas.
- **ResponsableArea** — libera revisión de facturas asignadas a su área (su `dependencia_revisora`).
- **ComercioExterior** — aprueba nivel 1 de aduanales.
- **JefeEmpleado** — aprueba nivel 1 de viáticos de sus subordinados.
- **DireccionFinanzas** — aprueba nivel 2 de aduanales, viáticos, TC; autoriza notas de cargo y anticipos.
- **DireccionGeneral** — todo lo de DireccionFinanzas + suplencia.
- **EmpleadoViajero** — captura su propia comprobación de viáticos.

---

## 11. Endpoints HTTP (resumen)

Versionado `/api/v1/cuentas-por-pagar/...` (ADR-0021). Idempotency-Key obligatorio en POST que crean recursos (ADR-0020). ETag/If-Match en mutaciones (ADR-0012). Problem Details (ADR-0010).

```
CFDIs
  GET    /cfdis                         — bandeja, paginada
  GET    /cfdis/{id}
  POST   /cfdis/cargar                  — carga manual (raro)
  POST   /cfdis/{id}/descartar
  POST   /cfdis/{id}/marcar-duplicado

Facturas
  GET    /facturas
  GET    /facturas/{id}
  POST   /facturas                      — capturar (con o sin OC en el body)
  PATCH  /facturas/{id}                 — editar pre-autorización
  POST   /facturas/{id}/enviar-revision
  POST   /facturas/{id}/liberar-revision
  POST   /facturas/{id}/cancelar
  POST   /facturas/{id}/aplicar-nc      — body: {nota_credito_id, monto}
  POST   /facturas/{id}/aplicar-anticipo
  GET    /facturas/{id}/evidencias
  POST   /facturas/{id}/evidencias

Notas de crédito
  GET    /notas-credito
  GET    /notas-credito/{id}
  POST   /notas-credito                 — capturar
  POST   /notas-credito/{id}/vincular-factura
  POST   /notas-credito/{id}/vincular-anticipo

Anticipos
  GET    /anticipos
  GET    /anticipos/{id}
  POST   /anticipos
  POST   /anticipos/{id}/cancelar

Notas de cargo
  GET    /notas-cargo
  GET    /notas-cargo/{id}
  POST   /notas-cargo
  POST   /notas-cargo/{id}/autorizar
  POST   /notas-cargo/{id}/aplicar
  POST   /notas-cargo/{id}/formalizar
  POST   /notas-cargo/{id}/cancelar

Comprobaciones (genérico para los 4 tipos)
  GET    /comprobaciones
  GET    /comprobaciones/{id}
  POST   /comprobaciones                — body: {tipo, ...}
  POST   /comprobaciones/{id}/lineas
  POST   /comprobaciones/{id}/enviar-revision
  POST   /comprobaciones/{id}/aprobar
  POST   /comprobaciones/{id}/aplicar

Viáticos
  POST   /viaticos/solicitudes
  POST   /viaticos/solicitudes/{id}/aprobar-n1
  POST   /viaticos/solicitudes/{id}/aprobar-n2
  POST   /viaticos/solicitudes/{id}/comprobar

Tarjetas de crédito
  GET    /tc/movimientos
  POST   /tc/movimientos
  GET    /tc/estados-cuenta
  POST   /tc/estados-cuenta
  POST   /tc/estados-cuenta/{id}/cerrar
  POST   /tc/estados-cuenta/{id}/generar-pasivo

Proveedores (vista CxP del master)
  POST   /proveedores/{id}/poner-revision
  POST   /proveedores/{id}/liberar-revision
  PATCH  /proveedores/{id}/tolerancia

Reportes
  GET    /reportes/antiguedad-saldos
  GET    /reportes/antiguedad-anticipos
  GET    /reportes/cartera
  GET    /reportes/diot
```

---

## 12. Frontend — patrones aplicables

Memoria `project_estructura_ventanas_erp` + [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md). CXP replica los patrones de Compras (memoria `feedback_reutilizacion_codigo`):

### 12.1 Master-detail por recurso

- `/cxp/cfdis` — bandeja tabular de CFDIs por procesar (lista compacta 320px + detalle a la derecha).
- `/cxp/cfdis/$id` — detalle del CFDI con preview del XML y PDF.
- `/cxp/facturas` — bandeja con filtros (estado, sucursal, proveedor, encargado, revisión).
- `/cxp/facturas/$id` — master-detail de la factura.
- `/cxp/notas-cargo`, `/cxp/anticipos`, `/cxp/comprobaciones`, `/cxp/tc` — mismo patrón.

### 12.2 Sheet (slide-from-right) para "Nueva..."

- "Nueva captura desde CFDI" — sheet con el CFDI precargado a la izquierda y los campos editables a la derecha; selector de OC, ConceptoContable por línea.
- "Nueva nota de cargo", "Nueva comprobación", "Nuevo movimiento TC" — todos con sheet provider a nivel shell.

### 12.3 Inline forms para items

Memoria `feedback_inline_no_modal_para_items` — **inline, no modal**:

- Líneas de factura → `LineaFacturaInlineForm` (estilo `LineaInlineForm` de Requisiciones).
- Líneas de comprobación → `LineaComprobacionInlineForm`.
- Adjuntos de nota de cargo → drag-and-drop inline.
- Evidencias de autorización → upload inline con preview.

### 12.4 Topbar

- Búsqueda contextual por ruta (debounce 200ms) — busca por UUID, folio del proveedor, número de OC, RFC.
- Quick Create popover con: "Capturar factura", "Nueva nota de cargo", "Nuevo movimiento TC", "Nueva comprobación".

### 12.5 Sub-topbar del detalle

Sticky, con acciones contextuales por estado: "Enviar a revisión", "Aplicar NC", "Aplicar anticipo", "Cancelar". `data-print="hidden"` para impresión limpia.

### 12.6 Bandejas asignadas

Cada `ResponsableArea` tiene una bandeja "Mis facturas en revisión" (filtro server-side por su `dependencia_revisora`). Patrón P2 de `patrones-compras.md`.

### 12.7 Shell de navegación

Memoria `project_nav_shell_pattern` — entra al sidebar + modal de cards. Sección "Cuentas por Pagar" con cards:

- CFDIs por capturar (con contador)
- Facturas
- Notas de crédito
- Notas de cargo
- Anticipos
- Comprobaciones de gastos
- Tarjetas de crédito
- Reportes

---

## 13. Dependencias de plataforma pendientes

> Sección obligatoria (ADR-0031). Ver §14 del [00-levantamiento](00-levantamiento.md#14-dependencias-de-plataforma-pendientes) para la tabla canónica. Resumen de los stubs / NoOps que este diseño introduce:

- `NoOpFiscalApiClient` — devuelve fixtures cuando no hay API key. `PLATFORM-TODO(<FiscalApi>)`.
- `NoOpMailboxIngestion` — worker noop cuando no hay credencial del mailbox. `PLATFORM-TODO(<MailboxIngestion>)`.
- `NoOpTesoreriaEventConsumer` — proyección de pagos vacía; CXP publica al outbox pero nadie consume. `PLATFORM-TODO(<TesoreriaEvents>)`.
- `NoOpContabilidadAsientoPort` — loggea sin persistir asientos. `PLATFORM-TODO(<ContabilidadAsientos>)`.
- `StubAlmacenEventConsumer` — `PLATFORM-TODO(<AlmacenEvents>)` — se cierra en el ciclo de Almacén.
- `StubDependenciaRevisoraReadPort` — devuelve seed local hasta que Administración exponga el catálogo compartido (A21). `PLATFORM-TODO(<DependenciasRevisorasEnAdmin>)`.
- ~~`StubPuestoReadPort`~~ — ✅ cerrado en ADM-PR2: `PuestoReadPortAdapter` + `EmpleadoReadPortAdapter` sobre `compartido.puestos`/`empleados` (doc 10 de Administración); el seed EJEC/GER/OPER migró a la tabla real con los mismos GUIDs (D6).

Cada `PLATFORM-TODO` busca-able con `rg "PLATFORM-TODO" backend/src/CuentasPorPagar`.

---

## 14. Riesgos técnicos

Ver §15 del [00-levantamiento](00-levantamiento.md#15-riesgos) para la tabla operativa. Riesgos específicos de diseño:

| Riesgo | Mitigación de diseño |
|---|---|
| **Acoplamiento implícito CXP ↔ Compras** vía tablas compartidas. | Solo puertos de lectura sobre `Compras.OC`; cero acceso directo a `compras.*` desde CXP. Comunicación vía eventos. |
| **Lock contention en `cfdis_recibidos` durante descarga masiva.** | Ingreso por lotes con `INSERT ... ON CONFLICT DO NOTHING` (dedupe en DB), no row-by-row. Worker con bulkhead. |
| **Reentrancia del worker `CfdiEstadoSatRefreshWorker`.** | Locks de aplicación (advisory locks de PostgreSQL); refresh por batches; idempotente. |
| **Crecimiento de `outbox_messages` si Service Bus se cae.** | TTL del outbox + alerta cuando hay más de 1000 mensajes pendientes. |
| **Desincronización CXP ↔ Almacén en devoluciones.** | Contrato bidireccional cerrado: `OcDevolucionRegistradaEvent` (Almacén → CxP) + `NotaCreditoFiscalDevolucionRecibidaEvent` (CxP → Almacén) correlacionados por `devolucion_a_proveedor_id`. Idempotencia explícita en ambos consumidores. Pruebas de integración cross-módulo. |
| **Cancelación de CFDI por el proveedor afecta REPP previo.** | Política: si una factura ya pagada es cancelada en SAT, se marca como `EnRevision` con motivo "Cancelación post-pago" para que Tesorería investigue. No se revierte automáticamente. |
| **Polimorfismo de `EvidenciaAutorizacion` complica queries.** | Vista materializada por tipo de documento para queries comunes; índice compuesto en `(tipo_documento, documento_id)`. |
| **Migración SAP — calidad de datos.** | Esquema de migración con tabla intermedia + validaciones; columna `requiere_completar` por proveedor; bloqueo de operación CxP en proveedores incompletos al go-live. |

---

## 15. Pendientes y próximos pasos

### 15.1 Antes del `02-plan-implementacion.md`

1. **Cerrar los 8 puntos del área** (§13.1 del levantamiento).
2. **Cerrar el contrato de eventos con Almacén** — siguiente ciclo de levantamiento (módulo Almacén no-prod). Hasta entonces, mantener stubs.
3. **Validar A1-A16** con el owner.
4. **Confirmar matriz de roles → permisos** con el área de CXP.
5. ~~**Anexo técnico de TC Empresarial**~~ ✅ Cerrado 2026-05-22 — ver [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md). Pendientes técnicos D9–D14 listados en §15.1 del anexo para validación con área y Contabilidad.

### 15.2 Diferidos a fase posterior (post-MVP)

- Portal propio de proveedores.
- Sync automatizado del estado de cuenta del banco (TC).
- Workflow engine genérico (si se confirma necesidad).
- CRUD de catálogos en UI (hoy seeds).
- Conciliación automática avanzada CFDI ↔ OC.

### 15.3 Coordinación con módulo Almacén — cerrada 2026-05-22

Los contratos cross-módulo con Almacén están cerrados (ver [`almacen/00-levantamiento.md`](../almacen/00-levantamiento.md) v0.1). Implementación pendiente:

- ✅ §6.1: `IAlmacenRecepcionReadPort` definido; adapter real cuando el módulo Almacén esté en runtime (stub `NoOpAlmacenRecepcionReadPort` hasta entonces).
- ✅ §8.1: CxP **publica** (entre otros) `FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`.
- ✅ §8.2: CxP **suscribe** `OcRecepcionRegistradaEvent` y `OcDevolucionRegistradaEvent`.
- ✅ §1.2 punto 5: flujo de NC tipo 03 con bloqueo hasta confirmar salida física vía sub-flujo 8.B de Almacén §8.4.
- ✅ §7.7 del levantamiento: flujo de devolución completo (cierre con NC fiscal del proveedor).
- §1.2 (Fuera): three-way match completo (OC + Recepción + Factura). v1 de CxP sigue validando solo OC + Factura al capturar; la recepción se consume como evento informativo. Bloqueo estricto por three-way match queda como vNext.

El esquema PostgreSQL de CXP **no requiere cambios** — los puntos de integración son eventos, no tablas. Esto es decisión de diseño consciente para que el módulo de Almacén pueda construirse después sin retrabajo en CXP.

---

## Rev.

- **2026-05-22 — v1.4** — Anexo [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) creado cerrando §13.3 punto 18 del levantamiento. Cubre el sub-módulo más complejo del alcance.
- **2026-05-22 — v1.3** — Cierre de los 8 puntos de §13.1 del levantamiento: asunciones A10 y A15 marcadas ✅ con decisión cerrada; agregadas A17–A22 (suplencias 3 niveles, política viáticos por puesto+destino, NC EnEspera, parser TC, dependencias en Admin, SLA único 5d); modelo §5 actualizado con 3 tablas nuevas (`politicas_viaticos`, `estado_cuenta_tc_lineas_banco`, `estado_cuenta_tc_archivos`); §6.1 añade `IDependenciaRevisoraReadPort`, `IPuestoReadPort`, `IEstadoCuentaTcParserPort`; §9 añade `NotaCreditoEnEsperaMatchWorker`; §13 actualiza stubs.
- **2026-05-22 — v1.2** — Alineación de naming de eventos cross-módulo con la convención canónica de Compras-OC (`{Agregado}{Verbo}Event`); §8.1 y §8.2 reescritas; agregada nota sobre `FacturaProveedorRegistradaEvent` vs `FacturaProveedorAutorizadaEvent`. Referencia a [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md) para reportería.
- **2026-05-22 — v1.1** — Cierre cross-módulo con Almacén: contratos de eventos canónicos (§8.1, §8.2), `IAlmacenRecepcionReadPort` con stub explícito (§6.1), §15.3 cerrada.
- **2026-05-22 — v1 (Draft)** — Propuesta de diseño inicial sobre el levantamiento v0.2. Asunciones A1-A16.
