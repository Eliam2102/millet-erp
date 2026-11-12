# Tasks: PDF e impresión
## Phase 1: Setup
- [x] T001 Inventariar consumidores y revisar reglas en frontend/src y specs/002-pdf-impresion-sistema/research.md.
## Phase 2: Foundation
- [x] T002 Añadir regresiones de seguridad, visor y valores calculados en frontend/src/lib/seguridad/csp.test.ts y frontend/src/components/erp/reportes/*.test.tsx.
## Phase 3: US1 Descargar PDF
- [x] T003 [US1] Corregir CSP específica de WASM en frontend/src/lib/seguridad/csp.ts.
- [x] T004 [US1] Conservar getter y paginación documental en frontend/src/components/erp/reportes/exporters/.
## Phase 4: US2 Imprimir PDF
- [x] T005 [US2] Crear apertura del visor con errores y limpieza en frontend/src/components/erp/reportes/imprimir-pdf.ts y ImprimirPdfButton.tsx.
- [x] T006 [US2] Migrar cinco reportes en frontend/src/components/erp/reportes/ReporteShell.tsx y frontend/src/features/{facturacion,almacen,cxc,comercio-exterior}/components/.
- [x] T007 [US2] Migrar nueve detalles en frontend/src/features/{compras,facturacion,contabilidad,activos-fijos,producto-terminado}/ con documentos de datos y originales.
## Phase 5: Validation
- [x] T008 Verificar PDF real y consumidores con pruebas, build y lint en frontend/.
- [x] T009 Verificar visor y CSP en navegador; seguir la guía specs/002-pdf-impresion-sistema/quickstart.md y registrar evidencia y límites en el PR.
## Dependencies
T001 → T002 → T003/T004 → T005 → T006/T007 → T008 → T009.
## Parallel opportunities
T003 y T004 son independientes después de T002. Los detalles de módulos distintos pueden migrarse por separado; se ejecutarán en esta sesión sin agentes adicionales.
## Implementation strategy
Resolver primero descarga (US1); luego apertura común, migración completa (US2) y comprobación transversal. No desplegar ni modificar datos.
