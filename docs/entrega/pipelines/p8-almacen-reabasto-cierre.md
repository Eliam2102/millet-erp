# P8 — Reabasto automático (reorden) y cierre de mes de Almacén

> ⚠️ **Sin evidencia e2e**: estos flujos no formaron parte de la verificación
> del 2026-07-15; los pasos y errores están verificados contra el código y las
> pantallas, pero no hay folios reales de referencia.

Dos controles internos de Almacén: el motor de reorden que genera
requisiciones de sistema cuando el stock cae bajo el punto de reorden, y el
cierre mensual del módulo que congela el periodo.

## Actores y permisos

| Flujo | Actor | Permisos requeridos |
|---|---|---|
| Configurar reorden | Administrador del almacén | `almacen.reorden.leer`, `.administrar`; `almacen.asignaciones.administrar` |
| Operar el reabasto | Jefe de Almacén | Toggle en la pantalla de Reabasto; las RQs generadas siguen el flujo normal de Requisiciones |
| Cierre de mes | Jefe de Almacén | `almacen.cierre-mes.ejecutar` |

## Flujo 1 — Reabasto automático (reorden N1/N2)

**Objetivo:** que el sistema detecte artículos bajo su punto de reorden y
genere **borradores de requisición** automáticamente, sin esperar a que alguien
note el desabasto.

**Cómo funciona:** un worker de fondo (`ReordenWorker`, ADR-0047) barre
periódicamente las configuraciones de reorden y genera las RQ de sistema. Tiene
**dos interruptores con precedencia**:

1. **Kill-switch de infraestructura** (`ReordenWorker:Disabled`, default
   **apagado** — opt-in por ambiente, requiere config + reinicio del App
   Service).
2. **Interruptor operativo** — `ReabastoAutomaticoActivo`, editable desde la
   pantalla de Reabasto, consultado en cada ciclo. Apagarlo no cancela un
   barrido en curso; surte efecto al siguiente tick. Los borradores ya
   generados quedan vivos como RQs normales.

**Pasos de configuración y operación:**

1. **Configurar niveles.** En `/almacen/reorden` (pantalla **"Reabasto"**):
   mínimo / máximo / punto de reorden / cantidad de reabasto por
   sucursal-almacén (N1/N2). Las asignaciones artículo→ubicación
   (`/almacen/asignaciones`) llevan su política de reposición.
2. **Activar el motor.** Toggle de reabasto automático en la misma pantalla
   (requiere que infraestructura haya prendido el worker en el ambiente).
3. **Revisar lo generado.** Las RQs de sistema aparecen como **borradores** en
   `/compras/requisiciones`; el flujo de autorización y surtido es el normal
   ([P1](p1-flujo-feliz-insumos.md) pasos 2–3).
   📸 Captura pendiente: pantalla "Reabasto" con el toggle y niveles.

**Errores esperados:** `REORDEN_RQ_SIN_LINEAS` (barrido sin faltantes no genera
RQ) · `REORDEN_SP_NO_DISPONIBLE` (falta el usuario de servicio del motor) ·
`REORDEN_ALMACEN_NO_PERTENECE_A_SUCURSAL` (configuración inconsistente).

## Flujo 2 — Cierre de mes

**Objetivo:** congelar el periodo contable del módulo: tras el cierre, ningún
movimiento puede registrarse con fecha dentro del mes cerrado.

1. **Preparar el mes.** Antes de cerrar deben quedar resueltos:
   - **Conteos** del mes en `EnConciliacion` o `Aprobado` → aplicarlos o
     rechazarlos.
   - **Movimientos** en `Borrador` o `Validado` con fecha del mes → firmarlos o
     cancelarlos.
2. **Ejecutar.** En `/almacen/cierre-mes`, elegir año/mes y ejecutar el cierre
   (permiso exclusivo del Jefe de Almacén). La operación es idempotente: un
   periodo ya cerrado no se vuelve a cerrar.
3. **Efecto.** Todos los registros de movimiento rechazan fechas dentro del
   periodo cerrado; el reporte **ALFAK-HISTORIAL-ALMACEN**
   (`/almacen/reportes/alfak-historial`) es el corte oficial del mes.
   📸 Captura pendiente: pantalla "Cierre de mes".

**Errores esperados (422):**

| Código | Mensaje |
|---|---|
| `PERIODO_YA_CERRADO` | "El periodo {AAAA}/{MM} ya está cerrado." |
| `CIERRE_CONTEOS_PENDIENTES` | "No se puede cerrar: N conteo(s) en EnConciliacion/Aprobado del mes. Aplicar o rechazar antes: {ids}." |
| (movimientos pendientes) | Misma mecánica para movimientos Borrador/Validado con fecha del mes |

## Cómo verificar el resultado

| Qué | Dónde | Qué esperar |
|---|---|---|
| RQs de sistema | `/compras/requisiciones` | Borradores generados por el motor, identificables como RQ de sistema |
| Motor activo | Application Insights | Ciclos del `ReordenWorker` en el log; sin errores repetidos |
| Periodo cerrado | `/almacen/cierre-mes` | El mes aparece cerrado; intentar un movimiento con fecha del mes cerrado rechaza |
