# UIX-01 — Base UX/UI del ERP y brief inicial para Stitch

Estado: borrador local para revisión de Eliam. No es una aprobación visual, una actualización de ClickUp ni evidencia de implementación de los diseños.

## Qué existe hoy

| Área | Evidencia en este repositorio | Qué revisar en la propuesta |
| --- | --- | --- |
| Acceso | `frontend/src/components/auth/LoginScreen.tsx`, selectores de empresa/sucursal | Entrada con Microsoft en modo Entra ID; selector de desarrollo solo en modo local. Estados de espera y error. |
| Navegación | `frontend/src/components/layout/`, `frontend/src/lib/nav.ts`, `frontend/src/lib/admin/registry.ts` | Sidebar por módulo, launcher y área de Administración filtrados por permisos. No inventar dashboards o contadores. |
| Organización | rutas `frontend/src/routes/_app/admin/{sucursales,departamentos,puestos,empleados}/` | Relación empresa única → sucursales → departamentos/puestos → empleados. La empresa no es alta repetida ni eje primario de navegación. |
| Identidad | rutas `frontend/src/routes/_app/admin/{usuarios,roles}/` | Cuenta, rol, permisos y vínculo con empleado. Rol = qué puede hacer; sucursales asignadas = dónde puede operar. |
| Operación posterior | rutas bajo `compras`, `almacen`, `facturacion`, `cxc`, `cxp`, `tesoreria`, `centros-costo` | Hay superficies de código; la presencia de una ruta **no prueba** que el proceso esté aceptado o listo para producción. Diseñar cada módulo solo después de revisar su flujo y criterios propios. |

Fuentes de decisión: [ADR-0002](../decisiones/0002-sistema-de-diseno-shadcn-ui.md) (shadcn/ui, Tailwind, Radix y accesibilidad), [ADR-0032](../decisiones/0032-shell-de-navegacion-app-launcher.md) (launcher y permisos), [ADR-0051](../decisiones/0051-segmentacion-de-datos-por-sucursal.md) (una razón social, varias sucursales) y [patrones de Compras](../../frontend/docs/patrones-compras.md) (bandejas, detalle, formularios y estados). El diseño previo de Administración en `docs/modulos/administracion/05-frontend-diseno.md` es propuesta, no aprobación final.

## Regla para todas las propuestas

- Conservar el flujo de negocio y el alcance de permisos existentes; el rediseño visual no puede abrir acciones ni datos a otro rol/sucursal.
- Aprovechar los componentes y tokens de `frontend/src/components/ui/`, `frontend/src/components/erp/` y `frontend/src/index.css`. Cualquier nueva identidad de marca, paleta o tipografía corporativa queda **Por confirmar** con Millet.
- Mostrar estados de carga, sin datos, sin resultados, error recuperable, validación, guardado, conflicto y falta de permiso. Incluir desktop y móvil; teclado, foco y contraste legibles.
- En bandejas, preservar búsqueda/filtros, selección y regreso al contexto. En formularios, distinguir consulta de edición, indicar campos obligatorios y confirmar acciones de alto impacto.
- Entregar por pantalla: propuesta visual, interacción principal, estados, variación móvil, componentes reutilizables, decisiones respecto de lo actual y dudas de negocio. Stitch produce propuesta, no implementación ni aceptación.

## Primer paquete para Stitch: Administración e Identidad (UIX-02)

1. **Ingreso y contexto**: login Microsoft; transición/error; selección de empresa/sucursal si corresponde al usuario. No presentar contraseña del ERP ni flujo de alta pública.
2. **Entrada a Administración**: cards de Organización e Identidad visibles según permiso; mantener el launcher del resto de módulos separado del área de configuración.
3. **Estructura**: sucursales, departamentos, puestos y ficha de empleado. En empleado, mostrar sucursal base, puesto/departamento, sucursales operativas asignadas y acceso como conceptos distintos.
4. **Acceso**: bandeja/detalle de cuentas; alta de cuenta independiente donde aplique; alta o vínculo del colaborador desde Empleados; rol y matriz de permisos; estados de cuenta. No transformar el rol en un rol distinto por sucursal.
5. **Empalme**: mostrar cómo un administrador termina el alta y cómo el colaborador entra y ve solo lo permitido; diferenciar lo que depende de una integración Microsoft real de lo que se puede simular localmente.

Pantallas de referencia inicial: `/admin`, `/admin/sucursales`, `/admin/departamentos`, `/admin/puestos`, `/admin/empleados`, `/admin/usuarios`, `/admin/roles`. `/admin/empresas/$id` es detalle avanzado de la única razón social, no un wizard de multiempresa.

### Prompt base para Stitch (copiar y ajustar por pantalla)

> Diseña una propuesta de mejora UX/UI para el ERP de Vidrios Millet, orientada a personal administrativo mexicano. Es una sola razón social con varias sucursales. Conserva el shell existente: sidebar por módulo, app launcher para operación y área de Administración con cards filtradas por permisos. Base técnica visual: shadcn/ui, Radix y Tailwind; estilo sobrio, legible y de alta densidad informativa. No inventes dashboards, métricas, funciones, nuevas reglas de negocio ni branding corporativo no confirmado. Para esta iteración diseña [PANTALLA/FLUJO], cuyo objetivo es [OBJETIVO], para [ROL]. Muestra desktop y móvil, estado normal, vacío, cargando, error, validaciones y acción completada. Incluye anotaciones breves de navegación, jerarquía, campos, acciones y componentes reutilizables. El rol determina qué acciones existen y las sucursales asignadas determinan el alcance de datos. Entrega una propuesta visual y una lista de preguntas por validar, no código final.

## Secuencia y criterios de salida

1. Eliam genera en Stitch el paquete de Administración/Identidad usando este brief y adjunta enlaces o capturas a UIX-02. **Pendiente**.
2. Se revisa contra flujos existentes con Geovany (Administración) y Uziel (Identidad), y con Millet para decisiones de negocio/branding. **Pendiente**.
3. Se documentan diferencias aprobadas y se dividen en cambios frontend implementables, sin reemplazar ADM-01/ADM-02 ni asumir que una maqueta equivale a una función. **Pendiente**.
4. Para UIX-03 se repite por módulo operativo, partiendo de su proceso y aceptación específicos; no se aprueba en bloque por tener rutas de frontend. **Pendiente**.

UIX-01 puede cerrarse cuando Eliam valide este inventario/brief y las prioridades de pantallas; UIX-02 requiere propuestas visuales revisadas; UIX-03 requiere el plan por módulo con responsables y fuente funcional.
