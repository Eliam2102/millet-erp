# Cajas — Adenda al módulo Facturación (alcance de datos + sesión de efectivo)

> **Proyecto:** ERP Millet — Módulo 3 del back-office (Facturación CFDI 4.0).
> **Versión:** 0.6 — adenda revisada; cierra los gaps de la v0.5 (borrador 2026-07-10).
> **Fecha:** 2026-07-10
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
> **Estado:** Diseño cerrado para implementación; decisiones D12-A..E pendientes de validación con Gerardo / key users (no bloquean PR1).
>
> **Documentos previos obligatorios:** [`00-levantamiento.md`](00-levantamiento.md)
> (actores §2, liquidación de caja §13.1, decisiones D1–D20) y
> [`01-diseno.md`](01-diseno.md) (modelo del dominio §4, permisos §10,
> endpoints §11). Esta adenda **no** renumera ni reescribe esos documentos;
> los extiende.

---

## 0. Cómo leer este documento

- `[Verificado]` — confirmado con el owner o leído del código actual.
- `[Decisión 12-N]` — decisión cerrada con el owner en la revisión de la v0.5 (2026-07-10).
- `[Decisión 12-X]` (letras) — decisión nueva introducida por este diseño; validar con Gerardo en la revisión del documento.

---

## 1. Objetivo — dos capas ortogonales

Introducir el concepto de **Caja** en Facturación con dos propósitos que
comparten la entidad pero tienen reglas independientes:

1. **Capa A — Alcance de datos.** Restringir visibilidad y acciones de los
   cajeros a los documentos (pedidos por facturar, facturas, anticipos,
   notas de crédito, REPPs) de su combinación **sucursal × canal de venta**.
2. **Capa B — Sesión de efectivo.** Formalizar apertura, operación y cierre
   de caja con arqueo. La cobranza de mostrador ocurre en Facturación
   (CxC instruye, caja ejecuta — D15 del levantamiento); por tanto el arqueo
   existe y es obligatorio.

Un error de diseño sería mezclarlas: la Capa A es configuración viva de
visibilidad; la Capa B es registro financiero inmutable.

---

## 2. Decisiones cerradas (revisión de la v0.5)

| # | Gap detectado | Decisión |
|---|---|---|
| `[Decisión 12-1]` | Apertura de caja ajena: el step-up auth inline es inviable (sistema passwordless vía Entra ID; no existe re-autenticación) | **Autorización previa consumible**: un usuario con `facturacion.caja.supervisar` crea, desde su propia sesión, una autorización "el cajero Y puede abrir la caja X" con vigencia corta (default 30 min, configurable) y un solo uso. El cajero la consume al abrir. Mismo patrón que `AutorizacionVentaActivo` (venta de activos fijos). Nunca contraseña compartida. |
| `[Decisión 12-2]` | El snapshot "conjunto de cajas por documento" de la v0.5 dejaba huérfanos permanentes (documento creado antes de configurar su caja jamás salía de "Sin asignar") y desalineaba el histórico en cada reconfiguración | **Resolución dinámica de la Capa A**: el alcance se evalúa como `(doc.sucursal_id, doc.canal_venta) ∈ alcance(caja)` contra la configuración **vigente** de cajas. Las dimensiones del documento ya están congeladas al crearse (sucursal y canal se snapshotean hoy); el mapeo caja↔dimensiones es configuración viva. `Comprobante.CajaId` queda reservado **exclusivamente** para la Capa B (la caja de la sesión que registró el cobro). |
| `[Decisión 12-3]` | Un cobro PUE de mostrador no es ninguna entidad hoy (el "pago" vive como forma de pago dentro de la factura); el `pago_id` de la v0.5 no tenía destino | **Nueva entidad `CobroMostrador`** que desacopla el cobro de la emisión. Referencia al `Comprobante` que lo documenta: `FacturaVenta`/`FacturaAnticipo` (PUE) o `ReciboPago` (PPD cobrado en mostrador). `caja_movimiento` referencia el cobro. |
| `[Decisión 12-4]` | "Canal comercial" era enum sin catálogo en la v0.5 | **Cerrado por código existente** (FAC-ING-PR2, #488): el catálogo `compartido.canales_venta` ya existe (PK smallint espejo del enum, campo `clave_aw`, CRUD en `/admin`); Facturación lo consume vía `ICanalesVentaReadPort`. `caja_canal` referencia ese catálogo. |
| `[Decisión 12-5]` | Ambigüedad entre §5 (solo efectivo genera movimiento) y §5.1 (corte por todas las formas) de la v0.5 | **Todas las formas de pago generan movimiento**: un `caja_movimiento` por cada `FormaPagoAplicada` del cobro. Solo efectivo (`01`) participa en el arqueo de diferencia; tarjeta/transferencia/cheque son informativos en el corte. El corte queda autocontenido en la sesión, sin joins contra comprobantes. |
| `[Decisión 12-6]` | El scope administrativo configurable (D3 de la v0.5) no estaba modelado | **Se modela en v1**: tabla `usuario_alcance` (usuario, sucursal opcional, canal opcional) para usuarios sin caja, administrada en la misma UI de cajas y evaluada por el mismo helper de alcance. |
| `[Decisión 12-7]` | Los cobros de reparto (chofer cobra en ruta; forma 99/PPD, REPP al liquidar — §7.3 del levantamiento) quedaban fuera del arqueo | **Sí entran a caja**: la liquidación de ruta la captura el cajero; cada cobro genera `CobroMostrador` con `origen = LiquidacionRuta` en su sesión. Todo el efectivo físico que entra a la caja cuadra en el arqueo. |
| `[Decisión 12-8]` | "Al día siguiente" sin definición de día (Millet opera en dos zonas horarias: Cancún UTC-5, Yucatán UTC-6) | El **día de operación** se calcula con la **zona horaria de la sucursal** de la sesión (nueva columna `zona_horaria` IANA en `compartido.sucursales`). Timestamps siguen en UTC. |
| `[Decisión 12-9]` | Anticipos cobrados en mostrador no estaban mencionados | Generan `CobroMostrador` + movimientos igual que una factura PUE (comprobante = `FacturaAnticipo`). Sin código especial. |
| `[Decisión 12-10]` | Herencia de caja en la NC de amortización (relaciona dos UUIDs) era ambigua | Las NC heredan **dimensiones** (sucursal/canal) — con resolución dinámica ya no heredan "cajas". La NC de amortización hereda de la **factura final** que la genera en la misma transacción. |
| `[Decisión 12-11]` | Reconciliación con la Liquidación de caja existente (`LiquidacionCajaQuery` agrupa por sucursal, no usa caja) | El permiso `facturacion.caja.liquidar` se **remapea** al cierre de sesión (arqueo); el reporte se redefine sobre `caja_sesion`/`caja_movimiento` conservando el shape ADR-0036. Roles que hoy tienen `liquidar` heredan el cierre de sesión. |
| `[Decisión 12-12]` | Cancelación de un pago con sesión origen cerrada: ¿a qué sesión va el ajuste si lo procesa un perfil administrativo sin sesión? | Ver `[Decisión 12-C]` (ajustes pendientes drenados en la próxima apertura). |

**Pendientes P1–P6 de la v0.5, cerrados:**

| # | Pendiente | Resolución |
|---|---|---|
| P1 | Planta de Pintura: ¿mostrador o transferencia? | Solo transferencia, sin caja física (alcance administrativo). Su canal propio ya existe en el catálogo. **Confirmar con Gerardo** (única consulta funcional abierta). |
| P2 | Múltiples sesiones por día (turnos) | Permitido sin restricción; el modelo sesión-por-apertura lo cubre. |
| P3 | Conteo por denominaciones vs. total | Monto total en v1; denominaciones como mejora posterior. |
| P4 | Retiros parciales durante el día | Permitidos como movimiento `Retiro`; requieren autorización consumible de supervisor (mismo mecanismo de `[Decisión 12-1]`) en v2 — en v1 basta el permiso `caja.operar` y quedan auditados. |
| P5 | Serie de folios por sucursal vs. caja | **Por sucursal** — coincide con la decisión A8 del 01-diseno (reuso de `Compartido.Series`, serie por sucursal-tipo). Cerrado, no era pendiente. |
| P6 | Cobros USD en mostrador | No en v1; caja solo MXN. USD exclusivamente por transferencia con alcance administrativo. |

---

## 3. Decisiones nuevas de este diseño (validar con Gerardo)

- **`[Decisión 12-A]` Sucursal de operación de la sesión.** Al abrir, la
  sesión declara una `sucursal_id` (∈ alcance de la caja). Su zona horaria
  define el día de operación del corte. Necesario porque la caja es N:M con
  sucursales pero el corte es por día local.
- **`[Decisión 12-B]` "Sin asignar" = combinación sin caja activa.** Los
  documentos cuya (sucursal, canal) no mapea a ninguna caja **activa** caen
  al bucket "Sin asignar", visible solo para `caja.leer-todas`, con badge y
  contador en las bandejas. Configurar la caja faltante vacía el bucket
  (consecuencia directa de la resolución dinámica). Los solapamientos están
  permitidos: dos cajas pueden compartir combinación y ambas ven los mismos
  documentos (D2 de la v0.5, sin cambio).
- **`[Decisión 12-C]` Ajustes pendientes drenados en apertura.** Una
  corrección sobre sesión cerrada (p. ej. cancelación de un cobro) procesada
  por un perfil sin sesión genera una fila en `caja_ajuste_pendiente` de la
  caja afectada; la **próxima apertura de esa caja** la drena automáticamente
  como movimientos `AjusteCorreccion` de la sesión nueva, con referencia al
  cobro original. Si quien procesa sí tiene sesión abierta, el ajuste entra
  directo a su sesión como `ReversaCobro`.
- **`[Decisión 12-D]` Canal de venta sube a la raíz `comprobante`.** Hoy
  `canal_venta` vive solo en `factura_venta`. Se eleva a la tabla base TPT
  (columna nullable + data migration 1:1 desde `factura_venta`, que la
  pierde) para que NC, REPP y anticipos hereden dimensiones y la Capa A se
  evalúe uniforme sobre la raíz. Comprobantes históricos sin canal quedan
  `NULL` → "Sin asignar" (aceptable: son pre-caja).
- **`[Decisión 12-E]` Emisión y cobro desacoplados.** Emitir (timbrar) y
  cobrar son dos comandos; la UI de mostrador los orquesta en secuencia. Una
  factura PUE timbrada sin cobro registrado aparece en la cola "por cobrar"
  del alcance del cajero — esto cubre fallos entre pasos (timbrado ok, cobro
  interrumpido) sin transacciones distribuidas.

---

## 4. Capa A — Alcance de datos

### 4.1 Modelo

- Una **Caja** se relaciona N:M con **sucursales** (`compartido.sucursales`,
  read-only), N:M con **canales de venta** (`compartido.canales_venta`,
  read-only) y N:M con **usuarios** (cajeros). Las relaciones se administran
  por UI **dentro de Facturación** — no son roles de I&A (ver §8).
- El alcance efectivo de una caja = producto cartesiano (sucursales ×
  canales). Caja con sucursales y sin canales → todos los canales de esas
  sucursales, y viceversa.
- El alcance de un **usuario** = unión de los alcances de sus cajas activas
  ∪ sus filas de `usuario_alcance` (perfil administrativo restringido,
  `[Decisión 12-6]`). Usuario con `facturacion.caja.leer-todas` → alcance
  total + bucket "Sin asignar".
- El alcance filtra **lectura y acción**: bandejas y detalle de pedidos por
  facturar, facturas, anticipos, NC y REPP; y las acciones emitir, cancelar
  CFDI, emitir NC y registrar cobros. El acceso directo por id fuera de
  alcance responde 404.
- Los **reportes** (`facturacion.reportes.leer`) quedan **fuera** de la
  Capa A en v1 (alcance administrativo). `[Decisión 12-N]` de esta adenda.

### 4.2 Escenarios sin origen A+W (sin cambio de fondo vs. v0.5)

- **Planta de Pintura:** canal propio en el catálogo, mapeable a cajas.
  Propuesta vigente: cobra solo por transferencia (sin caja física) — P1.
- **Venta de Activos Fijos y Facturación Administrativa:** operación
  exclusiva de alcance administrativo; no pasan por sesión de efectivo. Su
  cobranza es por transferencia y se registra sin movimiento de caja.

---

## 5. Capa B — Sesión de efectivo

### 5.1 Ciclo de vida

```
(sin sesión) ──abrir(fondo, sucursal[, autorización])──► ABIERTA ──iniciar arqueo──► EN_ARQUEO ──cerrar──► CERRADA
                                                            ▲                            │
                                                            └────────reabrir─────────────┘  (supervisor)
```

1. **Apertura** (`caja.operar`): el usuario relacionado a la caja declara el
   fondo de apertura y la sucursal de operación (`[Decisión 12-A]`). Si la
   caja es ajena, consume una autorización vigente (`[Decisión 12-1]`). La
   apertura **drena** los ajustes pendientes de la caja (`[Decisión 12-C]`)
   y genera el movimiento `FondoApertura`.
   - **Una caja no admite dos sesiones abiertas** (índice único parcial;
     conflicto → 409).
   - **Una sesión tiene un único responsable**: quien la abre responde el
     efectivo, aunque más usuarios estén relacionados a la caja.
2. **ABIERTA:** acepta movimientos. Los cobros del cajero generan
   movimientos automáticamente (§6); retiros y depósitos se capturan manual.
3. **EN_ARQUEO:** el sistema calcula el **esperado por forma de pago**
   (fondo + ingresos − retiros ± ajustes); el cajero captura el **contado
   físico de efectivo**. Diferencia = contado − esperado (solo efectivo,
   `[Decisión 12-5]`). Puede regresar a ABIERTA (reabrir, permiso
   `caja.supervisar`) si falta registrar algo.
4. **CERRADA:** inmutable — registro financiero. Montos, diferencia y
   desglose por forma de pago quedan congelados (`caja_sesion_corte`). No se
   reabre; correcciones posteriores = ajuste en la sesión siguiente con
   referencia (`[Decisión 12-C]`).

### 5.2 Sesión no cerrada de un día anterior

Si el cajero (o la caja) tiene una sesión con `dia_operacion` anterior al
día local vigente, el sistema **bloquea toda operación de cobro** hasta que
cierre esa sesión (cierre extemporáneo, marcado como tal). **No hay
auto-cierre**: el arqueo requiere conteo físico humano.

### 5.3 Apertura de caja ajena (cobertura de ausencias)

1. Un supervisor (`facturacion.caja.supervisar`) crea desde su propia
   sesión de trabajo una **autorización consumible**: caja, cajero
   beneficiario, motivo, vigencia (default 30 min). `[Decisión 12-1]`
2. El cajero abre la caja consumiendo la autorización; la sesión registra
   `autorizacion_apertura_id`.
3. **El alcance sigue a la caja, no al usuario**: durante esa sesión el
   cajero ve la sucursal/canal de la caja abierta. La responsabilidad del
   efectivo es del cajero que abrió; queda rastro del supervisor.
4. Auditoría completa: quién, cuándo, qué caja, qué supervisor, qué motivo.

---

## 6. Cobro de mostrador (`CobroMostrador`)

- **PUE mostrador:** la UI emite la factura (flujo actual, sin cambios en
  `EmitirFacturaVentaHandler`) y a continuación registra el cobro
  (`RegistrarCobroMostradorCommand`): valida sesión abierta + bloqueo de día
  anterior + comprobante timbrado dentro del alcance, crea el
  `CobroMostrador` con sus formas de pago, genera un `caja_movimiento` por
  forma (`[Decisión 12-5]`), y asigna `comprobante.CajaId = sesion.CajaId`
  (única vía de escritura de `CajaId`; el parámetro `CajaId` de
  `EmitirFacturaVentaCommand`/`EmitirReppCommand` se depreca).
- **Anticipos:** mismo comando con comprobante = `FacturaAnticipo`
  (`[Decisión 12-9]`).
- **PPD cobrado en mostrador:** el cajero emite el REPP (flujo actual) y
  registra el cobro con `comprobanteId = repp.Id`. Cuando exista Tesorería,
  los REPPs disparados por `PagoClienteConfirmadoEvent` (cobro bancario) NO
  generan cobro de mostrador — no pasan por caja.
- **Reparto:** liquidación de ruta = REPP + cobro con
  `origen = LiquidacionRuta` por cada factura de la ruta
  (`[Decisión 12-7]`). Endpoint batch en PR posterior; v1 opera con el
  comando unitario.
- **Cancelación de cobro** (`caja.supervisar`): con sesión abierta del
  ejecutor → movimiento `ReversaCobro` en su sesión; sin sesión →
  `caja_ajuste_pendiente` (`[Decisión 12-C]`). La sesión origen cerrada
  jamás se modifica.

---

## 7. Modelo de datos (esquema `facturacion`)

Convenciones: `BaseEntity` + `IAuditable`; sin FKs físicas cross-schema
(precedente `comprobante.sucursal_id`); referencias externas validadas vía
read ports; `EstatusCatalogo` para activación (patrón `SucursalDepartamento`).

```
caja                          -- agregado; IPerteneceAEmpresa (query filter global)
├── id uuid PK, empresa_id, nombre (UNIQUE empresa+nombre), descripcion?
├── estatus smallint (EstatusCatalogo), version (ETag)
│
├── caja_sucursal   (id, caja_id FK, sucursal_id;      UNIQUE caja+sucursal)
├── caja_canal      (id, caja_id FK, canal_venta_id smallint; UNIQUE caja+canal)
└── caja_usuario    (id, caja_id FK, usuario_id;       UNIQUE caja+usuario)

usuario_alcance               -- [Decisión 12-6]; concesiones para perfil administrativo
├── id, empresa_id, usuario_id req
├── sucursal_id uuid NULL (NULL = todas), canal_venta_id smallint NULL (NULL = todos)
└── UNIQUE (empresa_id, usuario_id, sucursal_id, canal_venta_id) NULLS NOT DISTINCT

caja_sesion                   -- agregado Capa B
├── id, empresa_id, caja_id, sucursal_id (operación, 12-A), responsable_usuario_id
├── estado smallint (Abierta=1, EnArqueo=2, Cerrada=3)
├── dia_operacion date (tz de la sucursal), fecha_apertura, fecha_cierre?
├── fondo_apertura numeric(18,2), autorizacion_apertura_id?
├── efectivo_declarado?, efectivo_teorico?, diferencia?   -- snapshot inmutable al cierre
├── cierre_extemporaneo bool, notas_cierre?, version
├── UNIQUE PARCIAL (caja_id) WHERE estado IN (1,2)        -- una sesión abierta por caja
└── caja_sesion_corte (id, caja_sesion_id FK, forma_pago char(2),
    monto_sistema, monto_declarado?;  UNIQUE sesión+forma)

caja_movimiento
├── id, empresa_id, caja_sesion_id FK req
├── tipo smallint (CobroCliente=1, Deposito=2, Retiro=3,
│                  AjusteCorreccion=4, ReversaCobro=5, FondoApertura=6)
├── forma_pago char(2) c_FormaPago, importe numeric(18,2) con signo, moneda char(3)
├── cobro_mostrador_id?, referencia?, descripcion, usuario_id
└── IX (caja_sesion_id), (cobro_mostrador_id)

cobro_mostrador               -- agregado [Decisión 12-3]
├── id, empresa_id, caja_sesion_id FK req, sucursal_id, canal_venta_id?
├── comprobante_id FK → comprobante (mismo esquema: FK física sí)
├── origen smallint (Mostrador=1, LiquidacionRuta=2)
├── estado smallint (Registrado=1, Cancelado=2)
├── total, moneda, fecha_cobro, usuario_cobrador_id, version
└── cobro_mostrador_forma_pago (id, cobro_id FK, forma_pago, importe,
    referencia?, cuenta_ordenante?, cuenta_beneficiaria?)  -- suma = total (invariante)

autorizacion_apertura_caja    -- [Decisión 12-1]; calco de AutorizacionVentaActivo
├── id, empresa_id, caja_id, cajero_usuario_id, supervisor_usuario_id
├── motivo, fecha_autorizacion, vigente_hasta (default 30 min, options)
├── estado smallint (Autorizada=1, Usada=2, Cancelada=3), caja_sesion_id?
└── IX (caja_id, cajero_usuario_id, estado)

caja_ajuste_pendiente         -- [Decisión 12-C]
├── id, empresa_id, caja_id, cobro_mostrador_id, importe con signo, forma_pago
└── motivo, creado_por, aplicado_en_sesion_id?
```

**Cambios a tablas existentes:**

- `compartido.sucursales` + `zona_horaria varchar(64)` IANA (default
  `America/Merida`; Cancún = `America/Cancun`) — migration Compartido +
  campo en el CRUD admin. `[Decisión 12-8]`
- `facturacion.comprobante` + `canal_venta smallint NULL` con data migration
  desde `factura_venta` (que pierde la columna). `[Decisión 12-D]`
- `Comprobante.AsignarCajaCobro(cajaId)` — única vía de escritura de
  `CajaId`.

---

## 8. RBAC y separación de capas

Dos capas que **no se mezclan en implementación** (sin cambio vs. v0.5):

| Capa | Mecanismo | Dónde vive |
|---|---|---|
| Acceso al módulo y permisos de acción | RBAC estándar (permisos canónicos) | Identidad (transversal) |
| Alcance de datos y asignación de cajas | `caja_usuario` / `caja_sucursal` / `caja_canal` / `usuario_alcance` | Facturación |

**Excepción declarada al ADR-0034:** el CRUD de Cajas y sus relaciones es
configuración **operativa** del módulo y su UI vive dentro de Facturación,
no en Administración (a diferencia de Sucursal↔Departamento, que es catálogo
organizacional y sí vive en `/admin`). Sucursales y canales se consumen
read-only del catálogo, conforme al patrón general.

**Permisos canónicos** (namespace `00000009-0009-*`; requieren migration
`HasData` en `IdentidadDbContext`):

| GUID sufijo | Código | Uso |
|---|---|---|
| `...0001` (existente) | `facturacion.caja.liquidar` | Cerrar sesiones de caja (arqueo/liquidación) — descripción remapeada `[Decisión 12-11]` |
| `...0002` | `facturacion.caja.administrar` | CRUD de cajas, alcances y usuario-alcances |
| `...0003` | `facturacion.caja.operar` | Abrir sesión propia, registrar cobros y movimientos, iniciar arqueo |
| `...0004` | `facturacion.caja.supervisar` | Autorizar apertura ajena, reabrir sesión en arqueo, cancelar cobros |
| `...0005` | `facturacion.caja.leer-todas` | Alcance administrativo Capa A + bucket "Sin asignar" |

---

## 9. Piezas técnicas de la Capa A

- **`ICurrentUserPermissions`** (nuevo, `SharedKernel/Application`;
  implementación en `Api/Auth` sobre `IPermissionCache`/`IPermissionLoader`,
  dual-path usuario/service-principal): permite a los query handlers
  consultar permisos efectivos. Primera implementación real de scoping fino
  en handler del ERP; pieza reutilizable (p. ej. el `leer-propias` de
  Almacén, hoy solo seed).
- **`IAlcanceCajaEvaluator`** (`Facturacion/Application/Cajas/Alcance`):
  resuelve `Total | Combinaciones(set (sucursalId, canalId?)) | Ninguno` y
  expone `AplicarA(IQueryable<T>)` (predicate-builder con OR de pares) y
  `PredicadoSinAsignar()`. La regla vive una sola vez; cada handler hace
  `q = _alcance.AplicarA(q)`.
- **Queries afectadas:** bandeja/detalle de pedidos facturables, facturas,
  REPP, anticipos y NC. Bandejas exponen `sinAsignarCount` y aceptan
  `?alcance=sin-asignar` (solo `leer-todas`).

---

## 10. Endpoints (`/api/v1/facturacion/...`)

RBAC vía permisos canónicos; ETag/If-Match (ADR-0012) en detalle/mutaciones;
Idempotency-Key (ADR-0020) en POSTs mutantes.

| Endpoint | Permiso |
|---|---|
| `GET/POST /cajas`, `GET/PUT /cajas/{id}` | administrar |
| `PUT /cajas/{id}/sucursales` · `/canales` · `/usuarios` (replace-set) | administrar |
| `GET/PUT /cajas/usuario-alcances` | administrar |
| `POST /cajas/{id}/autorizaciones-apertura` | supervisar |
| `GET /cajas/sesion-actual` | operar |
| `POST /cajas/{id}/sesiones` (apertura) | operar |
| `POST /cajas/sesiones/{id}/movimientos` | operar |
| `POST /cajas/sesiones/{id}/arqueo` | operar |
| `POST /cajas/sesiones/{id}/cierre` | liquidar |
| `POST /cajas/sesiones/{id}/reabrir` | supervisar |
| `GET /cajas/{id}/sesiones`, `GET /cajas/sesiones/{id}` | supervisar ∨ administrar |
| `POST /cobros`, `GET /cobros?sesionId=` | operar |
| `POST /cobros/{id}/cancelar` | supervisar |

**Eventos outbox** (convención `{Agregado}{Verbo}Event`, topic
`facturacion-events`): `CajaSesionAbiertaEvent`, `CajaSesionCerradaEvent`
(totales por forma + diferencia; consumidores futuros: Contabilidad,
Tesorería), `CobroMostradorRegistradoEvent`, `CobroMostradorCanceladoEvent`.

---

## 11. Impacto en módulos vecinos

- **Ingesta A+W:** cero impacto — el catálogo de canales ya existe y la
  vista on-prem `vw_erp_pedido_cabecera` no cambia. El gap G6 (valores
  `GRUPPE` sin mapear) se mitiga por datos: completar `clave_aw` en el
  admin de canales; los pedidos con canal no resuelto ya caen a excepciones
  de importación, no al bucket "Sin asignar".
- **CxC:** sin cambio de contrato — sigue instruyendo aplicación de
  anticipos (D15); caja ejecuta.
- **Tesorería (futuro):** `CajaSesionCerradaEvent` queda publicado para
  conciliación de depósitos; los REPPs de origen bancario no pasan por caja.
- **Contabilidad (futuro):** el cierre de sesión con diferencia
  (sobrante/faltante) genera evento con el desglose para la póliza
  correspondiente.

## 12. Dependencias de plataforma pendientes (ADR-0031)

| Pieza | Ticket | NoOp/stub en uso | Cómo se wirea |
|---|---|---|---|
| `EmpresaId` en `compartido.sucursales` (invariante caja↔sucursales de la misma empresa es latente: catálogo cross-empresa, una empresa activa hoy) | `<SucursalEmpresaId>` | Validación omitida con `PLATFORM-TODO(<SucursalEmpresaId>)` | Al agregar `EmpresaId` a Sucursal, activar validación en `Caja.AsignarSucursal` |
| Consumidor Tesorería de `CajaSesionCerradaEvent` | `<TesoreriaCajaSesion>` | Evento publicado sin consumidor | Cuando exista el módulo Tesorería |

## 13. Plan de PRs

| PR | Rama | Contenido |
|---|---|---|
| PR0 | `facturacion/cajas-pr0-adenda` | Este documento + toques a 01-diseno.md |
| PR1 | `facturacion/cajas-pr1-entidad-caja-permisos` | Domain Cajas (Caja + relaciones + UsuarioAlcance), configurations, migration Facturación, permisos + migration Identidad, Application CRUD/alcances, `CajasEndpoints` (solo CRUD), tests |
| PR2 | `facturacion/cajas-pr2-capa-a-alcance` | Lift-up canal a `comprobante` (12-D), `ICurrentUserPermissions`, `IAlcanceCajaEvaluator`, aplicación a queries, bucket Sin asignar |
| PR3 | `facturacion/cajas-pr3-sesiones` | `Sucursal.ZonaHoraria`, CajaSesion/Corte/Movimiento/Autorización/AjustePendiente, comandos FSM, bloqueo día anterior, endpoints, eventos |
| PR4 | `facturacion/cajas-pr4-cobro-mostrador` | CobroMostrador, registrar/cancelar, `AsignarCajaCobro`, deprecación CajaId en commands, LiquidacionCajaQuery sobre sesiones |
| PR5 | `facturacion-fe/cajas-pr5-admin` | Master-detail `/facturacion/cajas`, sheet, inline forms (patrones P1/P3/P4) |
| PR6 | `facturacion-fe/cajas-pr6-operacion` | Panel "Mi caja", flujo emitir→cobrar, badge Sin asignar, apertura ajena, liquidación de ruta |
| PR7 | `facturacion/cajas-pr7-hardening` | Batch liquidación de ruta, UI ajustes pendientes, telemetría (foldable) |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.5 | 2026-07-10 | Borrador inicial (chat, no versionado): concepto de caja, capas A/B, D1–D3, P1–P6 |
| 0.6 | 2026-07-10 | Revisión contra código y docs: 12 gaps cerrados (`[Decisión 12-1..12]`), decisiones de diseño 12-A..E, P1–P6 resueltos, modelo de datos, permisos, endpoints y plan de PRs |
