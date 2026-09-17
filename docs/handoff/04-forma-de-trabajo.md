# Forma de trabajo para desarrollo

## Antes de tomar una tarea

1. Confirmar ID funcional, ola, módulo y responsable en Notion/ClickUp.
2. Leer criterio de entrada, dependencias, límite y aceptación.
3. Localizar código, migraciones, pruebas y documentación relacionada.
4. Verificar si depende de un insumo de Millet.
5. Si falta evidencia, registrar bloqueo; no inventar regla ni dato.

## Rama y cambio

- Partir de `main` actualizado.
- Una rama por tarea o cambio coherente: `feature/<id>-<descripcion>`, `fix/<id>-<descripcion>`, `docs/<descripcion>` o `chore/<descripcion>`.
- Mantener el cambio limitado al ID funcional/técnico.
- Incluir migraciones, pruebas y documentación cuando correspondan.
- No mezclar refactors generales con entrega funcional sin acuerdo.

`main` no se modifica directamente: requiere pull request, al menos una aprobación, conversaciones resueltas y rama actualizada. Los reviewers nominales quedan `Por confirmar` hasta registrar los usuarios de GitHub y el responsable técnico de VILO.

## Pull request

Todo PR debe declarar:

- ID y resultado esperado.
- Qué cambió y qué no cambió.
- Dependencias/insumos utilizados.
- Pruebas ejecutadas y resultado.
- Migraciones/configuración.
- Evidencia visual o técnica.
- Riesgo y reversión.
- Pendientes o bloqueos.

## Definiciones

**Terminado por desarrollo:** código/configuración, revisión, pruebas, migración técnica y evidencia completas; sin bloqueantes técnicos conocidos.

**Listo para UAT:** además, datos, usuarios, caso, resultado esperado, dueño funcional y ambiente disponibles.

**Aceptado:** Millet ejecutó o presenció el caso y dejó aprobación/evidencia.

**Liberado:** UAT, migración final, capacitación, corte, reversión y monitoreo completados y autorizados.

## Escalamiento

- Alcance, horas, secuencia o aceptación: Eliam/Ángel.
- Bloqueo técnico interno: responsable técnico de VILO `Por confirmar`.
- Ambiente, A+W, acceso o portal Millet: Jorge Toache.
- Regla funcional: responsable de área registrado en Notion.
- Seguridad o datos sensibles: detener y escalar; no copiar a tareas, PR o chat.
