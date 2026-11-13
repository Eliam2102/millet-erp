# Tasks: Tablas adaptables

## Phase 1 Setup
- [x] T001 Inventariar familias y registrar alcance en specs/004-tablas-adaptables/research.md.
- [x] T002 Preparar contrato, plan y criterios en specs/004-tablas-adaptables/.
## Phase 2 Foundation
- [x] T003 Probar preferencia/auto/storage y rangos en frontend/src/components/erp/data/.
- [x] T004 Implementar preferencia y paginación común en frontend/src/components/erp/data/.
## Phase 3 US1 Cantidad persistida
- [x] T005 [US1] Consolidar ocho hooks lib/use-paginacion-local.ts y migrar sus consumidores.
- [x] T006 [US1] Migrar paginación servidor/URL y bitácoras para consulta/control consistentes en frontend/src/features y modules.
- [x] T007 [US1] Migrar controles propios y listados sin selector; documentar cobertura en specs/004-tablas-adaptables/coverage.md.
## Phase 4 US2 Espacio disponible
- [x] T008 [US2] Ajustar ListadoModulo, TableContainer, tablas nativas y alturas limitadas sin afectar formularios en frontend/src/.
- [x] T009 [US2] Verificar columnas/scroll/captura/impresión en vistas representativas de frontend/src/.
## Phase 5 US3 Navegación
- [x] T010 [US3] Homologar rango, extremos y página directa en frontend/src/components/erp/data/PaginacionTabla.tsx.
## Phase 6 Validation
- [x] T011 Ejecutar regresiones, build/lint y revisión visual móvil/escritorio; registrar resultados fuera del repo.
- [x] T012 Actualizar design-system/DESIGN.md y documentación de cobertura/cierre.

## Dependencies
T001→T002→T003→T004→T005/T006/T007→T008→T009/T010→T011→T012. Implementación secuencial para evitar colisiones entre hooks y consumidores.
## Parallel opportunities
Investigación de familias independiente de documentación. Las vistas se pueden comprobar en paralelo después de estabilizar componentes; no se requiere otra intervención en QA.
## Strategy
Base común primero, propagación completa por familias, excepciones explícitas para captura/reportes, validación sin declarar QA.
