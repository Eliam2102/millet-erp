# Arquitectura de Millet ERP

Este documento es para el dev nuevo que se incorpora al proyecto. Sintetiza
las quince decisiones arquitectónicas que ya fueron tomadas, no como índice
sino como una historia coherente que puedes leer en quince o veinte minutos.
Si después necesitas profundidad sobre alguna decisión particular, cada
sección enlaza al ADR original donde están el contexto completo, las
opciones descartadas y las consecuencias asumidas.

## 1. Qué estamos construyendo

Millet es una empresa mexicana de vidrio de valor agregado. Tiene varias
razones sociales operando bajo el mismo grupo y procesos administrativos
compartidos. Hoy opera con SAP en su back-office. El objetivo de este
sistema es **reemplazar progresivamente** ese SAP, módulo por módulo, sin
un corte total — es la estrategia conocida como Strangler Fig: el sistema
viejo y el nuevo conviven, y cada cierto tiempo migramos una funcionalidad.
La parte comercial y de producción la sigue manejando un sistema externo
llamado A+W; este ERP no la reemplaza, solo la consume.

Eso define dos características fundamentales del sistema. La primera es
que es un **back-office multi-empresa**: cada razón social del grupo opera
en él como un tenant lógico, comparte usuarios y catálogos pero mantiene
sus propios datos transaccionales. La segunda es que **integra con
sistemas externos** en ambas direcciones: A+W le envía pedidos y
producción, requisiciones externas alimentan compras, y el SAT recibe los
CFDIs vía un PAC. La arquitectura está pensada para que esas integraciones
sean ciudadanos de primera clase, no añadidos.

## 2. Stack y forma del sistema

El backend está escrito en **.NET 9** y se despliega como un **monolito
modular**. Una sola aplicación contiene varios módulos con fronteras
estrictas; dentro de cada módulo aplicamos arquitectura **hexagonal con
CQRS**, usando MediatR para los casos de uso. El frontend es una SPA en
**React 18 + TypeScript + Vite**, con sistema de diseño basado en
**shadcn/ui**. La base de datos es **PostgreSQL 16** (Flexible Server en
Azure). La mensajería asíncrona entre módulos pasa por **Azure Service
Bus**; las notificaciones en tiempo real al navegador, por **Azure SignalR
Service**. La identidad la maneja **Microsoft Entra ID** (no hay Active
Directory local). Todo vive en **Azure México Central** y se despliega
con **Bicep**.

Los módulos siguen los nombres del esquema de base de datos: `identidad`
(transversal), `aw` (integración A+W), `comercial`, `fiscal`, `financiero`,
`compras`, `almacen`, `activos`, `contabilidad`, más dos esquemas de
servicio que no son módulos de negocio: `compartido` (catálogos
cross-empresa) y `core` (tablas transversales del sistema como audit log
y locks).

Las claves arquitectónicas a internalizar de entrada son tres: (a) los
módulos no se hablan por SQL ni por interfaces de servicio in-process —
se comunican únicamente por **eventos asíncronos** publicados al Service
Bus; (b) el monolito se despliega como una sola unidad pero está pensado
para que cualquier módulo pudiera extraerse a su propia aplicación si en
el futuro se justifica; (c) la mayor parte de la complejidad transversal
(multi-empresa, auditoría, concurrencia, dinero, tiempo, autorización) se
resuelve **una sola vez en el `BaseDbContext` o en un componente
compartido**, no en cada handler.

## 3. La base de datos: una sola, muchos esquemas

PostgreSQL único con un esquema por módulo (ver
[ADR-0005](./decisiones/0005-migraciones-ef-core-esquema-por-modulo.md)).
Cada módulo tiene su propio `DbContext` apuntando a su esquema, sus
propias migraciones EF Core, y la garantía de que **no puede leer las
tablas de otro módulo accidentalmente** porque el filtro físico de esquema
lo impide. Los joins SQL cross-schema están prohibidos por convención y
por code review; cualquier consulta que necesite datos de otro módulo va
por interfaces de dominio bien definidas o se hace en el módulo de
Reportería (que tiene permiso de lectura sobre todos los esquemas y no
emite cambios).

A los esquemas de módulo se suman dos transversales. **`compartido`**
alberga catálogos cross-empresa: la tabla `empresas` que lista las RFCs
del grupo, los catálogos del SAT (regímenes fiscales, productos, monedas),
los tipos de cambio del DOF, los códigos postales. **`core`** alberga
tablas operativas del sistema mismo: `audit_log` central (ver
[ADR-0008](./decisiones/0008-estrategia-auditoria.md)) y `locks_duros`
(ver [ADR-0012](./decisiones/0012-concurrencia-hibrida.md)).

Las migraciones se generan desde el modelo C# con `dotnet ef migrations
add`. En desarrollo se aplican automáticamente en arranque. En QA y
producción **nunca** se aplican en arranque: corren como un job dedicado
en GitHub Actions antes del despliegue de la aplicación, lo que evita que
un arranque concurrente intente migrar dos veces. Las convenciones de
naming (snake_case en BD, GUIDs v7 como PK, columnas automáticas
`created_at`/`updated_at`/`created_by`/`updated_by`) viven en el
`BaseDbContext` heredado por todos los DbContext del monolito; el dev no
las escribe en cada entidad.

## 4. Multi-empresa: aislamiento lógico por `empresa_id`

Como el grupo Millet opera con varias RFCs que comparten infraestructura
y equipo, el sistema soporta multi-empresa con **aislamiento lógico** y no
físico (ver [ADR-0011](./decisiones/0011-multi-empresa-empresa-id.md)).
Una sola base de datos, una sola instancia del monolito, una sola
conexión, un solo backup. Los datos de cada empresa se separan con una
columna `empresa_id` que viaja en cada entidad de negocio y que EF Core
filtra automáticamente.

La mecánica es: toda entidad de dominio que tenga datos pertenecientes a
una empresa específica implementa la interfaz marker `IPerteneceAEmpresa`.
El `BaseDbContext` aplica un global query filter automático por
`empresa_id`: cualquier `SELECT` filtra implícitamente por la empresa
actual del usuario. Un `SaveChangesInterceptor` asigna `empresa_id`
automáticamente en `INSERT` desde el contexto del request — el dev no lo
escribe — y valida que ningún `INSERT/UPDATE` referencie (vía FK) a un
registro de otra empresa. Si alguien lo intenta, se lanza
`CrossTenantViolationException` que se mapea a HTTP 403.

La empresa actual viaja en el JWT del API como claim `current_empresa_id`.
Si un usuario tiene acceso a una sola empresa, se asigna automáticamente
al login. Si tiene acceso a varias, la UI muestra un selector de empresa
en el header global y obliga a elegir antes de proceder. Cambiar de
empresa no re-autentica al usuario contra Entra: llama al endpoint
`POST /api/auth/cambiar-empresa`, que valida que tenga roles en la
empresa destino y re-emite el JWT con el nuevo `current_empresa_id`. La
preferencia de "última empresa usada" se guarda en
`identidad.usuario_preferencias` para que el siguiente login default a
ella.

Un detalle a tener presente: la tabla que liga usuarios con roles se llama
**`usuario_empresa_roles`** (no `usuario_roles`), con clave compuesta
`(usuario_id, empresa_id, rol_id)`. Eso permite que María García sea
`Cobrador` en Empresa A y `Auditor` en Empresa B con cero ambigüedad. Los
catálogos globales (catálogo SAT, tipos de cambio del DOF, monedas,
códigos postales, las propias `usuarios`/`roles`/`permisos`/`empresas`)
viven en `compartido` o `identidad` y no llevan `empresa_id`.

## 5. La forma de las entidades: BaseEntity y convenciones transversales

Toda entidad de dominio hereda de `BaseEntity`, una clase abstracta que
estandariza lo que toda fila debe tener: `Id` (Guid v7, ordenable
temporalmente), `Version` (entero usado como concurrency token, ver
siguiente sección), `CreatedAt`/`UpdatedAt` (`DateTimeOffset`, siempre
UTC en BD), `CreatedBy`/`UpdatedBy` (resueltos automáticamente desde el
contexto de autenticación). Las convenciones de naming snake_case se
aplican vía interceptor en el `BaseDbContext`. Esto significa que crear
una entidad nueva es escribir un puñado de propiedades específicas del
dominio; lo transversal viene gratis.

Sobre `BaseEntity` se aplican varias **interfaces marker** que activan
comportamientos opt-in. `IPerteneceAEmpresa` activa el filtro de empresa
descrito arriba y exige la columna `empresa_id`. `IFiscalmenteRelevante`
activa **soft delete obligatorio**: la entidad nunca se borra físicamente,
solo se marca `deleted_at`; los queries filtran `WHERE deleted_at IS NULL`
automáticamente. Esto cubre CFDIs, asientos contables, pagos, notas de
crédito — cualquier cosa con implicaciones fiscales. `IAuditable` activa
el registro automático en `core.audit_log` (cubierto en la sección de
observabilidad). Y `INotAudited` es la antagonista explícita: marca
entidades operativas (outbox, eventos procesados, caches temporales) como
**conscientemente no auditadas**, para que el linter no se queje.

El detalle clave de implementación: `IAuditable` y `INotAudited` no son
opcionales. Hay un **test/linter en CI** que recorre todas las clases que
extienden `BaseEntity` y falla el build si una entidad nueva no declara
explícitamente cuál de las dos quiere. El dev está obligado a tomar una
decisión consciente; nunca por descuido se cae a un default. La misma
filosofía aplica al filtro de empresa: una entidad que no implementa
`IPerteneceAEmpresa` se considera global y se documenta en code review.

## 6. Tipos del dominio que importan: dinero y tiempo

En un ERP, dinero y tiempo no son tipos primitivos: cada uno arrastra
reglas que si se ignoran producen bugs sutiles que aparecen meses después
en un cierre contable o en una factura mal calculada. Por eso ambos son
**value objects de dominio** con disciplina obligatoria, no `decimal` y
`DateTime` sueltos.

`Money` (ver
[ADR-0014](./decisiones/0014-money-multimoneda-tipos-de-cambio.md)) es un
`readonly record struct` con dos campos: `decimal Amount` y `string
Currency`. Es inmutable: cada operación retorna un nuevo `Money`. Sumar o
restar dos `Money` con divisas distintas lanza
`IncompatibleCurrenciesException` — el compilador y los tests detectan
errores que serían invisibles con `decimal` suelto. La conversión entre
divisas es **explícita**: requiere pasar el tipo de cambio. No hay magia
que convierta USD a MXN sin que el código lo pida. En BD los montos se
almacenan como `decimal(18,4)` y los tipos de cambio como `decimal(18,6)`;
los cuatro decimales en montos son necesarios para precios unitarios
fraccionados (un kilo a $1,250.7350) y para evitar acumulación de error
en sumas largas. El redondeo default es banker's
(`MidpointRounding.ToEven`) para minimizar sesgo en sumas grandes; el
módulo Fiscal aplica redondeo comercial (`AwayFromZero`) en los campos
calculados del CFDI 4.0 que el SAT exige así, pero ese cambio es local y
explícito.

Para multimoneda, el sistema reconoce **cinco fuentes de tipo de cambio**
con metadata explícita en cada documento que las usa: `DOF` (Banxico,
oficial y default), `PACTADO` (acordado con un cliente o proveedor a
nivel de la relación, configurado en `clientes_tipos_cambio` o
`proveedores_tipos_cambio` con vigencias), `MANUAL` (capturado o
modificado por el usuario, marcado automáticamente cuando se altera el
valor prefiliado), `BANCO` (TC efectivo aplicado al cobrar o pagar,
captado en complementos de pago), y `AJUSTE` (usado en revaluación
contable de cierre). Un job nocturno consulta la API de Banxico y poblá
`compartido.tipos_de_cambio_dof` con el TC del día; ese histórico se
conserva indefinidamente porque es referencia fiscal. Cuando un cobro
llega con un TC distinto al del documento original, la **diferencia
cambiaria** se asienta automáticamente como ganancia o pérdida en una
cuenta contable parametrizable.

Para tiempo (ver
[ADR-0013](./decisiones/0013-tiempo-zona-horaria.md)) la convención es
estricta: **UTC en BD, `DateTimeOffset` en código, conversión a
`America/Mexico_City` en el frontend**. Las columnas temporales son
`timestamptz`. En código de dominio está prohibido `DateTime.Now`,
`DateTime.UtcNow` y `DateTimeOffset.Now`; es obligatorio inyectar `IClock`
(interfaz con `DateTimeOffset UtcNow { get; }`) para que los tests sean
deterministas. El frontend nunca muestra UTC al usuario — para eso
existen `<DateTimeDisplay>`, `<DateDisplay>` y `<DatePicker>` que aplican
la conversión automática. Los rangos contables ("movimientos del día 30
de abril", "cierre de mayo") tienen una trampa que vale memorizar: si uno
hace `WHERE DATE(timestamp) = '2026-04-30'` interpreta UTC y obtiene un
día equivocado para usuarios en hora local de México. Por eso existe la
clase helper `FechaContable` con métodos como `GetDayBoundsUtc(localDate)`
y `GetMonthBoundsUtc(year, month)` que convierten el rango local al UTC
equivalente. Toda query temporal de cortes contables debe usarlos. (México
eliminó el horario de verano en 2022, así que para fechas posteriores la
conversión es trivial; para fechas históricas anteriores el `tzdata`
resuelve correctamente la regla de DST de su época.)

## 7. Concurrencia: tres capas, una decisión por entidad

Cuando dos usuarios editan el mismo registro al mismo tiempo, sin
protección el último en guardar gana silenciosamente y los cambios del
primero se pierden. En un ERP financiero esto es inaceptable. El sistema
aplica una estrategia híbrida en **tres capas** (ver
[ADR-0012](./decisiones/0012-concurrencia-hibrida.md)), elegida según el
costo del conflicto en cada tipo de entidad.

La **capa 1 es optimismo en BD y es no-negociable**: toda entidad de
dominio hereda `Version` de `BaseEntity`, configurada como
`IsConcurrencyToken` en el `BaseDbContext`. EF Core la incrementa en cada
`SaveChanges`; si dos transacciones intentan actualizar la misma fila con
la misma version, la segunda recibe `DbUpdateConcurrencyException` que el
middleware traduce a HTTP 409 con Problem Details. Esta capa protege
contra **todos** los caminos de actualización: UI, jobs en background,
integraciones externas, scripts de mantenimiento. Es imposible perder
cambios silenciosamente.

La **capa 2 son soft locks vía SignalR** y aplica a la mayoría de las
entidades editables (clientes, proveedores, cotizaciones, pedidos,
requisiciones, etc.). Cuando un usuario abre el detalle de un registro,
el cliente notifica al hub `CollaborationHub`. El hub mantiene en memoria
un mapa `(empresa, entidad, id) → [usuarios presentes]`. Si otro usuario
abre el mismo registro, ambos reciben "María García también tiene este
registro abierto". Si uno empieza a teclear sin guardar, su estado pasa a
`editing` y los demás ven "María García está editando". Heartbeats cada
30 segundos mantienen el estado vivo; sin heartbeat por 90 segundos el
lock se libera. El estado **no se persiste**: vive en memoria del hub, y
si la app reinicia los soft locks se pierden hasta que los clientes
reconecten. Eso es aceptable por diseño — no protege contra nada en BD,
solo informa la UX.

La **capa 3 son hard locks** y aplica a una **whitelist mínima** de
entidades donde un conflicto sería catastrófico: CFDI en estado
`Borrador` (antes de timbrar; una vez timbrado el CFDI es inmutable y no
necesita lock), póliza contable en captura (antes de posteo), cierre
contable mensual o anual (mientras el job corre, nadie puede editar el
periodo). La mecánica usa `core.locks_duros` con `INSERT ... ON CONFLICT
DO NOTHING` y TTL de 15 minutos, heartbeats cada 60 segundos para
extender, liberación al guardar, y un job de limpieza cada minuto. La
whitelist es deliberadamente corta porque cada hard lock añade
complejidad operacional real (timeouts, locks huérfanos que ocasionalmente
un admin debe forzar, manejo de desconexiones).

Cuando ocurre un conflicto optimista, el frontend no muestra un error 409
crudo: presenta un componente reutilizable `<ConflictResolutionDialog>`
que muestra los cambios propios pendientes, los cambios remotos cargados
en vivo, y opciones para descartar, sobreescribir (solo con permiso
`core.conflicto.sobrescribir`), o fusionar campo a campo cuando los
cambios no se solapan.

## 8. Comunicación entre módulos: outbox pattern

Los módulos del ERP se comunican entre sí únicamente por **eventos de
integración** publicados a Azure Service Bus (ver
[ADR-0009](./decisiones/0009-outbox-pattern-eventos-integracion.md)).
Fiscal publica `CfdiTimbradoEvent` cuando timbra un CFDI; Financiero lo
consume y crea el documento por cobrar. Financiero publica
`PagoAplicadoEvent`; Contabilidad lo consume y asienta la póliza. La
razón de evitar comunicación in-process directa es la frontera modular:
si un módulo importara clases de otro o hiciera joins SQL cross-schema,
el aislamiento se rompería en silencio.

El problema clásico cuando se publica a una cola es: ¿cómo garantizar que
el cambio en BD y la publicación del evento sean consistentes? Si
escribes en BD y luego falla la publicación, queda un cambio sin evento.
Si publicas primero y luego falla la BD, queda un evento fantasma. La
solución es el **Outbox pattern**: cada módulo emisor tiene una tabla
`integration_events_outbox` en su esquema. El handler de un comando,
después de modificar las entidades de dominio y **antes** de
`SaveChanges`, agrega los eventos pendientes a esa tabla. EF Core
ejecuta una sola transacción que incluye entidades + outbox. Después un
**worker publisher** lee el outbox cada N segundos, publica a Service
Bus, marca `published_at`. Si Service Bus está caído, los eventos
esperan en outbox y se publican cuando se recupere. Si un evento falla
10 veces, se marca como tóxico y se notifica vía Application Insights
para revisión manual.

Del lado del consumidor, cada módulo tiene una tabla
`integration_events_processed`. Cuando llega un evento, antes de
procesarlo el consumidor verifica que `event_id` no exista en esa tabla;
si ya existe, descarta y ACK. Esto hace al consumidor **idempotente** sin
que el handler tenga que pensar en duplicados. Service Bus puede entregar
el mismo mensaje dos veces (especialmente bajo retries); la tabla
`processed` lo absorbe.

Sobre el despliegue de los workers vale aclarar la fase actual: **todos
los hosted services corren dentro del mismo proceso de la API web** (un
App Service único). El `OutboxPublisherWorker`, el `OutboxCleanupJob`, el
`LockCleanupJob`, el `DofTipoCambioFetcherJob`, el `AuditArchiveJob` —
todos viven en el mismo binario. Cuando se escala horizontalmente a N
instancias, cada instancia corre los workers en paralelo. Para los
workers de polling como el publisher de outbox, esto es seguro gracias a
`SELECT ... FOR UPDATE SKIP LOCKED` que se aplica al batch (cada
instancia toma un subconjunto disjunto). Para jobs nocturnos singleton
(cleanup, archival, fetch del DOF) se adquiere un **advisory lock** en
PostgreSQL al iniciar (`pg_try_advisory_lock`): solo la instancia que
obtiene el lock ejecuta, las demás duermen. Si en el futuro algún worker
crece a procesar volúmenes pesados, se extrae mecánicamente a Container
Apps Jobs o Azure Functions con Timer Trigger; el código `IHostedService`
permite la extracción sin reescribir.

Las convenciones de eventos: cada uno es un `record` en
`Backend/src/{Modulo}/Application/IntegrationEvents/`, naming
`{Sustantivo}{Verbo}Event` en pasado (`CfdiTimbradoEvent`,
`PagoAplicadoEvent`), todos heredan `IntegrationEventBase` con `EventId`,
`EmpresaId`, `OcurridoEn` (`DateTimeOffset` UTC) y `CorrelationId`. Los
eventos van versionados; cuando un evento evoluciona, los consumidores
manejan múltiples versiones explícitamente.

## 9. Identidad y autorización

La autenticación del sistema usa **Microsoft Entra ID con OIDC + PKCE**
(ver [ADR-0003](./decisiones/0003-autenticacion-entra-id.md)). Frontend
usa `@azure/msal-browser` y `@azure/msal-react`; backend valida JWTs con
`Microsoft.Identity.Web`. Los usuarios viven en Entra (no en la BD del
ERP), y aprovechamos el SSO con M365 que ya usan los empleados, las
políticas de MFA y acceso condicional administradas por TI corporativo, y
el lifecycle de altas y bajas en un solo lugar.

Pero la autenticación responde "quién es el usuario", no "qué puede
hacer". La autorización es **RBAC con permisos atómicos** (ver
[ADR-0007](./decisiones/0007-autorizacion-rbac-granular.md)) modelado
así: un **permiso** es una cadena con formato `{modulo}.{recurso}.{accion}`
(`fiscal.cfdi.timbrar`, `cobranza.pago.aplicar`,
`comercial.cotizacion.autorizar`); un **rol** agrupa permisos con un
nombre simbólico (`Facturador`, `Cobrador`, `SupervisorCobranza`); un
usuario tiene roles distintos por empresa vía la tabla
`usuario_empresa_roles`.

Hay un detalle de implementación que vale entender desde el inicio
porque atraviesa todo el sistema: **el JWT que la aplicación valida no
es directamente el de Entra ID**. El flujo es: el frontend autentica al
usuario contra Entra y obtiene el token de Microsoft. Ese token se envía
al endpoint `POST /api/auth/sesion` del backend, que lo valida contra
los metadatos públicos del tenant Millet, resuelve el usuario en
`identidad.usuarios`, calcula sus empresas accesibles, y emite **un JWT
propio** firmado con clave del backend con shape compacto: `{ sub:
userId, current_empresa_id, roleIds[], exp, iss: "millet-erp-api", aud:
"millet-erp-api" }`. Ese JWT del API es el que se usa para todas las
requests subsecuentes a REST y a SignalR. Cambiar de empresa llama a
`POST /api/auth/cambiar-empresa` que re-emite el JWT con el nuevo
`current_empresa_id` validando que el usuario tenga roles ahí.

Los permisos efectivos no se incluyen en el JWT (se inflaría); se
resuelven server-side por request desde un cache in-memory con clave
`(userId, empresaId)` y TTL de 5 minutos. Cuando un admin modifica los
roles de un usuario, se invalida explícitamente la entrada del cache de
los pares afectados — los cambios surten efecto en menos de cinco minutos
sin necesidad de re-login. Hay una abstracción `IPermissionCache` que
oculta la implementación; si en el futuro se escala a múltiples
instancias y se necesita coherencia inmediata, se migra a Redis sin tocar
handlers.

Sobre el bootstrap inicial: la **auto-provisión** crea automáticamente un
registro en `usuarios` cuando un usuario se autentica con Entra por
primera vez, pero **sin acceso a ninguna empresa** (cero filas en
`usuario_empresa_roles`). El usuario ve un mensaje "no tienes acceso,
contacta al administrador" hasta que alguien le asigne un rol. Esto evita
el escenario operativo de "el director no puede entrar el día 1 porque
TI no lo dio de alta a tiempo". Y para el primer admin del sistema
existe la variable `Auth:InitialAdminEntraOid` (en Key Vault por
ambiente) que en el arranque de la app, si no existe ningún `SuperAdmin`,
asigna ese rol al `oid` configurado.

La **segregación de funciones** estática (ej. "el `Facturador` y el
`AplicadorDePagos` son mutuamente excluyentes para el mismo usuario") se
modela en una tabla `restricciones_roles` y se valida automáticamente al
asignar roles. La SoD dinámica ("el aplicador de un pago no puede ser el
creador de la factura") se implementa caso por caso en los handlers que
la requieran usando un helper `ISegregationChecker`. Workflows de
aprobación más complejos quedan fuera de esta ADR — son ruteo de tareas,
no autorización.

Hay además un sub-sistema importante para desarrollo local. ADR-0003
establece Entra ID para todo, pero en una laptop de dev hacer login con
Entra contra el tenant productivo de Millet por cada reload del frontend
es insufrible (clic, password, MFA, esperar redirect; treinta veces al
día). Por eso existe un **modo `FakeForLocalDev`** (ver
[ADR-0015](./decisiones/0015-local-dev-auth.md)) que valida JWTs firmados
localmente con clave simétrica de desarrollo y expone un endpoint
`POST /api/dev/fake-login` con seis usuarios seed deterministas
(`dev-superadmin`, `dev-cobrador`, `dev-facturador`, `dev-multiempresa`,
etc.). El frontend muestra un componente `<DevUserSelector />` que
reemplaza el botón "Iniciar sesión con Microsoft" en el ambiente de dev:
clic → JWT del API → sesión iniciada en dos segundos. Las protecciones
para que este modo **jamás** llegue a un ambiente real son varias y
redundantes: el código del endpoint y la validación simétrica están
envueltos en `#if DEBUG` (no existen en builds Release); validación en
arranque que falla la aplicación si `Auth:Mode = "FakeForLocalDev"` y el
ambiente no es Development; test de CI que compila en Release y verifica
que `/api/dev/fake-login` retorna 404; inyección de `Auth:Mode` desde
Key Vault en QA y Prod hardcoded a `EntraId`; en el frontend,
`<DevUserSelector />` está bajo `if (import.meta.env.DEV)` que el
bundler elimina del bundle de producción.

## 10. Observabilidad: ver qué pasó, en orden

Un monolito modular con eventos asíncronos tiene una propiedad incómoda:
una sola operación de negocio puede atravesar varios módulos y workers.
"Timbrar un CFDI" toca Fiscal (genera el XML, llama al PAC), publica un
evento que consume Financiero (crea documento por cobrar), publica otro
que consume Contabilidad (asienta póliza). Si algo falla en el medio, sin
trazabilidad cruzada es imposible saber qué pasó. La observabilidad está
pensada para resolver esto.

El stack es **Serilog para logging estructurado, Application Insights
como sink, W3C Trace Context para correlación** (ver
[ADR-0006](./decisiones/0006-observabilidad-serilog-app-insights.md)).
Cada request HTTP entrante recibe (o reusa, si viene del cliente) un
`TraceId`. Ese `TraceId` se propaga automáticamente a todos los logs del
request, a los mensajes publicados a Service Bus, a los handlers que los
consumen, y a las llamadas HTTP salientes. Una sola operación de negocio
se ve completa en App Insights aunque pase por workers asíncronos y
cambios de proceso. La única propagación que **no es automática** es a
través de SignalR: cuando el servidor envía una notificación a un
cliente vía hub, hay que incluir manualmente `correlationId` en el DTO
del mensaje. Es un patrón a aplicar consistentemente.

Las convenciones de logging son estrictas: niveles `Verbose` y `Debug`
solo en desarrollo, `Information` en adelante en QA y Prod; siempre
templates con propiedades nombradas (`_logger.LogInformation("CFDI
timbrado {CfdiId} para {ClienteRfc}", cfdiId, rfc)`), nunca interpolación
de strings. Hay un `MaskSensitivePropertiesEnricher` con una lista
**balanceada** para el contexto mexicano: enmascara siempre password,
token, CURP, CLABE, números de cuenta, datos personales (nombre,
apellidos, email/teléfono personales), pero **no enmascara** RFC ni
domicilio fiscal ni razón social — son cuasi-públicos, aparecen en CFDIs
y reportes oficiales, y enmascararlos haría inservibles los logs para
debugging. El sampling adaptativo de Application Insights está
configurado con `ExcludedTypes = "Exception"` para preservar el 100% de
las excepciones aunque haya picos de tráfico. El recurso de App Insights
vive en México Central por residencia de datos.

El manejo de errores HTTP usa **Problem Details (RFC 7807)** end-to-end
(ver [ADR-0010](./decisiones/0010-manejo-errores-problem-details.md)).
Toda respuesta de error sigue la misma forma: `type` (URI del tipo de
error), `title`, `status`, `detail`, `instance`, más extensiones del
proyecto: `traceId` (mismo `TraceId` que App Insights, permite a soporte
buscar el log exacto) y `errores` (array granular con
`campo`/`codigo`/`mensaje` que el frontend mapea a campos de
formulario). El mapeo de excepciones a HTTP es uniforme:
`ValidationException` → 400, `BusinessRuleException` → 422,
`EntityNotFoundException` → 404, `ConcurrencyException` → 409,
`UnauthorizedAccessException` → 401, `ForbiddenException` → 403;
cualquier excepción no manejada cae a 500 con `detail` genérico al
usuario y stack completo en log. Nunca se filtran stacktraces al
frontend.

Aparte de los logs operacionales (que viven en App Insights y son
volátiles), el sistema tiene **auditoría de negocio** en BD (ver
[ADR-0008](./decisiones/0008-estrategia-auditoria.md)): tabla
`core.audit_log` particionada mensualmente por `timestamp`, con columnas
`usuario_id`, `empresa_id`, `modulo`, `entidad`, `entidad_id`,
`operacion` (`crear`/`actualizar`/`borrar`/`denegado`), `cambios` (jsonb
con snapshot completo en `crear`, diff campo a campo en `actualizar`), y
`correlation_id` que la liga con los logs operacionales. Un
`AuditSaveChangesInterceptor` en EF Core captura los cambios de las
entidades marcadas con `IAuditable` y escribe el log automáticamente —
cero código de auditoría por entidad.

La inmutabilidad del audit log es operacional: la cuenta de PostgreSQL
que usa la aplicación solo tiene `INSERT` y `SELECT` sobre `audit_log`
(no `UPDATE` ni `DELETE`). El acceso de lectura desde la API requiere
permiso `auditoria.log.leer` y las consultas se loguean en el mismo
`audit_log` (meta-auditoría). Los intentos de acción denegados por falta
de permiso también se registran con `operacion = 'denegado'` para
detectar usuarios probando cosas que no les corresponden. La política de
retención es: 24 meses live en particiones de PostgreSQL, después se
exporta a Blob Storage Cool por 5 años para cumplimiento SAT, y luego
las operaciones fiscales pasan a Archive indefinidamente mientras las no
fiscales se eliminan. El job mensual `AuditArchiveJob` es singleton y
aplica el patrón de advisory lock descrito en la sección anterior. Los
datos personales (PII) se guardan **en crudo** sin enmascaramiento
porque la auditoría debe ser autoritativa; la protección no es por
contenido sino por acceso restringido al permiso de lectura.

## 11. Frontend: lo que el dev internaliza

El frontend es React 18 + TypeScript + Vite + **shadcn/ui** (ver
[ADR-0002](./decisiones/0002-sistema-de-diseno-shadcn-ui.md)). La
diferencia importante con otras librerías de UI es que shadcn **no se
instala como dependencia npm**: se copia el código fuente de cada
componente al repositorio. Los componentes son nuestros y los podemos
modificar libremente. La estructura es `frontend/src/components/ui/` para
los nativos de shadcn y `frontend/src/components/erp/` para los
compuestos del dominio (`MoneyInput`, `RfcInput`, `FechaPicker`,
`TablaDeCartera`, `SelectorEmpresa`, etc.). Tailwind CSS para estilos,
Radix UI debajo para accesibilidad heredada.

Algunos patrones que verás repetidos y vale entender de entrada. La
conexión a SignalR (ver
[ADR-0001](./decisiones/0001-real-time-con-signalr.md)) usa el hook
`useSignalR(hubName)` con reconexión automática. La autenticación contra
los hubs es el JWT del API, no el token de Entra (consistente con la
sección de identidad). El `<CollaborationIndicator />` muestra los soft
locks en pantallas de detalle: cuando otro usuario abre el mismo
registro, aparece "María García también está editando hace 12s". El
`<ConflictResolutionDialog />` se invoca cuando una respuesta 409 indica
conflicto optimista; ofrece descartar, sobreescribir (con permiso) o
fusionar.

Los componentes de presentación aplican las convenciones del dominio sin
que el dev tenga que pensarlas. `<MoneyDisplay value={money} />` formatea
según moneda (MXN sin código, otras con sigla), `<DateTimeDisplay
value={utcString} />` siempre convierte a hora local de México,
`<DateDisplay>` y `<RelativeTimeDisplay>` ("hace 5 minutos") completan el
set. Los inputs hacen el camino inverso: `<MoneyInput />` valida según
moneda (no permite decimales en JPY), `<DatePicker />` interpreta la
entrada como local Mexico_City y la envía al backend en UTC ISO 8601,
`<TipoCambioInput />` muestra el badge de origen (DOF / PACTADO / MANUAL)
y la referencia DOF al lado para comparación visual. El dev no escribe
la conversión de zona ni el format de moneda; usa los componentes.

El selector de empresa siempre visible en el header global (cuando el
usuario tiene acceso a más de una). Cambiar de empresa llama al endpoint
del backend, recibe nuevo JWT, invalida el cache de React Query y
refresca. Para autorización en UI hay un hook
`useHasPermission(permission)` que oculta menús y botones cuando el
usuario no tiene permiso — pero la verificación final siempre la hace el
backend; el frontend solo evita errores 403 visibles por usabilidad.

El manejo de errores del API usa el tipo `ApiError` que coincide con la
forma del Problem Details. Un hook `useApiError(error)` traduce errores
a mensajes de UI; los formularios mapean `errores[].campo` a sus campos
para mostrar mensajes inline.

Sobre cache busting de assets (ver
[ADR-0004](./decisiones/0004-cache-busting-via-vite.md)): Vite genera
nombres de archivo con hash de contenido en cada `npm run build`
(`index-a1b2c3d4.js`, `main-e5f6g7h8.css`). Cualquier cambio produce un
hash distinto, así que los browsers no pueden servir versiones
cacheadas. La política de cache headers que acompaña esto: `index.html`
con `Cache-Control: no-cache` (siempre revalidar), assets con
`Cache-Control: public, max-age=31536000, immutable`. Está prohibido
`?v=` manual en imports — Vite lo resuelve solo.

## 12. Lo que NO debes hacer (las trampas más comunes)

Hay decisiones que están automatizadas por linters, tests de CI o
convenciones que un code review estricto enforza. Vale conocerlas para
no chocar con ellas.

`DateTime.Now`, `DateTime.UtcNow` y `DateTimeOffset.Now` están
**prohibidos** fuera de la implementación de `SystemClock`. Si los usas,
un Roslyn analyzer falla el build. Para obtener el tiempo actual,
inyecta `IClock` y llama `clock.UtcNow`.

`decimal` directo para montos está fuera. Usa `Money`. El compilador y
los tests detectan sumas USD+MXN inválidas y conversiones implícitas.

`IgnoreQueryFilters()` sobre entidades `IPerteneceAEmpresa` requiere
permiso `compartido.cross_empresa.leer` y revisión específica en code
review; no es un opt-out casual.

Joins SQL cross-schema o referencias C# entre módulos están prohibidos.
La comunicación entre módulos pasa por eventos del Service Bus o por
interfaces de dominio explícitas.

Endpoints sin `[RequirePermission]` fallan un test parametrizado de CI
a menos que estén en una lista blanca explícita de endpoints públicos.
Cualquier acción nueva debe declarar su permiso.

Crear una entidad nueva sin declarar `IAuditable` o `INotAudited` falla
el linter de CI. El programador está obligado a tomar una decisión
consciente sobre auditoría.

Mostrar UTC al usuario en cualquier parte de la UI rompe la convención.
Para fechas se usan los componentes (`<DateTimeDisplay>`, etc.); jamás
se imprime un UTC string crudo.

Borrar físicamente entidades `IFiscalmenteRelevante` no es posible — el
query filter de soft delete las hace invisibles aunque alguien intentara
`DELETE`, y el repositorio expone solo soft delete
(`MarcarComoEliminada`). Para CFDIs cancelados se usa la operación SAT
formal, no `DELETE`.

`?v=` manual en imports de assets contradice el cache busting de Vite.
Si necesitas incluir un asset, usa los imports normales; el bundler
hace lo correcto.

Interpolar strings en logs (`$"texto {var}"`) no produce templates
estructurados. Usa siempre la forma de Serilog con propiedades nombradas
para que App Insights pueda buscar por valor.

## 13. Lo que falta y se decidirá después

Las quince ADRs cubren la **fundación crítica** del sistema. Lo que
viene después está consciente y catalogado en el [README de las
ADRs](./decisiones/README.md), que mantiene el "backlog explícito" para
que ninguna decisión se olvide.

Hay un grupo **Tier-2 importante** (todavía fundación, pero abordable
cuando lo justifique el primer módulo que lo necesite): documentación de
API (OpenAPI / Swagger) y generación de tipos TypeScript del frontend;
estrategia de validación (FluentValidation vs Data Annotations);
idempotencia HTTP con header `Idempotency-Key`; versionado de API REST;
health checks y readiness probes; estrategia de background jobs (la
decisión actual de `IHostedService` con advisory locks es el punto de
partida; si crece la operación, evaluamos Hangfire o Quartz);
estrategia de testing unit + Testcontainers + Playwright; state
management del frontend (TanStack Query + Zustand) y forms
(react-hook-form + Zod). Cada uno se aborda como ADR cuando llegue.

Y hay **Tier-3 específico de ERP** que se difiere hasta que el módulo
respectivo lo necesite: almacenamiento de documentos (Blob Storage para
CFDIs XML, PDFs, adjuntos), generación de PDFs (probablemente QuestPDF),
notificaciones por email (ACS Email vs SendGrid), y la migración de
datos desde SAP — que es por sí mismo un mini-proyecto de varios meses.

La regla de oro: si vas a tomar una decisión que afecte a más de un
módulo o que tenga consecuencias difíciles de revertir, escribe un ADR
antes de implementar. El [template](./decisiones/template.md) está listo
y tarda menos en escribirse de lo que parece. El propósito no es
burocrático: es que dentro de seis meses alguien (incluyendo tú) entienda
**por qué** el código está como está, sin tener que reconstruirlo del
git blame.
