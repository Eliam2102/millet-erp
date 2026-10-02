# 05 — Contrato de importación del catálogo contable (entregable a Contabilidad)

> Ejemplos ficticios (`FIX-*`). Cómo pedir el archivo: **texto plano (CSV UTF-8 o .xlsx), códigos como TEXTO (no numérico), sin saldos**.

## Columnas

| Canónica | Obligatoria | Alias aceptados por defecto (sin acentos, minúsculas, espacios/guiones → `_`) |
|---|---|---|
| `codigo` | sí | codigo, numero, account_code, clave |
| `nombre` | sí | nombre, cuenta, descripcion, name, description |
| `codigo_padre` | no | codigo_padre, padre, cuenta_padre, parent, parent_code |
| `naturaleza` | no (advertencia) | naturaleza, nature |
| `tipo_cuenta` (título/afectable) | no (advertencia) | titulo_afectable, afectabilidad, account_type, tipo_cuenta |
| `cuenta_control` | no | cuenta_control, control |
| `codigo_agrupador` | no | codigo_agrupador, agrupador, codigo_agrupador_sat |
| `grupo_reporte` | no | grupo_reporte, grupo |
| `fuente`, `codigo_origen` | no | fuente/source; codigo_origen/origen/source_code (correspondencia con otro sistema) |
| `nivel_contable` | no | solo se valida contra el nivel derivado (advertencia) |

Cabeceras conocidas **sin mapeo** (se ignoran con advertencia): `tipo`, `nivel de cuenta SAT`. Cualquier otra cabecera desconocida: advertencia. Todo en `Contabilidad:Catalogo:Importacion`.

## Valores y reglas

- `naturaleza`: Deudora/Acreedora y alias (`d`, `a`…); `tipo_cuenta`: Titulo/Afectable y alias (`t`, `a`); `cuenta_control`: Ninguna/Clientes/Proveedores. Celda vacía = pendiente de validación (no se supone).
- `codigo`: se recorta, se colapsan espacios (incl. NBSP) y se pasa a mayúsculas; debe cumplir `Codigo.Patron` y longitud. Los ceros perdidos solo se reponen si `Codigo.RellenoCeros` lo declara, con advertencia por fila.
- Único por empresa (incluye inactivas). Duplicados dentro del archivo y de `fuente + codigo_origen`: error.
- Jerarquía: `Jerarquia.Modo = PorSegmentos` (padre = código con el último segmento numérico distinto de cero puesto a ceros) o `PorColumna`; un `codigo_padre` explícito siempre se acepta. El padre debe existir (archivo o catálogo), estar activo y no ser afectable; sin ciclos; nivel máximo `NivelMaximo` (10). El padre puede venir después del hijo.
- Cuenta de control: la lista `CuentasControl` de configuración marca la cuenta; la columna, si existe, debe coincidir; solo afectables (explícitos).
- Cuenta usada (con movimientos, ella o sus hijas): no se cambia naturaleza, tipo ni padre (`CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO`).
- **Upsert no destructivo**: lo ausente del archivo no se desactiva; celda vacía conserva el valor; el código no se modifica; identidad por `(fuente, codigo_origen)` y luego por `codigo`.

## Tolerancias

BOM UTF-8/UTF-16; UTF-8 con respaldo Windows-1252 (advertencia); delimitador `,` `;` o tab; comillas CSV; CRLF/LF; filas vacías ignoradas y contadas; límite 5 000 filas (`CONTAB_IMPORT_LIMITE_FILAS`). Número de fila = registro del archivo (cabecera = 1).

## Errores y advertencias `{fila, columna, codigo, severidad, mensaje, sugerencia}`

Errores: `CONTAB_IMPORT_ARCHIVO_VACIO`, `_ARCHIVO_INVALIDO`, `_LIMITE_FILAS`, `_COLUMNA_FALTANTE` (formato, 422 con cabeceras encontradas y alias), `_CODIGO_FORMATO`, `_NATURALEZA_DESCONOCIDA`, `_TIPO_DESCONOCIDO`,
`_PADRE_INEXISTENTE`, `_CODIGO_DUPLICADO_EN_ARCHIVO`, `_CICLO`, `_CONTROL_CONFLICTO`, `_ORIGEN_CODIGO_DISTINTO`, `_FILAS_CON_ERRORES` (aplicar), `_HUELLA_NO_COINCIDE` (409), `_CONFLICTO_CONCURRENTE` (409), más los `CONTAB_CUENTA_*` de `03-contrato-api.md`.
Advertencias: `_CAMPO_PENDIENTE`, `_CODIGO_RELLENADO`, `_COLUMNA_IGNORADA`, `_COLUMNA_SIN_MAPEO`, `_CODIFICACION`, `_NIVEL_DISCREPANTE`. Cada una trae una sugerencia accionable (qué alias o configuración ajustar).

## Perfilado (`POST /importaciones/perfilado`, solo lectura)

Resumen (filas, vacías, inválidas, columnas, codificación, ceros rellenados), distribuciones (longitud, nivel, segmentos, separadores, naturaleza/tipo reconocidos o no), estructura (huérfanas, ciclos, duplicados, títulos sin hijas, afectables con hijas, profundidad), control, **campos pendientes de validación**, **nivel contable vs derivado**, **columnas sin mapeo** (conteos), errores agrupados por código con ejemplos (solo fila y columna) y «qué se reabre». No exporta códigos ni nombres de cuentas.

## Idempotencia

Huella SHA-256 de las filas normalizadas; el mismo archivo (aunque cambie BOM, delimitador o espacios) devuelve el lote original sin tocar datos.

## Checklist «llegó el archivo real»

1. Guardarlo **fuera del repo**; confirmar origen, fecha y que no traiga saldos. 2. Perfilar en local; no aplicar. 3. Revisar el reporte y «qué se reabre». 4. Clasificar: error de datos (se devuelve a Contabilidad con códigos de error y números de fila), desajuste de configuración (se ajusta `Contabilidad:Catalogo` y se reperfila) o error de código (se reproduce con un `FIX-*` y se corrige con prueba). 5. Reabrir P2/P3/P6/P1/P14–P17 según el reporte. 6. Vista previa sin errores. 7. Aplicar primero en local/QA; reimportar ⇒ idempotente; en el ambiente real solo con aprobación de Contabilidad/Guillermo. 8. Documentar estructura y estadísticas (no contenido).
