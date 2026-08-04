# 08 — Operación y runbook del módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.

---

## 1. Estado del módulo

Módulo de emisión de CFDI 4.0. Consume `Integraciones.Fiscal` (timbrado),
ingesta desde A+W (cola de solicitudes) y Planta Pintura, write-back a A+W.
Workers `IHostedService` en `Millet.Api`. Esquema `facturacion`.

**Dependencias en runtime:**
- `Integraciones.Fiscal` (timbrado/cancelación; stub hasta su fase 2).
- Azure Hybrid Connection a `SER-DATA` (vistas A+W + tabla-puente).
- Service Bus (`facturacion-events`), Key Vault (CSD), Blob (XML/PDF).
- `Millet.Catalogos`, `Compartido.Series`, `DatosMaestros`.

---

## 2. Despliegue

- `FacturacionDbContext` en el bucle de migraciones de `deploy-app-*.yml` y en
  `MigrationsHealthCheckOptions.ContextTypes`. Validar `/health/ready`.
- Variables: FiscalAPI (sandbox/prod), Hybrid Connection, Service Bus, KV.
- Workers se levantan con el host; verificar logs de arranque de cada uno.
- Regla infra: `what-if` antes de `create` en cualquier ambiente no-sandbox.

---

## 3. Observabilidad

- Logs Serilog → App Insights, correlación por `uuid` / `pedido_facturable_id`
  / `solicitud_id`.
- Métricas clave: timbres OK/fallidos, facturas en `TimbradoEnProceso` y
  `PendientePedimento`, backlog `aw_solicitud_pedido`, excepciones de ingesta,
  latencia FiscalAPI, envíos de correo fallidos.
- Alertas: `TimbradoEnProceso` estancado >N min, backlog de cola creciente,
  cambio A+W sobre pedido `Facturado`, envío de correo reincidente.

---

## 4. Troubleshooting recetas

**Timbre estancado en `TimbradoEnProceso`.** Revisar logs del adapter
FiscalAPI / stub. Confirmar que `TimbradoPendienteWorker` corre. Si el PAC
rechazó, pasa a `TimbradoFallido`; ahí el reintento es **sobre el mismo
comprobante, con el mismo folio** ([Decisión 01-G] — acción "Reintentar
timbrado" en el detalle de la factura), nunca emitiendo una factura nueva.

**Cola `aw_solicitud_pedido` no avanza.** Verificar Hybrid Connection a
`SER-DATA`, permisos sobre la tabla-puente, y que `AwSolicitudesWorker` esté
vivo. Revisar `resultado=Error` en la cola (motivo registrado).

**Pedido cambió en A+W pero no se refleja.** Confirmar que A+W escribió la
solicitud (Modificación) con `version` mayor. Si el pedido está `Bloqueado`
(facturándose) el cambio se pospone; si está `Facturado`, va a revisión
manual (no overwrite).

**Claim "huérfano" o doble ingesta.** No debería ocurrir: el claim persiste y
`ingesta_control` es la fuente de verdad. Si A+W reescribió la cola, el worker
re-evalúa por hash. Revisar `ingesta_control` por `clave_natural`.

**Cliente/artículo no se auto-provisionó (A+W).** Verificar `IMasterProvisioningPort`
(stub vs real) y los readers de master de A+W. Origen Planta Pintura: debe
crearse en el ERP antes (no auto-provisiona).

**Re-facturar tras cancelar.** Tras cancelar el CFDI vigente, el pedido vuelve
a `Importado`. Si no aparece re-facturable, revisar que la cancelación se
aplicó (estado `Cancelado` del comprobante) y que `comprobante_vigente_id`
quedó null.

**Factura de exportación no timbra.** Revisar `requiere_pedimento`: si está en
`PendientePedimento`, falta el pedimento de Salidas (ver `PedimentoSalidasWorker`).

---

## 5. FAQs operativas

- *¿Por qué un anticipo no amortiza por su total?* Saldo amortizable = cobrado
  − amortizado; solo lo cobrado amortiza.
- *¿Por qué la bonificación no aparece en la factura?* Va por NC posterior
  (estrategia comercial).
- *¿Puedo modificar una factura timbrada?* No; es inmutable. Cancela +
  sustituye (relación 04).
- *¿Una obra con varias estimaciones?* Cada estimación es un pedido facturable
  discreto (no hay "parcialmente facturado").

---

## 5.bis Timbrado real contra FiscalAPI (F12)

### Registro por ambiente

Desde F12-PR3 el timbrado real es **siempre** `FiscalApiTimbradoAdapter` — el
flag `Facturacion__Timbrado__UsarPacReal` y los stubs de emisión se
eliminaron (un ambiente sin credenciales PAC falla **visible**, nunca emite
UUIDs falsos). Requiere: SDK habilitado
(`IntegracionesFiscal__Sdk__Disabled=false` + secreto `fiscalapi-tenant-key`
en el KV) y ApiKey por empresa capturada en ConfiguracionPac (sandbox:
`sk_test` + BaseUrl `https://test.fiscalapi.com` con el switch "Modo
sandbox").

### Onboarding del emisor (una vez por empresa / renovación de CSD)

1. Capturar el **CP fiscal** de la empresa (Admin → Empresas — es el
   `LugarExpedicion` del CFDI 4.0; sin él la emisión falla con
   `EMISOR_SIN_LUGAR_EXPEDICION`). La razón social debe ir **SIN régimen
   societario** ("SA DE CV") o el SAT rechaza con CFDI40139.
2. Capturar el **CSD del RFC emisor** en Integraciones Fiscal →
   Configuración del PAC (sección "CSD del emisor"): .cer + .key + password.
   Se persiste **cifrado** (DataProtection, mismo esquema que el ApiKey,
   ADR-0037) y viaja en cada timbrado como `Issuer.TaxCredentials` — la
   emisión "por valores" de FiscalAPI lo exige en el request; los
   certificados subidos al Person del dashboard NO aplican a este flujo
   (esos son para la FIEL del receptor / descarga masiva, PR-10). Sin CSD
   capturado el timbrado falla pre-vuelo con `CSD_NO_CONFIGURADO` (no llama
   al PAC). La captura **valida el trío al guardar** (incidente 2026-07-11,
   "The .KEY's password is incorrect" hasta el timbrado): la password debe
   abrir la llave (`CONFIG_PAC_CSD_PASSWORD_INCORRECTA`), el .cer y el .key
   deben ser del mismo par (`CONFIG_PAC_CSD_NO_CORRESPONDEN`) y el
   certificado debe estar vigente (`CONFIG_PAC_CSD_VENCIDO`). Cambiar de
   sandbox a live NO limpia el CSD: hay que rotarlo al CSD real (uno de
   prueba en live falla visible en el PAC).
3. Sandbox (dev): el SAT solo acepta **personas de la LCO sintética**
   (docs.fiscalapi.com/testing-data) — emisor **y receptor**; con RFC real
   el sandbox rechaza con error 305 (vigencia CSD) o CFDI40145/40148
   (receptor fuera de la lista). NO se cambia el RFC de la empresa: en la
   ConfiguracionPac (modo sandbox) se capturan **identidades de prueba**
   que sustituyen emisor/receptor solo en el payload al PAC
   (`FiscalApiTimbradoAdapter`, log `[SANDBOX]`). El snapshot del
   comprobante en el ERP conserva los datos reales; el XML timbrado queda
   con la persona de prueba (p.ej. EKU9003173C9 "ESCUELA KEMPER URGATE",
   régimen 601, CP 42501). El **CSD de prueba** de esa persona (ZIP en
   docs.fiscalapi.com/testing-data, password `12345678a`) se captura en la
   misma sección "CSD del emisor" de la ConfiguracionPac sandbox.
   Receptores genéricos (XAXX/XEXX) y extranjeros NO se sustituyen. La
   captura de identidades se rechaza con BaseUrl ≠ test.fiscalapi.com
   (`CONFIG_PAC_IDENTIDAD_REQUIERE_SANDBOX`) y cambiar la BaseUrl a live
   las limpia — imposible timbrar en prod con RFC de prueba.

### Folio del CFDI (lo asigna el PAC)

FiscalAPI calcula el atributo `Folio` del XML **internamente** (consecutivo
por RFC emisor) y rechaza requests con folio del cliente ("the invoice's
folio will be calculated internally, must be null"). El folio interno de
Millet (`{serie}-{consecutivo}`, p.ej. COTT-2026-000001) **no viaja al
PAC** — solo la `Serie` — y sigue siendo la referencia operativa en
bandejas y reportes. El folio que asigna FiscalAPI regresa en la respuesta
del timbrado y se persiste en `facturacion.comprobantes.folio_pac`. Ojo: el
consecutivo del PAC es **por RFC, no por serie** — los `folio_pac` de una
misma serie tendrán huecos; es esperado.

### Semántica de errores del timbrado

| Resultado | Qué significa | Acción |
|---|---|---|
| `TimbradoFallido` con `CFDI40xxx` | El SAT rechazó (datos fiscales) | Corregir el dato (nombre receptor sin régimen societario ‒ 40145; CP receptor ‒ 40148; tasa ‒ 40179) y **Reintentar timbrado** desde el detalle (mismo comprobante y folio interno; la emisión se reconstruye con los datos corregidos) |
| `TimbradoFallido` con `CSD_NO_CONFIGURADO` (pre-vuelo, no llama al PAC) | La ConfiguracionPac de la empresa no tiene el CSD del emisor | Capturar .cer/.key/password en Configuración del PAC → "CSD del emisor" y **Reintentar timbrado** |
| `CARTA_PORTE_DATOS_SAT_INCOMPLETOS` (pre-vuelo, sin quemar timbre) | Falta CP/estado del origen o destino, o el vehículo no tiene peso bruto vehicular | Capturar los datos (catálogo de vehículos / datos del tramo) y **Reintentar timbrado** |
| `CCE_DOMICILIO_RECEPTOR_INCOMPLETO` (pre-vuelo, sin quemar timbre) | Falta estado o CP del domicilio del receptor extranjero (CCE 2.0) | Capturar el domicilio en los datos del CCE y **Reintentar timbrado** |
| `TimbradoEnProceso` | Resultado AMBIGUO (timeout/red tras enviar) | NO reintentar a mano; el `TimbradoPendienteWorker` lo pasa a `PAC_TIMEOUT` tras el umbral |
| `TimbradoFallido` con `PAC_TIMEOUT` / `PAC_SIN_RESPUESTA` / `PAC_RESPUESTA_INCOMPLETA` | Fallo AMBIGUO: el PAC pudo haber timbrado sin responder | **ANTES de reintentar**: buscar en el dashboard de FiscalAPI un timbre de este comprobante (la búsqueda por serie+folio es determinista — el folio se retiene, [Decisión 01-G] G1); si existe, adoptarlo manualmente (soporte) — reintentar duplicaría el CFDI ante el SAT. Si NO existe, marcar la casilla de verificación del banner y reintentar (el API exige `confirmarNoDuplicado=true`; sin él responde `REINTENTO_REQUIERE_CONFIRMACION`) |
| Fallida que ya no se emitirá (pedido cancelado en origen, captura errónea de raíz) | El documento no debe reintentarse | **Descartar** en el detalle (`POST /comprobantes/{id}/descartar`): pasa a `Descartada` (terminal, [Decisión 01-G] G3), quema ese folio de forma auditada y libera el pedido (vuelve a `Importado`) |

**Reintento de timbrado** (`POST /api/v1/facturacion/comprobantes/{id}/reintentar-timbrado`,
permiso `facturacion.comprobantes.reintentar-timbrado`): aplica a TODOS los
tipos de comprobante (factura de venta, factura de anticipo, NC, REPP, carta
porte) en estado `TimbradoFallido`. Reabre el MISMO comprobante a Borrador
(el folio interno ya está reservado — no se re-emite, [Decisión 01-G] G1/G2),
reconstruye la emisión desde los datos actuales con fecha CFDI nueva y
replica los efectos post-éxito que el intento fallido omitió (evento de
integración, asiento contable, pedido facturable + write-back A+W). El banner
con el error y el botón viven en el detalle de factura / carta porte / REPP;
para NC y anticipos (sin página de detalle propia) el reintento se invoca por
API. Cada llamada al PAC queda en la bitácora de intentos
(`bitacora_intento_timbrado`, [Decisión 01-G] G4) visible en el detalle.

Worker: `Facturacion:Workers:TimbradoPendiente` (`Disabled` false,
`IntervalSeconds` 300, `UmbralMinutos` 30, `BatchSize` 20).

---

## 6. Inventario de `PLATFORM-TODO` abiertos

| ID | Pieza | Cierre |
|---|---|---|
| `<MasterProvisioningAw>` | Auto-provisión cliente/artículo | Cuando DatosMaestros lo soporte |
| `<IntegracionesOrigenes>` | Planta Pintura + Salidas | Cuando exista el módulo |
| `<WriteBackOrigenes>` | Canal de write-back A+W | Cuando se acuerde el SP/tabla |
| `<PeriodoContableCerrado>` | Candado de período | Cuando exista Contabilidad |
| `<ActivosFijos>` | Catálogo de activos | Módulo Activos Fijos |
| `<EnvioCfdiCliente>` | Notificaciones/Mailbox | Cuando exista el servicio |
| `<RepoCfdiComun>` | Migración de CxP al repo común | F2, coordinado |

---

## 7. Catálogos seed instalados

- Series (`Compartido.Series`): por sucursal-tipo + `FANT` (anticipos).
- `ConceptoContable`: placeholders `TBD-*` + `requiere_codigo_definitivo`.
- Catálogos SAT: vía `Millet.Catalogos` (sync desde FiscalAPI).
- Permisos `facturacion.*` asignados a roles operativos.

---

## 8. Métricas de éxito post go-live

- 100% de facturas emitidas con UUID válido (sin timbres perdidos por errores
  corregibles localmente).
- 0 pedidos duplicados / doble-facturados.
- Backlog de la cola de solicitudes A+W bajo control (procesado < N min).
- 0 incidentes de PAN completo / CSD filtrado.
- Re-facturación tras cancelación trazable end-to-end.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Runbook inicial. |
