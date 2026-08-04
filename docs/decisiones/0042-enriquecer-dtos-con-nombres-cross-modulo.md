# ADR-0042: Enriquecer DTOs de lectura con nombres cross-módulo en el backend (read-port)

- **Estado**: Aceptada
- **Fecha**: 2026-06-05
- **Decisores**: Eduardo Paredes (owner), Claude (backend)
- **Etiquetas**: seguridad, autorización, compras, arquitectura, límites-de-módulo, refinamiento

> Refina (no reemplaza) [ADR-0041](./0041-autorizacion-por-operacion-y-lectura-de-catalogos.md)
> y [ADR-0007](./0007-autorizacion-rbac-granular.md). ADR-0041 dejó como
> consecuencia negativa aceptada que `compartido.catalogos.leer` es **amplio y
> crece**, y que los roles operativos lo reciben para poblar selectores. Este
> ADR ataca un caso concreto de esa misma raíz —la **lectura de nombres para
> mostrar** en listados/detalle— moviéndola al backend para que NO dependa de
> que el cliente tenga un permiso de lectura amplio sobre catálogos de otro
> módulo.

## Contexto y problema

Las bandejas de requisiciones (general y "pendientes de autorización") y el
detalle de una requisición exponen `RequisitanteId` y `DepartamentoId` como
**GUIDs**. El frontend los convertía a nombre **del lado del cliente**: pedía
el catálogo completo de usuarios (`GET /api/v1/identidad/usuarios`,
permiso `identidad.usuarios.leer`) y el de departamentos
(`GET /api/v1/catalogos/departamentos`, permiso `compartido.catalogos.leer`),
y unía por id en memoria (`useUsuarios()` / `useDepartamentos()` + `mapById`).

Esto tiene dos defectos que comparten raíz con ADR-0041:

- **D1 — el nombre exige un permiso amplio que el rol operativo no debería
  necesitar.** Un Jefe de Departamento (autoriza/rechaza requisiciones) **no
  tiene** `identidad.usuarios.leer`, así que el `useUsuarios()` devolvía 403 y
  la columna Requisitante caía al GUID. Para "arreglarlo" habría que concederle
  un permiso de **lectura del padrón de usuarios** —sobre-otorgación—, que
  además **reabre el área `/admin`** (la card "Usuarios" se gatea con ese mismo
  permiso), exactamente la clase de fuga que ADR-0041 buscó cerrar.

- **D2 — mostrar un nombre no es "leer el catálogo".** Resolver
  `id → nombre` para una fila ya visible es una necesidad de **presentación**,
  no de exploración del catálogo. Atarla al permiso amplio mezcla dos
  conceptos y propaga la dependencia del permiso amplio a cada rol que solo
  necesita ver un listado.

De fondo: el dato derivado para mostrar (nombre del requisitante, del
departamento) debe viajar **en el propio DTO de lectura**, resuelto por quien
es dueño del dato, no reconstruido por el cliente a costa de un permiso amplio.

## Drivers de la decisión

- Cerrar la dependencia "ver un nombre ⇒ permiso de lectura amplio de otro
  módulo" (la consecuencia negativa que ADR-0041 dejó abierta).
- Que los roles operativos (requisitante, encargado, jefe de departamento)
  vean nombres **sin** ganar acceso a `/admin` ni al padrón de usuarios.
- Respetar los límites de módulo: cero acceso directo a tablas de otro módulo;
  solo puertos de lectura (`I*ReadPort`) o eventos (regla de oro de la triada,
  CLAUDE.md).
- Consistencia con el patrón cross-módulo ya establecido en Compras.

## Opciones consideradas

1. **Enriquecer los DTOs en el backend vía read-port** (`RequisitanteNombre`,
   `DepartamentoNombre`/`Clave` resueltos server-side) y eliminar la resolución
   client-side — *elegida*.
2. Conceder `identidad.usuarios.leer` a los roles operativos y re-gatear las
   cards de `/admin` (Usuarios/Roles) a permisos de gestión.
3. Dejar la resolución en el cliente como fallback (statu quo + fallback).

## Decisión

**(1) El backend resuelve los nombres y los entrega en el DTO de lectura.**

- Se agrega un puerto de lectura **`IUsuarioReadPort`** en `Compras.Domain.Ports`
  con un método **batch** (`ObtenerNombresAsync(ids) → IReadOnlyDictionary<Guid,string>`),
  para resolver `usuarioId → nombre` sin N+1.
- Su adaptador productivo **`UsuarioReadAdapter`** vive en
  `Compras.Infrastructure/PublicAdapters` y lee `IdentidadDbContext` directamente,
  **mismo patrón** que `AlmacenReadAdapter` / `SucursalDepartamentoReadAdapter`
  (el módulo consumidor hospeda el adaptador y referencia el proyecto dueño del
  dato, para evitar que el módulo dueño dependa de módulos de negocio y se
  formen ciclos). Esto agrega la referencia de proyecto **Compras → Identidad**
  (no circular: Identidad no referencia Compras).
- El nombre del **departamento** reutiliza el read ya existente hacia
  `Compartido` (extendido para resolver `clave + nombre` por ids en batch).
- Los DTOs de **lista** (`RequisicionListItemResponse`) y **detalle**
  (`RequisicionResponse`) llevan `RequisitanteNombre`, `DepartamentoNombre` y
  `DepartamentoClave`; los 3 handlers de lectura los poblan tras la query
  paginada (fallback al id si la resolución no encuentra la clave).
- El **histórico/bitácora de la RQ** (`HistoricoEntryResponse`, endpoint
  separado `GET /requisiciones/{id}/historico`) lleva `ActorNombre`, resuelto
  con el **mismo `IUsuarioReadPort`** por batch en `ObtenerHistoricoHandler`.
  Entradas sin actor (`ActorId` null = sistema/automática) → el FE muestra
  "Sistema"; actor no resuelto (service principal / usuario borrado) → cae al
  id. Esto permite **eliminar `useUsuarios()` por completo del detalle** (era
  su último consumidor allí, vía el modal de histórico).
- El **frontend** consume esos campos y **elimina** la resolución client-side de
  nombres. En el **detalle** se quitan `useUsuarios()` y `useDepartamentos()`
  por completo (requisitante, departamento y actores del histórico vienen del
  backend); en las **bandejas** se quita `useUsuarios()` y se conserva
  `useDepartamentos()` solo para el dropdown de filtro por departamento (gateado
  por `ver-todos-departamentos`). `useUsuarios()` sobrevive únicamente en el
  selector de **delegación** de requisitante (flujo aparte, bien gateado).

Alcance de esta decisión: **requisitante, departamento y actor del histórico**
en lista y detalle. **Anti-scope**: el `Creador` de la RQ queda con id (no es
prominente); los nombres de **sucursal, almacén y proveedor** del detalle se
siguen resolviendo client-side vía `compartido.catalogos.leer` (permiso que los
roles operativos sí tienen, ADR-0041) — fuera de alcance. Lo que este ADR cierra
es la dependencia de **`identidad.usuarios.leer`** para mostrar nombres.

## Consecuencias

**Positivas**

- El Jefe de Departamento (y cualquier rol operativo) ve los nombres **sin**
  `identidad.usuarios.leer` ni `/admin`. Cierra la fuga que ADR-0041 dejaba
  latente para este caso.
- "Ver un nombre" deja de depender del permiso amplio de catálogos: separa
  presentación de exploración de catálogo.
- El cliente deja de descargar el padrón completo de usuarios para pintar una
  bandeja (menos datos sensibles en el navegador, menos tráfico).
- El nombre queda disponible también para exportaciones/reportes server-side.

**Negativas / trade-offs**

- Acopla **Compras → Identidad** a nivel de proyecto (nueva referencia).
  **Aceptado**: es el mismo patrón ya usado para Almacén/Compartido, encapsulado
  tras un puerto en `Compras.Domain`; el dominio no conoce a Identidad, solo el
  adaptador de infraestructura.
- Dos queries de resolución extra por página (una a usuarios, una a
  departamentos). **Aceptado**: son batch (`WHERE id = ANY(@ids)`), acotadas al
  tamaño de página, sin N+1.
- El nombre viaja "denormalizado" en la respuesta; si un usuario se renombra, el
  valor es correcto en la siguiente lectura (no se cachea en la RQ).

## Descartadas

**Conceder `identidad.usuarios.leer` a los operativos + re-gatear `/admin`**
(Opción 2): resuelve el nombre pero **sobre-otorga** (padrón de usuarios) y
obliga a re-gatear las cards de Identidad en `/admin`, afectando a otros roles
(p. ej. auditor). Trata el síntoma (permiso) en vez de la causa (el nombre no
debería requerir el permiso amplio).

**Fallback client-side** (Opción 3): mantener `useUsuarios()`/`useDepartamentos()`
como respaldo **reintroduce** el fetch del catálogo completo y, con él, la
necesidad del permiso amplio → **no cierra** la vulnerabilidad. Por eso se
elimina del render de filas (no solo se complementa).

## Notas de implementación

- **Puerto**: `backend/src/Compras/Domain/Ports/Identidad/IUsuarioReadPort.cs`
  (`ObtenerNombresAsync(IEnumerable<Guid>, ct)`).
- **Adaptador**: `backend/src/Compras/Infrastructure/PublicAdapters/UsuarioReadAdapter.cs`
  (lee `IdentidadDbContext.Usuarios`, `Bypass()` de empresa como los hermanos);
  registrar en el DI de Compras.
- **csproj**: agregar `ProjectReference` a `Millet.Identidad.csproj` en
  `Millet.Compras.csproj`.
- **Departamento**: extender el read de Compartido
  (`ISucursalDepartamentoReadPort` / su adaptador) con resolución batch
  `id → (clave, nombre)`.
- **DTOs**: `RequisicionListItemResponse`, `RequisicionResponse` (+ 3 campos);
  `HistoricoEntryResponse` (+ `ActorNombre`).
- **Handlers**: `ListarRequisicionesHandler`, `ListarPendientesAutorizacionHandler`,
  `ObtenerRequisicionPorIdHandler`, `ObtenerHistoricoHandler` (batch por
  `ActorId`).
- **Red de seguridad (tests permanentes)**: tests de integración aciertan que un
  usuario **sin** `identidad.usuarios.leer` (el rol Jefe de Departamento) recibe
  (1) `RequisitanteNombre`/`DepartamentoNombre` en la bandeja de pendientes y
  (2) `ActorNombre` en el histórico de una RQ — guards permanentes del cierre de
  la vulnerabilidad. Cubrir también el caso actor null → `ActorNombre` null.
- **Frontend**: usar los campos del DTO; quitar `useUsuarios()` y
  `useDepartamentos()` por completo del **detalle** (incl. `TimelineRequisicion`/
  `HistoricoModal`, que usan `ActorNombre` en vez de `resolverUsuario`); en las
  **bandejas** quitar `useUsuarios()` y conservar `useDepartamentos()` solo para
  el dropdown gateado; `useUsuarios()` solo sobrevive en el selector de delegación.

## Addendum — 2026-06-09: Almacén/Salidas como segundo aplicador

El detalle de una **Salida de almacén** (pantalla + comprobante PDF, que comparten
el DTO `SalidaDetalle`) exponía cuatro campos como id/clave cruda: **sub-almacén**,
**artículo** (por línea), **folio de RQ** y **persona destinataria**. Se aplica el
patrón de este ADR (resolver en backend vía read-port, fallback al id). Esto añade
tres precisiones que rigen para todo el back-office:

1. **El read-port de resolución expone un método batch.** Igual que
   `IUsuarioReadPort.ObtenerNombresAsync(ids)`, las resoluciones por id usan
   firma batch para evitar N+1 al enriquecer varias filas/líneas:
   `IArticuloReadPort.ObtenerPorIdsAsync(ids)` y
   `IComprasRequisicionReadPort.ObtenerFoliosAsync(ids)`. El handler junta los ids
   distintos (p. ej. los `ArticuloId` de las líneas de la salida) y resuelve en una
   sola consulta.

2. **La lectura de presentación es state-agnostic.** El folio de una RQ debe
   mostrarse aunque la RQ ya haya avanzado de estado (una salida histórica puede
   tener su RQ `Surtida`/`Cerrada`). Por eso `ObtenerFoliosAsync` **no** filtra por
   estado, a diferencia de la lectura **operativa** `ObtenerAsync` —que sí gatea por
   `Autorizada`/`EnSurtido` porque valida si la RQ admite surtido—. Son dos lecturas
   con contratos distintos sobre el mismo puerto; no se mezclan.

3. **Dónde vive el adaptador depende del origen del dato y de los ciclos de
   proyecto.** Regla general (ADR §Decisión): el adaptador vive en la
   **Infrastructure del consumidor**, que referencia al módulo dueño del dato.
   Excepciones que aparecieron aquí:
   - **Artículo** → su adaptador vive en `Compartido.Infrastructure` porque su
     **origen es Compartido** (`compartido.articulos`); el consumidor (Almacén) lee
     a través del puerto y no referencia Compartido.
   - **Usuario (persona destinataria)** → su origen es Identidad, pero el consumidor
     **Almacén no puede hospedar el adaptador**: `Identidad` ya referencia
     `Almacén` (y `Compartido`) para sembrar permisos canónicos, así que una
     referencia inversa `Almacén→Identidad` (o `Compartido→Identidad`) **cierra
     ciclo**. A diferencia de Compras —que sí pudo hospedar su `UsuarioReadAdapter`
     porque Identidad no referencia Compras—, aquí el adaptador lo hospeda el
     **owner**: `Identidad.Infrastructure.PublicAdapters.UsuarioReadAdapter`
     implementa el `IUsuarioReadPort` declarado en `Almacen.Domain.Ports` y se
     cablea en `Program.cs`. Como Identidad ya referencia `Almacen.Domain`,
     implementar su puerto no agrega acoplamiento nuevo.

   **Heurística para el siguiente módulo:** el adaptador de un read-port va en la
   Infrastructure del **consumidor**; si esa referencia cerrara ciclo (el dueño del
   dato ya referencia al consumidor), lo hospeda el **dueño**. Nunca en un módulo
   que no pueda referenciar al dueño (p. ej. Compartido hacia Identidad).

**Nota de modelado descubierta:** la `PersonaDestinatariaId` de una salida es un
**usuario de Identidad** (el selector del front la toma de `identidad.usuarios`),
no un "empleado" de Administración. El `IEmpleadoReadPort` de Almacén —que la
documentaba como consumidor— se corrige: queda para conteos/devoluciones y sigue
como stub NoOp (PLATFORM-TODO `<EmpleadoReadAdapter>`).

**Anti-scope del addendum:** no migra el `IUsuarioReadPort` de Compras (se mantiene
el suyo, hospedado en Compras); no construye el catálogo Empleado real; la **bandeja**
de salidas (`SalidaListItem`) queda fuera —el alcance es detalle + comprobante—.

## Addendum — 2026-06-10: resolución intra-módulo (sin read-port)

El detalle de una **Orden de Compra** mostraba el `RequisicionId` (GUID) de cada
línea heredada en vez del folio humano de la RQ (`MID2026-NNNNNN`), en la pantalla
y en su impresión client-side. Es el mismo problema que este ADR resuelve, pero con
una diferencia clave: **la requisición es agregado del propio módulo Compras**, no
de otro módulo. Esto añade una precisión que rige para todo el back-office:

1. **Cuando el dato a resolver es del PROPIO módulo, se resuelve por query directa
   al DbContext propio — sin read-port ni nueva referencia de proyecto.** La regla
   de oro de la triada ("cero acceso directo a tablas de **otro** módulo") aplica
   solo a tablas ajenas; leer un agregado del mismo módulo es acceso intra-módulo
   legítimo. `ObtenerOrdenCompraPorIdHandler` resuelve `rqId → folio` consultando
   `ComprasDbContext.Requisiciones` directamente, en **batch** por los
   `RequisicionId` distintos de las líneas (sin N+1) y **state-agnostic** (sin filtro
   de estado: una OC histórica puede tener su RQ ya `Cerrada`). Si la RQ no resuelve,
   el campo queda `null` y el frontend cae al id.

2. **Se conservan los principios del ADR** aunque cambie el mecanismo: la resolución
   vive en el **backend**, viaja en el **DTO de lectura** (`LineaOrdenCompraResponse.
   RequisicionFolio`), es **batch** y **state-agnostic**, con **fallback al id**.

3. **No se reusa el `IComprasRequisicionReadPort` de #396** (declarado en
   `Almacen.Domain.Ports` para que Almacén lea Compras): que Compras dependiera de él
   **invertiría la dependencia** (`Compras → Almacen.Domain`) y rompería los límites
   de módulo. El read-port cross-módulo es para que **otro** módulo lea Compras; el
   propio Compras no lo necesita.

   **Heurística para el siguiente módulo:** ¿el dato es de tu módulo? → query directa
   a tu DbContext. ¿Es de otro módulo? → read-port (con la heurística de hospedaje del
   addendum anterior). Nunca al revés.

**Nota de implementación:** el enriquecimiento se hace **tras** el `Map` de Mapster,
reconstruyendo las líneas inmutables con `with` (`response with { Lineas = ... }`).
El campo nuevo se mapea explícito a `null` en el `OcMapsterConfig`
(`.Map(dest => dest.RequisicionFolio, src => null)`) y **no** con `.Ignore`: sobre un
`record` posicional Mapster construye por constructor y un `.Ignore` de un parámetro
del ctor desbalancea los argumentos ("Incorrect number of arguments for constructor").

**Anti-scope del addendum:** el **PDF server-side** de la OC (QuestPDF) tiene sus
propios bugs de contenido (imprime `ArticuloId` crudo y no muestra la RQ) — es un
render path distinto y queda como follow-up aparte. Las bandejas y demás queries de
OC no exponen `RequisicionId`, así que no se tocan.

## Addendum — 2026-06-12: Almacén/Recepciones como tercer aplicador

El detalle y la **bandeja** de una **Recepción de almacén** exponían tres campos
como id/clave cruda: **sub-almacén** y **artículo** (por línea) en el detalle, y la
**Orden de Compra** (GUID en vez de folio) tanto en el detalle como en la columna OC
de la bandeja. Se aplica el patrón de este ADR (resolver en backend, fallback al id).
Reusa la infraestructura del addendum de Salidas (#396): lookup local de sub-almacén,
`IArticuloReadPort.ObtenerPorIdsAsync` (batch, ya wired). Precisiones que añade:

1. **Segundo caso de lectura cross-módulo state-agnostic de presentación: el folio de
   OC.** Es el espejo exacto del folio de RQ de #396, pero hacia el agregado OC de
   Compras. Se agrega `IComprasOcReadPort.ObtenerFoliosAsync(ocIds)` —**batch** y
   **state-agnostic**— junto a `ObtenerAsync`, que **no se toca**: esa última es la
   lectura **operativa** que gatea por `Autorizada` para validar si la OC admite
   recepción. Dos contratos sobre el mismo puerto, no se mezclan (igual que RQ). El
   adaptador (`ComprasOcReadAdapter`, hospedado en `Compras.Infrastructure` porque
   Compras ya referencia `Almacen.Domain`) proyecta `{ Id, Folio }` **sin
   `Include(Lineas)`** y materializa el VO `Folio.Valor` **en memoria** (acceder a
   `.Valor` dentro del árbol SQL no es traducible de forma confiable sobre una
   propiedad value-converted — lección de #396).

2. **La bandeja también se enriquece, resolviendo folios en batch sobre la página.**
   A diferencia del addendum de Salidas (cuyo alcance fue solo detalle + comprobante
   porque la `SalidaListItem` no exponía OC), aquí `RecepcionListItem` sí muestra la
   OC, así que `ListarRecepcionesHandler` resuelve los folios de los `OcId`
   **distintos de la página** en una sola consulta (anti-N+1). El sub-almacén de la
   bandeja ya se resolvía client-side y no se toca.

3. **Sin comprobante/PDF que tocar.** La recepción no tiene impresión client-side ni
   server-side (a diferencia de Salida con su `ComprobanteSalidaDocument`), así que el
   fallback nombre∥id va **inline** en `RecepcionDetallePage` (estilo `SalidaDetallePage`),
   sin helper compartido.

**Nota sobre EF InMemory + VOs:** no aplica la fricción de #396/#397 aquí. El
`MovimientoInventario.Folio` es `string` (no VO) y sus líneas son primitivas, así que
el handler de Almacén se testea con InMemory + fakes end-to-end. El adaptador de OC se
testea con InMemory porque su proyección no hace `Include` (no materializa el VO
`Money` de las líneas, lo único que el provider de test no shapea). Test unit en ambos
lados; no se necesitó la estrategia helper-puro + integración de #397.

**Anti-scope del addendum:** `CfdiRecibidoId` y `FacturaId` del detalle siguen como
GUID — su resolución (folio CFDI / factura) exige read-ports a CxP que aún no existen;
queda como follow-up. `RegistradoPor` no se renderiza hoy (sin UI nueva). El proveedor
no está en el DTO de recepción (vive en la OC) y no se agrega.

## Addendum — 2026-06-18: proveedor/artículo en detalle req/OC resueltos server-side

El cuerpo de este ADR dejó como **anti-scope** los nombres de **proveedor y
artículo** en req/OC: se seguían resolviendo **client-side** vía
`compartido.catalogos.leer`. A escala (miles de proveedores/artículos) eso
falló: la resolución dependía de una lista **capada** (typeahead `limit=50` en
los selectores; `limit:1000` que el endpoint de catálogos topa a 200 en el
detalle, `CatalogosEndpoints.LimitMax`), así que el ítem seleccionado/ranqueado
más allá del tope caía a mostrar el **UUID**. El FK guardado siempre fue
correcto — era solo visualización.

**Decisión (cierra ese anti-scope para el detalle):** el detalle de requisición
y de OC **enriquece server-side** `articuloClave`/`articuloNombre` (por línea) y
`proveedor*RazonSocial`/`*Clave` (cabecera), con el **mismo patrón batch** de
este ADR. Se agregan puertos **propios de Compras** en
`Compras.Domain.Ports.DatosMaestros` (`IArticuloReadPort` con `ObtenerPorIdsAsync`
batch; `IProveedorReadPort` ídem), con adapters delgados en
`Compras.Infrastructure.PublicAdapters` que leen `compartido.articulos`/
`compartido.proveedores` via `CompartidoDbContext` (`AsNoTracking` + `Bypass()`
de empresa). Batch por ids distintos (sin N+1), fallback a `null` (el FE cae al
id). `ObtenerRequisicionPorIdHandler`/`ObtenerOrdenCompraPorIdHandler` los pueblan
tras el `Map` con helpers puros; Mapster deja los campos en `null` explícito (no
`.Ignore` — record posicional).

> **Soft-delete:** los adapters **no** usan `IgnoreQueryFilters()`. `Articulo`/
> `Proveedor` solo implementan `IAuditable` (no `IFiscalmenteRelevante`/
> `IPerteneceAEmpresa`), así que `BaseDbContext.ApplyQueryFilters` no les aplica
> filtro global — un catálogo desactivado/borrado igual resuelve su nombre en
> req/OC históricas.

**Complemento frontend:** los selectores `ArticuloSelector`/`ProveedorSelector`
guardan el objeto seleccionado `{id,clave,nombre}` y aceptan una etiqueta inicial
(`initialLabel`) desde el DTO enriquecido (edición). El form sigue guardando solo
el `id`. Así el selector no depende de la lista capada ni en alta ni en edición.

**Alcance:** detalle req/OC (cabecera proveedor + líneas artículo) + los
selectores compartidos. El cap de `CatalogosEndpoints` (200) se mantiene (se hace
explícito en comentario): deja de ser load-bearing porque el detalle ya no
depende de la lista client-side.

**Follow-ups** (mismo enfoque batch reusando los read-ports, en su propio DTO,
fuera de este PR): bandejas de OC (proveedor), ~~**Saldos** (artículo)~~ **(hecho:
`ListarSaldosHandler` enriquece `ArticuloClave`/`ArticuloDescripcion` vía
`IArticuloReadPort`; filtro de Saldos = `ArticuloSelector`)**,
**recepción de compra**, **factura/CxP**, y el **panel de cubrimiento estimado**
de la RQ (`CubrimientoEstimadoPanel`, cuyas líneas vienen de otro endpoint y
siguen resolviéndose client-side).
