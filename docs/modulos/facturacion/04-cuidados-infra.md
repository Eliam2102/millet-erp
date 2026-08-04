# Cuidados de infraestructura — Módulo Facturación (`Millet.Facturacion`)

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> Cuidados de infra/persistencia/integración al construir el módulo.

---

## 0. Cómo leer

Checklist de lo que **no** puede fallar en infra al implementar Facturación.
Cada sección apunta a la fase del `02-plan` donde aplica.

---

## 1. Migraciones EF Core (F0-PR2 en adelante)

- `FacturacionDbContext` con schema `facturacion`. **Checklist obligatorio**
  (memoria [feedback_dbcontext_nuevo_checklist]): en el mismo PR que crea el
  DbContext, tocar **`Program.cs`** (`AddDbContext` +
  `MigrationsHealthCheckOptions.ContextTypes`) **y** el bucle de migraciones
  de **`deploy-app-dev.yml`**. Sin esto, deploy falla con `/health/ready` 503.
- `Comprobante` con herencia **TPT** (tabla base + tabla por subtipo). Cuidar
  que las migraciones generen las FKs PK/FK 1:1 correctamente.
- `decimal(18,6)` para importes unitarios, `decimal(18,2)` para totales
  (precisión SAT). No usar `float`/`double`.
- Índices críticos (§5.1 diseño): `comprobante(uuid)` único parcial,
  `comprobante(sucursal_id, serie, folio)` único, `ingesta_control(origen,
  clave_natural)` único, `factura_venta(obra_id)`, `anticipo(cliente_id,
  estado)`.
- Permisos canónicos: modificar `PermisosCanonicos.Todos` exige **migración
  en `IdentidadDbContext`** (memoria [feedback_permisos_canonicos_migration]).

---

## 2. Outbox + Service Bus (ADR-0009)

- Reusar `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<FacturacionDbContext>`
  (mismo patrón que Compras/CxP). NoOp `NoOpIntegrationEventBusSender` si la
  connection string de Service Bus está vacía.
- Topic `facturacion-events`. Eventos: `FacturaVentaTimbradaEvent`,
  `FacturaAnticipoTimbradaEvent`, `NotaCreditoTimbradaEvent`,
  `ReciboPagoTimbradoEvent`, `ComprobanteCanceladoEvent` (§8 diseño).
- **Atomicidad crítica:** la NC de amortización se timbra y persiste en la
  **misma transacción** que la factura final (F4-PR3). El evento sale por
  Outbox dentro de esa transacción.
- Idempotencia en consumidores por `uuid` / `comprobante_id` /
  `solicitud_cancelacion_id`.

---

## 3. Integración FiscalAPI vía `Integraciones.Fiscal` (F1, F12)

- **Facturación no implementa el cliente del PAC.** Consume `IFiscalApiClient`
  + `ICsdProvider` de `Integraciones.Fiscal`. Hasta la **fase 2** de ese
  módulo (en rediseño asíncrono), se usa un **stub** que devuelve UUID/sello
  fake. `PLATFORM-TODO(<IntegracionesFiscalFase2>)`.
- **Timbrado asíncrono-tolerante:** la FSM modela `TimbradoEnProceso`; un
  `TimbradoPendienteWorker` resuelve los pendientes. No asumir respuesta
  síncrona del PAC.
- **CSD del emisor** por empresa en Key Vault (provisto por `Integraciones.Fiscal`);
  nunca en BD ni en código.
- **Validación local previa** antes de llamar al PAC, para no consumir timbres
  en errores corregibles.
- Sandbox FiscalAPI en dev/QA; producción solo en prod (variable de ambiente).

---

## 4. Ingesta A+W — tabla-puente y Hybrid Connection (F3, F10)

- **Tabla-puente `aw_solicitud_pedido`:** esquema **definido por el ERP**
  (D18); vive on-prem, A+W la llena. El acceso es vía
  **Azure Hybrid Connection Manager** a `SER-DATA`. Permisos de lectura sobre
  las vistas de datos + lectura/escritura sobre la tabla-puente.
- **No mutación directa de tablas internas de A+W:** todo write-back va por la
  tabla-puente / SP sancionado (coherente con anti-scope de `Integraciones.Aw`).
- **Doble candado de idempotencia:** `ingesta_control` (ERP, fuente de verdad,
  hash de contenido) + claim en A+W. El worker procesa la cola por `version`.
- **Soft-lock** durante facturación: reusar `ISoftLockManager` +
  `SoftLockExpirationWorker` (`Api/Hubs`); el worker de ingesta **salta**
  pedidos `Bloqueado`.
- Latencia/disponibilidad de la Hybrid Connection: timeouts + reintentos; el
  worker no debe bloquear el host.

---

## 5. Concurrencia y locking (ADR-0012)

- `BaseEntity.Version` (ETag) en `Comprobante` y `PedidoFacturable`. `PUT`/
  edición manual con `If-Match` → 412 al choque.
- **Soft-lock** del `PedidoFacturable` mientras un cajero factura (evita doble
  facturación y refresh concurrente desde la ingesta).
- **Folios atómicos** vía `Compartido.Series.ReservarFolioCommand` — no generar
  consecutivos a mano (riesgo de huecos/duplicados). La reserva ocurre **una
  sola vez por documento**: el reintento de timbrado reutiliza serie+folio
  ([Decisión 01-G] G1); huecos solo por `Descartada` o fallo de persistencia
  post-reserva.

---

## 6. Seguridad y datos sensibles

- **PCI-DSS:** nunca almacenar PAN completo ni vencimiento de tarjeta. Solo 4
  últimos dígitos + autorización del adquirente (regla §17 levantamiento).
- **XML/PDF y CSD:** el repo CFDI común (`Integraciones.Fiscal`) y el blob
  storage (ADR-0024) custodian XML/PDF. CSD solo en Key Vault.
- Logging: enmascarar RFC/datos fiscales sensibles en logs (Serilog masking).
- RBAC por recurso (`facturacion.*`); el rol Contador General requerido para
  `facturacion.activos.autorizar`.

---

## 7. Performance

- Volúmenes (§1.3 diseño): cientos de facturas/día, miles de anticipos
  abiertos, picos en cierre de mes.
- Particionar `comprobante` por año de `fecha_timbrado` si el volumen lo exige
  (reservado, no en MVP).
- El `AwSolicitudesWorker` lee solo solicitudes sin procesar (índice
  `aw_solicitud_pedido(resultado, numero_pedido, version)`); no escanea todo.
- Reportes vía proyecciones/SQL; no cargar agregados completos en memoria.

---

## 8. Cross-module integration

- **DatosMaestros:** `IClientesReadPort`/`IProductosReadPort` +
  `IMasterProvisioningPort` (auto-provisión A+W). Stub hasta que DatosMaestros
  soporte provisión (`PLATFORM-TODO(<MasterProvisioningAw>)`).
- **Millet.Catalogos:** catálogos SAT (`FormaPago`, `UsoCfdi`, `RegimenFiscal`,
  `Incoterm`, `Moneda`, `TipoCambio`) — adapter real, no recrear.
- **Compartido.Series:** series + folios (FANT seed).
- **Contabilidad:** `IPeriodoContablePort` (candado) + `IContabilidadAsientoPort`
  (asientos) — stubs hasta que exista.
- **Integraciones.Origenes:** Planta Pintura + Salidas — módulo nuevo, stubs.
- **CxP:** coordinación del repo CFDI común (F2-PR1).

---

## 9. Observabilidad

- Serilog estructurado → Application Insights. Correlación por `uuid` /
  `pedido_facturable_id` / `solicitud_id`.
- Métricas: timbres exitosos/fallidos, facturas en `PendientePedimento`,
  facturas en `TimbradoEnProceso`, excepciones de ingesta, latencia FiscalAPI,
  cola `aw_solicitud_pedido` pendiente.
- Alertas: backlog de la cola de solicitudes, `TimbradoEnProceso` estancado,
  envío de correo fallido reincidente, cambios A+W sobre pedidos `Facturado`.

---

## 10. Seeds y datos iniciales

- **Series** (`Compartido.Series`): seed inicial por sucursal-tipo + anticipos
  `FANT`. Configurable después.
- **`ConceptoContable`**: seed con placeholders `TBD-*` + `requiere_codigo_definitivo`
  (§6.5 levantamiento). Contabilidad entrega los códigos reales después.
- **Catálogos SAT**: ya en `Millet.Catalogos`; verificar vigencia (sync desde
  FiscalAPI vía `Integraciones.Fiscal`).
- **Permisos** `facturacion.*` + asignación a roles operativos (Cajero, Caja
  general, Contador General).

---

## 11. Despliegue

- Añadir `FacturacionDbContext` al bucle de migraciones de `deploy-app-dev.yml`
  (y prod). Validar `/health/ready`.
- Workers `IHostedService` dentro de `Millet.Api` (no Functions/Container
  Jobs): `AwSolicitudesWorker`, `PlantaPinturaImportWorker`,
  `TimbradoPendienteWorker`, `CancelacionSatPollerWorker`, `EnvioCfdiCorreoWorker`,
  `PedimentoSalidasWorker`, `WriteBackResultadoWorker`,
  `OutboxPublisherWorker<FacturacionDbContext>`.
- Variables de ambiente: FiscalAPI (sandbox/prod), Hybrid Connection,
  Service Bus, Key Vault (CSD). Secretos jamás en código/parámetros.

---

## 12. Pendientes para `08-operacion-y-runbook.md`

- Recetas de troubleshooting (timbre estancado, cola atascada, claim huérfano).
- Procedimiento de re-facturación tras cancelación.
- Inventario de `PLATFORM-TODO` abiertos.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Cuidados de infra iniciales del módulo. |
