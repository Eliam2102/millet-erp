# Levantamiento — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> **Proyecto:** ERP Millet — Módulo de Tesorería (Bancos)
> **Versión:** 0.1
> **Fecha:** 2026-07-14
> **Autoridad funcional:** Javier Humberto Canche Chan (Jefe de Tesorería)
>
> **Origen:** `plantilla_levantamiento_modulo_Tesoreria.docx` (05-may-2026) +
> digest de acoplamiento sobre el repo (2026-07-14): contratos CxP↔Tesorería
> F9-PR1, patrón propuesta/confirmación de CxC, puertos stub de Contabilidad,
> ADR-0036 de reportería.
>
> **Estado:** borrador para confirmación por owners. Las 9 decisiones
> estructurales (TES-1…TES-9, §10) fueron aprobadas por Eduardo Paredes el
> 2026-07-14. Los `[Pendiente — área]` requieren confirmación de Javier
> Canche, fiscal o CxP; están consolidados en §11.
>
> **Patrón:** sigue los exemplares de
> [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md)
> y [`docs/modulos/almacen/00-levantamiento.md`](../almacen/00-levantamiento.md).
> Hereda decisiones transversales del módulo Compras (hexagonal + CQRS,
> multi-DbContext por ADR-0030, Outbox por ADR-0009, Idempotency-Key por
> ADR-0020, versionado `/api/v1/` por ADR-0021, RBAC granular por ADR-0007,
> Problem Details por ADR-0010, ETag por ADR-0012, PLATFORM-TODO por
> ADR-0031, dinero/multimoneda por ADR-0014, reportería por ADR-0036).

---

## 0. Cómo leer este documento

- `[Verificado — área]` — tomado de la plantilla de levantamiento del área o confirmado en sesión con Tesorería.
- `[Verificado — repo]` — leído directamente del código/docs del monorepo (contrato ya implementado); se cita ruta.
- `[Inferido]` — deducido por convenciones o documentación parcial; no confirmado.
- `[Gap]` — agujero de conocimiento que requiere confirmación; consolidados en §11.
- `[Pendiente — área]` — respuesta del área aún no recibida.

> **Sobre las sugerencias del diseñador.** Las secciones marcadas con
> `> **Pendiente — sugerencia:**` proponen una respuesta plausible en lugar
> de dejar el punto en blanco. El responsable del área confirma o ajusta
> cada una antes de pasar a `01-diseno.md`.

---

## 1. Propósito y alcance

### Qué hace este módulo

Tesorería posee **el hecho bancario** de la empresa, en ambos sentidos
[TES-9]: todo peso que entra o sale de una cuenta bancaria de Millet pasa
por este módulo. Administra el catálogo de cuentas bancarias propias, el
libro de movimientos bancarios, la ejecución de pagos a proveedor (contra
pasivos ya autorizados por CxP), la corrida de pagos con oficio de cartera
autorizable, la confirmación de cobros de cliente (absorbe el rol del área
de "Ingresos"), el ciclo del pago a cuenta (dinero que salió sin documento),
la conciliación bancaria y el registro del REPP recibido del proveedor.

**Encuadre respecto a SAP B1:** el módulo es más delgado que "Gestión de
Bancos" de SAP — cuatro de sus cinco responsabilidades ya tienen dueño en
la arquitectura del ERP — y más ancho por otro lado: lo que SAP no da
(conciliación ágil, banca ligada, corrida autorizable dentro del sistema)
sí es de este módulo. `[Verificado — área]`

### Qué NO hace (anti-alcance explícito)

| Función que hoy hace el área en SAP | Dueño en el TO-BE |
|---|---|
| Elegir qué facturas pagar / matching factura-pago | **CxP** (pay-gate + autorización; flujo F4) |
| Emisión de REPP propio (cobros PPD de Millet) | **Facturación** (`EmitirReppCommand`; read models `FacturasCobrablesPpdQuery` y `BandejaReppQuery` ya existen) `[Verificado — repo]` |
| Asientos contables manuales | **Contabilidad** (futura); interín el GL sigue en SAP [TES-5] |
| Reconciliación interna BP / cuenta de mayor | **Contabilidad** (futura); interín en SAP |
| Control de periodos cerrados | Puerto `IPeriodoContablePort.EstaAbiertoAsync` (patrón ya elegido en Facturación; hoy stub siempre-abierto, `PLATFORM-TODO(<PeriodoContableCerrado>)`) `[Verificado — repo]` |
| Conector de banca en línea | `Millet.Integraciones.<Banco>` — fuera del módulo, detrás de la costura `IExtractoFuente` [TES-3]; el sub-namespace `Integraciones` está reservado para bancos (CLAUDE.md) |
| Validaciones del pay-gate (evidencia, tolerancias, NC pendientes) | **CxP** (ya implementadas o en F4); ver RN-9 |
| Emisión de cualquier CFDI | **Facturación**; Tesorería confirma el hecho bancario que dispara la emisión |

### Boundary con otros módulos

| Módulo | Qué le pide Tesorería | Qué le entrega Tesorería |
|---|---|---|
| **CxP** | Pasivos autorizados para pago (`pasivo.autorizado-para-pago.v1`) | Pagos aplicados/revertidos, REPP de proveedor recibido, solicitud de cancelación de pasivo (4 eventos `tesoreria.*.v1`, contrato congelado §1.4) |
| **CxC** | Propuestas de aplicación de depósito (`propuesta-aplicacion.creada.v1`) | Confirmación del hecho bancario (`pago-cliente.confirmado.v1` → dispara REPP en Facturación → CxC aplica a cartera al consumir el timbrado) |
| **Facturación** | Cierres de sesión de caja (expectativa de depósito) y timbrados de REPP | El disparo de emisión de REPP vía `PagoClienteConfirmadoEvent` |
| **Compras** | — | Los mismos eventos de pago (listener espejo `PLATFORM-TODO(<TesoreriaEventListenerCompras>)` se desbloquea al existir este publisher) `[Verificado — repo]` |
| **Contabilidad** (futura) | Mapeo concepto → cuenta contable; candado de periodo | Eventos de contabilización de movimientos bancarios al outbox (replay futuro) [TES-5] |
| **Administración / DatosMaestros** | Datos bancarios del proveedor (CLABE/banco, read-port nuevo), masters de Proveedor/Cliente para nombres (ADR-0042) | — |
| **Identidad** | Roles y permisos canónicos `tesoreria.*` (ADR-0007) | — |
| **Bancos** (externos) | Estados de cuenta (MVP: archivo por perfil; post-MVP: conector vivo) | — |

### 1.4 Contratos congelados que este módulo NO rediseña

El contrato CxP↔Tesorería quedó **congelado desde el lado CxP en F9-PR1**:
el worker `TesoreriaEventListenerWorker` ya está desplegado
(`backend/src/CuentasPorPagar/Infrastructure/Workers/TesoreriaEventListenerWorker.cs`,
topic `tesoreria-events`, subscription `cuentas-por-pagar-tesoreria-sub`) y
los payloads espejo viven en
`backend/src/CuentasPorPagar/Application/EventListeners/ContratosEspejo.cs:97-141`.
Construir Tesorería = convertirse en la contraparte real de ese contrato.
Se citan textuales para que ningún PR los "mejore". `[Verificado — repo]`

**Tesorería CONSUME (lado egresos):**

| EventType (wire) | Topic | Payload |
|---|---|---|
| `cuentas_por_pagar.pasivo.autorizado-para-pago.v1` | `cuentas-por-pagar-events` | `EmpresaId, OcurridoEn, FacturaProveedorId, ProveedorId, OrdenCompraId?, MontoTotal, SaldoPendiente, Moneda, TipoCambio?, FechaVencimiento, UuidCfdi?, FolioProveedor?` |

> El payload **no** trae datos bancarios del proveedor
> (`PLATFORM-TODO(PayloadEnriquecido)` en
> `PasivoAutorizadoParaPagoIntegrationEvent.cs:18-20`): Tesorería los
> resuelve por read-port propio a DatosMaestros (CLABE como VO con masking
> PII, ADR-0006/ADR-0018). Ver T-G1. Tampoco trae `MetodoPago` (PUE/PPD);
> ver T-G11. `SaldoPendiente` ya viene neto de anticipos y NC aplicadas.

**Tesorería PUBLICA (topic `tesoreria-events`, ya provisionado en
`infra/modules/servicebus.bicep`):**

| EventType (wire) | Payload (además de `EmpresaId, OcurridoEn`) | Efecto en CxP |
|---|---|---|
| `tesoreria.pago-factura-proveedor.aplicado.v1` | `FacturaProveedorId, PagoId, Monto, Moneda, FechaPago, MetodoPago?, ReferenciaBancaria?` | `RegistrarPago()` — pasa a `Pagada` solo con saldo 0 |
| `tesoreria.pago-factura-proveedor.revertido.v1` | `FacturaProveedorId, PagoOriginalId, MontoRevertido, Moneda, FechaReversa, Motivo` | `RevertirPago()` — regresa a `Autorizada` si saldo > 0 |
| `tesoreria.repp-proveedor.recibido.v1` | `FacturaProveedorId, UuidComplementoPago, FechaComplemento` | `MarcarReppRecibido()` — libera motivo de revisión `FALTA_REPP` |
| `tesoreria.cancelacion-pasivo.solicitada.v1` | `FacturaProveedorId, UsuarioSolicitanteId, Motivo` | `EnviarARevision()` con motivo "Tesorería solicita cancelar" |

**Tesorería PUBLICA (nuevo, lado ingresos — cierra el TODO de CxC):**

| Evento (canónico / wire) | Consumidor | Efecto |
|---|---|---|
| `PagoClienteConfirmadoEvent` / `tesoreria.pago-cliente.confirmado.v1` | Facturación (listener nuevo que invoca `EmitirReppCommand`; hoy `POST /api/v1/facturacion/repp` es manual) | Dispara la emisión del REPP; CxC consume el timbrado y aplica a cartera. Cierra `PLATFORM-TODO(<PagoClienteConfirmado>)` [TES-9] |

**Tesorería CONSUME (lado ingresos):**

| EventType (wire) | Topic | Uso |
|---|---|---|
| `cuentas_por_cobrar.propuesta-aplicacion.creada.v1` | `cuentas-por-cobrar-events` | Bandeja de propuestas de aplicación de depósito (CxC propone, Tesorería confirma) `[Verificado — repo: CuentasPorCobrarIntegrationEvents.cs:23-33]` |
| `facturacion.caja-sesion.cerrada.v1` | `facturacion-events` | Expectativa de depósito de Caja — resuelve `PLATFORM-TODO(<TesoreriaCajaSesion>)` `[Verificado — repo: FacturacionIntegrationEvents.cs:107-110]` |
| `facturacion.recibo-pago.timbrado.v1` | `facturacion-events` | Cierre del ciclo: marca el movimiento de ingreso como fiscalmente cubierto |

> **Naming de eventos:** convención canónica `{Agregado}{Verbo}Event` en
> docs y wire `{contexto}.{recurso}.{accion}.vN` vía Outbox (ADR-0009),
> igual que el resto de la triada. Los nombres exactos de subscriptions
> nuevas se fijan en `01-diseno.md` siguiendo el patrón existente del
> `servicebus.bicep` (`{consumidor}-…-sub` / `{consumidor}-subscription`).
> `[Inferido]`

---

## 2. Actores

| Actor | Rol en este módulo |
|---|---|
| **Jefe de Tesorería** (Javier Canche) | Autoriza corridas de pago (matriz), autoriza pagos a cuenta, cierra conciliaciones, confirma ligas tardías con diferencia de monto. |
| **Auxiliar de Tesorería** | Registra movimientos bancarios, arma corridas, ejecuta pagos en banca y captura referencias, propone ligas tardías, registra REPP recibidos, opera el matching de conciliación. |
| **Tesorería como "Ingresos"** [TES-9] | Confirma depósitos de cliente contra propuestas de CxC. En el AS-IS esta función estaba dispersa (Ingresos revisa banca, CxC propone, nadie confirma en sistema). |
| **Dirección de Finanzas / Dirección General** | Escalamiento en la matriz de autorización de corridas por monto (umbral por confirmar, T-G4). `[Pendiente — área]` |
| **CxP (área)** | No opera en este módulo; recibe los efectos vía eventos. Consulta el read model de pagos a cuenta abiertos para provisionar. |

> **Pendiente — sugerencia (T-G9):** la plantilla no trae volumetría (§9
> vacía). Del AS-IS se deriva ~26 pagos/día a 3 min/pago y 5-10 pagos a
> cuenta/semana — carga trivial para NFRs. Propongo asumir 2 usuarios
> concurrentes (Jefe + Auxiliar); Javier confirma.

---

## 3. Procesos (TO-BE)

### 3.1 Ejecución de pago a proveedor (individual) [TES-1]

**AS-IS:** el Jefe/Auxiliar arma el pago completo en SAP (Pagos
efectuados): elige proveedor, selecciona facturas, cuadra importe, elige
banco, contabiliza. ~3 min por pago, 26/día. El mismo actor decide y
ejecuta. `[Verificado — área]`

**TO-BE:** el pasivo llega **ya decidido** desde CxP:

1. El evento `pasivo.autorizado-para-pago.v1` se proyecta a la bandeja de
   pagos pendientes (proyección local, listener idempotente vía
   `EventoProcesado` — mismo patrón que los listeners de CxP/CxC).
2. Los datos bancarios del proveedor se resuelven por read-port a
   DatosMaestros (CLABE enmascarada en UI salvo permiso explícito).
3. El operador ejecuta la transferencia **en la banca** (fuera del sistema
   en MVP) y registra el `MovimientoBancario` de egreso con referencia
   bancaria, ligándolo al pasivo.
4. Al confirmar, se publica `tesoreria.pago-factura-proveedor.aplicado.v1`
   → CxP ejecuta `RegistrarPago()`.
5. Reversa: `revertido.v1` → `RevertirPago()`. La reversa no borra el
   movimiento: genera contramovimiento ligado (RN-10).

**Regla estructural:** un pago puede cubrir N pasivos y un pasivo puede
cubrirse en M pagos, pero el evento `aplicado` se emite **por factura**
(así lo espera el contrato espejo de CxP). El `MovimientoBancario` es 1;
las `AplicacionPagoProveedor` son N. (RN-4)

> **Pendiente — sugerencia (T-G6):** moneda distinta entre pasivo y cuenta.
> El AS-IS lo prohíbe ("no se paga factura con diferente moneda"). Propongo
> conservar la regla en MVP (RN-3): la cuenta de egreso debe coincidir en
> moneda con el pasivo; el cruce USD↔MXN con TC queda post-MVP. Javier
> confirma que no existe hoy ningún caso operativo cross-moneda.

### 3.2 Corrida de pagos y oficio de cartera [TES-8]

**AS-IS:** los oficios de cartera se arman manualmente; el área pide "que
el sistema pueda generar algún reporte o documento que pueda ser autorizado
a través del sistema para pagar". `[Verificado — área]`

**TO-BE:** hay **dos autorizaciones distintas** que no se mezclan:

1. **CxP autoriza el pasivo** (es pagable) — ya cubierto por su flujo.
2. **Tesorería autoriza la corrida** (desembolsar estos N pasivos ahora,
   desde esta cuenta) — nueva, vía la **matriz de autorización
   reutilizable** de Requisiciones/Compras.

Flujo:

1. El Auxiliar arma la corrida seleccionando pasivos de la bandeja
   (filtros: vencimiento, proveedor, moneda, monto).
2. La corrida entra al flujo de autorización (Jefe de Tesorería /
   suplencia según matriz).
3. El **oficio de cartera** se genera como artefacto de la corrida
   autorizada (`<ReporteShell>`, PDF — ADR-0036).
4. Al ejecutarse (pagos hechos en banca, referencias capturadas), se emite
   `aplicado.v1` por cada factura cubierta.
5. Estados: `Borrador → EnAutorizacion → Autorizada → Ejecutada` (parcial
   permitido) `→ Cerrada`; `Rechazada` y `Cancelada` terminales desde los
   dos primeros. (RN-5: solo se ejecuta en `Autorizada`.)

> **Pendiente — sugerencia (T-G4):** la plantilla §4 solo dice "Jefe de
> Tesorería, N/A montos". Propongo seed: cualquier monto → Jefe de
> Tesorería, suplente por definir; escalamiento por monto (p. ej.
> > $500k MXN → Dirección) configurable en la matriz. Javier confirma
> umbrales y suplente.

### 3.3 Confirmación de cobros de cliente [TES-9]

**AS-IS:** disperso — Ingresos revisa la banca; CxC propone matching; nadie
confirma dentro del sistema. `[Verificado — área]`

**TO-BE:** Tesorería es el confirmador. Réplica exacta del patrón
propuesta/confirmación ya implementado en CxC (interino A2):

1. CxC publica `propuesta-aplicacion.creada.v1` (depósito ↔ facturas
   propuestas). Tesorería la proyecta a su bandeja de depósitos por
   confirmar.
2. El operador coteja contra el `MovimientoBancario` de ingreso (o lo
   registra si aún no existe; con conciliación activa, el extracto lo
   trae).
3. Confirmar = ligar movimiento ↔ propuesta y publicar
   `tesoreria.pago-cliente.confirmado.v1` con: `EmpresaId, OcurridoEn,
   PropuestaId?, ClienteId, MovimientoBancarioId, CuentaBancariaId, Monto,
   Moneda, FechaValor, Referencia, Facturas[] (FacturaVentaId,
   ImporteAplicado)`. (RN-6: solo desde movimiento de ingreso
   identificado.)
4. Facturación consume → emite el REPP → `recibo-pago.timbrado.v1` → CxC
   aplica a cartera. Tesorería consume el timbrado para marcar el
   movimiento como fiscalmente cubierto.
5. Depósitos de Caja: `caja-sesion.cerrada.v1` alimenta la expectativa de
   depósito (sesión cerrada con efectivo X → debe aparecer un depósito ~X);
   el match cierra el ciclo Caja→Banco.

**Nota de contrato:** la aplicación real a cartera en CxC ocurre **al
consumir el timbrado**, no al confirmar (`[Verificado — repo]`:
`ReciboPagoTimbradoCommand` en CxC). Tesorería confirma el hecho bancario,
no el fiscal. Si el timbrado falla, el movimiento queda
confirmado-no-timbrado y aparece en el read model de control (el retry de
timbrado es de Facturación, patrón existente).

> **Pendiente — sugerencia (T-G7):** rechazo de propuesta. Propongo que
> Tesorería pueda rechazar con motivo (depósito no aparece, monto no
> coincide) → CxC re-propone; evento `tesoreria.propuesta-aplicacion.rechazada.v1`.
> El agregado `PropuestaAplicacionPago` de CxC ya contempla estado
> `Rechazada` `[Verificado — repo]`; validar con CxP/CxC que el rechazo
> regresa el depósito a su bandeja.

### 3.4 Pago a cuenta / movimiento no ligado [TES-2]

**AS-IS (única variante documentada en la plantilla §3):** pago urgente sin
factura; se ejecuta en banca y **no se registra en SAP** hasta que CxP
provisiona. Riesgo #1 del área: distorsiona impuestos al cierre; mitigación
actual = presión manual entre áreas. 5-10 casos/semana. `[Verificado — área]`

**TO-BE — el limbo se registra, no se oculta:**

1. El pago urgente **sí** se captura: `MovimientoBancario` de egreso con
   `EstadoAplicacion = NoAplicado`, beneficiario (proveedor si se conoce),
   motivo y autorización del Jefe de Tesorería (gate propio, matriz).
2. **Regla de política (RN-2):** máximo **un** pago no aplicado abierto por
   proveedor. Un segundo pago a cuenta al mismo proveedor se bloquea hasta
   ligar el primero. (Traducción de "no se aceptan más de un pago sin
   factura" del AS-IS.)
3. Cuando CxP provisiona y autoriza la factura, llega
   `pasivo.autorizado-para-pago.v1`; Tesorería hace la **reconciliación
   tardía**: liga el movimiento preexistente al pasivo y emite
   `aplicado.v1` **sin re-desembolsar**.
4. Read model de control: pagos a cuenta abiertos con antigüedad —
   reemplaza el reporte semanal manual y da a CxP la lista exacta de
   provisiones pendientes.
5. Al cierre de periodo, los no-aplicados abiertos se reportan
   explícitamente (insumo para fiscal; el candado duro llega con
   `IPeriodoContablePort`, RN-8).

> **Pendiente — sugerencia:** ¿quién liga tardíamente? Propongo: el
> Auxiliar propone la liga; el Jefe confirma solo si el monto difiere del
> pasivo (diferencias por comisiones se resuelven en conciliación, no
> aquí). Javier confirma.

### 3.5 Conciliación bancaria [TES-3]

**AS-IS:** mensual, 2-3 horas, manual/semiautomática en SAP con carga de
extracto; el área pide "banca ligada al sistema" y "conciliación más
ágil". `[Verificado — área]`

**TO-BE — dos capas, una costura:**

**Capa MVP — ingesta normalizada + matching asistido:**

1. **Ingesta:** carga del estado de cuenta por archivo, parseado por
   **perfil de banco** — se replica el patrón `PerfilParserBanco` del TC
   empresarial de CxP (`docs/modulos/cuentas-por-pagar/01a-anexo-tc-empresarial.md`;
   AMEX_MX/BANAMEX/BBVA_MX existen como referencia de diseño; los perfiles
   de cuentas operativas son nuevos). Cada línea del extracto →
   `ExtractoLinea` normalizada.
2. **Matching:** motor de emparejamiento `ExtractoLinea ↔
   MovimientoBancario` con reglas ponderables (referencia exacta >
   monto+fecha exactos > monto con tolerancia de comisión). Tres desenlaces
   por línea: match automático (sugerido, confirmable en lote), match
   manual, o **movimiento faltante** (está en el banco, no en el sistema →
   alta asistida; así entran comisiones bancarias, intereses y depósitos
   no anticipados).
3. **Cierre:** periodo por cuenta; se cierra cuando el saldo proyectado =
   saldo del extracto (RN-7: las diferencias jamás se absorben en
   matches); genera acta de conciliación (`<ReporteShell>`) con partidas
   en tránsito listadas.
4. La conciliación es **el punto de convergencia de los tres feeds**:
   extracto (ingesta), egresos (pagos a proveedor + pagos a cuenta),
   ingresos (depósitos confirmados + expectativas de Caja).

**Capa post-MVP — conector vivo:** la integración con banca
(API/agregador/host-to-host) entra como `Millet.Integraciones.<Banco>`
fuera del módulo, publicando las mismas `ExtractoLinea` por la misma
costura (`IExtractoFuente`). El motor no distingue archivo de API.

> **Pendiente — dato del área (T-G2, no bloquea el mapa, sí el PR de
> conciliación):** bancos y formatos de las cuentas operativas de Millet
> (¿BBVA, Banamex, Banorte…? ¿export CSV/XLS/BAI2/MT940?).
> **Pendiente — sugerencia:** tolerancia de comisión para match,
> configurable por cuenta (seed: $0 — match exacto; las comisiones entran
> como movimiento faltante separado, nunca absorbidas). Evita el vicio de
> "cuadrar" absorbiendo diferencias.

### 3.6 Control de complementos de pago (REPP) [TES-4]

**AS-IS:** consulta SQL semanal en SAP + cruce manual en Excel contra "One
Factor"; 1-2 h; 70-100 movimientos/semana. `[Verificado — área]`

**TO-BE — Tesorería monitorea y registra, nunca emite:**

- **Sabor (a) — REPP propio (Millet emite por cobros PPD):** ya resuelto en
  Facturación (`FacturasCobrablesPpdQuery` = PPD timbradas con saldo > 0;
  `BandejaReppQuery` = emitidos). Tesorería **no construye nada**; a lo
  sumo enlaza a esa bandeja desde su dashboard. `[Verificado — repo]`
- **Sabor (b) — REPP recibido (el proveedor debe emitir a Millet por pagos
  PPD):** esto sí es de Tesorería, y así lo fija el levantamiento de CxP
  ("lo registra Tesorería junto con el pago; CxP consulta el estado pero no
  opera el REPP", `cuentas-por-pagar/00-levantamiento.md` §1):
  1. Read model: pagos a proveedor ejecutados con método PPD sin REPP
     recibido, con antigüedad (SLA 5 días — alineado al motivo
     `FALTA_REPP` que CxP ya tiene en seed). Requiere conocer el
     `MetodoPago` del pasivo — hoy no viene en el evento (T-G11).
  2. Registro del REPP recibido (UUID + fecha; XML a Blob, ADR-0024;
     validación vía servicio fiscal post-MVP, T-G10) → publica
     `tesoreria.repp-proveedor.recibido.v1` → CxP libera `FALTA_REPP`.
  3. Reporte semanal para CxP/Fiscal desde el read model (reemplaza el
     Excel).

> **Pendiente — dato del área (T-G3):** qué es exactamente "One Factor".
> No existe en el repo (no confundir con OneFactura, el PAC sustituido por
> FiscalAPI, ADR-0027/ADR-0038). Hipótesis: portal donde consultan CFDI
> recibidos. Si es una fuente de XMLs, la ingesta del REPP de proveedor
> podría automatizarse contra el buzón SAT post-MVP; en MVP el registro es
> manual con UUID.

---

## 4. Entidades del dominio

El detalle de columnas/DDL va en `01-diseno.md` (esquema Postgres
`tesoreria`, DbContext propio por ADR-0030 con su checklist de deploy;
concurrencia optimista ADR-0012; outbox ADR-0009).

### 4.1 `CuentaBancaria` [TES-7]

Master data propia del módulo: banco (catálogo), número de cuenta
(enmascarado en UI), CLABE (VO con masking PII), moneda (ADR-0014),
referencia de cuenta contable (para la Contabilidad futura), perfil de
extracto asociado (§3.5), activa. **Seed inicial; CRUD diferido al área de
Administración.**

### 4.2 `MovimientoBancario` (agregado central)

Todo peso que entra o sale: cuenta, sentido (Ingreso/Egreso), monto,
moneda (= moneda de la cuenta, RN-3), fecha valor, referencia bancaria,
concepto (catálogo §5.2), beneficiario (tipo + ref a Proveedor/Cliente,
nombres vía read-port ADR-0042), contramovimiento (RN-10).

**Estados ortogonales:**
- `EstadoAplicacion`: `NoAplicado → AplicadoParcial → Aplicado` (contra
  documentos: pasivos o confirmaciones de depósito).
- `EstadoConciliacion`: `NoConciliado → Conciliado` (contra extracto).

### 4.3 `AplicacionPagoProveedor`

Liga N:M entre movimiento y pasivo: movimiento, `FacturaProveedorId` (del
evento; **sin FK cross-módulo**), proveedor, importe aplicado, corrida
(opcional), trazabilidad al evento `aplicado` publicado, flag de revertida.
Única por (movimiento, factura).

### 4.4 `PasivoPendientePago` (proyección local)

Bandeja de egresos: proyección del evento de CxP con todos sus campos +
saldo local actualizado por aplicaciones. Poblada por listener idempotente
(`EventoProcesado`). No es fuente de verdad — CxP lo es.

### 4.5 `CorridaPago` + `CorridaPagoLinea` [TES-8]

Cabecera: cuenta, estado (§3.2), total, moneda, solicitante, autorizador
(matriz), fecha de oficio. Línea: factura, importe programado, ejecutada.
Única por (corrida, factura).

### 4.6 `DepositoConfirmacion` [TES-9]

Liga movimiento de ingreso ↔ propuesta de CxC: cliente, estado
(`Pendiente/Confirmada/Rechazada`), motivo de rechazo, sesión de caja
(expectativa), flag `ReppTimbrado` (se marca al consumir
`recibo-pago.timbrado.v1`), confirmador y fecha.

### 4.7 `ReppProveedorRecibido` (§3.6.b)

Factura de proveedor, UUID del complemento (único), fecha, ref del XML en
Blob (ADR-0024), registrador y fecha.

### 4.8 `Conciliacion` + `ExtractoLinea` (§3.5)

Cabecera: cuenta, periodo (año/mes, único por cuenta), saldo del extracto,
estado (`Abierta/Cerrada`), cerrador. Línea: fecha, descripción,
referencia, cargo/abono, fuente (`Archivo/Api` — costura `IExtractoFuente`),
match a movimiento y tipo de match (`Auto/Manual/AltaAsistida`).

### 4.9 `ConceptoMovimiento` (catálogo, §5.2)

Nombre, clasificación de flujo (`Operación/Inversión/Financiamiento`),
activo. Alimenta el reporte de flujo de efectivo (§7).

### 4.10 `EventoProcesado`

Idempotencia de listeners (patrón idéntico al de CxP/CxC).

---

## 5. Catálogos

### 5.1 Cuentas bancarias propias

Seed inicial con las cuentas operativas de Millet (lista pendiente,
T-G2). El CRUD estaba diferido a Administración; TES-7 se revisó
(2026-07-15) y el alta/edición/toggle vive en Tesorería con el permiso
`tesoreria.cuentas.administrar` — el seed script queda solo para
bootstrap de ambientes.

### 5.2 Conceptos de movimiento

Seed con clasificación de flujo por defecto (editable por movimiento).

> **Pendiente — sugerencia:** Javier valida el catálogo inicial contra su
> plantilla actual de flujo de efectivo (hoy Excel sobre Libro Mayor SAP).

### 5.3 Perfiles de extracto

Uno por banco/formato (T-G2). Nuevos; el `PerfilParserBanco` de TC
empresarial en CxP es la referencia de diseño, no código compartido (esa
decisión — compartir o duplicar el patrón — se toma en `01-diseno.md`).

---

## 6. Autorizaciones

| Operación | Autoriza | Mecanismo |
|---|---|---|
| Corrida de pagos | Jefe de Tesorería (+ escalamiento por monto, T-G4) | Matriz de autorización reutilizable de Requisiciones/Compras [TES-8] |
| Pago a cuenta (sin documento) | Jefe de Tesorería | Matriz; gate propio de Tesorería [TES-2] |
| Liga tardía con diferencia de monto | Jefe de Tesorería | Confirmación explícita (§3.4) |
| Cierre de conciliación | Jefe de Tesorería | Estado `Cerrada` con acta (§3.5) |
| Confirmación de depósito de cliente | Auxiliar/Jefe (permiso `tesoreria.depositos.confirmar`) | RBAC ADR-0007 |
| Pago individual contra pasivo autorizado | No requiere segunda autorización (la decisión es de CxP) | RN-1 |

Suplencias: por definir con la matriz (T-G4). Los permisos canónicos
`tesoreria.*` se dan de alta en `Identidad.Domain.PermisosCanonicos`
(**recordar**: tocar `PermisosCanonicos` requiere migration en
`IdentidadDbContext`).

---

## 7. Reportes

Todos sobre el motor nativo (ADR-0036: JSON estructurado + React,
`<ReporteShell>`, PDF `@react-pdf/renderer`, Excel `exceljs`); ninguno con
lógica bespoke en el dominio [TES-6].

| Reporte | Fuente | Frecuencia AS-IS | Reemplaza |
|---|---|---|---|
| Flujo de efectivo | `MovimientoBancario` + saldos por cuenta, clasificación por `ConceptoMovimiento` | Mensual, 3-4 h manuales | Libro Mayor SAP + plantilla Excel |
| Auxiliares de bancos | `MovimientoBancario` por cuenta/periodo | Semanal | Export SAP |
| Pagos a cuenta abiertos | Read model §3.4 | Semanal | "Pagos efectuados no reconciliados" |
| REPP de proveedor pendientes | Read model §3.6.b | Semanal | Consulta SQL + Excel vs "One Factor" |
| Acta de conciliación | Cierre §3.5 | Mensual | Proceso manual SAP |

---

## 8. Reglas críticas

| # | Regla | Dueño | Estado |
|---|---|---|---|
| RN-1 | Un movimiento de egreso ligado a pasivo solo se registra contra `pasivo.autorizado-para-pago` recibido | Tesorería | Nueva |
| RN-2 | Máximo un pago no aplicado abierto por proveedor | Tesorería (gate §3.4) | Traducción de regla AS-IS |
| RN-3 | Moneda del movimiento = moneda de la cuenta; pasivo cross-moneda bloqueado en MVP | Tesorería | `[Pendiente — área]` (T-G6) |
| RN-4 | El evento `aplicado` se emite por factura, aun en corridas/pagos multi-factura | Tesorería | Contrato congelado |
| RN-5 | Corrida ejecutable solo en estado `Autorizada` (matriz) | Tesorería | Nueva [TES-8] |
| RN-6 | `PagoClienteConfirmadoEvent` solo desde movimiento bancario de ingreso identificado | Tesorería | Nueva [TES-9] |
| RN-7 | Conciliación cerrable solo con saldo cuadrado; diferencias jamás se absorben en matches | Tesorería | Nueva (§3.5) |
| RN-8 | Registro bloqueado en periodo cerrado vía `IPeriodoContablePort` | Puerto (stub hoy) | Patrón elegido; candado real llega con Contabilidad |
| RN-9 | "No pagar con complementos pendientes / NC sin aplicar / solo facturas completas" | **CxP, no Tesorería** | `FALTA_REPP` es gate posterior (seed existente en CxP); la NC ya netea el saldo del evento; el resto vive en el flujo F4 de CxP |
| RN-10 | Reversa de pago requiere motivo y no borra el movimiento: genera contramovimiento ligado | Tesorería | Nueva (auditoría, ADR-0008) |

---

## 9. Integraciones — resumen de cableado

| Dirección | Contraparte | Mecanismo | Estado |
|---|---|---|---|
| ← CxP | `pasivo.autorizado-para-pago.v1` | Service Bus, subscription nueva de Tesorería en `cuentas-por-pagar-events` | Contrato congelado; falta el consumer |
| → CxP | 4 eventos `tesoreria.*.v1` | Topic `tesoreria-events` (ya en Bicep) vía Outbox | Contrato congelado; falta el publisher |
| ← CxC | `propuesta-aplicacion.creada.v1` | Subscription nueva en `cuentas-por-cobrar-events` | Publisher existe |
| → CxC (vía Facturación) | `pago-cliente.confirmado.v1` | Topic `tesoreria-events` | **Nuevo — cierra `<PagoClienteConfirmado>`**; requiere listener nuevo en Facturación que invoque `EmitirReppCommand` (PR del lado Facturación) |
| ← Facturación | `caja-sesion.cerrada.v1`, `recibo-pago.timbrado.v1` | Subscriptions nuevas en `facturacion-events` | Publishers existen; cierra `<TesoreriaCajaSesion>` |
| → DatosMaestros | Datos bancarios del proveedor | Read-port nuevo (`IProveedorBancoReadPort`) con masking PII | **Nuevo — resuelve `PayloadEnriquecido`** (T-G1) |
| → Contabilidad (futura) | Eventos de contabilización de movimientos | Outbox; consumidor no existe (stub ADR-0031) | Publicar desde MVP para replay futuro [TES-5] |
| ← Bancos | Extractos | MVP: archivo por perfil; post-MVP: `Millet.Integraciones.<Banco>` tras `IExtractoFuente` | Nuevo |
| Compras | Listener espejo de eventos de Tesorería | `PLATFORM-TODO(<TesoreriaEventListenerCompras>)` | Fuera de este módulo; se desbloquea al existir el publisher |

---

## 10. Decisiones registradas (aprobadas 2026-07-14)

- **TES-1** — Frontera de pago a proveedor: CxP posee pasivo y decisión;
  Tesorería ejecuta contra el contrato congelado F9-PR1 (4 eventos espejo +
  consumer de pasivo autorizado). Banco del proveedor por read-port (T-G1).
- **TES-2** — Pago a cuenta como `MovimientoBancario` no aplicado con gate
  propio de Tesorería y reconciliación tardía sin re-desembolso; la regla
  "máximo uno abierto por proveedor" vive en Tesorería.
- **TES-3** — Conciliación MVP = ingesta por archivo con perfiles de parser
  (patrón `PerfilParserBanco`) + matching asistido; conector vivo post-MVP
  en `Millet.Integraciones.<Banco>` tras la costura `IExtractoFuente`.
- **TES-4** — Tesorería monitorea/registra REPP, nunca emite. La emisión
  propia queda en Facturación (read models existentes); Tesorería opera
  solo el REPP recibido de proveedor.
- **TES-5** — Sin asientos manuales ni reconciliación interna en Tesorería.
  Publica eventos de contabilización al outbox; el GL sigue en SAP hasta
  que exista el módulo Contabilidad. Candado de periodo por
  `IPeriodoContablePort`.
- **TES-6** — Flujo de efectivo y todos los reportes sobre el motor nativo
  ADR-0036 (`<ReporteShell>`); sin lógica de reporte en el dominio.
- **TES-7** — `CuentaBancaria` + `MovimientoBancario` como master data
  propia del módulo; catálogo de cuentas por seed, CRUD diferido a
  Administración. **Revisada 2026-07-15:** el CRUD del catálogo vive en
  Tesorería (permiso `tesoreria.cuentas.administrar`); `numero_cuenta`
  inmutable post-creación, `moneda` editable solo sin movimientos, CLABE
  write-only en edición. El seed script queda para bootstrap.
- **TES-8** — Oficio de cartera = artefacto de la corrida de pagos
  autorizada vía matriz reutilizable. Dos autorizaciones distintas: CxP
  autoriza el pasivo, Tesorería autoriza la corrida.
- **TES-9** — Tesorería absorbe Ingresos: posee la frontera bancaria en
  ambos sentidos, confirma depósitos de cliente y es el emisor real de
  `PagoClienteConfirmadoEvent` (cierra `<PagoClienteConfirmado>` y
  `<TesoreriaCajaSesion>`).

---

## 11. Decisiones pendientes y gaps

| ID | Gap | Impacto | Resolución propuesta | Owner |
|---|---|---|---|---|
| T-G1 | Payload de pasivo sin datos bancarios (`PayloadEnriquecido`) | Sin CLABE no se ejecuta el pago | Read-port a DatosMaestros; evaluar enriquecer el evento en v2 solo si el read-port duele | Eduardo/CxP |
| T-G2 | Bancos y formatos de extracto sin inventariar | Dimensiona el PR de conciliación | Lista de cuentas operativas + ejemplo de export por banco | Javier |
| T-G3 | "One Factor" sin identificar | Puede automatizar §3.6.b | Aclarar herramienta; MVP registra manual | Javier |
| T-G4 | Matriz de corridas sin montos/suplencias | Bloquea seed de la corrida | Propuesta en §3.2; confirmar umbrales | Javier |
| T-G5 | Emisor de asientos GL inexistente | Movimientos sin póliza hasta Contabilidad | Publicar eventos de contabilización desde MVP (outbox) para replay; interín SAP es el GL | Eduardo |
| T-G6 | Pago cross-moneda | Prohibido AS-IS; ¿casos reales? | RN-3 conserva la prohibición; confirmar | Javier |
| T-G7 | Rechazo de propuesta CxC desde Tesorería | Ciclo de re-propuesta | Evento `propuesta-aplicacion.rechazada.v1`; validar contra el agregado CxC | Eduardo |
| T-G8 | Migración de saldos/movimientos iniciales | Arranque de conciliación | Seed one-shot patrón ADR-0044: saldos iniciales por cuenta a fecha de corte; sin histórico SAP | Eduardo/Javier |
| T-G9 | Volumetría §9 de la plantilla vacía | NFRs sin datos | Derivar del AS-IS (26 pagos/día = carga trivial); confirmar concurrencia (¿2 usuarios?) | Javier |
| T-G10 | Validación fiscal de REPP recibidos | UUID falso/cancelado | MVP: registro con UUID; validación SAT (Validex u otro) post-MVP | Eduardo/Fiscal |
| T-G11 | `MetodoPago` (PUE/PPD) del pasivo no viene en `pasivo.autorizado-para-pago.v1` | Sin él no se detecta "pago PPD sin REPP recibido" (§3.6.b) | Opción a: enriquecer el evento v1 aditivamente (compatible por convención); opción b: read-port a CxP. Decidir en `01-diseno.md` | Eduardo/CxP |

---

## 12. Plan de implementación sugerido

Secuencia trunk-based; la ruta crítica es PR-1→PR-4 (cierra el contrato
congelado con CxP); ingresos y conciliación son paralelizables después.
El breakdown fino va en `03-pr-breakdown.md`.

| PR | Contenido | Depende de |
|---|---|---|
| **PR-1** | Esquema `tesoreria` + DbContext (checklist ADR-0030: `Program.cs` + `deploy-app-dev.yml`), `CuentaBancaria` + `ConceptoMovimiento` con seed, permisos canónicos (ADR-0007 + migration de Identidad), registro del módulo | — |
| **PR-2** | `MovimientoBancario` + `AplicacionPagoProveedor`: agregado, alta de egreso ligado, RN-3/RN-10, endpoints | PR-1 |
| **PR-3** | Consumer de `pasivo.autorizado-para-pago.v1` → `PasivoPendientePago` (listener idempotente) + `IProveedorBancoReadPort` a DatosMaestros con masking [T-G1] | PR-1 |
| **PR-4** | Publisher de los 4 eventos `tesoreria.*.v1` vía outbox (payloads espejo exactos) + flujo de pago individual completo (bandeja→registro→aplicado) + reversa | PR-2, PR-3 |
| **PR-5** | Corrida de pagos + matriz de autorización + oficio (`<ReporteShell>`) [TES-8] | PR-4; T-G4 |
| **PR-6** | Pago a cuenta: gate RN-2, reconciliación tardía, read model de abiertos [TES-2] | PR-4 |
| **PR-7** | Ingresos: consumer de propuestas CxC + `DepositoConfirmacion` + publisher `pago-cliente.confirmado.v1` + consumers de `caja-sesion.cerrada` y `recibo-pago.timbrado` [TES-9]. **Requiere PR gemelo en Facturación** (listener → `EmitirReppCommand`) | PR-2 |
| **PR-8** | REPP de proveedor: `ReppProveedorRecibido`, publisher `repp-proveedor.recibido.v1`, read model de pendientes + reporte [TES-4] | PR-4; T-G11 |
| **PR-9** | Conciliación: `Conciliacion`/`ExtractoLinea`, parser por perfil (costura `IExtractoFuente`), motor de matching, alta asistida, cierre + acta [TES-3] | PR-2; perfiles reales tras T-G2 |
| **PR-10** | Reportes: flujo de efectivo + auxiliares (`<ReporteShell>`, ADR-0036) [TES-6] | PR-2 |
| **PR-11** *(dependiente)* | Eventos de contabilización al outbox para Contabilidad futura [T-G5] | PR-4 |
| **PR-12** *(dependiente, fuera del módulo)* | `Millet.Integraciones.<Banco>` — conector vivo tras `IExtractoFuente` | PR-9; decisión de agregador/banco |

---

## 13. Dependencias de plataforma pendientes

Convención ADR-0031: cada stub lleva `// PLATFORM-TODO(<id>)` en código.

| Pieza | Ticket / ID | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| Candado de periodo contable | `<PeriodoContableCerrado>` (existente, Facturación) | Stub siempre-abierto | Tesorería consume el mismo `IPeriodoContablePort`; el real llega con Contabilidad |
| Asientos contables de movimientos bancarios | `<ContabilidadAsientos>` (existente) | Eventos al outbox sin consumidor | Contabilidad consume el topic al existir [T-G5] |
| Emisor de `PagoClienteConfirmadoEvent` | `<PagoClienteConfirmado>` (existente, CxC/Facturación) | Confirmación manual interina en CxC | **Este módulo lo cierra** (PR-7) + listener nuevo en Facturación |
| Conciliación de depósitos de Caja | `<TesoreriaCajaSesion>` (existente, Facturación) | Evento publicado sin consumidor | **Este módulo lo cierra** (PR-7) |
| Listener espejo en Compras | `<TesoreriaEventListenerCompras>` (existente) | Log informativo | Se desbloquea con el publisher de PR-4; el wiring es de Compras |
| Datos bancarios de proveedor en el evento de pasivo | `PayloadEnriquecido` (existente, CxP) | Payload sin bank data | Read-port `IProveedorBancoReadPort` (PR-3); enriquecer evento solo si duele [T-G1] |
| Conector bancario vivo | `<IntegracionBanco>` (nuevo) | Ingesta por archivo | `Millet.Integraciones.<Banco>` tras `IExtractoFuente` (PR-12) |
| Validación fiscal de REPP recibido | `<ValidacionReppRecibido>` (nuevo) | Registro manual por UUID | Servicio de validación SAT post-MVP [T-G10] |

---

## 14. Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Pago a cuenta sigue ocurriendo fuera del sistema (hábito AS-IS) | El limbo vuelve a ser invisible; distorsión fiscal al cierre | Gate RN-2 + read model de abiertos con antigüedad; el registro es más barato que el reporte manual semanal |
| Divergencia entre saldo local (`PasivoPendientePago`) y CxP | Pagos por montos incorrectos | La proyección nunca es fuente de verdad; el `aplicado` lo valida CxP (`PAGO_EXCEDE_SALDO` en su dominio) y la reversa existe |
| El listener de Facturación para `pago-cliente.confirmado` no se construye a tiempo | El ciclo de ingresos queda manual (como hoy) | PR-7 se coordina con un PR gemelo en Facturación; mientras tanto el endpoint manual de REPP sigue operando |
| Perfiles de extracto subestimados (formatos bancarios inconsistentes) | Conciliación MVP no despega | T-G2 antes de PR-9; alta asistida cubre líneas no parseadas |
| Doble emisión de `aplicado` (reintentos) | CxP duplica pagos | Outbox + idempotencia del consumer de CxP por `EventoProcesado` (ya implementada) `[Verificado — repo]` |
| Periodo contable sin candado real hasta Contabilidad | Movimientos retroactivos | RN-8 con stub documentado; reporte explícito de no-aplicados al cierre |

---

## 15. Glosario

| Término | Definición |
|---|---|
| **Pasivo** | Factura de proveedor autorizada en CxP; lo que Millet debe |
| **Pago a cuenta** | Egreso bancario ejecutado sin documento ligado (sin factura provisionada) |
| **Corrida de pagos** | Lote de pasivos seleccionados para desembolso, autorizable como unidad |
| **Oficio de cartera** | Documento PDF de la corrida autorizada, usado para instruir/soportar los pagos |
| **REPP** | Recibo Electrónico de Pago (complemento de pago, CFDI tipo P). "Propio" = Millet lo emite a clientes; "recibido" = el proveedor lo emite a Millet |
| **PPD / PUE** | Pago en Parcialidades o Diferido / Pago en Una Exhibición (método de pago SAT); solo PPD exige REPP |
| **Conciliación** | Cruce periódico extracto bancario ↔ movimientos internos por cuenta |
| **Extracto / estado de cuenta** | Archivo del banco con los movimientos reales de la cuenta |
| **Propuesta de aplicación** | Matching depósito↔facturas que CxC propone y Tesorería confirma |
| **One Factor** | Herramienta externa del AS-IS para cruce de complementos (por identificar, T-G3) |
| **Matriz de autorización** | Mecanismo reutilizable de Requisiciones/Compras para flujos de autorización con montos y suplencias |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. Mapa funcional del área (plantilla 05-may-2026) + acoplamiento validado contra el repo (contratos F9-PR1, patrón CxC, puertos Contabilidad). TES-1…TES-9 aprobadas por Eduardo. Se agrega T-G11 (`MetodoPago` ausente en el evento de pasivo), corrección sobre el borrador de origen. |
