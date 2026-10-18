# 14 — Pólizas recurrentes, reversa automática y reclasificación ligada (K10.4)

Entrega de F1-CON-04 sobre la póliza de C1.3 (doc 13). Planos: «Módulo 10 · Contabilidad» (R11, R12, K10.4). Criterios: CA10.6
(una renta mensual de 12 periodos con reversa automática se genera cada mes sin capturarla de nuevo), K10.4-a (al consultar el
movimiento original se ve la reclasificación ligada) y K10.4-b (llega el día 1 y se genera la reversa sola).

## 1. Lo que respondió Millet (Cuestionario 08, C-07)

- «Actualmente no hay pólizas recurrentes, pero si se requieren, tener la información del importe y periodos a aplicar, para que
  la repetición se libere en el sistema.»
- «Las reversiones en cada caso se deben de marcar en un check box si se realiza automática.»
- «Reclasificaciones se hacen manuales en Asiento Contable directo, actualmente no permite reconciliar contra la transacción
  origen que se desea reclasificar sólo a nivel cuenta contable pero SI SE REQUIERE.»

Plantilla, periodicidad, base de cálculo y autorización no se respondieron. El Anteproyecto Maestro (CON-03) asigna el diseño a
VILO: «VILO definirá el modelo parametrizable de pólizas recurrentes, provisiones, diferimientos, reclasificaciones, aprobación,
reversa y vínculo al origen».

## 2. Supuestos de diseño (propuestas VILO)

| # | Supuesto | Respaldo |
|---|---|---|
| R1 | La plantilla (tipo, concepto, partidas fijas, periodo inicial, número de periodos 1–120, día del mes 1–28 o último, reversa automática, nace autorizada) la prepara un usuario y la **activa otro** con permiso | R23 (prepara el Analista, aprueba el Supervisor); ficha K10.4 («borrador para autorizar o autorizada, según configuración») |
| R2 | Generación **automática** cada hora (proceso de fondo) y botón «Generar pendientes»; idempotente por llave (plantilla + periodo); lo que no se puede generar queda pendiente con motivo y se reintenta | CA10.6, K10.4-b; Plano C1 §6 (sin duplicados) |
| R3 | La **reversa automática** usa la regla de la anulación (mismas partidas en negativo, mismo lado), el **día 1 del periodo siguiente**, mismo tipo con folio nuevo, concepto «Reversa de TT-000123»; la original no cambia de estado | C-06/K10.4 (Contabilidad no quiere cargos y abonos invertidos); C-10 (reversa el día 1) |
| R4 | La póliza generada nace autorizada por quien activó la plantilla, o en borrador (la captura quien preparó la plantilla) para autorizarse; solo la autorizada se revierte | R23 |
| R5 | La **reclasificación** es una póliza manual que referencia la póliza autorizada que corrige (copia su documento de origen); se ve en ambos sentidos | C-07; JUNTA-03 (Contabilidad decide si corrige en el periodo actual o reabre el anterior) |
| R6 | Con la reversa vigente, la original no se anula directo (se anularía dos veces): primero se anula la reversa | Consecuencia de R3 + K10.3 |

Por confirmar con Contabilidad (no bloquea): el ejemplo de CA10.6 combina una renta fija con reversa automática; la reversa
suele aplicar a provisiones y devengos, no a rentas.

## 3. Modelo

- `PlantillaRecurrente` (`contabilidad.plantillas_recurrentes`): estado `Borrador → Activa ⇄ Suspendida → Terminada`; partidas fijas en
  JSON; quién la preparó y quién la activó.
- `Poliza` + `PolizaRelacionadaId`/`Relacion` (`Reversa`, `Reclasificacion`) y `PlantillaRecurrenteId`. La anulación conserva su liga
  propia (`PolizaAnuladaId`). La respuesta de la póliza incluye `ligadas[]` (sus reversas y reclasificaciones).
- Llaves de idempotencia: `recurrente:{plantilla}:{año}-{mes}` y `reversa:{póliza}` (índice único existente).

## 4. Contrato HTTP

`/api/v1/contabilidad/polizas-recurrentes`: `GET /`, `GET /{id}` (con el calendario de aplicaciones: póliza y reversa de cada periodo),
`POST /`, `PUT /{id}` (solo borrador), `POST /{id}/activar` (permiso `contabilidad.recurrentes.autorizar`, no quien la preparó; genera lo
que ya toca), `POST /{id}/suspender`, `POST /{id}/reanudar`, `POST /generar?plantillaId=` (generadas y pendientes con motivo). Permisos
`contabilidad.recurrentes.{leer,administrar,autorizar}`.

Pólizas: `PolizaManualRequest.reclasificaPolizaId`; la respuesta trae `polizaRelacionadaId/Folio`, `relacion`, `plantillaRecurrenteId/Nombre`
y `ligadas[]`. Anular con reversa vigente → 422 `CONTAB_POLIZA_TIENE_REVERSA`.

## 5. Proceso de fondo

`PolizasRecurrentesWorker` (`Contabilidad/Infrastructure/Workers`), opciones `Contabilidad:Recurrentes:{Deshabilitado, IntervaloMinutos=60}`.
Corre sin request: cada póliza, folio, uso de cuenta y consulta de periodo usan la empresa de la plantilla. Deshabilitado en las pruebas
de integración (que llaman a «generar»).
