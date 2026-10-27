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
| `facturacion.nota-credito.reembolso-solicitado.v1` | (misma subscription `facturacion-events` / `tesoreria-subscription`) | Crea `devolucion_cliente` de origen NC en `Solicitada`; idempotente por `EventoProcesado` y por `nota_credito_id` (índice único `ux_devolucion_cliente_nota_credito`) |

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

## Devoluciones de dinero a clientes (T9.6 · F1-TES-02)

Esta sección documenta lo ya construido en Tesorería para devolver dinero a un cliente. Código de referencia: `backend/src/Tesoreria/Domain/Devoluciones/DevolucionCliente.cs`, `backend/src/Tesoreria/Application/Devoluciones/*`, `backend/src/Tesoreria/Application/EventListeners/ProyectarReembolsoNotaCreditoCommand.cs`, `backend/src/Api/Endpoints/Tesoreria/DevolucionesEndpoints.cs` y `MovimientosEndpoints.cs`. Pruebas de integración de punta a punta: `backend/tests/Api.IntegrationTests/Tesoreria/DevolucionesClienteTests.cs`.

### Agregado y orígenes

El agregado `DevolucionCliente` (tabla `devolucion_cliente`) tiene dos orígenes:

- **Depósito erróneo** (`OrigenDevolucionCliente.DepositoErroneo`): un ingreso bancario que no corresponde a una cobranza. Se solicita desde el movimiento y exige banco, CLABE y titular del cliente desde el principio. El tope es el monto del depósito.
- **Nota de crédito** (`OrigenDevolucionCliente.NotaCredito`): Facturación pide devolver en dinero una NC timbrada. La solicitud nace en Tesorería sin cuenta destino; Tesorería la captura al editar. El tope es el total de la NC.

### Estados y transiciones

`Solicitada` (1) pasa a `Autorizada` (2) o `Rechazada` (3) o `Cancelada` (5); `Autorizada` pasa a `Pagada` (4). Solo una devolución `Solicitada` puede editarse, cancelarse, autorizarse o rechazarse (`DEV_NO_EDITABLE`, `DEV_NO_PENDIENTE`); solo una `Autorizada` puede pagarse (`DEV_NO_AUTORIZADA`). Todas las transiciones llevan `X-Expected-Version` (ADR-0012) e `Idempotency-Key` (ADR-0020), y las que cambian dinero comprometido se serializan con un advisory lock (`DevolucionLectura.LockKey`).

### Reglas

- **Tope (T9.6-a).** La suma de lo comprometido (estados `Solicitada`, `Autorizada` y `Pagada`) más la devolución nueva no puede exceder el origen. Se valida al solicitar, al editar (la propia solicitud no se cuenta dos veces) y de nuevo al pagar. Códigos: `DEV_IMPORTE_EXCEDE_DEPOSITO` y `DEV_IMPORTE_EXCEDE_NC`.
- **Segregación.** Quien solicita no autoriza (`DEV_MISMO_USUARIO`, 422). Para editar se exige ser el solicitante o tener el permiso de autorizar.
- **Depósito.** Debe ser un ingreso (`DEV_DEPOSITO_NO_INGRESO`), no una reversa (`DEV_DEPOSITO_ES_REVERSA`), no estar confirmado contra una propuesta de CxC (`DEV_DEPOSITO_CONFIRMADO`) y, si ya está identificado con un cliente, el cliente debe coincidir (`DEV_CLIENTE_DISTINTO_AL_DEPOSITO`). El cliente debe existir (`DEV_CLIENTE_NO_ENCONTRADO`). El cliente de una devolución de NC no puede cambiarse (`DEV_CLIENTE_NO_EDITABLE`).
- **Autorizar** exige destino completo (`DEV_DESTINO_INCOMPLETO`). Rechazar y cancelar exigen motivo (`DEV_MOTIVO_VACIO`).
- **Pago.** El período contable debe estar abierto (`MOV_PERIODO_CERRADO`) y quien paga confirma que verificó saldo en la banca (`fondosVerificados: true`; con `false` el endpoint responde 400).
  - *Depósito erróneo:* sale de la misma cuenta donde entró (`DEV_CUENTA_DISTINTA_AL_DEPOSITO`) y registra un **contramovimiento** egreso ligado al depósito (`ContramovimientoDe`, `MotivoReversa` con el motivo de la solicitud). El depósito original no se borra ni se modifica en monto: su estado de aplicación pasa a `Aplicado` y deja de poder confirmarse contra CxC.
  - *Nota de crédito:* requiere `cuentaBancariaId` (`DEV_CUENTA_REQUERIDA`) en la misma moneda (`DEV_CUENTA_OTRA_MONEDA`). Registra un egreso con beneficiario cliente ligado a la solicitud (`MovimientoPagoId`), sin `ContramovimientoDe`.
- **Idempotencia del reembolso de NC.** El consumidor crea la solicitud una sola vez: por `EventoProcesado` (mismo evento) y por el índice único `ux_devolucion_cliente_nota_credito` (misma NC en otro evento).

### Endpoints y permisos

| Endpoint | Permiso |
|---|---|
| `POST /api/v1/tesoreria/movimientos/{id}/solicitar-devolucion` | `tesoreria.devoluciones.solicitar` |
| `GET /api/v1/tesoreria/devoluciones`, `/conteos-por-estado`, `/{id}` | `tesoreria.devoluciones.ver` |
| `PUT /api/v1/tesoreria/devoluciones/{id}`, `POST .../{id}/cancelar` | `tesoreria.devoluciones.solicitar` |
| `POST .../{id}/autorizar`, `POST .../{id}/rechazar` | `tesoreria.devoluciones.autorizar` |
| `POST .../{id}/pagar` | `tesoreria.devoluciones.pagar` |
| `GET /api/v1/tesoreria/clientes-lookup` | `tesoreria.devoluciones.solicitar` |

Del lado de Facturación, `POST /api/v1/facturacion/notas-credito/{id}/solicitar-reembolso` (permiso `facturacion.notas-credito.reembolso-solicitar`) deja el evento en el Outbox de Facturación en la misma transacción que marca la NC.

### Sucursal (ADR-0051) y datos sensibles

El grupo de endpoints verifica la sucursal del documento con `DocumentoSucursalScope` (tipo `devolucion_cliente`, permisos `tesoreria.documentos.leer-todas-sucursales` o `gestionar-todas-sucursales`). Un usuario operativo solo ve y opera devoluciones de sus sucursales; el listado se filtra por las sucursales permitidas. La CLABE destino es PII: se devuelve enmascarada salvo con `tesoreria.movimientos.ver-cuenta-completa`.

### Decisiones y pendientes

- **Supuesto E11.** El evento `facturacion.nota-credito.reembolso-solicitado.v1` viaja por el topic `facturacion-events` y la subscription `tesoreria-subscription`. Está por validar entre Eliam y Uziel. Payload: `empresaId`, `ocurridoEn`, `notaCreditoId`, `folio`, `uuid`, `clienteId`, `receptorRfc` (Tesorería lo ignora), `importe`, `totalNotaCredito`, `moneda`, `sucursalId`, `motivo`, `solicitadoPor`.
- **Infraestructura pendiente de desplegar.** El filtro SQL de la subscription `tesoreria-subscription` sobre `facturacion-events` en `infra/modules/servicebus.bicep` ya incluye el nuevo `EventType`, pero el cambio no se ha desplegado. Antes de `create` hay que correr `what-if` (regla del repositorio). Sin el despliegue, el evento no llega a Tesorería en Azure.
- **NC ya registrada en el ERP.** Comentario de Eliam del 15-oct: la NC de "devolución de anticipo no aplicado" ya existe en el ERP cuando se pide el reembolso. La emisión de esa NC en Facturación queda abierta (I13.1, a cargo de Uziel).
- **Contradicción registrada, sin resolver.** `docs/modulos/facturacion/13-anticipos-ciclo-completo.md` [Decisión 13-K] dice que las NC siempre acreditan al saldo de la factura y nunca se reembolsan en efectivo. T9.6 sí devuelve dinero por NC. La implementación no cambia el saldo por cobrar de la factura al solicitar el reembolso. El owner debe reconciliar ambos textos.
- **Corridas de pago.** Incluir devoluciones autorizadas en una corrida queda fuera de alcance hasta TES-01 (las corridas no están construidas). Criterio C-16 ("puede entrar a una corrida"): fuera de alcance, solo de prueba.
- **Sin reversa.** Una devolución pagada no se revierte (por ejemplo, transferencia rechazada por el banco); requiere un flujo propio.
- **Sin evento hacia Contabilidad.** No se publica `tesoreria.devolucion-cliente.pagada.v1`; falta el contrato contable con Uziel.
- **Reapertura.** Reabrir el reembolso de una NC rechazada por Tesorería está fuera de alcance. La NC conserva la marca de reembolso solicitado.
- **Pagar ≠ autorizar (decisión del owner pendiente).** Hoy un mismo usuario puede autorizar y luego pagar una devolución si tiene ambos permisos. La segregación vigente es: quien solicita o edita por última vez no autoriza (`DEV_MISMO_USUARIO`). Imponer además que quien paga sea distinto de quien autoriza queda a decisión del owner; no está implementado.
- **Carrera depósito↔CxC (cerrada).** Confirmar un depósito contra CxC con una devolución Solicitada o Autorizada sobre ese ingreso se rechaza con `DEP_DEVOLUCION_EN_CURSO`; pagar la devolución de un depósito ya confirmado se rechaza con `DEV_DEPOSITO_CONFIRMADO`. Ambos flujos corren bajo el mismo advisory lock (`DevolucionLectura.LockKey`). El tope del depósito al pagar suma todo egreso con `ContramovimientoDe` igual al depósito, no solo devoluciones pagadas.
- **Rechazo o cancelación de la devolución de NC no libera la NC en Facturación (S3).** La NC conserva la marca de reembolso solicitado y no puede volver a pedirse. Pendiente de flujo de reapertura; no implementado.
- **CA6.17 «NC por anticipo no aplicado» no alcanzable (Q1).** Hoy ningún flujo emite esa NC hacia el reembolso. Por confirmar con Eliam; no implementado.
- **Cuenta de pago de la NC sin validar sucursal.** Al pagar el reembolso de una NC, Tesorería elige la cuenta bancaria sin comprobar que pertenezca a la sucursal de la devolución (solo se valida moneda). Pendiente; no implementado.

---

## Depósitos sin identificar y sobrante (F1-CXC-03 · C7.4)

- **Alta.** El ingreso se registra con la opción «Cobro de cliente (cliente
  opcional)» (`BeneficiarioTipo=Cliente`, `BeneficiarioRef` opcional). Sin
  remittance, CxC lo ve como «sin identificar» y no se aplica (CA7.9).
- **Puerto expuesto.** `DepositosPorIdentificarReadAdapter` implementa
  `IDepositosPorIdentificarReadPort` de CxC (registro en
  `AddTesoreriaModule`). Criterio: ingreso, no contramovimiento ni revertido,
  beneficiario Cliente, no aplicado por completo, sin devolución
  solicitada/autorizada/pagada y no ligado a caja o viáticos. Devuelve el
  disponible (monto − confirmado).
- **Preselección.** `propuesta-aplicacion.creada.v1` trae `MovimientoBancarioId`
  (aditivo). La proyección lo guarda en `deposito_confirmacion.movimiento_id`
  mientras está Pendiente (solo si el movimiento existe) y la respuesta lo
  expone como `movimientoSugeridoId` (`movimientoId` solo se llena al
  confirmar). El rechazo borra la sugerencia. Sin columna nueva.
- **Sobrante (corrección).** Confirmar ya no aplica el monto completo del
  movimiento: aplica lo propuesto (`MontoEsperado − SaldoAFavorPorIdentificar`).
  El resto deja el movimiento `AplicadoParcial` y vuelve a la bandeja de CxC.
  Una propuesta de CxC puede confirmarse contra un movimiento `AplicadoParcial`
  y se coteja contra lo disponible (no contra el monto total); si CxC identificó
  ese mismo movimiento, puede cubrir solo una parte. Caja y viáticos siguen
  ligando el movimiento completo. `pago-cliente.confirmado.v1` lleva en `Monto`
  el depósito propuesto (igual al del movimiento salvo identificación en
  partes). `DEP_MISMO_USUARIO` sin cambios (R12).
- **Bandeja para Tesorería.** Pestaña «Sin identificar» en Depósitos
  (`GET /api/v1/tesoreria/depositos/sin-identificar`, permiso existente
  `tesoreria.movimientos.ver`, alcance por sucursal ADR-0051). Reusa el criterio
  del puerto y descuenta las propuestas de CxC ligadas que esperan confirmación
  (proyección Pendiente con movimiento sugerido). Muestra «Días en espera».
- **E8.** Una confirmación con movimiento sugerido solo se confirma contra ese
  movimiento (`DEP_MOVIMIENTO_DISTINTO_AL_LIGADO`).
- **E9.** El sobrante de un ingreso sin cliente queda «sin identificar» del
  cliente de la última propuesta confirmada.
- **REP por propuesta.** Facturación crea un `repp_pendiente` por propuesta
  confirmada (migración `ReppPendientePorPropuesta`), de modo que el sobrante
  identificado después tiene su propio REP. `recibo-pago.timbrado.v1` trae
  `PropuestaId` (aditivo) y `MarcarReppTimbrado` marca exactamente esa
  confirmación; sin él conserva la correlación por movimiento.
- **PLATFORM-TODO(`<DepositoBancarioFeed>`):** la importación automática del
  banco (T9.2/T9.4) sustituirá la captura manual; el puerto no cambia.

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión desde levantamiento v0.1. DDL movido aquí desde el borrador de mapa funcional; RN-2 respaldada con índice parcial único; puerto propio `IPeriodoContablePort`; `IMatrizAutorizacionPort` marcado por verificar. |
