# 05 — Sincronización de clientes y condiciones A+W → ERP (F1-ADM-06)

> **Versión del documento:** 0.1.0 (borrador de contrato)
> **VersionContrato:** `1` · **VersionMapeo:** `0-borrador`
> **Decisión ancla:** [ADR-0048](../decisiones/0048-bd-integracion-aw-pedidos.md) (adenda ADM-06, ver A2)
> **Flujo hermano:** [04 — Ingesta de pedidos en firme](04-ingesta-pedidos-facturacion.md)
> **Estado:** especificación previa a construcción. Todo lo no validado contra A+W real está marcado **PENDIENTE**.

---

## 0. Cómo leer este documento

- Es el contrato lógico y la política por campo de la sincronización de clientes. **No** afirma que exista lectura real contra A+W: eso es otro entregable (O1A-AW-INT). El cierre de ADM-06 es construcción técnica con fixtures sintéticos.
- Toda equivalencia no validada (`UST_ID`/`STEUERNUMMER` -> RFC, mapeo de `KZ_STATUS`, `PESOSMX` -> MXN, domicilio) queda **PENDIENTE** y conserva el valor crudo.
- Las columnas A+W (`KU_KUNDEN`, `KA_ZAHLBED`) conservan su nombre original; el resto de nombres son propuestos y no implican código existente.
- Los ejemplos y fixtures son 100 % sintéticos (`CLIENTE DEMO 001`, RFC genéricos `XAXX010101000` / `XEXX010101000` o con prefijo `DEMO`).

## 1. Alcance y no-alcance

**Alcance (ADM-06, parcial):**
- Reconocer en el ERP al mismo cliente de A+W (una sola identidad), recibir sus datos comerciales y su condición de pago/moneda de maestro.
- Dejar que Facturación complete los datos fiscales y que CxC administre la línea de crédito autorizada.
- Que repetir lecturas, o recibir un pedido de ese cliente, no duplique clientes ni borre correcciones fiscales.
- Compatibilidad con un ERP sin SAP: ningún consumidor consulta SAP en tiempo de operación.

**No-alcance (otro entregable dentro del proyecto, no fuera del proyecto):**
- Modificar datos en A+W (el módulo es solo lectura, anti-scope del módulo `Millet.Integraciones.Aw`).
- Registrar/contabilizar una venta, devolverla a A+W, liberar pedidos, modificar `CxC.LineaCredito`.
- Extractor de ventas, hitos de pedidos, escritura de pagos/facturas a A+W.
- Sustituir el flujo de pedidos (flujo 2, doc. 04): se mantiene intacto.

La aprobación de este contrato **no** acepta el retiro de SAP.

## 2. Identidad

| Concepto | Regla |
|---|---|
| Referencia externa | `KU_KUNDEN.ID` como **texto canónico** -> `Cliente.ReferenciaExterna` (máx. 50, único cuando no es null). |
| `MANDANT` | Solo contexto (`MandantOrigen`). No equiparar con `EmpresaId` ni con sucursal sin evidencia. **PENDIENTE** de validar. |
| RFC | **Nunca** es llave de correlación ni de fusión. Dos referencias distintas pueden compartir RFC genérico y siguen siendo dos clientes. |
| Cliente manual con la misma referencia | No se apropia automáticamente: se abre conflicto de correlación. |

Código existente que se reutiliza (no se modifica en A1):
- `backend/src/DatosMaestros/Domain/Cliente.cs`: maestro único `compartido.clientes`; atributos fiscales nullable (fiscales incompletos no bloquean el alta, bloquean el timbrado); `Origen` (`OrigenMaster.Aw`/`Manual`), `Estatus`, `MonedaDefault`.
- `backend/src/Compartido/Application/Catalogos/Clientes/ProvisionarClienteDesdeAwCommand.cs`: auto-provisión desde pedidos; upsert por `ReferenciaExterna`; **hoy devuelve el existente sin actualizarlo**. El servicio único de aplicación (Entrega B) debe servir a este handler y a la sincronización, sin duplicar el algoritmo.
- `backend/src/Integraciones.Aw/Infrastructure/Pedidos/AwMastersSqlReaders.cs`: lector actual sobre la vista `dbo.vw_erp_cliente` (10 columnas: `cliente_ref, razon_social, rfc, calle, colonia, cp, ciudad, estado, pais, telefono`). No trae condición de pago, moneda, estado ni crédito; el contrato de este documento lo amplía sin alterar el lector de pedidos.

## 3. Tres grupos de datos separados

| Grupo | Qué contiene | Dueño | Regla de aplicación |
|---|---|---|---|
| **Comercial recibido** | Nombre/domicilio comercial de origen, contacto, condición y moneda de origen, crédito de referencia, estado crudo | A+W | Se guarda valor de origen + fecha de lectura; se aplican solo campos aprobados. |
| **Candidato fiscal** | `UST_ID`, `STEUERNUMMER`, razón social de origen, CP de origen | A+W como sugerencia | Se **registra**, no se aplica. No se infiere equivalencia con RFC/TaxId. Promoción a fiscal solo por decisión autorizada. |
| **Fiscal local** | `RazonSocial`, `Rfc`, `RegimenFiscal`, `CodigoPostalFiscal`, uso CFDI, formas/método de pago | Facturación/Fiscal en ERP | Una lectura **nunca** los borra ni los sustituye. Conflictos requieren permiso y bitácora. |

Además existe el grupo **control** (metadatos de integración, sin dato de negocio): `EjecucionId`, `LeidoEnUtc`, `AplicadoEnUtc`, `HashOrigen`, `VersionContrato`, `VersionMapeo`, resultado/error.

## 4. Contrato de datos (matriz campo -> dueño -> política)

Política: **aplica** = se escribe en el maestro ERP; **solo registra** = se guarda en el registro de origen sin tocar el maestro; **nunca pisa** = no se sobrescribe si ya hay valor local.

| Campo origen | Tipo observado | Campo ERP / registro | Grupo | Dueño | Transformación | Nulabilidad | Consumidor | Política | Estado |
|---|---|---|---|---|---|---|---|---|---|
| `KU_KUNDEN.ID` | int NOT NULL | `Cliente.ReferenciaExterna` | Control (identidad) | A+W/TI | Texto canónico, sin ceros añadidos ni recorte | No nulo | Todos | Aplica (solo al crear) | validado (código existente) |
| `KU_KUNDEN.MANDANT` | int NOT NULL | `MandantOrigen` (registro) | Control (contexto) | A+W | Ninguna | No nulo | Auditoría | Solo registra | PENDIENTE (significado) |
| `NAME1/NAME2/NAME3` | nvarchar | `NombreComercialOrigen` (registro) | Comercial recibido | A+W | Trim; concatenación determinista NAME1..3 | NAME1 esperado; 2/3 nulos | UI, Facturación (consulta) | Solo registra. **Nunca pisa** `RazonSocial` | PENDIENTE (uso de NAME2/3) |
| `STRASSE, ORT, PLZ, PROVINZ, LAND` | nvarchar | `DomicilioOrigen*` (registro) | Comercial recibido | A+W | Trim; valores crudos | Nulos posibles | UI | Solo registra | PENDIENTE (dirección) |
| `PLZ` (como CP fiscal) | nvarchar | candidato `CodigoPostalFiscal` | Candidato fiscal | Fiscal | Ninguna | Nulo posible | Facturación | Solo registra; **nunca pisa** CP fiscal local | PENDIENTE |
| `UST_ID` | nvarchar | `CandidatoFiscalUstId` (registro) | Candidato fiscal | Fiscal | Trim, sin normalizar a RFC | Nulo posible | Facturación | Solo registra; **nunca pisa** `Rfc` | PENDIENTE (equivalencia RFC/TaxId) |
| `STEUERNUMMER` | nvarchar | `CandidatoFiscalSteuernummer` (registro) | Candidato fiscal | Fiscal | Trim | Nulo posible | Facturación | Solo registra; **nunca pisa** `Rfc` | PENDIENTE (equivalencia RFC) |
| `TLF1`, `TLF2` | nvarchar | `Cliente.Telefono` (TLF1) / registro (TLF2) | Comercial recibido | A+W | Trim; validar longitud | Nulo posible | UI, CxC | Aplica solo si `Telefono` local vacío; si no, **nunca pisa** y registra | PENDIENTE (campos acordados) |
| `MAIL` | nvarchar | `Cliente.Email` | Comercial recibido | A+W | Trim; validar formato | Nulo posible | UI, CxC | Aplica solo si `Email` local vacío; si no, **nunca pisa** | PENDIENTE |
| `KU_KUNDEN.ZAHLBED` | nvarchar NOT NULL | `CondicionCodigoOrigen` (registro) | Comercial recibido | A+W | Sin sustituir por enum genérico | No nulo | Facturación, CxC (referencia) | Solo registra | validado en forma; mapeo PENDIENTE |
| `KA_ZAHLBED.NUMMER` | int NULL | `CondicionNumeroOrigen` (registro) | Comercial recibido | A+W | Resolución `ZAHLBED -> KA_ZAHLBED.BEZ`; 0 o >1 coincidencias = pendiente sin duplicar cliente | Nulo posible | CxC (referencia) | Solo registra | PENDIENTE (cardinalidad completa) |
| `KA_ZAHLBED.BRUTTOTAGE` | int NULL | `DiasNominalesOrigen` (registro) | Comercial recibido | A+W | Ninguna; **no** se calculan vencimientos | Nulo posible (nulo != 0) | CxC (referencia) | Solo registra | catálogo observado CONTADO 1/0, REPARTO 4/3, 60 DIAS 6/60; PENDIENTE completo |
| `KU_KUNDEN.WAEHRUNG` | nvarchar NOT NULL | `MonedaCodigoOrigen` (crudo) + `MonedaNormalizada` | Comercial recibido | A+W | `PESOSMX` -> candidato `MXN`; `<indf>` y desconocidas sin equivalencia | No nulo | Facturación (`MonedaDefault`) | Solo registra crudo; aplica `MonedaDefault` solo con mapeo validado | PENDIENTE (`PESOSMX`->MXN) |
| `KREDIT_LIMIT`, `KREDIT_LIMIT1` | decimal NULL | `CreditoReferencia*` (registro) | Comercial recibido | CxC (autoriza) | Valores fuente explícitos; nunca elegir el máximo | Nulo != 0 | CxC (lectura) | Solo registra. **Nunca** escribe `CxC.LineaCredito` | PENDIENTE (precisión/escala) |
| `KREDIT_LIMIT_NET` | float NULL | `CreditoReferenciaNet` (registro) | Comercial recibido | CxC | Ninguna | Nulo != 0 | CxC (lectura) | Solo registra | PENDIENTE |
| `KZ_STATUS` | int NULL | `EstadoOrigenCrudo` (registro) | Comercial recibido | A+W + decisión Millet | Conservar número; mapeo aprobado por separado | Nulo != 0 != desconocido | Facturación, UI | Solo registra; **no** cambia `Estatus` mientras no haya mapeo | PENDIENTE (mapeo) |
| `KZ_GESPERRT` | int NOT NULL | `BloqueoOrigenCrudo` (registro) | Comercial recibido | A+W + CxC | Conservar número; bloqueo de crédito != baja | No nulo | CxC | Solo registra | PENDIENTE (mapeo) |
| `DATUM` | date NULL | `FechaOrigen` (registro) | Control | A+W | Ninguna | Nulo posible | Diagnóstico | Solo registra; no es marca de cambios confiable | PENDIENTE |
| `TRANSACTION_TIME` | datetime NULL | `TransaccionOrigen` (registro) | Control | A+W | Ninguna | Nulo posible | Diagnóstico | Solo registra; no se usa como watermark | PENDIENTE |
| `ROWID` | char | (no se usa) | Control | A+W | No usar como secuencia ni versión | n/a | Ninguno | No se lee para versionado | n/a |
| `EjecucionId`, `LeidoEnUtc`, `AplicadoEnUtc` | ERP | registro de control | Control | ERP | `LeidoEnUtc` no es hora del cambio en A+W | No nulo | Auditoría | Aplica | propuesto |
| `HashOrigen`, `VersionContrato`, `VersionMapeo`, resultado/error | ERP | registro de control | Control | ERP | Ver §7 | No nulo | Auditoría, conciliación | Aplica | propuesto |

Notas:
- Los nombres `*Origen`/`*Referencia` son lógicos; el nombre físico y si va en tabla auxiliar o campos propios se decide en la Entrega B.
- No se guarda payload completo de la tabla ni secretos.
- Los datos de receptor extranjero (`NumRegIdTrib`, `PaisResidencia`, domicilio extranjero) siguen el gap ya documentado en el reader (`PLATFORM-TODO(<AwClienteNumRegIdTrib>)`); no se infieren desde `UST_ID`.

## 5. Nulo != 0 != desconocido

- `NULL` en `BRUTTOTAGE`, `KREDIT_LIMIT*` o `KZ_STATUS` significa "el origen no informa"; `0` es un valor informado; un código sin mapeo es "desconocido". Los tres se conservan distintos y se aplican por política de campo.
- Un nulo de origen **no borra** un valor existente en ERP (en particular ningún fiscal).
- Un código de estado o moneda desconocido queda **pendiente de validación**, sin baja, activación ni conversión automática.
- Un cliente sin fiscales suficientes puede existir como maestro incompleto (aptitud comercial != aptitud fiscal); no se inventan valores para volverlo timbrable.

## 6. Condición del maestro vs. condición del pedido

- La condición/moneda del **maestro** (`ZAHLBED`, `KA_ZAHLBED.BEZ`, `WAEHRUNG`) es referencia. `BRUTTOTAGE` son días nominales; no se calculan vencimientos.
- La condición del **pedido** es un snapshot independiente (`BW_AUFTR_KOPF.FI_ZAHLBED`, flujo 2). No se reescriben históricos por un cambio de catálogo o de maestro.

## 7. Hash de origen

- `HashOrigen` = hash determinista de los **campos comerciales consumidos** más la **condición resuelta del catálogo** (`NUMMER`, `BRUTTOTAGE`). Así un cambio en `BRUTTOTAGE` se detecta aunque `ZAHLBED` del cliente no cambie.
- La normalización previa al hash (trim, nulos, orden de campos) es determinista y **versionada** junto con `VersionMapeo`. Cambiar la normalización exige subir versión y se documenta como cambio de contrato.
- Se omiten campos ajenos a ADM-06 y los de control.
- El hash prueba igualdad o diferencia; **no** prueba antigüedad ni orden temporal. Ni el hash, ni `LeidoEnUtc`, ni una secuencia local sustituyen una versión monotónica de origen.

## 8. Detección de cambios

1. **Sin watermark.** No hay fecha de cambios confiable demostrada en A+W. Se usa conciliación paginada (orden estable por ID) y comparación de snapshot; no se filtra por "fecha del día". En fixtures se prueba versión fuente si existe.
2. **Ausencia != baja.** Que una fila falte en un lote, un timeout o un código nuevo nunca produce baja. Solo una baja explícita con estado mapeado y aprobado restringe operaciones nuevas, conservando documentos y cartera. En A+W real, detectar borrado físico exacto requiere un mecanismo de origen confirmado (**PENDIENTE**).
3. **Relectura en reintento.** Un reintento vuelve a leer A+W antes de aplicar; un payload antiguo guardado no reemplaza un dato más reciente. Con versión de origen monotónica verificada, se rechaza la menor y la repetida es idempotente (**PENDIENTE** hasta verificarla).
4. **Serialización.** Aplicación serializada por referencia; un solo barrido por origen a la vez. La paginación no da foto transaccional: se repiten barridos y se concilia para converger.
5. **Carrera pedido vs. sincronización.** Índice único en referencia + reintento específico al conflicto de esa clave + relectura del cliente ganador. No capturar cualquier error SQL como "ya existe". No duplicar el algoritmo entre handlers.
6. **Fallo parcial** no se reporta como ejecución completa; tras caída se reanuda o repite de forma idempotente.
7. **Resultado por cliente:** creado, actualizado, sin cambios, pendiente de validación, conflicto o error; más totales por ejecución.

## 9. Casos de prueba esperados (12 escenarios de fixtures)

Cada fixture (`backend/tests/Integraciones.Aw.UnitTests/Clientes/Fixtures/<escenario>.json`, A3) contiene `{ escenario, descripcion, origen, estadoErpPrevio, resultadoEsperado }`, con datos sintéticos.

| # | Escenario | Situación | Resultado esperado |
|---|---|---|---|
| 1 | `alta-valida` | Referencia libre, fila válida, condición resuelta | Cliente **creado** con `Origen = Aw`; comerciales aplicados; fiscales sin inventar; registro de origen con hash. |
| 2 | `repeticion` | Misma fila ya aplicada, sin cambios | **Sin cambios**; no se crea otro cliente; se registra la comprobación. |
| 3 | `cambio-comercial` | Cambia un campo comercial permitido (p. ej. teléfono vacío localmente) | **Actualizado** solo en campos permitidos; hash nuevo; fiscales intactos. |
| 4 | `correccion-fiscal-local` | Fiscales corregidos localmente; origen trae valores distintos | Fiscales locales **preservados**; diferencia como candidato/conflicto registrado con causa; sin sobrescritura. |
| 5 | `moneda-desconocida` | `WAEHRUNG` sin equivalencia (p. ej. `<indf>`) | Valor crudo conservado; `MonedaDefault` no cambia; resultado **pendiente de validación**. |
| 6 | `condicion-sin-coincidencia-o-duplicada` | `ZAHLBED` con 0 o >1 coincidencias en `KA_ZAHLBED` | Condición **no resuelta**, pendiente con causa; cliente no duplicado ni bloqueado en su identidad. |
| 7 | `cambio-catalogo-dias` | `BRUTTOTAGE` cambia en catálogo con `ZAHLBED` igual | Hash **distinto**; actualización del registro de condición; snapshots de pedidos históricos intactos. |
| 8 | `estado-desconocido` | `KZ_STATUS`/`KZ_GESPERRT` con código sin mapeo | Números crudos conservados; **sin baja ni activación**; pendiente de validación. |
| 9 | `baja-explicita-demo` | Baja explícita de demostración con estado mapeado (solo en fixture; el mapeo real es PENDIENTE) | `Estatus` restringe operaciones nuevas; documentos y cartera **conservados**; auditoría registrada. |
| 10 | `fallo-y-reintento` | Primera ejecución falla a mitad (error de lectura/aplicación); luego reintento | Primera ejecución **no completada**; reintento **relee** origen y converge de forma idempotente; sin duplicados. |
| 11 | `rfc-generico-dos-referencias` | Dos referencias distintas con RFC genérico `XAXX010101000` | **Dos clientes** distintos; nunca se fusiona por RFC. |
| 12 | `competencia-pedido-sincronizacion` | Alta simultánea por auto-provisión de pedido y por sincronización | **Un solo cliente** (índice único); el perdedor reintenta específico a esa clave y relee al ganador; sin error genérico. |

El test de forma (`ClientesFixturesTests`, A3) verifica que existan los 12 archivos, que los campos obligatorios estén presentes, que `resultadoEsperado` no esté vacío y que no haya RFC reales (solo genéricos o con prefijo `DEMO`). Las pruebas de comportamiento contra persistencia real corresponden a las Entregas B y C.

## 10. Configuración y ejecución (propuesta)

Nombres **propuestos**, no implementados: `ConnectionStrings:AwClientesDb`, `IntegracionesAw:Clientes`. Lector real **deshabilitado por defecto**, con flags independientes para leer, aplicar y programar; `AwIntegracionDb` no es el interruptor del lector de clientes y configurar clientes no habilita pedidos. Un adaptador de vista `vw_erp_cliente` ampliada podrá usar el mismo contrato. Ruta de conexión privada, TLS, cuenta de lectura y cadencia: **PENDIENTE** (VILO/TI); ningún secreto va al repositorio.

## 11. Versionado

| Elemento | Valor |
|---|---|
| `VersionContrato` | `1` (cambia al alterar campos, grupos o políticas de este documento) |
| `VersionMapeo` | `0-borrador` (sube al validar equivalencias RFC, moneda, estado, condición y dirección) |
| Normalización del hash | Versionada con `VersionMapeo` (§7) |

Cada ejecución y cada registro de origen guardan ambas versiones.

## 12. Pendientes (resumen)

- Equivalencia `UST_ID`/`STEUERNUMMER` -> RFC/TaxId (validar con ficha conocida por Fiscal).
- Mapeo de `KZ_STATUS` y `KZ_GESPERRT`; significado de `MANDANT`.
- `PESOSMX` -> MXN y catálogo de monedas completo.
- Cardinalidad completa de `KA_ZAHLBED`; precisión/escala de crédito.
- Campos de contacto/domicilio a aplicar; uso de `NAME2/NAME3`.
- Versión monotónica de origen y detección exacta de borrado físico.
- Ruta privada, instancia efectiva, TLS y permisos SELECT hacia `MILMAIN`; medición de carga y cadencia.
