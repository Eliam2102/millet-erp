# ADR-0036: Estrategia de reportería del ERP — motor nativo, no Crystal embebido

- **Estado**: Aceptada
- **Fecha**: 2026-05-22
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: reportería, frontend, exportación, almacén, cxp, contabilidad, cross-módulo

## Contexto y problema

El levantamiento del módulo Almacén ([`docs/modulos/almacen/00-levantamiento.md`](../modulos/almacen/00-levantamiento.md) §9) declara que el Portal Millet (`192.168.1.38/PortalSap/`) queda **deprecado** al cerrar el módulo. El portal expone hoy 10 reportes Crystal sobre SAP B1; el nuevo ERP debe asumirlos. Esta decisión cruza otros módulos: CxP define sus propios reportes (`cuentas-por-pagar/00-levantamiento.md` §10), Compras-OC tiene reportes operativos, y el módulo 10 del back-office ("Reportes y BI") está explícitamente en el roadmap.

Sin una decisión transversal sobre **qué motor de reportería usa el ERP**, cada módulo arrancaría con su propio enfoque (algunos exportarían PDF con HTML+CSS, otros con librerías distintas, otros con Crystal embebido). Eso fragmenta el código, complica la UX, y multiplica las dependencias del backend.

**Restricciones del problema:**

- El stack frontend ya está decidido: React + TanStack Router + shadcn/ui (ADR-0023, ADR-0002). Cualquier motor de reportería debe encajar en ese stack.
- El backend es .NET 8 monolito modular (CLAUDE.md). No queremos arrastrar runtimes pesados de Crystal o SSRS al deployment.
- Los usuarios del back-office (Carlos en Almacén, Auxiliar de CxP, Compras) **imprimen y exportan a Excel diariamente**. Los reportes no son solo visualización en pantalla — son artefactos para firmas, archivo físico, entrega a auditoría externa y consumo en hojas de cálculo.
- El cierre de mes (Almacén) y la antigüedad de saldos (CxP) son los dos reportes más críticos del MVP. Tienen volúmenes moderados (cientos a miles de líneas) — no son cubos analíticos.
- El módulo 10 (Reportes y BI) sí tendrá necesidades analíticas reales (dashboards, cruces multidimensionales). Pero eso es **post-MVP** y vive en su propio módulo cuando llegue.

## Drivers de la decisión

- **Deprecar el Portal Millet completo** sin reintroducir Crystal en el nuevo stack.
- **Consistencia transversal**: todos los módulos del back-office siguen el mismo patrón para sus reportes operativos.
- **Stack mínimo**: evitar agregar runtimes nuevos al backend o al deployment.
- **Exportación obligatoria a PDF y Excel** desde cualquier reporte.
- **Bajo costo de implementación por reporte**: cada reporte nuevo debe ser una operación de horas, no de días.
- **Separación clara entre reportería operativa (este ADR) y analítica/BI (futuro)**. No mezclar.

## Opciones consideradas

1. **Crystal Reports embebido** (replicar el patrón del Portal Millet).
2. **SSRS / Power BI Report Server** (Microsoft, on-prem).
3. **JsReport / Carbone / DocRaptor** (server-side templating en Node/Java/.NET).
4. **Motor nativo: componentes React + endpoints JSON + exportación client-side.**
5. **QuestPDF / iText 7 en backend** (genera PDF directo desde .NET, sin templating engine).

## Decisión

**Motor nativo del ERP basado en componentes React + endpoints JSON + exportación client-side**, con las siguientes piezas concretas:

| Capa | Tecnología | Responsabilidad |
|---|---|---|
| **Backend (datos)** | Endpoints `GET /api/v1/<modulo>/reportes/<nombre>` que devuelven **JSON estructurado** con headers, filtros aplicados y filas. Paginación cuando aplique. | Producir los datos del reporte. Sin presentación. |
| **Frontend (presentación)** | Componente React por reporte, con filtros, tabla virtualizada (TanStack Table cuando ya está en el stack), totalizadores. | Renderizar el reporte en pantalla. |
| **Exportación PDF** | `@react-pdf/renderer` (mismo stack, server-side rendering opcional). Cada reporte tiene su componente `<Reporte...PDF>` que reusa los mismos datos del JSON. | Exportación a PDF preservando estilos y branding. |
| **Exportación Excel** | `exceljs` client-side. Cada reporte expone un botón "Exportar a Excel" que genera el archivo en navegador a partir del mismo JSON. | Exportación a Excel con celdas tipificadas (números, fechas, fórmulas opcionales). |
| **Impresión** | CSS print + `data-print="hidden"` (ya en uso, ver memoria `project_estructura_ventanas_erp`). | Vista de impresión limpia desde el navegador. |

**Implicación arquitectónica:** la **"plantilla" del reporte es código React**, no un archivo `.rpt` ni un template Handlebars/Mustache. El reporte vive en `frontend/src/components/reportes/<modulo>/<NombreReporte>.tsx` con su variante PDF en el mismo archivo o adyacente. Cambios al reporte se hacen como cambios al frontend (PR estándar, revisado, con preview en branch).

**Estandarización por convención** (no obligatoria por contrato — el repo refuerza con eslint/lint y revisión de PR):

- Backend devuelve `{ titulo, generadoEn, filtrosAplicados, columnas: [{key, label, tipo, alineacion}], filas: [...], totales? }`.
- Frontend componente envuelve en un `<ReporteShell>` compartido (header con título + fecha + filtros aplicados, botones de exportar, área de impresión, footer con paginación).
- Endpoints de reporte llevan permiso canónico `<modulo>.reportes.<nombre>` (ADR-0007).
- Todos los reportes tienen una versión "vista previa" (límite 100 filas) y "completa" (paginada o stream).

## Consecuencias

**Positivas**

- **Cero runtime nuevo en el backend.** No se agrega Crystal, SSRS, Java o engines externos. El ERP sigue siendo .NET 8 + React.
- **Reutilización del stack**: el desarrollador de React puede hacer un reporte; no se necesita un especialista en Crystal.
- **Consistencia visual** automática (shadcn/ui), preview instantáneo en navegador, hot-reload durante desarrollo.
- **Exportación robusta** vía `@react-pdf/renderer` (PDF con paginación, headers, footers controlados) y `exceljs` (Excel con celdas tipificadas, no CSV).
- **Versionamiento limpio** vía git: los reportes son código revisado por PR, no archivos binarios opacos.
- **Test fácil**: componente React tiene snapshot test; endpoint JSON tiene contract test.
- **Independencia del módulo de BI futuro**: cuando llegue el módulo 10, Power BI Embedded (u otro) cubre BI sin chocar con esto. Los reportes operativos de cada módulo siguen viviendo en sus respectivos `frontend/src/components/reportes/<modulo>/`.

**Negativas**

- **No es WYSIWYG para usuarios finales.** Cambios a la plantilla de un reporte requieren código + PR. Un usuario power no puede "diseñar" un reporte por sí mismo. **Mitigación**: parametrización rica (filtros, columnas opcionales, agrupaciones). Si la necesidad de "diseñar reportes" surge, se evalúa Metabase o similar en su momento.
- **Reportes muy complejos (>50k filas, cubos)** chocan con el modelo cliente-side. **Mitigación**: tales reportes se difieren al módulo de BI; el motor nativo cubre reportes operativos hasta ~5k filas con virtualización.
- **Curva de aprendizaje de `@react-pdf/renderer`** para desarrolladores que no lo conocen. **Mitigación**: un par de plantillas exemplares (cierre de mes, antigüedad de saldos) sirven de referencia para el resto del equipo.

## Descartadas

**Crystal Reports embebido** — descartado por:
- Runtime pesado y costoso en términos de licencias.
- Acopla el backend a una stack heredada que el proyecto está saliendo.
- Replicaría el problema que motivó deprecar el Portal Millet (mantenimiento de 10 `.rpt` ilegibles).
- Los archivos `.rpt` no se versionan bien en git (binarios opacos).

**SSRS / Power BI Report Server** — descartado para reportería operativa por:
- Requiere infraestructura adicional (SQL Server Reporting Services).
- El modelo de subscripción/scheduling no es necesario para reportes operativos diarios.
- Power BI Report Server tiene licenciamiento que no se justifica para 10–20 reportes operativos.
- Sí se considera **Power BI Embedded para el módulo de BI futuro**, pero eso es ortogonal a este ADR.

**JsReport / Carbone / DocRaptor** — descartado por:
- Agregan un servicio externo (más infraestructura, otra red, otro punto de falla).
- DocRaptor es SaaS de pago.
- JsReport o Carbone autohospedados son contenedores Node/Java adicionales que no aportan valor frente al patrón React nativo.

**QuestPDF / iText 7 en backend** — descartado como motor principal por:
- Acopla la presentación al backend; la "plantilla" sería C# code-behind, mezclando dominio con renderizado.
- Pierde el preview instantáneo del frontend.
- Es buena opción para casos puntuales (PDFs no-reporte como acuses de recepción o comprobantes de salida — ver "Notas de implementación").

## Notas de implementación

**Inmediatas (sin código aún):**

1. **Pendiente confirmar:** la decisión de PDF generation también cubre los **acuses de recepción**, **comprobantes de salida** y **listas de conteo** de Almacén (§9.5), los **comprobantes de pasivos autorizados** de CxP, y los **PDFs de OC** que envía Compras al proveedor. Para esos casos puntuales que se generan server-side (no son reportes con filtros), evaluar si conviene `@react-pdf/renderer` en SSR o **QuestPDF en .NET**. Esta decisión se cierra en un ADR hijo dedicado a "Generación de PDFs operativos" (ya existe ADR-0025 que se revisa y posiblemente se actualiza).
2. **Archivos a crear:**
   - `frontend/src/components/reportes/ReporteShell.tsx` — componente compartido (header + filtros + tabla + botones exportar + footer).
   - `frontend/src/components/reportes/exportExcel.ts` — utilitario `exceljs` con tipado por columna.
   - `frontend/src/components/reportes/ReportePdfBase.tsx` — plantilla base de `@react-pdf/renderer` con branding Millet.
3. **Permisos canónicos:** cada módulo declara `<modulo>.reportes.<nombre_reporte>` en su seed de permisos (ADR-0007).
4. **Reportes MVP que materializan este ADR:**
   - **Almacén:** ALFAK-HISTORIAL-ALMACEN (cierre de mes) y SAP-REPORTE-EXISTENCIA-MP-CNK.
   - **CxP:** Antigüedad de saldos, Antigüedad de anticipos, Cartera por categoría × revisión, CFDIs sin capturar.
   - **Compras-OC:** Partidas abiertas (ver `compras-ordenes-compra/01-diseno.md`).

**Diferidas (post-MVP):**

5. Módulo de BI (Power BI Embedded u otro motor analítico) — fase posterior, no compite con este ADR; vive en `Reportes y BI` (módulo 10 del CLAUDE.md).
6. Subscripciones a reportes (email recurrente con PDF adjunto) — evaluable cuando exista el módulo Notificaciones (ADR-0026).
7. Reportes 3–6 del Portal Millet (catálogo en `almacen/00-levantamiento.md` §9.3).

**ADRs adyacentes que conviene revisar a la luz de este:**

- [ADR-0025](0025-generacion-pdfs.md) — generación de PDFs operativos. Conviene actualizarlo para reflejar que `@react-pdf/renderer` es el motor para PDFs derivados de reportes (mismo dato JSON), mientras que QuestPDF puede quedarse como opción para PDFs no-reporte (acuses, comprobantes con plantilla simple).
- ADR-0007 (RBAC) y ADR-0021 (versionado API) ya cubren los endpoints `GET /api/v1/<modulo>/reportes/<nombre>` sin cambios.
- ADR-0023 (frontend stack) — confirma que TanStack Table y `@react-pdf/renderer` son compatibles con el stack vigente.
