# UI-NAV · entrega local para revisión

Fuente: instrucciones y decisión de Eliam del 10-oct-2026, maqueta
`outputs/Claridad_ERP_Millet/K_Propuesta_menu_y_administracion.html` del directorio
padre. Rama actual `feat/UI-NAV-menu-y-administracion`, HEAD base `1cbd3b7`.
El `main` local (`4933872`) es antecesor de este HEAD; se conserva el corte más
reciente del worktree. No se hizo checkout de otro worktree, commit ni push.

## Qué se consiguió

- Menú oscuro de 248px, una columna, grupos por proceso, operación/consulta/reportes
  visibles, filtro de nombres, nombres completos e íconos diferenciados.
- Empresa y sucursal arriba; persona, acceso único a Administración, cerrar sesión
  y contraer abajo. Contraído de 64px, tooltip y nombres accesibles; preferencia
  protegida por try/catch. El filtro oculto no limita los íconos al contraer.
- Mismos permisos, incluido cualquiera de varios permisos. Datos maestros en
  Compras y Facturación, con las mismas URLs y permisos granulares.
- Configuración de todos los paneles en Administración → Por módulo. Se conservan
  también los gates anteriores de las URLs genéricas de settings, aunque un acceso
  cambie de lugar. Centros de costo tiene solo configuración en esta base: su
  entrada abre la página raíz existente; las dos opciones están en Administración.
- Se retiran ModulePanel, AppLauncherModal, sus pruebas y AdminLandingCard sin
  consumidores. AppLauncherCard se conserva en páginas de entrada existentes.
- Administración por filas: siete secciones simultáneas, índice por anclas que
  señala la sección visible, buscador, descripciones sin códigos internos y SAT
  agrupado como solo consulta. Sin acordeones ni pestañas.
- Scroll separado de menú y contenido; impresión oculta navegación y libera el
  alto del contenido. El engrane anterior llevaba a `/admin`: se retiró. Solo
  exponía esa función; cerrar sesión se conserva directamente al pie del menú.
- Tab bar móvil con Inicio, Pendientes (bandeja de Inicio), Buscar y Módulos
  (drawer con todas las pantallas). La base solo tenía drawer, no tab bar.
- DESIGN §3 y equivalencias de íconos, tokens, FIGMA/README y ADR-0032 actualizados.

## Evidencia automatizada

| Comprobación | Resultado |
|---|---|
| `npx tsc --noEmit -p tsconfig.json` | Exit 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | Exit 0; verificación explícita de aplicación |
| `npm run -s typecheck:test` | Exit 0 |
| `npm run -s lint` | Exit 0; 0 errores, 10 advertencias en archivos no modificados |
| `npx vitest run` | Exit 0; 373 archivos y 2,117 pruebas aprobadas |
| Revisión final de prueba del índice de Administración | 5 pruebas aprobadas |
| `npm run build` | Exit 0; advertencia por paquetes generados mayores de 500 kB |
| `git diff --check` | Sin problemas |
| Código nuevo/migrado de shell, Administración y selectores | Sin hex sueltos ni colores de paleta default |
| Backend | No aprobado: intento inicial cancelado; intento sin restauración: NETSDK1004 (assets ausentes); restauración acotada con servidores de compilación desactivados: NU1301, 38 errores por DNS de api.nuget.org |

Logs en `evidencia/`. Pruebas nuevas: permisos parciales y alternativos, filtrado,
maestros en sus módulos, acceso único a Administración, contraer/expandir y
persistencia/fallo de almacenamiento, navegación móvil directa, orden/búsqueda
administrativa, índice visible, contadores completos y avisos a partir de respuestas
API de prueba. La suite anterior de AppLauncherCard sigue vigente para las páginas
de módulo; se retiran pruebas de los componentes eliminados.

No hay handlers ni endpoints nuevos. No se cambiaron archivos de backend, infra
ni `.env*`. No se levantaron servidores ni se usó o activó fake-login.

## «Falta configurar o validar»: evidencia y límites

No se consultó una sesión real ni se verificaron estados actuales de Millet.
Se verificaron los contratos de endpoints existentes y el comportamiento con
respuestas de prueba explícitas; los avisos en ejecución requieren respuestas
correctas del API y sus permisos, nunca salen de la maqueta ni de supuestos.

| Candidato | Comprobación implementada | Límite |
|---|---|---|
| Equivalencias DEMO | GET de equivalencias de la sucursal seleccionada; observaciones con palabra DEMO → warning «Datos de ejemplo» | Sin sucursal, permiso de lectura o respuesta correcta, no hay aviso. No revisa automáticamente todas las sucursales |
| Series sin capturar | GET de series por empresa/sucursal con `limit=1`; `total=0` → warning «Por validar», texto «sin series propias» | No afirma falta de tipos concretos, ausencia de series corporativas ni series de ejemplo |
| Festivos | GET de parámetros globales; parámetro ausente o valor vacío → warning «Por validar» | Una lista capturada no acredita revisión de Millet. No hay campo de validación; esa validación se omite |
| Origen de A+W | Omitido | GET `/api/v1/integraciones/aw/origen` no existe en el código base |

Las consultas fallidas se muestran como por confirmar, nunca como falta de datos.
Sin hallazgos se indica el alcance de la consulta; no se afirma configuración completa.
Los contadores del menú reutilizan Inicio y suman acciones de sus bandejas, no
registros únicos; sin datos completos no presentan un total parcial. No se agregaron
conteos de usuarios/sucursales ni estados «Completa» supuestos a las filas.

El contrato de sesión actual no incluye nombre del rol. Se deja `[ROL]`, conforme
al placeholder del diseño, y no se infiere «Administrador» por los permisos.
Reportes continúa como marcador sin pantallas propias: no se añade acceso vacío;
los reportes existentes de cada módulo permanecen visibles en su módulo.

## Qué queda pendiente y siguiente acción

1. Claude revisa la pantalla con la sesión de Eliam: escritorio expandido y
   contraído, recorrido sin abrir secciones, teclado/foco/tooltips, nombre de
   empresa y selector de sucursal, búsqueda y scroll, índice de Administración,
   impresión y tab bar/drawer móvil. Comparar con la maqueta aprobada; no activar
   fake-login. Verificar también restricciones de usuarios con permisos parciales.
2. Consultar los avisos con respuestas de la sesión real: los estados actuales
   permanecen **Por confirmar**. Revisar los casos con y sin sucursal seleccionada.
3. Restaurar NuGet y ejecutar `dotnet build Millet.sln` en un entorno con acceso
   a api.nuget.org. Ejecutar `tools/validate-integration-isolated.sh` con Docker
   en el entorno de Claude; no se ejecutó esa suite aquí.
4. Incorporar la nota local de actualización de la bóveda incluida abajo. El
   sandbox no permite escribir directamente en Obsidian.
5. Revisar el diff y autorizar el siguiente paso. No se publicaron documentos,
   modificaron tareas externas ni enviaron mensajes.

Mensaje de commit propuesto, sin commit realizado:
`feat(navegacion): unificar menú lateral y ordenar administración por tareas`

## Archivos tocados

```text
M	design-system/DESIGN.md
M	design-system/FIGMA.md
M	design-system/README.md
M	design-system/tailwind.config.ts
M	design-system/tokens.css
M	design-system/tokens.json
M	docs/decisiones/0032-shell-de-navegacion-app-launcher.md
D	frontend/src/components/admin/AdminLandingCard.tsx
M	frontend/src/components/auth/EmpresaSelector.tsx
M	frontend/src/components/auth/SucursalSelector.tsx
M	frontend/src/components/layout/AccessSearch.test.tsx
M	frontend/src/components/layout/AccessSearch.tsx
M	frontend/src/components/layout/AppLauncherCard.test.tsx
M	frontend/src/components/layout/AppLauncherCard.tsx
D	frontend/src/components/layout/AppLauncherModal.test.tsx
D	frontend/src/components/layout/AppLauncherModal.tsx
M	frontend/src/components/layout/AppShell.tsx
M	frontend/src/components/layout/MobileSidebar.test.tsx
M	frontend/src/components/layout/MobileSidebar.tsx
D	frontend/src/components/layout/ModulePanel.test.tsx
D	frontend/src/components/layout/ModulePanel.tsx
M	frontend/src/components/layout/Sidebar.tsx
M	frontend/src/components/layout/SidebarNav.tsx
M	frontend/src/components/layout/Topbar.tsx
M	frontend/src/features/inicio/modulos.ts
M	frontend/src/index.css
M	frontend/src/lib/admin/registry.ts
M	frontend/src/lib/admin/use-admin-registry.test.ts
M	frontend/src/lib/admin/use-admin-registry.ts
M	frontend/src/lib/nav.ts
M	frontend/src/routes/_app/admin/index.tsx
A	frontend/src/components/admin/AdminLandingPage.test.tsx
A	frontend/src/components/admin/AdminLandingPage.tsx
A	frontend/src/components/layout/AppShell.test.tsx
A	frontend/src/components/layout/SidebarNav.test.tsx
A	frontend/src/features/inicio/contadores-menu.test.ts
A	frontend/src/features/inicio/contadores-menu.ts
A	frontend/src/lib/admin/pendientes.test.ts
A	frontend/src/lib/admin/pendientes.ts
A	frontend/src/lib/admin/use-admin-pendientes.test.tsx
A	frontend/src/lib/admin/use-admin-pendientes.ts
A	frontend/src/lib/nav-catalog.ts
A	frontend/src/lib/nav-permissions.ts
```

Se agregan además esta entrega, la nota pendiente para Obsidian y los logs en outputs/UI-NAV/evidencia.
