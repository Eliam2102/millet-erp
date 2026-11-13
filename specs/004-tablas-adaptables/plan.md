# Implementation Plan: Tablas adaptables

**Branch**: feature/tablas-adaptables | **Date**: 2026-11-12 | **Spec**: spec.md

## Summary
Consolidar preferencias/paginación existentes, propagar límite real a todos los consumidores, ajustar contención vertical de listados y homologar bitácoras. No introducir paginación indiscriminada a partidas editables ni reportes jerárquicos.

## Technical Context
TypeScript/React19/Tailwind4/Vite8, Vitest/Testing Library y pruebas de navegador. Persistencia localStorage segura; bibliotecas existentes, sin dependencia nueva. Cantidades máximas 100 por omisión o contrato menor explícito. Scope: todos los archivos TSX de tablas; 219 detectados inicialmente en la investigación de Spec Kit. Usar componentes ERP existentes y hooks de módulo como compatibilidad. Datos de negocio/servidor sin cambios salvo parámetros de paginación ya existentes.

## Constitution Check
PASS: no datos operativos, sin cambios de permisos/reglas, sin despliegue. Regresión dirigida antes de cambio; inventario de cobertura y pruebas reales de navegador con muestras identificadas. No afirmar aceptación QA por pruebas locales. Después de diseño: PASS, persistencia sólo preferencias y URL explícita preservada.

## Project Structure
- components/erp/data: preferencia, hook local común, PaginacionTabla y ListadoModulo.
- components/ui/table.tsx e index.css: distribución, cabecera y contención.
- hooks lib de cada módulo: delegación compatible al común.
- páginas servidor y bitácoras: mismo limit para consulta/control.
- design-system: actualizar regla de diez filas por preferencia y ajuste automático.
- specs/004-tablas-adaptables: investigación, contrato, pruebas y cobertura.

## Phase 0: Research
Investigación delegada sólo lectura completada: 219 archivos con tabla,101 con paginador,74 ocultan selector,51 fijan10,8 hooks locales. Separar colecciones completas, lotes parciales, servidor, editores y reportes. No hay clarificaciones pendientes: decisiones en research.md.

## Phase 1: Design
Preferencia por ruta/tabla, default automático acotado; cantidad explícita vence resize y URL explícita vence preferencia. Medir espacio real de la zona de listado y conservar scroll para cantidades explícitas. Los hooks gestionan offset y límite juntos; el paginador nunca promete cambiar cantidad si el llamador no la conecta a datos. Reportes exportan colección completa. Matriz cobertura clasifica todos los archivos y excepciones. Evitar cambios en AppShell que impongan alto a formularios arbitrarios; ListadoModulo declara su región disponible.

## Validation
Unitarias de preferencia, límites, persistencia, filtros y páginas; regresiones locales/servidor; build/lint; navegador móvil/escritorio en varias familias con capturas fuera del repo. Revisar impresión y captura editable. Sin QA hasta autorización y coordinación específica.
