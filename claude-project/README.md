# Claude Project — réplica individual

Esta carpeta contiene los artefactos para que cada persona del equipo
configure su propio Claude Project en `claude.ai/projects` con las mismas
instructions y knowledge base que usamos colectivamente.

> **Nota de alcance.** Esto es para Claude **Project** (la app web en
> `claude.ai`). Es distinto de `CLAUDE.md` en la raíz del repo, que orienta a
> **Claude Code** (el CLI/IDE). Los dos coexisten y se mantienen alineados,
> pero apuntan a herramientas diferentes.

---

## Cómo crear tu Project

1. Entra a <https://claude.ai/projects> → **New project**.
2. Nombre: `Millet ERP`. Descripción opcional.
3. **Custom instructions**: pega el contenido completo de
   [instructions.md](./instructions.md).
4. **Knowledge base**: sube los archivos listados en
   [knowledge-base.md](./knowledge-base.md). La forma más cómoda es generar
   el zip con `./bundle.ps1` y arrastrarlo al uploader.
5. Modelo recomendado: el más capaz disponible (Opus o Sonnet — depende del
   plan).

---

## Cómo mantenerlo sincronizado

Cuando un ADR cambie, se agregue uno nuevo, o se actualice cualquiera de los
documentos del manifiesto, regenerar el bundle y resubirlo:

```powershell
.\bundle.ps1
# Sube claude-project/millet-erp-kb.zip a tu Project (reemplazando los archivos previos).
```

Idealmente esto lo hace el owner cada vez que se mergea un PR que toque
`docs/decisiones/`, `CLAUDE.md`, `README.md`, `CONTRIBUTING.md` o
`docs/arquitectura.md`. Quien quiera puede correrlo desde su clon local.

---

## Para qué sirve un Project con esta KB

- Hacer preguntas de arquitectura (`¿qué dice el ADR-0011 sobre multi-empresa?`)
  con respuestas que citan el documento exacto.
- Redactar nuevos ADRs siguiendo la plantilla y el estilo establecido.
- Revisar diseños propuestos contra las convenciones del proyecto antes de
  abrir PR.
- Onboarding asistido: un dev nuevo puede preguntarle al Project en lugar de
  releer 400 KB de docs en orden.

No reemplaza a Claude Code (CLI) para edición de código real — para eso,
abre el repo en VS Code con la extensión y deja que `CLAUDE.md` orqueste.
