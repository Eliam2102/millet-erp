# 09 · A+W OTR y exportación (flujo 3)

> **Propuesta para el contrato I13.4 · no aplicar sin acuerdo.**
> Propiedad de datos: **propuesta VILO, PEN-CE-025**, pendiente de validar con Millet y Jorge.
> Implementación local y Demo no acreditan conexión con OTR, UAT ni aceptación de Millet.

A+W OTR acomoda piezas y cajas en contenedores y define rutas de entrega (SRC-089 §1.0,
05-oct-2026). «OTR no es una compra» (respuesta Millet, 05-oct). No crea requisiciones,
OC ni proveedores. La compra extranjera conserva requisición → OC → expediente de importación.
La «Orden de Transporte» del glosario de CxP es un concepto distinto.

## Frontera y dependencias

El flujo 3 vive en `Integraciones.Aw/Infrastructure/Exportacion/`. Implementa los puertos
`IOtrEntradaReader` y `IOtrSalidaWriter` definidos por Comercio Exterior. La dependencia es
`Integraciones.Aw → ComercioExterior`; CE no referencia A+W. No usa `AwSolicitudesWorker`,
`IngestaControl`, EDI, drop service ni consumidores de eventos propios en Service Bus.

SQL Server real: **exclusivamente** `MILLET_INTEGRACION.dbo.aw_otr_envio`,
`aw_otr_envio_linea` y `erp_otr_estado`. Son tablas puente **nuevas, definidas por el ERP**
(D18). No hay INSERT/UPDATE/DELETE contra tablas internas de A+W.

## Contrato de entrada

A+W inserta cabecera y todas sus líneas **en una sola transacción**, de modo que el lector
nunca encuentre un envío publicado a medias. Cada versión es inmutable; los cambios se
insertan con una versión estrictamente mayor. Las cancelaciones son operaciones explícitas.

`dbo.aw_otr_envio` (SQL Server; en Demo cambian UUID/timestamp a `uuid`/`timestamptz`):

| Columna | Tipo | Quién escribe / significado |
|---|---|---|
| `otr_envio_id` | nvarchar(100), requerido | A+W; identificador estable del envío |
| `version` | bigint > 0 | A+W; creciente por envío; PK compuesta `(otr_envio_id, version)` |
| `cliente_numero_aw`, `sucursal_clave_aw` | nvarchar(50), requeridos | A+W; referencias propias. ERP resuelve cliente por número A+W y sucursal por clave A+W con los puertos existentes de Facturación. |
| `cliente` | nvarchar(200), requerido | A+W; nombre informativo, no crea un cliente |
| `direccion_entrega` | nvarchar(1000), requerido | A+W; dirección informativa |
| `producto` | nvarchar(500), opcional | A+W; descripción informativa |
| `numero_contenedor`, `ruta` | nvarchar(200), opcionales | A+W; datos de transporte |
| `bultos` | int >= 0, opcional | A+W |
| `peso_kg` | decimal(18,6) >= 0, opcional | A+W |
| `fecha_carga_prevista` | date, opcional | A+W |
| `booking`, `bl_guia` | nvarchar(200), opcionales | A+W sólo informa una propuesta; son **propiedad ERP**, las diferencias generan conflicto |
| `operacion` | nvarchar(20), requerida | A+W: `Alta`, `Modificacion`, `Cancelacion` |
| `creada_at` | datetimeoffset(3) | Default de la tabla al insertar A+W |
| `erp_resultado` | nvarchar(30), opcional | **Sólo ERP**: NULL pendiente, `Aplicada`, `Aplicada (idempotente)`, `Error`, `Rechazada` |
| `erp_motivo` | nvarchar(500), opcional | **Sólo ERP**: código de rechazo |
| `erp_procesada_at` | datetimeoffset(3), opcional | **Sólo ERP**: fecha de resultado |

`dbo.aw_otr_envio_linea`, insertada por A+W, sólo SELECT para ERP:

| Columna | Tipo | Significado |
|---|---|---|
| `otr_envio_id`, `version` | nvarchar(100), bigint | FK a cabecera |
| `numero_pedido` | nvarchar(50) | Número A+W, no ID interno ERP |
| `posicion` | int > 0 | Posición del pedido |
| `cantidad` | decimal(18,6) > 0 | Cantidad propuesta |
| `unidad` | nvarchar(20) | Debe coincidir con la unidad de la posición del ERP |

PK de línea: `(otr_envio_id, version, numero_pedido, posicion)`.
El índice de barrido selecciona `erp_resultado IS NULL` o `Error`, orden por id y versión. Cada fila tiene control local de intentos: un error técnico informa `CE_OTR_ERROR_TECNICO` y mensaje recortado a 500 caracteres, sin detener el lote; al alcanzar `MaxIntentos` queda `Rechazada`. Los rechazos de negocio son definitivos.

## Aplicación, vínculo y conflictos

- La identidad local es única por `(empresa, id de origen)`. Versión menor o misma versión con el mismo hash: `Aplicada (idempotente)`. Misma versión con hash distinto: `Rechazada`, `CE_OTR_VERSION_CONTENIDO_DISTINTO`; conserva el contenido previamente aplicado. El SHA-256 corresponde al contenido estructurado persistido.
- Cliente o sucursal sin equivalencia: el envío queda por vincular con `CE_OTR_CLIENTE_DESCONOCIDO` o `CE_OTR_SUCURSAL_DESCONOCIDA`; no se crean datos maestros. Se requiere corregir el catálogo y publicar una nueva versión.
- Envío sin expediente: bandeja «Envíos OTR por vincular». CE lo vincula a una exportación
  abierta del mismo cliente y sucursal o abre una nueva con DDP propuesto y validación de
  responsable/moneda. Un vínculo existente no se puede mover a otro expediente.
- La consulta `IPedidosEmbarcablesReadPort.BuscarPorNumeroAsync` conserva Facturación como
  fuente. Pedido ausente: `CE_OTR_PEDIDO_INEXISTENTE`, línea Pendiente. Más de un pedido con el mismo número A+W: `CE_OTR_PEDIDO_AMBIGUO`, línea Pendiente. K3–K5 conservan
  sus códigos (`CE_CANTIDAD_EXCEDE_PEDIDO`, `CE_PEDIDO_NO_ASIGNABLE`,
  `CE_SUCURSAL_DISTINTA`, `CE_PEDIDO_CON_EXPEDIENTE`). Unidad diferente:
  `CE_OTR_UNIDAD_DISTINTA`. No se provisionan pedidos ni datos maestros.
- Una partida propuesta reserva cantidad bajo K3 y queda `RequiereRevision` en CE hasta
  su confirmación explícita. Una nueva versión conserva las asignaciones anteriores y las
  pone en revisión; no duplica cantidades. La nueva línea queda Pendiente con
  `CE_OTR_PARTIDA_EN_REVISION` mientras exista asignación anterior de esa posición.
- OTR controla número, ruta, bultos, peso y carga prevista; ERP controla booking, BL,
  pedimento, embarque real e hitos. Sello permanece capturado por ERP. Una edición manual
  de un campo OTR guarda actor y fecha; las diferencias posteriores conservan el ERP y
  registran ambos valores, versión OTR y recepción. Las asignaciones de piezas editadas
  manualmente también guardan protección por pedido/posición. Si difieren de lo recibido,
  quedan Pendientes con `CE_OTR_CONFLICTO_PARTIDA`. «Usar OTR» reemplaza esa asignación
  bajo K3–K5 y la deja en revisión; una factura vigente impide retirar la partida. «Conservar
  ERP» mantiene las cantidades manuales y audita la decisión.
- Una versión nueva cierra conflictos abiertos anteriores como `Superado`, con `SuperadoPorVersion`; dejan de contar como pendientes y conservan su evidencia.
- Resolver exige `comercio_exterior.otr.gestionar`, versión vigente y motivo de 5–500
  caracteres. «Usar OTR» aplica el valor con las validaciones de CE-02; «Conservar ERP»
  conserva la protección manual. Se auditan actor, fecha, decisión y motivo. Una decisión
  sobre un valor ERP ya modificado o versión antigua se rechaza
  (`CE_OTR_CONFLICTO_DESACTUALIZADO`); no hay sobrescritura silenciosa.
- Cancelación de origen: vínculo Cancelado, partidas en revisión; pedidos y facturas se
  conservan. Un id cancelado no se reactiva (`CE_OTR_CANCELADO`); se requiere un nuevo id.
- Cierre financiero sigue bloqueado por CE-03 (`CE_CIERRE_FINANCIERO_PENDIENTE`). El
  contrato de salida de cierre está preparado, sin habilitar el cierre en CE-04.

## Salida, transacción y acuse

CE genera `comercio_exterior.otr_salidas` dentro del mismo `SaveChangesAsync` que el hito,
booking o embarque. No se usa Service Bus para consumir eventos del propio módulo. Llave
única `(envío, tipo, versión ERP, intento)`; versión ERP = máximo + 1 por envío y tipo, considerando filas persistidas y las nuevas del mismo guardado. Es independiente de la versión del expediente.
Tipos: `hito`, `booking`, `booking-cancelado`, `embarque`, `contenedor-cancelado`,
`despacho-aduanal`, `liberado`, `entregado`, `cerrado`, `estado-expediente`.

`dbo.erp_otr_estado`:

| Columna | Tipo SQL Server | Quién escribe |
|---|---|---|
| `salida_id` | uniqueidentifier PK | ERP, nuevo id por reintento manual; estable durante reintentos automáticos de entrega |
| `otr_envio_id` | nvarchar(100) | ERP |
| `tipo` | nvarchar(50) | ERP |
| `version_erp` | int > 0 | ERP; contador por envío y tipo |
| `intento` | int > 0, DEFAULT 1 | ERP; UNIQUE con envío, tipo y versión ERP |
| `valor_json` | nvarchar(max), ISJSON | ERP; estado e hitos con fechas, booking, embarque y cancelación del contenedor |
| `creada_at` | datetimeoffset(3) | Default al insertar ERP |
| `aw_acuse_at` | datetimeoffset(3), opcional | **Sólo A+W** |
| `aw_resultado` | nvarchar(30), opcional | **Sólo A+W**: `Aplicada` o `Rechazada` |
| `aw_motivo` | nvarchar(500), opcional | **Sólo A+W** |

Ejemplo de `valor_json`:

```json
{"expedienteId":"00000000-0000-0000-0000-000000000001","estado":"Abierto","hito":"entregado","hitos":[{"Clave":"entregado","FechaReal":"2026-10-16"}],"contenedor":[{"Booking":"DEMO-BOOK","FechaEmbarqueReal":"2026-10-15","estado":"Activo"}]}
```

A+W debe ignorar duplicados por `(envío,tipo,versión ERP,intento)`, aplicar versiones ERP superiores y permitir un intento nuevo de una versión antes rechazada. Una versión ya aplicada se confirma también como Aplicada. A+W escribe fecha, resultado y motivo juntos tras aplicar. ERP sólo inserta y lee: nunca modifica columnas `aw_*` ni borra filas puente.

Estados locales: Pendiente → Entregada → Acusada. `MaxIntentos` limita exclusivamente errores al insertar. Una salida Entregada conserva su estado mientras no llegue el acuse y se consulta en cada ciclo, sin reentregarla. Al superar `AcuseVencidoMinutos` aparece marcada «Sin acuse» en la bandeja, sin cambiar su estado. Un acuse tardío Aplicada pasa a Acusada.

Acuse Rechazada → Fallida. El reintento manual crea otra salida Pendiente con nuevo `salida_id`, misma versión ERP e `intento + 1`; la fallida queda Reemplazada con `ReemplazadaPorId`. La fila y el rechazo originales permanecen en el puente. Está disponible con permiso OTR y alcance de sucursal, incluso tras cierre o cancelación.

La bandeja del lanzador contiene dos listas: envíos por vincular, y salidas Fallidas o Sin acuse de todos los expedientes dentro del alcance de sucursal.

## Encendido, Demo y operación

`IntegracionesAw:Exportacion`:

| Clave | Omisión | Regla |
|---|---|---|
| `EntradaHabilitada` | false | Independiente de salida y de flujos 1/2 |
| `SalidaHabilitada` | false | Independiente de entrada y de flujos 1/2 |
| `EmpresaId` | null | Vacío o Guid.Empty: no procesa ni abre conexiones |
| `IntervalSeconds` | 120 | > 0; espera inicial antes del primer barrido |
| `BatchSize` | 50 | 1–1000 |
| `MaxIntentos` | 5 | > 0, errores técnicos de entrada y errores de entrega de salida |
| `AcuseVencidoMinutos` | 1440 | > 0; marca visual Sin acuse sin cambiar el estado Entregada |
| `ContratoI134Firmado` | false | Candado real adicional; activar sólo con evidencia de I13.4 firmado |

Real se registra únicamente con contrato firmado, conexión `AwIntegracionDb` y bandera de
la dirección activa. `Demo` usa la misma selección `AwOrigenSesion`, registro keyed `Demo`
y `AwOrigenPgDb`; requiere `IntegracionesAw:OrigenDemo:Permitido`, y nunca en Production.
No hay autenticación simulada: HTTP conserva EntraId + JWT del API y filtro territorial CE.
Ver requiere `expedientes.leer`; gestionar usa `otr.gestionar` y alcance CE por sucursal.

Scripts para Jorge: [12](../operacion/aw-integracion-scripts/12_create_otr_tables.sql) y
[13](../operacion/aw-integracion-scripts/13_grant_otr.sql). Login ERP existente
`millet_erp_integracion`; login propuesto A+W `aw_otr`, por confirmar. No ejecutar scripts
sin acuerdo; no conceden permisos sobre tablas internas del proveedor.

Demo sintética: `tools/aw-origen-demo/schema.sql` y `otr.sql`. Después de preparar el origen,
`aw-origen-demo.sh otr` carga dos envíos y la versión 2 del primero (peso 100 → 125).
Use `OTR_SUCURSAL_CLAVE_AW` y `OTR_CLIENTE_NUMERO_AW` si difieren de las referencias A+W del catálogo DEMO. El pedido
`DEMO-PED-EXP-P1` ya tiene cantidades asignadas en CE-02: se respetan K3–K5, y la línea
permanece pendiente hasta corregir/liberar su asignación previa. El segundo usa un pedido
inexistente a propósito. No se eliminan resultados ni controles ERP al resembrar.

Frecuencia real A+W, equivalencias cliente/sucursal, tolerancias de volumen, retención,
responsables de recuperación y firma I13.4: **Por confirmar con Millet y Jorge**.
