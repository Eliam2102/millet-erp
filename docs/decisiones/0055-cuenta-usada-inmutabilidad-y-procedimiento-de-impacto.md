# ADR-0055: Cuenta usada, campos protegidos y procedimiento de impacto

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-02
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: contabilidad, integridad, auditoria

## Contexto y problema

Cambiar naturaleza, tipo o padre de una cuenta con movimientos altera la interpretación de saldos históricos.
Aún no existen pólizas, por lo que "movimiento" no puede salir de una tabla de pólizas.

## Decisión

- Una cuenta está **usada** si existe una fila en `contabilidad.cuentas_contables_uso` para ella o para un descendiente (P4).
  Solo la escribe `RegistrarUsoCuentaCommand` (Contabilidad; lo invocará el módulo de pólizas). Los consumidores nunca escriben ahí;
  sus referencias (p. ej. `CuentaBancaria.CuentaContableRef`) no cuentan como uso por ahora.
- **Campos protegidos** en cuentas usadas: `naturaleza`, `tipo`, `padre_id`. Intentar cambiarlos (edición o importación)
  responde `CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO` (422) con instrucción de usar el procedimiento de impacto o crear una cuenta nueva y
  desactivar la anterior. La transacción falla antes de mutar: versión, `updated_at`, auditoría y uso quedan intactos.
  Nombre, agrupador y grupo de reporte siguen editables.
- Baja de un título con hijas activas se **bloquea** (P8; sin cascada, a diferencia de ADR-0049).
- **Procedimiento de impacto (reclasificación aprobada por Contabilidad) diferido**: `PLATFORM-TODO(<ContabilidadReclasificacion>)`.
- Cuenta de control: solo se afecta por su auxiliar (`OrigenMovimiento`); R10.

## Consecuencias

Protege el histórico sin construir aún el flujo de reclasificación. Cuando existan pólizas, "usada" pasa a derivarse de ellas.
La casilla "no afectable por asiento manual" (P17) queda abierta y NO se implementa.
