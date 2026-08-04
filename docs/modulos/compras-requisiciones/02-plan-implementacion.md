# Plan de implementación — Submódulo Requisiciones (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md)
> (diseño v1 con asunciones cerradas excepto A1).
>
> **Estado:** propuesta de plan para revisión con el equipo. Sizing en
> bandas (XS/S/M/L/XL) — calibrar contra capacidad real del equipo.
>
> **Fecha:** 2026-05-07.

---

## 0. Cómo leer

- Sizing en bandas:
  - **XS** ≈ 1–2 días
  - **S** ≈ 3–5 días
  - **M** ≈ 1–2 semanas
  - **L** ≈ 3–4 semanas
  - **XL** > 1 mes
- Cada fase produce algo **deployable y validable** — no son entregables
  internos del equipo.
- Las fases son secuenciales por dependencia técnica, pero dentro de
  cada fase hay paralelismo posible (anotado como "‖").

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 del submódulo Requisiciones del módulo Compras
del nuevo ERP, alineado con el diseño aprobado.

**Estrategia:**

1. **Walking skeleton** primero (crear y leer una RQ trivial),
   antes que las features ricas.
2. **Stubs cross-module** para Almacén y OC mientras esos módulos no
   existen — el contrato vive en el repo aunque la implementación real
   no. Esto desbloquea Compras sin esperar.
3. **Iterativo**: cada fase suma capacidades sobre el agregado. Nada de
   "fase grande de modelo y luego endpoints".
4. **A1 (matriz de aprobación)** se incorpora en una fase tardía,
   reemplazando un placeholder simple. El resto del módulo no la
   bloquea.

**Pendientes que NO bloquean arranque:**
- Refinamiento de A1 (cuántas naturalezas, "aprobador adicional" para
  Riesgo, suplencias, etc.) — la estructura del motor está clara, los
  detalles se afinan en Fase 9.
- Mapeo de los 26 derechos legacy → permisos provisionales mientras se
  trabaja con el cliente; mapeo final antes de release.
- Coordinación con módulo OC para la migración de `PEDIDOS` legacy
  (Fase 7).

---

## 2. Prerrequisitos — audit del repo (2026-05-07)

Auditado contra `c:\Users\UserSP\Desktop\Project_Millet_ERP\backend\`.
Estado real:

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| Esqueleto del monolito modular (.NET 9, hexagonal) | ✅ existe | `Millet.sln` con `SharedKernel`, `Identidad`, `Api`. Compras se agrega siguiendo el patrón. |
| **`BaseEntity`** con `Version` `IsConcurrencyToken` (ADR 0012 Capa 1) | ✅ existe | `backend/src/SharedKernel/Domain/BaseEntity.cs`. Incluye `Id` (Guid v7), `Version`, `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`, `DeletedAt`. |
| **`BaseDbContext`** con interceptors (ADR 0008) | ✅ existe | `Audit`, `EmpresaContext`, `Metadata` SaveChanges interceptors ya configurados. |
| EF Core + migraciones por esquema (ADR 0005) | ✅ existe | Patrón confirmado: `Identidad.Infrastructure.Migrations/` separado. Compras hará lo mismo. |
| Identidad + RBAC granular (ADR 0003, 0007) | ✅ existe | `Usuario`, `Rol`, `Permiso`, `RolPermiso`, `UsuarioEmpresaRol`, `PermisosCanonicos`, `IPermissionLoader`. |
| Auth Entra ID + JWT (ADR 0003) | ✅ existe | `EntraTokenValidator`, `LoginOrchestrator`, `AuthEndpoints`, `JwtTokenService`, `EntraIdOptions`. |
| `RequirePermissionAttribute` para policies | ✅ existe | `Api/Auth/RequirePermissionAttribute.cs` + `PermissionAuthorizationHandler`. |
| Problem Details (ADR 0010) | ✅ existe | `Api/Web/GlobalExceptionHandler.cs`. |
| `Money`, `Moneda`, `Empresa` (ADR 0014, 0011) | ✅ existe | `SharedKernel/Domain/Money.cs`, `Moneda.cs`, `Empresa.cs`. |
| Soft delete vía `IFiscalmenteRelevante` (ADR 0008) | ✅ existe | Marker interface + filtro en `BaseDbContext`. |
| Health checks (ADR 0019) | ✅ existe | `MigrationsAppliedHealthCheck`, `HealthCheckResponseWriter`. |
| Logging Serilog + masking (ADR 0006) | ✅ existe | `MaskSensitivePropertiesEnricher`. |
| `IClock`, `ICurrentUserContext`, `ICurrentEmpresaContext` | ✅ existe | abstracciones listas para inyectar. |
| **`CollaborationHub` SignalR (ADR 0012 Capa 2)** | ⏳ pendiente | Solo mencionado en `backend/README.md`, sin implementación. Plan B: en Fase 2 implementamos Capa 1 (que ya existe) y dejamos un stub `NoOp` para Capa 2 hasta que el hub se construya. **No bloquea**: la protección de datos vive en Capa 1. |
| **Outbox + Service Bus (ADR 0009)** | ⏳ pendiente | Solo en README. Plan B: `IIntegrationEventPublisher` `NoOp` en Fase 1; eventos in-process funcionan vía MediatR `INotification`. Se conecta a Service Bus en Fase 6 cuando la infraestructura exista. |
| Módulos Almacén, Compras-OC, Contabilidad, Notificaciones | ⏳ no existen | Compras es probablemente el primer módulo de negocio. Plan B: stubs cross-module en Fase 3 (ya estaba en el plan). |
| OpenAPI + tipos TypeScript (ADR 0017) | **[Verificar]** | revisar si `Program.cs` ya genera OpenAPI; si no, S/M para añadir. |
| MediatR / FluentValidation / Mapster wiring | **[Verificar]** | revisar `Program.cs` — paquetes en `Directory.Packages.props`. Si no están, agregar (XS). |

**Conclusión del audit: no hay bloqueantes para arrancar Fase 0.** El
framework cubre los prerrequisitos críticos. Las dos brechas
(`CollaborationHub`, Outbox) son habilitadores transversales del ERP
que se construyen en otros tracks; Compras los consume cuando estén,
mientras tanto opera con `NoOp` / Capa 1 únicamente.

> **Tracking del debt** (ADR-0031): los dos `NoOp` se documentan en
> la sección **§8.6 "Dependencias de plataforma pendientes"** del
> documento de diseño y llevan comentarios `PLATFORM-TODO(<ticket>)`
> en código. Cuando los tickets de plataforma cierren, abrir PRs de
> wireup, reemplazar `NoOp`, borrar comentarios y actualizar la
> tabla del diseño.

---

## 3. Dependencias con otros módulos

| Módulo | Necesidad | Estado | Plan |
|---|---|---|---|
| **Identidad** | usuarios, roles, claims | depende del bootstrap | **prerrequisito** |
| **Almacén de no-producción** | consultar stock, reservar, generar mov. salida, liberar reserva | probablemente no existe aún | **stubs en repo Compras** mientras tanto (ver §4 Fase 3) |
| **Compras / Órdenes de Compra** (mismo BC) | generar OC borrador, recibir notif de recepción | probablemente no existe aún | **stubs in-proc** + tabla puente provisional |
| **Contabilidad** | catálogo cuentas y centros de costo (read-only) | probablemente no existe aún | tabla seed con catálogo importado de SAP, sin módulo formal en v1 |
| **Notificaciones** | enviar emails por evento | depende de ADR 0026 | si no existe, los eventos se publican al Outbox y se consumen cuando Notificaciones esté listo |
| **A+W** | catálogo de proveedores no-prod (export inicial) | externo, depende del cliente | export one-shot con script que el cliente ejecuta |

---

## 4. Fases

### Fase 0 — Foundation (S)

Crear el esqueleto del módulo Compras. **Sin features funcionales.**

- [ ] Crear proyecto `Millet.Compras` con layout hexagonal
      (`Domain/`, `Application/`, `Infrastructure/`) siguiendo el
      patrón de `Identidad`.
- [ ] Agregar a `Millet.sln` y referenciar `SharedKernel`.
- [ ] `ComprasDbContext` que herede de `BaseDbContext`, esquema
      `compras`, migración inicial vacía.
- [ ] Verificar/agregar wiring en `Program.cs` para Compras:
      registrar handlers de MediatR, validators de FluentValidation,
      profiles de Mapster (alineado con cómo lo hace Identidad).
- [ ] Health check `compras/health` (ADR 0019) — extender el
      `MigrationsAppliedHealthCheck` existente para Compras.
- [ ] CI/CD: build + tests del módulo en pipeline (ADR 0029) — añadir
      el proyecto al workflow existente.

**Criterio de aceptación:** el módulo arranca, el endpoint `/health`
responde 200, las migraciones corren sin error.

---

### Fase 0.b — Permisos canónicos de Compras (XS)

‖ paralelizable a Fase 1.

- [ ] Agregar las constantes de permisos a
      `Identidad/Domain/PermisosCanonicos.cs`:
  - `compras:requisiciones:crear`
  - `compras:requisiciones:editar`
  - `compras:requisiciones:eliminar`
  - `compras:requisiciones:cancelar`
  - `compras:requisiciones:autorizar:nivel1`
  - `compras:requisiciones:autorizar:nivel2`
  - `compras:requisiciones:rechazar`
  - `compras:requisiciones:editar-de-otros-usuarios`
  - `compras:requisiciones:seleccionar-requisitante`
  - `compras:requisiciones:ver-todos-departamentos`
- [ ] Migración para insertar los permisos en `identidad.permisos`.
- [ ] Documentar en CLAUDE.md / README cómo se asignan a roles.
- [ ] Coordinar con cliente la asignación inicial a roles existentes.

> Los roles `JefeAlmacen`, `JefeDepartamento`, `AutorizadorN2` que
> surgen de A1 se materializan después (Fase 9), cuando el cliente
> confirme la matriz multidimensional.

**Criterio de aceptación:** los permisos están registrados en BD; un
endpoint de prueba con `[RequirePermission("compras:requisiciones:crear")]`
devuelve 403 a un usuario sin el permiso y 200 al que sí lo tiene.

---

### Fase 1 — Walking skeleton (S)

Crear y leer una RQ trivial. Sin autorización, sin líneas ricas, sin
nada. Solo prueba que el plumbing funciona end-to-end.

- [ ] Agregado `Requisicion` con campos mínimos y estado `Borrador`.
- [ ] Tabla `compras.requisiciones` (sin `requisicion_lineas` aún).
- [ ] `CrearRequisicionCommand` + handler + validator.
- [ ] `ObtenerRequisicionPorIdQuery` + handler.
- [ ] Endpoints `POST /api/v1/compras/requisiciones` y `GET .../{id}`.
- [ ] Tests unitarios del agregado.
- [ ] Tests de integración happy path (POST → GET).

**Criterio de aceptación:** `curl POST` crea una RQ, `curl GET` la
recupera con su `requisicion_id`.

---

### Fase 2 — Líneas y workflow básico (M)

Agregar líneas, transmitir a autorización, autorizar/rechazar/eliminar.
**Sin bifurcación todavía** — la autorización deja la RQ en `Autorizada`
sin tocar Almacén ni OC.

‖ paralelizable: el frontend puede empezar las pantallas en paralelo.

- [ ] Entidad `LineaRequisicion` + tabla `compras.requisicion_lineas`.
- [ ] Value object `Cubrimiento` (con cantidades a cero).
- [ ] Catálogo `compras.motivos_rechazo` con seed (§3.bis.3).
- [ ] Comandos `AgregarLineaCommand`, `ActualizarLineaCommand`,
      `EliminarLineaCommand`, `ActualizarNotasLineaCommand`.
- [ ] `EnviarAAutorizacionCommand` → estado `EnAutorizacion`.
- [ ] Entidad `Autorizacion` + tabla `compras.requisicion_autorizaciones`.
- [ ] `AutorizarRequisicionCommand` con **motor de matriz simplificada
      v0**: solo capa de monto por departamento. Naturaleza y reglas
      finas se enchufan en Fase 9 sobre la misma interface.
- [ ] `RechazarRequisicionCommand` con `motivoId` + `motivoTexto`
      opcional → estado `Rechazada`.
- [ ] `EliminarRequisicionCommand` con `motivoId` → estado `Eliminada`.
- [ ] Tabla `compras.umbrales_aprobacion_departamento` con seed inicial.
- [ ] Endpoints REST correspondientes (§9 del diseño).
- [ ] **Concurrencia (ADR 0012)**:
  - Capa 1: `Requisicion` hereda de `BaseEntity` (`Version` int como
    `IsConcurrencyToken`). EF Core la incrementa automáticamente.
  - Capa 2: declarar `Requisicion` en la lista de entidades con soft
    lock del módulo Compras. La infraestructura (CollaborationHub,
    `useCollaboration` hook) la provee el ERP — no requiere código en
    este módulo más allá de la declaración.
- [ ] Validators FluentValidation completos.
- [ ] Tests unitarios + integración.

**Criterio de aceptación:** captura → transmisión → autorización N1+N2
→ estado `Autorizada` (sin movimiento ni OC todavía). Rechazo y
eliminación funcionan con catálogo de motivos. Notas editables en
cualquier estado no terminal. Soft lock muestra "X está editando" en
el frontend cuando dos usuarios abren la misma RQ. Conflicto al guardar
retorna 409 con resolución vía componente compartido.

---

### Fase 3 — Contratos y stubs cross-module (M)

Definir los puertos hexagonales y crear stubs para Almacén y OC.
**Permite avanzar sin esperar a esos módulos.**

‖ paralelizable: equipo de Almacén puede ir leyendo el contrato.

- [ ] Definir interfaces de puerto (en `Millet.Compras.Domain.Ports`):
  - `IConsultarStockPort`
  - `IReservarStockPort` (devuelve `ReservaId`, acepta `TimeSpan ttl`)
  - `ILiberarReservaPort`
  - `IGenerarMovimientoSalidaPort`
  - `IGenerarSolicitudCompraPort`
- [ ] Stub `InMemoryConsultarStockPort` configurable (devuelve N% de
      disponibilidad por artículo según appsettings).
- [ ] Stub `InMemoryReservarStockPort` (devuelve `ReservaId` random,
      no persiste real).
- [ ] Stub `InMemoryGenerarSolicitudCompraPort` (escribe a tabla
      provisional `compras.oc_borrador_stub`).
- [ ] Inyectar stubs vía DI con flag `Compras:UseStubs=true` en config.
- [ ] Tests que validan el flujo completo con stubs.

**Criterio de aceptación:** el contrato compila, los stubs funcionan,
el equipo de Almacén tiene los `.cs` de las interfaces para empezar
diseño paralelo.

---

### Fase 4 — Bifurcación stock-aware (M)

El handler de autorización ahora invoca los puertos. La RQ pasa a
`EnSurtido` con cubrimiento real (vía stubs).

- [ ] Extender `AutorizarRequisicionCommand` handler:
  - Tras matriz satisfecha, llamar `IConsultarStockPort` por línea.
  - Llamar `IReservarStockPort` con TTL 14d (asunción A2).
  - Llamar `IGenerarMovimientoSalidaPort` para lo cubierto.
  - Llamar `IGenerarSolicitudCompraPort` para el saldo.
  - Persistir `Cubrimiento` en cada línea.
  - Transicionar a `Autorizada` (todo cubierto con stock) o `EnSurtido`
    (hay saldo de compra).
  - Toda la operación en una transacción de aplicación; si algo falla,
    falla todo.
- [ ] `CancelarRequisicionCommand` → estado `Cancelada`. Llama
      `ILiberarReservaPort` y aborta OC borrador.
- [ ] Tests con stubs simulando: stock total, stock parcial, stock
      cero, fallo de Almacén, fallo de OC.

**Criterio de aceptación:** una RQ con líneas mixtas pasa por la
bifurcación; las líneas con stock generan movimiento (en el stub), las
sin stock generan OC borrador (en el stub). Cancelación libera todo.

---

### Fase 5 — Recepción y cierre (S)

OC notifica recepción de material. La RQ avanza a `Cerrada`.

- [ ] `RegistrarRecepcionCommand` (invocado por handler de evento de
      OC, in-proc).
- [ ] Lógica: actualizar `Cubrimiento.CantidadRecibida`. Si todas las
      líneas cierran → estado `Cerrada` + evento
      `RequisicionCerradaEvent`.
- [ ] Evento `SaldoNoSurtidoEvent` cuando OC se cierra con saldo no
      entregado (informativo, A12).
- [ ] Tests del ciclo completo: capturar → autorizar → recibir → cerrar.

**Criterio de aceptación:** el ciclo completo de una RQ termina en
`Cerrada` con stubs simulando recepciones.

---

### Fase 6 — Eventos de integración + Notificaciones (S)

Conectar el Outbox y Service Bus. Los consumidores externos
(Notificaciones, BI) reciben eventos.

- [ ] `IIntegrationEventPublisher` real (no NoOp) con Outbox + Service
      Bus (ADR 0009).
- [ ] Publicar:
  - `compras.requisicion.autorizada.v1`
  - `compras.requisicion.rechazada.v1`
  - `compras.requisicion.eliminada.v1`
  - `compras.requisicion.cancelada.v1`
  - `compras.requisicion.cerrada.v1`
  - `compras.requisicion.saldoNoSurtido.v1`
- [ ] Coordinar con módulo Notificaciones para crear plantillas de los
      eventos relevantes (ADR 0026).
- [ ] Tests E2E con un consumer dummy verificando la publicación.

**Criterio de aceptación:** los eventos llegan al Service Bus en el
ambiente de dev. Los consumers (cuando existan) los reciben en el orden
correcto.

---

### Fase 7 — Catálogos reales y migración (L)

Reemplazar los stubs y datos seed por catálogos reales **+** ejecutar
la migración de RQs vivas (RA8).

‖ paralelizable: el cliente prepara los exports de SAP y el mapping
de IDs en paralelo a las fases anteriores.

**Catálogos:**
- [ ] Tabla `compras.proveedores` (estructura mínima v1).
- [ ] Tabla `compras.articulos` (estructura mínima v1, **incluyendo
      atributo `Naturaleza`**).
- [ ] Script de import desde SAP (proveedores + artículos, §11.1).
- [ ] Importar prioridades y derechos del legacy (RA9).
- [ ] Cargar departamentos, sucursales, almacenes, clasificaciones
      desde el ERP nuevo (no del legacy, RA9).
- [ ] Cleanup de tablas provisionales (stubs).

**Migración de RQs vivas (§11.3.b del diseño):**
- [ ] Coordinar con módulo OC para migración conjunta de `PEDIDOS`
      legacy.
- [ ] Tabla puente `migracion.legacy_map` (departamento, sucursal,
      almacén, usuario, artículo, proveedor).
- [ ] El cliente provee el mapping de IDs (`legacy → ERP nuevo`) para
      departamentos, sucursales, almacenes.
- [ ] Script T-SQL de selección: RQs con OC pendiente de recepción.
- [ ] Script de migración con mapeo de estados:
      Legacy 9/10/7/8/5(con compra) → ERP `EnSurtido`.
- [ ] Reconstruir `Cubrimiento` desde `UNIDADES_TRANSMITIDAS`,
      `UNIDADES_MOV_INVENTARIO`, y datos de SAP (recepciones).
- [ ] Migrar autorizaciones (descartando las de tipo Saldo legacy, ya
      que A12 las eliminó conceptualmente).
- [ ] Validación post-migración: spot-check de N RQs migradas
      (cabecera, líneas, cubrimiento, autorizaciones, FK al pedido OC).
- [ ] Configurar el portal legacy en modo read-only.

**Criterio de aceptación:** sin stubs en producción. Catálogos reales
sostienen el flujo. Las RQs migradas se abren correctamente, se les
puede registrar recepción, y al cerrar quedan en estado `Cerrada`.

---

### Fase 8 — Hardening y polish (M)

Lo que falta para release.

- [ ] Tests integration coverage > 80% del flujo principal.
- [ ] Idempotencia HTTP (ADR 0020) verificada en todos los `POST` de
      mutación.
- [ ] Problem Details en todas las rutas de error (ADR 0010).
- [ ] Observabilidad: traces distribuidos (Serilog → App Insights, ADR
      0006).
- [ ] Performance: medir P50/P95 de queries de bandeja con dataset
      seed de 10k RQs.
- [ ] Documentar OpenAPI con descripciones (ADR 0017).
- [ ] UAT con un grupo piloto del cliente.
- [ ] Documentación de operación (runbook).

**Criterio de aceptación:** el cliente firma el UAT. Listo para
release a un piloto.

---

### Fase 9 — A1 multidimensional + RBAC final (M)

Reemplazar el motor v0 (Fase 2) por la matriz de 3 capas (RA1).

- [ ] Tabla `compras.aprobadores_departamento` (rol JefeDpto,
      JefeAlmacen, AutorizadorN2).
- [ ] Confirmar con cliente las preguntas abiertas de §3.bis.2:
      cuántas naturalezas, "aprobador adicional" para Riesgo,
      suplencias, auto-aprobación, etc.
- [ ] Implementar `IRequiereNivelEvaluator`:
      capa 1 (departamental) + capa 2 (monto vs umbral) + capa 3
      (naturaleza más restrictiva eleva el nivel).
- [ ] Implementar `IResolverAutorizadorPort`:
      N1 = jefe almacén o jefe departamento según naturaleza;
      N2 = autorizador de sucursal.
- [ ] Reemplazar el placeholder de Fase 2 por el motor real.
- [ ] Mapeo final de los 26 derechos legacy a la lista RBAC del ERP
      (ADR 0007), incluyendo nuevos roles `JefeAlmacen`,
      `JefeDepartamento`, `AutorizadorN2`. Sesión con cliente.
- [ ] Tests con casos del cliente para cada combinación
      (naturaleza × monto × departamento).

**Criterio de aceptación:** la matriz multidimensional del cliente se
respeta. Los permisos del legacy están mapeados 1:1 (o documentadamente
fusionados). Tests pasan para los casos representativos.

---

### Fase 10 (post-v1) — Items diferidos

- **A11 / Adjuntos** (v1.1): modelo `Adjunto` con blob storage (ADR
  0024).
- **A13 / Bulk operations**: endpoint y UI para aprobar varias RQs.
- **A14 alternativo**: vistas materializadas si las queries de bandeja
  se degradan.

---

## 5. Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| Almacén / OC tardan más de lo esperado | media | alto | Stubs desde Fase 3; el módulo Compras no se bloquea. |
| Refinamiento de A1 no llega a tiempo | media | medio | Motor v0 (solo monto por departamento) en Fase 2. Motor multidimensional en Fase 9 sobre la misma interface. |
| Export de proveedores/artículos desde SAP es complejo | media | alto | Pedir el export en cuanto se inicia Fase 0. Pedir explícitamente el atributo `Naturaleza` por artículo. Si llega tarde, datos seed pequeños para no bloquear desarrollo. |
| **Migración de RQs vivas (RA8) más compleja de lo esperado** | media | alto | Coordinar con módulo OC desde Fase 3. El cliente provee mapping de IDs. Validación spot-check post-cutover. |
| El cliente no entrega clasificación `Naturaleza` por artículo | media | alto | Default `Estandar` para todos; el cliente reclasifica vía pantalla de catálogo en post-go-live. |
| Performance de queries con concurrencia | baja | medio | Medir en Fase 8 con seed de 10k RQs. Plan B: vistas materializadas. |
| Conflictos de versión optimista en bandeja del autorizador | baja | bajo | UX: refrescar y reintentar; mensaje claro 412. Si A6 = híbrido, el bloqueo previene la mayoría de conflictos. |
| Cambios en el diseño durante implementación | alta | medio | El diseño está sellado (Rev. 9). Cambios se anotan como "Rev. 10+" y se evalúan en backlog. |

---

## 6. Sizing total y dependencias

| Fase | Sizing | Bloquea a |
|---|---|---|
| 0 — Foundation | S | Todas |
| 0.b — Permisos canónicos | XS | 2 (parcial) |
| 1 — Walking skeleton | S | 2, 3 |
| 2 — Líneas y workflow básico | M | 4 |
| 3 — Contratos y stubs | M | 4, 6 |
| 4 — Bifurcación stock-aware | M | 5, 7 |
| 5 — Recepción y cierre | S | 8 |
| 6 — Eventos + Notificaciones | S | 8 |
| 7 — Catálogos + migración RQs vivas | L | 8 |
| 8 — Hardening | M | release v1 |
| 9 — A1 multidimensional + RBAC | M | release v1 (entra antes de UAT) |
| 10 — Diferidos | — | post-v1 |

**Camino crítico hasta release v1:** 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 9 → 8 ≈ **4 a 5 meses con 2 desarrolladores backend**, asumiendo:
- Prerrequisitos listos (§2) — incluye `BaseEntity` y
  `CollaborationHub` del ADR 0012 ya disponibles en el framework
- Refinamiento de A1 disponible antes de Fase 9
- Coordinación con módulo OC desde Fase 3

La Fase 7 creció de M a L por el alcance ampliado (migración de RQs
vivas, no solo catálogos). Esto añade ~2-3 semanas vs el plan original.

---

## 7. Decisiones operativas pendientes

- [ ] Validar prerrequisitos de §2 con el lead del proyecto.
- [ ] Confirmar que los módulos Almacén, OC y Contabilidad no van a
      estar listos antes que Compras (caso típico) y por tanto los
      stubs son necesarios.
- [ ] Coordinar con el cliente el envío del export de SAP
      (proveedores + artículos) idealmente en Fase 0–2.
- [ ] Pedir al cliente la matriz de aprobación (A1) cuando esté
      disponible — empuja Fase 9.
- [ ] Acordar con el equipo de frontend cuándo arranca su trabajo
      (puede iniciar en Fase 1 una vez que existan los primeros
      endpoints estables).

---

## 8. Cambios respecto a versiones previas

### Rev. 5 — Tracking de deuda de plataforma (ADR-0031) (2026-05-07)

- Referencia explícita a la sección §8.6 del diseño y al patrón
  `PLATFORM-TODO(<ticket>)` para los `NoOp` que se introduzcan en
  Fase 1 (Outbox `NoOp`) y Fase 2 (CollaborationHub `NoOp`).
- Cuando los tickets de plataforma cierren (durante o después de
  Fase 6 / Fase 8), los wireups son parte del cierre del ticket, no
  del scope del módulo Compras.

### Rev. 4 — audit del repo y Fase 0.b (2026-05-07)

Auditado el estado real del backend en
`c:\Users\UserSP\Desktop\Project_Millet_ERP\backend\`:

- **§2 reescrita**: 13 prerrequisitos confirmados como ya existentes
  en `SharedKernel`, `Identidad` y `Api` (BaseEntity con concurrency
  token, BaseDbContext con interceptors, Money, RBAC, Entra ID,
  Problem Details, etc.).
- **2 brechas reales** identificadas: `CollaborationHub` SignalR
  (ADR 0012 Capa 2) y Outbox/Service Bus (ADR 0009). Ambas son
  infraestructura compartida, fuera de scope de Compras. Se manejan
  con `NoOp` mientras tanto.
- **Conclusión: no hay bloqueantes** para arrancar Fase 0.
- **Nueva Fase 0.b**: registro de los permisos canónicos del módulo
  en `PermisosCanonicos`. XS, paralelizable a Fase 1.
- **§6 Sizing**: agregada Fase 0.b al cuadro.

### Rev. 3 — A6 alineado con ADR 0012 (2026-05-07)

Corrección tras lectura del ADR 0012:

- **Eliminada bandera roja A6** y el bloqueo de Fase 2.
- **Fase 2 simplificada**: la concurrencia es aplicación directa del
  ADR 0012 (Capa 1 + Capa 2). No hay tabla `bloqueos_edicion`, ni
  comandos `TomarBloqueo*`, ni endpoints de bloqueo. La infraestructura
  la provee el framework (`BaseEntity`, `CollaborationHub`).
- **§5 Riesgos**: removido el riesgo bloqueante "A6 sin resolver".
- **§6 Sizing**: removido el supuesto "A6 resuelta antes de Fase 2"
  del camino crítico. Sigue siendo 4-5 meses por la migración de RQs
  vivas (Fase 7 = L).

### Rev. 2 — alineado con respuestas del cliente (2026-05-07)

Cambios derivados de Rev. 9 del diseño:

- **Bandera roja A6**: añadido como bloqueante de Fase 2 — sin
  resolución de "bloqueo pesimista vs ADR 0012", el módulo no arranca.
- **Fase 2 ampliada**: incluye comandos de bloqueo de edición si A6
  termina en híbrido, motor v0 de matriz (solo monto), catálogo de
  motivos de rechazo.
- **Fase 7 crece a L**: incluye migración de RQs vivas (RA8) además
  de catálogos. Camino crítico se alarga ~2-3 semanas.
- **Fase 9 reescrita**: motor multidimensional de A1 (3 capas), no
  solo monto. Resolver de autorizadores por rol según naturaleza.
- **Riesgos actualizados**: nuevo riesgo bloqueante (A6), nuevo
  riesgo alto (migración RQs vivas), nuevo riesgo de clasificación
  de Naturaleza por artículo.
- Total: el camino crítico pasa de **3-4 meses** a **4-5 meses** con
  el mismo equipo de 2 backend devs.

### Rev. 1 — versión inicial (2026-05-07)

Plan inicial basado en el diseño Rev. 8. Pendiente de calibración con
capacidad real del equipo y validación de los prerrequisitos del ERP.
