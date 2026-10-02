# ADR-0054: Módulo Contabilidad y catálogo de cuentas contables

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-02
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: contabilidad, catalogo, multiempresa, modulos

## Contexto y problema

Facturación, CxP, Almacén y Tesorería apuntan a un módulo Contabilidad que no existía (solo stubs `NoOp`
con cuentas como `string` libre). Antes de automatizar cualquier asiento hace falta un catálogo de
cuentas consumible. Plan: `docs/modulos/contabilidad/01-plan-catalogo-cuentas.md`.

## Decisión

1. **Módulo nuevo `Millet.Contabilidad`**, esquema Postgres `contabilidad`, `ContabilidadDbContext` propio
   (13.º contexto; ADR-0030). No se reutiliza `compartido` ni Centros de Costo.
2. **`EmpresaId` técnico conservado** (`IPerteneceAEmpresa`, ADR-0011). Difiere de Centros de Costo (global, ADM-08):
   aquí la ficha pide conservarlo. **ADR-0051 no aplica**: el catálogo es corporativo, sin `SucursalId`
   ni permiso de bypass por sucursal; las agrupaciones de reporte evitan duplicar cuentas por sucursal.
3. **Baja lógica por estatus** (`Inactivo`), no `DeletedAt`. **Código único por empresa incluyendo inactivas**
   (índice único NO parcial): preserva el histórico. El código es inmutable tras el alta.
4. **`naturaleza` y `tipo` (título/afectable) son anulables** (P14): `NULL` = pendiente de validación de Contabilidad;
   no se suponen ni se derivan de la jerarquía. Una cuenta pendiente no es válida para movimientos
   (`PendienteValidacion` en el puerto de lectura).
5. **Jerarquía** configurable (P15): `PorSegmentos` (padre = código con el último segmento numérico distinto de cero
   puesto a ceros) o `PorColumna`. Nivel almacenado, tope configurable (10), sin ciclos, padre activo y no afectable.
6. **Cuenta de control** (`Clientes`/`Proveedores`) solo en afectables y solo movible por su auxiliar
   (`OrigenMovimiento`); la lista real de cuentas de control es configuración, vacía por defecto.
7. Puerto de lectura público `ICuentaContableReadPort` (patrón `IDim3ReadPort`, el owner hospeda el adaptador).
   Consumidores previstos: Facturación, CxP, Almacén, Tesorería; se cablean en sus propias tareas
   (`PLATFORM-TODO(<ContabilidadCuentasConsumidores>)`). Sin outbox en v1 (`<OutboxContabilidad>`).
8. Permisos `contabilidad.catalogo.{leer,administrar,importar}` (namespace `0000000d-*`, ADR-0041).

## Consecuencias

Positivas: fronteras claras, cero datos de negocio inventados, consumidores desacoplados.
Negativas: un contexto más en migraciones/CI; los consumidores siguen con stubs hasta su cableado;
varias reglas (herencia de naturaleza, tipos de control) dependen de datos de Contabilidad (se reabren con ellos).
