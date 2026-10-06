# ADR-0058: Periodos contables, estados y contrato de consulta

- **Estado**: Propuesta (pendiente de ratificación del owner)
- **Fecha**: 2026-10-06
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: contabilidad, periodos, concurrencia, auditoría

## Contexto y problema

F1-CON-03 requiere abrir, cerrar y reabrir periodos con permisos y auditoría. La ficha C1.1 de la base Obsidian y el
cuestionario 08 definen año natural, 12 periodos ordinarios y el 13 de auditoría; el Contador General realiza la reapertura.
Facturación y Tesorería tienen puertos temporales que siempre contestan abierto. D18 separa el cierre contable del inventario.
Los adaptadores de otros módulos corresponden a C1.2 y las pólizas a C1.3–C1.5.

## Decisión propuesta e implementación

- Un ejercicio por empresa y año (2000–2999), creado con 13 periodos `NoAbierto`. Apertura explícita:
  `NoAbierto → Abierto → Cerrado`; reapertura `Cerrado → Abierto` con permiso separado y motivo.
- Periodos de empresa; no se aplica un guard de sucursal a su administración. Los consumidores conservan su propio alcance.
- Periodo 13: 31-dic, selección por número, nunca por fecha; abre tras cerrar el 12 y solo admite origen Manual.
  La autorización de la póliza se implementará en C1.3.
- Cierre secuencial dentro del ejercicio y reapertura desde el último periodo cerrado hacia atrás. Estas dos reglas son
  **supuestos de diseño VILO**, pendientes de ratificación; no se presentan como respuesta recibida de Millet.
- Motivo obligatorio de 10–500 caracteres en cierre y reapertura. Historial exclusivamente en `core.audit_log`:
  transición explícita con usuario, instante, estados, motivo y versión resultante en metadatos. El registro y el periodo
  se guardan en el mismo `ContabilidadDbContext` y transacción. Se conserva la auditoría automática de ejercicio y periodo.
  No hay tabla propia ni cambios al esquema central; duplicados controlados por versión, candado e idempotencia.
  La consulta mantiene el permiso contable de lectura y filtra explícitamente empresa, entidad y periodo.
- Mutaciones protegidas por versión `If-Match`, idempotencia HTTP y advisory lock transaccional para serializar transiciones
  y reglas entre periodos vecinos. El lote de apertura usa versión del ejercicio; cierre/reapertura, versión del periodo.
- `IPeriodoContableConsultaPort` publica estado por fecha o año/número. `VerificadorPeriodoContable` rechaza inexistente,
  sin abrir o cerrado: se debe crear y abrir el ejercicio antes de registrar movimientos.
- Conectar los dos consumidores internos de CON-02. Facturación/Tesorería/Almacén mantienen sus adaptadores pendientes de C1.2.
- No se modifica `almacen.periodos_cerrados`, ni se emiten eventos de integración sin outbox/suscriptores.

## Consecuencias y límites

El cierre y la reapertura son reproducibles y auditables, y los consumidores tienen un contrato común con rechazo explícito.
Conectar C1.2 requiere ejercicios sembrados en pruebas y arranque para evitar bloquear módulos sin calendario configurado.
El advisory lock actual serializa las transiciones de periodos; hay que conservar el mismo bloqueo al guardar pólizas para
evitar carreras entre verificar un periodo y registrar el movimiento. El estado de esa garantía se registra en la evidencia.

No hay saldos ni arrastre aún: CA10.10 queda parcial hasta C1.3/C1.7. Las conciliaciones/checklist y cierre anual son CON-12.
Los datos `FIX-`/DEMO no acreditan calendario operativo ni aceptación UAT.

## Referencias

- [Plan F1-CON-03](../modulos/contabilidad/09-plan-periodos-contables.md)
- [Contrato API](../modulos/contabilidad/10-contrato-api-periodos.md)
- [Evidencia y handoff](../modulos/contabilidad/11-evidencia-f1-con-03.md)
- ADR-0011, ADR-0012, ADR-0020, ADR-0031 y regla de negocio D18 de Obsidian.

## Corrección acordada con Uziel

Se retira la bitácora específica porque el historial es una consulta, no un proceso contable independiente.
La migración posterior conserva las transiciones de la tabla anterior en la auditoría central antes de retirarla,
sin modificar la migración ya publicada. El contrato HTTP del historial permanece igual.
