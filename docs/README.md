# Documentación de Millet ERP

Punto de entrada a la documentación del proyecto. Si llegaste aquí buscando
cómo arrancar localmente, ve al [README raíz](../README.md) y a
[CONTRIBUTING.md](../CONTRIBUTING.md).

| Documento | Para qué |
|---|---|
| [arquitectura.md](arquitectura.md) | Vista completa: módulos, flujos, eventos, multi-empresa, auth. Léelo antes de tocar código nuevo (15-20 min). |
| [decisiones/](decisiones/) | Architecture Decision Records (ADRs). Decisiones inmutables y su justificación. |
| [decisiones/README.md](decisiones/README.md) | Índice de ADRs por tier (fundación / aplicación / dominio / DevOps) y backlog de decisiones pendientes. |
| [decisiones/template.md](decisiones/template.md) | Plantilla para nuevos ADRs. |
| [levantamientos/](levantamientos/) | Levantamientos técnicos por módulo de negocio (entregables del área funcional antes de desarrollo). |
| [onboarding-dev-seed-users.md](onboarding-dev-seed-users.md) | Roles y `oid` de los usuarios seed del modo `FakeForLocalDev`. |

---

## Cuándo escribir qué

- **ADR** (`decisiones/00NN-*.md`): cuando se toma una decisión técnica con
  varias alternativas razonables y queremos preservar la justificación. Una
  decisión, un ADR. Inmutables: si cambia la realidad, ADR nuevo que
  reemplaza al viejo.
- **Levantamiento** (`levantamientos/NN-modulo.md`): cuando llega un módulo
  de negocio nuevo y necesitamos capturar alcance, reglas, integraciones y
  esquema antes de empezar a codear.
- **`arquitectura.md`**: vista cross-módulo. Se actualiza cuando cambia algo
  estructural (un patrón nuevo, un componente fundacional, una integración
  externa nueva).
- **`CLAUDE.md`** (raíz): contexto que Claude Code (CLI) carga
  automáticamente. Convenciones cortas, no doc larga.
- **`claude-project/`** (raíz): instructions y knowledge base para replicar
  el Project en `claude.ai/projects` individualmente.
