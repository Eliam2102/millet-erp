# Millet ERP Constitution

## Core Principles

### I. Operación y autoridad verificables
La bóveda Obsidian es la referencia operativa; código, tarea, despliegue, prueba y
aceptación se documentan por separado. No se atribuyen decisiones al cliente a partir
de respuestas de asistentes. Se reutiliza lo recibido antes de solicitar información.

### II. Cambios mínimos y fronteras de módulos
Se reutilizan puertos, servicios y validadores del monolito modular. Cada módulo es
el dueño de su esquema. No se añaden dependencias, subsistemas o funcionalidades
ajenas al objetivo aprobado. Se conserva la compatibilidad de históricos y contratos.

### III. Regresiones antes de corregir lógica
Toda corrección no trivial debe demostrar primero la falla con una prueba dirigida y
luego probar el resultado corregido. Se cubren concurrencia, duplicados, orden de eventos,
reversas y validación del servidor cuando los afecte el cambio. No se omiten pruebas rojas.

### IV. Alcance y seguridad preservados
Se conservan permisos, empresa/sucursal, separación de funciones, validación de adjuntos,
idempotencia y control de concurrencia. No se amplía acceso ni se anula una incidencia
para simular éxito. Secretos y documentación privada no entran al repositorio.

### V. Cierre demostrado
El pase de pruebas locales o CI no sustituye QA. Un recorrido se declara aprobado sólo
con sus pasos y resultado final verificados. Muestras ficticias se identifican y se
separan de aceptación fiscal/productiva. Las tareas permanecen abiertas si falta evidencia.

## Restricciones de implementación
C#/.NET, PostgreSQL y los mecanismos actuales de MediatR/Outbox/Service Bus constituyen
la base existente, no decisiones nuevas de este documento. Seguir ADR-0051 para sucursal
y docs/qa/gate-integracion.md. Logs, capturas y resultados quedan fuera del repositorio.

## Flujo de desarrollo y revisión
Usar las etapas oficiales de Spec Kit: especificación, investigación/plan, tareas,
implementación, análisis de convergencia cuando corresponda. Registrar supuestos y límites.
Respetar la autorización vigente de Eliam; preparar cambios externos concretos antes de
solicitar cualquier autorización adicional necesaria. Actualizar fichas y Bitácora local.

## Governance
Este documento adapta reglas existentes a Spec Kit; no desplaza AGENTS.md, CLAUDE.md,
las instrucciones de la sesión ni decisiones vigentes de Millet. Modificaciones sustantivas
requieren trazabilidad y revisión del owner. Versionar semánticamente y comprobar la
conformidad en el plan y antes de publicar. La fecha de ratificación indica adopción local
del método por instrucción de Eliam, no aprobación contractual de requisitos por Millet.

**Version**: 1.0.0 | **Ratified**: 2026-11-11 | **Last Amended**: 2026-11-11
