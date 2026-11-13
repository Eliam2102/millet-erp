# Feature Specification: Tablas adaptables y paginación uniforme

**Feature Branch**: `feature/tablas-adaptables`
**Created**: 2026-11-12
**Status**: Approved for implementation by user request
**Input**: Eliam solicita homologar tablas y bitácoras, aprovechar la pantalla grande/chica, elegir cantidad de registros y recordar la preferencia.

## User Scenarios & Testing

### User Story 1 - Elegir y recordar registros (Priority: P1)
El usuario consulta un listado, elige 10, 20, 25, 50 o 100 registros o ajuste automático y conserva su elección al volver.
**Why this priority**: evita listas limitadas a diez y repetir configuraciones.
**Independent Test**: cambiar tamaño, navegar, volver y comprobar rango, filas y preferencia.
**Acceptance Scenarios**:
1. Given un listado con varias páginas, When cambia cantidad, Then comienza en la primera página con la cantidad elegida.
2. Given una elección guardada, When vuelve a la vista, Then se conserva en ese navegador.
3. Given datos remotos, When pagina, Then usa la paginación del origen y conserva total/filtros; nunca presenta un lote parcial como el universo completo.

### User Story 2 - Aprovechar pantalla (Priority: P1)
El listado ocupa el ancho y alto disponibles después de título/filtros, con encabezado y controles accesibles.
**Why this priority**: evita tablas a media pantalla y facilita consultar más registros.
**Independent Test**: inspección a 390×844, 1366×768 y 1920×1080 con datos ficticios.
**Acceptance Scenarios**:
1. Given pantalla grande, When hay suficientes registros y modo automático, Then se muestran más filas que en una pantalla baja.
2. Given pantalla pequeña o columnas anchas, When consulta, Then mantiene texto legible y desplazamiento de columnas dentro de la tabla, sin desbordar la página.
3. Given detalle/editor con partidas, When cambia tamaño de pantalla, Then conserva captura, acciones y acceso a todas las partidas.

### User Story 3 - Navegar listados y bitácoras (Priority: P2)
Puede ir al principio/final y a una página concreta, conocer rango y total, y usar teclado.
**Why this priority**: agiliza colecciones grandes y normaliza controles.
**Independent Test**: primera/última, filtros que reducen total, vacío, carga y error.
**Acceptance Scenarios**:
1. Given filtros nuevos, When reducen resultados, Then no queda una página vacía fuera del rango.
2. Given cero resultados, When consulta controles, Then no aparecen rangos negativos ni acciones inválidas.

### Edge Cases
- Preferencia inválida o almacenamiento bloqueado: valor seguro y vista utilizable.
- Varias tablas en una vista y cambio de ruta: evitar mezclar offset/filtros.
- Listas remotas con límite máximo: respetar contrato, no descargar todo automáticamente.
- Tablas de captura, reportes jerárquicos/totales e impresión: preservar orden, edición y totales completos; no cortar filas automáticamente.
- Cantidad elegida mayor al alto: scroll interno, sin perder acceso al pie.

## Requirements
### Functional Requirements
- **FR-001**: Listados y bitácoras deben ofrecer cantidad seleccionable, ajuste automático y preferencia durable en el navegador por vista.
- **FR-002**: Una elección explícita debe prevalecer al redimensionar; cambiar cantidad/filtros reinicia página con límites válidos.
- **FR-003**: Listados deben ocupar ancho/alto útil y mantener scroll/tabulación/foco accesibles.
- **FR-004**: Paginación debe conservar totales, búsquedas, orden y alcance real de consultas.
- **FR-005**: Tablas de edición y reportes con agrupaciones conservan todas sus filas y captura; se homologan contención/distribución sin introducir cortes que alteren significado.
- **FR-006**: Inventariar todas las tablas y justificar las excepciones; comprobar componentes compartidos y familias de consumidores.
- **FR-007**: Conservar navegación aprobada, PR728, permisos, impresión y reglas de negocio.
### Key Entities
- Preferencia de cantidad por vista: automática o cantidad válida, guardada sin registros de negocio.
- Página: total, cantidad, posición y filtros; diferenciación de consulta completa y lote recibido.

## Success Criteria
### Measurable Outcomes
- **SC-001**: Todas las familias de listados identificadas usan controles uniformes o tienen excepción funcional documentada.
- **SC-002**: Cantidades 10/20/25/50/100 y restauración verificadas; vacío/cambio de filtro no produce rangos inválidos.
- **SC-003**: Cero desbordamientos horizontales de página en los tres tamaños de prueba; columnas anchas accesibles en su contenedor.
- **SC-004**: Listados con registros suficientes aprovechan el espacio vertical útil; los controles quedan accesibles sin scroll horizontal de página.
- **SC-005**: Captura editable, totales e impresión mantienen sus datos y comportamiento en pruebas de regresión.

## Assumptions
- Preferencia local al navegador y vista; no se incorpora sincronización de perfil en servidor.
- Cantidades máximas siguen el contrato de cada consulta; no se añade opción ilimitada.
- Móvil permite scroll interno para columnas que no caben legiblemente; no se reduce todo a texto diminuto.
- Implementación local autorizada. Este pedido no concede un nuevo pase QA ni despliegue durante ventanas ajenas.


## Ampliación de validación QA · 13-nov-2026
Fuente: prueba del lote PR736 publicado en QA y solicitud de Eliam de evitar peticiones desproporcionadas en Expedientes. La autorización posterior de integrar/publicar todo develop supera la restricción de implementación sólo local, conservada arriba como corte inicial. Correcciones de preferencia y estabilidad automática pertenecen al mismo alcance; no añaden reglas de negocio.

- **FR-008**: La medición automática debe ignorar placeholders de carga y estados sin filas reales. El paso carga→datos no debe alternar repetidamente la cantidad ni cancelar/reabrir consultas.
- **SC-006**: Con viewport/filtros/datos estables, una primera consulta y, si hace falta, un único ajuste de cantidad son suficientes; después de la respuesta no aparecen nuevas consultas sin interacción o actualización legítima de datos. Cero bucles de abortos.
- **US1/AC4**: Una cantidad guardada prevalece sobre el default del schema al volver sin cantidad en el enlace; un enlace nuevo explícito conserva cantidad y página también por navegación SPA sin desmontar.
- **US2/AC4**: Un ensayo de Expedientes con loading/filas reales de alturas distintas reproduce el problema antes de corregirlo y demuestra consultas acotadas después. Repetir en QA sin inducir ráfagas, conservando captura y cantidades manuales.
