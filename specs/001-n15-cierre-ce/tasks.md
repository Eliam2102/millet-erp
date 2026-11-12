# Tasks: Cierre N-15

**Input**: spec.md, plan.md, research.md, data-model.md, contracts/conciliacion.md.
**Tests**: solicitadas expresamente; regresiones rojas antes de corregir.

## Phase 1: Setup
- [x] T001 Preparar rama aislada de develop e integrar Spec Kit sin colisiones en .specify/ y .agents/skills/.
- [x] T002 Resolver plantillas y validar requisitos de specs/001-n15-cierre-ce/spec.md y checklists/requirements.md.

## Phase 2: Foundational
- [x] T003 Añadir y ejecutar regresión de aviso viejo en backend/tests/Api.IntegrationTests/ComercioExterior/P25ExpedienteRealHttpTests.cs.
- [x] T004 Añadir y ejecutar regresión de bypass del responsable en backend/tests/Api.UnitTests/CeResponsableHitoTests.cs.

## Phase 3: US1 · Cantidades consistentes
Independent test: avisos invertidos/empatados/repetidos no revierten neto; disminución válida sí.
- [x] T005 [US1] Extender consulta neta por línea, unidad de OC y redondeo4 existente en backend/src/Almacen/Application/PublicPorts/IRecepcionesImportacionReadPort.cs y su adapter RecepcionesImportacionReadAdapter.cs.
- [x] T006 [US1] Cubrir neto, canceladas/devoluciones/AF y conversiones en backend/tests/Almacen.UnitTests/Recepciones/; ajustar constructor directo existente.
- [x] T007 [US1] Compartir reparto actual bajo candados CE antes de leer Almacén en backend/src/ComercioExterior/Application/ConciliacionRecepcionServicio.cs y RecepcionCe.cs; rechazar línea ausente y acumulados inválidos.
- [x] T008 [US1] Conservar eventos reales y no duplicar historia/revisión sin cambios en backend/src/ComercioExterior/Domain/ExpedienteRecepcion.cs y ExpedienteImportacion.cs.
- [x] T009 [US1] Probar reparto entre guías/expedientes, cancelados/cerrados, duplicados, empates y disminuciones en backend/tests/ComercioExterior.UnitTests/P20RecepcionTests.cs y Api.IntegrationTests/ComercioExterior/.

## Phase 4: US2 · Responsable con su acceso vigente
Independent test: aceptar asignado/corporativo/bypass de gestión; rechazar lectura sola, ajeno/inactivo/denegado.
- [x] T010 [US2] Consultar permiso efectivo del responsable en la empresa de la sucursal con IPermissionLoader en backend/src/ComercioExterior/Infrastructure/PublicAdapters/CatalogosCeReadAdapter.cs.
- [x] T011 [US2] Cubrir roles/denegaciones, otra empresa/actor y asociación existente en backend/tests/Api.UnitTests/CeResponsableHitoTests.cs; no editar concesiones ni el puerto global.

## Phase 5: US3 · Recuperación y cierre del mismo expediente
Independent test: CE1/Almacén2 se recupera por operación normal, sin recepción ni evento nuevos.
- [x] T012 [US3] Incorporar reconciliación canónica en revisión/cierres, preservando transacción/versión/alcance y evitando crear incidencias en operaciones sin recepción en backend/src/ComercioExterior/Application/ExpedienteHandlers.cs y DependencyInjection.cs.
- [x] T013 [US3] Probar reparación histórica por HTTP, misma incidencia/historia, sin UUID fabricado y rollback422 en backend/tests/Api.IntegrationTests/ComercioExterior/P25ExpedienteRealHttpTests.cs.
- [x] T014 [US3] Ejecutar pruebas dirigidas CE/Almacén/Api.UnitTests y gate CE según docs/qa/gate-integracion.md; guardar salidas fuera del repo.
- [x] T015 [US3] Preparar commit/PR y verificar CI antes de integrar/pasar QA, con evidencia fuera del repo y trazabilidad a N-15.
- [x] T016 [US3] Verificar versión y permiso exacto de Eliam en QA; recuperar CE-IMP-2026-000004 usando sus seis recepciones, completar hitos, revisión y tres cierres/aprobación; guardar evidencia externa.

## Phase 6: Polish
- [x] T017 Evaluar convergencia del resultado con la skill oficial y registrar pendientes reales en specs/001-n15-cierre-ce/.
- [x] T018 Actualizar N-15 y fichas/Bitácora de la bóveda con evidencia y límites; cerrar sólo si T016 pasó íntegro.
- [x] T019 Verificar diff limpio de secretos, logs y archivos de trabajo según docs/qa/gate-integracion.md y AGENTS.md.

## Dependencies
T001→T002→(T003,T004)→US1/US2→US3→convergencia/gestión.
Dentro de US1: puerto→reparto/dominio→regresiones/gate. US2 es independiente de US1,
pero su prueba requiere el diseño de permiso del responsable. Publicación y QA secuenciales.

## Parallel opportunities
Investigación de cantidades y permisos ya separada en dos revisiones de sólo lectura.
Pruebas de permisos pueden prepararse mientras se investiga el neto; modificaciones de un mismo
archivo y migraciones/pases nunca en paralelo. No se añaden agentes de implementación implícitos.

## Implementation strategy
MVP=US1 resuelve el fallo de acumulado; entrega completa exige US2+US3 y evidencia QA.
No marcar terminado un task por haber intentado ejecutarlo; registrar fallo/pending si no pasa.

## Phase 7: Convergence
- [x] T020 Corregir la búsqueda de incidencia automática por línea completa, incluida la partida histórica de una guía cancelada, y demostrar con regresión que el mismo neto/diferencia no repite historia ni revisión; FR-002 / US1-AC2 / plan: historial estable (partial, HIGH).

## Phase 8: Convergence
- [x] T021 Reutilizar la corrección existente del PR654 para habilitar la aprobación de Gerencia con los tres cierres registrados; validar y publicar sólo el frontend sobre su paquete QA actual, conservando #674/#663/#669, y aprobar el ensayo desde la pantalla. US3/AC3, FR-008 y plan: ajuste del consumidor (partial, HIGH). No duplicar implementación ni ampliar permisos.

## Resultado verificado · 11-nov-2026

Implementación integrada mediante PR676 y PR654, ambos con CI aprobado. QA: CE-IMP-2026-000004 cerrado desde la pantalla a las20:12 Mérida con las mismas seis recepciones; tres cierres y aprobación final persistidos, cero incidencias pendientes. N-15 COMPLETE verificado después del ensayo. Convergencia oficial: 10FR, 5SC, 11 escenarios de aceptación, 8 decisiones de ejecución y 5 principios; cero brechas nuevas. Evidencia y resultados fuera del repositorio. No implica aceptación fiscal/productiva ni cierre de Q-CE/Activos Fijos.
