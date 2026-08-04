# Anticipos ciclo completo + Trazabilidad transversal — Adenda al módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 del back-office (Facturación CFDI 4.0).
> **Versión:** 1.0
> **Fecha:** 2026-07-12
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
> **Estado:** Diseño cerrado para implementación.
>
> **Documentos previos obligatorios:** [`00-levantamiento.md`](00-levantamiento.md)
> (anticipos §6, cancelación §10), [`01-diseno.md`](01-diseno.md) (modelo del
> dominio §4, Decisión 01-G §2.bis, permisos §10, endpoints §11),
> [`03-pr-breakdown.md`](03-pr-breakdown.md) (Fase 13) y
> [`12-cajas.md`](12-cajas.md) (Capa A — alcance de datos). Esta adenda **no**
> renumera ni reescribe esos documentos; los extiende, y **absorbe el
> "alcance diferido" de la Fase 13** (acciones de ciclo de vida para
> `FacturaAnticipo` sin página de detalle propia).

---

## 0. Cómo leer este documento

- `[Verificado]` — leído del código actual en `main` (post #530/#534/#535/#536).
- `[Decisión 13-X]` — decisión de esta adenda, cerrada con el owner (2026-07-12).

---

## 1. Objetivo y alcance

El módulo de anticipos entregó en F4/FE-F4 la **emisión** (M1), la
**vinculación/amortización** (M2/M3) y el **reporte** Control de Anticipos,
pero la factura de anticipo no existe como *unidad de trabajo operable*: no
hay bandeja ni detalle, no hay descarga de XML/PDF, y las acciones de ciclo
de vida (cancelar / reintentar / descartar) solo son alcanzables por API o
no aplican a su familia. Detonante: pruebas del owner 2026-07-11/12 tras
destrabar la serie FANT (#526/#528).

**Alcance de esta adenda:**

1. **Bandeja + detalle** de facturas de anticipo (master-detail P1/P3) en
   rutas aditivas `/facturacion/anticipos/facturas[/$id]` — el Control de
   Anticipos queda intacto en `/facturacion/anticipos`.
2. **Detalle enriquecido** backend: relaciones CFDI, saldo y estado del
   `Anticipo`, vinculaciones con la factura final y su NC de amortización,
   campos de error de timbrado para el banner de #530.
3. **Descargas XML/PDF genéricas** para `FacturaAnticipo` **y**
   `NotaCredito` (generalización de las queries hoy acopladas a
   `FacturasVenta`).
4. **Acciones de ciclo de vida en el detalle**: Cancelar (reusa el flujo
   genérico existente), Reintentar timbrado y Descartar (componentes
   compartidos de #530/#535), bitácora de intentos.
5. **Trazabilidad transversal**: endpoint propio de Facturación que arma el
   árbol documento-céntrico (pedido → comprobantes → NC/REPP) y componente
   FE montado en **todos** los detalles del módulo (factura, anticipo,
   carta porte, REPP, pedido).
6. **Fix 13-J**: relación 07 faltante al amortizar un anticipo cuyo CFDI no
   quedó timbrado (bug observado: `VEN-000003` solo se relacionó con
   `FACANT-2026-000003` y no con `FACANT-2026-000002`).

**No-alcance (diferido):**

- Página completa (bandeja/detalle) de **Notas de crédito** — la NC se
  opera desde los documentos que la generan; en el árbol aparece como chip
  informativo sin ruta.
- Formato **térmica** para PDF de anticipo y NC (solo bilingüe).
- Trazabilidad multi-nivel (el componente pinta un nivel por lado; la
  cadena completa se navega saltando de detalle en detalle).

---

## 2. Estado actual `[Verificado]`

| Pieza | Estado | Dónde |
|---|---|---|
| Bandeja backend `GET /anticipos/facturas` | ✅ existe, **sin consumidor FE** | `Application/Anticipos/Queries/FacturasAnticipoQueries.cs` |
| Detalle backend `GET /anticipos/facturas/{id}` | ✅ existe, **pobre** (sin relaciones/saldo/NC/error timbrado) | ídem |
| Cancelación de anticipo | ✅ end-to-end genérica (valida cadena, `anticipo.Cancelar()`, poller) | `Application/Cancelaciones/`, `ComprobantesEndpoints.cs` |
| Reintentar timbrado | ✅ #530 — genérico 5 tipos, permiso `facturacion.comprobantes.reintentar-timbrado`, banner FE compartido | `Application/Timbrado/ReintentarTimbrado/`, `TimbradoFallidoBanner.tsx` |
| Descartar + bitácora | ✅ #534/#535 — `Descartada=8`, permiso `facturacion.comprobantes.descartar`, efectos por tipo (anticipo → cancela saldo), `IntentosTimbradoPanel` | `Application/Timbrado/DescartarComprobante/`, `IntentosTimbradoQuery.cs` |
| Descarga XML/PDF | ⚠️ solo `FacturasVenta` (`FacturaXmlQuery`/`FacturaPdfQuery`); **sin alcance de cajas** | `Application/Facturas/Queries/` |
| PDF | ⚠️ `IGenerarPdfFacturaPort.Generar(FacturaVenta, formato)` tipado a FV | `Domain/Ports/`, `Infrastructure/Pdf/QuestPdfFacturaGenerator.cs` |
| NC de amortización | ✅ creada/timbrada en `AmortizarAnticiposAsync` (relación 07 doble) | `EmitirFacturaVentaHandler.cs` |
| Trazabilidad FE | ✅ componente genérico por props, usado solo por Compras (tipos 0–4) | `frontend/src/components/erp/trazabilidad/` |
| Saldo `Anticipo` con CFDI no timbrado | 🐞 se abre igual y es amortizable sin relación 07 (ver §3 13-J) | `EmitirFacturaAnticipoHandler.cs`, `EmitirFacturaVentaHandler.cs` L220-222 |

---

## 3. Decisiones

| # | Decisión |
|---|---|
| `[Decisión 13-A]` | El detalle se enriquece **extendiendo** `FacturaAnticipoDetalleResponse` (mismo endpoint; aditivo, el FE aún no lo consume). Enriquecimiento de relaciones por UUID con el mismo patrón de `ComprobanteDetalleQuery`. |
| `[Decisión 13-B]` | Descargas **genéricas polimórficas** `ComprobanteXmlQuery`/`ComprobantePdfQuery` sobre la raíz TPT `Comprobantes`, con parámetro `TipoComprobanteEsperado` fijado por cada endpoint (un id de factura de venta no es descargable con permiso de anticipos) y **alcance de cajas Capa A** aplicado — esto corrige de paso la brecha de alcance de las descargas de factura de venta. `FacturaXmlQuery`/`FacturaPdfQuery` se eliminan; `/facturas/{id}/xml|pdf` migran internamente sin cambiar contrato HTTP. |
| `[Decisión 13-C]` | PDF por sobrecargas en `IGenerarPdfFacturaPort`: anticipo = plantilla bilingüe con tabla de un solo concepto (los campos fiscales viven en la entidad); NC = variante **Egreso** ("NOTA DE CRÉDITO / CREDIT NOTE", motivo, tabla de relaciones 01/07). Sin térmica para estas familias (el parámetro se ignora). REPP/Carta Porte → 422 `PDF_NO_SOPORTADO`. |
| `[Decisión 13-D]` | Trazabilidad con **endpoint propio de Facturación** (`GET /comprobantes/{id}/arbol-documentos`, `GET /pedidos/{id}/arbol-documentos`) que resuelve un nivel de ascendientes/descendientes desde FKs + relaciones CFDI + `ReciboPagoFactura` + `AnticipoVinculacion` + `CartaPortePreviaId`. El shape espejo del árbol de Compras; un componente FE `TrazabilidadFacturacion` montado en los 5 detalles del módulo. Facturación **no** referencia `Compras.Domain` (enum propio; el mirror FE comparte numeración, ver §6.3). |
| `[Decisión 13-E]` | **Cero permisos nuevos** (sin migration de Identidad): descargas de anticipo con `facturacion.anticipos.leer`, de NC con `facturacion.notas-credito.leer`; árbol con el permiso `leer` de la familia raíz; cancelar/reintentar/descartar con los permisos ya existentes. |
| `[Decisión 13-F]` | Rutas FE aditivas. En TanStack Router el segmento estático `facturas` gana al dinámico `$clienteId` — `/anticipos/facturas` convive con `/anticipos/$clienteId` (cubierto con test). Card nueva "Facturas de anticipo" en el nav (sección Operación, permiso `anticipos.leer`). |
| `[Decisión 13-G]` | `CancelarForm` + banner de estatus se **extraen** de `DetalleFactura.tsx` a `components/CancelarComprobanteForm.tsx` (los hooks ya son genéricos sobre `/comprobantes/{id}/cancelar`); las invalidaciones de cancelar/reintentar/descartar se extienden a las keys de anticipos. |
| `[Decisión 13-H]` | La bandeja incluye el saldo: `FacturaAnticipoBandejaItem` + `EstadoAnticipo`/`Saldo` (left-join a `anticipos`). El filtro de estado incluye `Descartada`. |
| `[Decisión 13-I]` | El `IntentosTimbradoPanel` en el detalle de anticipo se gatea por `facturacion.facturas.leer` (permiso del endpoint de bitácora; no se amplía el endpoint). |
| `[Decisión 13-J]` | **Fix relación 07 faltante.** Causa raíz: `EmitirFacturaVentaHandler` omite silenciosamente la relación 07 si la factura de anticipo no tiene UUID, y el saldo `Anticipo` se abre aunque el timbre falle → un anticipo con CFDI `TimbradoFallido`/`EnProceso` es seleccionable y amortizable sin rastro fiscal. Fix doble: (a) validación dura `ANTICIPO_CFDI_NO_TIMBRADO` (422) en `CargarYValidarAnticiposAsync` y en `VincularAnticipoHandler` si `FacturaAnticipo.Estado != Timbrado`; (b) el `AnticipoPicker` no ofrece anticipos sin CFDI timbrado (`EstadoCfdi` expuesto en estado de cuenta / control; el estado de cuenta los sigue mostrando, marcados). |
| `[Decisión 13-K]` | **Saldo por cobrar neto de NC (2026-07-13).** Las NC **siempre acreditan al saldo** de la factura relacionada — nunca se reembolsan en efectivo (cerrado con el owner). Cálculo canónico en `SaldoPorCobrar` (Application/Facturas): *por cobrar = Total − Σ NC timbradas con `FacturaRelacionadaId` (amortización 07 y generales) − Σ pagos previos (REPP)*. Aplica en: `EmitirReppHandler` (`SaldoAnterior`/`SaldoInsoluto` del complemento de pago — dato fiscal; nuevo 422 `REPP_FACTURA_SIN_SALDO`), `CobroMostradorRegistrador` (total esperado neto; nuevo 422 `COBRO_SIN_SALDO`), `ListarComprobantesCobrables` (`Total` neto + `MontoAcreditado`; oculta saldadas) y `ComprobanteDetalleQuery` (`TotalAcreditado`/`TotalPorCobrar`/`NotasCreditoAplicadas`). Antes: el mostrador exigía el total bruto del CFDI (doble cobro del anticipo ya cobrado en caja) y el REPP timbraba saldos sobrestimados. |

---

## 4. Contratos backend

### 4.1 Detalle enriquecido (`GET /anticipos/facturas/{id}`)

```csharp
public sealed record FacturaAnticipoDetalleResponse(
    /* ...campos actuales sin cambio... */
    // 13-A
    string? TimbradoErrorCodigo, string? TimbradoErrorMensaje,
    IReadOnlyList<RelacionCfdiDetalle> Relaciones,
    AnticipoSaldoDetalle? Anticipo);

public sealed record AnticipoSaldoDetalle(
    Guid AnticipoId, Guid ClienteId, string Estado,
    decimal MontoCobrado, decimal MontoAmortizado, decimal Saldo, decimal SaldoDisponible,
    string? PedidoOrigenRef, long? ObraId, string? ObraNombre,
    IReadOnlyList<AnticipoVinculacionInfo> Vinculaciones);

public sealed record AnticipoVinculacionInfo(
    Guid FacturaVentaId, string? FacturaFolio, string? FacturaUuid, string? FacturaEstado,
    decimal Importe, DateTimeOffset CreadoEn,
    Guid? NcAmortizacionId, string? NcFolio, string? NcUuid, string? NcEstado);
```

Bandeja: `FacturaAnticipoBandejaItem` + `string? EstadoAnticipo, decimal? Saldo`.
Reportes: `ControlAnticipoFila` y `AnticipoEstadoCuenta` + `Guid FacturaAnticipoId`
(enlace cruzado) y `string EstadoCfdi` (13-J).

### 4.2 Descargas genéricas

```csharp
public sealed record ComprobanteXmlQuery(Guid ComprobanteId, TipoComprobante? TipoEsperado)
    : IRequest<ComprobanteXmlResponse>;
public sealed record ComprobantePdfQuery(Guid ComprobanteId, FormatoPdfFactura Formato,
    TipoComprobante? TipoEsperado) : IRequest<PdfFacturaGenerado>;
```

Errores: `COMPROBANTE_NO_ENCONTRADO` (404, también fuera de alcance de caja o
tipo distinto al esperado), `COMPROBANTE_SIN_XML` (422), `PDF_NO_SOPORTADO` (422).

### 4.3 Árbol de trazabilidad

```csharp
public enum TipoNodoTrazabilidadFacturacion : short
{ PedidoFacturable = 5, FacturaVenta = 6, FacturaAnticipo = 7,
  NotaCredito = 8, ReciboPago = 9, CartaPorte = 10 }   // 0–4 reservados Compras

public sealed record NodoTrazabilidadFacturacion(
    short Tipo, Guid Id, string Folio, string Estado, string? Uuid,
    decimal? Total, DateTimeOffset? Fecha);

public sealed record ArbolDocumentosFacturacionResponse(
    NodoTrazabilidadFacturacion Actual,
    IReadOnlyList<NodoTrazabilidadFacturacion> Ascendientes,
    IReadOnlyList<NodoTrazabilidadFacturacion> Descendientes);
```

Resolución por tipo de raíz (un nivel por lado):

| Raíz | Ascendientes | Descendientes |
|---|---|---|
| PedidoFacturable | — | FV/FANT/CP con `PedidoFacturableId` = id |
| FacturaVenta | pedido, FANT relacionadas (07) | NC (`FacturaRelacionadaId`), REPP (`ReciboPagoFactura`) |
| FacturaAnticipo | pedido (si hay) | FV vinculadas + NC de amortización (vía `AnticipoVinculacion`) |
| NotaCredito | FANT (`AnticipoOrigenId`), FV (`FacturaRelacionadaId`) | — |
| ReciboPago | FV pagadas (`ReciboPagoFactura`) | — |
| CartaPorte | pedido, CP previa (`CartaPortePreviaId`) | CP siguiente |

---

## 5. Endpoints

| Método | Ruta | Permiso | PR |
|---|---|---|---|
| GET | `/api/v1/facturacion/anticipos/facturas/{id}/xml` | `facturacion.anticipos.leer` | PR1 |
| GET | `/api/v1/facturacion/anticipos/facturas/{id}/pdf` | `facturacion.anticipos.leer` | PR1 |
| GET | `/api/v1/facturacion/notas-credito/{id}/xml` | `facturacion.notas-credito.leer` | PR1 |
| GET | `/api/v1/facturacion/notas-credito/{id}/pdf` | `facturacion.notas-credito.leer` | PR1 |
| GET | `/api/v1/facturacion/comprobantes/{id}/arbol-documentos` | `leer` de la familia del comprobante | PR3 |
| GET | `/api/v1/facturacion/pedidos-facturables/{id}/arbol-documentos` | `facturacion.pedidos.leer` | PR3 |

Los existentes `/facturas/{id}/xml|pdf` no cambian de contrato (migran a las
queries genéricas y ganan alcance de cajas).

---

## 6. Frontend

### 6.1 Rutas y páginas

| Ruta | Página | Patrón |
|---|---|---|
| `/facturacion/anticipos/facturas` | `BandejaFacturasAnticipo` | P1 (molde `BandejaFacturas`) |
| `/facturacion/anticipos/facturas/$id` | `FacturasAnticipoLayout` + `DetalleFacturaAnticipo` | P3 (molde `FacturasLayout`/`DetalleFactura`) |

Detalle: sub-topbar con XML / PDF / Imprimir / Cancelar; `TimbradoFallidoBanner`
(Reintentar + Descartar); `IntentosTimbradoPanel` (13-I); banner de estatus de
cancelación; tabs Encabezado / Concepto / **Saldo y cadena** (KPIs
Cobrado/Amortizado/Saldo/Disponible + tabla de vinculaciones con links a la
factura final y descargas XML/PDF de la NC gateadas por `notas-credito.leer`)
+ sección de trazabilidad (PR3).

### 6.2 Reuso

`descargarArchivo` (lib/descargas.ts) · `ChipTimbrado` · `TimbradoFallidoBanner`
· `IntentosTimbradoPanel` · extracción `CancelarComprobanteForm` ·
`useSolicitarCancelacion`/`useCancelacionEstatus`/`useReintentarTimbrado`/
`useDescartarComprobante` (solo invalidaciones ampliadas) · `ArbolDocumentos`/
`NodoDocumento` (erp/trazabilidad).

### 6.3 Trazabilidad — tipos 5–10

El enum FE `TipoDocumentoTrazabilidad` (mirror de Compras 0–4) se extiende con
los valores 5–10 de §4.3. Rutas por tipo: Pedido → `/facturacion/pedidos/$id`,
FV → `/facturacion/facturas/$id`, FANT → `/facturacion/anticipos/facturas/$id`,
REPP → `/facturacion/repp/$id`, CP → `/facturacion/carta-porte/$id`,
NC → sin ruta (chip). `TrazabilidadFacturacion(tipo, id)` consume el endpoint
de §5 y se monta en `DetalleFactura`, `DetalleFacturaAnticipo`,
`DetalleCartaPorte`, `DetalleRepp` y `DetallePedido`.

---

## 7. RBAC

Sin permisos nuevos. Por endpoint: ver §5. Acciones del detalle gateadas por
los permisos existentes (`cancelaciones.solicitar`,
`comprobantes.reintentar-timbrado`, `comprobantes.descartar`,
`facturas.leer` para la bitácora — 13-I).

---

## 8. Plan de PRs

| PR | Rama | Contenido |
|---|---|---|
| PR0 (S) | `facturacion/anticipos-pr0-doc-ciclo-completo` | Este documento + referencias cruzadas (01/02/03/07/10/11). |
| PR1 (M) | `facturacion/anticipos-pr1-detalle-xml-pdf` | Detalle enriquecido + bandeja con saldo; descargas genéricas (13-B) + sobrecargas PDF (13-C); endpoints §5 (xml/pdf); `FacturaAnticipoId`/`EstadoCfdi` en reportes; **fix 13-J backend**; unit tests. Sin migrations. |
| PR2 (M) | `facturacion-fe/anticipos-pr2-bandeja-detalle` | Bandeja + detalle (P1/P3) con todas las acciones; extracción `CancelarComprobanteForm`; hooks/keys/types; enlaces cruzados (Control/EstadoCuenta/nav); **fix 13-J frontend** (picker filtra no timbrados); smoke tests. |
| PR3 (M) | `facturacion/anticipos-pr3-trazabilidad` + `facturacion-fe/anticipos-pr3-trazabilidad` | Query + endpoints del árbol (§4.3/§5); extensión trazabilidad FE (tipos 5–10) + `TrazabilidadFacturacion` montado en los 5 detalles; tests por tipo de raíz. |
| PR4 (S) | `facturacion/saldo-por-cobrar` | **Decisión 13-K backend**: helper `SaldoPorCobrar`; REPP resta NC del saldo (+ `REPP_FACTURA_SIN_SALDO`); mostrador cobra el neto (+ `COBRO_SIN_SALDO`); cobrables neto + `MontoAcreditado`; detalle con `TotalAcreditado`/`TotalPorCobrar`/`NotasCreditoAplicadas`; unit tests. Sin migrations. |
| PR5 (S) | `facturacion-fe/saldo-por-cobrar` | **Decisión 13-K frontend**: Mi Caja muestra el por cobrar con el acreditado desglosado; detalle de factura con sección "NC aplicadas" y monto por cobrar; types/tests. |
| PR6 (S) | `facturacion/repp-factura-picker` + `facturacion-fe/repp-factura-picker` | **Cierra PLATFORM-TODO `<FacturaPicker>`** sobre el saldo canónico 13-K: `GET /repp/facturas-cobrables` (PPD timbradas con saldo > 0, NC + REPP vigentes descontados, parcialidad siguiente); `PagosVigentes` excluye REPP Cancelado/Descartada (también en `EmitirReppHandler`); FE `FacturaPpdPicker` en `NuevoRepp` (sin GUID, prefill del saldo, mismo cliente). |

---

## 9. Verificación

Automática: `Facturacion.UnitTests` (detalle enriquecido; descargas
polimórficas: 404 fuera de alcance / tipo distinto, 422 sin XML,
`PDF_NO_SOPORTADO`; generator anticipo/NC; emisión con 2 anticipos timbrados
→ 2 relaciones 07 + 2 NCs; anticipo no timbrado → `ANTICIPO_CFDI_NO_TIMBRADO`;
árbol por tipo de raíz) · FE `tsc` + eslint + vitest (smokes nuevos +
regresión de los 4 detalles existentes y nav).

E2E en dev: emitir anticipo → bandeja → detalle (descargas, imprimir) →
factura final con 2 anticipos → relaciones y árbol en ambos sentidos →
descargas de NC → cancelación (con y sin NCs vigentes) → fallo de timbre →
reintentar/descartar → RBAC y alcance de cajas. Caso reportado: verificar el
estado de `FACANT-2026-000002` (se espera no-timbrada; explica la relación
faltante de `VEN-000003`).

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-07-12 | Adenda inicial: bandeja/detalle de facturas de anticipo, descargas XML/PDF genéricas (anticipo+NC), acciones de ciclo de vida en el detalle, trazabilidad transversal (tipos 5–10) y fix 13-J (relación 07 faltante con CFDI de anticipo no timbrado). |
| 1.1 | 2026-07-13 | Decisión 13-K: saldo por cobrar neto de NC (REPP, cobro mostrador, cobrables, detalle) + PR4/PR5. |
