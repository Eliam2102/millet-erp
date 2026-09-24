#!/usr/bin/env bash
set -euo pipefail

# Uso desde cualquier proyecto:
#   bash /ruta/a/planear-con-claude.sh /ruta/a/brief.md
# El brief debe incluir objetivo, alcance, restricciones y evidencia relevante.
# Este programa NO lee el proyecto ni concede permisos de edición a Claude.
# El asistente que implementa verifica el plan contra el código real.
# Bloqueado por defecto hasta confirmar que Opus 5.5 está cubierto por el plan:
#   CLAUDE_INCLUDED_USAGE_CONFIRMED=1 bash planear-con-claude.sh brief.md

if [ "$#" -ne 1 ] || [ ! -f "$1" ]; then
  printf 'Uso: %s /ruta/al/brief.md\n' "$0" >&2
  exit 2
fi
if ! command -v claude >/dev/null 2>&1; then
  printf 'Claude Code no está instalado o no está en PATH.\n' >&2
  exit 1
fi
if [ "${CLAUDE_INCLUDED_USAGE_CONFIRMED:-}" != "1" ]; then
  printf 'No se ejecutó Claude. Confirma primero que Opus 5.5 está incluido sin cargos adicionales en esta cuenta.\n' >&2
  exit 3
fi

brief_file="$(realpath "$1")"

# Ejecutar fuera del repositorio evita aceptar configuraciones locales con
# permisos amplios. El brief entra por stdin; Claude no recibe herramientas.
(
  cd /tmp
  claude -p --model claude-opus-5-5 --tools '' --permission-mode plan \
    --append-system-prompt 'Responde en español. Propón un plan verificable, distingue hechos de supuestos y no afirmes que algo está implementado sin evidencia. No solicites editar archivos.' \
    < "$brief_file"
)
