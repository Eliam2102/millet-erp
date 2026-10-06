# ADR-0057: Reglas de dimensión contable con vigencia y alcance de centros por sucursal

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-04
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: contabilidad, centros-costo, sucursal, vigencia

## Contexto y problema

F1-CON-02 pide reglas configurables cuenta × tipo de documento × dimensión (obligatorio / opcional / no aplica) con vigencia, y que
la API rechace con mensaje claro una dimensión faltante, un centro inactivo o un centro de otra sucursal. Restricciones: reutilizar
los centros de ADM-08 (no crear otro catálogo); ADM-08 decidió que su catálogo es global y no se relaciona con sucursales; aún no
existen pólizas; la política real la define Contabilidad de Millet más adelante.

## Decisión

- **Dimensiones** (ficha K10.2): **Dim1 ubicación / Dim2 área (CeCo) / Dim3 equipo de `Millet.CentrosCosto`** (mismos IDs), más **proyecto** (clave libre hasta que exista catálogo, V41), **cliente**, **proveedor** y **banco** (validados contra sus catálogos por puertos). Un valor capturado en un nivel inferior deriva los
  superiores; "obligatorio" se cumple con el valor derivado y "no aplica" solo rechaza lo capturado directamente.
- **Regla** en `contabilidad.reglas_dimension`: cuenta **o rama** (hereda a descendientes), tipo de documento o todos, dimensión,
  requerimiento, `vigente_desde`/`vigente_hasta` (DateOnly). Resolución: gana la cuenta más cercana y, a igual cuenta, el tipo específico.
  Sin regla ⇒ `SinReglaEs` (configurable, por defecto opcional).
- **Historia**: sin traslapes (handler bajo advisory lock; sin `btree_gist`), sin retroactividad por defecto. La protección es por
  **uso** (`reglas_dimension_uso`): una regla que no ha validado movimientos se edita o se borra; una usada solo se cierra, y no antes
  de la última fecha contable que validó. Cada movimiento guarda el snapshot de las reglas con que se validó.
- **Ubicación → sucursal** en `contabilidad.ubicaciones_sucursal` (cada Dim1 a una sucursal; sus Dim2/Dim3 la heredan) y **centros
  corporativos** en `contabilidad.centros_corporativos` (Dim2 usable desde cualquier sucursal), según K10.2/V49. Vive en Contabilidad,
  no en CentrosCosto: es el alcance contable del centro y respeta la decisión de ADM-08. Interruptor `ExigirSucursalDelCentro`.
- **Tipos de documento** en catálogo propio `contabilidad.tipos_documento_contable` (no `TipoDocumentoSerie`, que es de folios).
- **Movimientos de prueba** (`contabilidad.movimientos_dimension_prueba`) mientras no hay pólizas; no marcan la cuenta como usada.
  `PLATFORM-TODO(<Polizas>)`.
- **Dependencias**: Contabilidad declara puertos de consumidor (`ICentroCostoContabilidadPort`, `ISucursalContabilidadPort`,
  `ITerceroContabilidadPort`, `ICuentaBancariaContabilidadPort`); los implementan los dueños del dato (CentrosCosto, Compartido y
  Tesorería) y se cablean en `Program.cs`. Contabilidad no referencia esos módulos.
- **Alcance por sucursal** con el patrón ADR-0051: `UsuarioSucursal` + bypass `contabilidad.movimientos.gestionar-todas-sucursales`.
- **Puerto público** `IDimensionContableValidacionPort` para futuros consumidores.

## Consecuencias

Positivas: la política real entra como datos (reglas, tipos, asignaciones) y configuración, sin código; la historia se conserva.
Negativas: la equivalencia ubicación → sucursal y la lista de corporativos son un supuesto hasta la sesión contable del 16-oct (V49).
Si Contabilidad pide reglas por rango no jerárquico o por otras dimensiones (proyecto, línea), se reabre este ADR.
