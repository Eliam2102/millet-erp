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

- **Dimensiones = niveles Dim1/Dim2/Dim3 de `Millet.CentrosCosto`** (mismos IDs). Un valor capturado en un nivel inferior deriva los
  superiores; "obligatorio" se cumple con el valor derivado y "no aplica" solo rechaza lo capturado directamente.
- **Regla** en `contabilidad.reglas_dimension`: cuenta **o rama** (hereda a descendientes), tipo de documento o todos, dimensión,
  requerimiento, `vigente_desde`/`vigente_hasta` (DateOnly). Resolución: gana la cuenta más cercana y, a igual cuenta, el tipo específico.
  Sin regla ⇒ `SinReglaEs` (configurable, por defecto opcional).
- **Historia**: sin traslapes (handler bajo advisory lock; sin `btree_gist`), sin retroactividad por defecto, una regla en vigor no se
  edita: se cierra y se crea otra. Cada movimiento guarda el snapshot de las reglas con que se validó.
- **Centros por sucursal** en `contabilidad.centros_costo_sucursal` (Dim2 ↔ sucursal, N:M; Dim3 hereda). Vive en Contabilidad, no en
  CentrosCosto: es el alcance contable del centro y respeta la decisión de ADM-08 de no ligar el catálogo a sucursales.
  Interruptor `ExigirSucursalDelCentro`.
- **Tipos de documento** en catálogo propio `contabilidad.tipos_documento_contable` (no `TipoDocumentoSerie`, que es de folios).
- **Movimientos de prueba** (`contabilidad.movimientos_dimension_prueba`) mientras no hay pólizas; no marcan la cuenta como usada.
  `PLATFORM-TODO(<Polizas>)`.
- **Dependencias**: Contabilidad declara puertos de consumidor (`ICentroCostoContabilidadPort`, `ISucursalContabilidadPort`); los
  implementan los dueños del dato (CentrosCosto y Compartido) y se cablean en `Program.cs`. Contabilidad no referencia esos módulos.
- **Alcance por sucursal** con el patrón ADR-0051: `UsuarioSucursal` + bypass `contabilidad.movimientos.gestionar-todas-sucursales`.
- **Puerto público** `IDimensionContableValidacionPort` para futuros consumidores.

## Consecuencias

Positivas: la política real entra como datos (reglas, tipos, asignaciones) y configuración, sin código; la historia se conserva.
Negativas: la relación centro ↔ sucursal es un dato nuevo que Millet debe entregar; hasta entonces solo hay datos de prueba.
Si Contabilidad pide reglas por rango no jerárquico o por otras dimensiones (proyecto, línea), se reabre este ADR.
