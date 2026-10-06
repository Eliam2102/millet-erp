# 10 — Contrato de API de periodos contables (F1-CON-03)

Base `/api/v1/contabilidad/periodos`. Contrato contrastado con los endpoints y DTOs de la rama el 2026-10-06.
La empresa procede del token (ADR-0011); el periodo pertenece a la empresa y no exige alcance por sucursal.
Todas las rutas requieren autenticación y el permiso indicado. Fechas `yyyy-MM-dd`, instantes ISO 8601 y enums como texto.

## Rutas y permisos

| Método y ruta relativa | Cuerpo / consulta | Permiso `contabilidad.periodo.*` | Respuesta |
|---|---|---|---|
| `GET /ejercicios` | — | `leer` | 200 lista de ejercicios, año descendente, con sus 13 periodos |
| `POST /ejercicios` | `{anio}` (2000–2999) | `administrar` | 201 ejercicio + Location + ETag; 409 año existente |
| `GET /ejercicios/{id}` | — | `leer` | 200 ejercicio + ETag; 404 |
| `POST /ejercicios/{id}/abrir` | `{numeros: [1,2], motivo?}` | `administrar` | 200 ejercicio + ETag; 404/409/422/428 |
| `POST /{periodoId}/cerrar` | `{motivo}` | `cerrar` | 200 periodo + ETag; 404/409/422/428 |
| `POST /{periodoId}/reabrir` | `{motivo}` | `reabrir` | 200 periodo + ETag; 403/404/409/422/428 |
| `GET /{periodoId}/bitacora` | — | `leer` | 200 lista ordenada por versión resultante; 404 |
| `GET /estado` | `fecha=2026-09-15` o `anio=2026&numero=13` | `leer` | 200 estado; 400 consulta inválida |

Los POST exigen `Idempotency-Key` (ADR-0020). Apertura exige `If-Match` con la **versión del ejercicio**;
cierre y reapertura con la **versión del periodo**. Ausente o inválido: 428; versión desactualizada: 409
`CONCURRENCY_CONFLICT`. Repetir una petición con la misma clave conserva la respuesta original, sin repetir la transición.
Los errores usan Problem Details con `code`; FluentValidation rechaza cuerpos inválidos con 400.

## DTOs

- Ejercicio: `{id, anio, version, periodos[]}`.
- Periodo: `{id, ejercicioId, anio, numero, nombre, fechaInicio, fechaFin, estado, esAjuste, abiertoPor?, abiertoEn?,
  cerradoPor?, cerradoEn?, reabiertoPor?, reabiertoEn?, version}`.
- Estado: `{anio, numero, estado, existe, admiteMovimientos}`. Sin ejercicio: `existe=false`, `estado=NoAbierto`,
  `admiteMovimientos=false`; la consulta devuelve 200 para expresar ese estado.
- Bitácora: `{id, accion, estadoAnterior, estadoNuevo, motivo?, usuarioId?, usuarioNombre, ocurridoEn, versionResultante}`.

Estados `NoAbierto`, `Abierto`, `Cerrado`; acciones `Abrir`, `Cerrar`, `Reabrir`.
`admiteMovimientos` significa periodo existente y abierto; el verificador aplica además el origen permitido para el 13.

## Transiciones y errores de negocio

Crear genera los 13 periodos sin abrir. La apertura acepta números únicos 1–13, en un lote atómico; motivo opcional de hasta
500 caracteres. Cierre y reapertura exigen motivo de 10–500 caracteres tras quitar espacios de los extremos.
Cierre exige los anteriores del ejercicio cerrados; reapertura exige que el siguiente no esté cerrado.

| Código | HTTP / condición |
|---|---|
| `CONTAB_EJERCICIO_EXISTE` | 409: año ya creado en la empresa |
| `CONTAB_EJERCICIO_NO_ENCONTRADO`, `CONTAB_PERIODO_NO_ENCONTRADO` | 404: recurso por ID inexistente |
| `CONTAB_PERIODO_YA_CERRADO` | 409: repetir cierre con la versión vigente |
| `CONTAB_PERIODO_NO_ABRIBLE`, `CONTAB_PERIODO_NO_CERRADO` | 422: transición desde estado incompatible |
| `CONTAB_PERIODO_ANTERIOR_ABIERTO` | 422: falta cerrar un periodo anterior (incluye sin abrir) |
| `CONTAB_PERIODO_SIGUIENTE_CERRADO` | 422: reabrir primero el siguiente |
| `CONTAB_PERIODO_13_REQUIERE_12_CERRADO` | 422: apertura del 13 antes del cierre de diciembre |
| `CONTAB_PERIODO_MOTIVO_INVALIDO` | 422: validación de dominio del motivo; HTTP lo valida también antes con FluentValidation |
| `CONTAB_PERIODO_INEXISTENTE`, `CONTAB_PERIODO_NO_ABIERTO`, `CONTAB_PERIODO_CERRADO` | 422: confirmar movimiento en periodo que no admite registros |
| `CONTAB_PERIODO_13_SOLO_MANUAL` | 422: origen distinto de Manual en el verificador por número |

Los periodos 1–12 son meses naturales. El 13 tiene fecha 31-dic, pero una consulta **por fecha siempre resuelve diciembre al 12**;
se accede al 13 por año y número. La autorización de pólizas manuales corresponde a C1.3; aún no existe en este contrato.
Reabrir contabilidad no altera el cierre de inventario (D18).

## Consumidores y límites

`IPeriodoContableConsultaPort` en `Contabilidad/Application/PublicPorts` expone `ConsultarPorFechaAsync(fecha, ct)` y
`ConsultarAsync(anio, numero, ct)` dentro de la empresa actual. Su adaptador consulta el esquema de Contabilidad.
`VerificadorPeriodoContable` aplica el bloqueo antes de confirmar el movimiento de prueba de CON-02;
`POST /api/v1/contabilidad/movimientos/validar` devuelve 200 con `valido=false` y el error de periodo en `fechaContable`.

Los NoOp de Facturación, Tesorería y Almacén siguen pendientes de C1.2. El puerto es de lectura y no publica eventos.
Consulte [evidencia y pendientes](11-evidencia-f1-con-03.md) y [ADR-0058](../../decisiones/0058-periodos-contables-y-contrato-de-consulta.md).

## Persistencia del historial

`GET /{periodoId}/bitacora` lee exclusivamente `core.audit_log`, con operaciones `abrir`, `cerrar` y `reabrir`
de `PeriodoContable`. El detalle estructurado está en `metadatos`; la respuesta HTTP conserva sus campos.
Se valida que el periodo pertenezca a la empresa actual y se filtra también la empresa del registro central.
El permiso sigue siendo `contabilidad.periodo.leer`; no concede lectura de auditoría de otros recursos.
Cada transición se inserta junto al cambio de estado en el mismo contexto y transacción.
