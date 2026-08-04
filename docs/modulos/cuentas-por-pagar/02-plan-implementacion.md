# Plan de implementación — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Construido sobre:**
> - [00-levantamiento.md](00-levantamiento.md) (Rev. 0.4) y
> - [01-diseno.md](01-diseno.md) (Rev. 1.4),
> - [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) (Rev. 1).
>
> **Estado:** propuesta de plan para revisión con el equipo. Sizing en bandas — calibrar contra capacidad real.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Sizing en bandas:
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
  - **XL** > 1 mes
- Cada fase produce algo **deployable y validable**.
- Las fases son secuenciales por dependencia técnica; dentro de cada fase hay paralelismo posible (anotado como "‖").
- **Reuso primero (regla del proyecto):** cada fase indica qué hereda de Compras/Requisiciones/Administración antes de listar lo nuevo.

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 del módulo `Millet.CuentasPorPagar`, alineado con `01-diseno.md` Rev. 1.4 y el anexo TC Empresarial.

**Estrategia:**

1. **Walking skeleton primero**: ingestar un CFDI, capturar una factura simple con OC, verla en bandeja, autorizarla. Solo después agregar la riqueza (TC, viáticos, devoluciones).
2. **Reuso máximo** del módulo Compras (Outbox, BaseEntity, RBAC, FluentValidation, ProblemDetails, Idempotency, ETag, SoftLock, FolioSecuencia) y de Administración (Sucursales, Empleados, ConceptoContable, TipoCambio).
3. **Stubs cross-module** para Tesorería (no existe), Almacén (en construcción paralela), Contabilidad (no existe). Patrón ADR-0031.
4. **TC Empresarial como bloque dedicado** en Fase 7 — el sub-módulo más complejo se aborda después de que el ciclo básico esté sólido.
5. **Reportes con motor nativo** (ADR-0036) en Fase 8 — al final, cuando los datos del módulo ya fluyen.
6. **Coordinación con Almacén** crítica en Fases 4 y 5 — los eventos cruzados se cablean con stubs en CxP hasta que Almacén exista.

**Pendientes que NO bloquean arranque:**

- TC Empresarial (Fase 7).
- Eventos reales con Almacén (Fase 5 wireup; stubs antes).
- Servicio PDF, Notificaciones, Contabilidad — stubs hasta Fase 9.

---

## 2. Prerrequisitos — audit del repo (2026-05-22)

Auditado contra `c:\Users\UserSP\Desktop\Project_Millet_ERP\backend\`.

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| Proyecto `Millet.CuentasPorPagar` | ❌ no existe | F0-PR1 crea el csproj + folders. |
| `CuentasPorPagarDbContext` + schema `cuentas_por_pagar` | ❌ no existe | F0-PR2 lo crea + registra en `MigrationsHealthCheckOptions.ContextTypes` y en `deploy-app-dev.yml` (memoria `feedback_dbcontext_nuevo_checklist`). |
| `BaseEntity` con `Version IsConcurrencyToken` (ADR-0012 Capa 1) | ✅ existe | Compartido en SharedKernel. CxP hereda. |
| `BaseDbContext` con interceptors | ✅ existe | Auditoría, multi-empresa, idempotencia gratis. |
| EF Core + migraciones por esquema | ✅ existe | Patrón establecido. |
| Identidad + RBAC granular + Entra ID | ✅ existe | Permisos `cuentas_por_pagar.*` se agregan a `PermisosCanonicos.Todos` (F0-PR3). |
| `RequirePermissionAttribute` + handler | ✅ existe | Reusable. |
| Problem Details (ADR-0010) | ✅ existe | Estándar. |
| `Money`, `Moneda` VOs | ✅ existe | Reusable. |
| Soft delete vía `IFiscalmenteRelevante` | ✅ existe | Reusable. |
| Health checks + `MigrationsAppliedHealthCheck` | ✅ existe | Cubre las migraciones de CxP automáticamente. |
| Serilog + masking | ✅ existe | Logging estructurado gratis. |
| `IClock`, `ICurrentUserContext`, `ICurrentEmpresaContext` | ✅ existe | Inyectables. |
| Patrón `Outbox` (ADR-0009) | ⏳ existe parcial | `OutboxSaveChangesInterceptor` ya en Compras; CxP lo reusará vía `OutboxPublisherWorker<CuentasPorPagarDbContext>` (F0-PR2). |
| Patrón `Folio` + secuencia atómica | ✅ existe | CxP crea `cuentas_por_pagar.folio_secuencias_nota_cargo` siguiendo el patrón. |
| `CollaborationHub` SignalR (ADR-0012 Capa 2) | ✅ existe | CxP declara entidades con awareness colaborativo. |
| `IProveedorReadPort` / `IArticuloReadPort` (DatosMaestros) | ⏳ a confirmar | Si no existen, CxP los define en F0-PR4 con stubs y los pasa al equipo de Administración para implementación real. |
| Almacén — eventos | ⏳ paralelo | Almacén se construye en paralelo. Stubs `NoOpAlmacenEventConsumer` hasta runtime real. |
| FiscalAPI client (ADR-0027) | ⏳ pendiente | F2-PR1 crea cliente con stub fixture; cs y credenciales se cablean cuando Operations las entrega. |
| OpenAPI + tipos TS | ✅ existe | CxP se agrega al pipeline automáticamente. |

---

## 3. Estrategia de paralelización con Almacén

CxP y Almacén comparten 5 eventos críticos (§11.6 del 00-levantamiento de CxP). Coordinación:

- **CxP define los contratos** primero (interface + record de payload + tests de serialización).
- Ambos módulos publican stubs `NoOpEventConsumer` para los eventos del otro.
- En **F5 de CxP** y **F2 de Almacén** se cablea el primer evento real (`OcRecepcionRegistradaEvent`).
- En **F6 de CxP** y **F6 de Almacén** se cierra el ciclo bidireccional de devoluciones (8.B).
- Sesión semanal de 30 min con el equipo de Almacén durante F4-F6.

---

## 4. Fases

### Fase 0 — Foundation del módulo (S)

Crea el módulo desde cero. Sin features, solo andamiaje.

- F0-PR1: csproj `Millet.CuentasPorPagar` + folders (`Domain/`, `Application/`, `Infrastructure/`). Smoke endpoint `GET /api/v1/cuentas-por-pagar/smoke` con `[RequirePermission("cuentas_por_pagar.smoke")]`.
- F0-PR2: `CuentasPorPagarDbContext` + schema `cuentas_por_pagar` + migración vacía. Registro en `MigrationsHealthCheckOptions.ContextTypes` y `deploy-app-dev.yml`.
- F0-PR3: Permisos canónicos `cuentas_por_pagar.*` (~25 permisos del §10 del 01-diseno).
- F0-PR4: Puertos de lectura cross-module (`IProveedorReadPort`, `IArticuloReadPort`, `ISucursalReadPort`, `IEmpleadoReadPort`, `IConceptoContableReadPort`, `ITipoCambioReadPort`, `IDependenciaRevisoraReadPort`, `IPuestoReadPort`) con stubs `NoOp*` o adapters reales si Administración ya los expone.

**Paralelización:** F0-PR1 → F0-PR2 → (F0-PR3 ‖ F0-PR4) secuencial.

### Fase 1 — `CfdiRecibido` + ingestión manual (M)

El primer agregado, sin lógica de pasivo todavía. Ingreso manual (sube XML, parsea, valida).

- F1-PR1: Agregado `CfdiRecibido` + tabla + estados. Parser CFDI 4.0 (`XmlCfdiParser`). Tests con corpus de XMLs reales.
- F1-PR2: Comando `IngresarCfdiManualCommand` + endpoint `POST /cfdis/cargar` (multipart). Validación contra catálogos SAT, dedupe por UUID.
- F1-PR3: Bandeja `CfdisPorCapturarQuery` + endpoint `GET /cfdis` (paginado, filtrado).
- F1-PR4: Comandos `MarcarCfdiDuplicadoCommand`, `DescartarCfdiCommand`.

**Paralelización:** F1-PR1 → F1-PR2 → (F1-PR3 ‖ F1-PR4).

### Fase 2 — Ingestión automática (descarga SAT + mailbox) (M)

Conecta FiscalAPI (ADR-0027) y el mailbox dedicado.

- F2-PR1: Cliente FiscalAPI tipado (`IFiscalApiClient`) con stub fixture. Adapter HTTP + Polly.
- F2-PR2: Worker `CfdiDescargaMasivaSatWorker` (`IHostedService`).
- F2-PR3: Worker `CfdiMailboxIngestionWorker` (IMAP o Microsoft Graph).
- F2-PR4: Worker `CfdiEstadoSatRefreshWorker` (refresca estado de CFDIs autorizados; detecta cancelaciones).

**Paralelización:** F2-PR1 obligatorio primero. F2-PR2/PR3/PR4 paralelos después.

### Fase 3 — `FacturaProveedor` con OC (M)

El flujo central. Captura con OC, conciliación, autorización implícita.

- F3-PR1: Agregado `FacturaProveedor` + `LineaFacturaProveedor` + tabla + estados (5).
- F3-PR2: Comando `CapturarFacturaConOcCommand`. Match con OC + tolerancia del proveedor. Heredar `Encargado` + `Subcategoría` (snapshot).
- F3-PR3: Endpoints `GET/POST/PATCH /facturas`. Bandeja `FacturasAutorizadasPorPagarQuery`.
- F3-PR4: Eventos publicados: `FacturaProveedorRegistradaEvent`, `FacturaProveedorAutorizadaEvent`, `FacturaProveedorRechazadaPorToleranciaEvent`, `FacturaProveedorCanceladaEvent`. Outbox + `OutboxPublisherWorker<CuentasPorPagarDbContext>`.

**Paralelización:** secuencial.

### Fase 4 — Workflow de revisión + autorización informal (M)

Agrega flag `en_revision`, SLA, evidencia de autorización informal.

- F4-PR1: Seed `motivos_revision` + tabla `dependencias_revisoras` (consume de Administración cuando exista, seed local hasta entonces — A21).
- F4-PR2: Flag `en_revision` en `FacturaProveedor` + lógica de disparo automático.
- F4-PR3: Comandos `EnviarFacturaARevisionCommand`, `LiberarRevisionFacturaCommand`. Bandeja `FacturasEnRevisionPorAreaQuery`.
- F4-PR4: Agregado `EvidenciaAutorizacion` polimórfica. Upload de adjuntos (Azure Blob, ADR-0024).
- F4-PR5: Worker `RevisionSlaNotificacionWorker` (5d + 15d para disputas — A22). Stub `INotificacionService`.

**Paralelización:** F4-PR1 → F4-PR2 → (F4-PR3 ‖ F4-PR4 ‖ F4-PR5).

### Fase 5 — Integración con Compras y Almacén (M)

Wireup real de eventos cross-módulo.

- F5-PR1: Adapter real `IComprasOcReadPort` (lee `compras.ordenes_compra`).
- F5-PR2: Suscripción a eventos de Compras: `OrdenCompraAutorizadaEvent`, `OrdenCompraCanceladaEvent`. Proyección local opcional.
- F5-PR3: Suscripción a `OcRecepcionRegistradaEvent` (Almacén). Si trae merma → factura entra a revisión.
- F5-PR4: Publicación de `FacturaProveedorRegistradaEvent` consumido por Compras y Almacén (variante B).

**Paralelización:** F5-PR1 → F5-PR2 ‖ F5-PR3 ‖ F5-PR4.

### Fase 6 — Notas de crédito, anticipos, notas de cargo (M)

- F6-PR1: Agregado `NotaCreditoProveedor` + comando `CapturarNotaCreditoCommand`. Vinculación a factura origen (tipo 01) o anticipo (tipo 07). Manejo de NC pre-factura `EnEspera` (A19).
- F6-PR2: Worker `NotaCreditoEnEsperaMatchWorker` (A19).
- F6-PR3: Agregado `AnticipoProveedor` + comandos. Serie FANT. Aplicaciones a facturas finales.
- F6-PR4: Agregado `NotaCargo` + comandos. Folio interno `NCG-{año}-{secuencial:6}` (A3). Autorización Dirección.
- F6-PR5: Suscripción a `OcDevolucionRegistradaEvent` (Almacén) → genera `NotaCargo` borrador. Publicación de `NotaCreditoFiscalDevolucionRecibidaEvent` cuando llega NC fiscal tipo 03.

**Paralelización:** F6-PR1 → F6-PR2; F6-PR3 ‖ F6-PR4; F6-PR5 al final.

### Fase 7 — Comprobaciones de gastos (L)

Caja Chica + Viáticos + TC Empresarial. **Sub-módulo más complejo del alcance.**

- F7-PR1: Agregado `ComprobacionGastos` + `LineaComprobacion` + estados.
- F7-PR2: Variante **Caja Chica** — captura con N CFDIs/tickets, autorización por sucursal con catálogo `aprobadores_limites` (A10).
- F7-PR3: Catálogo `politicas_viaticos` + `aprobadores_limites` con seed (A18). Pantalla de solicitud de anticipo de viáticos con validación contra política.
- F7-PR4: Variante **Viáticos** — flujo electrónico de autoservicio. Préstamo al empleado, comprobación al regresar.
- F7-PR5: Aduanales — flujo con doble autorización (Comercio Exterior + Dirección).
- F7-PR6 a F7-PR12: **TC Empresarial** según [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) §13. Subdivisiones:
  - F7-PR6: Master `Tarjeta` + `tarjeta_usuarios_autorizados`.
  - F7-PR7: Agregado `MovimientoTarjetaCredito` (con/sin CFDI). Flujos A y B (§5.1, §5.2 del anexo).
  - F7-PR8: Agregado `EstadoCuentaTC` + `estado_cuenta_tc_lineas_banco`.
  - F7-PR9: Parser configurable por perfil (`PerfilParserBanco`). Perfil `AMEX_MX` seed (§6.4 del anexo).
  - F7-PR10: Algoritmo de match automático con `pg_trgm` (§7 del anexo). Score 0-100.
  - F7-PR11: Cierre del estado de cuenta + generación de `FacturaProveedor` agregada contra el banco (§5.4 del anexo).
  - F7-PR12: Refunds + intereses + comisiones + disputas (§8 del anexo).

**Paralelización:** F7-PR1 obligatorio primero. F7-PR2 ‖ F7-PR3 ‖ F7-PR4 después. F7-PR5 independiente. F7-PR6 → F7-PR7 → F7-PR8 → F7-PR9 → F7-PR10 → F7-PR11 → F7-PR12 (TC bloque secuencial por dependencias internas).

### Fase 8 — Reportes (M)

Motor nativo (ADR-0036).

- F8-PR1: `<ReporteShell>` compartido (si no existe ya en frontend). Endpoint base con shape JSON estandarizado.
- F8-PR2: `AntiguedadSaldosProveedoresQuery` + endpoint + componente React + variante PDF + Excel.
- F8-PR3: `AntiguedadAnticiposProveedoresQuery`.
- F8-PR4: `CarteraPorCategoriaRevisionQuery`.
- F8-PR5: `PasivosObrasQuery` (expuesto a módulo Obras).
- F8-PR6: Reporte `MovimientosTcPendientesConciliarQuery` + `EstadosCuentaTcConsolidadoQuery` (TC).

**Paralelización:** F8-PR1 obligatorio. Después todos paralelos.

### Fase 9 — Tesorería stubs → wireup real (S)

Cuando Tesorería empiece a existir.

- F9-PR1: Publicación `PasivoAutorizadoParaPagoEvent` (ya integrado en F3-PR4; este PR lo conecta a Service Bus real).
- F9-PR2: Suscripción a `PagoFacturaProveedorEvent` y `PagoFacturaProveedorRevertidoEvent`.
- F9-PR3: Suscripción a `ReppProveedorRecibidoEvent` y `CancelacionPasivoSolicitadaEvent`.

**Paralelización:** independiente de las demás fases una vez Tesorería exista.

### Fase 10 — Limpieza y hardening (S)

- F10-PR1: Tests E2E del happy path completo (CFDI llega → factura capturada → autorizada → pagada).
- F10-PR2: Performance tests con datos sintéticos (500 facturas/mes, 30 TC/mes).
- F10-PR3: Auditoría de PLATFORM-TODO y cierre de stubs reemplazables.
- F10-PR4: Documentación operativa (`08-operacion-y-runbook.md`).
- F10-PR5: Checklist de go-live (`09-go-live-checklist.md`).

---

## 5. Cronograma sugerido (con un equipo de 2 devs backend + 1 dev frontend)

| Mes | Fases |
|---|---|
| 1 | F0 + F1 + F2 |
| 2 | F3 + F4 |
| 3 | F5 + F6 |
| 4 | F7-PR1 a F7-PR5 (Caja Chica + Viáticos + Aduanales) |
| 5 | F7-PR6 a F7-PR12 (TC Empresarial completo) |
| 6 | F8 + F9 + F10 |

Total ~6 meses calendario con el equipo descrito. Con un equipo más grande, F7 paralelizable (PR2/PR3/PR4 vs PR6-PR12 con dev dedicado a TC).

---

## 6. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| **Catálogos pendientes del área** (aprobadores, políticas de viáticos, tarjetas) bloquean F7 al go-live | Empezar a recopilarlos desde F3 con un Excel maestro. F7 puede arrancar con seed de prueba; el seed real va antes del go-live. |
| **FiscalAPI no listo cuando F2 arranca** | Stub fixture cubre dev/test. Conectar credenciales al final de F2. |
| **Almacén atrasado respecto a CxP** | F5-PR3 y F6-PR5 pueden quedar en stub hasta que Almacén llegue. No bloquea release del módulo. |
| **TC Empresarial sin archivo de muestra del banco** | Pedir un export anonimizado del portal Amex al inicio de F7-PR9. Sin archivo, el parser se diseña a ciegas y rompe en producción. |
| **Volumen real 10x estimado** | A1 del 01-diseno cubre rollback (particionamiento). Monitorear desde F3 con métricas de bandeja. |

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Plan de 10 fases sobre el 01-diseno v1.4 + anexo TC. Cronograma 6 meses con equipo 2BE+1FE.
