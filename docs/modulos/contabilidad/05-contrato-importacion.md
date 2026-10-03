# 05 — Contrato de importación del catálogo contable (entregable a Contabilidad)

> Ejemplos ficticios (`FIX-*`). Cómo pedir el archivo: **texto plano (CSV UTF-8 o .xlsx), códigos como TEXTO (no numérico), sin saldos**.

## Columnas

| Canónica | Obligatoria | Alias aceptados por defecto (sin acentos, minúsculas, espacios/guiones → `_`) |
|---|---|---|
| `codigo` | sí | codigo, numero, account_code, clave |
| `nombre` | sí | nombre, cuenta, descripcion, name, description |
| `codigo_padre` | no | codigo_padre, padre, cuenta_padre, parent, parent_code |
| `naturaleza` | no (advertencia) | naturaleza, nature |
| `tipo_cuenta` (acumula/afectable) | no; **no manda** con derivación (P19) | titulo_afectable, afectabilidad, account_type, tipo_cuenta |
| `cuenta_control` (cuenta colectiva) | no | cuenta_control, control |
| `codigo_agrupador` | no | codigo_agrupador, agrupador, codigo_agrupador_sat |
| `grupo_reporte` | no | grupo_reporte, grupo, **reporte** (P22; se conserva el valor recibido) |
| `fuente`, `codigo_origen` | no | fuente/source; codigo_origen/origen/source_code (correspondencia con otro sistema) |
| `nivel_contable` | no | solo se valida contra el nivel derivado (advertencia) |

Cabeceras conocidas **sin mapeo** (se ignoran con advertencia): `tipo`, `nivel de cuenta SAT`. Cualquier otra cabecera desconocida: advertencia. Todo en `Contabilidad:Catalogo:Importacion`.
La columna `Tipo` (`Importacion.ColumnaClasificacion`) no se carga, pero se lee para reconocer **títulos de reporte** (`TiposTitulo`: titulo, título) y **rubros** (`TiposRubro`: rubro, acumula rubro).

**Formato de Contabilidad** («Plan de cuentas»): fila 1 = nombre de empresa (el cliente la descarta al detectar el encabezado en la fila 2),
encabezado `Nivel Contable | Numero | Cuenta | Tipo | Naturaleza | Reporte | Nivel de cuenta SAT | Código agrupador SAT` y, opcional,
`Cuenta padre` para relacionar explícitamente un código cuyo padre por segmentos no existe (P26). Muestra ficticia:
`5-FIX-formato-Laura.xlsx` (fuera del repo) y `FixturesCatalogo.FormatoLaura` (pruebas).

## Valores y reglas

- `naturaleza`: Deudora/Acreedora y alias (`d`, `a`…); celda vacía = pendiente de validación (no se supone), salvo en rubros. `tipo_cuenta`: Titulo/Afectable y alias (`t`, `a`), solo informativo con derivación. `cuenta_control`: Ninguna, Clientes, Deudores, Proveedores, Acreedores (o su singular).
- **Tipo derivado (P19):** nivel 1 o con hijas ⇒ acumula; nivel ≥ 2 sin hijas ⇒ afectable. Si el archivo trae `tipo_cuenta` distinto: aviso `CONTAB_IMPORT_TIPO_DERIVADO` y se guarda el derivado.
- **Títulos de reporte (P21):** fila sin código con Tipo «Titulo/Título» ⇒ acción `Omitida`, aviso `CONTAB_IMPORT_FILA_TITULO`; no se carga ni entra en la huella. Una fila sin código que no es título: error `CONTAB_IMPORT_CODIGO_FORMATO`.
- **Rubros (P24, propuesta del TL):** fila con Tipo «Rubro»/«Acumula rubro» ⇒ clase `Rubro` (sin padre, acumula, sin exigir naturaleza). Las cuentas de nivel 1 debajo del rubro, en el orden del archivo y hasta el siguiente rubro, quedan asociadas a él (`RubroPorOrden`). Un rubro no puede ser padre (`CONTAB_CUENTA_PADRE_INVALIDO`) ni tener padre explícito (`CONTAB_CUENTA_RUBRO_INVALIDO`); una cuenta existente no cambia de clase por importación.
- `codigo`: se recorta, se colapsan espacios (incl. NBSP) y se pasa a mayúsculas; debe cumplir `Codigo.Patron` y longitud. Los ceros perdidos solo se reponen si `Codigo.RellenoCeros` lo declara, con advertencia por fila.
- Único por empresa (incluye inactivas). Duplicados dentro del archivo y de `fuente + codigo_origen`: error.
- Jerarquía: `Jerarquia.Modo = PorSegmentos` (padre = código con el último segmento numérico distinto de cero puesto a ceros) o `PorColumna`; un `codigo_padre` explícito siempre se acepta (y manda sobre los segmentos; el nivel contable se compara con el nivel que resulta del padre explícito). El padre debe existir (archivo o catálogo) y estar activo; sin ciclos; nivel máximo `NivelMaximo` (10). El padre puede venir después del hijo. Un padre existente **afectable sin movimientos** que recibe hijas pasa a acumular (aviso `CONTAB_IMPORT_PADRE_CONVERTIDO`); con movimientos o colectivo, la fila hija se rechaza (P20). Los códigos se conservan completos, incluidos los de 13 caracteres (P27).
- Cuenta colectiva: la lista `CuentasControl` de configuración marca la cuenta; la columna, si existe, debe coincidir; solo en cuentas que reciben movimientos (nivel ≥ 2 sin hijas).
- Cuenta usada (con movimientos, ella o sus hijas): no se cambia naturaleza, tipo ni padre (`CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO`).
- **Upsert no destructivo**: lo ausente del archivo no se desactiva; celda vacía conserva el valor; el código no se modifica; identidad por `(fuente, codigo_origen)` y luego por `codigo`.

## Tolerancias

BOM UTF-8/UTF-16; UTF-8 con respaldo Windows-1252 (advertencia); delimitador `,` `;` o tab; comillas CSV; CRLF/LF; filas vacías ignoradas y contadas; límite 5 000 filas (`CONTAB_IMPORT_LIMITE_FILAS`). Número de fila = registro del archivo (cabecera = 1).

## Errores y advertencias `{fila, columna, codigo, severidad, mensaje, sugerencia}`

Errores: `CONTAB_IMPORT_ARCHIVO_VACIO`, `_ARCHIVO_INVALIDO`, `_LIMITE_FILAS`, `_COLUMNA_FALTANTE` (formato, 422 con cabeceras encontradas y alias), `_CODIGO_FORMATO`, `_NATURALEZA_DESCONOCIDA`, `_TIPO_DESCONOCIDO`,
`_PADRE_INEXISTENTE`, `_CODIGO_DUPLICADO_EN_ARCHIVO`, `_CICLO`, `_CONTROL_CONFLICTO`, `_ORIGEN_CODIGO_DISTINTO`, `_FILAS_CON_ERRORES` (aplicar), `_HUELLA_NO_COINCIDE` (409), `_CONFLICTO_CONCURRENTE` (409), más los `CONTAB_CUENTA_*` de `03-contrato-api.md`.
Advertencias: `_CAMPO_PENDIENTE`, `_CODIGO_RELLENADO`, `_COLUMNA_IGNORADA`, `_COLUMNA_SIN_MAPEO`, `_CODIFICACION`, `_NIVEL_DISCREPANTE`, `_FILA_TITULO`, `_TIPO_DERIVADO`, `_PADRE_CONVERTIDO`. La vista previa trae `resumen.omitidas` (títulos). Cada una trae una sugerencia accionable (qué alias o configuración ajustar).

## Perfilado (`POST /importaciones/perfilado`, solo lectura)

Resumen (filas, vacías, inválidas, columnas, codificación, ceros rellenados, **títulos de reporte, rubros, cuentas con rubro**), distribuciones (longitud, nivel, segmentos, separadores, naturaleza/tipo reconocidos o no), estructura (huérfanas, ciclos, duplicados, **acumulan / afectables derivadas, padres que pasarán a acumular**, profundidad), control, **cuentas sin naturaleza** (no rubros), **nivel contable vs derivado**, **columnas sin mapeo** (conteos), errores agrupados por código con ejemplos (solo fila y columna) y «qué se reabre». No exporta códigos ni nombres de cuentas.

## Idempotencia

Huella SHA-256 de las filas normalizadas; el mismo archivo (aunque cambie BOM, delimitador o espacios) devuelve el lote original sin tocar datos.

## Checklist «llegó el archivo real»

1. Guardarlo **fuera del repo**; confirmar origen, fecha y que no traiga saldos. 2. Perfilar en local; no aplicar. 3. Revisar el reporte y «qué se reabre». 4. Clasificar: error de datos (se devuelve a Contabilidad con códigos de error y números de fila), desajuste de configuración (se ajusta `Contabilidad:Catalogo` y se reperfila) o error de código (se reproduce con un `FIX-*` y se corrige con prueba). 5. Reabrir P2/P3/P6/P1/P14–P17 según el reporte. 6. Vista previa sin errores. 7. Aplicar primero en local/QA; reimportar ⇒ idempotente; en el ambiente real solo con aprobación de Contabilidad/Guillermo. 8. Documentar estructura y estadísticas (no contenido).
