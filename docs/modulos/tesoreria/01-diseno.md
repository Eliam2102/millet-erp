# Diseño — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> **Versión:** 0.1
> **Fecha:** 2026-07-14
> **Basado en:** [`00-levantamiento.md`](00-levantamiento.md) v0.1 (decisiones TES-1…TES-9 aprobadas; gaps T-G1…T-G11 abiertos).
> **Estado:** borrador. Ejecutable desde PR-1; las secciones marcadas con gap
> no bloquean la foundation pero sí su PR correspondiente (ver §3).

---

## 0. Cómo leer este documento

- Replica el patrón de [`cuentas-por-cobrar/01-diseno.md`](../cuentas-por-cobrar/01-diseno.md): posicionamiento → dominio → esquema → puertos → CQRS → eventos → workers → RBAC → endpoints → plataforma.
- Los contratos con CxP están **congelados** (levantamiento §1.4); aquí solo se referencian, no se redefinen.
- `[T-Gn]` remite al gap del levantamiento §11.

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

Módulo backend `Millet.Tesoreria` (namespace nuevo), esquema Postgres
`tesoreria`, DbContext propio (`TesoreriaDbContext`, ADR-0030). Décimo
módulo funcional del back-office; contraparte real del topic
`tesoreria-events` que CxP y Compras ya esperan. Arquitectura hexagonal +
CQRS con MediatR, siguiendo el exemplar de Compras.

### 1.2 Alcance funcional v1 (MVP)

1. Catálogo de cuentas bancarias propias (seed) y libro de movimientos.
2. Bandeja de pasivos autorizados (proyección de CxP) y pago individual
   con publicación de los 4 eventos espejo.
3. Corrida de pagos con matriz de autorización y oficio de cartera.
4. Pago a cuenta con gate RN-2 y reconciliación tardía.
5. Confirmación de depósitos de cliente (`PagoClienteConfirmadoEvent`).
6. Registro de REPP recibido de proveedor con read model de pendientes.
7. Conciliación bancaria por archivo con matching asistido.
8. Reportes: flujo de efectivo, auxiliares, actas (ADR-0036).

**Fuera de v1:** conector bancario vivo (`Millet.Integraciones.<Banco>`),
pago cross-moneda (RN-3), validación fiscal automática de REPP recibidos
[T-G10], dispersión/SPEI. (El CRUD de cuentas bancarias estaba diferido a
Administración; TES-7 se revisó post-v1 y el CRUD vive en Tesorería — ver
§4.1 y §11.)

### 1.3 Volúmenes esperados

Del AS-IS (levantamiento §2): ~26 pagos/día, 5-10 pagos a cuenta/semana,
70-100 movimientos con REPP/semana, conciliación mensual por cuenta.
Concurrencia estimada: 2 usuarios (Jefe + Auxiliar) [T-G9]. Carga trivial;
ningún requisito de performance especial más allá de índices de bandeja.

---

## 2. Decisiones de diseño y ADRs aplicados

| Tema | Decisión | ADR / referencia |
|---|---|---|
| Persistencia | Esquema `tesoreria`, `TesoreriaDbContext`, migrations propias | ADR-0005, ADR-0030 |
| Eventos | Outbox propio (`integration_events_outbox`) + `OutboxPublisherWorker<TesoreriaDbContext>` | ADR-0009 |
| Multi-empresa | `empresa_id` en todas las tablas de negocio | ADR-0011 |
| Dinero | `numeric(18,2)` + moneda ISO; TC del pasivo viene en el evento | ADR-0014 |
| Fechas de negocio | `fecha_valor`, `fecha_pago`, `fecha_complemento` como `date` (`DateOnly`) | ADR-0040 |
| Concurrencia | Optimista vía `xmin` en agregados mutables (corrida, conciliación, depósito) | ADR-0012 |
| Idempotencia HTTP | `Idempotency-Key` (UUID v4 puro) en todas las mutaciones | ADR-0020 |
| Idempotencia de listeners | Tabla `tesoreria.evento_procesado` (patrón CxP/CxC) | — |
| Nombres cross-módulo | Proveedor/Cliente por read-port, nunca JOIN | ADR-0042 |
| PII | CLABE/número de cuenta como VO con masking en logs y UI | ADR-0006, ADR-0018 |
| Documentos | XML de REPP recibido y archivos de extracto a Blob | ADR-0024 |
| Reportería | JSON estructurado + `<ReporteShell>`; oficio y acta como PDF client-side | ADR-0036 |
| Autorización | Permisos canónicos `tesoreria.{recurso}.{accion}` | ADR-0007, ADR-0041 |
| Stubs | `PLATFORM-TODO(<id>)` según tabla §13 | ADR-0031 |
| Candado de periodo | Puerto propio `IPeriodoContablePort` (espejo del de Facturación), stub siempre-abierto | levantamiento RN-8 |
| Contabilización | Sin puerto síncrono: eventos al outbox desde PR-11 [TES-5] | — |

---

## 3. Asunciones que deben confirmarse

| # | Asunción | Bloquea | Gap |
|---|---|---|---|
| A1 | No hay pago cross-moneda operativo hoy; RN-3 aplica en MVP | PR-2 (constraint) | T-G6 |
| A2 | Matriz de corridas: Jefe cualquier monto, escalamiento configurable | PR-5 (seed) | T-G4 |
| A3 | `MetodoPago` del pasivo se obtiene enriqueciendo el evento v1 de CxP (opción a) o por read-port (opción b) | PR-8 | T-G11 |
| A4 | Los bancos operativos exportan extracto en formato tabular parseable | PR-9 (perfiles) | T-G2 |
| A5 | CxC acepta el evento de rechazo de propuesta y re-propone | PR-7 (rechazo) | T-G7 |
| A6 | Saldos iniciales por cuenta a fecha de corte, sin histórico SAP | PR-9 (arranque) | T-G8 |
| A7 | GUIDs de permisos: siguiente bloque libre tras CxC (`0000000a-*`) — **validar contra `PermisosCanonicos` al abrir PR-1** | PR-1 | — |

---

## 4. Modelo del dominio

### 4.1 Agregados raíz

| Agregado | Contenido | Mutabilidad |
|---|---|---|
| `CuentaBancaria` | Cuenta propia + perfil de extracto | CRUD en Tesorería [TES-7 revisada]: alta/edición/toggle con `tesoreria.cuentas.administrar`; `numero_cuenta` inmutable post-creación; `moneda` editable solo sin movimientos; seed script solo para bootstrap |
| `MovimientoBancario` | Movimiento + colección `AplicacionPagoProveedor` | Inmutable una vez conciliado; reversa por contramovimiento (RN-10) |
| `CorridaPago` | Cabecera + `CorridaPagoLinea[]` | Máquina de estados §4.2 |
| `DepositoConfirmacion` | Liga movimiento↔propuesta CxC | `Pendiente → Confirmada/Rechazada` |
| `ReppProveedorRecibido` | UUID + XML ref | Inmutable post-registro |
| `Conciliacion` | Cabecera + `ExtractoLinea[]` | `Abierta → Cerrada` |
| `ConceptoMovimiento` | Catálogo con clasificación de flujo | CRUD admin básico |

`PasivoPendientePago` es **proyección**, no agregado: la escribe solo el
listener de CxP y las aplicaciones locales; nunca un endpoint.

### 4.2 Invariantes principales

- `MovimientoBancario.Moneda == CuentaBancaria.Moneda` (RN-3).
- `sum(AplicacionPagoProveedor.ImporteAplicado) <= Movimiento.Monto`;
  `EstadoAplicacion` se deriva: sin aplicaciones = `NoAplicado`, suma
  parcial = `AplicadoParcial`, suma completa = `Aplicado`.
- `AplicacionPagoProveedor.ImporteAplicado <= PasivoPendientePago.SaldoPendiente`
  (validación local; la autoridad final es CxP, que rechaza con
  `PAGO_EXCEDE_SALDO`).
- Máximo un `MovimientoBancario` de egreso con `EstadoAplicacion=NoAplicado`
  y `BeneficiarioTipo=Proveedor` por proveedor (RN-2) — validado en el
  command, con índice parcial único de respaldo (§5.2).
- `CorridaPago`: `Borrador → EnAutorizacion → Autorizada → Ejecutada → Cerrada`;
  `Rechazada`/`Cancelada` solo desde `Borrador`/`EnAutorizacion`. Ejecutar
  requiere `Autorizada` (RN-5). Ejecución parcial permitida (flag por línea).
- `DepositoConfirmacion.Confirmar()` exige `MovimientoBancarioId` de un
  movimiento de ingreso (RN-6).
- `Conciliacion.Cerrar()` exige todas las líneas con match o marcadas en
  tránsito, y saldo cuadrado (RN-7).
- Toda mutación valida `IPeriodoContablePort.EstaAbiertoAsync` (RN-8; hoy
  stub).
- Reversa de aplicación: marca `Revertida=true`, crea contramovimiento
  ligado y publica `revertido.v1`; nada se borra (RN-10).

### 4.3 Value objects

- `Clabe` — 18 dígitos, validación de dígito verificador; `ToString()`
  enmascara (`***…1234`); ya existe regex de referencia (ADR-0018 §CLABE).
- `NumeroCuenta` — masking análogo.
- `ReferenciaBancaria` — texto corto normalizado (trim, upper).
- `PeriodoConciliacion` — (año, mes) con validación de rango.

---

## 5. Esquema PostgreSQL

```sql
CREATE TABLE tesoreria.cuenta_bancaria (
    id                  uuid PRIMARY KEY,
    empresa_id          uuid NOT NULL,
    banco               text NOT NULL,
    numero_cuenta       text NOT NULL,
    clabe               text NULL,
    moneda              char(3) NOT NULL,
    cuenta_contable_ref text NULL,
    perfil_extracto     text NULL,
    activa              boolean NOT NULL DEFAULT true
);

CREATE TABLE tesoreria.concepto_movimiento (
    id                  uuid PRIMARY KEY,
    nombre              text NOT NULL,
    clasificacion_flujo smallint NOT NULL,  -- 1=Operación 2=Inversión 3=Financiamiento
    activo              boolean NOT NULL DEFAULT true
);

CREATE TABLE tesoreria.movimiento_bancario (
    id                  uuid PRIMARY KEY,
    empresa_id          uuid NOT NULL,
    cuenta_bancaria_id  uuid NOT NULL REFERENCES tesoreria.cuenta_bancaria(id),
    sentido             smallint NOT NULL,        -- 1=Ingreso 2=Egreso
    monto               numeric(18,2) NOT NULL CHECK (monto > 0),
    moneda              char(3) NOT NULL,
    fecha_valor         date NOT NULL,
    referencia_bancaria text NULL,
    concepto_id         uuid NULL REFERENCES tesoreria.concepto_movimiento(id),
    estado_aplicacion   smallint NOT NULL DEFAULT 1, -- 1=NoAplicado 2=AplicadoParcial 3=Aplicado
    estado_conciliacion smallint NOT NULL DEFAULT 1, -- 1=NoConciliado 2=Conciliado
    beneficiario_tipo   smallint NULL,            -- 1=Proveedor 2=Cliente 3=Otro
    beneficiario_ref    uuid NULL,
    contramovimiento_de uuid NULL REFERENCES tesoreria.movimiento_bancario(id),
    motivo_no_aplicado  text NULL,                -- pago a cuenta: motivo obligatorio
    creado_por          uuid NOT NULL,
    creado_en           timestamptz NOT NULL
);

CREATE TABLE tesoreria.aplicacion_pago_proveedor (
    id                    uuid PRIMARY KEY,       -- = PagoId del evento aplicado.v1
    movimiento_id         uuid NOT NULL REFERENCES tesoreria.movimiento_bancario(id),
    factura_proveedor_id  uuid NOT NULL,          -- del evento; sin FK cross-módulo
    proveedor_id          uuid NOT NULL,
    importe_aplicado      numeric(18,2) NOT NULL CHECK (importe_aplicado > 0),
    corrida_id            uuid NULL REFERENCES tesoreria.corrida_pago(id),
    revertida             boolean NOT NULL DEFAULT false,
    creado_en             timestamptz NOT NULL,
    UNIQUE (movimiento_id, factura_proveedor_id)
);

CREATE TABLE tesoreria.pasivo_pendiente_pago (
    factura_proveedor_id uuid PRIMARY KEY,
    empresa_id           uuid NOT NULL,
    proveedor_id         uuid NOT NULL,
    orden_compra_id      uuid NULL,
    monto_total          numeric(18,2) NOT NULL,
    saldo_pendiente      numeric(18,2) NOT NULL,
    moneda               char(3) NOT NULL,
    tipo_cambio          numeric(12,6) NULL,
    fecha_vencimiento    date NOT NULL,
    uuid_cfdi            uuid NULL,
    folio_proveedor      text NULL,
    metodo_pago          text NULL,               -- [T-G11] hoy no llega; nullable a propósito
    recibido_en          timestamptz NOT NULL
);

CREATE TABLE tesoreria.corrida_pago (
    id                 uuid PRIMARY KEY,
    empresa_id         uuid NOT NULL,
    cuenta_bancaria_id uuid NOT NULL REFERENCES tesoreria.cuenta_bancaria(id),
    estado             smallint NOT NULL DEFAULT 1, -- 1..7 (§4.2)
    total              numeric(18,2) NOT NULL,
    moneda             char(3) NOT NULL,
    solicitada_por     uuid NOT NULL,
    autorizada_por     uuid NULL,
    oficio_generado_en timestamptz NULL,
    creada_en          timestamptz NOT NULL
);

CREATE TABLE tesoreria.corrida_pago_linea (
    id                   uuid PRIMARY KEY,
    corrida_id           uuid NOT NULL REFERENCES tesoreria.corrida_pago(id),
    factura_proveedor_id uuid NOT NULL,
    importe_programado   numeric(18,2) NOT NULL,
    ejecutada            boolean NOT NULL DEFAULT false,
    UNIQUE (corrida_id, factura_proveedor_id)
);

CREATE TABLE tesoreria.deposito_confirmacion (
    id                  uuid PRIMARY KEY,
    empresa_id          uuid NOT NULL,
    movimiento_id       uuid NULL REFERENCES tesoreria.movimiento_bancario(id),
    propuesta_cxc_id    uuid NULL,
    cliente_id          uuid NOT NULL,
    estado              smallint NOT NULL DEFAULT 1, -- 1=Pendiente 2=Confirmada 3=Rechazada
    motivo_rechazo      text NULL,
    caja_sesion_id      uuid NULL,
    repp_timbrado       boolean NOT NULL DEFAULT false,
    facturas_json       jsonb NOT NULL DEFAULT '[]', -- (FacturaVentaId, ImporteAplicado)[]
    confirmada_por      uuid NULL,
    confirmada_en       timestamptz NULL
);

CREATE TABLE tesoreria.repp_proveedor_recibido (
    id                    uuid PRIMARY KEY,
    empresa_id            uuid NOT NULL,
    factura_proveedor_id  uuid NOT NULL,
    uuid_complemento      uuid NOT NULL UNIQUE,
    fecha_complemento     date NOT NULL,
    xml_blob_ref          text NULL,
    registrado_por        uuid NOT NULL,
    registrado_en         timestamptz NOT NULL
);

CREATE TABLE tesoreria.conciliacion (
    id                 uuid PRIMARY KEY,
    empresa_id         uuid NOT NULL,
    cuenta_bancaria_id uuid NOT NULL REFERENCES tesoreria.cuenta_bancaria(id),
    periodo_anio       int NOT NULL,
    periodo_mes        int NOT NULL CHECK (periodo_mes BETWEEN 1 AND 12),
    saldo_extracto     numeric(18,2) NULL,
    estado             smallint NOT NULL DEFAULT 1, -- 1=Abierta 2=Cerrada
    cerrada_por        uuid NULL,
    cerrada_en         timestamptz NULL,
    UNIQUE (cuenta_bancaria_id, periodo_anio, periodo_mes)
);

CREATE TABLE tesoreria.extracto_linea (
    id               uuid PRIMARY KEY,
    conciliacion_id  uuid NOT NULL REFERENCES tesoreria.conciliacion(id),
    fecha            date NOT NULL,
    descripcion      text NOT NULL,
    referencia       text NULL,
    cargo            numeric(18,2) NULL,
    abono            numeric(18,2) NULL,
    fuente           smallint NOT NULL DEFAULT 1, -- 1=Archivo 2=Api
    match_movimiento uuid NULL REFERENCES tesoreria.movimiento_bancario(id),
    match_tipo       smallint NULL                -- 1=Auto 2=Manual 3=AltaAsistida
);

CREATE TABLE tesoreria.evento_procesado (
    evento_id    uuid NOT NULL,
    evento_tipo  text NOT NULL,
    procesado_en timestamptz NOT NULL,
    PRIMARY KEY (evento_id, evento_tipo)
);
-- + tabla integration_events_outbox estándar del ADR-0009
```

### 5.1 Índices críticos

- `pasivo_pendiente_pago (empresa_id, fecha_vencimiento)` y `(proveedor_id)` — bandeja.
- `movimiento_bancario (cuenta_bancaria_id, fecha_valor)` — auxiliares y matching.
- `movimiento_bancario (beneficiario_ref) WHERE estado_aplicacion = 1 AND sentido = 2` — gate RN-2.
- `aplicacion_pago_proveedor (factura_proveedor_id)` — trazabilidad por pasivo.
- `extracto_linea (conciliacion_id, match_movimiento)` — cierre.

### 5.2 Constraints

- **Todos los enums persistidos con `HasCheckConstraint` + mirror FE**
  (regla del repo; incidente FacturaAnticipo 2026-07-11): `sentido`,
  `estado_aplicacion`, `estado_conciliacion`, `beneficiario_tipo`,
  `corrida_pago.estado`, `deposito_confirmacion.estado`,
  `conciliacion.estado`, `extracto_linea.fuente`, `match_tipo`,
  `clasificacion_flujo`.
- Índice parcial único para RN-2:
  `CREATE UNIQUE INDEX ux_pago_cuenta_abierto ON tesoreria.movimiento_bancario (empresa_id, beneficiario_ref) WHERE sentido = 2 AND estado_aplicacion = 1 AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL;`
- `CHECK (cargo IS NOT NULL OR abono IS NOT NULL)` en `extracto_linea`.

### 5.3 Migraciones

Primera migration crea el esquema completo + seed de
`concepto_movimiento` (catálogo inicial pendiente de Javier, §5.2 del
levantamiento) + seed de `cuenta_bancaria` [T-G2]. Checklist ADR-0030
en el mismo PR: `Program.cs` (AddDbContext + `MigrationsHealthCheckOptions`)
y `deploy-app-dev.yml`.

---

## 6. Puertos y adaptadores

### 6.1 Puertos que Tesorería consume (`Tesoreria/Domain/Ports/`)

| Puerto | Dueño real | Adapter v1 |
|---|---|---|
| `IProveedorBancoReadPort` | DatosMaestros | **Nuevo** — lectura de CLABE/banco/beneficiario del proveedor, con masking. Resuelve `PayloadEnriquecido` [T-G1] |
| `IProveedorReadPort` / `IClienteReadPort` (nombres) | DatosMaestros | Reuso del patrón ADR-0042 existente |
| `IPeriodoContablePort` | Contabilidad (futura) | Stub siempre-abierto propio del módulo, `PLATFORM-TODO(<PeriodoContableCerrado>)` — puerto propio en el namespace de Tesorería (hexagonal), misma semántica que el de Facturación |
| `IExtractoFuente` | Propio (costura) | `ArchivoExtractoAdapter` (perfiles §7); post-MVP `Millet.Integraciones.<Banco>` implementa la misma interfaz |
| `IMatrizAutorizacionPort` | Compras/plataforma (matriz reutilizable) | Verificar en PR-5 cómo exponen Requisiciones/Compras la matriz; si no hay contrato público, stub + `PLATFORM-TODO(<MatrizAutorizacionCompartida>)` |

### 6.2 Puertos que Tesorería expone

Ninguno en v1. Los módulos interesados (CxP, Compras, CxC, Facturación)
consumen **eventos**, nunca lecturas síncronas. Si Contabilidad futura
necesita detalle de movimientos, se evaluará un `ITesoreriaReadPort`
entonces.

### 6.3 Adaptadores de mensajería

- Outbox publisher: registro de `OutboxPublisherWorker<TesoreriaDbContext>`.
- 3 listeners entrantes (§9), todos con dedupe por `evento_procesado`,
  dead-letter en EventType desconocido/JSON corrupto y abandono para
  retry en dependencias aún no proyectadas (patrón `MaxDeliveryCount=5`
  de CxC).

---

## 7. Comandos y queries (CQRS, MediatR)

### 7.1 Comandos

| Comando | Efecto | Publica |
|---|---|---|
| `RegistrarPagoProveedorCommand` | Movimiento de egreso + N aplicaciones contra pasivos de la bandeja | `aplicado.v1` × N |
| `RevertirPagoProveedorCommand` | Contramovimiento + marca aplicación revertida | `revertido.v1` |
| `RegistrarPagoACuentaCommand` | Movimiento egreso `NoAplicado` con motivo + gate RN-2 | — |
| `LigarPagoACuentaCommand` | Reconciliación tardía: aplica movimiento preexistente a pasivo | `aplicado.v1` |
| `CrearCorridaCommand` / `AgregarLineaCorridaCommand` / `QuitarLineaCorridaCommand` | Armado en `Borrador` | — |
| `EnviarCorridaAAutorizacionCommand` / `AutorizarCorridaCommand` / `RechazarCorridaCommand` / `CancelarCorridaCommand` | Máquina de estados + matriz | — |
| `EjecutarPagoCorridaCommand` | Por línea: movimiento + aplicación, marca ejecutada | `aplicado.v1` |
| `CerrarCorridaCommand` | Todas las líneas resueltas | — |
| `RegistrarMovimientoIngresoCommand` | Alta manual de depósito (o vía alta asistida de conciliación) | — |
| `ConfirmarDepositoCommand` | Liga movimiento↔propuesta + confirma | `pago-cliente.confirmado.v1` |
| `RechazarPropuestaDepositoCommand` | Rechazo con motivo [T-G7] | `propuesta-aplicacion.rechazada.v1` |
| `RegistrarReppRecibidoCommand` | UUID + XML a Blob | `repp-proveedor.recibido.v1` |
| `SolicitarCancelacionPasivoCommand` | Solicitud hacia CxP | `cancelacion-pasivo.solicitada.v1` |
| `CargarExtractoCommand` | Parse por perfil → `extracto_linea[]` + matching automático sugerido | — |
| `ConfirmarMatchCommand` / `ConfirmarMatchesEnLoteCommand` | Fija matches sugeridos/manuales | — |
| `AltaAsistidaDesdeExtractoCommand` | Crea movimiento faltante desde línea (comisiones, intereses) | — |
| `CerrarConciliacionCommand` | Valida RN-7, genera acta | — |

Validación con FluentValidation (ADR-0018); todos con `Idempotency-Key`.

### 7.2 Queries

`BandejaPasivosPendientesQuery` (filtros: vencimiento, proveedor, moneda,
monto), `MovimientosBancariosQuery` + `MovimientoDetalleQuery`,
`SaldosPorCuentaQuery`, `CorridasQuery` + `CorridaDetalleQuery` +
`OficioCarteraQuery` (JSON del reporte), `DepositosPorConfirmarQuery`,
`PagosACuentaAbiertosQuery` (con antigüedad), `ReppPendientesQuery`
[T-G11], `ConciliacionesQuery` + `ConciliacionDetalleQuery` +
`ActaConciliacionQuery`, `FlujoEfectivoReporteQuery`,
`AuxiliarBancosReporteQuery`.

---

## 8. Eventos de integración

### 8.1 Publicados (topic `tesoreria-events`, vía outbox)

| EventType | Consumidor | Estado del contrato |
|---|---|---|
| `tesoreria.pago-factura-proveedor.aplicado.v1` | CxP (desplegado), Compras (TODO) | **Congelado** — payload espejo exacto |
| `tesoreria.pago-factura-proveedor.revertido.v1` | CxP | **Congelado** |
| `tesoreria.repp-proveedor.recibido.v1` | CxP | **Congelado** |
| `tesoreria.cancelacion-pasivo.solicitada.v1` | CxP | **Congelado** |
| `tesoreria.pago-cliente.confirmado.v1` | Facturación (listener nuevo) | **Nuevo** — payload en levantamiento §3.3; cerrar junto con el PR gemelo de Facturación |
| `tesoreria.propuesta-aplicacion.rechazada.v1` | CxC | **Propuesto** [T-G7] |
| `tesoreria.movimiento-bancario.registrado.v1` | Contabilidad (futura) | PR-11; diseño del payload contable diferido |

### 8.2 Suscritos

| EventType | Topic / subscription nueva | Handler |
|---|---|---|
| `cuentas_por_pagar.pasivo.autorizado-para-pago.v1` | `cuentas-por-pagar-events` / `tesoreria-subscription` | Upsert a `pasivo_pendiente_pago`; si existe pago a cuenta abierto del proveedor, marca sugerencia de liga tardía |
| `cuentas_por_cobrar.propuesta-aplicacion.creada.v1` | `cuentas-por-cobrar-events` / `tesoreria-subscription` | Crea `deposito_confirmacion` en `Pendiente` |
| `facturacion.caja-sesion.cerrada.v1` | `facturacion-events` / `tesoreria-subscription` | Crea expectativa de depósito (deposito_confirmacion con `caja_sesion_id`) |
| `facturacion.recibo-pago.timbrado.v1` | (misma subscription) | Marca `repp_timbrado=true` en la confirmación correspondiente |

Filtros SQL por `EventType` en el Bicep, igual que las subscriptions
existentes. Naming exacto se alinea al patrón del `servicebus.bicep`
en PR-3/PR-7.

---

## 9. Workers en-proceso (`IHostedService` en `Millet.Api`)

| Worker | Consume | PR |
|---|---|---|
| `OutboxPublisherWorker<TesoreriaDbContext>` | outbox propio | PR-4 |
| `CuentasPorPagarEventListenerWorker` (en Tesorería) | pasivo autorizado | PR-3 |
| `CuentasPorCobrarEventListenerWorker` (en Tesorería) | propuestas de aplicación | PR-7 |
| `FacturacionEventListenerWorker` (en Tesorería) | caja-sesion.cerrada + recibo-pago.timbrado | PR-7 |

Todos con advisory lock (ADR-0022), `PeekLock`, `AutoCompleteMessages=false`,
dedupe por `evento_procesado`.

---

## 10. RBAC — permisos canónicos

Formato `tesoreria.{recurso}.{accion}` (3 segmentos exactos). Bloque de
GUIDs: siguiente libre en `PermisosCanonicos` (validar en PR-1; CxC usó
`0000000a-*`). **Cada alta requiere migration en `IdentidadDbContext`.**

| Permiso | Uso |
|---|---|
| `tesoreria.cuentas.ver` | Catálogo y saldos |
| `tesoreria.cuentas.administrar` | CRUD del catálogo de cuentas (alta/edición/toggle) [TES-7 revisada] |
| `tesoreria.movimientos.ver` / `tesoreria.movimientos.registrar` | Libro de movimientos |
| `tesoreria.movimientos.ver-cuenta-completa` | Des-enmascarar CLABE/cuenta |
| `tesoreria.pagos.aplicar` / `tesoreria.pagos.revertir` | Pago individual y reversa |
| `tesoreria.pagos-cuenta.registrar` / `tesoreria.pagos-cuenta.ligar` | §3.4 |
| `tesoreria.corridas.crear` / `tesoreria.corridas.autorizar` / `tesoreria.corridas.ejecutar` | §3.2 (autorizar además pasa por matriz) |
| `tesoreria.depositos.confirmar` / `tesoreria.depositos.rechazar` | §3.3 |
| `tesoreria.repp.registrar` | §3.6.b |
| `tesoreria.conciliacion.operar` / `tesoreria.conciliacion.cerrar` | §3.5 |
| `tesoreria.pasivos.solicitar-cancelacion` | Evento hacia CxP |
| `tesoreria.reportes.ver` | ADR-0036 |

---

## 11. Endpoints HTTP (resumen)

Base `/api/v1/tesoreria/` (ADR-0021), Problem Details (ADR-0010),
ETag/If-Match en agregados mutables (ADR-0012).

- `GET/POST cuentas` (lista + saldos; alta con Idempotency-Key), `PUT cuentas/{id}` (edición; CLABE write-only, `numero_cuenta` inmutable), `POST cuentas/{id}/activar|desactivar` — mutaciones con `X-Expected-Version` y permiso `tesoreria.cuentas.administrar` [TES-7 revisada]
- `GET pasivos-pendientes` (bandeja)
- `POST pagos` / `POST pagos/{id}/revertir`
- `POST pagos-cuenta` / `POST pagos-cuenta/{id}/ligar`
- `GET/POST corridas`, `POST corridas/{id}/enviar-autorizacion|autorizar|rechazar|ejecutar-linea|cerrar`, `GET corridas/{id}/oficio`
- `GET depositos` / `POST depositos/{id}/confirmar|rechazar`
- `GET/POST movimientos` (alta manual de ingreso; egresos solo vía pagos)
- `GET repp-pendientes` / `POST repp-recibidos`
- `GET/POST conciliaciones`, `POST conciliaciones/{id}/extracto` (upload), `POST .../matches`, `POST .../alta-asistida`, `POST .../cerrar`, `GET .../acta`
- `GET reportes/flujo-efectivo`, `GET reportes/auxiliar-bancos`

---

## 12. Frontend — patrones aplicables

Ver [`05-frontend-diseno.md`](05-frontend-diseno.md). Resumen: P2 (bandeja
filtrada server-side) para pasivos pendientes y depósitos; P1+P3 para
movimientos; master-detail para corridas y conciliaciones; Sheet para
"Registrar pago" / "Pago a cuenta" / "Registrar REPP"; inline forms para
líneas de corrida (nunca modal); pantalla específica de matching de
conciliación (dos columnas extracto↔movimientos, patrón del matching de
aplicación de pagos de CxC).

---

## 13. Dependencias de plataforma pendientes (ADR-0031)

| Pieza | ID | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| Candado de periodo | `<PeriodoContableCerrado>` | Stub siempre-abierto | Contabilidad implementa; swap del adapter |
| Asientos de movimientos | `<ContabilidadAsientos>` | Eventos sin consumidor (PR-11) | Contabilidad consume topic |
| Matriz de autorización compartida | `<MatrizAutorizacionCompartida>` | Por verificar en PR-5 | Contrato público de Compras o stub local |
| Listener de Facturación para `pago-cliente.confirmado` | `<PagoClienteConfirmado>` | Endpoint REPP manual sigue vivo | PR gemelo en Facturación |
| Listener espejo en Compras | `<TesoreriaEventListenerCompras>` | Log informativo | Wiring de Compras post PR-4 |
| Conector bancario vivo | `<IntegracionBanco>` | Ingesta por archivo | `Millet.Integraciones.<Banco>` tras `IExtractoFuente` |
| Validación fiscal REPP recibido | `<ValidacionReppRecibido>` | Registro manual | Servicio SAT post-MVP [T-G10] |
| `MetodoPago` en evento de pasivo | `<MetodoPagoEnPasivo>` | Columna nullable + captura manual opcional | Enriquecer evento CxP v1 (aditivo) o read-port [T-G11] |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión desde levantamiento v0.1. DDL movido aquí desde el borrador de mapa funcional; RN-2 respaldada con índice parcial único; puerto propio `IPeriodoContablePort`; `IMatrizAutorizacionPort` marcado por verificar. |
