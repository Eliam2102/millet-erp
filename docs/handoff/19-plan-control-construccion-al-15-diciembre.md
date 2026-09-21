# Plan de control de construcción al 15 de diciembre de 2026

**Estado:** borrador interno listo para revisión de Eliam.

**Acción externa:** no publicado ni aplicado en ClickUp o Notion.
**Meta del 15 de diciembre:** Fase 1 terminada por desarrollo y lista para
iniciar UAT, no aceptada ni liberada a producción.

## 1. Método de trabajo

Se adopta **Scrumban por flujo, cortes verticales y puertas de evidencia**.

- Scrum puro no resuelve bien las esperas por A+W, PAC, datos y decisiones de
  Millet.
- Kanban sin cadencia no protege el deadline.
- Scrumban permite planear semanalmente, limitar trabajo en curso y mover sólo
  tareas realmente listas, conservando hitos por ola.

Reglas:

1. Planeación y compromiso semanal cada lunes.
2. Una tarea principal de construcción por desarrollador.
3. Una segunda tarea permitida sólo para revisión o desbloqueo.
4. Una rama y WIP PR desde el primer día de trabajo.
5. Ninguna tarea puede permanecer `En desarrollo` sin rama/PR visible.
6. Desarrollo terminado, QA, UAT y producción son estados diferentes.
7. Cada viernes se demuestra un recorrido reproducible y se recalibra la
   capacidad restante al 15 de diciembre.

## 2. Flujo Entra–ERP y responsabilidad compartida

Uzziel entrega autenticación Entra y sesión. Geovany entrega la organización
multiempresa/multisucursal. El punto de integración es:

```text
Entra oid → Usuario ERP → UsuarioEmpresaRol → Empresa ERP → permisos ERP
```

No se usarán roles ni grupos de Microsoft para autorizar funciones. La prueba
conjunta de ADM-01/ADM-02 debe demostrar que una empresa creada por Geovany
puede asignarse dentro del ERP a un usuario autenticado por el flujo de Uzziel.

## 3. Plan de arranque para el 21 de septiembre

### Uzziel · responsable de autenticación e identidad

Objetivo de la jornada:

- publicar rama y WIP PR de ADM-02;
- inventariar lo ya construido y los gaps reales;
- validar `token Entra → oid → Usuario ERP`;
- confirmar que un usuario nuevo queda sin asignaciones;
- definir caso conjunto con ADM-01;
- documentar login, retorno, sesión y logout;
- registrar por separado evidencia fake/local y Entra real.

No debe construir grupos, roles de aplicación ni permisos en Microsoft.

### Geovany · responsable de organización

Objetivo de la jornada:

- publicar rama y WIP PR de ADM-01;
- conservar el modelo multiempresa;
- validar empresa, sucursal, departamento y puesto;
- preparar dos empresas ficticias y su estructura mínima;
- exponer el identificador de empresa consumido por Identidad;
- preparar la prueba de asignación conjunta con Uzziel;
- no trasladar usuarios, roles o permisos al módulo de Administración.

### Entregable conjunto del primer corte

Antes de abrir otro frente deben ejecutar:

1. Crear empresa A y empresa B en el ERP.
2. Autenticar un usuario por `oid`.
3. Asignarle un rol ERP en A y otro en B.
4. Entrar a A y observar sólo permisos/datos de A.
5. Cambiar a B y observar sólo permisos/datos de B.
6. Intentar una empresa no asignada y recibir 403.

## 4. Calendario de capacidad

La base actual supone dos desarrolladores con 32 horas efectivas semanales
cada uno. Es una condición de planeación que debe confirmarse; si no se cumple,
el alcance o el hito deben renegociarse.

| Ventana interna | Objetivo | Salida verificable |
|---|---|---|
| 21 sep–7 oct | Ola 1A: identidad, organización, segregación, maestros y base contable | 15 funciones con PR/evidencia o bloqueo externo nominal |
| 5–23 oct | Ola 1B-1: compra, recepción e inventario | Flujo compra–recepción–existencia reproducible |
| 19 oct–6 nov | Ola 1B-2: pasivo y comercio exterior mínimo | Recepción–pasivo–costo puesto en almacén conciliados |
| 2–27 nov | Ola 1C: producto terminado, entrega, CFDI y cartera | Pieza–entrega–CFDI–cartera con trazabilidad |
| 16 nov–11 dic | Ola 1D: tesorería, contabilidad y cierre | Pago/cobro–póliza–conciliación–cierre técnico |
| 12 oct–11 dic | Carril continuo de integración y migración | Contratos, reintentos, mapeos y primer ensayo conciliado |
| 14–15 dic | Gate de estabilización | Build, pruebas, ambiente y paquete listos para iniciar UAT |

Las ventanas se solapan deliberadamente. El segundo desarrollador sólo toma
trabajo de la ola siguiente cuando su contrato de entrada está estable y no
rompe el límite de WIP.

## 5. Estructura exacta propuesta para ClickUp

### Jerarquía

```text
Fase 1 · Meta 15-dic · Lista para UAT
├── Ola 1A · Fundaciones
├── Ola 1B-1 · Compra, recepción e inventario
├── Ola 1B-2 · Pasivo y ComEx mínimo
├── Ola 1C · Venta, entrega y cartera
├── Ola 1D · Tesorería y cierre
└── Carril transversal · Integración, migración y QA
```

Dentro de cada ola:

- una tarea por función `F1-*`;
- subtareas por corte vertical/PR sólo cuando la función exceda tres días;
- una tarea de QA/regresión;
- un hito de salida por evidencia.

### Estados

1. `Backlog`.
2. `Ready`.
3. `En desarrollo`.
4. `En revisión`.
5. `QA técnico`.
6. `Listo para UAT`.
7. `Aceptado`.
8. `Bloqueado interno`.
9. `Bloqueado por Millet`.

No usar `Completo` para representar simultáneamente desarrollo, aceptación y
producción.

### Campos personalizados obligatorios

| Campo | Uso |
|---|---|
| ID matriz | `F1-ADM-02`, `F1-COM-*`, etc. |
| Ola | 1A, 1B-1, 1B-2, 1C, 1D o transversal |
| Tipo de trabajo | Construcción, configuración, integración, migración, QA o UAT |
| Responsable técnico | Una sola persona |
| Revisor | El otro desarrollador |
| Dueño funcional Millet | Persona/área que acepta; `Por confirmar` cuando falte |
| Estimación base | Horas del inventario; nunca sustituirlas por horas reales |
| Horas reales | Tiempo registrado por ejecución |
| Dependencia | Interna, Millet, A+W, PAC, SAP, datos o ninguna |
| Criterio de entrada | Condición para mover a `Ready` |
| Criterio de aceptación | Resultado observable |
| PR/rama | Enlace obligatorio desde `En desarrollo` |
| Evidencia QA | Prueba, captura/log y recorrido |
| Estado externo | No requerido, pendiente, recibido, incompleto o validado |

### Plantilla de descripción

```text
Objetivo:
Alcance incluido:
Fuera de alcance:
Criterio de entrada:
Criterio de aceptación:
Prueba nominal:
Prueba negativa:
Regresión requerida:
Dependencias y dueño:
Evidencia de cierre:
```

### Vistas para Eliam

1. `Esta semana`: sólo Ready/En desarrollo/En revisión/QA.
2. `Bloqueos`: agrupada por dueño de dependencia y antigüedad.
3. `Deadline 15-dic`: progreso por horas base aceptadas técnicamente, no por
   número bruto de tareas.
4. `Carga por desarrollador`: estimación y WIP de Uzziel/Geovany.
5. `Sin evidencia`: tareas en revisión o QA sin PR/prueba.
6. `Millet debe responder`: dependencias externas con responsable y fecha
   requerida.

## 6. Cadencia de liderazgo

### Lunes · compromiso semanal, 30 minutos

- elegir una tarea principal por desarrollador;
- revisar Definition of Ready;
- confirmar capacidad disponible;
- declarar dependencias y responsables;
- no comprometer más trabajo del que cabe esa semana.

### Diario · control de flujo, 15 minutos

Cada persona responde con evidencia:

1. Qué quedó integrado o visible ayer.
2. Qué corte exacto terminará hoy.
3. Qué bloqueo existe, desde cuándo y quién lo resuelve.

### Martes y jueves · revisión cruzada

Cada desarrollador reserva una ventana para revisar el PR del otro. Un PR sin
revisión durante más de un día hábil se vuelve bloqueo de equipo.

### Viernes · demo y control del deadline, 45 minutos

- demo reproducible;
- revisar estimado, real y trabajo restante;
- comprobar evidencia;
- recalcular capacidad hasta el 15 de diciembre;
- escalar desviación en la semana en que aparece, no al final de la ola.

## 7. Indicadores mínimos

- Funciones terminadas técnicamente / total de Fase 1.
- Horas base terminadas / horas base restantes.
- Días de envejecimiento por tarea activa.
- Tiempo medio de PR abierto a integrado.
- Bloqueos abiertos y horas/días bloqueados.
- Defectos reabiertos.
- Funciones sin evidencia QA.
- Capacidad requerida restante / capacidad disponible al 15 de diciembre.

Semáforo semanal:

- Verde: capacidad requerida ≤ 90 % de capacidad disponible.
- Amarillo: 91–100 % o un bloqueo crítico mayor a dos días.
- Rojo: trabajo requerido supera la capacidad o un contrato crítico externo no
  tiene fecha de entrega.

## 8. Reglas de cierre

Una función sólo queda `Lista para UAT` si tiene:

- PR revisado e integrado;
- build, lint y pruebas aplicables en verde;
- migración/configuración documentada;
- recorrido reproducible;
- prueba nominal, negativa y regresión;
- evidencia vinculada;
- dependencia externa resuelta o variante real explícitamente bloqueada.

UAT, aceptación de Millet, liberación y producción siguen siendo puertas
posteriores.

## 9. Autorización antes de publicar

Antes de cambiar ClickUp se requiere aprobación explícita de Eliam sobre:

1. reasignar ADM-02 a Uzziel y ADM-01 a Geovany;
2. usar el flujo de estados propuesto;
3. crear o actualizar los campos personalizados;
4. cargar las ventanas internas de trabajo;
5. sustituir el manifiesto anterior donde asignaba ADM-02 a Geovany;
6. conservar el 15 de diciembre como `Listo para UAT`.
