# Referencia visual en Figma

Archivo: [UI VIDRIOS MILLET](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=0-1).
Eliam lo indicó el 1-oct-2026 como referencia de cómo debe comenzar a verse
el ERP. Se revisaron estructura, renders y contexto de diseño de las 13 vistas
(12 numeradas y la variante adicional 3b). Este corte no acredita implementación
completa ni aceptación funcional de Millet.

## Pantallas verificadas

| Vista | Nodo directo | Tamaño |
|---|---|---|
| 1 · Inicio — bandeja cross-módulo | [5:7](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-7) | 1440 × 900 |
| 2 · Módulo — CxP › Facturas | [5:301](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-301) | 1440 × 900 |
| 3 · Buscar o ir a (⌘K) | [5:677](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-677) | 1440 × 900 |
| 3b · Buscar o ir a (⌘K) — sin texto: sugeridos y accesos directos | [5:1101](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-1101) | 1440 × 900 |
| 4 · Detalle — factura con conciliación de tres vías | [5:1557](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-1557) | 1440 × 900 |
| 5 · Registrar factura (panel lateral) | [5:1806](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-1806) | 1440 × 900 |
| 6 · Móvil — Inicio | [5:2259](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-2259) | 390 × 844 |
| 7 · Almacén › Recepciones | [5:2358](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-2358) | 1440 × 900 |
| 8 · Compras › Órdenes de compra | [5:2781](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-2781) | 1440 × 900 |
| 9 · Tesorería › Programación de pagos | [5:3185](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-3185) | 1440 × 900 |
| 10 · CxC › Cartera | [5:3581](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-3581) | 1440 × 900 |
| 11 · Facturación › Comprobantes | [5:3978](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-3978) | 1440 × 900 |
| 12 · Centros de costo › Presupuesto vs ejercido | [5:4375](https://www.figma.com/design/npuhEBfcWiHrUfGZUDW8ZH/UI-VIDRIOS-MILLET?node-id=5-4375) | 1440 × 900 |

El nodo `0:1` es la página completa. Para obtener contexto de diseño usa los
nodos de pantalla de esta tabla; la extracción sobre la página devolvió un aviso
de selección vacía. Las fuentes son IBM Plex Sans y IBM Plex Mono, y los colores
principales coinciden con los tokens adjuntos.

## Comparación con la rama de migración

| Elemento | Referencia Figma | Estado observado en código |
|---|---|---|
| Navegación | Rail oscuro 76px, ícono y etiqueta, Admin al fondo, sin logo | Rail de 76px implementado; módulos permitidos en el orden visual y Admin al fondo. Móvil conserva AppLauncher |
| Panel de módulo | Panel blanco 240px, filtro y secciones | Panel de 240px implementado con filtro, secciones, selección y contraer/expandir; utiliza el registro filtrado por permisos |
| Topbar | Breadcrumb, búsqueda global, sucursal, Nuevo, notificaciones, avatar | Breadcrumb, altura 56px, Nuevo y avatar adaptados. Buscador de accesos ⌘K/Ctrl+K implementado con permisos; búsqueda contextual conservada dentro del diálogo. Acciones y registros globales pendientes |
| Inicio | 4 indicadores, Requiere tu acción, Recientes y Módulos | La ruta Inicio muestra contexto y datos de sesión; aún no es la bandeja del diseño |
| Listados | KPIs, tabs, filtros, tabla densa y paginación integrados | La primera etapa sólo migró controles; faltan contenedores y consumidores de tablas |
| Detalle y captura | Lista maestra 320px, detalle por secciones y sheet 560px | Pendiente de migración visual de estos patrones y sus consumidores |
| Móvil | Header, búsqueda, listas y barra inferior | El shell conserva el drawer con botón de menú |

Esta comparación proviene de lectura de `AppShell`, `SidebarNav`, `Topbar` y la
ruta Inicio; la comprobación del shell usó una vista aislada con datos vacíos y sesión de prueba, sin conexión al API. No acredita una sesión real. La primera
etapa de controles sigue siendo la base reutilizable del PR 27.

## Orden para continuar

1. Migrar el shell: rail, panel de módulo, topbar y contenido, conservando rutas,
   permisos y acceso a acciones existentes. Comparar con Inicio y Compras.
2. Adaptar una pantalla real de Órdenes de compra como referencia de listado;
   después propagar el patrón a los otros módulos.
3. Migrar detalle, sheets y búsqueda de registros/acciones; después completar Inicio y móvil con los
   datos y capacidades reales disponibles.

La maquetación contiene ejemplos, conteos, prioridades y acciones. No copiar sus
datos ni considerar que el diseño implementa esos servicios. No habilitar rutas
sin permiso ni reemplazar estados del dominio con los textos del prototipo.
Conserva alertas necesarias de sesión y de asignación al reorganizar Inicio.

## Detalles por conciliar al implementar

- La guía define topbar de 56px; el frame importado mide 57px. Revisar borde y
  tamaño efectivo en navegador al comparar, sin introducir offsets arbitrarios.
- Los placeholders usan `#757575` y algunos bordes de checkbox `#767676`,
  que no aparecen en los tokens adjuntos. Conciliar estos pares antes de migrarlos;
  no ampliar silenciosamente la paleta.
- La importación `html.to.design` produjo posicionamiento absoluto y algunos
  textos estrechos. Construir con los layouts React existentes y comprobar
  cortes/ellipsis, conservando la composición de referencia.

Antes de implementar un nodo, obtener su contexto de diseño y screenshot actual
con el plugin Figma. Reutilizar los componentes del ERP y descargar los assets
estáticos requeridos según las instrucciones del plugin. Las URLs temporales
de assets no deben quedar en el código ni usarse como enlaces durables.


## Buscador de accesos · 1-oct-2026

AccessSearch usa Command y Dialog existentes, Plex, scrim y diálogo 640×520 de
la guía. Busca pantallas por nombre/módulo/ruta sin acentos, agrupadas por módulo;
abre con botón, ⌘K o Ctrl+K, flechas/Enter y cierra con Escape. Reutiliza nav y
adminRegistry; permisos refrescados y sin rutas duplicadas. En pantallas que
ya soportan búsqueda se conserva el campo contextual separado. No incorpora
folios, registros, sugerencias con conteos, favoritos ni creación de documentos
del prototipo; estos servicios no forman parte de este buscador de accesos.
