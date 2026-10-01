# F1-CON-01 — contexto para retomar la planificación

Fecha: 1-oct-2026. Rama `feature/F1-CON-01-catalogo-contable` (parte de `main`, punta `0096cb8`).
Estado: **plan escrito, sin aprobar, sin código implementado.** Plan completo en
[`01-plan-catalogo-cuentas.md`](01-plan-catalogo-cuentas.md).

## Qué falta

1. El dueño aprueba o ajusta el plan y responde las 13 preguntas abiertas (cada una trae recomendación).
2. El dueño decide la estimación: el plan suma **34–37 h** contra las 18 h provisionales de la ficha. Recomendación del plan: dividir en **01a** (backend, permisos y pruebas, ~27 h) y **01b** (UI y evidencia, ~10 h), o ampliar horas.
3. Solo después se lanza la implementación. No implementar antes de la aprobación.

## Lo que no está en el plan

- **Choque con el fix de permisos (otra rama).** La rama `fix/permisos-personalizados-por-usuario` (sin integrar a `main` al escribir esta nota) modifica `PermisosCanonicos.cs`, `IdentidadDbContext` y agrega una migración de Identidad. F1-CON-01 también registra permisos canónicos. Integrar primero el fix y **rebasar** esta rama; commits aislados para facilitarlo.
- **Numeración de ADR.** El plan propone los ADR 0053 y 0054, pero el fix de permisos ya usa el **0053**. Renumerar a partir del **0054** al implementar.
- **Permisos.** El plan usa el rango de ids `0000000d-*` para los permisos de contabilidad; verificar que siga libre tras integrar el fix.
- **Ficha de Notion no consultada.** El plan se basó solo en el texto de la ficha entregado por el dueño. Si la ficha trae más, incorporarlo.
- **Divergencia con Centros de Costo.** CeCo (modelo a seguir) es global y no tiene `EmpresaId`; la ficha pide conservarlo. El plan usa `IPerteneceAEmpresa` (ADR-0011) y pide documentar la diferencia en un ADR.

## Cómo retomar

- Trabajar **solo en el worktree** `.claude/worktrees/agent-a711ea266f97f0107` o en esta rama; no en el directorio principal, donde hay cambios sin commitear de otra tarea.
- No hacer commit/push de código sin permiso explícito del dueño (CLAUDE.md).
- No inventar cuentas ni valores de negocio; usar fixtures marcados como no reales (`FIX-*`).
- Archivo oficial y equivalencias requieren aprobación de Contabilidad/Guillermo antes de cargar datos definitivos.
