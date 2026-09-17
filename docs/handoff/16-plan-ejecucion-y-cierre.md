# Plan ejecutable y cierre del handoff · Fase 1

## Objetivo de cierre

El handoff queda terminado cuando Geovany y Uzziel pueden entrar al repositorio y a la planeación, levantar el sistema, tomar una tarea de Ola 1A, saber qué construir/probar/documentar y escalar un bloqueo sin depender de una explicación verbal de Eliam o Ángel.

El Word enviado a Millet permanece como solicitud externa de insumos. No forma parte de esta guía interna y su recepción no detiene el trabajo local que tenga entradas ficticias o sandbox.

## Estado verificable al 16 de septiembre de 2026

| Frente | Estado | Evidencia | Falta para cierre |
|---|---|---|---|
| Revisión de secretos/configuración | Comprobado | `07-publicacion-y-seguridad.md`; revisión de archivos rastreados y configuración | Repetir antes de cada publicación que incorpore configuración o secretos |
| Repositorio privado | Comprobado | `https://github.com/Eliam2102/millet-erp` | Mantener privado |
| Línea base | Comprobado | `handoff-fase1-v1.3` → `d93e1258d694e9ef7f7e665e3ff750d60209b43d` | Ninguno para congelar la base |
| Protección técnica de `main` | Limitación confirmada | GitHub devolvió HTTP 403 para el repositorio privado bajo el plan actual | Plan compatible u organización; hasta entonces PR/revisión como control de proceso |
| Arranque limpio central | Comprobado | clon limpio, 12 migraciones, API/UI/login y gates descritos en `02-estado-verificado.md` | Repetir por cada desarrollador con su cuenta/equipo |
| Acceso Geovany/Uzziel | Pendiente | No hay usuarios exactos de GitHub confirmados | Recibir usuarios, invitar y comprobar aceptación/MFA |
| Inventario Fase 1 | Comprobado a nivel de trazabilidad | 139/139: `10-auditoria-35-existentes.md`, `15-auditoria-104-parciales-no-existentes.md` y CSV; gate renovado en `e6f5062` | Ejecutar `17-plan-validacion-35-existentes.md`, QA/regresión de las demás y UAT según corresponda |
| Ola 1A | Planeación preparada | 15 funciones, responsables, fechas, criterios y 102 h en `09`, `11` y `13` | Publicar tareas, iniciar, producir evidencia y cerrar dependencias |
| Notion | Cambio preparado, no publicado | `14-manifiesto-notion-handoff.md` | Conector MCP disponible, autorización y verificación posterior |
| ClickUp | Cambio preparado, no publicado | `13-manifiesto-clickup-ola1a.md` | Autorización, creación/actualización y relectura |

## Secuencia operativa

### Puerta 1 · Acceso y onboarding técnico

Responsables: Eliam para accesos; Geovany y Uzziel para ejecución individual.

1. Recibir los usuarios exactos de GitHub.
2. Invitar a ambos con el menor permiso suficiente para desarrollar.
3. Cada desarrollador acepta, confirma MFA y clona desde el remoto privado.
4. Cada desarrollador ejecuta `06-checklist-primer-dia.md` con base exclusiva.
5. Registrar equipo/fecha, commit, resultado de migraciones, pruebas y primer PR.

Salida: dos checklists completos y reproducibles. La prueba central previa no sustituye esta puerta.

### Puerta 2 · Publicación de la guía interna

Responsable: Eliam; ejecución mediante conectores autorizados.

1. Publicar en Notion únicamente el manifiesto `14` después de autorización.
2. Releer portada, páginas, bases, relaciones, propiedades y enlaces.
3. Crear/corregir en ClickUp las 15 tareas y transversales del manifiesto `13` después de autorización.
4. Releer tareas, responsables, fechas, horas, dependencias y enlaces Notion.
5. Cambiar las tareas coordinadoras sólo al estado indicado en el manifiesto; no cerrarlas por haber creado contenido.

Salida: Notion y ClickUp coinciden con la línea base v1.3 y con la Ola 1A congelada.

### Puerta 3 · Arranque de Ola 1A

Responsables: Geovany (8 funciones), Uzziel (7 funciones), revisión cruzada.

1. Tomar tareas en el orden técnico de `11-matriz-arranque-ola1a.md`.
2. Usar datos ficticios en 13 funciones, sandbox en ADM-09 y resolver internamente ADM-11.
3. En cada tarea registrar entrada usada, cambio, pruebas, evidencia, regresión y bloqueo de cierre.
4. No consumir la reserva para trabajo omitido: QA-1A ya contempla 8 h y el total operativo permanece en 102 h.
5. No marcar aceptada una función por compilar o por pasar una prueba aislada.

Salida: las 15 funciones tienen PR/evidencia; las cuatro heredadas como existentes tienen regresión; los bloqueos externos están explícitos.

### Puerta 4 · Validación de las 35 heredadas como existentes

Responsables: desarrollador de la ola correspondiente; revisor cruzado; usuario funcional en UAT.

Para cada ID de `10-auditoria-35-existentes.md`:

1. Levantar el módulo y abrir la ruta.
2. Preparar un caso reproducible sin datos productivos.
3. Ejecutar éxito y al menos una negativa relevante.
4. Vincular prueba automatizada aplicable.
5. Registrar captura/log/dato y regresión.
6. Corregir matriz/tarea si el resultado es parcial o no localizado.
7. Presentar en UAT cuando ambiente, datos y usuario estén disponibles.

Salida: ninguna de las 35 conserva cero horas operativas sin evidencia; `Resultado QA/UAT` cambia de `Pendiente` sólo con prueba registrada.

### Puerta 5 · Recepción de insumos Millet

Responsables: Jorge Toache coordina por Millet; Eliam/Ángel clasifican y asignan.

Cada entrega del cliente se registra como `Recibida`, `Incompleta`, `Requiere aclaración` o `Pendiente`. Antes de usarla se verifica vigencia, empresa, ambiente, responsable y canal seguro. Un insumo faltante bloquea únicamente la tarea o el cierre que realmente dependa de él; no bloquea el resto de la ola.

## Controles de no sobrepromesa

- Código localizado no significa flujo terminado.
- Prueba automatizada no significa UAT.
- Mock, stub o `NoOp` no significa integración real.
- `main` privado sin branch protection no significa control técnico obligatorio.
- Correo enviado no significa insumo recibido.
- Tarea creada no significa desarrollo iniciado.
- Desarrollo terminado no significa liberación a producción.

## Decisiones/insumos todavía necesarios de Eliam

1. Usuarios exactos de GitHub de Geovany y Uzziel.
2. Autorización expresa para ejecutar el manifiesto de Notion.
3. Autorización expresa para ejecutar el manifiesto de ClickUp.
4. Confirmación del revisor final del handoff; el borrador conserva a Samuel como candidato hasta validación.

## Criterio de cierre global

No declarar terminado este frente hasta comprobar conjuntamente:

- remoto privado y etiqueta exacta;
- acceso y onboarding individual de ambos desarrolladores;
- Notion publicado y releído;
- ClickUp publicado y releído;
- Ola 1A asignada y arrancada;
- inventario 139/139 trazable;
- evidencia/QA de las funciones ejecutadas;
- dependencias Millet registradas sin ampliar alcance automáticamente.
