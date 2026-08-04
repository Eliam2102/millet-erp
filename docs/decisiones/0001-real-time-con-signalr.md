# ADR-0001: Real-time con SignalR + Azure SignalR Service

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: real-time, infraestructura, fundación

## Contexto y problema

El ERP requiere actualizar la interfaz de los usuarios cuando ocurren eventos
relevantes en otros lugares del sistema: autorización de una requisición,
timbrado exitoso de un CFDI, aplicación de un pago, llegada de un complemento
de pago, etc. Forzar al usuario a refrescar manualmente o ejecutar polling cada
N segundos es inaceptable por experiencia de usuario y por costo en consumo de
API.

Se necesita una infraestructura de push del servidor al navegador, con bajo
overhead, autenticación integrada, y capacidad de escalar a múltiples instancias
del backend sin perder mensajes.

## Drivers de la decisión

- Integración nativa con .NET 9 y con la autenticación de Entra ID
- Capacidad de scale-out sin operar infraestructura adicional (Redis backplane manual, etc.)
- SDK cliente maduro para React/TypeScript
- Costo operacional bajo (no agregar contenedores que mantener)

## Opciones consideradas

1. SignalR + Azure SignalR Service
2. Soketi self-hosted (servidor compatible con Pusher)
3. Azure Web PubSub
4. Polling AJAX cada N segundos

## Decisión

Se adopta **SignalR como librería del backend con Azure SignalR Service como
backplane gestionado**. El frontend consume la conexión usando
`@microsoft/signalr`.

Cada módulo expondrá uno o más Hubs según necesidad (ej. `FacturacionHub`,
`CobranzaHub`).

Los Hubs aceptan el mismo bearer token del API que los endpoints REST. Ese
token es emitido por el backend al hacer login (ver ADR-0007), no es el token
original de Entra ID. La validación sucede en `OnConnectedAsync` usando la
misma configuración de JWT que el resto del API.

## Consecuencias

**Positivas**
- Idiomático de .NET: hubs como clases, inyección de dependencias, integración con `[Authorize]`
- Azure SignalR Service maneja scale-out: el backend puede tener múltiples instancias sin Redis backplane manual
- Cliente oficial de Microsoft para TypeScript con tipos
- Autenticación uniforme: el cliente se conecta con el mismo JWT del API que usa para REST

**Negativas**
- Dependencia adicional de Azure (no portable trivialmente fuera de Azure)
- Costo del SKU de Azure SignalR Service (Standard S1 ~ 50 USD/mes, 1000 conexiones concurrentes; rara vez es cuello de botella en ERP back-office)
- WebSockets puede ser bloqueado por algunos firewalls corporativos (mitigado por fallback automático de SignalR a Server-Sent Events o long polling)

## Descartadas

**Soketi self-hosted**. Aunque es familiar al equipo desde proyectos PHP, en
el stack .NET implica operar un contenedor adicional con su propia
infraestructura, monitoreo y parches; usar el SDK de Pusher en .NET (de tercero,
no oficial) sin integración con autenticación/autorización del backend; y
sincronizar eventos de dominio con el servidor de notificaciones manualmente.
El costo operacional supera el ahorro en licencia.

**Azure Web PubSub**. Capacidades similares pero más bajo nivel, sin las
abstracciones de Hubs. Más útil cuando hay clientes en lenguajes diversos; aquí
el cliente es solo React.

**Polling AJAX**. Descartado por consumo innecesario de API, latencia
inherente, y mala experiencia de usuario.

## Notas de implementación

- Crear módulo Bicep `infra/modules/signalr.bicep` (pendiente)
- **Región del recurso**: Azure SignalR Service NO está disponible en `mexicocentral`. Se hostea en `southcentralus` (Texas, ~50 ms desde MX), la región soportada más cercana. Esta excepción a la regla de "todo en México Central" se justifica porque SignalR Service es un relay sin persistencia de datos del cliente: la metadata de conexiones es transitoria en memoria del servicio y los mensajes solo pasan a través. Los datos persistentes del ERP (PostgreSQL, audit_log, Key Vault, Storage) siguen en `mexicocentral`. El módulo Bicep recibe un parámetro `signalrLocation` separado del `location` global para reflejarlo
- Modo de servicio: `Default` (no Serverless al inicio; usaremos integración con la app .NET)
- Autenticación: validar JWT en `OnConnectedAsync`; usar claims para autorización de grupos/usuarios
- Cliente React: instalar `@microsoft/signalr`, crear hook `useSignalR(hubName)` con reconexión automática
- Documentar convenciones de nombrado de Hubs y eventos en `CLAUDE.md`
