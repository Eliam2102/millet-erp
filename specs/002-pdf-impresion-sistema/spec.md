# Feature Specification: PDF e impresión de todo el ERP
**Feature Branch**: `fix/pdf-impresion-sistema`
**Created**: 2026-11-11
**Status**: Implementado y probado localmente; pendiente QA
**Input**: Generar PDF correctos en todos los botones; imprimir documentos preparados, sin capturas de pantalla.

## User Scenarios & Testing
### User Story 1 - Descargar documentos completos (Priority: P1)
El usuario descarga análisis de proveedor y cualquier reporte/documento con PDF.
**Independent Test**: Descargar reporte con 100 filas y verificar texto, filtros y totales.
**Acceptance Scenarios**:
1. Dado el análisis cargado, al elegir PDF obtiene un documento legible sin error de seguridad.
2. Dado un reporte paginado o con celdas calculadas, el documento contiene todas las filas y valores correspondientes al reporte aplicado.
### User Story 2 - Preparar impresión (Priority: P1)
El usuario elige Imprimir y revisa un PDF con páginas y texto antes de ajustar impresora o guardar.
**Independent Test**: Abrir impresión desde reporte y detalle; comprobar que no incluye navegación ni botones.
**Acceptance Scenarios**:
1. Cada botón de impresión genera o reutiliza el documento correspondiente y abre su vista PDF.
2. Si falla la generación o se bloquea la ventana, conserva la pantalla y muestra un error recuperable.
### Edge Cases
Reportes vacíos; múltiples páginas; textos largos; totales; celdas calculadas; documentos fiscales originales frente a borradores; ventana bloqueada; generación concurrente; cierre temprano.

## Requirements
### Functional Requirements
- **FR-001**: Todos los botones existentes de PDF generan o descargan documentos PDF con texto, sin imágenes de pantalla.
- **FR-002**: Todos los botones existentes de imprimir abren el PDF correspondiente listo para ajustar e imprimir.
- **FR-003**: Se preservan filtros, fechas, filas completas, valores calculados y totales.
- **FR-004**: Los detalles conservan identificación, estado, información de cabecera y partidas del documento; los CFDI usan su representación disponible sin presentarla como timbrada cuando no lo está.
- **FR-005**: Conservar autorizaciones y datos; no importar, vincular ni modificar QA o producción.
- **FR-006**: Informar errores y liberar recursos del visor al cerrarlo; impedir clics repetidos durante la generación.
### Key Entities
- Documento imprimible: título, contexto, filas, importes y PDF preparado; no crea registros persistentes.

## Success Criteria
### Measurable Outcomes
- **SC-001**: Los 14 puntos de impresión identificados abren documentos, con cero llamadas a impresión de la pantalla.
- **SC-002**: El análisis de proveedor genera PDF bajo las restricciones de seguridad publicadas.
- **SC-003**: Un documento de prueba de 100 filas conserva la primera, la última y los totales con texto seleccionable.
- **SC-004**: Ventana bloqueada y error de generación tienen recuperación verificada.

## Assumptions
El visor PDF del navegador ofrece elección de impresora, papel y escala. Los PDF originales del servidor se reutilizan; no se regeneran ni timbran documentos fiscales. El alcance es código y pruebas locales, sin despliegue ni escrituras de negocio.
