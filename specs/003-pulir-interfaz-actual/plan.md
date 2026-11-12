# Plan · Navegación ocultable y cabecera adaptable

Rama fix/pulir-interfaz-actual. React19/TypeScript/Tailwind4/Radix/TanStack/ReactHookForm actuales. Sin dependencias ni consultas nuevas. ConstituciónI–V conservada: fuente/aceptación separadas, mínimo cambio, regresiones rojas antes de lógica, permisos/reglas intactos, evidencia local distinta deQA.

## Archivos y decisión
- AppShell: Dialog no modal ya instalado, fijo left0/top-topbar/bottom0, animaciones laterales del Sheet y motion-reduce. Conserva montaje del main; pin con margen de tokens. Zona hover4px persistente comprueba destino para cerrar el roce rápido y permitir entrar al panel. Sin listeners globales ni timers nuevos.
- SidebarNav/ModulePanel: mismo registro/RBAC, orden completo, filtro con limpiar, iconos y selección específica. Propiedades opcionales para panel embebido/fijado/cierre.
- Topbar: controles prioritarios con separación y sin marca textual Millet ERP; Popover existente para QuickCreate/ayuda/admin secundarios, condicionado por breakpoint/permisos. Escritorio sin overflow que oculte controles.
- AccessSearch: sólo presentación del trigger compacto hastaxl; atajos y búsqueda contextual sin cambios.
- EmpresaSelector/SucursalSelector de sesión: únicos callers funcionales Topbar, distintos de selectoresERP. Sólo CSS/etiquetas y tamaños adaptables; cambios de empresa/sucursal y errores intactos.
- QuickCreateMenu: texto Nuevo visible en herramientas, sin alterar acciones.
- CampanaAvisos: sólo max-width del viewport; backend/audio/lecturas intactos.
- NuevaRequisicion: tres FormSeccion existentes, alineación y superficies planas; sin esquema, validación ni persistencia nueva.

## Pruebas y pase
Pruebas dirigidas antes de lógica para menú/borde/herramientas/RBAC; suite layout/auth-selectores/forms/nav/piloto y avisos. Build/lint y revisión manual320/360/390/768/1366/1920, muestra multiempresa, foco y captura tras abrir/cerrar. Guardar capturas/logs fuera delrepo. Verificar parche contra develop reciente y preparar candidato aislado con artefacto/reversión; excluir visual-review.html/jsx. Sin commit externo, PR, push o despliegue hasta autorización. Riesgos a comprobar: foco, popovers anidados, cierre al salir/roce, pin sin superposición y controles en nombres largos. Teléfono físico/QA real posteriores.
