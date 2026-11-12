# Tasks: Pulir interfaz existente
## Phase 1: Setup
- [x] T001 ContrastarQA/código e instrucciones; registrar specs/003-pulir-interfaz-actual/research.md.
## Phase 2: Foundational
- [x] T002 Completar spec/plan/contracts/checklists en specs/003-pulir-interfaz-actual/.
## Phase 3: US1 · Navegación familiar
- [x] T003 [US1] Añadir regresiones de orden/limpiar antes de lógica en frontend/src/components/layout/{SidebarNav,ModulePanel}.test.tsx.
- [x] T004 [US1] Pulir sin sustituir shell en frontend/src/components/layout/{SidebarNav,ModulePanel}.tsx.
## Phase 4: US2 · Captura
- [x] T005 [US2] Reutilizar grupos y pruebas de captura conservada en frontend/src/features/compras/pages/NuevaRequisicion{,.smoke.test}.tsx.
## Phase 5: US3 · Móvil
- [x] T006 [US3] Reorganizar cabecera móvil sin quitar controles en frontend/src/components/layout/Topbar.tsx.
## Phase 6: Polish
- [x] T007 Ejecutar quickstart con pruebas/build/lint y comparación de capturas; evidencia fuera delrepo.
- [x] T008 Registrar alcance/pendientes en bóveda y actualizar specs/003-pulir-interfaz-actual/tasks.md.
## Dependencies & Execution Order
T001→T002→T003→T004;T005/T006independientes trasT002;T007→T008. Se pueden inspeccionar testsUS1/US2en paralelo; ejecución de edición secuencial. Estrategia: pulir lo actual, revisar localmente antes depublicar. Sin agentes adicionales ni nuevas dependencias.

## Phase 7: Convergence
- [x] T009 Completar comparación visual final1366/1920/390 cuando el control de navegador esté estable, segúnSC003/US3 (partial); evidencia pendiente fuera delrepo. T007ya tiene147pruebas/build/lint aprobados y escritorio con validación/filtro observados. No atribuir esa evidencia al móvil.

## Phase 8: Navegación que se oculta · nueva instrucción de Eliam
- [x] T010 [US1] Escribir regresiones de menú oculto,mouseleave,búsqueda enfocada,pin yEscape en frontend/src/components/layout/AppShell.test.tsx.
- [x] T011 [US1] Implementar navegación desplegable usando Popover/SidebarNav/ModulePanel en frontend/src/components/layout/{AppShell,Topbar,ModulePanel}.tsx; ancho liberado/pin sin superposición.
- [x] T012 [US2] Dar acabado formal al piloto en frontend/src/features/compras/pages/NuevaRequisicion.tsx sin modificar reglas.
- [x] T013 Verificar pruebas/build/lint y navegador abierto/cerrado/fijado/captura; registrar resultados y limitaciones fuera delrepo.

Resultado Phase 8: 152 pruebas/25 archivos, build aprobado, lint 0 errores/2 advertencias. Navegador oculto/abierto/fijado observado. T013 permanece parcial: repetición manual Escape/edición interrumpida; T009 responsive final pendiente. Evidencia: outputs/Identidad_navegacion_2026-11-12/auto-ocultar/Resultado_local.md (fuera del repositorio).

## Phase 9 · Deslizamiento desde la izquierda
- [x] T014 Sustituir anclaje flotante por panel fijo lateral con animación y movimiento reducido en AppShell.
- [x] T015 Verificar seis regresiones de AppShell, build y navegador: x=0, alto bajo cabecera, fijado reserva316px, Escape cierra. Lint y evidencia en resultado local.

## Phase 10 · Revisión móvil solicitada
- [x] T016 Revisar360×800/390×844, menú y captura real de muestra trasX/Escape/opciones de módulo.
- [x] T017 Añadir regresión de conservación móvil con MobileSidebar real y ejecutar suite ampliada:154/25 aprobadas; lint dirigido aprobado.
T009: parte móvil revisada; comparación final escritorio1366/1920 permanece pendiente. Sin cambio funcional adicional ni repetición innecesaria del build. Evidencia externa: auto-ocultar/Revision_movil.md.

## Phase 11 · Cabecera móvil y preparación de QA
- [x] T018 Añadir regresiones borde automático y herramientas móvil/RBAC en AppShell.test.tsx y Topbar.test.tsx.
- [x] T019 Mejorar Topbar/EmpresaSelector/SucursalSelector y ancho de CampanaAvisos; abrir sidebar al borde desde AppShell.
- [x] T020 Verificar320/360/390/768/1366/1920, herramientas/avisos/búsqueda, pin/cierre/captura y muestra multiempresa; capturas fuera del repo.
- [x] T021 Ejecutar pruebas afectadas/build/lint y convergencia; preparar parche limpio, inventario y borrador QA/reversión fuera del repo. Actualizar bóveda. Publicación requiere autorización posterior.

Cierre local del corte: T007/T009/T013 completados con comparación1366/1920/390,320/360/768 y captura/pin/roce rápido. T018–T021: candidato aplicado sin conflictos a develop8364f70d,187pruebas/30archivos, build/lint aprobados; evidencia, parche y borrador de pase/reversión fuera del repo. QA real/publicación/aceptación no forman parte del cierre local ni están autorizadas.

## Phase 12 · Limpieza de cabecera
- [x] T022 Retirar marca textual de Topbar/AppShell y separar controles; actualizar pruebas y revisar móvil/escritorio.
- [x] T023 Sustituir el candidato/borrador anterior por la revisión actual comprobada, sin publicar.
