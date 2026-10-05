# 07 — Contrato de API de dimensiones contables (F1-CON-02)

Base `/api/v1/contabilidad`. Mismas convenciones que `03-contrato-api.md`: ETag en `GET` por id, `If-Match` en mutaciones de
recursos versionados (428 si falta, 409 si no coincide), `Idempotency-Key` en `POST`/`PUT`, Problem Details con `code`, enums como
texto. Fechas de vigencia y fecha contable como `yyyy-MM-dd`. La empresa sale del token (filtro global ADR-0011).

Enums: `dimension` = `Dim1` (ubicación) | `Dim2` (área/CeCo) | `Dim3` (equipo) | `Proyecto` | `Cliente` | `Proveedor` | `Banco` (ficha K10.2);
`requerimiento` = `Obligatorio` | `Opcional` | `NoAplica`; estado de regla = `Futura` | `Vigente` | `Cerrada`.

## Rutas y permisos

| Método y ruta | Permiso | Respuestas |
|---|---|---|
| `GET /tipos-documento?incluirInactivos=` | `contabilidad.dimensiones.leer` | 200 `[{id, clave, nombre, activo, esPrueba, version}]` |
| `POST /tipos-documento` `{clave, nombre, esPrueba}` | `contabilidad.dimensiones.administrar` | 201 + ETag; 409 `CONTAB_TIPO_DOC_DUPLICADO`; 422 |
| `PUT /tipos-documento/{id}` `{nombre, activo}` (`If-Match`) | administrar | 200; 404; 409; 428. La clave es inmutable |
| `GET /reglas-dimension?cuentaId=&tipoDocumentoId=&dimension=&vigentesA=&esPrueba=&offset=&limit=` | leer | 200 paginado de reglas |
| `GET /reglas-dimension/efectivas?cuentaId=&tipoDocumentoId=&fecha=` | leer | 200 requerimiento efectivo por dimensión (fecha por defecto: hoy); 404 cuenta |
| `GET /reglas-dimension/{id}` | leer | 200 + ETag; 404 |
| `POST /reglas-dimension` | administrar | 201 + ETag; 409 traslape; 422 reglas |
| `PUT /reglas-dimension/{id}` `{requerimiento, vigenteDesde, vigenteHasta?, nota?}` (`If-Match`) | administrar | 200; 422 `CONTAB_REGLA_USADA` si ya validó movimientos; 409; 428 |
| `DELETE /reglas-dimension/{id}` (`If-Match`) | administrar | 204; 422 `CONTAB_REGLA_USADA`; 404; 409; 428 |
| `POST /reglas-dimension/{id}/cerrar` `{vigenteHasta}` (`If-Match`) | administrar | 200; 422 (`CONTAB_REGLA_CIERRE_ANTES_DE_USO` si queda antes de la última fecha contable que validó); 409; 428 |
| `GET /sucursales` | leer | 200 sucursales de la empresa (para configurar centros) |
| `GET /ubicaciones-sucursal` | leer | 200 `[{dim1Id, clave, nombre, activo, sucursal?}]` |
| `PUT /ubicaciones-sucursal/{dim1Id}` `{sucursalId?}` | administrar | 200 (null la desliga); 404 ubicación; 422 sucursal ajena |
| `GET /centros-corporativos?q=&soloCorporativos=&limit=` | leer | 200 `[{dim2Id, clave, nombre, activo, dim1Clave, corporativo}]` |
| `PUT /centros-corporativos/{dim2Id}` `{corporativo}` | administrar | 200; 404 |
| `GET /movimientos/auxiliares?tipo=Cliente\|Proveedor\|Banco&q=&limit=` | validar | 200 activos `[{id, clave, nombre, activo}]` |
| `GET /movimientos/sucursales` | `contabilidad.movimientos.validar` | 200 sucursales que el usuario puede operar (ADR-0051) |
| `GET /movimientos/centros?sucursalId=&nivel=&dim2Id=&q=&limit=` | validar | 200 centros **activos** de las ubicaciones de la sucursal más los corporativos; 403 `SUCURSAL_NO_ASOCIADA` |
| `POST /movimientos/validar` | validar | 200 `ValidacionDimensiones` (no guarda); 400 validación; 403 sucursal |
| `POST /movimientos-prueba` | validar | 201 movimiento con `reglasAplicadas`; 422 `CONTAB_DIM_MOVIMIENTO_INVALIDO` con `errores[]`; 403 |
| `GET /movimientos-prueba?sucursalId=&cuentaId=&offset=&limit=` | leer | 200 paginado; sin bypass solo sucursales propias |
| `GET /movimientos-prueba/{id}` | leer | 200; 404; 403 si es de otra sucursal |

Bypass de alcance: `contabilidad.movimientos.gestionar-todas-sucursales` (corporativo). Sin él, el usuario opera solo las
sucursales con `UsuarioSucursal` activa.

## Regla

`{id, cuentaId, cuentaCodigo, cuentaNombre, tipoDocumentoId?, tipoDocumentoClave?, tipoDocumentoNombre?, dimension, nombreDimension,
requerimiento, vigenteDesde, vigenteHasta?, estado, editable, esPrueba, nota?, version, usada, ultimaFechaUso?}`.

- `cuentaId` puede ser una rama (cuenta que acumula): aplica a sus descendientes salvo regla más específica. No se aceptan rubros
  ni cuentas inactivas.
- `tipoDocumentoId` null = todos los tipos.
- Sin traslape para la misma (cuenta, tipo, dimensión). Sin retroactividad: `vigenteDesde` ≥ hoy y, en una regla en vigor,
  `vigenteHasta` ≥ hoy (`Contabilidad:Dimensiones:PermitirVigenciaRetroactiva` lo permite para la carga inicial real).
- La protección depende del **uso**, no de la fecha: mientras la regla no haya validado ningún movimiento (`usada = false`) se edita o se borra,
  sea futura o vigente. Una regla usada solo se cierra (y no antes de `ultimaFechaUso`) y se crea otra. Motivo: la validación usa la
  **fecha contable** del movimiento, así que una regla futura también puede haber validado movimientos con fecha contable futura.
  El uso se registra en `contabilidad.reglas_dimension_uso` al confirmar un movimiento.

## Movimiento (validar / registrar prueba)

`{cuentaId, tipoDocumentoId, fechaContable, sucursalId, dim1Id?, dim2Id?, dim3Id?, proyecto?, clienteId?, proveedorId?, cuentaBancariaId?, origen (Manual|AuxiliarCxC|AuxiliarCxP), referencia?}`.
`proyecto` es una clave libre (≤ 40) mientras no exista catálogo de proyectos (V41).

Respuesta de validación: `{valido, errores[], requerimientos[], centros: {dim1Id, dim2Id, dim3Id}}`.
`errores[]` = `{codigo, mensaje, campo, dimension?}`; `campo` ∈ `cuentaId`, `tipoDocumentoId`, `sucursalId`, `dim1Id`, `dim2Id`, `dim3Id`,
`proyecto`, `clienteId`, `proveedorId`, `cuentaBancariaId`
(la UI pinta el error junto a ese campo). `requerimientos[]` = `{dimension, nombreDimension, requerimiento, reglaId?,
cuentaOrigenCodigo?, heredada, paraTodosLosTipos, vigenteDesde?, vigenteHasta?, esPrueba}`; `reglaId` null = sin regla.
`centros` incluye los niveles superiores derivados del árbol.

Orden de validación: cuenta (mismas reglas que `ICuentaContableReadPort`) → tipo de documento → sucursal → centros (existe, nivel,
jerarquía, activo en toda la cadena) → ubicación del centro ligada a la sucursal (salvo CeCo corporativo) → auxiliares (cliente,
proveedor, banco: existen y están activos) → reglas vigentes a `fechaContable`. Se devuelven **todos** los errores.
Solo los centros se derivan del nivel inferior; proyecto y auxiliares cuentan solo si vienen capturados.

## Códigos de error

| Código | Cuándo |
|---|---|
| `CONTAB_DIM_OBLIGATORIA_FALTANTE` | Dimensión obligatoria sin valor (ni capturado ni derivado) |
| `CONTAB_DIM_NO_APLICA` | Dimensión «no aplica» capturada directamente (un valor derivado no cuenta) |
| `CONTAB_DIM_CENTRO_NO_EXISTE` · `CONTAB_DIM_CENTRO_NIVEL_INCORRECTO` | Id inexistente o de otro nivel |
| `CONTAB_DIM_JERARQUIA_INCONGRUENTE` | La Dim3/Dim2 capturada no pertenece a la Dim2/Dim1 capturada |
| `CONTAB_DIM_CENTRO_INACTIVO` | El centro o un superior está dado de baja |
| `CONTAB_DIM_UBICACION_SIN_SUCURSAL` · `CONTAB_DIM_CENTRO_OTRA_SUCURSAL` | La ubicación del centro no está ligada a una sucursal o es de otra (y el CeCo no es corporativo) |
| `CONTAB_DIM_AUXILIAR_NO_EXISTE` · `CONTAB_DIM_AUXILIAR_INACTIVO` | Cliente, proveedor o cuenta bancaria inexistente o dado de baja |
| `CONTAB_DIM_CUENTA_NO_VALIDA` · `CONTAB_DIM_TIPO_DOC_INVALIDO` · `CONTAB_DIM_SUCURSAL_INVALIDA` | Cuenta, tipo o sucursal no aptos |
| `CONTAB_DIM_MOVIMIENTO_INVALIDO` (422) | Envoltura de `POST /movimientos-prueba` con `errores[]` |
| `CONTAB_REGLA_VIGENCIA_TRASLAPADA` (409) | Otra regla de la misma combinación se traslapa |
| `CONTAB_REGLA_USADA` · `CONTAB_REGLA_CIERRE_ANTES_DE_USO` · `CONTAB_REGLA_VIGENCIA_RETROACTIVA` · `CONTAB_REGLA_VIGENCIA_INVALIDA` · `CONTAB_REGLA_CUENTA_INVALIDA` · `CONTAB_REGLA_TIPO_DOC_INVALIDO` | 422 de configuración de reglas |
| `CONTAB_TIPO_DOC_DUPLICADO` (409) · `CONTAB_UBICACION_SUCURSAL_INVALIDA` · `CONTAB_UBICACION_NO_ENCONTRADA` · `CONTAB_CENTRO_NO_ENCONTRADO` (404) | Tipos, ubicaciones y corporativos |
| `SUCURSAL_NO_ASOCIADA` (403) | El usuario no opera esa sucursal y no tiene el bypass |

## Puerto público

`IDimensionContableValidacionPort.ValidarAsync(MovimientoDimensionado)` (`Millet.Contabilidad.Application.PublicPorts`): misma validación
sin el chequeo de pertenencia del usuario a la sucursal (autorización de quien llama). Pensado para Pólizas, CxP y Compras
(`PLATFORM-TODO(<DimensionesConsumidores>)`).

## Configuración `Contabilidad:Dimensiones`

| Clave | Default | Efecto |
|---|---|---|
| `SinReglaEs` | `Opcional` | Requerimiento cuando no hay regla vigente |
| `ExigirSucursalDelCentro` | `true` | Exige que la ubicación del centro esté ligada a la sucursal del movimiento (salvo corporativos) |
| `PermitirVigenciaRetroactiva` | `false` | Permite fechas anteriores a hoy al crear o cerrar reglas |
| `ZonaHoraria` | `America/Merida` | Define "hoy" para las vigencias |
| `NombresDimension` | `Dimensión 1/2/3`, Proyecto, Cliente, Proveedor, Banco | Rótulos en mensajes |
