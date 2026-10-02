# ADR-0056: Importación de catálogo: huella, idempotencia, perfilado y formato por configuración

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-02
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: contabilidad, importacion, idempotencia, configuracion

## Contexto y problema

ADR-0044 rechazó una herramienta genérica de importación para proveedores/artículos. Aquí la importación con vista previa,
errores por fila y reimportación idempotente es requisito de aceptación, y el formato real del archivo de Contabilidad
(código, naturaleza, cuentas de control) aún no se conoce: debe entrar por configuración, no por código.

## Decisión

- **Parseo**: `.xlsx` en el cliente (texto crudo por celda); CSV como base64 que el **servidor** decodifica (BOM, UTF-8 con respaldo
  Windows-1252, delimitador). **Un único normalizador en el servidor** (`FormatoCatalogo` + `ImportadorCatalogo`) lo usan CRUD,
  vista previa, perfilado y aplicar; sin lógica duplicada.
- **Operaciones**: vista previa y perfilado (solo lectura, cero escrituras, con prueba) y aplicar (un solo `SaveChanges`,
  todo o nada, `Idempotency-Key`). Límite 5 000 filas configurable.
- **Huella** SHA-256 de las filas canonicalizadas; índice único `(empresa_id, huella)`: reenviar el mismo archivo devuelve el lote
  original (`idempotente=true`). Carreras: la violación de índice se reintenta como idempotente o responde 409.
- **Upsert no destructivo**: lo ausente no se desactiva; una celda vacía conserva el valor existente; el código no se modifica por importación.
- **Identidad**: correspondencia `(fuente, codigo_origen)` primero, luego `codigo`.
- **Configuración** `Contabilidad:Catalogo` (patrón/longitud de código, relleno de ceros declarado, `Jerarquia.Modo`, alias de columnas,
  naturaleza y tipo, columnas sin mapeo, `CuentasControl`, `NivelMaximo`, `MaxFilas`), con `ValidateOnStart`. Defaults permisivos y genéricos.
  Cambiar de formato real = editar configuración; si exige código, se reabre este ADR.
- **Mapeo provisional (P16)**: `Numero`→codigo, `Cuenta`→nombre, `Código agrupador SAT`→codigo_agrupador, `Nivel Contable`→se valida contra el nivel derivado
  (advertencia), `Tipo` y `Nivel de cuenta SAT` no se mapean (advertencia). La columna canónica de título/afectable se llama `tipo_cuenta`
  justamente para no chocar con el `Tipo` de la hoja base.
- **Errores** `{fila, columna, codigo, severidad, mensaje, sugerencia}`; logs sin valores de celdas; datos reales nunca al repo.

## Consecuencias

Positivas: el archivo real se absorbe con configuración; los desajustes salen agrupados y accionables.
Negativas: pieza nueva de mantener; el contrato queda en `docs/modulos/contabilidad/05-contrato-importacion.md`.
