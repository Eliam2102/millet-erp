# `<CollaborationHub>` — estado y plan (B.3)

Assessment formal del estado de la deuda de plataforma `<CollaborationHub>` (SignalR + Azure SignalR Service, ADR-0001 + ADR-0012 Capa 2) y la decisión de scope para v1 del módulo Compras.

> **Audiencia**: owner del proyecto + tech lead frontend + responsable de plataforma cuando se asigne.
>
> **Pregunta que responde**: ¿implementamos `<CollaborationHub>` antes del UAT/release de Compras, o lo diferimos a v1.1?
>
> **Respuesta corta**: **Diferir a v1.1**. La capa 1 de concurrencia (optimismo en BD vía `IsConcurrencyToken` + `Version`) ya protege contra lost updates en todos los caminos. La capa 2 (awareness colaborativo via SignalR) mejora UX pero no es bloqueante para go-live. Detalle abajo.

---

## 1. Estado actual del repo

Inventario (verificado al `2026-05-09`):

| Pieza | Estado | Evidencia |
|---|---|---|
| ADR-0001 (SignalR + Azure SignalR Service) | ✅ **Aceptada** desde 2026-05-01 | `docs/decisiones/0001-real-time-con-signalr.md` |
| ADR-0012 Capa 2 (soft locks) | ✅ **Aceptada** | `docs/decisiones/0012-concurrencia-hibrida.md` §"Capa 2" |
| Package `Microsoft.AspNetCore.SignalR` | ❌ no instalado | `Directory.Packages.props` no lo lista |
| Package `Azure.SignalR` | ❌ no instalado | idem |
| Connection string `AzureSignalR` | ❌ no configurada | `appsettings*.json` sin entrada |
| Bootstrap del hub en `Program.cs` | ❌ no wireado | `app.MapHub<>()` no aparece |
| Clase `ComprasHub` o equivalente | ❌ no existe | `Get-ChildItem -Filter "*Hub.cs"` retorna 0 |
| Bicep para Azure SignalR Service | ❌ no provisionado | `infra/main.bicep` no lo declara |
| Comentarios `PLATFORM-TODO(<CollaborationHub>)` en código | 1 referencia | [`RequisicionConfiguration.cs:18`](../../backend/src/Compras/Infrastructure/Configurations/RequisicionConfiguration.cs#L18) |
| Mención en diseño Compras | §8.6 (deuda) + §13 Rev. 14 (deferred sin plazo fijo) | [`01-diseno.md`](../modulos/compras-requisiciones/01-diseno.md) |
| Frontend stubs (`<CollaborationIndicator>`, `useCollaboration`) | ✅ planeados en UF0-PR2 | `05-frontend-diseno.md` (brief del FE) |

**Conclusión del inventario**: la decisión arquitectónica está tomada; la implementación es 100% greenfield.

---

## 2. Estimación honesta de esfuerzo

Lo que implica completar `<CollaborationHub>` para Compras:

| Pieza | Esfuerzo | Notas |
|---|---|---|
| Provisión Azure SignalR Service (Bicep) | 0.5 día | SKU Standard S1 (~50 USD/mes), connection string en Key Vault |
| Package + bootstrap + DI | 0.5 día | `AddSignalR().AddAzureSignalR()` + JWT auth share con API |
| `ComprasHub` (Hub class del módulo) | 1-2 días | Métodos `viewingResource(entidad, id)`, `editing(entidad, id)`, heartbeat 30s, mapa en memoria por empresa, expiración 90s |
| Wiring en `RequisicionConfiguration` (declarar entidad con soft lock) | 0.5 día | Quitar PLATFORM-TODO, agregar a "lista de entidades del hub" |
| Tests integration (conexión + heartbeat + expiración + cross-empresa isolation) | 2-3 días | Hubs son non-trivial: necesitan `WebApplicationFactory` + clientes SignalR de test |
| Manejo de scale-out (multi-instancia con backplane Azure) | 0 días extra | Azure SignalR Service lo absorbe; sin Redis backplane manual |
| Alertas + dashboards en App Insights | 0.5 día | Connections, errors, latency |
| Frontend wiring (FE ya tiene stubs según el brief) | "Trivial" — palabras del FE | Sustitución de noOp por cliente real |
| Runbook (qué hacer cuando el hub falla) | 0.5 día | Update `runbook-compras.md` |

**Total backend: 5-7 días-persona** (~ **2-3 sprints** considerando review, ajustes, integración).

**No incluye**:
- Operacionalización en producción (monitoring específico, alertas de SLO sobre hub)
- Soft lock en otras entidades del ERP (CFDI Borrador, pólizas, cotizaciones, etc — fuera de scope Compras pero comparten la misma decisión)

---

## 3. Tres caminos posibles

### Camino A — Implementar antes del UAT

**Costo**: 2-3 sprints backend + tiempo de FE para sustituir stubs + provisión Azure + ventana de prueba en UAT.

**Pros**:
- UX completa desde v1: usuarios ven "Pedro está editando" en tiempo real
- Cierra el ticket `<CollaborationHub>` para todos los módulos futuros
- Capa 2 + Capa 1 dan defensa en profundidad

**Contras**:
- **Bloqueante para release**: si Azure SignalR tarda en provisionarse, retrasa go-live
- Agrega dependencia de plataforma nueva justo en la primera entrega del ERP — riesgo operacional concentrado
- El cliente NO solicitó esto explícitamente: es mejora de UX, no requisito funcional

### Camino B — Diferir todo a v1.1

**Costo**: 0 días en v1. Todo el esfuerzo se mueve a v1.1.

**Pros**:
- Foco total de v1 en lo funcional + estable
- Plataforma SignalR se introduce con un solo módulo wireado primero (Compras), aprendizaje barato
- Cero riesgo en cutover

**Contras**:
- Usuarios pueden experimentar "lost updates aparentes": Pedro y María editan al mismo tiempo, María guarda primero, Pedro recibe **412 Precondition Failed** al guardar (no perdió datos, pero el flow es sorpresivo)
- Mitigado por: mensaje claro en el 412 ("alguien más actualizó esta RQ, recarga y reintenta") + el FE puede mostrar polling cada 30s sobre el `Version` actual del recurso como awareness ligero

### Camino C — Híbrido: Capa 1 + polling ligero del FE en v1

**Costo**: 0 días backend en v1. ~2 días en el FE para implementar el polling de `Version`.

**Pros**:
- Misma protección de datos que Camino B (Capa 1 hace el trabajo crítico)
- Awareness ligero en UI: el FE poltea el endpoint `GET /requisiciones/{id}` cada 30s, compara `Version` actual vs cargado, muestra "Esta RQ se actualizó. Recarga para ver cambios" si difieren
- Sin dependencia nueva (sin SignalR, sin Azure SignalR Service, sin connection string adicional)
- Migration path a Capa 2 transparente: cuando el hub esté, FE sustituye polling por subscription; backend ya está protegido por Capa 1

**Contras**:
- Polling tiene costo en consumo de API (~120 req/h por usuario que tenga una RQ abierta)
- "Pedro está editando" en tiempo real no llega hasta v1.1
- Estimación FE: hay que validar con el tech lead frontend si los 2 días caben en su plan

---

## 4. Recomendación

**Camino C** (= Camino B del brief original del FE, con polling ligero adicional).

Razones:
1. **Proteger el cutover**: introducir `<CollaborationHub>` en la primera entrega del ERP concentra riesgo donde menos lo queremos. Mejor estabilizar Compras en v1 con una arquitectura más conocida.
2. **No bloquear el SLO de v1**: los SLOs operacionales y de UAT no requieren soft-lock real para pasar.
3. **Migration path limpio**: el FE ya tiene los stubs `<CollaborationIndicator>` + `useCollaboration` (UF0-PR2 según brief). Cuando el hub aterrice en v1.1, el wireup es sustitución directa, sin refactor.
4. **El polling cubre 80% del valor**: la cantidad de casos reales donde dos personas editan la misma RQ al mismo tiempo en una empresa back-office es bajo. El polling cada 30s detecta el caso, evita lost updates aparentes, y no requiere nueva infra.

### Cuándo SÍ implementar `<CollaborationHub>` (v1.1+)

Disparadores que indican que Camino A pasa a ser prioridad:
- Reportes recurrentes en producción de "edité y me dijo que alguien más cambió la RQ" sin awareness previo (frustración real de usuarios) → 5+ tickets/mes.
- Otro módulo (Facturación, Cobranza) llega con caso de uso de soft-lock que justifica la inversión compartida.
- El cliente formaliza el requerimiento "queremos ver quién más está editando".

---

## 5. Acciones inmediatas

Lo que sale de este assessment:

| Acción | Owner | Plazo |
|---|---|---|
| Confirmar **Camino C** con owner del proyecto (Eduardo) | Eduardo | esta semana |
| Si confirma: actualizar `01-diseno.md` §13 con Rev. 15 (deferral oficial) | backend (este PR) | inmediato |
| Notificar al tech lead frontend que opera con stubs + polling ligero en v1 | tech lead BE → FE | inmediato |
| Crear ticket placeholder `<CollaborationHub>` en backlog v1.1 | tech lead BE | esta semana |
| Cuando se priorice v1.1: estimación detallada + asignación de persona | TBD | post-cutover |

---

## 6. Para el tech lead frontend (referencia rápida)

Si confirmas Camino C, tu trabajo en v1 es:
1. Mantener los stubs `<CollaborationIndicator>` y `useCollaboration` como están (no-op visible)
2. Agregar polling cada 30s del `Version` actual en pantallas de detalle de RQ
3. Si detecta cambio en `Version`: mostrar banner "Esta RQ se actualizó. Recargar" con CTA de recarga
4. El 412 Precondition Failed sigue siendo el último guardia (capa 1)

Cuando llegue v1.1:
1. Activar el cliente SignalR en `useCollaboration` real
2. Quitar el polling
3. El componente `<CollaborationIndicator>` empieza a mostrar usuarios concurrentes
4. Cero cambio en endpoints ni en el contrato de datos del backend

---

## 7. Cambios y firma

| Fecha | Autor | Cambio |
|---|---|---|
| 2026-05-09 | B.3 (backend) | Versión inicial — recomendación Camino C |
| 2026-05-09 | Owner (Eduardo) | **Decisión final: Camino A**. Ver §8. |
| 2026-05-09 | Sprint 1 (backend) | PR #74 mergeado — bootstrap + `ComprasHub` skeleton + 2 smoke tests |
| 2026-05-09 | Sprint 2 (backend) | PR #75 mergeado — soft-lock manager + worker de expiración + 5 behavior tests |
| 2026-05-09 | Sprint 3 (backend) | OTel meter custom (`softlock.tracked/released/expired`, `hub.connections.active`) + Bicep metric alerts (`alert-signalr-system-errors`, `alert-signalr-server-load`) + runbook §8 + dashboard §6. **FE wireup deferred**: el módulo Compras del FE aún no existe, no hay consumidor real para los stubs. Ver §8 actualizado. |
| 2026-05-09 | Sprint Buffer (backend) | Health check `signalr_hub` en `/health/ready` + UAT cases colaborativos (Bloque H, CP-70 a CP-77 en `plan-uat-compras.md`) + spot-check post-deploy actualizado con curls al hub negotiate. Backend completamente listo para UAT. |

> **Decisión oficializada por el owner el 2026-05-09**: Camino A. Las secciones 1-6 quedan como contexto histórico del análisis; la sección 8 documenta la decisión final y plan revisado.

---

## 8. Decisión final del owner: Camino A (2026-05-09)

Tras revisar la recomendación inicial de Camino C (sección 4) y el breakdown detallado de esfuerzo, el owner decidió ir por **Camino A**: implementar `<CollaborationHub>` antes del UAT de Compras.

### Razón
- Awareness colaborativo en tiempo real desde v1 mejora UX significativamente para back-office con 30-50 usuarios concurrentes.
- El equipo absorbe el "pioneer cost" de SignalR ahora; los módulos siguientes del ERP heredan la infra wireada.
- La infra Bicep de Azure SignalR Service **ya está provisionada y wireada** (módulo [`infra/modules/signalr.bicep`](../../infra/modules/signalr.bicep), connection string persistida en Key Vault como `signalr-connection-string`, App Service consume vía app setting `SignalR__ConnectionString`). Esto reduce el trabajo a backend code, no infra nueva — significativamente menor que el estimado original.
- Costo operacional ya absorbido por la infra existente: Free F1 en dev, Standard S1 en prod (~50 USD/mes).

### Plan de ejecución revisado

4 PRs secuenciales:

| PR | Sprint | Alcance | Estado |
|---|---|---|---|
| **#74** | 1 | Backend bootstrap (packages + DI + JWT query param) + `ComprasHub` skeleton + smoke test | ✅ mergeado 2026-05-09 |
| **#75** | 2 | Soft-lock manager (in-memory) + heartbeat + expiration worker + 5 behavior tests + cleanup PLATFORM-TODO | ✅ mergeado 2026-05-09 |
| **#76** | 3a | OTel meter custom + Bicep metric alerts (SignalR SystemErrors + ServerLoad) + Action Group con email + runbook §8 + dashboards §6 | ✅ mergeado 2026-05-09 |
| **#77** | Buffer | Health check `signalr_hub` en `/health/ready` + UAT Bloque H + spot-check post-deploy con hub negotiate | 🔄 en curso |
| **3b futuro** | 3b | FE primitives (`useCollaboration` + `<CollaborationIndicator>`) cuando exista pantalla Compras del FE — **no bloqueante de UAT del backend** | ⏳ deferred |

**Total realista**: 20-25 días-persona ≈ 4-5 sprints de 5 días.

### Camino C ya NO aplica

El polling ligero del FE sobre `Version` que las secciones 3-4 recomendaban como Camino C **no se implementa**. El FE sustituye los stubs `<CollaborationIndicator>` y `useCollaboration` (UF0-PR2) por el cliente SignalR real cuando llegue el sprint 3.

### Acciones inmediatas (revisadas)

| Acción | Owner | Plazo |
|---|---|---|
| ✅ Doc oficializa Camino A | backend | 2026-05-09 |
| ✅ PR #74 sprint 1 (bootstrap + skeleton) | backend | 2026-05-09 |
| ✅ PR #75 sprint 2 (soft-lock manager + behavior tests + expiration worker) | backend | 2026-05-09 |
| ✅ PR #76 sprint 3a (OTel meter + Bicep alerts + runbook + dashboards) | backend | 2026-05-09 |
| 🔄 PR #77 sprint buffer (health check + UAT cases + spot-check) | backend | en curso |
| ⏳ Notificar al tech lead frontend que el backend del hub está listo y observable | tech lead BE → FE | esta semana |
| ⏳ Validar deploy real a Azure dev (Bicep ya está, falta `az deployment sub create`) | DevOps | post-merge sprint buffer |
| 🔜 Sprint 3b (FE primitives) — **bloqueado** hasta que exista pantalla Compras del FE | FE | después de FE Compras module |
