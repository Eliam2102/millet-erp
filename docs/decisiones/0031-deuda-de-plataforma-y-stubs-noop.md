# ADR-0031: Tracking de deuda de plataforma y stubs `NoOp`

- **Estado**: Aceptada
- **Fecha**: 2026-05-07
- **Decisores**: Eduardo Paredes
- **Etiquetas**: arquitectura, proceso, deuda-técnica, fundación

## Contexto y problema

Los módulos de negocio del ERP (Compras, Cuentas por Cobrar, Cuentas
por Pagar, Activos Fijos, etc.) se construyen sobre infraestructura
compartida: `CollaborationHub` SignalR (ADR-0012 Capa 2),
`IIntegrationEventPublisher` con Outbox + Service Bus (ADR-0009),
motor de notificaciones (ADR-0026), etc.

Esa infraestructura no estará lista al mismo tiempo que el primer
módulo. Cuando un módulo arranca antes de que cierta pieza compartida
exista, debe usar una implementación temporal — `NoOp`, stub
in-memory, o flag de configuración — para no bloquearse.

El problema: cuando la pieza compartida llega después, hay que
"wirearla" en cada módulo consumidor. Sin una convención explícita:

- Los `NoOp` quedan en producción sin que nadie se entere
- Los engineers de plataforma no saben a qué módulos avisar al cerrar
- La revisión periódica del debt depende de buena memoria
- Cada módulo inventa su propia forma de marcar el debt

Necesitamos un patrón uniforme que haga **visible** la deuda de
plataforma sin imponer overhead operacional.

## Drivers de la decisión

- **Visibilidad**: un dev nuevo debe entender al instante qué piezas
  de plataforma usa un módulo y cuáles están en `NoOp`
- **Trazabilidad**: posibilidad de listar todo el debt del repo en
  cualquier momento, sin pedir favores
- **Coordinación**: el equipo de plataforma sabe a quién avisar
  cuando cierra una pieza
- **Bajo costo**: no agregar herramientas ni procesos nuevos; usar
  docs, comentarios y tickets que ya existen

## Opciones consideradas

1. **Cuatro mecanismos redundantes** (doc del módulo + comentario en
   código + checklist en ticket de plataforma + referencia en CLAUDE.md)
2. Solo comentarios `// TODO` en código
3. Solo doc por módulo
4. Sistema de tickets dedicado a "platform debt" con tooling

## Decisión

Se adopta la **opción 1**. Cuatro mecanismos que actúan en distintos
niveles y se refuerzan entre sí.

### 1. Sección estándar en el diseño del módulo

Cada documento de diseño de módulo
(`docs/modulos/<modulo>/01-diseno.md` y similares) incluye una
sub-sección obligatoria:

> **"Dependencias de plataforma pendientes"**

Con tabla:

```markdown
| Pieza | Ticket | NoOp en uso | Cómo se wirea cuando llegue |
|---|---|---|---|
| CollaborationHub SignalR (ADR-0012 Capa 2) | MIL-XXXX | el módulo declara su entidad en la lista de soft-locks pero el hub no está conectado | quitar el flag `Compras:UseSoftLockNoOp=true` en `appsettings`, registrar `CollaborationHubClient` real en DI |
| Outbox + Service Bus (ADR-0009) | MIL-YYYY | `IIntegrationEventPublisher` registrado como `NoOpIntegrationEventPublisher`; los `INotification` MediatR in-proc funcionan | reemplazar el registro DI por la implementación real conectada a Service Bus, configurar suscripciones de los consumers |
```

Esto hace explícito el debt al **leer el diseño**, no al revisar
tickets.

### 2. Convención de comentarios grepeables en código

Toda implementación temporal lleva un comentario con el formato:

```csharp
// PLATFORM-TODO(<identificador>): reemplazar por <X> cuando <Y> esté
// disponible. Ver ADR-NNNN.
public class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync(...) => Task.CompletedTask;
}
```

Donde:

- `<identificador>` es:
  - Si el proyecto usa sistema de tickets formal (Jira, Linear,
    GitHub Issues, Azure DevOps): el ID nativo del sistema
    (`MIL-1234`, `#42`, `AB#5678`).
  - Si **no** hay sistema de tickets: un **nombre descriptivo entre
    `<>`** (`<CollaborationHub>`, `<Outbox>`,
    `<NotificacionesEmail>`). Debe ser **consistente** entre código,
    tabla del módulo y cualquier ticket interno informal.
- `<X>` es la implementación esperada (`CollaborationHubClient`,
  `ServiceBusIntegrationEventPublisher`, etc.)
- `<Y>` es la condición que hay que esperar (cierre del ticket,
  release del paquete, etc.)
- `ADR-NNNN` es el ADR que decide la pieza, si existe

Una sola convención de prefijo (`PLATFORM-TODO`) permite búsqueda en
todo el repo:

```bash
rg "PLATFORM-TODO" backend/src
```

> **Cuando el proyecto adopte un sistema de tickets formal**, hacer
> un sweep `rg -l "PLATFORM-TODO\(<"` y reemplazar los identificadores
> descriptivos por los IDs del sistema. La cadena `<` (con paréntesis
> izquierdo) garantiza que solo matchea los descriptivos, no los IDs
> ya en formato `MIL-XXXX`.

### 3. Checklist de consumidores en el ticket de plataforma

Cada ticket de plataforma incluye en su descripción un bloque
"Módulos consumidores" que se va llenando conforme se construyen
módulos:

```markdown
## Módulos consumidores
- [ ] Compras / Requisiciones (NoOp activo desde 2026-05-07)
- [ ] Cuentas por Cobrar (pendiente)
```

**El ticket de plataforma no se cierra hasta que todos los módulos en
el checklist están wired.** Quien cierra el ticket es responsable de
coordinar (o ejecutar) los PRs de wireup en cada módulo.

### 4. Referencia en CLAUDE.md

CLAUDE.md tiene una sección breve apuntando a este ADR como fuente de
verdad. Esto asegura que:

- Cualquier dev humano lo encuentre durante onboarding
- Claude Code lo aplique automáticamente al diseñar/implementar
  nuevos módulos

## Consecuencias

**Positivas**

- **Triple visibilidad** del debt: doc del módulo + grep en código +
  checklist en ticket de plataforma. Es muy difícil que pase
  desapercibido en los tres lugares.
- Sin herramientas ni servicios nuevos. Usa solo docs, comentarios y
  tickets.
- El cierre de un ticket de plataforma **fuerza** la revisión de
  consumidores (no se cierra hasta wirearlos).
- Onboarding más rápido: un dev que llega a un módulo encuentra el
  debt explicitado en el doc.
- Patrón replicable a los 10 módulos del back-office.

**Negativas**

- Redundancia: hay tres ubicaciones que mantener sincronizadas (doc,
  código, ticket). Si una queda desactualizada, el debt puede volverse
  invisible parcialmente.
- Disciplina dependiente: si un dev olvida poner el `PLATFORM-TODO`
  en código, el grep no lo encuentra. Mitigación: revisión de PR
  explícita.
- No previene `NoOp` que nunca documenta debt (ej. un singleton mal
  registrado por error). Detectable solo en code review.

## Descartadas

- **Solo comentarios `TODO`**: no diferencia debt de plataforma de
  TODOs operativos. El prefijo `PLATFORM-TODO` es lo mínimo para
  filtrar.
- **Solo doc por módulo**: si la pieza nunca entra al código (ej.
  módulo cancelado), el doc queda con debt fantasma. El triple
  enfoque permite que código y ticket validen lo del doc.
- **Sistema dedicado de "platform debt"**: overkill para un equipo
  pequeño. La redundancia entre doc/código/ticket es suficiente.

## Notas de implementación

### Plantilla del bloque en docs

```markdown
## Dependencias de plataforma pendientes

| Pieza | Ticket | NoOp en uso | Cómo se wirea cuando llegue |
|---|---|---|---|
| <nombre + referencia ADR> | <ID-ticket> | <descripción del NoOp> | <pasos para wirear> |
```

### Plantilla del comentario en código

```csharp
// PLATFORM-TODO(MIL-XXXX): <una línea sobre qué hacer cuando llegue>.
// Ver ADR-NNNN si aplica.
```

Una sola línea cuando alcance, dos máximo. La prosa larga vive en el
ADR / doc del módulo.

### Plantilla del bloque en ticket de plataforma

```markdown
## Módulos consumidores

- [ ] <Módulo 1> (NoOp activo desde <fecha>)
- [ ] <Módulo 2> (pendiente de implementación)
```

### Cierre del debt

Cuando se cierra un ticket de plataforma:

1. Quien cierra abre PRs (o uno solo) por cada módulo en el checklist.
2. Cada PR reemplaza el `NoOp` por la implementación real, **borra el
   comentario `PLATFORM-TODO` correspondiente** y **actualiza la fila
   de la tabla de "Dependencias de plataforma pendientes"** en el doc
   (típicamente: borrar la fila).
3. Marca el item del checklist como completado.
4. Una vez que todos los items están completados, el ticket de
   plataforma se cierra.

### Verificación periódica

Cada mes (o al iniciar un nuevo sprint), correr:

```bash
rg "PLATFORM-TODO" backend/src --no-heading | sort | uniq -c
```

Cruzar el resultado contra los tickets vivos. Tres escenarios:

- `PLATFORM-TODO` en código + ticket abierto → OK, debt vivo
- `PLATFORM-TODO` en código + ticket cerrado → **trabajo pendiente
  de wireup**; abrir hot-fix
- Ticket cerrado + sin `PLATFORM-TODO` correspondiente → OK, debt
  resuelto

## ADRs hijo posibles

- Plantilla de PR que incluya checklist "¿agregaste o quitaste
  `PLATFORM-TODO`s? ¿actualizaste la tabla del módulo?"
- CI check que falle si hay `PLATFORM-TODO` con ticket que ya está
  cerrado (requiere integración con el sistema de tickets)
