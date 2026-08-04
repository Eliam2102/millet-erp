# Plan de UAT — Módulo Compras / Requisiciones

User Acceptance Test antes del go-live productivo del módulo Compras
del Millet ERP. Verifica que el flujo completo de requisiciones —
desde captura hasta cierre — cubre los casos reales del back-office
de Millet con los datos del cliente.

---

## 1. Objetivo y alcance

### Objetivo
Validar que el módulo Compras del ERP cubre **end-to-end** las
operaciones de back-office de no-producción que hoy pasan por el
sistema legacy + SAP, y que está listo para reemplazarlos en
producción.

### Alcance del UAT
- Submódulo **Requisiciones** (cabecera + líneas + estados terminales).
- Catálogos **Proveedores** + **Articulos** en `compartido`
  (lectura — el alta administrativa queda fuera del MVP, ver Rev. 13
  del diseño).
- Integración con **A+W** y **submódulo Almacén** vía stubs (los
  consumidores reales llegan en fases posteriores; UAT prueba el
  contrato de los stubs).
- **Idempotencia** HTTP en operaciones críticas (ADR-0020).

### Fuera de alcance
- Migración de RQs vivas del legacy — descartado por el cliente
  (ver F7-PR4 SKIPPED en `03-pr-breakdown.md`).
- Integración SAP real para catálogos — diferido hasta entrega del
  export (F7-PR2 DEFERRED).
- A1 multidimensional + RBAC final — Fase 9 (no bloquea UAT del MVP).
- Reportes / dashboards de negocio — Fase 10.

---

## 2. Grupo piloto

### Participantes mínimos sugeridos

| Rol | Cantidad | Responsabilidad en el UAT |
|---|---|---|
| **Solicitante de RQ** | 2 | Crear RQ desde su perspectiva; capturar líneas; transmitir |
| **Jefe de departamento (N1)** | 2 | Autorizar a Nivel 1 desde la bandeja `pendientes-autorizacion` |
| **Autorizador alto (N2)** | 1 | Autorizar Nivel 2 cuando aplique (montos altos / críticos) |
| **Comprador / Almacén** | 1 | Recepción simulada; verificar cierre |
| **Owner del módulo** | 1 (Eduardo) | Firma criterios de aceptación |
| **Soporte técnico** | 1 (dev-Compras) | Triage + bug fix en el día |

### Departamentos sugeridos
- **Compras** (alto volumen, casos típicos).
- **Almacén** (consumibles + indirectos — `Naturaleza=Estandar`).
- **Mantenimiento** (incluye críticos — ejercita matriz N1+N2).

> Razón: estos tres deptos cubren las 4 naturalezas del catálogo
> (Estandar/Servicio/Critico/Riesgo) y los tres niveles de prioridad,
> dando confianza de que la matriz de aprobación y los flujos de
> reserva funcionan.

---

## 3. Pre-requisitos

### Datos
- [ ] Catálogo de **proveedores** seedeado (5 mínimo, mix de Activos /
      Inactivos / EnRevisión).
- [ ] Catálogo de **artículos** seedeado (10 mínimo, cubriendo las 4
      naturalezas).
- [ ] **Departamentos** y **sucursales** del cliente cargados en
      Identidad.
- [ ] **Usuarios** del grupo piloto creados con roles correctos
      (`compras.requisiciones.crear`, `.autorizar.nivel1`,
      `.autorizar.nivel2`, `.cancelar`, etc).
- [ ] **Empresa(s)** del cliente registradas en `compartido.empresas`.

### Ambiente
- [ ] **UAT** desplegado en `app-millet-uat-mxc-01` (slot dedicado,
      no comparte con dev ni prod).
- [ ] Postgres UAT con migrations aplicadas (todas las del repo hasta
      F8-PR4).
- [ ] Service Bus UAT con namespace dedicado.
- [ ] **Azure SignalR Service** (`signalr-millet-uat-mxc-01`) provisionado
      y connection string en Key Vault como `signalr-connection-string`.
      `/health/ready` retorna 200 incluyendo el check `signalr_hub`
      (CollaborationHub Sprint Buffer).
- [ ] Application Insights con dashboards de `dashboards-compras.md`
      cargados (incluyendo §6 — métricas custom del hub).
- [ ] Action Group `ag-millet-uat-mxc-01` con email del owner verificado
      (recibió email de confirmación de Azure Monitor) — alertas
      `alert-signalr-system-errors-uat` y `alert-signalr-server-load-uat`
      activas.

### Capacitación
- [ ] Sesión de 1h con el grupo piloto:
  - Demo del flujo en SPA (login → crear RQ → autorizar → cancelar).
  - Cómo reportar bugs (Issue tracker o Slack).
  - Qué esperar de los emails / notificaciones en el periodo de UAT.

### Acceso
- [ ] SPA UAT URL accesible para el grupo piloto desde la red interna.
- [ ] Credenciales Entra ID con MFA configurado.

---

## 4. Casos de prueba

Cada caso tiene un **CP-XX** identificador. Los criterios de paso son
binarios (cumple / no cumple); cualquier falla bloquea aceptación
hasta resolver.

### Bloque A — Captura y edición (Borrador)

| ID | Caso | Pasos resumidos | Criterio |
|---|---|---|---|
| CP-01 | Crear RQ Borrador | Capturar cabecera completa + 3 líneas con artículos del seed | RQ creada, folio asignado, `estado=Borrador`, ETag retornado |
| CP-02 | Validación: artículo inactivo | Intentar agregar línea con artículo `Inactivo` | Error 422 `ARTICULO_INACTIVO`, RQ no afectada |
| CP-03 | Validación: proveedor sugerido inexistente | Crear RQ con `proveedorSugeridoId` random | Error 404 `PROVEEDOR_NO_ENCONTRADO` |
| CP-04 | Editar línea estructural | PATCH cantidad / precio en línea de RQ Borrador | 204 OK, próximo GET muestra cambios, version incrementa |
| CP-05 | Eliminar línea | DELETE línea, luego GET | Línea ya no aparece, version incrementa |
| CP-06 | Notas en cualquier estado | Editar notas de línea en RQ ya transmitida | 204 OK (notas NO requieren Borrador) |

### Bloque B — Transmisión y autorización

| ID | Caso | Pasos resumidos | Criterio |
|---|---|---|---|
| CP-10 | Transmitir RQ con líneas | RQ Borrador → POST /transmitir | 204 OK, `estado=EnAutorizacion`, evento publicado en outbox |
| CP-11 | Transmitir RQ sin líneas | RQ Borrador vacía → POST /transmitir | 422 `TRANSMITIR_SIN_LINEAS` |
| CP-12 | Autorizar N1 (matriz simple, monto bajo) | RQ EnAutorizacion → POST /autorizaciones N1 | 204 OK, RQ pasa a `Cerrada` (stub stock cubre todo) |
| CP-13 | Autorizar N1 sin que sea suficiente | RQ con monto alto → POST N1 | 204 OK, RQ sigue `EnAutorizacion` (espera N2) |
| CP-14 | Autorizar N2 con N1 ya firmado | continúa CP-13 → POST N2 | 204 OK, RQ pasa a `Autorizada` o `Cerrada` según stock |
| CP-15 | Autorizar sin permiso | Solicitante intenta POST /autorizaciones | 403 `AUTORIZAR_NIVEL_DENEGADO` |
| CP-16 | Autorizar dos veces el mismo nivel | POST N1 dos veces (mismo usuario, mismo nivel) | 422 `AUTORIZACION_DUPLICADA` (o equivalente) |

### Bloque C — Estados terminales

| ID | Caso | Pasos resumidos | Criterio |
|---|---|---|---|
| CP-20 | Rechazar RQ | RQ EnAutorizacion → POST /rechazar | 204 OK, `estado=Rechazada`, motivo persistido |
| CP-21 | Eliminar pre-autorización | RQ Borrador → POST /eliminar | 204 OK, `estado=Eliminada` |
| CP-22 | Cancelar Autorizada | RQ Autorizada → POST /cancelar | 204 OK, `estado=Cancelada`, reservas liberadas (stub) |
| CP-23 | No se puede transitar terminal → otro | RQ Cancelada → POST /transmitir | 422 `ESTADO_INVALIDO` |

### Bloque D — Bandejas y consultas

| ID | Caso | Criterio |
|---|---|---|
| CP-30 | Bandeja general filtra por estado | GET con `?estado=Borrador` solo trae Borradores |
| CP-31 | Bandeja general filtra por departamento | Solo trae RQs del depto del filtro |
| CP-32 | Bandeja general filtra por requisitante | Solo trae RQs del requisitante |
| CP-33 | Bandeja general paginada | offset/limit funciona, total correcto |
| CP-34 | Pendientes de autorización | Solo RQs con `estado=EnAutorizacion`, no otras |
| CP-35 | Detalle de RQ | GET /{id} trae cabecera + líneas + autorizaciones + ETag |
| CP-36 | RQ inexistente | GET /{id-random} → 404 `REQUISICION_NO_ENCONTRADA` |

### Bloque E — Concurrencia + idempotencia

| ID | Caso | Criterio |
|---|---|---|
| CP-40 | Doble-click en Transmitir | Cliente manda 2 POST con misma `Idempotency-Key` simultáneo | Uno responde 204, el otro 409 `IDEMPOTENCY_IN_PROGRESS` |
| CP-41 | Replay con misma key + mismo body | POST /autorizaciones con misma key 5 min después | 200 con `Idempotent-Replayed: true`, NO se crea autorización duplicada |
| CP-42 | Replay con misma key + body distinto | POST con misma key pero `Notas` cambiadas | 422 `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY` |
| CP-43 | Conflict de versión | Dos usuarios editan mismo RQ Borrador, segundo sin `If-Match` actualizado | 412 Precondition Failed |
| CP-44 | Sin Idempotency-Key en endpoint requerido | POST /transmitir sin header | 400 `MISSING_IDEMPOTENCY_KEY` |

### Bloque F — Performance + observabilidad

| ID | Caso | Criterio |
|---|---|---|
| CP-50 | Bandeja P95 < 200 ms | Con 10k RQs en UAT, P95 medido por el grupo piloto durante operación normal | Cumple (referencia `bench-bandejas.md`) |
| CP-51 | Custom dimensions visibles | Filtrar logs en App Insights por `customDimensions.RequisicionId == "<id>"` | Devuelve traces del flujo completo de esa RQ |
| CP-52 | Spans Compras.Autorizar visibles | Buscar dependency `Compras.Autorizar` en App Insights | Cada autorización aparece como span, con tags `compras.requisicion.id`, `compras.estado.antes/despues` |

### Bloque G — Eventos de integración (downstream)

| ID | Caso | Criterio |
|---|---|---|
| CP-60 | Evento autorizada publicado | Tras CP-12, consumer (script de prueba) recibe `compras.requisicion.autorizada.v1` con shape correcto | Mensaje recibido, headers OK |
| CP-61 | Evento cancelada publicado | Tras CP-22, consumer recibe `compras.requisicion.cancelada.v1` | Idem |
| CP-62 | Outbox no acumula | Después de toda la batería de tests, `compras.outbox.pending` = 0 en App Insights | Cumple |

### Bloque H — Colaboración en tiempo real (CollaborationHub)

> **Pre-requisito**: el FE debe tener integrado el cliente SignalR para
> que estos casos sean ejecutables vía SPA. Si el FE Compras del piloto
> no incluye el cliente todavía (sprint 3b deferred), los casos H se
> ejecutan vía **scripts de validación manual** (PowerShell + cliente
> SignalR de .NET) hasta que el FE wireup llegue. Documentar como
> "validado vía script" en el reporte UAT, no es bloqueante de
> aceptación si los demás bloques pasan.

| ID | Caso | Pasos resumidos | Criterio |
|---|---|---|---|
| CP-70 | Presencia: dos usuarios viendo la misma RQ | Usuario A abre RQ-X. Usuario B (misma empresa) abre RQ-X 5s después. | A ve indicador con B en la lista; B ve indicador con A. Modo `Viewing` en ambos. |
| CP-71 | Presencia: transición Viewing → Editing | Usuario A está viendo RQ-X. A pone foco en un input editable. | B ve que el modo de A cambia a `Editing` en < 2s. |
| CP-72 | Salida explícita | Usuario A cierra la pantalla de RQ-X. | B ve que A desaparece de la lista en < 2s (`LeaveResource`). |
| CP-73 | Salida por desconexión silenciosa | Usuario A cierra la laptop / pierde wifi. | B ve que A desaparece de la lista en < 90s + 30s (TTL + sweep máximo). |
| CP-74 | Aislamiento cross-empresa | Usuarios en empresas distintas abren la "misma" RQ-X (distinto tenant pero coincide el GUID). | Ninguno ve al otro. Verificar manualmente en App Insights `customMetrics` que el grupo SignalR `empresa:{id}` solo recibió pushes de los usuarios de su empresa. |
| CP-75 | Lost-update protegido por Capa 1 | A y B abren RQ-X (Borrador). Ambos editan la cabecera. A guarda primero (200), B guarda después (412 `Version` mismatch). | El indicador de presencia mostraba ambos, NO bloqueó la edición de B. La protección final la dio Capa 1 (ETag/Version). Es la semántica documentada en ADR-0012. |
| CP-76 | Métricas del hub se ven en App Insights | Tras los casos CP-70..CP-74, ejecutar KQL del dashboards-compras.md §6.2: `softlock.tracked` / `released` / `expired`. | Las métricas reflejan el flujo: tracked >= 5, released > 0, expired >= 1 (CP-73). |
| CP-77 | Health check del hub funciona en UAT | `curl /health/ready` y verificar la sección JSON. | El check `signalr_hub` aparece como `Healthy` con `mode: "azure-signalr"` (no in-process). |

---

## 5. Criterios de aceptación

### Para cerrar UAT con ✅ ACEPTADO
1. **100% de casos del Bloque A, B, C, D, E pasan**.
2. **Bloque F (perf)**: P95 cumplido en al menos una sesión sostenida
   de 30 min con 5+ usuarios concurrentes.
3. **Bloque G**: los 6 eventos de integración fluyen al consumer dummy.
4. **Bloque H (colaboración)**: CP-70 a CP-73 pasan vía SPA o vía script
   manual. CP-74 + CP-75 + CP-77 son obligatorios. CP-76 es informativo
   (no bloquea si las métricas tardan en aparecer en App Insights).
5. **Bugs P1/P0 resueltos**. Bugs P2/P3 documentados en backlog,
   asumibles para post go-live.
6. **Owner firma** este documento (firma manual o digital).

### Bug severity
- **P0:** corrupción de datos, security flaw, deadlock — bloquea
  cualquier release.
- **P1:** funcionalidad core rota (ej. no se puede crear RQ) — bloquea
  go-live.
- **P2:** edge case no funciona o UX confusa — fix en sprint siguiente.
- **P3:** cosmético / mejoras — backlog.

---

## 6. Entry / exit criteria

### Entry
- [ ] Pre-requisitos §3 ✅
- [ ] Capacitación al grupo piloto completada
- [ ] Smoke test post-deploy (§7 del runbook) en UAT pasó
- [ ] Owner aprueba arrancar (kickoff meeting)

### Exit (UAT exitoso)
- [ ] Criterios §5 cumplidos
- [ ] Acta de cierre firmada por el owner
- [ ] Plan de go-live agendado (fecha + ventana de mantenimiento)

### Exit anticipado (UAT fallido)
- [ ] P0 o múltiples P1 abiertos > 1 semana sin fix
- [ ] Decisión del owner de pausar y replanear
- [ ] Documentar en post-mortem qué bloqueó

---

## 7. Plan de rollback

Si el UAT detecta show-stoppers (P0 o varios P1):

1. **No mergear cambios** en main de los días de UAT activo, salvo
   hot-fixes puntuales acordados.
2. Si se descubre un defecto fundamental que requiere rediseño
   (ej. el modelo de autorización no encaja con el proceso real del
   cliente):
   - Pausar UAT.
   - Reunión owner + dev-Compras para definir el cambio.
   - Crear ADR documentando la decisión.
   - Implementar en una nueva fase o iteración.
3. Si el cambio es chico (ej. validación faltante, copy de mensaje):
   - Hot-fix branch desde main.
   - PR aislado siguiendo convención (`fix/<descripcion>`).
   - Aplicar a UAT y reanudar.

---

## 8. Cronograma sugerido

| Día | Actividad |
|---|---|
| **D-7** | Confirmación de pre-requisitos. Capacitación al grupo piloto. |
| **D-3** | Smoke test final en UAT. Reunión kickoff. |
| **D0–D2** | Bloques A + B (captura + autorización). Bug triage diario al final del día. |
| **D3–D5** | Bloques C + D + E (terminales + bandejas + idempotencia). |
| **D6** | Bloque F + G (perf + eventos). Sesión sostenida de 5+ usuarios. |
| **D6 (tarde)** | Bloque H (colaboración). 30 min con 2 usuarios en distintas máquinas. |
| **D7** | Reporte de hallazgos. Decisión: aceptar / extender / rollback. |

> **Duración total esperada: 1 semana laboral (5 días) + 2 días de
> contingencia.** Ajustable según disponibilidad del grupo piloto.

---

## 9. Anexos

- [Diseño del módulo](../modulos/compras-requisiciones/01-diseno.md)
- [Contrato de eventos de integración](../integraciones/compras-eventos.md)
- [Runbook operacional](runbook-compras.md)
- [Dashboards y KQL queries](dashboards-compras.md)
- [Bench de bandejas](../../tools/perf/bench-bandejas.md)

---

## 10. Cambios y firma

| Fecha | Autor | Cambio |
|---|---|---|
| 2026-05-09 | F8-PR4 | Versión inicial. Cubre alcance MVP de Fase 8. |
| 2026-05-09 | CollaborationHub Sprint Buffer | §3 Pre-requisitos: SignalR Service + health check + Action Group. §4 Bloque H nuevo (8 casos CP-70 a CP-77). §5 criterio 4 actualizado. §8 cronograma agrega sesión Bloque H. |

> **Firma del owner pendiente** (Eduardo Paredes) — se firma como
> aceptación del plan al mergear el PR; firma de cierre tras ejecución
> del UAT en hoja aparte.
