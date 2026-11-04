# CFDI de prueba para una OC (servidor de pruebas)

Para recorrer compra a pago (RQ → OC → recepción → factura CxP → pago) hace falta el
XML de la factura del proveedor que cuadre con la OC. En pruebas no hay proveedor real que
la emita, así que `tools/cfdi-prueba/generar.mjs` arma un CFDI 4.0 sintético desde la OC.

- **No tiene validez fiscal:** no lleva sello ni certificado, no pasa por ningún PAC, por
  FiscalAPI ni por el SAT. El XML dice «CFDI DE PRUEBA» y la serie es `PRUEBA`.
- **No corre contra producción:** antes de leer nada consulta `GET /api/version` y se
  detiene si el ambiente es `Production`.
- Solo usa endpoints que ya existen y los permisos del usuario que lo corre; no agrega
  permisos, configuración ni cambios al API.

## Qué arma

| Dato | De dónde sale |
|---|---|
| Emisor | RFC y razón social del proveedor de la OC (`/catalogos/proveedores/{id}`) |
| Receptor | RFC de la empresa de la OC (`/admin/empresas`) o `--rfc-receptor` |
| Conceptos | Líneas de la OC: precio unitario de la OC, descuento de la línea, IVA con la tasa de la línea (16 % hoy) |
| Cantidad por línea | Lo recibido y no facturado (servicios: todo, si la OC tiene el servicio confirmado). Con `--cantidades` se elige otra, p. ej. parcial |
| UUID | Aleatorio en el `TimbreFiscalDigital`; nunca se repite |
| Moneda | La de la OC (con su tipo de cambio si no es MXN) |

No reparte el descuento global de la OC; si la OC lo tiene, avisa.

## Requisitos

- Node 22 (el del frontend). Sin dependencias.
- Usuario con `compras.ordenes.leer` y `compartido.catalogos.leer`; con `--cargar`,
  además `cuentas_por_pagar.cfdis.cargar-manual`. Para leer el RFC de la empresa,
  `admin.empresas.leer`; si no lo tiene, pasar `--rfc-receptor`.
- El proveedor de la OC debe tener RFC en el catálogo.
- Token: entrar a la app, abrir las herramientas del navegador › Red, tomar cualquier
  llamada a `/api/...` y copiar el valor del encabezado `Authorization`.

## Uso

```bash
export MILLET_TOKEN='Bearer eyJ...'
API=https://app-millet-qa-mxc-01.azurewebsites.net

# Solo escribe el XML (para subirlo en CxP › CFDIs › Cargar CFDI o desde el portal del proveedor)
node tools/cfdi-prueba/generar.mjs --url $API --oc OC-MID2026-000002

# Factura parcial (posición de línea = cantidad) y lo deja en la bandeja de CFDIs
node tools/cfdi-prueba/generar.mjs --url $API --oc OC-MID2026-000002 --cantidades 1=4,2=1 --cargar
```

Después: CxP › CFDIs › buscar el UUID › «Capturar factura» › elegir la OC. La conciliación
debe salir «Sin diferencia».

## Por qué pasa las validaciones del ERP

- **Lectura del XML** (`XmlCfdiParser`): raíz `cfdi:Comprobante` 4.0, `Emisor`, `Receptor`,
  `TimbreFiscalDigital` con UUID, fecha `yyyy-MM-ddTHH:mm:ss`.
- **UUID único** (`CFDI_DUPLICADO`, `FACTURA_DUPLICADA`): UUID aleatorio en cada corrida.
- **Emisor = proveedor** (captura en CxP y `PORTAL_RFC_NO_COINCIDE` del portal): se toma
  del proveedor de la OC.
- **Importes** (conciliación con la OC y `FACTURA_EXCEDE_RECIBIDO`): mismo precio, descuento
  e IVA que la OC y, por omisión, solo lo recibido.
- **Receptor mapeado** (`RFC_RECEPTOR_NO_MAPEADO`): solo aplica al buzón de correo
  (`Workers:RfcsReceptores`). La carga manual y el portal no lo piden. Para mandarlo por
  correo, el RFC de la empresa debe estar en ese mapeo del servidor.
- **SAT/PAC**: CxP no consulta al SAT al recibir ni al capturar un CFDI de proveedor; no hay
  nada que simular.

Pruebas del armado del XML: `node --test tools/cfdi-prueba/cfdi.test.mjs`.
