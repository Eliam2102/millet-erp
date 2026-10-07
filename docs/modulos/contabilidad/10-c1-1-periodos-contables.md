# 10 — C1.1: periodos contables (crear, cerrar y reabrir)

Fecha: 06-oct-2026. Ficha C1.1 (F1-CON-03). Reglas: Módulo 10 R18, R19 y R28; decisión D18.

## 1. Qué resuelve

Hasta esta ficha nadie era dueño del periodo contable: Facturación y Tesorería usan `IPeriodoContablePort` NoOp y Almacén `IPeriodoContableReadPort` NoOp, y los tres consideran abierto cualquier mes. C1.1 crea el dueño real en Contabilidad. **Conectar esos tres módulos es C1.2**; queda marcado con `PLATFORM-TODO(<ContabilidadPeriodoConsumidores>)` en el puerto.

## 2. Decisiones de diseño (aprobadas con el plan de la ficha)

1. **Ejercicio no creado = no abierto.** El puerto responde `NoExiste` y nunca supone «abierto». El ejercicio se crea desde la pantalla (2026, y 2027 antes de operar).
2. **Periodo 13:** solo acepta pólizas manuales autorizadas y solo cuando el periodo 12 del mismo ejercicio está cerrado («después del cierre ordinario»).
3. **Motivo:** obligatorio para reabrir y opcional para cerrar. Cada acción queda en un historial que solo se agrega.
4. **Sin cierre secuencial obligatorio.** Se puede cerrar febrero con enero abierto; el cierre secuencial es CON-12.
5. **Saldos calculados desde las pólizas,** sin tablas de acumulados. Reabrir no rehace nada; el arrastre ocurre al recalcular (C1.3 y C1.7).

Reabrir contabilidad **no** toca el cierre de inventario de Almacén (`almacen.periodos_cerrados`, D18).

## 3. Modelo

- `contabilidad.periodos_contables`: `ejercicio` (2000–2100), `numero` (1–13), `estado` (Abierto/Cerrado), `fecha_inicio`/`fecha_fin` (nulas en el 13), quién y cuándo cerró y reabrió por última vez, `version`. Índice único `(empresa_id, ejercicio, numero)`.
- `contabilidad.periodos_contables_eventos`: `periodo_id`, `accion` (Creado/Cerrado/Reabierto), usuario, fecha y motivo (≤ 500).

## 4. API · `/api/v1/contabilidad/periodos`

| Método y ruta | Permiso | Notas |
|---|---|---|
| `GET ?ejercicio=` | `contabilidad.periodo.leer` | Los 13 periodos; lista vacía si el ejercicio no existe |
| `GET /ejercicios` | leer | Ejercicios creados, del más reciente al más antiguo |
| `POST /ejercicios {ejercicio}` | `contabilidad.periodo.administrar` | 201 con los 13 periodos abiertos; 409 `CONTAB_EJERCICIO_YA_EXISTE` |
| `GET /{id}` | leer | Con `ETag` |
| `GET /{id}/historial` | leer | Del más reciente al más antiguo |
| `POST /{id}/cerrar {motivo?}` | `contabilidad.periodo.cerrar` | `If-Match` + `Idempotency-Key` |
| `POST /{id}/reabrir {motivo}` | `contabilidad.periodo.reabrir` | Solo el Contador General; motivo obligatorio |

Errores: versión desactualizada → **409** (`ConcurrencyException`, mapeo general del API; el plan decía 412); sin `If-Match` → 428; motivo vacío al reabrir → 400; cerrar un cerrado o reabrir un abierto → 422.

## 5. Puerto público `IPeriodoContableReadPort`

- `EstaAbiertoAsync(año, mes)`: la firma que esperan Facturación, Tesorería y Almacén (C1.2).
- `ValidarRegistroAsync(ejercicio, periodo, esManualAutorizada)` → `{Valido, Motivo}`, con los motivos `NoExiste`, `Cerrado`, `Periodo13SoloAjusteAuditoria` y `Periodo13AntesDelCierreDeDiciembre`. Lo usará el motor de pólizas (C1.3 y C1.4).

## 6. Pantalla

`/contabilidad/periodos` (tarjeta «Periodos contables» en Contabilidad):

- selector de ejercicio y, con permiso de administrar, «Crear ejercicio»;
- tabla de 13 periodos con fechas, estado, último cierre y última reapertura;
- botones Cerrar/Reabrir según estado y permiso, con diálogo de motivo;
- panel de historial por periodo.

## 7. Evidencia

| Criterio | Prueba | Resultado |
|---|---|---|
| Crear ejercicio = 13 periodos; repetir = 409 | `PeriodosHttpTests.Crear_ejercicio_da_13_periodos_abiertos_y_repetirlo_es_409` | Verde |
| Cerrar y reabrir con historial; el puerto lo refleja | `Cerrar_y_reabrir_quedan_en_el_historial_y_el_puerto_lo_refleja` | Verde |
| CA10.10 (parte de periodos): reapertura auditada con motivo | misma prueba | Verde |
| C1.1-a: sin permiso de reabrir → 403 | `Sin_permiso_de_reabrir_no_puede_reabrir` | Verde |
| C1.1-b: reabrir no cambia `almacen.periodos_cerrados` | `Cerrar_y_reabrir_...` | Verde |
| Concurrencia: versión vieja 409, sin `If-Match` 428 | `Version_desactualizada_es_409_y_sin_If_Match_es_428` | Verde |
| CA10.12 (parte de periodos): reglas del periodo 13 | `Puerto_periodo_13_solo_ajustes_manuales_despues_de_cerrar_diciembre` | Verde |
| Dominio y validadores | `Contabilidad.UnitTests/PeriodosTests` (12) | Verde |
| Pantalla: estados, botones por permiso, motivo, If-Match, historial | `PeriodosPage.test.tsx` (6) | Verde |

Totales al 06-oct-2026: 148 unitarias de Contabilidad; 57 de integración de Contabilidad; Identidad 101 unitarias y 148 de integración; Vitest de `features/contabilidad` y `lib`: 208.

**Queda para fichas posteriores:** la prueba completa de CA10.10 y CA10.12 con saldos y pólizas reales (C1.3 y C1.4), y la conexión de Facturación, Tesorería y Almacén al puerto (C1.2).

**Observación ajena a la ficha:** `tsc -b` reporta un error previo en `src/lib/nav.test.ts:348` (tipado de `permisos.includes`), que ya existía en `main`.
