# 11 — Evidencia y handoff de F1-CON-03

> Corte: 2026-10-06 · Rama `feature/F1-CON-03-periodos-contables` · Datos de prueba.
> Inventario de implementación contrastado con código y commits. Los resultados de ejecución se registran por separado;
> tener una prueba escrita no demuestra que pasó ni equivale a aceptación UAT.

## Trabajo recuperado de Claude Code

| Commit | Entrega localizada |
|---|---|
| `fb4f9e8` | Plan y decisiones D1–D10 |
| `f7e9ec0` | Dominio, bitácora, migración y pruebas unitarias |
| `c85451c` | Permisos leer, administrar, cerrar y reabrir |
| `23fee37` | Aplicación, endpoints, concurrencia, idempotencia y SQL de prueba 2026 |
| `a55ed95` | Puerto público y validación en movimientos de prueba |
| `2e33e71` | Pruebas de integración HTTP |
| `7238361` | Pantalla, diálogos, bitácora y pruebas frontend |

La reanudación añade [contrato API](10-contrato-api-periodos.md), [ADR-0058](../../decisiones/0058-periodos-contables-y-contrato-de-consulta.md)
y esta evidencia; actualiza el plan para distinguir lo implementado de lo validado. También revisa la carrera entre cierre
y registro del movimiento, la validación de consulta de estado y la conservación de claves de idempotencia ante respuestas
perdidas. Sus resultados se registran en la sección de validación al terminar los checks.

## Casos y pruebas localizadas

Pruebas HTTP en `backend/tests/Api.IntegrationTests/Contabilidad/PeriodosHttpTests.cs`:

| Caso | Método |
|---|---|
| Abierto acepta; cerrado, sin abrir e inexistente rechazan; validación informa periodo | `Movimiento_en_periodo_abierto_se_guarda_y_en_cerrado_no_abierto_o_inexistente_se_rechaza` |
| Cierre concurrente: 200 + 409, una transición | `Cierre_concurrente_con_la_misma_version_da_un_200_un_409_y_una_sola_fila_de_bitacora` |
| Operativo no puede reabrir (403) | `Rol_operativo_cierra_pero_no_reabre_y_el_estado_no_cambia` |
| Cierre repetido 409; misma clave reproduce respuesta | `Cerrar_un_periodo_ya_cerrado_es_409_explicito_y_la_misma_clave_devuelve_la_respuesta_original` |
| Reapertura con motivo, bitácora y auditoría; inventario conserva cierre (C1.1-a/b) | `Reapertura_autorizada_deja_bitacora_y_auditoria_y_no_reabre_el_inventario` |
| 13 solo tras cierre del 12; solo Manual (CA10.12 parcial) | `Periodo_13_no_abre_con_diciembre_abierto_y_solo_admite_movimientos_manuales` |
| Confirmación y cierre comparten el candado; después del cierre no hay nuevos movimientos | `Cierre_espera_al_movimiento_que_ya_verifico_el_periodo_y_luego_rechaza_nuevos_movimientos` |
| Apertura fallida del lote revierte estados, versiones y bitácora; libera el candado | `Apertura_de_lote_invalido_no_guarda_transiciones_bitacora_ni_version_y_libera_el_candado` |

Unitarias: `backend/tests/Contabilidad.UnitTests/PeriodosTests.cs`. Frontend:
`frontend/src/features/contabilidad/pages/PeriodosPage.test.tsx` (permisos, motivo y errores).

## Resultados previos a la corrección de auditoría

| Validación | Resultado |
|---|---|
| Unitarias Contabilidad | 167/167, sin fallos ni pruebas omitidas; `dotnet test backend/tests/Contabilidad.UnitTests/Millet.Contabilidad.UnitTests.csproj --no-build --no-restore --nologo` |
| Integración HTTP con PostgreSQL real | 60/60 de Contabilidad, sin fallos ni omitidas, duración 3 min 59 s; `./tools/validate-integration-isolated.sh --filter 'FullyQualifiedName~Contabilidad'` |
| Guard de cobertura de auditoría | Falla por 4 entidades anteriores de CON-01/02: `CuentaContableOrigen`, `CuentaContableUso`, `ImportacionCatalogo`, `ReglaDimensionUso`. En ese corte, las 3 entidades nuevas de periodos declaraban `IAuditable`; ahora solo ejercicio y periodo son entidades persistidas |
| Vitest de pantalla de periodos y permisos | 14/14, con `--testTimeout=15000`; incluye respuesta perdida/reintento con la misma clave, comando corregido con nueva clave y 403 en reapertura |
| TypeScript, lint y build frontend | Correctos; lint sin errores, 10 advertencias previas en Administración. Build con advertencias previas de fuentes CSS y tamaño de chunks |
| Build backend Debug | Solución compilada con 0 advertencias y 0 errores tras corregir CA1861 en las regresiones nuevas |
| Inspección visual y capturas | Pendiente; no se han adjuntado capturas en este documento |
| UAT y aceptación del área | Pendiente |

El gate creó PostgreSQL temporal, aplicó los 13 contextos del manifiesto y retiró el contenedor al terminar. El filtro
solo ejecutó Contabilidad en `Api.IntegrationTests`; Compras e Integraciones A+W no tenían coincidencias. No se acredita
la suite de integración completa. El guard de auditoría se ejecutó aparte y conserva el fallo anterior indicado arriba.

## Migraciones y datos

Migración publicada `20261006121604_ContabilidadPeriodos`: ejercicios, periodos y bitácora original en esquema `contabilidad`.
La corrección añade `20261006220000_HistorialPeriodosEnAuditoriaCentral`: copia el historial anterior a `core.audit_log`
y retira `contabilidad.periodos_contables_bitacora`. El esquema final solo conserva ejercicio y periodo.
No se reescribe la migración publicada ni se pierden los motivos previos. La reversa reconstruye la tabla anterior
para periodos existentes sin borrar auditoría; volver a aplicar la migración no duplica las transiciones.
Permisos en la migración `SeedPermisosContabilidadPeriodos` de Identidad.
`tools/datos-prueba-f1-con-03.sql` crea 2026 para la primera empresa local: 1–12 abiertos, 13 sin abrir, marca
`seed-f1-con-03-prueba`; si el ejercicio ya existe, no lo altera. El script no acredita una aplicación ejecutada.
El calendario natural y el periodo 13 son reglas recibidas; la apertura DEMO de todos los meses no es el calendario operativo.

Aplicación local comprobada en la reanudación: copia previa `/tmp/f1-con-03-local-before.dump`, migraciones de Identidad y
Contabilidad correctas, script DEMO ejecutado. Lectura SQL posterior: 2026 tiene 12 periodos abiertos (versión 2) y el 13
sin abrir (versión 1). La API en `http://localhost:5000/health/ready` devuelve `Healthy` y HTTP 200; listado de ejercicios
sin sesión devuelve 401. Vite sirve `/contabilidad/periodos` con HTTP 200 en `http://localhost:5173`.
Estas comprobaciones no acreditan navegación autenticada ni UAT.

El login local conserva `Auth__Mode=EntraId`. Para las migraciones y el proceso API se usaron
`Entra__Proveedor=Simulado` y `Entra__Provision__Disabled=true`: la configuración Graph de provisión está incompleta y
no se necesita para Contabilidad. Los overrides afectan solo esos procesos; no se editaron archivos de configuración.

La migración registra los cuatro permisos; el bootstrap del entorno conserva acceso de SuperAdmin. No crea roles operativos ni «Contador General»: la prueba de 403
crea un rol temporal con `leer` y `cerrar` y lo retira. Para cuentas reales, asignar los permisos a los roles aprobados
desde Identidad; no confundir ese fixture con una asignación operativa ya configurada.

## Consumidores y pendientes

| Consumidor / criterio | Estado en este alcance |
|---|---|
| Confirmar movimiento de prueba CON-02 | Verifica periodo antes de guardar |
| Panel «Probar movimiento» | Devuelve error de periodo sobre fecha contable |
| Registrar uso de cuenta | Sin fecha propia; debe llamarse desde movimiento validado |
| Facturación y Tesorería | NoOp sin sustituir; C1.2 |
| Almacén | Cierre de inventario independiente; puerto contable sin usos, pendiente de análisis C1.2 |
| Pólizas manuales/automáticas | C1.3–C1.5; usar verificador y coordinar guardado con cierre |
| CA10.10 arrastre de saldos | Parcial: reapertura y auditoría disponibles; saldos dependen de pólizas/C1.7 |
| CA10.12 autorizadas | Parcial: origen Manual validado; autorización de póliza pendiente C1.3 |
| Checklist de cierre y cierre anual | CON-12 |

La reanudación corrige la carrera del código recuperado: `ConfirmarMovimientoPruebaHandler` adquiere el candado de periodos
y el de reglas dentro de una sola transacción, antes de consultar el estado y guardar. Se añadió una regresión que pausa
la consulta, observa el cierre esperando en `pg_locks` y verifica que, después del cierre, no se guarden nuevos movimientos.
Su resultado de ejecución se registra arriba; una prueba de dos cierres por sí sola no demuestra ese escenario.

También se corrigen las claves de idempotencia de la pantalla usando `useBodyScopedIdempotencyKey`: una respuesta perdida
conserva la clave al reintentar el mismo comando; cambiar recurso, versión o cuerpo genera otra. La consulta de estado
rechaza combinar fecha con año/número o enviar parámetros incompletos. Una regresión adicional cubre el rollback de
apertura en lote si el periodo 13 no puede abrirse.

## Contexto de Obsidian y entrega

Se consultó la copia de la bóveda descargada el 2026-10-05, su `AGENTS.md`, `00 Inicio/Inicio.md`, las fichas C1.1/C1.2,
`Plano C1 · Motor contable y conexión de módulos`, `Módulo 10 · Contabilidad`, D18 y la fuente SRC-009
(`08_Cuestionario_Contabilidad_SAP.docx`). La carpeta `11 Manual y plan de construcción` rige sobre las fichas históricas.
No se modificó la bóveda externa. Esta nota permite trasladar el avance con sus resultados reales cuando se actualice la base.

Se buscaron documentos conectados en Drive por Millet, contabilidad, Contabilidad_SAP y C1.1. Se revisaron un documento
de [administración e impuestos](https://docs.google.com/document/d/16VgikdzYiEWYWtTR1mP4F5dVwYElvgN3ZV1_V78YKEI/edit)
y la [guía de configuración Entra ID del 23-sep](https://docs.google.com/document/d/1jP61Qj1dI-3lLIeZKwMRv6i1zl2QoGEOiKgFe6s6dnk/edit).
No aportaron requisitos contables de CON-03. La muestra `FakeForLocalDev` de la guía es un antecedente;
se conserva la configuración Entra ID vigente del repositorio. Estos documentos no se presentan como fuente contable confirmada.

Ponytail no estaba instalado localmente. Se consultó el [SKILL.md oficial](https://raw.githubusercontent.com/DietrichGebert/ponytail/main/skills/ponytail/SKILL.md)
y se aplicó su flujo de revisión e implementación, sin instalar hooks ni modificar configuración externa. La revisión se
distribuyó entre agentes de backend, frontend y planeación/documentación, con validación coordinada en la rama existente.

La ficha asigna C1.1/C1.2 a Eliam hasta 16-oct; la rama y el trabajo recuperado pertenecen a Uziel. No se cambian asignaciones
externas. El cierre y la reapertura por Contador General ya están respondidos en el Módulo 10 (R18); cierre secuencial y orden
inverso de reapertura son supuestos de diseño pendientes de ratificación.

Pendientes de entrega: revisión visual autenticada, ratificación de supuestos y PR integrado. Sin publicación,
comentario externo ni aceptación de Millet acreditados por este documento.

## Corrección: historial en auditoría central

Eliam ratificó esta corrección en el mensaje compartido por Uziel el 2026-10-06: usar únicamente `core.audit_log`,
persistir cierre/reapertura con usuario, fecha y motivo en la misma transacción del periodo, mantener el historial
por mes en la pantalla y demostrar el rechazo de reapertura sin permiso. Solicitud/aprobación separadas,
recálculo de saldos y ajustes ligados a reapertura no son requisitos actuales; checklist y conciliaciones corresponden a CON-12.

Por instrucción de Uziel, se elimina la entidad/tabla de bitácora específica. `TransicionPeriodoContable` es un record
de datos, no una entidad. `AuditoriaPeriodos` agrega `AuditLogEntry` al mismo `ContabilidadDbContext`, ya mapeado por
`BaseDbContext`, antes del único `SaveChanges` de la transición dentro del advisory lock transaccional.
No se usa `IAuditLogWriter` para estas mutaciones porque ese escritor guarda mediante otro `CoreDbContext`.

Se mantienen las filas automáticas de creación/actualización y se agregan transiciones funcionales con operaciones
`abrir`, `cerrar`, `reabrir`, módulo `Contabilidad`, entidad/aggregate `PeriodoContable` y detalle en `metadatos`.
El diff también incluye motivo y versión para el detalle general de Administración. La pantalla de auditoría
central permite filtrar Contabilidad y esas tres acciones. No hay cambios a su esquema ni a sus permisos.

`GET /periodos/{id}/bitacora` conserva su respuesta y `contabilidad.periodo.leer`; verifica la pertenencia del periodo
y filtra por empresa en el log central. No otorga lectura global. Los comandos conservan versión, idempotencia y
serialización para evitar duplicados; ya no existe el índice único específico de la tabla retirada.

Regresiones añadidas: migración/reversa/reaplicación con historial previo, fallo de auditoría revierte el cierre,
y registros de otra empresa quedan fuera del historial. Se amplían reapertura, permisos, cierre concurrente,
idempotencia y rollback del lote para verificar la persistencia central y su lectura desde Administración.
El script DEMO escribe su historial exclusivamente en la central.

Validación de esta corrección (2026-10-06):

| Comprobación | Resultado |
|---|---|
| Unitarias de Contabilidad con binarios finales | 167/167 |
| Integración PostgreSQL aislado: Contabilidad y `AuditoriaEndpointsTests` | 73/73; 13 contextos migrados |
| Frontend: pantalla de periodos y smoke de auditoría | 15/15 |
| Build solución backend Debug | 0 errores, 0 advertencias |
| TypeScript, build y lint frontend | Aprobados; 10 advertencias de lint previas |
| EF `has-pending-model-changes` | Sin cambios pendientes |
| Script DEMO ejecutado dos veces en esquema aislado | 13 periodos, 12 aperturas centrales, sin duplicados |
| Migración/reversa/reaplicación sobre copia del historial local | 29 transiciones conservadas sin duplicados |
| API local después de reinicio | `/health/ready`: Healthy |

El filtro de integración no acredita la suite completa de todos los módulos. Se conserva pendiente la inspección
visual autenticada y la aceptación UAT, así como el fallo previo del guard de auditoría de CON-01/02.

La migración se aplicó a la base local con respaldo previo en `/tmp/f1-con-03-before-auditoria-central.dump`.
La tabla específica ya no existe y se comprobaron 29 transiciones funcionales en la central. Una compilación
intermedia había usado el identificador de módulo en minúsculas; se recompiló y se normalizó únicamente ese
identificador local, dejando un evento técnico en `core.audit_log`. Las pruebas finales anteriores corresponden
a la recompilación y al identificador canónico `Contabilidad`.
