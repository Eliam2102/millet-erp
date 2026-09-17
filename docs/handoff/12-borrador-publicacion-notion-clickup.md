# Borrador de publicación · Notion y ClickUp

Este documento deja preparado el cambio externo. No acredita que Notion o ClickUp ya hayan sido modificados.

## Corte de evidencia

- Repositorio privado: `https://github.com/Eliam2102/millet-erp`.
- Línea base técnica congelada: etiqueta `handoff-fase1-v1.3`, commit `d93e1258d694e9ef7f7e665e3ff750d60209b43d`.
- Inventario técnico: 139 de 139 funcionalidades con trazabilidad; la ejecución individual, QA, regresión y UAT permanecen pendientes por fila.
- Plan de validación de heredados: 35 de 35 con responsable y recorrido definido; distribución 23 verificables, 10 parciales y 2 no localizadas.
- Backend: 2,768 pruebas aprobadas en la verificación de handoff.
- Frontend: 1,567 pruebas y compilación de producción aprobadas.
- Clonación limpia, migraciones de 12 contextos, inicio de sesión y arranque local: comprobados.
- Gate automatizado renovado en `56a3215`; la línea base funcional congelada permanece en `handoff-fase1-v1.3` / `d93e125`.
- Acceso individual de Geovany y Uzziel y repetición del arranque por ambos: pendiente.
- Protección obligatoria de `main`: no disponible mientras el repositorio privado permanezca en el plan actual de GitHub. No se hará público para habilitarla.
- Riesgos npm pendientes: 6 vulnerabilidades reportadas por `npm audit` (5 moderadas y 1 alta); no se aplicó corrección forzada.

## Cambios preparados para Notion

### Página principal

1. Conservar `Desarrollo Fase 1 · Base universal de ejecución` como portada única.
2. Sustituir el estado histórico del repositorio por el corte de evidencia anterior.
3. Mantener separados:
   - guía interna de desarrollo y handoff;
   - solicitud externa de insumos a Millet.
4. Agregar acceso visible a:
   - arranque y primer día;
   - mapa del código;
   - Ola 1A;
   - módulos y 139 funcionalidades;
   - dependencias del cliente;
   - integraciones;
   - migración;
   - QA, regresión y UAT;
   - evidencias;
   - escalamiento de bloqueos.

### Ruta por olas

1. `Ola 0` deja de mostrarse como ola funcional independiente. Se convierte en `Puerta técnica de Ola 1A` o se integra en la ficha de 1A después de revisar sus relaciones.
2. `Ola 1A` pasa a `En curso` cuando se publique el corte.
3. Se crea `Ola 1E · Migración final, UAT y salida`, ausente en la base actual.
4. Las olas quedan en este orden:
   - 1A Fundaciones.
   - 1B-1 Abastecimiento e inventario.
   - 1B-2 Pasivo y comercio exterior mínimo.
   - 1C Venta, entrega y cartera.
   - 1D Tesorería y cierre.
   - 1E Migración final, UAT, corte y estabilización.

### Ola 1A

Actualizar las 15 fichas con cuatro campos operativos:

- `Estado de arranque`.
- `Bloqueo de cierre`.
- `Inicio objetivo`.
- `Fin objetivo`.

Resultado preparado:

- 13 pueden comenzar con datos ficticios.
- 1 puede comenzar con sandbox (`F1-ADM-09`).
- 1 requiere decisión interna (`F1-ADM-11`).
- 0 están completamente bloqueadas por Millet.
- 0 están bloqueadas por el repositorio.

Estados propuestos en Notion:

- `Lista para iniciar`: 14 fichas.
- `Bloqueada`: `F1-ADM-11`, indicando expresamente que el bloqueo es una decisión interna sobre módulos y registros cubiertos, no una dependencia del cliente.

La propiedad `Base existente` de `F1-ADM-11` debe pasar de `Ya existe y funciona` a `Existe a medias`, de acuerdo con la auditoría técnica.

### Controles transversales

La base contiene diez frentes `TR-01` a `TR-10` y actividades detalladas `V3-*`. No se deben mostrar como una sola lista plana.

- Vista `Frentes`: sólo `Tipo = Frente`.
- Vista `Actividades ejecutables`: sólo `Tipo = Actividad`.
- Los diez `TR-*` funcionan como agrupadores; las actividades `V3-*` contienen horas, entradas, dependencias y resultado.
- `V3-QA-1A` cambia de 3 h a 8 h.
- `V3-ARCH-01`, `V3-ARCH-02`, `V3-ARCH-03`, `V3-ARCH-06`, `V3-ARCH-07` y `V3-QA-1A` pasan a `En curso`; no pasan a `Con evidencia` mientras falte el arranque independiente de Geovany y Uzziel o algún criterio particular.

### Validación de funcionalidades heredadas

1. Crear bajo `QA, regresión y UAT` la página `Plan de validación · 35 funcionalidades heredadas`.
2. Reutilizar los 35 registros existentes mediante una vista enlazada; no duplicarlos.
3. Agregar a la base de funcionalidades:
   - `Resultado de validación`;
   - `Evidencia QA/UAT`;
   - `Fecha de validación`.
4. Inicializar los 35 resultados en `Pendiente`, sin evidencia ni fecha.
5. Publicar los cinco bloques por ola y el reparto nominal: 17 Geovany y 18 Uzziel.
6. No convertir el gate automatizado en aceptación funcional ni UAT.

### Onboarding técnico

Corregir la ficha existente:

- sustituir el repositorio histórico `Millet-TI/millet_erp` por `Eliam2102/millet-erp`;
- retirar la afirmación de que no existe remoto;
- reemplazar el commit histórico por la línea base que resulte de esta publicación;
- conservar como pendiente el acceso individual y la prueba de primer día de ambos desarrolladores;
- enlazar los documentos operativos de `docs/handoff/`.

## Cambios preparados para ClickUp

Lista destino verificada: `List`, dentro de `Desarrollo e Implementación`, proyecto `VIDRIOS MILLET`.

Estado observado:

- La reunión de planeación está cerrada.
- La tarea del handoff está `en espera`.
- La tarea de carga por olas está `en espera`.
- La tarea para compartir la base existente está `en espera`.
- No se localizaron todavía tareas ejecutables individuales de Ola 1A en esa lista.

### Estructura propuesta

1. Tarea padre `Ola 1A · Fundaciones · 102 h operativas`.
2. Dos tareas padre de módulo:
   - `Administración y usuarios · Ola 1A`.
   - `Contabilidad base · Ola 1A`.
3. Quince subtareas, una por funcionalidad, con:
   - ID y nombre;
   - responsable nominal;
   - revisor cruzado;
   - fechas;
   - horas funcionales de referencia;
   - estado de arranque;
   - bloqueo de cierre;
   - criterio de aceptación;
   - prueba mínima;
   - evidencia requerida;
   - enlace a la ficha de Notion.
4. Tareas transversales separadas para repositorio/arranque, seguridad/configuración, onboarding y QA-1A.
5. Hito `Salida Ola 1A · 7 octubre 2026`, condicionado por evidencia y dependencias, no por consumo de horas.

### Regla de horas

- Funcionalidades: 76 h.
- Trabajo transversal asociado: 18 h.
- QA/regresión de cuatro existentes: 8 h.
- Total operativo: 102 h.
- Las 5 h adicionales de QA salen de la reserva interna; no aumentan las 840 h del alcance total.
- Las cuatro funcionalidades con 0 h de construcción no se crean con estimación cero: su ejecución, evidencia y regresión se controlan dentro de `QA-1A · 8 h`.

## Actualización de las tareas coordinadoras

Después de crear las tareas operativas y verificar sus enlaces:

- `Documento de handoff y onboarding del proyecto en Notion`: mover a `en revisión`, no a completo, hasta que Samuel revise y los dos desarrolladores hagan la prueba de primer día.
- `Cargar tareas en ClickUp por ola con fechas estimadas`: mover a `en progreso`; el entregable mínimo se cumple al publicar y verificar Ola 1A, pero el objetivo de todas las olas continúa pendiente.
- `Definir cómo se comparten al equipo los módulos ya trabajados`: mover a `en progreso`; se cierra cuando Geovany y Uzziel accedan, clonen y reproduzcan un recorrido sin asistencia.

## Autorización requerida

Antes de ejecutar estos cambios externos se debe confirmar:

1. Autorización para modificar Notion y ClickUp conforme a este borrador.
2. Usuarios de GitHub de Geovany y Uzziel.
3. Si Samuel será el revisor final del handoff, como indica la tarea actual de ClickUp.
