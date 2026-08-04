# Decisiones arquitectónicas (ADRs)

Architecture Decision Records que documentan decisiones importantes y su
justificación. Cada ADR captura **una** decisión: el contexto que la motivó,
las alternativas evaluadas, la opción elegida, y las consecuencias positivas y
negativas asumidas.

Los ADRs son inmutables una vez aceptados: si una decisión se cambia, se crea
un ADR nuevo que **reemplaza** al anterior, y el viejo se marca como
`Reemplazada por ADR-XXXX`. Esto preserva la historia y permite entender por
qué un sistema es como es.

## Convenciones

- Numeración de cuatro dígitos: `0001`, `0002`, ..., `0099`, `0100`
- Nombre de archivo: `NNNN-titulo-en-kebab-case.md`
- Idioma: español
- Plantilla: ver [`template.md`](./template.md)

## Estados

- **Propuesta** — escrita pero pendiente de confirmación. Refleja una recomendación, no una decisión final.
- **Aceptada** — la decisión está tomada y vigente.
- **Rechazada** — la propuesta fue evaluada y descartada (se mantiene el documento por trazabilidad).
- **Reemplazada por ADR-XXXX** — la decisión fue revisada y cambiada en un ADR posterior.

## Índice

### Fundación técnica del sistema

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0001](./0001-real-time-con-signalr.md) | Real-time con SignalR + Azure SignalR Service | Aceptada |
| [0002](./0002-sistema-de-diseno-shadcn-ui.md) | Sistema de diseño basado en shadcn/ui     | Aceptada |
| [0003](./0003-autenticacion-entra-id.md) | Autenticación con Microsoft Entra ID         | Aceptada |
| [0004](./0004-cache-busting-via-vite.md) | Cache busting de assets vía Vite             | Aceptada |
| [0005](./0005-migraciones-ef-core-esquema-por-modulo.md) | Migraciones de BD con EF Core, esquema por módulo | Aceptada |
| [0006](./0006-observabilidad-serilog-app-insights.md) | Observabilidad con Serilog + App Insights + correlation IDs | Aceptada |
| [0007](./0007-autorizacion-rbac-granular.md) | Modelo de autorización RBAC con permisos granulares | Aceptada |
| [0008](./0008-estrategia-auditoria.md) | Estrategia de auditoría                          | Aceptada |
| [0009](./0009-outbox-pattern-eventos-integracion.md) | Outbox pattern para eventos de integración | Aceptada |
| [0010](./0010-manejo-errores-problem-details.md) | Manejo de errores end-to-end con Problem Details | Aceptada |
| [0011](./0011-multi-empresa-empresa-id.md) | Multi-empresa con `empresa_id` y esquema compartido | Aceptada |
| [0012](./0012-concurrencia-hibrida.md) | Estrategia de concurrencia híbrida (optimista + soft + hard) | Aceptada |
| [0013](./0013-tiempo-zona-horaria.md) | Tiempo, zona horaria y manejo de fechas | Aceptada |
| [0014](./0014-money-multimoneda-tipos-de-cambio.md) | Modelo de dinero, multimoneda y tipos de cambio | Aceptada |
| [0015](./0015-local-dev-auth.md) | Autenticación en desarrollo local con `FakeForLocalDev` | Aceptada |

### Capas de aplicación (Tier 2)

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0016](./0016-estrategia-testing.md) | Estrategia de testing (xUnit + Testcontainers + Playwright) | Aceptada |
| [0017](./0017-openapi-tipos-typescript.md) | Documentación de API con OpenAPI nativo y generación de tipos TypeScript | Aceptada |
| [0018](./0018-validacion-fluentvalidation.md) | Validación de input con FluentValidation y reglas de negocio en handlers | Aceptada |
| [0019](./0019-health-checks.md) | Health checks y readiness probes | Aceptada |
| [0020](./0020-idempotencia-http.md) | Idempotencia HTTP con header `Idempotency-Key` | Aceptada |
| [0021](./0021-versionado-api-rest.md) | Versionado de API REST con URL path y filosofía no-breaking | Aceptada |
| [0022](./0022-background-jobs.md) | Background jobs con Hosted Services + advisory locks + NCrontab | Aceptada |
| [0023](./0023-frontend-stack.md) | Frontend stack (TanStack Query, Zustand, react-hook-form, Zod, TanStack Router) | Aceptada |

### Específicos del dominio ERP (Tier 3)

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0024](./0024-almacenamiento-documentos.md) | Almacenamiento de documentos con Azure Blob Storage | Aceptada |
| [0025](./0025-generacion-pdfs.md) | Generación de PDFs con QuestPDF | Aceptada |
| [0026](./0026-notificaciones-email.md) | Notificaciones por email con Azure Communication Services Email | Aceptada |
| [0027](./0027-integracion-pac-onefactura.md) | Integración con PAC fiscal (`IPacProvider` + OneFactura) | Reemplazada por [ADR-0038](./0038-fiscalapi-pac-unico.md) |

### Operación y entrega (DevOps)

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0028](./0028-ambientes-despliegue.md) | Ambientes de despliegue (`dev` hasta MVP, `qa-mini` y `prod` post-MVP) | Aceptada |
| [0029](./0029-cicd-github-actions.md) | CI/CD pipeline con GitHub Actions y deploy automático solo a `dev` | Aceptada |

### Persistencia (complementarios)

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0030](./0030-multi-dbcontext-por-modulo.md) | Múltiples DbContexts por familia de esquemas | Propuesta |

### Específicos de módulo Compras

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0031](./0031-deuda-de-plataforma-y-stubs-noop.md) | Tracking de deuda de plataforma con `PLATFORM-TODO` + stubs `NoOp` | Aceptada |
| [0032](./0032-shell-de-navegacion-app-launcher.md) | Shell de navegación con app launcher modal | Aceptada |
| [0033](./0033-setting-auto-generar-oc-al-autorizar.md) | Setting `AutoGenerarOcAlAutorizar` por empresa (manual vs automático RQ→OC) | Aceptada |

### Específicos del área Administración

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0034](./0034-area-administracion-settings-hibrido.md) | Área de Administración y settings — modelo híbrido con registry de extensibilidad | Aceptada |
| [0035](./0035-relocalizar-entidades-sharedkernel-a-modulos.md) | Re-localizar entidades de negocio de `SharedKernel/Domain/` a módulos dueños | Aceptada |

### Transversales

| #    | Título                                                    | Estado     |
|------|-----------------------------------------------------------|------------|
| [0036](./0036-estrategia-de-reporteria.md) | Estrategia transversal de reportería del ERP | Aceptada |
| [0037](./0037-cifrado-de-secretos-operativos.md) | Cifrar secretos operativos con ASP.NET DataProtection y DEK en Key Vault | Aceptada |
| [0038](./0038-fiscalapi-pac-unico.md) | Adoptar FiscalAPI como PAC único (descarga + timbrado + cancelación) — reemplaza ADR-0027 | Propuesta |
| [0040](./0040-fechas-de-calendario-de-negocio-dateonly.md) | Fechas de calendario de negocio como `DateOnly` (refinamiento de ADR-0013) | Aceptada |
| [0041](./0041-autorizacion-por-operacion-y-lectura-de-catalogos.md) | Autorización por operación + lectura de catálogos cross-empresa (refinamiento de ADR-0007) | Aceptada |
| [0045](./0045-busqueda-textual-sin-extensiones.md) | Búsqueda textual insensible a acentos sin extensiones (`translate` nativo + primer `HasDbFunction`) | Aceptada |
| [0046](./0046-catalogo-unidad-medida-y-conversion.md) | Catálogo de unidad de medida, conversión en dos niveles y validación de decimales por unidad (workstream 4 etapas) | Aceptada |

## Backlog explícito (decisiones pendientes que NO deben olvidarse)

La fundación técnica (ADR-0001 a 0015), las capas de aplicación + específicos
de dominio Tier-2/3 (ADR-0016 a 0027) y la operación/entrega (ADR-0028 a 0029)
están documentadas. Lo que queda es trabajo que se aborda cuando el módulo
correspondiente lo demande.

**Pendientes**
- Estrategia de migración de datos desde SAP (mini-proyecto en sí, se
  abordará cuando arranquen los primeros módulos productivos)

**Fase 1 — convenciones declaradas pero aún no instaladas en `Directory.Packages.props`**

Estos paquetes son obligatorios por convención
([CLAUDE.md](../../CLAUDE.md), [CONTRIBUTING.md](../../CONTRIBUTING.md)) y
ADRs aceptados, pero todavía no aparecen en
`backend/Directory.Packages.props` porque Phase 1 es scaffolding y no
implementa módulos de negocio. Agregar cuando arranque PR-7+:

- **MediatR** (CQRS) — base de la arquitectura, ver `docs/arquitectura.md`.
- **Mapster** (mapeo DTO ↔ entidad) — convención de [CLAUDE.md](../../CLAUDE.md).
- **FluentValidation** + **FluentValidation.AspNetCore** — [ADR-0018](./0018-validacion-fluentvalidation.md).

A medida que cada uno se aborda, se crea su ADR correspondiente y se mueve a
la sección de índice.
