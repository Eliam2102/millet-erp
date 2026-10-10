# 03 — Contrato de API del catálogo contable (F1-CON-01)

Base `/api/v1/contabilidad`. Convenciones del repo: ETag en `GET` por id; `If-Match` obligatorio en mutaciones sobre existentes
(428 si falta, 409 si no coincide, ADR-0012); `Idempotency-Key` en las mutaciones (ADR-0020); errores Problem Details con `code` (ADR-0010).
Enums como texto (`Deudora`, `Titulo`, `Clientes`, `Manual`…). La empresa sale siempre del token; un `empresaId` en body/query se ignora.
Una cuenta de otra empresa responde 404.

## Rutas y permisos

| Método y ruta | Permiso | Respuestas |
|---|---|---|
| `GET /cuentas?estatus=&tipo=&q=&padreId=&pendientes=&clase=&rubroId=&offset=&limit=` | `contabilidad.catalogo.leer` | 200 paginado. `clase` = `Cuenta`\|`Rubro`; `rubroId` = cuentas de nivel 1 del rubro (P24); `pendientes` = cuentas (no rubros) sin naturaleza |
| `GET /cuentas/arbol?estatus=&raizId=` | leer | 200 hijas directas de `raizId` (null = raíces), carga perezosa. Los rubros no son nodos del árbol |
| `GET /cuentas/siguiente-codigo?padreId=` | administrar | 200 `{codigo, motivo}`: código sugerido (editable) para una hija nueva; `codigo` null + `motivo` si no se puede inferir (rama llena o padre sin hijas en ancho libre); 422 si el padre no existe, está inactivo o es un rubro (con derivación, un padre afectable sí recibe sugerencia: P20) |
| `GET /cuentas/{id}` | leer | 200 + `ETag`; incluye `usada` y `origenes`; 404 |
| `POST /cuentas` | `contabilidad.catalogo.administrar` | 201 + ETag; 409 código duplicado; 422 reglas |
| `PUT /cuentas/{id}` (`If-Match`) | administrar | 200; 404; 409; 422; 428 |
| `POST /cuentas/{id}/desactivar` · `/reactivar` (`If-Match`) | administrar | 200; 422; 409; 428 |
| `POST /cuentas/validar-movimiento` | leer | 200 `{valida, motivo?, cuenta?}` (mismo puerto que usan los módulos) |
| `GET /configuracion-formato` | leer | 200 configuración activa (§20.1) |
| `POST /importaciones/vista-previa` | `contabilidad.catalogo.importar` | 200; 422 formato |
| `POST /importaciones/perfilado` | importar | 200 reporte de solo lectura; 422 formato |
| `POST /importaciones` | importar | 201 lote; 200 `idempotente=true`; 422 `CONTAB_IMPORT_FILAS_CON_ERRORES` con `errores[]` (sin escritura); 409 |
| `GET /importaciones` | leer | 200 lotes |

Sin permiso: 403 `PERMISO_FALTANTE`; sin token: 401. El «usuario de consulta» es un rol con solo `leer`.
`RegistrarUsoCuentaCommand` NO tiene ruta HTTP (solo Contabilidad escribe `cuentas_contables_uso`).

## Cuerpo de cuenta (alta / edición)

`{ codigo (solo alta), nombre, padreId?, naturaleza?, tipo?, cuentaControl, codigoAgrupador?, grupoReporte?, clase? (solo alta, Cuenta|Rubro), rubroId? }`.
Con `Tipo.DerivarPorJerarquia` (encendida) **`tipo` se ignora**: el sistema lo calcula (nivel 1 o con hijas ⇒ `Titulo` = acumula;
nivel ≥ 2 sin hijas ⇒ `Afectable`) y la respuesta lo devuelve. Dar una hija a una afectable la convierte en acumula si no tiene
movimientos (P20); con movimientos ⇒ 422 `CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO`; si es colectiva ⇒ 422 `CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE`.
`cuentaControl`: `Ninguna`, `Clientes`, `Deudores`, `Proveedores`, `Acreedores` (P23). `rubroId` solo en cuentas sin padre; un PUT sin
`rubroId` deja la cuenta sin rubro. La respuesta incluye `clase` y `rubroId`; `pendienteValidacion` = naturaleza nula y no rubro.

## Cuerpo de importación

`{ fuente?, archivoNombre?, csvBase64? | columnas[] + filas[][], huella? }`. Con `huella` distinta de la calculada: 409 `CONTAB_IMPORT_HUELLA_NO_COINCIDE`.
Detalle del contrato de archivo: `05-contrato-importacion.md`.

## Códigos de error

Cuentas: `CONTAB_CUENTA_NO_ENCONTRADA` (404), `CONTAB_CUENTA_CODIGO_INVALIDO`, `CONTAB_CUENTA_NOMBRE_INVALIDO`, `CONTAB_CUENTA_PADRE_INVALIDO`,
`CONTAB_CUENTA_PADRE_NO_ES_TITULO`, `CONTAB_CUENTA_CICLO`, `CONTAB_CUENTA_NIVEL_EXCEDIDO`, `CONTAB_CUENTA_NATURALEZA_INVALIDA`,
`CONTAB_CUENTA_AFECTABLE_CON_HIJAS`, `CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE`, `CONTAB_CUENTA_CONTROL_CONFLICTO`,
`CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO` (también: padre con movimientos que recibiría una hija, P20), `CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS`, `CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA` (alta manual: el código no empieza con la parte significativa del padre o no agrega exactamente un nivel), `CONTAB_CUENTA_RUBRO_INVALIDO` (rubro con padre o colectivo, rubro inexistente, rubro en una cuenta con padre) (todos 422), `CONTAB_CUENTA_CODIGO_DUPLICADO` (409).
`CONTAB_CUENTA_PADRE_NO_ES_TITULO` solo con la derivación apagada. Un padre que es rubro ⇒ `CONTAB_CUENTA_PADRE_INVALIDO`.
Puerto/validar-movimiento (`motivo`): `NoExiste`, `Rubro`, `Inactiva`, `PendienteValidacion`, `Titulo` (= la cuenta acumula), `ControlSoloAuxiliar` (colectiva: solo su módulo; `Manual` siempre se rechaza).
Importación: ver `05-contrato-importacion.md` (tabla completa con sugerencias). Concurrencia estándar: `CONCURRENCY_CONFLICT` (409).

## Puerto de lectura (módulos consumidores)

`ICuentaContableReadPort` (`Millet.Contabilidad.Application.PublicPorts`): `ValidarParaMovimientoAsync(codigo|id, OrigenMovimiento)` y `ObtenerAsync(id)`.
Orden de rechazo: NoExiste → Rubro → Inactiva → PendienteValidacion (naturaleza nula) → Titulo (acumula; un `tipo` nulo previo a P19 cuenta como acumula) → ControlSoloAuxiliar. La empresa sale del contexto, no es parámetro.
Orígenes por colectiva (SUPUESTO por defecto, P23): Clientes y Deudores ⇒ `AuxiliarCxC`; Proveedores y Acreedores ⇒ `AuxiliarCxP`.
La UI lo usa en el detalle de la cuenta («Probar si la cuenta acepta movimientos»).
