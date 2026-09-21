// ============================================================================
// Módulo: Azure Service Bus Namespace
// Mensajería asíncrona entre módulos del ERP (ADR-0009: Outbox pattern
// publica eventos aquí; los demás módulos los consumen).
//
// SKU:
//   - dev/qa: Standard (permite topics + subscriptions; ~$10/mes base)
//   - prod: Premium (zone redundancy, capacity dedicada; mucho más caro)
//
// Basic se descarta porque solo soporta queues (no topics), y el patrón de
// integración entre módulos suele necesitar fan-out vía topic.
// ============================================================================

param location string
param projectName string
param environmentTier string
param regionCode string
param tags object

@description('SKU del Service Bus Namespace.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${projectName}-${environmentTier}-${regionCode}-01'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    zoneRedundant: skuName == 'Premium'
    // Permitir auth por keys mientras Phase 1; en Phase 2+ se evaluará
    // disableLocalAuth=true y switch a managed identity + RBAC.
    disableLocalAuth: false
  }
}

// La regla RootManageSharedAccessKey la crea Azure por default. La referenciamos
// para extraer su connection string y persistirla en Key Vault desde main.bicep.
resource rootAuthRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2024-01-01' existing = {
  parent: serviceBus
  name: 'RootManageSharedAccessKey'
}

// ============================================================================
// Topology
//
// Topics declarados aquí:
//   - integraciones-aw-events (módulo Millet.Integraciones.Aw)
//   - compras-events          (módulo Millet.Compras)
//   - almacen-events          (módulo Millet.Almacen)
//   - cuentas-por-pagar-events (módulo Millet.CuentasPorPagar)
//   - facturacion-events       (módulo Millet.Facturacion)
//   - tesoreria-events         (módulo Millet.Tesoreria; publisher en TES-PR4)
//   - admin-events              (Administración; publisher en F1-ADM-01)
//
// Sub-cara por subscription:
//   compras-events / cuentas-por-pagar-subscription
//     ↳ ComprasEventListenerWorker (CxP), filter IN (autorizada, cancelada).
//   almacen-events / cuentas-por-pagar-subscription
//     ↳ AlmacenEventListenerWorker (CxP), filter IN (recepcion, devolucion).
//   almacen-events / compras-subscription-almacen
//     ↳ AlmacenEventListenerWorker (Compras), filter IN (recepcion).
//   cuentas-por-pagar-events / compras-subscription
//     ↳ CxpEventListenerWorker (Compras), filter IN (factura.* + nota-credito
//       + nota-cargo + anticipo + pasivo + tc.cerrado).
//   cuentas-por-pagar-events / almacen-subscription
//     ↳ CxpEventListenerWorker (Almacén), filter IN (factura.registrada +
//       factura.diferencia-precio-detectada) para variante B (materiales
//       directos) y ajuste de costo.
//   cuentas-por-pagar-events / tesoreria-subscription
//     ↳ CuentasPorPagarEventListenerWorker (Tesorería, TES-PR3), filter IN
//       (pasivo.autorizado-para-pago) → proyección pasivo_pendiente_pago.
//   tesoreria-events / cuentas-por-pagar-tesoreria-sub
//     ↳ TesoreriaEventListenerWorker (CxP), filter IN (pago aplicado +
//       revertido + repp recibido + cancelacion-pasivo solicitada).
//   facturacion-events / cuentas-por-cobrar-subscription
//     ↳ FacturacionEventListenerWorker (CxC), filter IN (factura-venta +
//       repp + cobro mostrador ± cancelado + nc + comprobante cancelado +
//       factura-anticipo).
//
// Cuando se adopte un topic adicional, replicar el patrón: topic +
// subscription(s) con su SQL filter por EventType.
// ============================================================================

// ============================================================================
// Topic: integraciones-aw-events (módulo Millet.Integraciones.Aw)
// Fan-out de eventos del módulo de integración con A+W. Lo publica el
// OutboxPublisherWorker<IntegracionesAwDbContext> (mismo patrón que el de
// Compras hoy). Lo consumen workers HostedService dentro de Millet.Api:
//
//   - drop-subscription:
//       Consumida por AwDropWorker. Para cada evento, hace HTTP POST al
//       drop service on-prem vía Hybrid Connection. Reintenta automáticamente
//       con backoff (configurado en MaxDeliveryCount + dead-lettering).
//
// **PR #201 retiró `AwCorrelationWorker`** (flow callback per-EDI obtiene el
// outcome en la respuesta HTTP del drop service, sin polling SQL desde Azure).
// La subscription `correlation-trigger-subscription` quedó huérfana (sin
// consumer + acumulando mensajes activos y DLQ) y se elimina en este PR.
//
// Política común:
//   - DeadLetteringOnMessageExpiration: true → mensajes expirados van a
//     dead-letter queue para inspección manual del operador.
//   - MaxDeliveryCount: 5 → 5 reintentos con backoff antes de DLQ.
//   - DefaultMessageTimeToLive: P1D → mensajes válidos por 24h.
//
// Si el módulo crece y aparecen más subscribers, cada uno se agrega como
// una subscription nueva del MISMO topic (fan-out clásico). NO crear
// topics adicionales por cada consumidor.
// ============================================================================

resource awEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'integraciones-aw-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

resource awEventsDropSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: awEventsTopic
  name: 'drop-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    // PR C: subido de PT1M a PT5M. El AwDropWorker hace HTTP POST al on-prem
    // vía Hybrid Connection con timeout de 30s; en happy path 1 min sobra,
    // pero si HC está degradado el procesamiento puede acercarse al límite.
    // PT5M da margen sin diluir la garantía de redelivery (MaxDeliveryCount=5
    // siguen vigentes; cliente renueva con MaxAutoLockRenewalDuration=10m).
    lockDuration: 'PT5M'
    enableBatchedOperations: true
  }
}

// PR C: filter SQL para que la subscription solo entregue el evento que el
// AwDropWorker debe procesar. Sin filter, drop-subscription recibe TODOS los
// eventos del topic (incluyendo AwEdiEntregadoAAw, AwPedidoCorrelacionado,
// etc. que el worker debe ignorar idempotentemente). El filter elimina ese
// ruido evitando carga innecesaria de DB + logs.
//
// IMPORTANTE: el prefijo "user." en la sqlExpression apunta a las
// ApplicationProperties del mensaje. Verificado contra
// SharedKernel/Infrastructure/Outbox/ServiceBusIntegrationEventBusSender.cs:66
//     message.ApplicationProperties["EventType"] = entry.EventType;
// El EventType emitido por PR B es "integraciones.aw.cotizacion.recibida.v1"
// (constante AwCotizacionRecibida.EventTypeName, línea 29).
//
// SystemProperties (sys.*) no se usan: aunque Subject también lleva el
// EventType, ApplicationProperties es la fuente canónica del repo.
resource awEventsDropSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: awEventsDropSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType = \'integraciones.aw.cotizacion.recibida.v1\''
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: admin-events (Administración)
// Publicado por OutboxPublisherWorker<CompartidoDbContext>. No tiene
// subscriptions de negocio todavía; los consumidores se agregan cuando cada
// módulo adopte los contratos admin.*.
// ============================================================================

resource adminEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'admin-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// ============================================================================
// Topic: compras-events (módulo Millet.Compras)
// Publicado por OutboxPublisherWorker<ComprasDbContext>. Lo consumen workers
// de Almacén y Cuentas por Pagar. Hasta este PR el topic existía sólo en
// runtime/portal; ahora queda declarativo en Bicep junto con la primera
// subscription que necesita filter SQL.
//
// Antes de aplicar este Bicep en un ambiente donde el topic ya existe en
// Azure (creado vía portal o az CLI), Bicep lo adopta sin recrearlo. Si las
// properties divergen del estado existente, Bicep las alinea — ver what-if.
// ============================================================================

resource comprasEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'compras-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por ComprasEventListenerWorker (módulo CuentasPorPagar).
// Procesa OC autorizada (registra OC facturable) y OC cancelada (alerta sobre
// facturas afectadas). El SQL filter restringe la entrega a esos 2 eventos
// para evitar carga inútil con OcCerradaEvent y otros que CxP no consume.
// Referencia: backend/src/CuentasPorPagar/Infrastructure/Workers/ComprasEventListenerWorker.cs
resource comprasEventsCxpSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: comprasEventsTopic
  name: 'cuentas-por-pagar-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource comprasEventsCxpSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: comprasEventsCxpSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'compras.orden-compra.autorizada.v1\', \'compras.orden-compra.cancelada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: almacen-events (módulo Millet.Almacen)
// Publicado por OutboxPublisherWorker<AlmacenDbContext>. Lo consumen workers
// de Compras y Cuentas por Pagar.
//
// Adopción en Bicep cierra el bug histórico donde:
//   - El App Service no tenía `Almacen__Outbox__ServiceBusTopicName`, así
//     que el publisher usaba el default `compras-events` por OutboxPublisherOptions.
//   - El topic `almacen-events` nunca existió en SB de dev.
//   - Resultado: las recepciones de OC publicaban al topic equivocado o se
//     descartaban; `LineaOrdenCompra.CantidadRecibida` no se incrementaba
//     y las recepciones parciales seguían mostrando el pendiente original.
//
// EventTypes publicados (al 2026-05):
//   - almacen.oc_recepcion.registrada.v1
//   - almacen.oc_devolucion.registrada.v1
//   - almacen.saldo.proyectado.v1                (informativo)
//   - almacen.entrada-inventario.valorada.v1     (informativo)
//   - almacen.salida-inventario.aplicada.v1      (informativo)
// ============================================================================

resource almacenEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'almacen-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por CxP AlmacenEventListenerWorker (F6-PR3).
// Procesa recepciones (proyecta a `recepciones_oc_local`) + devoluciones
// (genera `NotaCargo` Borrador). Filtra a esos 2 EventTypes para evitar
// carga inútil con eventos informativos.
// Referencia: backend/src/CuentasPorPagar/Infrastructure/Workers/AlmacenEventListenerWorker.cs
resource almacenEventsCxpSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: almacenEventsTopic
  name: 'cuentas-por-pagar-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource almacenEventsCxpSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: almacenEventsCxpSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'almacen.oc_recepcion.registrada.v1\', \'almacen.oc_devolucion.registrada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// Subscription consumida por Compras AlmacenEventListenerWorker
// (PR #293, "fix recepciones parciales"). Incrementa CantidadRecibida
// por línea de OC. Filtra estrictamente al único EventType que Compras
// consume; los informativos van a esta subscription pero el worker los
// loggea + marca idempotencia sin dispatch.
// Referencia: backend/src/Compras/Infrastructure/Workers/AlmacenEventListenerWorker.cs
resource almacenEventsComprasSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: almacenEventsTopic
  name: 'compras-subscription-almacen'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource almacenEventsComprasSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: almacenEventsComprasSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'almacen.oc_recepcion.registrada.v1\', \'almacen.oc_devolucion.registrada.v1\', \'almacen.salida_requisicion.registrada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: cuentas-por-pagar-events (módulo Millet.CuentasPorPagar)
// Publicado por OutboxPublisherWorker<CuentasPorPagarDbContext>. Lo consumen
// dos workers actualmente:
//
//   - CxpEventListenerWorker (Compras, PR #288) — sub `compras-subscription`.
//     Aplica `cuentas_por_pagar.factura.registrada.v1` al sub-estado Facturación
//     de OC (RegistrarFacturacionLinea por línea). Los demás EventTypes (cancelada,
//     autorizada, rechazada, diferencia-precio, nota-credito, nota-cargo, anticipo,
//     pasivo, estado-cuenta-tc) llegan como informativos: el worker los marca
//     idempotentes pero no dispatcha command.
//   - CxpEventListenerWorker (Almacén, F3-PR1) — sub `almacen-subscription`.
//     Aplica `factura.registrada` (variante B = materiales directos: concilia
//     recepción pendiente) y `factura.diferencia-precio-detectada` (ajusta costo
//     de inventario dentro de tolerancia).
//
// Hasta este PR el topic existía sólo creado vía portal/CLI; ahora queda
// declarativo en Bicep junto con sus 2 subscriptions y SQL filters. Bicep
// adopta los recursos existentes si las properties coinciden; si divergen,
// las alinea (ver what-if antes de aplicar).
// ============================================================================

resource cxpEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'cuentas-por-pagar-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por Compras CxpEventListenerWorker (PR #288).
// Filtra al set completo de eventos que el worker reconoce — el único con
// efecto en dominio es `factura.registrada` (dispatch a command); los demás
// pasan por idempotencia + log informativo. Si llegan otros EventTypes al
// topic, quedan fuera del filter y se descartan en el broker (sin DLQ).
// Referencia: backend/src/Compras/Infrastructure/Workers/CxpEventListenerWorker.cs
resource cxpEventsComprasSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: cxpEventsTopic
  name: 'compras-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource cxpEventsComprasSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cxpEventsComprasSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'cuentas_por_pagar.factura.registrada.v1\', \'cuentas_por_pagar.factura.cancelada.v1\', \'cuentas_por_pagar.factura.autorizada.v1\', \'cuentas_por_pagar.factura.rechazada-por-tolerancia.v1\', \'cuentas_por_pagar.factura.diferencia-precio-detectada.v1\', \'cuentas_por_pagar.nota-credito.registrada.v1\', \'cuentas_por_pagar.nota-cargo.autorizada.v1\', \'cuentas_por_pagar.anticipo.capturado.v1\', \'cuentas_por_pagar.pasivo.autorizado-para-pago.v1\', \'cuentas_por_pagar.estado-cuenta-tc.cerrado.v1\')'
      compatibilityLevel: 20
    }
  }
}

// Subscription consumida por Almacén CxpEventListenerWorker (F3-PR1).
// EventTypes relevantes para Almacén: variante B (factura primero,
// recepción después), ajuste de costo dentro de tolerancia, enlace
// diferido de recepción variante A (cfdi.ingresado backfillea el
// CfdiRecibidoId de recepciones registradas con folio fiscal a mano) y
// nota-credito.registrada (NC fiscal relación 03 → concilia la
// devolución 8.B con ConciliadaConNcFiscal; faltaba en el filtro y la
// conciliación nunca llegaba — incidente verificación e2e 2026-07-15).
// Referencia: backend/src/Almacen/Infrastructure/Workers/CxpEventListenerWorker.cs
resource cxpEventsAlmacenSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: cxpEventsTopic
  name: 'almacen-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource cxpEventsAlmacenSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cxpEventsAlmacenSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'cuentas_por_pagar.factura.registrada.v1\', \'cuentas_por_pagar.factura.diferencia-precio-detectada.v1\', \'cuentas_por_pagar.cfdi.ingresado.v1\', \'cuentas_por_pagar.nota-credito.registrada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// Subscription consumida por Tesorería CuentasPorPagarEventListenerWorker
// (TES-PR3). Alimenta la proyección pasivo_pendiente_pago (bandeja de
// egresos): cada pasivo.autorizado-para-pago se upserta para que Tesorería
// ejecute el pago. Los pasivos autorizados ANTES de crear esta subscription
// requieren backfill one-shot (runbook
// backend/scripts/backfill-pasivos-pendientes-tesoreria.sql).
// Referencia: backend/src/Tesoreria/Infrastructure/Workers/CuentasPorPagarEventListenerWorker.cs
resource cxpEventsTesoreriaSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: cxpEventsTopic
  name: 'tesoreria-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource cxpEventsTesoreriaSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cxpEventsTesoreriaSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'cuentas_por_pagar.pasivo.autorizado-para-pago.v1\', \'cuentas_por_pagar.deposito-viaticos.esperado.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: facturacion-events (módulo Millet.Facturacion)
// Publicado por OutboxPublisherWorker<FacturacionDbContext>. Hasta este PR
// el topic existía sólo creado en runtime/portal; ahora queda declarativo
// en Bicep junto con la primera subscription (CxC). Bicep adopta el topic
// existente si las properties coinciden; si divergen, las alinea — ver
// what-if antes de aplicar.
//
// EventTypes publicados (al 2026-07):
//   - facturacion.factura-venta.timbrada.v1
//   - facturacion.factura-anticipo.timbrada.v1
//   - facturacion.nota-credito.timbrada.v1
//   - facturacion.recibo-pago.timbrado.v1
//   - facturacion.comprobante.cancelado.v1
//   - facturacion.caja-sesion.abierta.v1        (informativo)
//   - facturacion.caja-sesion.cerrada.v1        (informativo)
//   - facturacion.cobro-mostrador.registrado.v1
//   - facturacion.cobro-mostrador.cancelado.v1
// ============================================================================

resource facturacionEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'facturacion-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por CxC FacturacionEventListenerWorker (CXC-PR3).
// Alimenta la proyección factura_cartera: alta (factura-venta timbrada),
// pagos (REPP + cobro mostrador ± cancelado), NC, reversa (comprobante
// cancelado) y anticipos (informativo, estado de cuenta). Filtra a los 7
// EventTypes que el worker reconoce — los eventos de caja-sesión quedan
// fuera del filter (CxC no los consume).
// Referencia: backend/src/CuentasPorCobrar/Infrastructure/Workers/FacturacionEventListenerWorker.cs
resource facturacionEventsCxcSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: facturacionEventsTopic
  name: 'cuentas-por-cobrar-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource facturacionEventsCxcSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: facturacionEventsCxcSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'facturacion.factura-venta.timbrada.v1\', \'facturacion.recibo-pago.timbrado.v1\', \'facturacion.cobro-mostrador.registrado.v1\', \'facturacion.cobro-mostrador.cancelado.v1\', \'facturacion.nota-credito.timbrada.v1\', \'facturacion.comprobante.cancelado.v1\', \'facturacion.factura-anticipo.timbrada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// Subscription consumida por Tesorería FacturacionEventListenerWorker
// (TES-PR7): caja-sesion.cerrada genera la expectativa de depósito
// Caja→Banco (cierra PLATFORM-TODO <TesoreriaCajaSesion>) y
// recibo-pago.timbrado marca repp_timbrado en la confirmación (ciclo
// fiscal cerrado).
// Referencia: backend/src/Tesoreria/Infrastructure/Workers/FacturacionEventListenerWorker.cs
resource facturacionEventsTesoreriaSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: facturacionEventsTopic
  name: 'tesoreria-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource facturacionEventsTesoreriaSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: facturacionEventsTesoreriaSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'facturacion.caja-sesion.cerrada.v1\', \'facturacion.recibo-pago.timbrado.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: cuentas-por-cobrar-events (módulo Millet.CuentasPorCobrar)
// Publicado por OutboxPublisherWorker<CuentasPorCobrarDbContext>. Primer
// publicador: CXC-PR4 (DecisionLiberacionEmitidaEvent). Primera
// subscription: Tesorería (TES-PR7, propuestas de aplicación). El
// write-back de liberación a A+W (CXC-PR9, bloqueado por contrato) y
// Notificaciones agregarán las suyas siguiendo el patrón.
//
// EventTypes publicados (al 2026-07):
//   - cuentas_por_cobrar.decision-liberacion.emitida.v1
//   - cuentas_por_cobrar.propuesta-aplicacion.creada.v1
//   - cuentas_por_cobrar.alerta-cartera.generada.v1
// ============================================================================

resource cxcEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'cuentas-por-cobrar-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por Tesorería CuentasPorCobrarEventListenerWorker
// (TES-PR7): cada propuesta-aplicacion.creada se proyecta a la bandeja de
// depósitos por confirmar (deposito_confirmacion Pendiente) — CxC propone,
// Tesorería confirma (TES-9). Las propuestas creadas ANTES de esta
// subscription no llegan; en dev se re-proponen desde CxC si hace falta.
// Referencia: backend/src/Tesoreria/Infrastructure/Workers/CuentasPorCobrarEventListenerWorker.cs
resource cxcEventsTesoreriaSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: cxcEventsTopic
  name: 'tesoreria-subscription'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource cxcEventsTesoreriaSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: cxcEventsTesoreriaSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'cuentas_por_cobrar.propuesta-aplicacion.creada.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Topic: tesoreria-events (módulo Millet.Tesoreria)
// Desde TES-PR1 Tesorería es un módulo del ERP; el publisher real
// (OutboxPublisherWorker<TesoreriaDbContext>) llega en TES-PR4 con los 4
// eventos espejo congelados. La subscription `cuentas-por-pagar-tesoreria-sub`
// la consume TesoreriaEventListenerWorker (F9-PR1) en CxP para cerrar el ciclo
// PasivoAutorizadoParaPago → PagoAplicado → REPP recibido.
//
// Cuando exista la subscription `compras-tesoreria-sub` (PLATFORM-TODO
// <TesoreriaEventListenerCompras>, requiere proyección local de facturas en
// Compras), agregar aquí siguiendo el mismo patrón.
// ============================================================================

resource tesoreriaEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'tesoreria-events'
  properties: {
    defaultMessageTimeToLive: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// Subscription consumida por CxP TesoreriaEventListenerWorker (F9-PR1).
// Eventos: pago aplicado/revertido (actualiza sub-estado Pago + balance del
// pasivo), REPP recibido (registra CFDI de pago), cancelación de pasivo
// solicitada por Tesorería (devuelve pasivo a CxP para revisión).
// Referencia: backend/src/CuentasPorPagar/Infrastructure/Workers/TesoreriaEventListenerWorker.cs
resource tesoreriaEventsCxpSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: tesoreriaEventsTopic
  name: 'cuentas-por-pagar-tesoreria-sub'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource tesoreriaEventsCxpSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: tesoreriaEventsCxpSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'tesoreria.pago-factura-proveedor.aplicado.v1\', \'tesoreria.pago-factura-proveedor.revertido.v1\', \'tesoreria.repp-proveedor.recibido.v1\', \'tesoreria.cancelacion-pasivo.solicitada.v1\', \'tesoreria.pago-prestamo-viaticos.aplicado.v1\')'
      compatibilityLevel: 20
    }
  }
}

// Subscription consumida por Facturación TesoreriaEventListenerWorker
// (PR gemelo de TES-PR7): pago-cliente.confirmado dispara EmitirReppCommand
// — la automatización que cierra PLATFORM-TODO <PagoClienteConfirmado>.
// Referencia: backend/src/Facturacion/Infrastructure/Workers/TesoreriaEventListenerWorker.cs
resource tesoreriaEventsFacturacionSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: tesoreriaEventsTopic
  name: 'facturacion-tesoreria-sub'
  properties: {
    deadLetteringOnMessageExpiration: true
    deadLetteringOnFilterEvaluationExceptions: true
    maxDeliveryCount: 5
    defaultMessageTimeToLive: 'P1D'
    lockDuration: 'PT1M'
    enableBatchedOperations: true
  }
}

resource tesoreriaEventsFacturacionSubscriptionFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: tesoreriaEventsFacturacionSubscription
  name: 'EventTypeFilter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'user.EventType IN (\'tesoreria.pago-cliente.confirmado.v1\')'
      compatibilityLevel: 20
    }
  }
}

// ============================================================================
// Outputs
// ============================================================================

output name string = serviceBus.name
output id string = serviceBus.id
output endpoint string = 'sb://${serviceBus.name}.servicebus.windows.net'

@description('Nombre del topic del módulo Integraciones.Aw — el publisher lo usa para enviar eventos.')
output awEventsTopicName string = awEventsTopic.name

@description('Nombre de la subscription consumida por AwDropWorker.')
output awEventsDropSubscriptionName string = awEventsDropSubscription.name

@description('Nombre del topic del módulo Compras — publica OC autorizada/cancelada/cerrada y otros eventos OC.')
output comprasEventsTopicName string = comprasEventsTopic.name

@description('Nombre de la subscription consumida por ComprasEventListenerWorker (módulo CxP).')
output comprasEventsCxpSubscriptionName string = comprasEventsCxpSubscription.name

@description('Nombre del topic del módulo Almacén — publica recepciones / devoluciones / saldos.')
output almacenEventsTopicName string = almacenEventsTopic.name

@description('Nombre de la subscription en almacen-events consumida por CxP AlmacenEventListenerWorker.')
output almacenEventsCxpSubscriptionName string = almacenEventsCxpSubscription.name

@description('Nombre de la subscription en almacen-events consumida por Compras AlmacenEventListenerWorker.')
output almacenEventsComprasSubscriptionName string = almacenEventsComprasSubscription.name

@secure()
output primaryConnectionString string = rootAuthRule.listKeys().primaryConnectionString
