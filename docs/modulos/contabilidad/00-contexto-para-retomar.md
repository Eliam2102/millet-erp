# F1-CON-01 — contexto para retomar la planificación

Fecha: 1-oct-2026. Rama `feature/F1-CON-01-catalogo-contable` (parte de `main`, punta `0096cb8`).
Estado: **plan escrito, sin aprobar, sin código implementado.** Plan completo en
[`01-plan-catalogo-cuentas.md`](01-plan-catalogo-cuentas.md).

## Estado (actualizado 1-oct-2026, plan v0.2)

- Decisiones P1–P13 **cerradas** (§18 del plan; las provisionales se reabren con datos reales). Pendientes del dueño/TL: roles que reciben los permisos (P7), interpretación del criterio "mapeo documentado", ruta local de datos reales.
- Se añadió §20 **Preparación para datos reales** (config, alias, perfilado, errores accionables, fixtures, checklist).
- Estimación vigente: **≈50 h** → 01a ≈38 h (hasta ~12-oct) y 01b ≈12 h (hasta ~14-oct); supuestos en §14.3.
- ADR a escribir: **0054, 0055, 0056** (el 0053 ya es permisos personalizados, en `main`).
- El fix de permisos (PR #24) y ADM-06/07 **ya están en `main`**; la rama está 31 commits atrás. Antes de implementar: rebase autorizado por el dueño, correr migraciones de los 12 contextos (+ el nuevo), añadir los 3 permisos también a `permission-codes.test.ts` y regenerar `routeTree.gen.ts`.
- **Actualización 2-oct-2026:** implementación 01a (backend) y 01b (UI) terminadas y subidas; hoja «Plan de cuentas-VILO» confirmada; los 12 códigos de 13 caracteres son depreciación acumulada y se resuelven sin homologar (ver plan §20.10). Pendiente de Contabilidad: naturaleza, afectabilidad, cuentas de control y la homologación propuesta.
- ~~No implementar hasta que el dueño apruebe el plan v0.2.~~ (superado)

## Cómo retomar

- Trabajar **solo en el worktree** `.claude/worktrees/agent-a711ea266f97f0107` o en esta rama; no en el directorio principal, donde hay cambios sin commitear de otra tarea.
- No hacer commit/push de código sin permiso explícito del dueño (CLAUDE.md).
- No inventar cuentas ni valores de negocio; usar fixtures marcados como no reales (`FIX-*`).
- Archivo oficial y equivalencias requieren aprobación de Contabilidad/Guillermo antes de cargar datos definitivos.
