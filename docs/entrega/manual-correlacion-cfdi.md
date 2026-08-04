# Manual — Correlación de comprobantes CFDI (cómo leer los XML)

Guía de referencia para el manual de facturación: cómo se **ligan entre sí** los
CFDI que emite Millet (factura, notas de crédito, complemento de pago,
comprobante de traslado) y dónde ver cada liga en el XML timbrado. Todos los
fragmentos de abajo son de **XML reales timbrados en dev** (sandbox FiscalAPI),
etiquetados "P10 verificacion pipeline".

> **Nota sandbox:** en dev el PAC sustituye emisor/receptor por las identidades
> de prueba (`EKU9003173C9` / `CACX7605101P8`); por eso algunos RFC de los
> fragmentos aparecen como `EKU…`. En producción son los RFC reales. Los UUID,
> relaciones y complementos son idénticos en estructura.

## Cómo bajar el XML

Cada familia expone la descarga del XML timbrado (Bearer token):

| Familia | Endpoint |
|---|---|
| Factura de venta | `GET /api/v1/facturacion/facturas/{id}/xml` |
| Factura de anticipo | `GET /api/v1/facturacion/anticipos/facturas/{id}/xml` |
| Nota de crédito | `GET /api/v1/facturacion/notas-credito/{id}/xml` |
| REPP (complemento de pago) | `GET /api/v1/facturacion/repp/{id}/xml` |
| Carta Porte | `GET /api/v1/facturacion/carta-porte/{id}/xml` |

El `{id}` es el **GUID** del comprobante (no el UUID/folio fiscal). El XML sellado
se guarda en `integraciones_fiscal.cfdi_archivo.xml_contenido`. En la app, botón
**"XML"** del detalle. La vista de negocio equivalente es el árbol de documentos:
`GET /api/v1/facturacion/pedidos-facturables/{id}/arbol-documentos`.

---

## 1. Relación 07 — anticipos (únicos y múltiples)

Cuando una factura final amortiza anticipos, la liga vive en **dos** lugares:

**(a) La factura final** apunta a cada anticipo con `TipoRelacion="07"`. Ejemplo
real: `VEN-000024` (MXN) que aplicó **dos** anticipos:

```xml
<cfdi:Comprobante TipoDeComprobante="I" Moneda="MXN" SubTotal="1000.00" Total="1160.00" ...>
  <cfdi:CfdiRelacionados TipoRelacion="07">
    <cfdi:CfdiRelacionado UUID="35bea119-e83c-477f-9528-020924445fd1" />  <!-- FACANT-000009 -->
  </cfdi:CfdiRelacionados>
  <cfdi:CfdiRelacionados TipoRelacion="07">
    <cfdi:CfdiRelacionado UUID="1d853ba6-64fd-4078-a1f5-8d47ecd3c514" />  <!-- FACANT-000010 -->
  </cfdi:CfdiRelacionados>
```

**(b) Una NC de amortización por anticipo**, con `TipoRelacion="07"` **doble**:
al anticipo **y** a la factura final. Ejemplo real: `NCRED-000009` ($290):

```xml
<cfdi:Comprobante TipoDeComprobante="E" Total="290.00" ...>
  <cfdi:CfdiRelacionados TipoRelacion="07">
    <cfdi:CfdiRelacionado UUID="35bea119-e83c-477f-9528-020924445fd1" />  <!-- el anticipo -->
  </cfdi:CfdiRelacionados>
  <cfdi:CfdiRelacionados TipoRelacion="07">
    <cfdi:CfdiRelacionado UUID="79778572-c603-4331-9057-e5ef4e358ce0" />  <!-- la factura VEN-000024 -->
  </cfdi:CfdiRelacionados>
```

> **Aplica igual en MXN y USD.** El único cambio en exportación es que las
> facturas/anticipos van en `Moneda="USD"` con su `TipoCambio`.

## 2. Relación 01 — ranura (NC de bonificación)

La ranura (KO_FALZ) se documenta con una NC automática relación **01** que apunta
a la factura emitida por el total. Ejemplo real: `NCRED-000008` ($116) sobre
`VEN-000015`:

```xml
<cfdi:Comprobante TipoDeComprobante="E" ...>
  <cfdi:CfdiRelacionados TipoRelacion="01">
    <cfdi:CfdiRelacionado UUID="394ffbad-6dc0-41e7-8d4a-9ce66600c8f9" />  <!-- VEN-000015 -->
  </cfdi:CfdiRelacionados>
```

> Solo aplica a ventas nacionales (la ranura viene de la ingesta A+W).

## 3. Complemento de Comercio Exterior (CCE 2.0) — exportación

La factura de exportación lleva `Exportacion="02"`, moneda USD y el nodo
`cce20:ComercioExterior`. Ejemplo real: `VEN-000093` (USD $5,000, IVA 0%):

```xml
<cfdi:Comprobante Moneda="USD" TipoCambio="17.391" Exportacion="02" ...>
  <cfdi:Complemento>
    <cce20:ComercioExterior Version="2.0" ClaveDePedimento="A1" CertificadoOrigen="0"
                            Incoterm="FOB" TipoCambioUSD="17.391" TotalUSD="5000.00">
      <cce20:Receptor NumRegIdTrib="123456789" />
      <cce20:Mercancias>
        <cce20:Mercancia NoIdentificacion="30171708-1" FraccionArancelaria="4011101099"
                         CantidadAduana="500.0" UnidadAduana="06"
                         ValorUnitarioAduana="10.0" ValorDolares="5000.00" />
      </cce20:Mercancias>
    </cce20:ComercioExterior>
  </cfdi:Complemento>
```

**IVA 0 % por exportación** (decisión fiscal: acto gravado a **tasa 0**, no
exento) — en cada línea:

```xml
<cfdi:Traslado Base="5000.00" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.000000" Importe="0.000000" />
```

## 4. Complemento de Pago 2.0 (REPP) — nacional y USD

El REPP liga el pago a la(s) factura(s) PPD que cubre, vía
`pago20:DoctoRelacionado` (por el UUID de la factura). En **USD** el detalle
clave es `EquivalenciaDR` (equivalencia entre la moneda del documento y la del
pago). Ejemplo real: REPP `VEN-000094` que paga la exportación `VEN-000093`:

```xml
<pago20:Pago FechaPago="2026-07-18T21:00:00" FormaDePagoP="03"
             MonedaP="USD" TipoCambioP="17.391" Monto="5000.0" ...>
  <pago20:DoctoRelacionado IdDocumento="496d5f4f-0491-463c-89c4-e9f19b12db29"
                           MonedaDR="USD" EquivalenciaDR="1" NumParcialidad="1"
                           ImpSaldoAnt="5000.00" ImpPagado="5000.0"
                           ImpSaldoInsoluto="0.00" ObjetoImpDR="02">
    <pago20:ImpuestosDR>
      <pago20:TrasladosDR>
        <pago20:TrasladoDR BaseDR="5000.00" ImpuestoDR="002" TipoFactorDR="Tasa"
                           TasaOCuotaDR="0.000000" ImporteDR="0.000000" />
      </pago20:TrasladosDR>
    </pago20:ImpuestosDR>
  </pago20:DoctoRelacionado>
</pago20:Pago>
```

Claves de lectura:
- `IdDocumento` = UUID de la factura pagada (`496d5f4f…` = `VEN-000093`).
- `EquivalenciaDR="1"` porque `MonedaDR` (USD) == `MonedaP` (USD). Si el pago
  fuera en MXN de una factura USD, aquí iría el tipo de cambio. En un REPP MXN de
  factura MXN también es `1`.
- `ObjetoImpDR="02"` + `TrasladoDR` a **tasa 0.000000** = el desglose de impuestos
  de la factura de exportación, prorrateado al importe pagado.

## 5. Complemento Carta Porte 3.1 — traslado nacional

CFDI de **traslado** (`TipoDeComprobante="T"`, total 0) con el nodo
`cartaporte31:CartaPorte`. Ejemplo real: `VEN-000056`:

```xml
<cfdi:Comprobante TipoDeComprobante="T" ...>
  <cfdi:Complemento>
    <cartaporte31:CartaPorte Version="3.1" IdCCP="CCC9d794-d2c5-4ea9-ba84-a0607e2c537a"
                             TranspInternac="No" TotalDistRec="320.0">
      <cartaporte31:Ubicaciones>
        <cartaporte31:Ubicacion TipoUbicacion="Origen"  IDUbicacion="OR000001"
                                RFCRemitenteDestinatario="EKU9003173C9" .../>
        <cartaporte31:Ubicacion TipoUbicacion="Destino" IDUbicacion="DE000001"
                                RFCRemitenteDestinatario="EKU9003173C9"
                                DistanciaRecorrida="320.0" .../>
      </cartaporte31:Ubicaciones>
      <cartaporte31:Mercancias>
        <cartaporte31:Mercancia BienesTransp="30171708" Descripcion="Vidrio templado 9mm"
                                Cantidad="500.0" ClaveUnidad="KGM" PesoEnKg="500.0" />
        <cartaporte31:Autotransporte PermSCT="TPAF01" NumPermisoSCT="P10PERM01"> ... </cartaporte31:Autotransporte>
      </cartaporte31:Mercancias>
      <cartaporte31:FiguraTransporte>
        <cartaporte31:TiposFigura TipoFigura="01" RFCFigura="KAHO641101B39"
                                  NumLicencia="D0908240" NombreFigura="OSCAR KALA HAAK" />
      </cartaporte31:FiguraTransporte>
    </cartaporte31:CartaPorte>
  </cfdi:Complemento>
```

> `TranspInternac="No"` — solo autotransporte federal **nacional** (limitación
> vigente). En traslado tipo T el `RFCRemitenteDestinatario` == emisor
> (mercancía propia).

---

## Tabla de correlación (folios reales de la corrida P10 en dev)

| Comprobante | Folio | UUID | Liga / complemento |
|---|---|---|---|
| Factura con 2 anticipos | `VEN-000024` | `79778572…` | `CfdiRelacionados 07` → 2 anticipos |
| Anticipo 1 | `FACANT-000009` | `35bea119…` | referido por la factura y su NC |
| Anticipo 2 | `FACANT-000010` | `1d853ba6…` | referido por la factura y su NC |
| NC amortización | `NCRED-000009` | — | `07` doble → anticipo + factura |
| NC ranura | `NCRED-000008` | — | `01` → `VEN-000015` |
| Factura exportación | `VEN-000093` | `496d5f4f…` | `Exportacion=02` + CCE 2.0, IVA 0 % |
| REPP USD | `VEN-000094` | `89892d75…` | `Pago 2.0` → `VEN-000093` (`EquivalenciaDR`, `ObjetoImpDR=02`) |
| Carta Porte traslado | `VEN-000056` | `40d86bd5…` | `TipoComprobante T` + Carta Porte 3.1 |

Ver el guion completo del pipeline en
[`pipelines/p10-facturacion-multimoneda-cce-obra.md`](pipelines/p10-facturacion-multimoneda-cce-obra.md).
