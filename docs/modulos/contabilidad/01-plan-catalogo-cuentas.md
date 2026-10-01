# Plan — F1-CON-01 Catálogo contable consumible (`Millet.Contabilidad`)

> **Versión:** 0.1 (borrador de planeación) · **Fecha:** 2026-10-01
> **Tarea:** F1-CON-01 · **Responsable:** Uziel · **Rama:** `feature/F1-CON-01-catalogo-contable`
> **Estado:** PLAN. No hay código de producto, migraciones ni cambios en `backend/`/`frontend/`.
> **Fuente funcional:** ficha F1-CON-01 (texto del dueño, planeación provisional 28-sep).
> **Regla de la ficha:** no depende de Geovany; no mezclar cambios de otro responsable; PR propio con ID de tarea.

---

## 0. Cómo leer este documento

§1 contexto y alcance · §2 hallazgos (lo que YA existe) · §3 decisión de módulo ·
§4 modelo de datos · §5 reglas de dominio · §6 importación · §7 puerto de lectura y
cableado a stubs · §8 API · §9 permisos · §10 UI · §11 migración y DbContext ·
§12 pruebas · §13 aislamiento · §14 fases, esfuerzo y commits · §15 ADRs ·
§16 entregables · §17 dependencias de plataforma pendientes (ADR-0031) ·
§18 preguntas abiertas.

Todos los datos de ejemplo de este plan son **ficticios y no reales** (prefijo
`FIX-`); no representan cuentas de Millet.

---

## 1. Contexto, alcance y anti-alcance

### Contexto
Antes de automatizar una compra o un cobro, el ERP necesita cuentas donde
registrar el efecto. El catálogo es **de Millet** (una sola razón social, varias
sucursales) y debe permitir agrupar reportes sin duplicar cuentas por sucursal.
Hoy cuatro módulos apuntan a un módulo Contabilidad que no existe (§2).

### Alcance (en esta tarea)
- Entidad `CuentaContable` jerárquica, persistida en esquema propio, con comandos
  y consultas (MediatR) bajo el patrón hexagonal vigente.
- Lista/árbol, búsqueda, detalle, edición y baja lógica (API + pantalla).
- Importación de muestra: vista previa, errores por fila, correspondencia de
  origen, aplicación transaccional e idempotente.
- Puerto de lectura para consumidores (`ICuentaContableReadPort`): validar cuenta
  vigente y afectable.
- Permisos canónicos nuevos + migración del catálogo de permisos (Identidad).
- Pruebas nominales y negativas contra persistencia y API reales.
- Documentación: contrato de endpoints y permisos, códigos de error, muestra y
  mapeo contra el catálogo de Contabilidad.

### Anti-alcance (NO se hace)
- No se inventan cuentas reales, naturalezas, cuentas de control ni agrupaciones
  de Millet (las entrega/valida Contabilidad; ver §18).
- No se crean pólizas, asientos automáticos, balanza ni cierre. El
  `IContabilidadAsientoPort` de Facturación y `IPeriodoContablePort` NO se
  tocan (pertenecen a otra tarea).
- No se cablean todos los consumidores; se publica el puerto y se cablea el
  mínimo (§7). Los consumidores se migran en sus propias tareas.
- No se crea modelo de sucursal en el catálogo (ver §13).
- No se modifica Centros de Costo, ADM-08 ni cambios de otro responsable.
- No se hace BI/reportes contables (ADR-0036 queda para el módulo 10).

---

## 2. Hallazgos: lo que YA existe (verificado en el árbol, HEAD `0096cb8`)

### 2.1 No existe un módulo ni catálogo de Contabilidad
`rg "CuentaContable|PlanCuentas|cuentas_contables"` en `backend/src`: **cero**
entidades, DbContext o tablas de catálogo de cuentas. No hay carpeta
`backend/src/Contabilidad` ni `docs/modulos/contabilidad/` (este plan es el
primer archivo). `tools/migration-contexts.txt` no lista un DbContext contable.
Conclusión: **un puerto o stub no equivale al catálogo; hay que construirlo.**

### 2.2 Puertos y stubs que esperan Contabilidad (contratos de consumidor)
| Consumidor | Archivo | Contrato esperado | Estado |
|---|---|---|---|
| Facturación | `backend/src/Facturacion/Domain/Ports/IContabilidadAsientoPort.cs`, stub `Infrastructure/Stubs/NoOpContabilidadAsientoPort.cs` | `RegistrarAsientoAsync` (póliza; escritura). Cuentas `TBD-*`. | Fuera de alcance (pólizas) |
| Facturación / Tesorería / Almacén | `Facturacion/Domain/Ports/IPeriodoContablePort.cs`, `Tesoreria/Domain/Ports/IPeriodoContablePort.cs`, `Tesoreria/Infrastructure/Stubs/NoOpPeriodoContablePort.cs`, `Almacen/Infrastructure/Stubs/NoOpReadPorts.cs` (`NoOpPeriodoContableReadPort`) | Candado de período. | Fuera de alcance (cierre) |
| CxP | `CuentasPorPagar/Domain/Ports/Contabilidad/IConceptoContableReadPort.cs` (`ConceptoContableDto(Id, Codigo, Nombre, CuentaContable: string, Activo)`), stub `Infrastructure/Stubs/NoOpConceptoContableReadPort.cs` (`PLATFORM-TODO(<ContabilidadConceptos>)`) | Concepto→cuenta como **string**. | Stub; no es el catálogo de cuentas |
| Almacén | `Almacen/Domain/Ports/IConceptoContableReadPort.cs` (`ConceptoContableLectura(Codigo, Nombre, CuentaDeudora?, CuentaAcreedora?, EsActivo)`), stub en `Almacen/Infrastructure/Stubs/NoOpReadPorts.cs` (`PLATFORM-TODO(<ConceptoContableReadAdapter>)`) | Concepto→cuenta (códigos string). | Stub |
| Tesorería | `Tesoreria/Domain/Cuentas/CuentaBancaria.cs` (`CuentaContableRef` string?, "Contabilidad futura [TES-5]"), `Application/Cuentas/CuentasCommands.cs` | Referencia libre, sin validación. | Referencia sin FK |
| Centros de Costo | `Identidad/Domain/PermisosCanonicos.cs` ~L446: "Contabilidad ve todas las máquinas" | Solo comentario de rol. | n/a |

**Hallazgo clave:** los contratos existentes son de **conceptos contables**
(concepto → cuenta, strings) y de **asientos/período**, no de "validar que una
cuenta esté vigente y sea afectable". Hace falta un puerto nuevo (§7). Los
`IConceptoContableReadPort` de CxP y Almacén son de otra capa (mapeo
concepto→cuenta) y se quedan como `NoOp` hasta que exista el módulo de conceptos;
solo se **les agrega validación futura** contra el nuevo puerto (no en F1-CON-01).

### 2.3 Documentación
- `docs/modulos/` tiene 13 carpetas; ninguna `contabilidad`. CLAUDE.md lista el
  módulo 9 "Contabilidad — plan de cuentas, pólizas…" sin diseño.
- Obsidian (`.../Modulos/Contabilidad_y_Fiscal.md`): Contabilidad = "Plan de
  Cuentas centralizado", pólizas, cierre mensual, balanza. Confirma el alcance
  conceptual pero **no define formato de código, niveles ni agrupaciones**.
  `Catalogos_y_DatosMaestros.md` lista catálogos compartidos (esquema
  `compartido`) sin cuentas contables. Solo lectura; no hay dato de negocio que
  copiar.
- Rama de trabajo: ya existen archivos sin versionar en el repo original
  (`docs/modulos/identidad/…`) de otro agente; este plan **no** depende de ellos.

### 2.4 Exemplar elegido: Centros de Costo (`Millet.CentrosCosto`)
Es el CRUD jerárquico más reciente con baja lógica, ETag, permisos y UI de árbol.
Estructura a replicar:
- **Proyecto** `backend/src/CentrosCosto/Millet.CentrosCosto.csproj` (net10.0;
  refs a `SharedKernel`, `Compartido`; MediatR, FluentValidation, EF Core Npgsql,
  EFCore.NamingConventions). `InternalsVisibleTo` a UnitTests/IntegrationTests.
  Capas: `Domain/` (`Dim1.cs` … : `BaseEntity, IAuditable`, constructor con
  validación que lanza `BusinessRuleException(code, msg)`), `Application/`
  (`Catalogo/*Commands.cs`, `*Queries.cs`, `Common/PagedResponse.cs`,
  `PublicPorts/IDim3ReadPort.cs`, `AssemblyMarker.cs`), `Infrastructure/`
  (`DependencyInjection.cs` → `AddCentrosCostoModule()`,
  `Persistence/CentrosCostoDbContext.cs`, `Configurations/*`,
  `Migrations/*`, `PublicAdapters/Dim3ReadAdapter.cs`).
- **DbContext**: hereda `BaseDbContext`, `SchemaName = "centros_costo"`,
  `HasDefaultSchema`, `ApplyConfiguration` por entidad. `BaseDbContext` aporta
  `Version` como concurrency token, auditoría temporal, soft-delete (`DeletedAt`)
  y **query filter por `EmpresaId`** para entidades `IPerteneceAEmpresa`
  (ADR-0011).
- **Endpoints**: `backend/src/Api/Endpoints/CentrosCosto/CentrosCostoCatalogoEndpoints.cs`
  (`MapGroup("/api/v1/centros-costo/…")`, `RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.X)`,
  `RequireIdempotencyKeyAttribute` en POST, `ETag` en GET por id, `If-Match`
  obligatorio en mutaciones: 428 si falta, 409 si no coincide). Registro en
  `Program.cs`: `using`, `AddXxxModule()`, `AddDbContext<…>(ConfigureMilletDbContext)`,
  `MigrationsHealthCheckOptions.ContextTypes`, `MapXxxEndpoints(app)`, y
  referencia del proyecto en `Millet.Api.csproj` y `backend/Millet.sln`.
- **Permisos**: `PermisosCanonicos.cs` (const + tupla `(Guid, código, descripción)`;
  CeCo usa namespace `0000000c-*`). Migración de seed en `Identidad`
  (`InsertData` en `identidad.permisos`; ejemplo
  `20260928230813_SeedPermisosDatosMaestrosProveedoresBancarios.cs`). El
  namespace libre siguiente es **`0000000d-*`** (verificado: 02..0c en uso).
- **Pruebas**: no hay proyecto UnitTests de CeCo; sus pruebas son de integración
  en `backend/tests/Api.IntegrationTests/CentrosCosto/*` con
  `IClassFixture<WebApplicationFactory<Program>>`, mediator real contra Postgres,
  sufijos aleatorios y limpieza en `finally`. Para HTTP con permisos:
  `PermisoFaltante403Tests` usa `POST /api/dev/fake-login` + `Bearer`; helper
  `TestClientExtensions.CreateClientWithIdempotency`. Los módulos grandes
  (Tesorería, Almacén) tienen proyecto `*.UnitTests` en el `.sln`.
- **Importación**: **no hay herramienta de importación in-app aceptada**
  (ADR-0044 rechazó la de PR #410; carga única por SQL). Único precedente de
  lectura de Excel: ClosedXML 0.105 en `Millet.CuentasPorPagar` (F7-PR5).
  Frontend ya depende de `exceljs`. → la importación de F1-CON-01 es una pieza
  **nueva** que debe justificarse (ver §6).
- **Alta de DbContext**: `tools/migration-contexts.txt` (orden canónico),
  `.github/workflows/deploy-app-dev.yml` (bucle `for ctx in …` + `case` con la ruta
  del proyecto + comentario de lista), `Program.cs` (`MigrationsHealthCheckOptions`),
  y `tools/validate-*.sh/.ps1` si leen la lista. Último contexto agregado:
  `CentrosCostoDbContext`.
- **UI**: `frontend/src/features/centros-costo/` (api/hooks de React Query,
  `components/ArbolCentrosCosto.tsx`, `pages/ConfiguracionCentrosCostoPage.tsx`,
  `lib/handle-conflict.ts`, `schemas/catalogo.ts`, tests `*.smoke.test.tsx`),
  rutas `frontend/src/routes/_app/centros-costo/*.tsx`, permisos en
  `frontend/src/lib/auth/permission-codes.ts`. Patrón general:
  `frontend/docs/patrones-compras.md` (master-detail 320px, Sheet para "Nuevo",
  inline forms, sub-topbar `data-print="hidden"`).

### 2.5 Divergencia a notar: EmpresaId
Centros de Costo es **global, sin `EmpresaId`** (decisión ADM-08,
`docs/modulos/centros-costo/09-adm08-analisis-plan.md`). La ficha F1-CON-01 pide
**conservar EmpresaId técnico**. Este plan sigue la ficha: `IPerteneceAEmpresa`
(ADR-0011), una Millet operativa. Hay que explicitarlo en ADR (§15) para que no se
confunda con CeCo.

---

## 3. Decisión de módulo

**Propuesta: nuevo módulo `Millet.Contabilidad`, esquema Postgres `contabilidad`,
`ContabilidadDbContext` propio.** Justificación:
1. CLAUDE.md define Contabilidad como módulo 9 con fronteras propias; ADR-0030
   (un DbContext/esquema por módulo) y la triada de stubs (`PLATFORM-TODO`) lo
   nombran como dueño real. Reutilizar `compartido` o CeCo rompe la regla "cada
   módulo es dueño de su esquema".
2. `Compartido` es de datos maestros transversales de bajo cambio (proveedores,
   artículos); un catálogo con reglas de uso/inmutabilidad y futuros asientos
   crecerá hasta ser el módulo de pólizas — nace ya en su sitio.
3. Centros de Costo declaró **separación total** (§1 de su diseño); mezclar
   cuentas ahí contradice su ADR/decisión.
4. Precedente directo: Tesorería y CeCo crearon su proyecto/esquema/DbContext y
   su set de docs desde el primer PR.

Costo asumido: alta de DbContext (§11) ≈ 1 h; es el camino ya trillado.
Namespace: `Millet.Contabilidad` (Domain/Application/Infrastructure),
subárea `Catalogo/` para dejar espacio a `Polizas/`, `Periodos/`.

---

## 4. Modelo de datos (esquema `contabilidad`)

### 4.1 `cuentas_contables`
Entidad `CuentaContable : BaseEntity, IAuditable, IPerteneceAEmpresa`
(`BaseEntity` ya da `Id` GUID v7, `Version`, `CreatedAt/By`, `UpdatedAt/By`,
`DeletedAt`; `IAuditable` registra diff en `core.audit_log`).

| Columna | Tipo | Regla |
|---|---|---|
| `id` | uuid PK | v7 |
| `empresa_id` | uuid | ADR-0011; asignado por interceptor; nunca del cliente |
| `codigo` | varchar(30) | normalizado (trim, mayúsculas); formato exacto **pendiente** (§18); **único `(empresa_id, codigo)`** (índice único; no parcial para que baja lógica no libere el código — recomendado, §18) |
| `nombre` | varchar(254) | requerido |
| `padre_id` | uuid? FK self | nulo = raíz; `ON DELETE RESTRICT` |
| `nivel` | smallint | **almacenado y validado**: raíz=1, hijo=padre.nivel+1; tope configurable (default 10, §18); recalculado en cascada si el padre cambia (solo permitido sin uso) |
| `ruta` | varchar | materialized path opcional (`/id1/id2/`) para detectar ciclos y listar subárbol sin recursión; **solo si EF/CTE recursivo resulta costoso** — recomendado omitir en v1 y usar CTE recursivo |
| `naturaleza` | smallint enum `NaturalezaCuenta {Deudora, Acreedora}` | requerida; la entrega Contabilidad (§18) |
| `tipo` | smallint enum `TipoCuenta {Titulo, Afectable}` | requerida. Título puede tener hijos y **no recibe movimientos**; afectable es hoja |
| `estatus` | smallint `EstatusCatalogo` (reuso de `Millet.Catalogos`: Activo/Inactivo) | baja lógica = `Inactivo` (no `DeletedAt`; "desactivar no destruye referencias") |
| `cuenta_control` | smallint enum `CuentaControl {Ninguna, Clientes, Proveedores}` | solo `Afectable`; marca cuenta de control (§5.7) |
| `codigo_agrupador` | varchar(30)? | referencia de agrupación (p. ej. código agrupador SAT) — **valor pendiente de Contabilidad** (§18); texto libre validado por longitud |
| `grupo_reporte` | varchar(60)? | agrupación de reportes por Millet (no por sucursal); pendiente de definición (§18) |
| `version` | int | `BaseEntity.Version` (ETag/If-Match, ADR-0012) |
| auditoría | — | `BaseEntity` + `IAuditable` |

Índices: `(empresa_id, codigo)` único; `(empresa_id, padre_id)`;
`(empresa_id, estatus, tipo)`; búsqueda textual por `codigo`/`nombre` con el
patrón de ADR-0045 (sin extensiones).

### 4.2 `cuentas_contables_origen` (correspondencia de origen)
`id`, `empresa_id`, `cuenta_id` FK, `fuente` varchar(40) (p. ej. `ARCHIVO-CONTABILIDAD`, `SAP`),
`codigo_origen` varchar(60), `lote_id` FK. Único `(empresa_id, fuente, codigo_origen)`.
Permite reimportar sin duplicar aun si el código interno se normaliza distinto, y
documentar la equivalencia SAP↔ERP.

### 4.3 `importaciones_catalogo` (lote)
`id`, `empresa_id`, `fuente`, `archivo_nombre`, `huella_sha256` (de filas
canonicalizadas), `estado` (`Aplicado`), `total_filas`, `creadas`, `actualizadas`,
`sin_cambios`, `aplicado_en`, `aplicado_por`. Único `(empresa_id, huella_sha256)` →
idempotencia a nivel lote (reenvío del mismo archivo = respuesta del lote original,
sin tocar datos). Sin tabla de filas en v1 (el resumen y los errores se devuelven
en la vista previa; el lote guarda conteos y huella). Justificación en §6.

### 4.4 `cuentas_contables_uso` (para "cuenta con movimientos")
Mientras no existan pólizas, "movimientos" no puede salir de una tabla de pólizas.
Propuesta mínima: tabla `cuenta_id`, `empresa_id`, `consumidor` varchar(40),
`primer_uso_en`, `referencia` (opcional). Una fila ⇒ la cuenta se considera
**usada**. La escribe **solo Contabilidad** (comando interno
`RegistrarUsoCuentaCommand`, invocado por el módulo de pólizas/asientos futuro o por
el contrato contable); los consumidores nunca la escriben. En F1-CON-01 se usa para
las pruebas del bloqueo (§12) y queda lista para pólizas. Alternativa y pregunta en
§18-P4.

### 4.5 Sin outbox en v1
No hay consumidor de eventos del catálogo todavía (misma decisión que CeCo). Se
deja `PLATFORM-TODO(<OutboxContabilidad>)` y se agrega la
`integration_events_outbox` (ADR-0009) cuando Contabilidad emita eventos
(`CuentaContableDesactivadaEvent` etc. con naming `{Agregado}{Verbo}Event`).

---

## 5. Reglas de dominio

Todas viven en `CuentaContable` (invariantes de la entidad) o en un servicio de
dominio `CatalogoCuentasPolicy` (reglas que requieren leer el árbol). Errores:
`BusinessRuleException`(422) / `ConflictException`(409). Códigos en §8.4.

| # | Regla | Mecanismo |
|---|---|---|
| R1 | Código único por empresa | índice único + pre-chequeo → `CONTAB_CUENTA_CODIGO_DUPLICADO` 409 (condición de carrera cae en violación de índice, mapeada al mismo código) |
| R2 | Sin ciclos | al asignar/cambiar padre: recorrer ancestros del nuevo padre (CTE recursivo) y rechazar si aparece la propia cuenta o cualquier descendiente; autoreferencia incluida → `CONTAB_CUENTA_CICLO` |
| R3 | Padre válido | existe, misma empresa, **activo** y de tipo `Título` → `CONTAB_CUENTA_PADRE_INVALIDO` (no existe/otra empresa/inactivo) o `CONTAB_CUENTA_PADRE_NO_ES_TITULO` |
| R4 | Nivel calculado/validado | `nivel = padre.nivel + 1` (o 1); el cliente no lo envía; excede tope → `CONTAB_CUENTA_NIVEL_EXCEDIDO` |
| R5 | Naturaleza obligatoria; hijo hereda compatibilidad | validación de coherencia hijo-padre **solo si Contabilidad la confirma** (§18-P2); por defecto solo se exige valor válido |
| R6 | Cuenta de título no se selecciona contablemente | `Titulo` ⇒ el puerto de lectura responde `NoAfectable`; una afectable no puede tener hijos (se rechaza crear hijo bajo ella: R3) |
| R7 | Cuenta inactiva no acepta movimientos nuevos; conserva saldos e históricos | puerto responde `Inactiva`; baja no borra ni cambia hijos/uso/orígenes; reactivación permitida si el padre sigue activo |
| R8 | Cambios incompatibles bloqueados en cuentas usadas | si existe fila en `cuentas_contables_uso` (o descendientes con uso) → no se permite cambiar `naturaleza`, `padre_id` ni `tipo` por edición directa: `CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO` (422) con `detail` que explica: *"La cuenta {codigo} ya tiene movimientos; cambiar {campo} alteraría la interpretación de saldos históricos. Use el procedimiento de impacto (reclasificación aprobada por Contabilidad) o cree una cuenta nueva y desactive esta."* El histórico no se toca (la transacción falla antes de mutar). Cambios no sensibles (nombre, agrupación, `grupo_reporte`) siguen permitidos. **El "procedimiento de impacto explícito" NO se construye en F1-CON-01**: se documenta como contrato pendiente (§17/§18-P5) |
| R9 | Baja en cascada controlada | no se desactiva un título con hijos **activos** (`CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS`), a diferencia de ADR-0049 (cascada automática): para cuentas contables se bloquea para evitar bajas masivas silenciosas — **decisión a confirmar** (§18-P8) |
| R10 | Cuentas de control (clientes/proveedores) | `cuenta_control != Ninguna` solo en `Afectable`. El puerto exige que el movimiento declare su `OrigenMovimiento` (`AuxiliarCxC`, `AuxiliarCxP`, `Manual`, …): una cuenta de control rechaza `Manual` con `CONTAB_CUENTA_CONTROL_SOLO_AUXILIAR`; solo `AuxiliarCxC` para control Clientes y `AuxiliarCxP` para control Proveedores. Ningún formulario genérico de captura de movimientos existe en este alcance; el contrato queda en el puerto y se prueba. Qué cuentas son de control lo define Contabilidad (§18-P3) |
| R11 | Versión/concurrencia | `If-Match` con `Version`; mismatch → 409 `VERSION_CONFLICT`-estándar (ADR-0012), UI obliga a recargar |
| R12 | Aislamiento empresa | query filter + interceptor; `empresaId` jamás en body/query como autorización |

---

## 6. Importación (muestra con vista previa, idempotente)

### 6.1 Decisión de diseño (pieza nueva, justificada)
La ficha exige importación con vista previa, errores por fila, correspondencia y
reimportación idempotente. ADR-0044 rechazó una herramienta genérica para
proveedores/artículos, pero **aquí es requisito explícito de aceptación** y la
repetibilidad es regla del dueño; se construye el mínimo:
- **El parseo del archivo se hace en el cliente** (CSV UTF-8 y `.xlsx` con
  `exceljs`, ya dependencia) y se envía JSON de filas al servidor. Evita agregar
  un parser binario al backend y mantiene el dominio hexagonal. El servidor revalida
  todo (nunca confía en el cliente) y calcula la huella canónica.
- Dos operaciones: **vista previa** (sin escribir) y **aplicar** (transaccional).

### 6.2 Formato de muestra (CSV, `;` o `,` autodetectado; ficticio)
```
fuente,codigo_origen,codigo,nombre,codigo_padre,naturaleza,tipo,cuenta_control,codigo_agrupador,grupo_reporte
FIX-FUENTE,O-1,FIX-1000,Cuenta titulo ficticia,,Deudora,Titulo,Ninguna,,FIX-GRP-A
FIX-FUENTE,O-2,FIX-1000-01,Cuenta afectable ficticia 1,FIX-1000,Deudora,Afectable,Ninguna,,FIX-GRP-A
FIX-FUENTE,O-3,FIX-1000-02,Cuenta afectable ficticia 2,FIX-1000,Deudora,Afectable,Ninguna,,FIX-GRP-A
```
`FIX-*` marca datos no reales. El archivo oficial de Millet y su mapeo se documentan
en `docs/modulos/contabilidad/02-muestra-y-mapeo.md` cuando Contabilidad lo
entregue; hasta entonces el mapeo queda **pendiente** (§18-P1). Límite de lote
(p. ej. 5 000 filas) configurable.

### 6.3 Vista previa (`POST …/importaciones/vista-previa`)
Sin escritura. Para cada fila devuelve `{fila, accion, errores[]}` con
`accion ∈ {Crear, Actualizar, SinCambios, Rechazar}` y errores por fila con
`campo` y `codigo` (mismos códigos de §8.4 + `CONTAB_IMPORT_*`). Validaciones:
formato, duplicados **dentro del archivo** (código y `fuente+codigo_origen`), padre
resuelto dentro del archivo o ya existente, orden topológico (el padre puede estar
después en el archivo), ciclos del archivo + ciclos contra el árbol existente,
naturaleza/tipo válidos, nivel tope, acción sobre existente: si la cuenta existe y
el cambio es incompatible por uso (R8) → `Rechazar`. Devuelve resumen, `huella` y
`puedeAplicar` (= 0 rechazos).

### 6.4 Aplicar (`POST …/importaciones`)
Mismo cuerpo + `huella`. Dentro de **una sola transacción** de `ContabilidadDbContext`:
revalida; si hay cualquier error → `422 CONTAB_IMPORT_FILAS_CON_ERRORES` con
`errores[]` y **nada se escribe** (sin resultados parciales). Si la huella ya
fue aplicada (único `(empresa_id, huella)`) → responde `200` con el lote original y
`idempotente=true`, sin tocar datos. Resolución de identidad por orden:
1. correspondencia de origen `(fuente, codigo_origen)`; 2. `codigo` normalizado.
Misma identidad y datos iguales → `SinCambios`; datos distintos y no sensibles →
`Actualizar`; sensibles con uso → rechazo (R8). Es **upsert, no destructivo**: lo
ausente del archivo NO se borra ni desactiva (documentado; desactivar masivamente
queda fuera). Tras crear: inserta orígenes y el lote. Concurrencia de dos
importaciones simultáneas: el índice único de huella y de `(fuente, codigo_origen)`
hace que la segunda falle limpio y se reintente como idempotente (prueba, §12).
Requiere `Idempotency-Key` (ADR-0020).

**Aceptación cubierta:** importar 1 título + 2 afectables; reimportar el mismo
archivo → 0 nuevas, 3 `SinCambios` (o mismo lote); conteos de tabla idénticos.

---

## 7. Puerto de lectura para consumidores y cableado a stubs

### 7.1 Contrato (publicado por el dueño, patrón `IDim3ReadPort`)
`backend/src/Contabilidad/Application/PublicPorts/ICuentaContableReadPort.cs`:
```
Task<CuentaContableValidacion> ValidarParaMovimientoAsync(
    string codigoCuenta, OrigenMovimiento origen, CancellationToken ct);
Task<CuentaContableValidacion> ValidarParaMovimientoAsync(
    Guid cuentaId, OrigenMovimiento origen, CancellationToken ct);
Task<CuentaContableLectura?> ObtenerAsync(Guid cuentaId, CancellationToken ct);
```
`CuentaContableValidacion(Valida, Motivo?, Cuenta?)` con
`Motivo ∈ {NoExiste, Titulo, Inactiva, ControlSoloAuxiliar}` (mapeo 1:1 a los
códigos `CONTAB_CUENTA_*`). `CuentaContableLectura(Id, Codigo, Nombre, Naturaleza, Tipo, Activa, CuentaControl)`.
La empresa sale de `ICurrentEmpresaContext` (no es parámetro): una cuenta de otra
empresa responde `NoExiste`. Solo lectura; **cero escritura** del consumidor.
Adaptador en `Infrastructure/PublicAdapters/CuentaContableReadAdapter.cs`;
registro en `AddContabilidadModule()`.

### 7.2 Cableado a stubs existentes (mínimo; sin escribir en la base de Contabilidad)
- Los consumidores **no pueden referenciar `Millet.Contabilidad`** si genera ciclos
  (precedente: Almacén no referencia CeCo; adaptador hospedado en
  `Millet.Compras`/`Compartido`, ver `Program.cs` ~L650-690). Plan:
  - F1-CON-01 publica el puerto + adaptador y **lo registra en DI del Api**.
  - Cada consumidor define (cuando se toque su módulo) su puerto propio
    `I*CuentaContableReadPort` hexagonal y un adaptador delgado que delegue en el
    nuevo; **no se hace en esta tarea** salvo, opcionalmente, **un** consumidor
    piloto de bajo riesgo: `Tesoreria.CuentaBancaria.CuentaContableRef` validado en
    `CuentasCommands` (hoy string libre). Requiere puerto propio en Tesorería +
    transición (cuentas existentes con ref inválida se aceptan en lectura y se
    advierten, no se rompen). **Recomendación: NO incluirlo en F1-CON-01**
    (cambia contrato de Tesorería; otra tarea) y dejar `PLATFORM-TODO` + tabla §17.
- `IConceptoContableReadPort` (CxP, Almacén) y `IContabilidadAsientoPort`
  (Facturación): sin cambios; siguen `NoOp`. Se documenta en qué cambio futuro
  consumirán el nuevo puerto.
- Prueba de contrato: un test de integración inyecta `ICuentaContableReadPort`
  desde el contenedor real y verifica el matriz de §12.

---

## 8. API (diseño; endpoints NUEVOS, no implementados hoy)

Base `/api/v1/contabilidad/…`, `.RequireAuthorization()` + permiso por ruta, tags
`Contabilidad`. Convenciones del repo: ETag en GET por id; `If-Match` obligatorio en
PUT/PATCH/POST de estado (428/409); `Idempotency-Key` en POST; Problem Details.

### 8.1 Cuentas
| Método y ruta | Permiso | Respuestas |
|---|---|---|
| `GET /cuentas?estatus=&tipo=&q=&padreId=&offset=&limit=` | `contabilidad.catalogo.leer` | 200 `PagedResponse`; 401; 403 |
| `GET /cuentas/arbol?estatus=&raizId=` | leer | 200 árbol (nodos con `tieneHijos`, `nivel`); carga perezosa por `raizId` |
| `GET /cuentas/{id}` | leer | 200 + `ETag`; 404 |
| `POST /cuentas` (`Idempotency-Key`) | `contabilidad.catalogo.administrar` | 201 + ETag; 409 código dup; 422 reglas; 400 validación |
| `PUT /cuentas/{id}` (`If-Match`) | administrar | 200 + ETag; 404; 409 versión; 422 (R2–R8); 428 |
| `POST /cuentas/{id}/desactivar` (`If-Match`) | administrar | 200; 422 R9; 409; 428 |
| `POST /cuentas/{id}/reactivar` (`If-Match`) | administrar | 200; 422 padre inactivo |
| `POST /cuentas/validar-movimiento` | `contabilidad.catalogo.leer` | 200 `CuentaContableValidacion` (útil para UI de consumidores; mismo puerto) |

### 8.2 Importación
| `POST /importaciones/vista-previa` | `contabilidad.catalogo.importar` | 200 resumen+filas; 422 `CONTAB_IMPORT_*` formato |
|---|---|---|
| `POST /importaciones` (`Idempotency-Key`) | importar | 201 lote; 200 idempotente; 422 filas con errores (sin escritura); 409 conflicto concurrente |
| `GET /importaciones?offset=&limit=` | leer | 200 lotes |

### 8.3 Estados comunes
401 sin sesión; 403 `PERMISO_FALTANTE` (Problem Details existente de
`PermisoFaltanteResultHandler`); 404 inexistente **o de otra empresa** (no filtra
existencia); 409 duplicado/versión; 422 regla de negocio; 428 falta `If-Match`.

### 8.4 Códigos de error nuevos (se documentan en este directorio, `03-contrato-api.md`)
`CONTAB_CUENTA_NO_ENCONTRADA`(404), `CONTAB_CUENTA_CODIGO_INVALIDO`(422),
`CONTAB_CUENTA_NOMBRE_INVALIDO`(422), `CONTAB_CUENTA_CODIGO_DUPLICADO`(409),
`CONTAB_CUENTA_PADRE_INVALIDO`(422), `CONTAB_CUENTA_PADRE_NO_ES_TITULO`(422),
`CONTAB_CUENTA_CICLO`(422), `CONTAB_CUENTA_NIVEL_EXCEDIDO`(422),
`CONTAB_CUENTA_NATURALEZA_INVALIDA`(422), `CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO`(422),
`CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS`(422), `CONTAB_CUENTA_ES_TITULO`,
`CONTAB_CUENTA_INACTIVA`, `CONTAB_CUENTA_CONTROL_SOLO_AUXILIAR` (resultados del
puerto/`validar-movimiento`; 422 si se usan como error HTTP),
`CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE`(422),
`CONTAB_IMPORT_ARCHIVO_VACIO`(422), `CONTAB_IMPORT_LIMITE_FILAS`(422),
`CONTAB_IMPORT_FILAS_CON_ERRORES`(422, `errores[{fila,campo,codigo,mensaje}]`),
`CONTAB_IMPORT_HUELLA_NO_COINCIDE`(409: el cuerpo cambió entre vista previa y aplicar).
Versión: se reutiliza el error estándar de `ConcurrencyException`/ADR-0012.

---

## 9. Permisos canónicos

Namespace nuevo **`0000000d-*`** en `PermisosCanonicos.cs` (const + tupla + migración
`InsertData` en `Identidad`, patrón `…SeedPermisosDatosMaestrosProveedoresBancarios`).
Granularidad por operación (ADR-0041); lectura separada de modificación:

| Código | GUID propuesto | Uso |
|---|---|---|
| `contabilidad.catalogo.leer` | `0000000d-0001-0000-0000-000000000001` | listar, árbol, detalle, validar-movimiento, lotes |
| `contabilidad.catalogo.administrar` | `0000000d-0001-0000-0000-000000000002` | crear, editar, desactivar, reactivar |
| `contabilidad.catalogo.importar` | `0000000d-0001-0000-0000-000000000003` | vista previa y aplicar importación (separado: es masivo) |

No se crea permiso `…todas-sucursales` (ADR-0051 no aplica; §13). Frontend:
espejo en `frontend/src/lib/auth/permission-codes.ts` + test de paridad si existe.
Asignación a roles: **pendiente** (§18-P7; qué rol de Millet recibe cuáles). El
"usuario de consulta" = rol con solo `…leer`: prueba negativa 403 en
POST/PUT/importación.

---

## 10. UI (frontend)

Ubicación: `frontend/src/features/contabilidad/` (api, components, pages, lib,
schemas) — misma carpeta `features/` que `centros-costo`; rutas
`frontend/src/routes/_app/contabilidad/catalogo.tsx` (+ `catalogo.$id.tsx`,
`importacion.tsx`). Reuso: `ArbolCentrosCosto` como referencia del árbol,
`handle-conflict.ts` (extraer a compartido solo si no hay ya uno), patrón P1/P3/P4
de `patrones-compras.md` (master-detail + Sheet "Nueva cuenta"), `<ReporteShell>`
no aplica en v1.

- **Árbol/lista**: toggle árbol/lista; árbol perezoso por `raizId`; búsqueda por
  código/nombre con debounce 200 ms (topbar contextual); filtros estatus/tipo;
  insignias Título/Afectable, Inactiva, Control.
- **Detalle**: datos, ruta de ancestros, hijos, orígenes (correspondencia),
  "usada" (candado con explicación R8), historial (auditoría existente).
- **Edición**: inline form (border amber) con campos bloqueados y tooltip del
  motivo cuando la cuenta está usada. Baja lógica con confirm y mensaje de efectos.
- **Importación**: paso 1 elegir archivo (`.csv`/`.xlsx`, descarga de plantilla de
  muestra ficticia), paso 2 vista previa (tabla con acción/errores por fila, filtro
  "solo con errores", resumen), paso 3 aplicar (deshabilitado si hay errores),
  resultado (creadas/actualizadas/sin cambios, "idempotente: ya aplicado").
- **Estados visibles** (todos con prueba de componente): cargando (skeleton), vacío
  (CTA importar), guardando (botón bloqueado + spinner), guardado (toast **solo tras
  respuesta 2xx**; nada optimista), error de validación (campo + mensaje del
  Problem Details; **el formulario conserva los datos**), sin permiso (sin acciones
  de escritura renderizadas **y** la API igual devuelve 403), conflicto (diálogo
  "recargar" que refetch + conserva borrador para comparar; nunca sobrescribe),
  fallo recuperable (reintentar sin perder datos).
- **Permisos en UI**: ocultar botones es solo UX; la autorización es del API.
- Tests frontend: smoke de página, componentes de formulario (conservación de
  datos al 422), vista previa de importación, conflicto 409.

---

## 11. Migración y alta del DbContext

1. Nuevo proyecto `backend/src/Contabilidad/Millet.Contabilidad.csproj` (copia del
   csproj de CeCo) + agregar a `backend/Millet.sln` y `Millet.Api.csproj`.
2. `ContabilidadDbContext : BaseDbContext`, `SchemaName="contabilidad"`.
3. Migración inicial `…_ContabilidadCatalogoInicial` (`dotnet ef migrations add … --project src/Contabilidad --startup-project src/Api`),
   revisar el SQL generado (índices únicos, FK `RESTRICT`).
4. Migración de **Identidad** de seed de permisos (`…_SeedPermisosContabilidadCatalogo`).
5. Manifiesto: `tools/migration-contexts.txt` (línea
   `ContabilidadDbContext|src/Contabilidad/Millet.Contabilidad.csproj` al final, antes
   no hay dependencias entre contextos); `.github/workflows/deploy-app-dev.yml`
   (lista del `for`, `case` y comentario); `Program.cs`
   (`AddContabilidadModule()`, `AddDbContext`, `MigrationsHealthCheckOptions`,
   `MapContabilidadEndpoints`); revisar `tools/validate-*.sh/.ps1` y
   `docs/arquitectura.md`/README de infra por listas de contextos.
6. Sin datos sembrados (el catálogo real lo carga Contabilidad vía importación; no
   hay seed de cuentas — evita inventar valores de negocio).
7. Verificación: `tools/validate-local.sh` (migraciones + pruebas) y comprobar
   `/health/ready` con la migración aplicada.

---

## 12. Plan de pruebas (nominales y negativas) mapeado a criterios de aceptación

Infraestructura: proyecto `backend/tests/Contabilidad.UnitTests` (dominio y
handlers con DbContext en proveedor de prueba, patrón Tesorería) +
`backend/tests/Api.IntegrationTests/Contabilidad/*` (HTTP real, Postgres real,
`fake-login`, `CreateClientWithIdempotency`). Datos `FIX-*` con sufijo aleatorio y
limpieza (molde `CatalogoCrudTests`). Para varios usuarios/permisos: helper de
fixture que crea usuarios de prueba con permisos específicos (verificar si
`fake-login` permite elegir permisos; si no, ampliar el helper de pruebas dev
**sin tocar producción**).

| Criterio de aceptación | Prueba | Tipo |
|---|---|---|
| Importar 1 título + 2 afectables | `Importar_1_titulo_2_afectables_crea_3_cuentas_con_origen_y_niveles` (nivel 1,2,2; 3 filas origen; lote=1) | Int. HTTP |
| Segunda importación no duplica | `Reimportar_mismo_archivo_no_duplica` (conteos iguales, `idempotente=true`); `Reimportar_archivo_distinto_con_mismos_origenes_da_SinCambios` | Int. |
| Rechazar jerarquía cíclica | unit `Padre_descendiente_produce_CICLO`; HTTP PUT crea ciclo A→B→A ⇒ 422 `CONTAB_CUENTA_CICLO`; import con ciclo en archivo ⇒ fila rechazada y lote sin escritura | Unit + Int. |
| Rechazar código repetido | POST dup ⇒ 409 `CONTAB_CUENTA_CODIGO_DUPLICADO`; import con dup en archivo ⇒ error por fila; **carrera** dos POST paralelos ⇒ uno 201 y uno 409 | Int. |
| Consumidor acepta activa/afectable | `ReadPort_acepta_activa_afectable` | Int. (DI real) |
| Consumidor rechaza título | `…rechaza_titulo` (`Titulo`) | Int. |
| Consumidor rechaza inactiva | `…rechaza_inactiva` (`Inactiva`; sigue legible en `ObtenerAsync`) | Int. |
| Consumidor rechaza inexistente / de otra empresa | `…rechaza_inexistente`, `…rechaza_cuenta_de_otra_empresa` | Int. |
| Cuenta de control solo por auxiliar | `Control_clientes_rechaza_Manual_y_acepta_AuxiliarCxC` | Int. |
| Cambio incompatible sobre cuenta usada bloqueado sin alterar histórico | arrange: cuenta + fila en `cuentas_contables_uso`; PUT cambia `naturaleza`/`padre`/`tipo` ⇒ 422 `CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO` con motivo; assert: fila idéntica (misma versión, mismo `updated_at`), uso intacto, auditoría sin diff; PUT de nombre sí 200 | Int. |
| Usuario de consulta no modifica | token con solo `…leer`: GET 200; POST/PUT/desactivar/importar/vista previa ⇒ 403 `PERMISO_FALTANTE`; sin token 401 | Int. HTTP |
| Muestra y mapeo documentados | revisión: existe `02-muestra-y-mapeo.md`; la muestra de pruebas se carga desde ese fixture | Doc/Int. |
| Concurrencia (ficha) | PUT con `If-Match` viejo ⇒ 409; sin header ⇒ 428; el registro no cambia | Int. |
| Baja lógica no destruye | desactivar ⇒ sigue en detalle/orígenes/uso; hijos no se tocan; reactivar OK | Int. |
| Aislamiento multiempresa | con el contexto de empresa B: lista no ve cuentas de A; `GET /{idDeA}` ⇒ 404; PUT/desactivar ⇒ 404; mismo `codigo` permitido en empresa B; `empresaId` en body/query ignorado | Int. |
| Transaccionalidad import | lote de 3 filas con 1 inválida en la 3ª ⇒ 422 y 0 filas nuevas; fallo forzado a mitad (excepción en repo de pruebas) ⇒ rollback total | Int. |
| Idempotency-Key | repetir POST con misma clave ⇒ misma respuesta sin duplicado (cubre ADR-0020) | Int. |
| Vista previa no escribe | tablas sin cambios tras `vista-previa` | Int. |
| Permisos canónicos | test existente de catálogo de permisos/paridad front-back incluye los 3 nuevos; migración Up/Down | Unit/Int. |
| UI | smoke + estados (§10) + conservación de datos al rechazo + conflicto | Vitest |

Evidencia: tabla resultado esperado/obtenido (salida de `dotnet test` y `vitest`),
capturas de pantalla de árbol, vista previa con errores, conflicto y sin permiso.

---

## 13. Aislamiento multiempresa y sucursal

- `EmpresaId` técnico (ADR-0011): `IPerteneceAEmpresa` en las 4 tablas; filtro
  global y asignación por interceptor. Nunca se acepta `empresaId` del navegador.
  Un id válido de otra empresa ⇒ 404 (también en detalle, edición, baja,
  importación y descarga/exportación de plantilla).
- **Sucursal (ADR-0051): no aplica.** El catálogo es corporativo de Millet y
  "la sucursal no es otra entidad fiscal"; no se agrega `SucursalId` ni permiso de
  bypass por sucursal, y se documenta para evitar la duplicación por sucursal que
  la ficha prohíbe. Las agrupaciones de reporte (`grupo_reporte`) permiten agrupar
  sin duplicar. Si en el futuro un reporte necesita sucursal, vive en pólizas/
  movimientos, no en el catálogo.
- Todas las rutas (lista, árbol, detalle, mutación, importación) pasan por el mismo
  filtro de empresa y por la política de permiso.

---

## 14. Fases ordenadas, esfuerzo y commits propuestos

Esfuerzo en horas de construcción técnica. **Total estimado 34–37 h vs 18 h
provisionales: no cuadra** (se acerca a las 36 h históricas). Solo backend
(F0–F6 ≈ 27 h) tampoco entra en 18 h; ver §14.2.

| Fase | Alcance | Archivos principales | Depende de | Riesgos | h |
|---|---|---|---|---|---|
| F0 Contrato y ADR | ADR-0053/0054, `03-contrato-api.md` (rutas, permisos, códigos), muestra ficticia `02-muestra-y-mapeo.md` (estructura, mapeo pendiente) | `docs/decisiones/0053-*.md`, `docs/modulos/contabilidad/*` | respuestas §18 P1–P6 deseables | decisiones abiertas | 2 |
| F1 Esqueleto y persistencia | proyecto, entidades, configs, DbContext, migración inicial, alta en manifiesto/CI/Program/sln, health | `backend/src/Contabilidad/**`, `tools/migration-contexts.txt`, `deploy-app-dev.yml`, `Program.cs`, `Millet.Api.csproj`, `Millet.sln` | F0 | olvidar un punto del manifiesto; ciclos de referencia | 4 |
| F2 Permisos | 3 constantes + tuplas + migración seed Identidad + espejo frontend | `PermisosCanonicos.cs`, `Identidad/Infrastructure/Migrations/*`, `frontend/src/lib/auth/permission-codes.ts` | F1 (namespace) | **conflicto con cambios de otro responsable en `PermisosCanonicos.cs` (rama de permisos personalizados tiene ese archivo modificado)** — rebase y commit aislado | 2 |
| F3 Dominio y CRUD | `CuentaContable` + `CatalogoCuentasPolicy`, commands/queries/validadores, endpoints, ETag/If-Match, códigos de error | `Contabilidad/Domain`, `Application/Catalogo`, `Api/Endpoints/Contabilidad/*` | F1, F2 | rendimiento CTE; reglas R5/R9 sin definir | 6 |
| F4 Importación | vista previa + aplicar, huella, orígenes, lote, límites | `Application/Importacion/*`, endpoints | F3 | idempotencia concurrente; semántica de upsert | 5 |
| F5 Puerto y uso | `ICuentaContableReadPort` + adapter + `cuentas_contables_uso` + `RegistrarUso` interno | `Application/PublicPorts`, `Infrastructure/PublicAdapters` | F3 | definición de "movimiento" (§18-P4) | 3 |
| F6 Pruebas backend | unit + integración (matriz §12) | `tests/Contabilidad.UnitTests`, `tests/Api.IntegrationTests/Contabilidad/*` | F3–F5 | helper de usuarios por permiso; suite lenta | 5 (parcial, se escribe junto con F3–F5) |
| F7 UI | árbol/lista, detalle, edición, baja, importación, estados, tests | `frontend/src/features/contabilidad/**`, `routes/_app/contabilidad/*` | F3–F5 | volumen de estados | 8 |
| F8 Evidencia y cierre | ejecutar suite, capturas, tabla esperado/obtenido, doc de pendientes, PR | `docs/modulos/contabilidad/04-evidencia-f1-con-01.md` | todas | — | 2 |
| **Total** | | | | | **37** (con F6 5 h separadas); neto 34 h si F6 se solapa 3 h con F3–F5 |

### 14.1 Commits propuestos (pequeños, mensajes neutrales; sin push ni commit hasta aprobación)
1. `docs: contrato, ADR y muestra del catalogo contable (F1-CON-01)`
2. `chore: alta del proyecto y DbContext de Contabilidad (F1-CON-01)`
3. `feat: persistencia inicial del catalogo de cuentas (F1-CON-01)`
4. `feat: permisos canonicos del catalogo contable (F1-CON-01)`
5. `feat: comandos y consultas de cuentas contables (F1-CON-01)`
6. `feat: endpoints del catalogo contable (F1-CON-01)`
7. `feat: importacion con vista previa del catalogo (F1-CON-01)`
8. `feat: puerto de lectura de cuentas para consumidores (F1-CON-01)`
9. `test: pruebas del catalogo contable (F1-CON-01)`
10. `feat: pantalla de catalogo contable (F1-CON-01)`
11. `docs: evidencia de F1-CON-01`

### 14.2 Cómo acercarse a 18 h (si el dueño no amplía la estimación)
Orden de recorte, de menor a mayor impacto: (a) UI de importación en paso único
sin vista previa gráfica rica (-2); (b) omitir vista de árbol perezoso y usar lista
con indentación (-2); (c) diferir `cuentas_contables_uso`/R8 a la tarea de pólizas
**NO recomendado** porque es criterio de aceptación; (d) omitir `grupo_reporte` y
dejar solo `codigo_agrupador` (-0.5). Aun así ≈ 28 h. **Recomendación: confirmar
con el dueño ~32–36 h o dividir en F1-CON-01a (backend + permisos + pruebas, ≈ 27 h)
y F1-CON-01b (UI + evidencia, ≈ 10 h)**, sin recortar criterios de aceptación.

---

## 15. ADRs a escribir
- **ADR-0053 — Módulo Contabilidad y catálogo de cuentas**: nuevo módulo/esquema,
  `EmpresaId` conservado (divergencia de CeCo), baja lógica por estatus, código
  único por empresa incluyendo inactivas, sin sucursal (aplica/no aplica ADR-0051).
- **ADR-0054 — Cuenta usada, inmutabilidad y procedimiento de impacto**: definición
  de "usada" (`cuentas_contables_uso`), campos protegidos, contrato de cuentas de
  control, y que el procedimiento de reclasificación queda diferido con su
  contrato.
- (Opcional) **ADR-0055 — Importación de catálogo con huella e idempotencia**:
  si el dueño pide que sea patrón reutilizable (contrasta con ADR-0044).
Cada ADR lista consumidores (Facturación, CxP, Almacén, Tesorería) y su motivo.

---

## 16. Documentación y evidencia a entregar
- `docs/modulos/contabilidad/01-plan-catalogo-cuentas.md` (este), `02-muestra-y-mapeo.md`
  (muestra ficticia + tabla de mapeo contra el catálogo de Contabilidad: pendiente
  hasta recibir archivo oficial), `03-contrato-api.md` (rutas, permisos, códigos),
  `04-evidencia-f1-con-01.md` (esperado/obtenido, pruebas ejecutadas, capturas).
- Actualizar `CLAUDE.md`/`docs/modulos/README.md` con el módulo (solo si el owner
  lo aprueba; es cambio de documentación transversal).
- PR propio con ID de tarea; migraciones; datos de prueba no sensibles; registro de
  qué dependencia real sigue pendiente; "decisiones y pruebas reales de Millet se
  reportan por separado".

---

## 17. Dependencias de plataforma pendientes (ADR-0031)

| Pieza | Ticket | NoOp/stub en uso | Cómo se wirea |
|---|---|---|---|
| Archivo oficial del catálogo y equivalencias | (por definir, Guillermo/Contabilidad) | Muestra ficticia `FIX-*` | Importar archivo oficial con la misma pantalla |
| Pólizas/movimientos reales | (tarea futura) | `cuentas_contables_uso` registrado por comando interno | El módulo de pólizas llama `RegistrarUsoCuentaCommand` al afectar |
| Consumidores del puerto | `<ContabilidadCuentasConsumidores>` | `Tesoreria.CuentaContableRef` (string libre), CxP/Almacén `NoOpConceptoContableReadPort` | Puerto propio + adaptador por consumidor (checklist: Tesorería, CxP, Almacén, Facturación) |
| Procedimiento de impacto (reclasificación) | `<ContabilidadReclasificacion>` | Bloqueo 422 con mensaje | Flujo aprobado por Contabilidad con auditoría |
| Outbox de eventos del catálogo | `<OutboxContabilidad>` | Sin eventos | ADR-0009 cuando haya consumidor |
| Candado de período / asientos | `<PeriodoContableCerrado>`, `<ContabilidadAsientos>` | stubs existentes | Fuera de F1-CON-01 |

Cada stub nuevo lleva `// PLATFORM-TODO(<id>): …` (buscable con `rg PLATFORM-TODO`).

---

## 18. Preguntas abiertas / decisiones que requieren aprobación del dueño o de Contabilidad

Cada una con recomendación; ninguna se resuelve inventando un valor.

| # | Pregunta | Quién | Recomendación |
|---|---|---|---|
| P1 | **Archivo oficial del catálogo y equivalencias** (formato, hoja, encabezados, fuente SAP) y quién valida la muestra | Guillermo / Contabilidad | Pedir un extracto mínimo (≥1 título y 2 afectables reales, sin saldos) y la tabla de equivalencias de códigos SAP↔ERP. Mientras tanto trabajar con la muestra `FIX-*` y mantener `fuente`/`codigo_origen` genéricos |
| P2 | **Formato del código** (longitud, separadores, segmentos), niveles máximos y **naturaleza** (¿hereda del padre? ¿hay cuentas de naturaleza mixta o acreedora de activo/ contra-cuentas?) | Contabilidad | Código varchar(30) texto libre validado por patrón configurable; nivel máx 10; naturaleza obligatoria por cuenta **sin** regla de herencia hasta que Contabilidad la confirme (R5 desactivada) |
| P3 | **Cuentas de control** de clientes y proveedores: ¿cuáles son? ¿una por moneda/sucursal? ¿qué orígenes pueden afectarlas (auxiliares CxC/CxP, ajustes de cierre)? | Contabilidad + CxC/CxP | Enum `Ninguna/Clientes/Proveedores` + validación por `OrigenMovimiento`; los orígenes permitidos viven en configuración documentada, no hardcode adicional; esperar lista oficial |
| P4 | **¿Qué es "cuenta con movimientos" mientras no existan pólizas?** | Dueño + Contabilidad | Tabla `cuentas_contables_uso` escrita solo por Contabilidad (comando interno) y simulada en pruebas; considerar también "usada" si el consumidor la **referencia** (Tesorería `CuentaContableRef`, conceptos) — recomendado diferir: documentado como límite hasta wiring de consumidores |
| P5 | **Procedimiento de impacto explícito**: ¿quién aprueba, qué evidencia, se permite reclasificar? | Contabilidad | En esta tarea solo bloquear y explicar; diseñar el flujo (solicitud → aprobación → alta de cuenta nueva + baja de la vieja, sin tocar histórico) en ADR-0054 como trabajo futuro |
| P6 | **Agrupaciones**: ¿qué referencias de agrupación necesitan los reportes (código agrupador SAT, grupo de estado financiero, rubro)? ¿un nivel o varios? | Contabilidad | Dos campos opcionales de texto (`codigo_agrupador`, `grupo_reporte`) sin catálogo, hasta definir; no crear tabla de agrupaciones sin definición |
| P7 | **Permisos exactos y roles**: ¿están bien `leer/administrar/importar`? ¿qué rol de Millet los recibe? ¿existe rol "consulta"? | Dueño (Eduardo) | Mantener los 3; asignar en el seed solo a roles definidos por el dueño; "consulta" = solo `leer` |
| P8 | Baja de un título con hijos activos: ¿bloquear (R9) o cascada como ADR-0049? | Contabilidad | Bloquear (más seguro para contabilidad); revisar con el dueño |
| P9 | ¿Un código de una cuenta dada de baja puede reutilizarse? | Contabilidad | No: único incluyendo inactivas (preserva histórico) |
| P10 | ¿Formato de importación: solo CSV o también `.xlsx`? ¿Tamaño máximo? ¿La importación puede desactivar cuentas ausentes? | Contabilidad | CSV y xlsx con parseo en cliente; límite 5 000 filas; **no** desactivar ausentes (upsert no destructivo) |
| P11 | Estimación de horas (18 vs 34–37) y posible división a/b (§14.2) | Dueño | Dividir 01a/01b o ampliar |
| P12 | ¿Ubicar el módulo en `features/contabilidad` (patrón CeCo) o `modules/` (patrón Administración/Identidad)? ¿Contexto empresa: una Millet? | Dueño | `features/` (el más reciente); confirmar que `EmpresaId` es el único contexto |
| P13 | Piloto de consumidor (Tesorería `CuentaContableRef`) dentro de F1-CON-01 o aparte | Dueño | Aparte (cambia contrato de otro módulo) |

---

## 19. Riesgos transversales
- **Choque en `PermisosCanonicos.cs`/snapshot de Identidad** con el trabajo de otro
  responsable (permisos personalizados, archivos modificados en el repo original):
  commits aislados, rebase explícito, no mezclar cambios.
- **Migración de Identidad** ordenada por timestamp: crearla con fecha posterior a
  la más reciente de `main` (`20260928230813…`) y verificar que no pisa la de la
  otra rama (`20261001174838_AgregarPermisosOverridePorUsuario` existe sin
  versionar en el otro árbol).
- Reglas aún sin definir de negocio (P2–P6) pueden forzar retrabajo: construir con
  configuración mínima y sin valores por defecto que cambien el negocio.
