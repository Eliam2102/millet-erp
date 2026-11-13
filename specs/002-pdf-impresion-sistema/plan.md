# Implementation Plan: PDF e impresión
## Summary
Corregir CSP para el motor existente y sustituir impresión de pantalla con PDF de datos; reutilizar documentos oficiales existentes.
## Technical Context
TypeScript/React19, Vite8, react-pdf4, Vitest. Sin dependencias nuevas ni cambios de servidor/datos. Base origin/develop `954a52c7a`, contrastada con GitHub mediante fetch en esta sesión.
## Constitution Check
Cumple fronteras, permisos, muestras ficticias, pruebas de regresión y cierre separado de QA. No se relaja eval JavaScript. Sin migraciones.
## Project Structure
frontend/src/lib/seguridad/csp.ts; components/erp/reportes; features/*/pages y componentes con impresión; specs/002-pdf-impresion-sistema.
## Phase 0: Research
Ver research.md e inventario de consumidores. Se elige permiso exclusivo WASM, generador existente y visor nativo.
## Phase 1: Design
Ver data-model.md, contracts/impresion.md y quickstart.md. Un helper abre ventana durante clic y carga Blob; cada detalle adapta datos existentes al documento compartido. Los reportes pasan getter al exportador.
## Validation
Regresión CSP, error/ventana bloqueada/cierre, valores calculados, PDF real multipágina y pruebas de módulos afectados; build/lint y navegador con CSP del build.
## Complexity Tracking
Sin nueva dependencia, servicio, persistencia ni permiso.
