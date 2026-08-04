# Levantamiento — Módulo Integración A+W (`Millet.Integraciones.Aw`)

> **Estado:** `Draft - WIP` — esqueleto inicial. Pendiente llenar tras
> revisar [`docs/integration/00-system-overview.md`](../../integration/00-system-overview.md).
>
> **Origen:** integración con el sistema productivo on-prem **A+W**
> (servidor `SER-DATA` de Millet, conectividad vía Azure Hybrid Connection).
> Primer consumidor del módulo: **Glass Agent** (sistema externo en PHP,
> hospedado en Cloudways).
>
> **Fecha del levantamiento:** _pendiente_
> **Owner del módulo:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Patrón:** sigue el exemplar de Compras
> ([`docs/modulos/compras-requisiciones/00-levantamiento-legacy-portalsap.md`](../compras-requisiciones/00-levantamiento-legacy-portalsap.md)).
> Replica decisiones del módulo Compras en lo que aplique: hexagonal +
> CQRS, multi-DbContext (ADR-0030), Outbox (ADR-0009), Idempotency-Key
> (ADR-0020), versionado `/api/v1/` (ADR-0021), RBAC granular (ADR-0007),
> Problem Details (ADR-0010), ETag (ADR-0012), PLATFORM-TODO (ADR-0031).

---

## 0. Cómo leer este documento

- **[Verificado]** = leído directamente en el sistema A+W, en el Glass
  Agent (PHP) o confirmado en sesión con el cliente / equipo on-prem.
- **[Inferido]** = deducido por nombres, convenciones o documentación
  parcial; no confirmado.
- **[Gap]** = agujero de conocimiento que requiere confirmación.

---

## 1. Contexto

_Pendiente_ — qué es A+W, por qué Millet depende de él, qué papel juega
en la operación comercial y de producción de vidrio, por qué el ERP
nuevo necesita integrarse en lugar de reemplazarlo, y cuál es la
ventana de oportunidad de negocio que motiva este módulo.

Referencia obligatoria: [`docs/integration/00-system-overview.md`](../../integration/00-system-overview.md).

---

## 2. Objetivos

_Pendiente_ — qué problema resuelve el módulo `Millet.Integraciones.Aw`,
qué casos de uso habilita, qué métrica de éxito tiene cada uno.

A llenar al menos con:

- Objetivo de negocio.
- Objetivos técnicos.
- Métricas de éxito (latencia, tasa de error, throughput esperado).

---

## 3. Alcance

_Pendiente_ — qué entra en el módulo en la versión inicial:

- Dirección de los flujos de datos (A+W → ERP, ERP → A+W, o bidireccional).
- Entidades/eventos cubiertos (facturación, inventario, datos comerciales,
  etc.) y nivel de detalle.
- Consumidores previstos en fase 1 (Glass Agent) y fase 2+ (CFDI,
  Almacén, CxC, …).
- Modos de operación (push, pull, polling, near-realtime).
- Endpoints HTTP y eventos de integración (Service Bus) expuestos.
- Workers en-proceso definidos: `AwDropWorker`, `AwCorrelationWorker`
  (D4 — `IHostedService` dentro de `Millet.Api`).

---

## 4. No-objetivos

_Pendiente_ — explicitar lo que el módulo **NO** hace. Heredado de las
decisiones D1–D5 y el anti-scope ya declarado en `CLAUDE.md`:

- **NO** genera EDI — lo genera el Glass Agent en PHP y lo envía al ERP
  como contenido binario opaco en fase 1.
- **NO** modifica datos en A+W vía SQL — sólo lectura.
- **NO** conoce de glass-specific quoting (medidas, áreas, templado,
  optimización de corte).
- **NO** reemplaza a A+W — el ERP convive con A+W bajo Strangler Fig.

A completar con cualquier otro no-objetivo derivado del overview.

---

## 5. Stakeholders

_Pendiente_ — quién pide qué, quién aprueba, quién opera, quién paga.
A llenar con:

- **Sponsor / decisor de negocio.**
- **Owner técnico del ERP** (Eduardo Paredes).
- **Owner del Glass Agent** (responsable del lado PHP / Cloudways).
- **Owner de A+W on-prem** (DBA / sysadmin que controla `SER-DATA` y la
  configuración de Hybrid Connection).
- **Usuarios finales** del flujo (vendedores, planta, contabilidad,
  facturación) — sólo los que interactúan indirectamente con la
  integración.
- **Responsable de seguridad / cumplimiento** (revisa el service
  principal del Glass Agent y los permisos RBAC asignados).

---

## 6. Glosario

_Pendiente_ — términos del dominio A+W y del ecosistema Millet que
hay que fijar antes de modelar. Candidatos a definir:

- **A+W** — qué módulos del producto usa Millet, versión, dónde corre.
- **Glass Agent** — alcance, stack, dónde corre, qué responsabilidades
  asume.
- **EDI** (en este contexto) — formato real, payload binario, validación.
- **Drop** — unidad de trabajo entrante del Glass Agent al ERP.
- **Correlación** — cómo se asocia una respuesta asíncrona de A+W con
  el evento originador.
- **Service principal / usuario de servicio** — concepto nuevo a
  introducir en módulo Identidad (D3).
- **Hybrid Connection / HCM** — Azure Relay + Hybrid Connection Manager
  instalado en `SER-DATA`.
- **SER-DATA** — servidor on-prem que aloja A+W (DB) y HCM.
- Otros tipos de documento de A+W relevantes (pedidos, órdenes,
  embarques, facturas) — a confirmar.

---

## 7. Dependencias de plataforma pendientes

> Sección obligatoria (ADR-0031). Tabla *Pieza · Ticket · NoOp en uso ·
> Cómo se wirea*. Cada `PLATFORM-TODO` en el código tiene una fila aquí.

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| _Pendiente_ — Hybrid Connection (Azure Relay) hacia `SER-DATA` | — | _por definir_ (stub `IAwReadPort` que devuelve fixtures) | Provisionar Azure Relay + namespace en Bicep; HCM ya instalado del lado on-prem. Connection string al Key Vault. |
| _Pendiente_ — Service principal del Glass Agent en Entra ID | — | _por definir_ (auth fake en dev) | App registration + client credentials; secret/cert al KV; rol "glass-agent" con permisos canónicos `integraciones.aw.*`. |
| _Pendiente_ — Concepto "usuario de servicio" en `Identidad` | — | _por definir_ | Flag `EsUsuarioDeServicio` en `Usuario` (o nueva entidad si requiere otros atributos); habilita login no-interactivo y bypass de selector dev. |
| _Pendiente_ — Driver/cliente de lectura A+W (SQL Server) | — | _por definir_ | Adapter Infrastructure con queries parametrizadas; sin EF, sin tracking; timeouts cortos. |
| _Pendiente_ — Esquema Postgres `integraciones_aw` + `IntegracionesAwDbContext` | — | n/a (se crea desde inicio) | Agregar a `MigrationsHealthCheckOptions.ContextTypes` y al bucle de migraciones en `deploy-app-dev.yml`. |
| _Pendiente_ — Outbox para eventos de integración del módulo | — | `NoOpIntegrationEventBusSender` si la cs de Service Bus está vacía | Reutilizar `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<IntegracionesAwDbContext>`. |

A completar conforme se confirme cada pieza con el overview y las
sesiones con el cliente.

---

## 8. Riesgos

_Pendiente_ — listar cada riesgo con probabilidad, impacto, mitigación
y dueño. Candidatos a evaluar:

- **Conectividad on-prem inestable.** Hybrid Connection cae o el
  servidor `SER-DATA` no responde → workers en backoff, drops se
  acumulan en la cola, eventos se atrasan.
- **Acoplamiento al esquema de A+W.** Cambios de versión / parches del
  proveedor pueden romper queries de lectura.
- **Acceso de sólo lectura mal configurado.** Si el usuario de BD que
  usa el ERP termina con permisos de escritura por error, riesgo de
  corromper A+W.
- **Auth del Glass Agent.** Rotación de secretos/certificados del
  service principal; revocación accidental.
- **Idempotencia cross-sistema.** El Glass Agent reintenta; A+W es
  fuente de verdad; el ERP debe deduplicar drops y correlacionar
  respuestas sin doblar movimientos.
- **Payload EDI opaco.** En fase 1 el ERP no valida contenido; un EDI
  malformado puede llegar a A+W y romper procesos productivos.
- **Volumetría / throughput.** A+W es un sistema productivo crítico;
  poll/query agresivo desde Azure puede degradarlo.
- **Compliance fiscal.** Si pedazos del flujo tocan facturación CFDI
  o inventario reportable, las decisiones de scope (D1, anti-scope)
  deben sostenerse.

---

## Rev.

- **2026-05-15** — Esqueleto inicial creado. Pendiente llenar tras
  revisar `docs/integration/00-system-overview.md` y sesiones con
  cliente / owner del Glass Agent / DBA de A+W.
