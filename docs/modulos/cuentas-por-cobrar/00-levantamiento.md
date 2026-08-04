# Módulo Cuentas por Cobrar (CxC) — Mapa funcional

> **Versión:** v0.2 (v0.1 del levantamiento + validación contra código 2026-07-13, ver §9)
> **Fecha:** 2026-07-13
> **Autoridad funcional:** Néstor Manuel Prida Rodarte (Gerente de Crédito y Cobranza)
> **Fuentes de levantamiento:** `CXC_G.docx` (Gerencia, cartera nacional+internacional) · `CXC_NACIONALES.docx` (Coordinación nacional)
> **Contexto de acoplamiento:** `cxc_contexto_facturacion.md` (digest de decisiones de Facturación)
> **Estado:** borrador para confirmación por owners. Las 7 decisiones estructurales fueron aprobadas por Eduardo (2026-07-13); los `Pendiente` restantes requieren confirmación de Prida / fiscal / equipo A+W.

**Convenciones del módulo (alineadas al repo):**

| Aspecto | Valor | Precedente |
|---|---|---|
| Proyecto | `Millet.CuentasPorCobrar` | `Millet.CuentasPorPagar` |
| Esquema Postgres | `cuentas_por_cobrar` | `cuentas_por_pagar` (schema-per-module, ADR-0030) |
| Topic Service Bus | `cuentas-por-cobrar-events` | `cuentas-por-pagar-events` |
| Prefijo de permisos | `cuentas_por_cobrar.{recurso}.{accion}` | `cuentas_por_pagar.facturas.leer` |
| Namespace GUID permisos | `0000000a-*` (siguiente libre) | 07=CxP, 08=Almacén, 09=Facturación |
| Prefijo de ramas | `cxc/*` (backend) · `cxc-fe/*` (frontend) | auto-mode N2 por prefijo |

"CXC" se usa como alias informal en documentos; en código, esquema, permisos y
topics se usa siempre el nombre completo, igual que CxP.

---

## 0. Encuadre y alcance

CxC es **un solo módulo**, con **moneda/nacionalidad como dimensión de alcance (scoping), no como módulos separados** [Decisión CXC-7]. El ciclo de fondo es idéntico en cartera nacional e internacional: `línea de crédito → cartera/antigüedad → cobranza → decisión de liberación → aplicación de pago → estado de cuenta`. Lo internacional es una capa encima (SOLUNION, transferencias USD/SWIFT, plazos 45/60/90). Prida es la autoridad funcional que reconcilia el solape entre los dos documentos de levantamiento.

**Principio de frontera con Facturación:** Facturación posee la *emisión* de CFDI (ingreso, anticipos `FANT`, egreso/NC, pago/REPP). CxC posee el *ciclo del cobrable*: líneas de crédito, cartera/antigüedad, seguimiento de cobranza, decisión de liberación, **propuesta** de aplicación de pago, y estados de cuenta. CxC **dispara y consume** eventos fiscales; **no emite** CFDI.

### Lo que CxC posee

| Capacidad | Naturaleza |
|---|---|
| Línea de crédito por cliente (límite, moneda, origen, plazo) | Master data propia de CxC (seed inicial) — [Decisión CXC-3] |
| Cálculo de crédito disponible | Read model derivado (`límite − facturado − liberado sin factura`) |
| Decisión de liberación de pedido | Agregado con reglas de crédito + reglas por serie + override consumible |
| Seguimiento de cobranza auditable | Historial por cliente (fecha, usuario, canal, resultado) |
| Propuesta de aplicación de pago | Matching depósito↔facturas desde remittance → a Ingresos |
| Cartera / antigüedad de saldos | Reporte nativo (ADR-0036: JSON + React + `<ReporteShell>`) |
| Estado de cuenta del cliente | Reporte nativo (ADR-0036) |
| Alertas de cartera | 90 días SOLUNION · exceso de crédito · auto-bloqueo por vencimientos |
| Saldo por cobrar neto de NC (13-K) | Definición formalizada en CxC — [Decisión CXC-5] |

### Lo que CxC consume (de Facturación, topic `facturacion-events`)

Eventos reales de `Facturacion.Application.Integration.FacturacionIntegrationEvents`
(sufijo `IntegrationEvent`; naming de tipo `facturacion.{recurso}.{accion}.v1`).
El header del archivo ya declara a CxC como consumidor previsto.

| Evento (clase real) | Tipo | Uso en CxC |
|---|---|---|
| `FacturaVentaTimbradaIntegrationEvent` | `facturacion.factura-venta.timbrada.v1` | **Alta en cartera** — base del `facturado` para crédito disponible y antigüedad |
| `ReciboPagoTimbradoIntegrationEvent` | `facturacion.recibo-pago.timbrado.v1` | Actualiza cartera al confirmarse cobro bancario (REPP) |
| `CobroMostradorRegistradoIntegrationEvent` | `facturacion.cobro-mostrador.registrado.v1` | Actualiza cartera al cobro en mostrador |
| `CobroMostradorCanceladoIntegrationEvent` | `facturacion.cobro-mostrador.cancelado.v1` | Reversa el cobro en cartera |
| `NotaCreditoTimbradaIntegrationEvent` | `facturacion.nota-credito.timbrada.v1` | Ajusta saldo neto (13-K); trae `Motivo`, `FacturaRelacionadaId`, `AnticipoOrigenId` |
| `ComprobanteCanceladoIntegrationEvent` | `facturacion.comprobante.cancelado.v1` | **Baja/reversa en cartera** — sin esto la cartera queda inflada tras cancelaciones |
| `FacturaAnticipoTimbradaIntegrationEvent` | `facturacion.factura-anticipo.timbrada.v1` | Estado de cuenta (los anticipos no son cartera, pero sí movimiento del cliente) |

Además del stream de eventos, CxC consume el **contrato de lectura de anticipos**:
`AnticipoSaldoDetalle` (`AnticipoId`, `ClienteId`, `Estado`, `MontoCobrado`,
`MontoAmortizado`, `Saldo`, `SaldoDisponible`, `PedidoOrigenRef`, `ObraId/Nombre`,
`Vinculaciones[]` con factura + NC de amortización). **Ojo:** hoy ese record vive
en `Facturacion.Application` (`FacturasAnticipoQueries.cs`), es contrato *interno*
del módulo Facturación; para consumirlo cross-módulo hay que promoverlo a puerto
público (`I*ReadPort` implementado por Facturación), igual que
`IComprasOcReadPort`/`IAlmacenRecepcionReadPort` en CxP. Ver PR-6.

### Lo que CxC emite (topic `cuentas-por-cobrar-events`, Outbox ADR-0009)

Naming canónico `{Agregado}{Verbo}Event` (convención de la triada) con tipo
`cuentas_por_cobrar.{recurso}.{accion}.v1`:

| Evento / salida | Destino |
|---|---|
| `PropuestaAplicacionPagoCreadaEvent` | Ingresos (quien confirma y emite `PagoClienteConfirmadoEvent`) — [Decisión CXC-1] |
| `DecisionLiberacionEmitidaEvent` | Write-back worker → tabla-puente `MILLET_INTEGRACION` (PR dependiente) — [Decisión CXC-2] |
| Instrucción de aplicación de anticipo | Caja ejecuta (ya decidido en Facturación `D15`) |
| `AlertaCarteraGeneradaEvent` | Notificaciones |

---

## 1. Procesos del módulo (TO-BE)

### 1.1 Liberación de pedidos

**AS-IS:** el Coordinador/Encargado salta entre A+W (estatus 47→69→70), SAP (línea de crédito) y correo; suma a mano el crédito disponible; desbloquea en A+W.

**TO-BE:** el ERP calcula el crédito disponible, evalúa las reglas y **persiste la decisión de liberación** con auditoría. El efecto sobre A+W (que A+W respete la liberación) se resuelve por write-back a tabla-puente en un PR dependiente y desacoplado [Decisión CXC-2].

Reglas (de ambos levantamientos):
- Un pedido se libera solo si: el cliente tiene crédito activo (SOLUNION o interno), no excede su línea, y pagó o tiene promesa de pago válida.
- **Reglas por serie de folio** (catálogo `regla_liberacion_serie`): folios que inician en `5000`/`7000` **siempre** liberan; folios `3000`/`4000`/`8000` **nunca** liberan.
- Clientes bloqueados por falta de pago no se liberan.
- Clientes sin línea de crédito: solo con override de gerente (autorización consumible).
- Exceder el límite: solo con override de gerente.

> **Pendiente — sugerencia:** el catálogo de reglas por serie proviene del documento internacional (§8.3). Confirmar con Prida si la cartera **nacional** comparte exactamente esas series (`3000/4000/5000/7000/8000`) o maneja otras. Propuesta: modelarlo como catálogo versionado (seed) `regla_liberacion_serie(prefijo, comportamiento)` para que ambas carteras compartan la tabla y difieran solo en filas.

### 1.2 Cartera y cobranza

**AS-IS:** la cartera se arma a mano cada semana en Excel (descarga de SAP, `BUSCARV` para clasificar A/B/C/E, formato, ordenamiento) — 45–90 min por corte, susceptible a error humano. Los comentarios de seguimiento viven en un Excel de red sin historial auditable.

**TO-BE:**
- **Antigüedad de saldos** generada nativamente con el motor de reportes del ERP (**ADR-0036, ya cerrado**: backend JSON estructurado + componentes React + `<ReporteShell>` + export PDF/Excel client-side; mismo patrón que los reportes de CxP/Almacén). Buckets de días **configurables** (nacional observado: `0-15/16-30/31-60/61-90/91+`; internacional/SAP observado: `15/30/60/90` y `ago-14/15-21/22-31/32+`). Clasificación de cliente por tipo de crédito (A/B/C/E) como atributo, no como fórmula manual.
- **Seguimiento de cobranza auditable** dentro del sistema: por cliente, con fecha, usuario, canal (llamada/correo/WhatsApp), resultado (promesa de pago, monto, fecha comprometida) e historial completo.

### 1.3 Aplicación de pagos

**AS-IS:** Ingresos notifica depósitos 2×/día (9:15 y 16:00); el Encargado identifica cliente y facturas (con remittance), llena tabla de confirmación y la envía a Ingresos + Caja General (CC gerente) con asunto `CONFIRMAR Y APLICAR $[Monto] + [Cliente]`; recibe PR de aplicación.

**TO-BE** [Decisión CXC-1]:
1. CxC recibe la notificación de depósito y **propone el matching** depósito↔facturas desde el remittance (`propuesta_aplicacion_pago`).
2. **Ingresos confirma** y emite `PagoClienteConfirmadoEvent`.
3. Facturación timbra el **REPP** (`ReciboPago`).
4. CxC **consume** `ReciboPagoTimbradoIntegrationEvent` y actualiza cartera y saldo neto.

Se preserva la separación de funciones del AS-IS (Cartera propone, Ingresos aplica). CxC **no emite el REPP**.

- **Diferencia de comisión bancaria (< $50 USD):** es **tolerancia de conciliación de cartera, no descuento comercial**; **no genera CFDI** [Decisión CXC-4]. Se registra como ajuste no fiscal en la propuesta/aplicación.
- **Pago parcial (abono):** se registra parcialidad con comentario, reflejada en el saldo neto.
- **Pago sin remittance:** no se propone aplicación hasta que el cliente envíe el detalle de facturas.

> **Pendiente — sugerencia:** confirmar con fiscal que la tolerancia < $50 USD por comisión bancaria ajena **no** requiere CFDI de Egreso. Reservar el egreso (bonificación relación 01, subtipo `PRONTO_PAGO`) solo para descuentos financieros genuinamente pactados con el cliente.

> **Pendiente — confirmado en código:** `PagoClienteConfirmadoEvent` sigue siendo stub: `PLATFORM-TODO(<PagoClienteConfirmado>)` en `Api/Endpoints/Facturacion/ReppEndpoints.cs` — hoy el REPP se emite por endpoint manual y `EmitirReppCommand` ya está listo para que un listener lo invoque cuando Tesorería/Ingresos publique el evento. El topic `tesoreria-events` **ya existe** (CxP lo consume con `TesoreriaEventListenerWorker` para `PagoFacturaProveedorEvent`); el emisor natural de `PagoClienteConfirmadoEvent` es ese mismo topic. Falta confirmar quién posee "Ingresos/Tesorería" como emisor. La recomendación aprobada es mantener a Ingresos como confirmador (no que CxC se auto-confirme).

### 1.4 Estados de cuenta y saldos de anticipo

**AS-IS:** el Coordinador consulta SAP (estado de cuenta), complementa con A+W, integra en Excel. Los saldos de anticipo se ven en un "mapa" de A+W visualmente caótico; solo Ingresos puede depurar saldos.

**TO-BE:**
- **Estado de cuenta** generado nativamente (ADR-0036); debe reflejar **todos los movimientos aplicados** (pagos, NC) antes de emitirse (regla: no se envía con saldos desactualizados).
- **Saldo de anticipo por cliente:** CxC **consume** `AnticipoSaldoDetalle` (vía puerto público a promover, ver §0 y PR-6) y lo presenta como **lista filtrable** (fecha, folio, total), reemplazando el mapa de A+W. CxC **no re-modela** anticipos.
- **Saldo por cobrar neto de NC (13-K):** `saldo_neto = facturas_emitidas − pagos_aplicados − NC` — definición **propia de CxC** [Decisión CXC-5].

**Variante — Estados de cuenta manuales de Obras:** clientes de Obras requieren estado de cuenta que combina facturado (histórico) + material en producción (A+W). Se marca como caso especial; el material en producción depende del gap G1 (definición de "en firme").

### 1.5 Gestión de líneas de crédito (SOLUNION — internacional)

**AS-IS:** solicitud/actualización de líneas en portal SOLUNION + SAP + A+W (triple captura manual); reporte mensual de ventas SOLUNION; reporte de impago **antes de 90 días** de vencimiento so pena de perder la cobertura.

**TO-BE** [Decisión CXC-6]:
- La **línea de crédito es entidad propia de CxC** con `origen ∈ {SOLUNION, interno}`, lo que elimina la triple captura como *fuente*. La integración automatizada con el portal SOLUNION queda **fuera del MVP**.
- **Alerta de 90 días** (factura acercándose al vencimiento de cobertura SOLUNION) — sí entra al MVP; es el riesgo #1 de Prida y es barata.
- Reporte mensual de ventas SOLUNION y gestión de límites en portal: **siguen manuales** por ahora.

---

## 2. Reglas de negocio críticas

### 2.1 Validaciones obligatorias
- No liberar clientes bloqueados por falta de pago.
- Clientes sin crédito o sobre límite: solo con override de gerente (autorización consumible).
- Estado de cuenta debe reflejar todos los movimientos aplicados antes de emitirse.
- Un pago no se propone/aplica sin remittance o confirmación directa del cliente.
- NC requiere firmas autorizadas antes de aplicarse (regla de Facturación; CxC solo consume el timbrado).

### 2.2 Cálculos automáticos
- **Crédito disponible** = `límite − facturado − liberado sin factura`.
- **Saldo vencido** = suma de los buckets de antigüedad.
- **Saldo excedido** = `saldo total − límite` (indicador en rojo si > 0).
- **Diferencia de pago** = `depósito − Σ facturas`; si negativa y < $50 USD → tolerancia no fiscal.
- **Saldo neto (13-K)** = `facturado − pagos aplicados − NC`.

### 2.3 Restricciones operativas
- Folios `3000/4000/8000` no se liberan bajo ninguna circunstancia.
- En la línea de crédito, límite y comprometido deben ser consistentes (regla heredada de SAP; en el modelo nuevo el "comprometido" se deriva del cálculo de crédito disponible).
- Multi-moneda de primera clase: cada saldo y línea se expresa en su moneda (USD internacional, MXN nacional). No se fuerza conversión para operar.

### 2.4 Autorización (reuso de patrones existentes)
- **Override de crédito** (liberar bloqueo / aplicar sobre límite): patrón de **autorización consumible** — calco de `AutorizacionAperturaCaja` (`Facturacion/Domain/Cajas`, `[Decisión 12-1]`), que a su vez es calco de `AutorizacionVentaActivo`. Rasgos a preservar del patrón: estados `Autorizada/Usada/Cancelada`, beneficiario único, supervisor auditado, motivo obligatorio (≤254), vigencia corta configurable (máx. 24 h), un solo uso, prohibido el autoconsumo (supervisor ≠ beneficiario).
- **Umbrales por monto** (si se requieren para "liberación especial"): patrón de **matriz multidimensional** de Requisiciones (`umbrales_aprobacion_departamento` + `aprobadores_departamento`).

> **Pendiente — sugerencia:** no existe abstracción común de autorización (OC 2-niveles, matriz de Requisiciones, y consumible de Caja/Activos son mecanismos ad-hoc). Con CxC la réplica del consumible sería la **tercera** (`AutorizacionVentaActivo` → `AutorizacionAperturaCaja` → CxC). Para el MVP se **replica** el patrón en el esquema `cuentas_por_cobrar`; recomiendo abrir un **ADR transversal** que unifique matriz multinivel + autorización consumible antes de que un cuarto módulo lo replique.

---

## 3. Modelo de datos (esquema `cuentas_por_cobrar`, PostgreSQL)

Arquitectura: modular monolith, hexagonal + CQRS con MediatR, schema-per-module
(`cuentas_por_cobrar`, ADR-0030), EF Core con `CuentasPorCobrarDbContext`,
Outbox → Service Bus (topic `cuentas-por-cobrar-events`).

> **Nota de implementación (incidente 2026-07-11):** todo enum persistido lleva
> `HasCheckConstraint` + migration, y su mirror en el frontend; al agregar un
> valor nuevo se actualizan los tres en el mismo PR. Aplica a `estado`,
> `resultado`, `comportamiento`, `canal`, `tipo` de las tablas siguientes.

### 3.1 Agregados y tablas

**`cuentas_por_cobrar.linea_credito`** — master data (seed inicial; sin CRUD prematuro salvo edición de límite/estado)
| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `cliente_id` | uuid | FK lógica a `datos_maestros.Cliente` (master ADR-0048 D6), vía read port |
| `moneda` | text | `MXN` / `USD` |
| `limite` | numeric | |
| `origen` | text | `SOLUNION` / `interno` |
| `plazo_dias` | int | 45 / 60 / 90 según país/origen |
| `estado` | text | `Activa` / `Bloqueada` / `Suspendida` |
| `motivo_bloqueo` | text? | |
| `version` | int | concurrencia optimista (ADR-0012) |

**`cuentas_por_cobrar.decision_liberacion`** — agregado de la decisión de crédito sobre un pedido
| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `pedido_ref` | text | folio/ref de A+W |
| `cliente_id` | uuid | |
| `credito_disponible_snapshot` | numeric | foto al momento de decidir |
| `resultado` | text | `Liberado` / `Retenido` / `LiberadoConOverride` |
| `regla_aplicada` | text | crédito / serie / override |
| `override_id` | uuid? | FK a `autorizacion_credito` |
| `decidido_por` | uuid | usuario |
| `decidido_en` | timestamptz | |

**`cuentas_por_cobrar.regla_liberacion_serie`** — catálogo (seed)
| `prefijo` | `comportamiento` (`siempre_libera` / `nunca_libera` / `evalua_credito`) |

**`cuentas_por_cobrar.seguimiento_cobranza`** — historial auditable
| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `cliente_id` | uuid | |
| `fecha` | timestamptz | |
| `usuario_id` | uuid | |
| `canal` | text | `llamada` / `correo` / `whatsapp` |
| `resultado` | text | `promesa_pago` / `sin_respuesta` / `excusa` / … |
| `monto_comprometido` | numeric? | |
| `fecha_comprometida` | date? | |
| `nota` | text | |

**`cuentas_por_cobrar.propuesta_aplicacion_pago`** + **`cuentas_por_cobrar.propuesta_aplicacion_factura`** (unión)
| Cabecera | Nota |
|---|---|
| `id`, `cliente_id`, `deposito_ref`, `monto_deposito`, `moneda`, `remittance_ref`, `estado` (`Propuesta`/`Confirmada`/`Rechazada`), `ajuste_no_fiscal` (tolerancia < $50) |
| Detalle: `propuesta_id`, `factura_uuid`, `importe_aplicado`, `num_parcialidad?` |

**`cuentas_por_cobrar.alerta_cartera`**
| `id`, `cliente_id`, `tipo` (`solunion_90d` / `exceso_credito` / `auto_bloqueo_vencimiento`), `disparada_en`, `atendida` |

**`cuentas_por_cobrar.autorizacion_credito`** — réplica del patrón consumible (calco de `AutorizacionAperturaCaja`)
| `id`, `empresa_id`, `supervisor_usuario_id`, `beneficiario_usuario_id`, `motivo`, `cliente_o_pedido_ref`, `estado` (`Autorizada`/`Usada`/`Cancelada`), `vigente_hasta`, `decision_liberacion_id?` (consumo) |

**`cuentas_por_cobrar.factura_cartera`** — proyección local de comprobantes cobrables
| Columna | Tipo | Nota |
|---|---|---|
| `id` | uuid PK | |
| `factura_venta_id` | uuid | correlación idempotente con Facturación |
| `cliente_id`, `uuid`, `folio?`, `total`, `moneda`, `fecha_timbrado`, `fecha_vencimiento` | | vencimiento = timbrado + plazo de la línea |
| `monto_pagado`, `monto_nc` | numeric | acumulados desde eventos |
| `estado` | text | `Abierta` / `Parcial` / `Pagada` / `Cancelada` |

Esta proyección se alimenta **solo** de los eventos de Facturación (§0) —
cero lectura directa a tablas del esquema `facturacion` (regla de oro de la
triada). Es la base de cartera, antigüedad, saldo neto 13-K y estado de cuenta.

### 3.2 Read models (vistas / proyecciones)
- **`vw_credito_disponible`** = `linea_credito.limite − facturado(factura_cartera) − liberado_sin_factura(G1)`.
- **Antigüedad de saldos** — proyección sobre `factura_cartera` con buckets configurables.
- **Saldo neto por cliente (13-K)** = `facturado − pagos_aplicados − NC` (derivable de `factura_cartera`).

> **Pendiente — sugerencia (G1):** `liberado sin factura` / "material en firme" **no tiene vista ni definición cerrada** en el contrato A+W (gap G1). Propuesta: el cálculo de crédito disponible se implementa con el término `liberado_sin_factura` marcado `PLATFORM-TODO(<CreditoLiberadoSinFactura>)` (ADR-0031), devolviendo 0 y una bandera `dato_incompleto=true` hasta que A+W cierre la definición; así el módulo avanza sin bloquearse y el número se vuelve exacto cuando llegue el contrato.

### 3.3 Permisos (RBAC, ADR-0007 + ADR-0041)

Convención confirmada en `Identidad.Domain.PermisosCanonicos`: código
`{modulo}.{recurso}.{accion}` con **exactamente 3 segmentos** separados por `.`
(el seed parser de `IdentidadDbContext` hace `Split('.')`), módulo en
snake_case (`cuentas_por_pagar.facturas.leer` es el precedente directo),
kebab-case dentro de un segmento cuando hay calificadores
(`autorizar-nivel1`). Namespace GUID: **`0000000a-*`** (siguiente libre;
ocupados 02=Identidad, 03=Compras, 04=Compartido, 05=Admin,
06=Integraciones Aw/Fiscal, 07=CxP, 08=Almacén, 09=Facturación).

- `cuentas_por_cobrar.lineas-credito.leer` · `cuentas_por_cobrar.lineas-credito.gestionar`
- `cuentas_por_cobrar.liberacion.decidir` · `cuentas_por_cobrar.liberacion.override`
- `cuentas_por_cobrar.cobranza.registrar` · `cuentas_por_cobrar.cartera.leer`
- `cuentas_por_cobrar.aplicacion-pago.proponer`

> **Nota de implementación:** tocar `PermisosCanonicos` exige migration en
> `IdentidadDbContext` (`HasData`); sin ella el deploy aborta con
> `PendingModelChangesWarning`. Va en PR-1.

---

## 4. Integraciones y frontera

### 4.1 Con Facturación (eventos + puerto de lectura)
- **Consume (worker):** `FacturacionEventListenerWorker` en
  `CuentasPorCobrar/Infrastructure/Workers`, topic `facturacion-events`,
  subscription `cuentas-por-cobrar-subscription` — mismo patrón que los
  cinco listeners existentes de la triada (`ComprasEventListenerWorker`,
  `AlmacenEventListenerWorker`, `TesoreriaEventListenerWorker` en CxP, etc.).
  Idempotencia correlacionando por `FacturaVentaId` / `ReciboPagoId` /
  `NotaCreditoId` / `ComprobanteId`.
- **Consume (puerto):** `IFacturacionAnticiposReadPort` (a definir en
  `CuentasPorCobrar/Domain/Ports/Facturacion/`, implementado por Facturación)
  exponiendo `AnticipoSaldoDetalle` — requiere promover el record desde
  `Facturacion.Application` a contrato público (cambio pequeño en Facturación).
- **Coordina:** instrucción de aplicación de anticipo (CxC instruye, Caja ejecuta — `D15`).

### 4.2 Con Ingresos
- CxC emite `PropuestaAplicacionPagoCreadaEvent`; Ingresos confirma y emite `PagoClienteConfirmadoEvent` (hoy [stub], `PLATFORM-TODO(<PagoClienteConfirmado>)`; topic natural: `tesoreria-events`, ya existente).

### 4.3 Con A+W (write-back de liberación) — PR dependiente
El ERP **nunca** escribe a A+W directamente (solo lectura de vistas, regla de `CLAUDE.md`). La única vía de escritura es la **tabla-puente en `MILLET_INTEGRACION`** que A+W lee (patrón ADR-0048). El write-back de facturación existente **no cubre** estado de crédito/liberación.

> **Pendiente — sugerencia (G-writeback / G6):** el write-back de la decisión de liberación es **contrato nuevo** (columna o tabla nueva en `MILLET_INTEGRACION`) y depende de coordinación con el equipo A+W. Además `estado_origen` del pedido está hardcodeado a NULL (G6). Propuesta: agendar como **PR-9 dependiente**, fuera de la ruta crítica del MVP; el MVP entrega el *cálculo y la decisión*, no el efecto sobre A+W. Recordar que cualquier cambio a `03_create_views.sql` se re-corre a mano en SER-DATA (SQL Server 2016 RTM: sin `CREATE OR ALTER`, patrón stub+`ALTER`).

### 4.4 Con SAP (transición)
**Cero vistas contratadas contra SAP.** Solo extractores one-shot para carga inicial de maestros (ADR-0044).

> **Pendiente — sugerencia [Decisión CXC-3]:** CxC gestiona AR **solo de facturas emitidas por el ERP nuevo**. La cartera histórica de SAP se **migra una vez** (patrón ADR-0044) o se deja read-only en SAP durante una ventana de transición. Confirmar con Prida el alcance de la migración histórica (¿saldos abiertos al corte, o histórico completo?).

---

## 5. Gap analysis consolidado

| # | Gap | Impacto | Manejo propuesto |
|---|---|---|---|
| G1 | "Material liberado / en firme" sin vista ni definición (A+W) | Crédito disponible no exacto | `PLATFORM-TODO` + bandera `dato_incompleto`; cerrar con A+W |
| G6 | `estado_origen` de pedido = NULL en vista A+W | Estatus de pedido no confiable | Coordinar columna real con A+W |
| G-writeback | Sin contrato de write-back de liberación | A+W no respeta la liberación del ERP | PR-9 dependiente + coordinación A+W |
| G-SAP | Cero vistas contra SAP | Cartera histórica en transición | Migración one-shot (CXC-3) |
| G-13K | "Saldo neto de NC" no formalizado | Definición ambigua | Formalizar en CxC (CXC-5) |
| G-prontopago | NC pronto pago inexistente como figura | Riesgo de timbrar egreso indebido | Tratar < $50 USD como tolerancia no fiscal (CXC-4) |
| G-anticipos-port | `AnticipoSaldoDetalle` es record interno de `Facturacion.Application` | CxC no puede consumirlo cross-módulo | Promover a `IFacturacionAnticiposReadPort` (cambio chico en Facturación, PR-6) |
| G-auth | Sin abstracción común de autorización (ya hay 2 réplicas del consumible) | CxC sería la 3ª réplica | Replicar calco de `AutorizacionAperturaCaja`; recomendar ADR transversal |
| G-eventostub | `PagoClienteConfirmadoEvent` [stub] (`PLATFORM-TODO(<PagoClienteConfirmado>)`) | Emisor real indefinido | Confirmar owner Ingresos/Tesorería; topic natural `tesoreria-events` |

**Gaps cerrados en la validación contra código (eran pendientes en v0.1):**

| # | Era | Resolución |
|---|---|---|
| ~~G-reportes~~ | "ADR de motor de reportes pendiente" | **ADR-0036 ya existe y está cerrado** (motor nativo React + JSON + `<ReporteShell>`); PR-5 no está bloqueado por ADR |
| ~~G-permisos~~ | "Notación inconsistente + sin índice GUID" | Convención confirmada (3 segmentos, módulo snake_case); siguiente namespace libre `0000000a-*` |

---

## 6. Breakdown de PRs (secuencia propuesta)

Disciplina de un PR a la vez, squash merges, trunk-based. Ramas `cxc/*` para
entrar al auto-mode N2. Módulo sensible → se parte fino.

| PR | Alcance | Depende de |
|---|---|---|
| **PR-1** | Fundaciones: proyecto `Millet.CuentasPorCobrar` + `CuentasPorCobrarDbContext` (esquema `cuentas_por_cobrar`), entidad `LineaCredito` (seed), permisos canónicos (`0000000a-*` + migration en `IdentidadDbContext`) + wiring RBAC. **Checklist DbContext nuevo:** `Program.cs` (`AddDbContext` + `MigrationsHealthCheckOptions`) **y** `deploy-app-dev.yml` en el mismo PR | — |
| **PR-2** | Read model de crédito disponible (`límite − facturado − liberado`); `liberado` como `PLATFORM-TODO(<CreditoLiberadoSinFactura>)` (G1); `facturado` provisional en 0 hasta PR-3 (bandera `dato_incompleto`) | PR-1 |
| **PR-3** | Consumo de eventos de Facturación: `FacturacionEventListenerWorker` + subscription `cuentas-por-cobrar-subscription` (Bicep) + proyección `factura_cartera` (factura-venta timbrada/cancelada, REPP, cobro mostrador ± cancelación, NC) + saldo neto 13-K + puerto `IFacturacionAnticiposReadPort` (incluye promover `AnticipoSaldoDetalle` en Facturación) | PR-1 |
| **PR-4** | Decisión de liberación: reglas de crédito + `regla_liberacion_serie` (seed) + override consumible (`autorizacion_credito`, calco de `AutorizacionAperturaCaja`). **Persiste la decisión; sin write-back** | PR-2 |
| **PR-5** | Seguimiento de cobranza (historial auditable) | PR-1 |
| **PR-6** | Cartera / antigüedad de saldos + estado de cuenta (read model + render ADR-0036 con `<ReporteShell>`) | PR-3 |
| **PR-7** | Propuesta de aplicación de pago (matching desde remittance) → `PropuestaAplicacionPagoCreadaEvent` (topic `cuentas-por-cobrar-events` + Outbox, Bicep del topic) | PR-3 |
| **PR-8** | Alertas de cartera (90d SOLUNION, exceso de crédito, auto-bloqueo por vencimientos) | PR-2, PR-6 |
| **PR-9** *(dependiente)* | Write-back de decisión de liberación a tabla-puente `MILLET_INTEGRACION`. **Bloqueado por coordinación A+W** (G1/G6/G-writeback) | PR-4 + contrato A+W |

Cambios de secuencia vs v0.1: el consumo de eventos (antes PR-6) **sube a PR-3**
porque la proyección `factura_cartera` es prerequisito real de cartera/antigüedad
(antes PR-5, ahora PR-6) y del `facturado` del crédito disponible; la antigüedad
ya no depende de ningún ADR (0036 cerrado). Infra que acompaña: topic
`cuentas-por-cobrar-events` y subscription en `facturacion-events` van por Bicep
(y cualquier setting nuevo de `Program.cs` lleva su app setting en
`appservice.bicep` en el mismo PR).

El frontend se planifica aparte (05/06/07-frontend-*) sobre los patrones de
`frontend/docs/patrones-compras.md` y el nav shell de cards; ramas `cxc-fe/*`.

---

## 7. Decisiones estructurales (aprobadas 2026-07-13)

- **CXC-1** — Aplicación de pago bancario: CxC propone matching; Ingresos confirma y emite `PagoClienteConfirmadoEvent`; Facturación timbra REPP; CxC consume el timbrado.
- **CXC-2** — Liberación: el ERP calcula y persiste la decisión en el MVP; el write-back a A+W es contrato nuevo y va como PR dependiente, desacoplado de la ruta crítica.
- **CXC-3** — Cartera durante el strangler: CxC gestiona AR de facturas emitidas por el ERP; histórico SAP migrado one-shot o read-only en ventana de transición. Límite de crédito pasa a ser entidad propia de CxC.
- **CXC-4** — Diferencia < $50 USD por comisión bancaria: tolerancia de conciliación **no fiscal**; sin CFDI.
- **CXC-5** — "Saldo por cobrar neto de NC" (13-K): definición propia de CxC; se formaliza en este mapa.
- **CXC-6** — SOLUNION: fuera de integración automatizada en MVP; se modela `origen` de línea y la alerta de 90 días; reporte mensual y gestión de límites siguen manuales.
- **CXC-7** — Un solo módulo CxC scoped por moneda/nacionalidad; Prida como autoridad funcional.

---

## 8. Siguiente paso

Confirmación de los `Pendiente` por parte de los owners (Prida: series de folio nacionales, plazos de crédito nacionales, alcance de migración histórica; fiscal: tolerancia < $50 sin CFDI; equipo A+W: G1/G6/G-writeback). Con eso se cierra a **v0.3** y se arranca **PR-1**. Los pendientes de código de v0.1 (permisos, reportes, nombres de eventos) ya quedaron resueltos en §9.

---

## 9. Validación contra el código (2026-07-13)

Diferencias encontradas al contrastar la v0.1 con el monorepo; todas ya están
integradas arriba:

1. **Naming del módulo.** El repo no usa siglas: esquema `cuentas_por_pagar`,
   topic `cuentas-por-pagar-events`, permisos `cuentas_por_pagar.*`. CxC adopta
   `cuentas_por_cobrar` / `cuentas-por-cobrar-events` / `cuentas_por_cobrar.*`
   (v0.1 proponía `cxc` en los tres).
2. **Eventos de Facturación.** Los nombres reales llevan sufijo
   `IntegrationEvent` (`FacturacionIntegrationEvents.cs`) y tipo
   `facturacion.{recurso}.{accion}.v1`. v0.1 omitía dos eventos necesarios:
   `FacturaVentaTimbradaIntegrationEvent` (la base del `facturado` — el header
   del archivo ya nombra a CxC como consumidor) y
   `ComprobanteCanceladoIntegrationEvent` (sin él la cartera no reversa
   cancelaciones).
3. **"Facturas emitidas (Postgres interno)" violaba la regla de oro** de cero
   acceso directo a tablas de otro módulo. Se reemplaza por la proyección local
   `factura_cartera` alimentada por eventos (patrón "acumulados calculados por
   el publisher" de la triada).
4. **`AnticipoSaldoDetalle` no es contrato público** — vive en
   `Facturacion.Application.Anticipos.Queries`. Nuevo gap G-anticipos-port:
   promoverlo a `I*ReadPort` (patrón `CuentasPorPagar/Domain/Ports/*`).
5. **G-reportes era stale:** ADR-0036 existe y está cerrado; nada del render
   está bloqueado.
6. **G-permisos resuelto:** convención = 3 segmentos `Split('.')`, módulo
   snake_case, kebab en calificadores; namespaces GUID ocupados hasta
   `00000009-*` (Facturación) → CxC toma `0000000a-*`.
7. **Autorización consumible:** el calco correcto es `AutorizacionAperturaCaja`
   (que a su vez calca `AutorizacionVentaActivo`) — CxC sería la tercera
   réplica; se copian sus invariantes (un solo uso, vigencia ≤24 h, no
   autoconsumo, estados).
8. **`PagoClienteConfirmadoEvent` confirmado como stub** en
   `ReppEndpoints.cs` (`PLATFORM-TODO(<PagoClienteConfirmado>)`); el topic
   `tesoreria-events` ya existe y CxP ya lo consume — es el emisor natural.
9. **Secuencia de PRs corregida:** el consumo de eventos sube a PR-3 (era
   PR-6) porque cartera/antigüedad y el `facturado` dependen de él; PR-1
   arrastra el checklist de DbContext nuevo (`Program.cs` +
   `deploy-app-dev.yml`) y la migration de `IdentidadDbContext`.
