/**
 * DEMO-COMPRA-PAGO: purchase-to-payment journey with TWO real users, driven through the UI.
 *
 *   A (almacen-operativo session = fio@, sucursal MID, permissions granted in the test DB): requests and buys.
 *   B (super-admin session = geovanny@): authorizes, receives, books the invoice, authorizes it in CxP and pays.
 *
 * Steps (each one hits its real endpoint; apiCallOn fails with "WIRING BROKEN" otherwise):
 *   1. A creates a RQ (header + line) WITHOUT price: only Compras quotes.
 *   2. A transmits; RQ detail shows the "En almacén" badge (partial stock exists in Almacén).
 *   3. B authorizes N1 from the RQ detail (department threshold configured so N1 is enough with two users).
 *   4. Mass approval from Pendientes (second RQ, so the mass path is exercised on its own).
 *   5. A creates the OC from the RQ (AutoGenerarOc=false), quotes the price, transmits it.
 *      A cannot sign it (UI has no enabled "Aprobar" and the API refuses).
 *   6. B signs the OC (N1) -> Autorizada.
 *   7. B receives the exact quantity (tolerance 0 %, open accounting period).
 *   8. B uploads the supplier CFDI (RFC of the demo supplier) and captures the invoice reconciled against the OC.
 *   9. B authorizes the invoice in CxP.
 *  10. B registers the payment from Tesoreria.
 *  11. Reload: invoice "Pagada" and OC "Cerrada".
 *
 * NO sql(): the whole journey is arranged with the app's own API (and the UI for the action under test), so it runs in `test` AND in
 * `dev` (disposable copy millet_dev_evidence). Accounting period: opened through POST /contabilidad/periodos/ejercicios/{id}/abrir.
 * Stock: an OC created by A, quoted/signed by B and received by B (a second real user is needed because the author cannot sign).
 *
 * ============================== PREREQUISITES (read before running) ==============================
 * Sessions (e2e/.auth/test): ALREADY SAVED and enough: super-admin (B) and almacen-operativo (A = fio@).
 *   No new Entra login is needed. Nothing is saved for Compras / CxP / Tesoreria roles and none is required,
 *   because super-admin holds every permission and fio@ gets the Compras ones through the admin API (see `conceder`).
 *   Optional, only if the owner wants the story with 3+ distinct people: `node scripts/login-entra.mjs compras`,
 *   `node scripts/login-entra.mjs cxp`, `node scripts/login-entra.mjs tesoreria` (accounts phiks@ / mariana.ortega@ /
 *   qa.catalogos@ are NOT suitable as-is: they sit in QRO / auditor / catalogs roles; they would need the role + MID branch).
 * Permissions granted to A in `test` by the spec itself (user overrides through /api/v1/identidad/usuarios/.../permisos-override,
 *   reverted in cleanup): compras.requisiciones.{crear,leer,editar}, compras.ordenes.{crear,leer,adjuntar},
 *   centros_costo.dim3.leer-todos, compartido.catalogos.leer, datos_maestros.proveedores.leer. Codes missing in the catalog are skipped.
 * Backend event bridge (BLOCKER if absent): the verify "test" env starts the API with every *Outbox__Disabled=true and an empty
 *   ServiceBus__ConnectionString, so cross-module events (OC authorized/received -> CxP and Compras; invoice authorized -> Tesoreria;
 *   payment -> CxP and Compras) never flow. The API test pumps them by hand (CompraAPagoDemoTests.BombearAsync). For this spec run
 *   the API with the local emulator (tools/servicebus-emulator/levantar.sh; ServiceBus__Modo=EmuladorLocal and the six
 *   *Outbox__Disabled=false flags + CuentasPorPagar__Workers__Disabled=false). The spec detects the missing bridge with
 *   expect.poll and fails with an explicit "EVENT BRIDGE" message instead of passing silently.
 * Data: supplier PROV-EST (RFC read from the API; the CFDI uses that RFC), article with partial stock created through the API,
 *   cost center / department seeded (CC_SEED, DEPTO_COMPRAS), 12 accounting periods opened for the current year.
 * ================================================================================================
 */
import fs from 'node:fs';
import { apiCallOn, expect, expectNoPlaceholders, reloadAndExpect, shot, test } from '../../_harness/fixtures';
import type { Page } from '../../_harness/fixtures';
import {
  abrirSheetRecepcion, asRole, CC_SEED, DEPTO_COMPRAS, elegirBin, MID, ok, OPER_EMAIL, proveedorSeed, rand,
  SA_EMAIL, today, uuid, warm, type Escenario,
} from '../86e3pc50v/_data';
import { apiBaseURL } from '../86e3pc50v/_data';
import {
  A_PERMS, TES_PERMS, CUENTA_MILLET, PROV_CLABE, PROV_CUENTA_ID, sembrarCuentaMillet, sembrarCatalogoContable, abrirRecepcion, PROV, sembrarProveedor, abrirPeriodoActual, adjuntarCotizacionPorUi, articulo, campo, captureBearer, conceder, crearBinNeutro, elegir, foto, lista, ocSinRqTransmitida,
  umbralesGuion,
} from './_demo';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(600_000);

const STOCK = 4;      // units already in Almacén
const PEDIDO = 10;    // units requested by A
const PRECIO = 95;    // price the buyer quotes in the OC
const FALTANTE = PEDIDO - STOCK;

function cfdiXml(u: string, rfc: string, subtotal: number, iva: number, folio: string) {
  const f = today();
  return `<?xml version="1.0" encoding="UTF-8"?>
<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="I" Serie="A" Folio="${folio}" Fecha="${f}T10:00:00" Total="${(subtotal + iva).toFixed(2)}" SubTotal="${subtotal.toFixed(2)}" Moneda="MXN" MetodoPago="PUE">
  <cfdi:Emisor Rfc="${rfc}" Nombre="${PROV.razon}" />
  <cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet SA" />
  <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="01010101" Cantidad="${FALTANTE}" ClaveUnidad="H87" Descripcion="Resma de papel bond carta 500 hojas" ValorUnitario="${(subtotal / FALTANTE).toFixed(2)}" Importe="${subtotal.toFixed(2)}" /></cfdi:Conceptos>
  <cfdi:Impuestos TotalImpuestosTrasladados="${iva.toFixed(2)}" />
  <cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="${u}" FechaTimbrado="${f}T10:00:00" /></cfdi:Complemento>
</cfdi:Comprobante>`;
}

test('DEMO-01 [AC] @evidence purchase-to-payment with two users: RQ -> OC -> reception -> invoice -> payment -> closed', async ({ page, guard, cleanup, browser }) => {
  // ---------- session B (primary) and session A ----------
  const bearerB = captureBearer(page);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const pa = A.page;
  await pa.setViewportSize({ width: 1600, height: 900 });
  const erroresA: string[] = [];
  pa.on('response', (r) => { if (r.url().includes('/api/') && r.status() >= 400) erroresA.push(`${r.request().method()} ${r.url()} ${r.status()}`); });

  // ---------- arrange (API as B; never the action under test) ----------
  await conceder(guard, cleanup, OPER_EMAIL, A_PERMS);
  await abrirPeriodoActual(page, bearerB); // reception and invoice need the current period open
  await umbralesGuion(guard); // General (empresa) and Compras: N1 up to 1,000 / N2 over 10,000
  sembrarProveedor(); // SQL: supplier + validated BBVA account + current compliance opinion
  const prov = { id: PROV.id, rfc: PROV.rfc };
  sembrarCatalogoContable(); // SQL: chart of accounts + role→account mapping, so the accounting engine can post (see _demo.ts)
  const sucursales = lista(await ok(await guard.api.get('/api/v1/catalogos/sucursales?limit=200')));
  const departamentos = lista(await ok(await guard.api.get('/api/v1/catalogos/departamentos?limit=200')));
  const sucNombre: string = sucursales.find((x: { id: string }) => x.id === MID)?.nombre;
  const deptoNombre: string = departamentos.find((x: { id: string }) => x.id === DEPTO_COMPRAS)?.nombre;
  expect(sucNombre && deptoNombre, 'sucursal MID / departamento Compras must exist in the catalogs').toBeTruthy();
  const rqDescripcion = `Papelería para oficinas de planta ${rand()}`;

  // Partial stock of the article in Almacén so the RQ detail shows "En almacén" and the OC covers only the shortage.
  // Without SQL the only way to get stock is the real purchase path: A creates the OC, B attaches the quotation, A transmits,
  // B signs (the author cannot sign) and B receives it.
  const art = await articulo(guard, 'Resma de papel bond carta 500 hojas');
  const bin = await crearBinNeutro(guard, art.id);
  const stock = { articuloId: art.id, articuloClave: art.clave, binId: bin.id, binClave: bin.clave };
  {
    const oc = await ocSinRqTransmitida({ guard, page, A, bearerB, provId: prov.id, articuloId: art.id, cantidad: STOCK, precio: 90, motivo: 'Reposición de existencias de almacén' });
    await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 1 }));
    const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
    expect(det.estado, 'the stock OC must end Autorizada with one N1 signature (general threshold arranged above)').toBe(3);
    await ok(await guard.api.post('/api/v1/almacen/recepciones', {
      ordenCompraId: oc.id, fechaMovimiento: today(), cfdiUuidFiscal: uuid().toUpperCase(), observaciones: 'Existencias iniciales',
      lineas: [{ articuloId: art.id, lineaOcId: det.lineas[0].id, cantidad: STOCK, ubicacionId: bin.id }],
    }));
  }

  let rqId = ''; let rqFolio = '';
  await test.step('1. A creates the RQ (header + line, no price)', async () => {
    await pa.goto('/compras/requisiciones');
    await pa.waitForLoadState('networkidle');
    await pa.getByRole('button', { name: /Nueva requisici/ }).first().click();
    const sheet = pa.getByRole('dialog', { name: 'Nueva requisición' });
    await expect(sheet).toBeVisible();
    // the sheet pre-fills the active branch; picking it again would toggle it off, so only pick when it is empty
    if (!(await sheet.getByRole('combobox', { name: 'Seleccionar sucursal' }).innerText()).includes(sucNombre))
      await elegir(pa, sheet, 'Seleccionar sucursal', { opcion: new RegExp(sucNombre) });
    await elegir(pa, sheet, 'Seleccionar departamento de la sucursal', { opcion: new RegExp(deptoNombre) });
    // the sheet defaults to 'Servicio' (no stock coverage): pick a goods classification so Almacén coverage applies
    await sheet.getByRole('combobox', { name: 'Clasificación' }).click();
    await pa.getByRole('option', { name: 'Orden de compra', exact: true }).click();
    await sheet.getByRole('textbox').last().fill(rqDescripcion);
    const res = await apiCallOn(pa, () => sheet.getByRole('button', { name: 'Crear requisición' }).click(),
      { method: 'POST', url: /\/compras\/requisiciones$/, status: [200, 201] });
    const body = JSON.parse(res.request().postData() ?? '{}');
    expect(body).toMatchObject({ sucursalId: MID, departamentoId: DEPTO_COMPRAS, clasificacion: 1 });
    expect(body.descripcion).toBe(rqDescripcion);
    const created = await res.json();
    rqId = created.id; rqFolio = created.folio;
    await expect(pa).toHaveURL(new RegExp(`/compras/requisiciones/${rqId}`));

    // line without price: the form has no price field, only article, quantity and cost center
    await pa.getByRole('button', { name: /Agregar partida/ }).first().click();
    const form = pa.locator('form[aria-label="Agregar partida"]');
    await expect(form).toBeVisible();
    await form.getByRole('combobox', { name: 'Seleccionar artículo' }).click();
    await pa.getByPlaceholder('Buscar por clave o nombre…').fill(stock.articuloClave);
    await pa.getByRole('option', { name: new RegExp(stock.articuloClave) }).first().click();
    await campo(form, /^Cantidad/).fill(String(PEDIDO));
    const cc = form.getByRole('combobox', { name: 'Seleccionar centro de costo' });
    if (await cc.isVisible().catch(() => false)) { await cc.click(); await pa.getByRole('listbox').getByRole('option').first().click(); }
    const lres = await apiCallOn(pa, () => form.getByRole('button', { name: /Agregar partida/ }).click(),
      { method: 'POST', url: new RegExp(`/compras/requisiciones/${rqId}/lineas$`), status: [200, 201] });
    const lbody = JSON.parse(lres.request().postData() ?? '{}');
    expect(lbody).toMatchObject({ articuloId: stock.articuloId, cantidad: PEDIDO });
    expect(lbody.precioEstimadoMonto ?? 0, 'the RQ line must carry no price').toBe(0);
    await expectNoPlaceholders(pa);
    await foto(pa, '01-rq-creada');
    // independent verification
    const rq = await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}`));
    expect(rq.lineas).toHaveLength(1);
    expect(rq.lineas[0].cantidad).toBe(PEDIDO);
  });

  await test.step('2. A transmits; detail shows the "En almacén" badge', async () => {
    await expect(pa.getByRole('toolbar', { name: 'Acciones de la requisición' })).toBeVisible();
    await pa.getByRole('button', { name: 'Transmitir', exact: true }).click();
    const res = await apiCallOn(pa, () => pa.getByRole('alertdialog').getByRole('button', { name: 'Confirmar' }).click(),
      { method: 'POST', url: new RegExp(`/compras/requisiciones/${rqId}/transmitir$`), status: [200, 204] });
    expect(res.ok()).toBe(true);
    await expect(pa.getByText('Requisición transmitida a autorización')).toBeVisible();
    await expect(pa.getByText('En almacén', { exact: true }).first(), 'RQ detail must show the "En almacén" badge (API returns enAlmacen)').toBeVisible();
    await foto(pa, '02-insignia-en-almacen');
    await reloadAndExpect(pa, async () => {
      await expect(pa.getByText(rqFolio).first()).toBeVisible();
    });
    const rq = await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}`));
    expect(rq.enAlmacen, 'API enAlmacen must be true with 4 units in stock for MID').toBe(true);
    // A cannot approve their own RQ
    await expect(pa.getByRole('button', { name: 'Aprobar Nivel 1' })).toHaveCount(0);
  });

  await test.step('3. B authorizes N1 from the RQ detail', async () => {
    await page.goto(`/compras/requisiciones/${rqId}`);
    await page.waitForLoadState('networkidle');
    await page.getByRole('button', { name: 'Aprobar Nivel 1' }).click();
    const res = await apiCallOn(page, () => page.getByRole('alertdialog').getByRole('button', { name: 'Confirmar' }).click(),
      { method: 'POST', url: new RegExp(`/compras/requisiciones/${rqId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(1);
    await expect(page.getByText('Autorización Nivel 1 registrada')).toBeVisible();
    await foto(page, '03-autorizacion-n1');
    await reloadAndExpect(page, async () => { await expect(page.getByText('Listo para surtir').first()).toBeVisible(); });
    // N1 is enough: the RQ moves to EnSurtido (3, "Listo para surtir")
    expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}`))).estado).toBe(3);
  });

  await test.step('4. B mass-approves TWO RQs from Pendientes (Nivel / Disponibilidad / Recurrente columns visible)', async () => {
    const ids: string[] = []; const folios: string[] = [];
    for (const [desc, nombreArt] of [['Papelería para almacén', 'Folder tamaño carta caja con 100'], ['Tóner para impresoras de planta', 'Tóner negro compatible para impresora láser']]) {
      const otro = await articulo(guard, nombreArt);
      const rq2 = await ok(await A.api.post('/api/v1/compras/requisiciones', {
        sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: 1, prioridad: 1,
        fechaSolicitud: new Date().toISOString(), descripcion: `${desc} ${rand()}`, fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null,
      }));
      const id2 = (rq2.id ?? rq2.requisicionId) as string;
      await ok(await A.api.post(`/api/v1/compras/requisiciones/${id2}/lineas`, {
        articuloId: otro.id, cantidad: 3, unidadMedida: 'PZA', precioEstimadoMonto: 0, precioEstimadoMoneda: 'MXN', centroCostoId: CC_SEED,
      }));
      await ok(await A.api.post(`/api/v1/compras/requisiciones/${id2}/transmitir`));
      ids.push(id2);
      folios.push((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id2}`))).folio as string);
    }

    await page.goto('/compras/pendientes');
    await page.waitForLoadState('networkidle');
    await expect(page.getByRole('heading', { name: 'Requisiciones por autorizar' })).toBeVisible();
    for (const h of ['Nivel', 'Disponibilidad', 'Recurrente'])
      await expect(page.getByRole('columnheader', { name: h, exact: true }), `column "${h}" must be visible in Pendientes`).toBeVisible();
    for (const f of folios) await page.getByRole('checkbox', { name: `Seleccionar ${f}` }).check();
    for (const f of folios) await expect(page.getByRole('checkbox', { name: `Seleccionar ${f}` })).toBeChecked();
    await expect(page.getByRole('button', { name: /Aprobar seleccionadas \(2\)/ })).toBeVisible();
    await foto(page, '04a-pendientes-seleccion-antes-de-aprobar');
    await page.getByRole('button', { name: /Aprobar seleccionadas \(2\)/ }).click();
    const confirmar = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Aprobar 2 requisiciones' });
    await expect(confirmar).toBeVisible();
    await foto(page, '04b-pendientes-confirmar-aprobacion-masiva');
    const res = await apiCallOn(page, () => confirmar.getByRole('button', { name: 'Aprobar 2', exact: true }).click(),
      { method: 'POST', url: /requisiciones\/autorizaciones-masivas$|autorizar-masivo|autorizaciones-masivas/, status: [200, 207] });
    const enviados = JSON.parse(res.request().postData() ?? '{}').ids;
    for (const id of ids) expect(enviados).toContain(id);
    // the list refetches after approval, so the rows leave Pendientes
    for (const f of folios) await expect(page.getByRole('checkbox', { name: `Seleccionar ${f}` })).toHaveCount(0);
    await foto(page, '04-aprobacion-masiva-pendientes');
    for (const id of ids) expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`))).estado).toBe(3);
  });

  let ocId = ''; let ocFolio = '';
  await test.step('5. A creates the OC from the RQ, quotes the price and transmits (A cannot sign)', async () => {
    await pa.goto(`/compras/requisiciones/${rqId}`);
    await pa.waitForLoadState('networkidle');
    await pa.locator('[data-action="convertir-a-oc"]').click();
    const sheet = pa.getByRole('dialog').last();
    await expect(sheet.locator('form[data-component="sheet-nueva-oc"]')).toBeVisible();
    await elegir(pa, sheet, 'Seleccionar proveedor', { buscar: { label: 'Buscar por clave, RFC o nombre…', texto: PROV.clave }, opcion: new RegExp(PROV.clave) });
    await elegir(pa, sheet, 'Seleccionar condiciones de pago');
    await sheet.getByRole('combobox', { name: 'Seleccionar uso principal' }).click();
    const usos = await pa.getByRole('option').allInnerTexts();
    test.info().annotations.push({ type: 'usos-principales', description: usos.join(' | ') });
    await pa.getByRole('option', { name: /Consumo|Operaci|Mantenimiento|Gasto|Insumo/i }).first().click().catch(async () => { await pa.getByRole('option').first().click(); });
    // the sheet runs two calls: POST /ordenes (empty header) and then POST /ordenes/{id}/lineas/desde-requisicion
    const creada = pa.waitForResponse((r) => r.request().method() === 'POST' && /\/compras\/ordenes$/.test(r.url()));
    const res = await apiCallOn(pa, () => sheet.locator('[data-action="submit"]').click(),
      { method: 'POST', url: /\/compras\/ordenes\/[^/]+\/lineas\/desde-requisicion$/, status: [200, 201] });
    const cres = await creada;
    expect(cres.status()).toBe(201);
    const cbody = JSON.parse(cres.request().postData() ?? '{}');
    expect(cbody.proveedorId).toBe(prov.id);
    expect(JSON.parse(res.request().postData() ?? '{}').requisicionId).toBe(rqId);
    ocId = (await cres.json()).id;
    await pa.waitForURL(new RegExp(`/compras/ordenes/${ocId}`));
    await pa.waitForLoadState('networkidle');
    const oc0 = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    ocFolio = oc0.folio;
    expect(oc0.lineas, 'the OC must carry only the shortage, not what Almacén already has').toHaveLength(1);
    expect(oc0.lineas[0].cantidad).toBe(FALTANTE);

    // the buyer quotes the price (RQ went without price)
    await pa.getByRole('tab', { name: 'Partidas' }).click();
    await pa.getByRole('button', { name: 'Editar partida 1' }).click();
    const form = pa.locator('form[aria-label^="Editar partida"]');
    await campo(form, /^Precio/).fill(String(PRECIO));
    await apiCallOn(pa, () => form.getByRole('button', { name: 'Guardar cambios' }).click(),
      { url: new RegExp(`/compras/ordenes/${ocId}/lineas/`), status: [200, 204] });
    await expect(pa.getByText(`$${FALTANTE * PRECIO}.00`).filter({ visible: true }).first()).toBeVisible();
    // «Datos para la factura del proveedor» are required before transmitting (OC_DATOS_FACTURA_INCOMPLETOS): captured from the header editor
    await foto(pa, '05a-oc-revision-previa-faltan-requisitos');
    await pa.getByRole('tab', { name: 'Información' }).click();
    await pa.getByRole('button', { name: 'Editar cabecera' }).click();
    const cab = pa.locator('form[aria-label="Editar cabecera"]');
    for (const [campoNombre, opcion] of [['Uso de CFDI', /^G03/], ['Método de pago', /^PUE/], ['Forma de pago', /^03 /]] as const) {
      await cab.getByLabel(campoNombre).click();
      await pa.getByRole('option', { name: opcion }).click();
    }
    await foto(pa, '05b-oc-datos-para-la-factura');
    await apiCallOn(pa, () => cab.getByRole('button', { name: 'Guardar cabecera' }).click(), { method: 'PATCH', url: new RegExp(`/compras/ordenes/${ocId}$`), status: [200, 204] });
    await adjuntarCotizacionPorUi(pa, guard, ocId);
    await pa.reload();
    await pa.waitForLoadState('networkidle');

    await pa.getByRole('button', { name: 'Transmitir a autorización' }).click();
    await apiCallOn(pa, () => pa.getByRole('button', { name: 'Transmitir', exact: true }).click(),
      { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/transmitir$`), status: [200, 204] });
    await expect(pa.getByText(`OC ${ocFolio} transmitida a autorización.`)).toBeVisible();

    // whoever captured the OC cannot authorize it: no usable control in the UI ...
    const aprobar = pa.getByRole('button', { name: 'Aprobar Nivel 1' });
    await expect(aprobar, 'the UI offers no "Aprobar Nivel 1" to A (no autorizar-nivel1 permission: AccionesOC hides it)').toHaveCount(0);
    // ... and the API refuses it too (403 or 422)
    const t = (await A.api.post(`/api/v1/compras/ordenes/${ocId}/autorizaciones`, { nivel: 1 })).status();
    expect([403, 422], `A signing its own OC answered ${t}`).toContain(t);
    erroresA.length = 0; // that refusal is the expected negative case
    await foto(pa, '05-oc-creada-sin-poder-firmar');
    const oc = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    expect(oc.estado).toBe(1); // EnAutorizacionJefeCompras
  });

  await test.step('6. B signs the OC (N1)', async () => {
    await page.goto(`/compras/ordenes/${ocId}`);
    await page.waitForLoadState('networkidle');
    await page.getByRole('button', { name: 'Aprobar Nivel 1' }).click();
    const res = await apiCallOn(page, () => page.locator('[role=dialog],[role=alertdialog]').getByRole('button', { name: 'Aprobar Nivel 1' }).click(),
      { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(1);
    await expect(page.getByText(`OC ${ocFolio} aprobada Nivel 1.`)).toBeVisible();
    await reloadAndExpect(page, async () => { await expect(page.getByText('Autorizada').first()).toBeVisible(); });
    await foto(page, '06-oc-firmada');
    expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado).toBe(3);
  });

  const uuidFiscal = uuid().toUpperCase();
  let lineaOcId = ''; let subtotal = 0; let iva = 0; let ocInicial: any;
  await test.step('7. B receives the exact quantity (tolerance 0 %)', async () => {
    ocInicial = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    lineaOcId = ocInicial.lineas[0].id;
    subtotal = ocInicial.lineas[0].subtotalLinea; iva = Math.round(subtotal * 0.16 * 100) / 100;
    expect(subtotal).toBe(FALTANTE * PRECIO);
    const dlg = await abrirRecepcion(page, ocFolio);
    await elegirBin(page, { binClave: stock.binClave } as Escenario);
    await dlg.getByRole('button', { name: /Captura el folio fiscal/ }).click();
    await dlg.getByLabel(/Folio fiscal/).fill(uuidFiscal);
    await dlg.getByLabel('Cantidad a recibir, partida 1').fill(String(FALTANTE));
    const res = await apiCallOn(page, () => dlg.getByRole('button', { name: 'Registrar recepción' }).click(),
      { method: 'POST', url: /\/almacen\/recepciones$/, status: 201 });
    const body = JSON.parse(res.request().postData() ?? '{}');
    expect(body.ordenCompraId).toBe(ocId);
    expect(body.lineas[0]).toMatchObject({ lineaOcId, cantidad: FALTANTE, articuloId: stock.articuloId });
    await foto(page, '07-recepcion');
    // independent check of the stored quantity, seen from Compras through the event (Almacen -> Compras)
    await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).lineas[0].cantidadRecibida, {
      timeout: 40_000, message: 'EVENT BRIDGE: Compras never recorded the received quantity (OcRecepcionRegistrada).',
    }).toBe(FALTANTE);
    await expect.poll(async () => String((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).subEstadoRecepcion), {
      timeout: 40_000, message: 'EVENT BRIDGE: Compras never received OcRecepcionRegistrada. Run the API with the Service Bus emulator and Outbox enabled (see header).',
    }).not.toBe(String(ocInicial.subEstadoRecepcion));
  });

  let facturaId = ''; const folioFac = `F-${rand()}`;
  await test.step('8. B uploads the CFDI (supplier RFC) and captures the reconciled invoice', async () => {
    await page.goto('/cxp/cfdis');
    await page.waitForLoadState('networkidle');
    await foto(page, '08a-cxp-cfdis-recibidos-pantalla');
    await page.getByRole('button', { name: 'Cargar CFDI' }).first().click();
    const sheet = page.getByRole('dialog');
    await sheet.locator('#cfdi-xml').setInputFiles({ name: 'factura-proveedor.xml', mimeType: 'text/xml', buffer: Buffer.from(cfdiXml(uuidFiscal, prov.rfc, subtotal, iva, folioFac)) });
    await expect(sheet.getByRole('button', { name: 'Cargar CFDI' })).toBeEnabled();
    await foto(page, '08b-cargar-cfdi-hoja-abierta');
    const cres = await apiCallOn(page, () => sheet.getByRole('button', { name: 'Cargar CFDI' }).click(),
      { method: 'POST', url: /\/cfdis\/cargar$/, status: [200, 201] });
    const cfdi = await cres.json();
    expect(cfdi.id).toBeTruthy();
    await page.getByLabel('Buscar por UUID o folio').fill(uuidFiscal);
    await page.getByRole('button', { name: new RegExp(`Acciones para CFDI ${uuidFiscal}`, 'i') }).click();
    await page.getByRole('menuitem', { name: 'Capturar factura' }).click();

    const cap = page.getByRole('dialog', { name: 'Capturar factura desde OC' });
    await expect(cap).toBeVisible();
    await cap.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
    await page.getByPlaceholder('Folio o referencia…').fill(ocFolio);
    await expect(page.getByRole('option', { name: new RegExp(ocFolio) }, ), 'EVENT BRIDGE: the authorized OC must be visible in CxP (OrdenCompraAutorizadaEvent)').toBeVisible({ timeout: 30_000 });
    await page.getByRole('option', { name: new RegExp(ocFolio) }).click();
    await cap.getByRole('button', { name: /Importar 1 partida/ }).click();
    await cap.getByRole('combobox', { name: 'Seleccionar línea de OC' }).click();
    await page.getByRole('option').first().click();
    await foto(page, '08c-capturar-factura-desde-oc-hoja-abierta');
    const res = await apiCallOn(page, () => cap.getByRole('button', { name: 'Capturar factura' }).click(),
      { method: 'POST', url: /\/cuentas-por-pagar\/facturas$/, status: [200, 201] });
    const body = JSON.parse(res.request().postData() ?? '{}');
    expect(body).toMatchObject({ ordenCompraId: ocId, uuidCfdi: uuidFiscal, cfdiRecibidoId: cfdi.id });
    expect(body.lineas[0].lineaOcId).toBe(lineaOcId);
    expect(body.total).toBeCloseTo(subtotal + iva, 2);
    facturaId = (await res.json()).id;
    await expect(page.getByText('Factura capturada')).toBeVisible();
    await expect(page.getByText('Factura cancelada por diferencia en la conciliación')).toHaveCount(0);
    await foto(page, '08-factura-conciliada');
    const fac = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`));
    expect(fac.estado).toBe(1); // Capturada, not cancelled by tolerance
    expect(fac.proveedorId).toBe(prov.id);
  });

  await test.step('9. B authorizes the invoice in CxP (3-way match: OC + reception + CFDI)', async () => {
    await page.goto(`/cxp/facturas/${facturaId}`);
    await page.waitForLoadState('networkidle');
    await expect(page.getByText('Sin diferencia').first(), 'the 3-way match must show «Sin diferencia»').toBeVisible();
    await foto(page, '09a-cxp-factura-capturada-sin-diferencia');
    await page.getByRole('button', { name: 'Autorizar', exact: true }).click();
    const dlg = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Autorizar factura para pago' });
    await expect(dlg).toBeVisible();
    await foto(page, '09b-cxp-confirmar-autorizacion');
    await apiCallOn(page, () => dlg.getByRole('button', { name: 'Autorizar para pago' }).click(),
      { method: 'POST', url: new RegExp(`/facturas/${facturaId}/autorizar$`), status: [200, 204] });
    await expect(page.getByText('Factura autorizada')).toBeVisible();
    await reloadAndExpect(page, async () => { await expect(page.getByText('Autorizada').first()).toBeVisible(); });
    await foto(page, '09-cxp-autorizada');
    expect((await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`))).estado).toBe(3);
    await expect.poll(async () => {
      const r = await ok(await guard.api.get(`/api/v1/tesoreria/pasivos-pendientes?proveedorId=${prov.id}`));
      return r.items.some((p: { facturaProveedorId: string }) => p.facturaProveedorId === facturaId);
    }, { timeout: 40_000, message: 'EVENT BRIDGE: Tesoreria never got PasivoAutorizadoParaPago.' }).toBe(true);
  });

  const total = Math.round((subtotal + iva) * 100) / 100;
  const referencia = `SPEI${today().replace(/-/g, '')}${rand()}`;
  let corridaId = '';
  const nombreCorrida = 'Pago a proveedores de papelería';
  await test.step('10. Tesoreria payment run: A arms and sends it, B signs, BBVA layout, payment registered, run closed', async () => {
    // arrange (SQL): Millet BBVA account with CLABE and BBVA statement profile (no API sets CLABE / profile)
    sembrarCuentaMillet();
    await conceder(guard, cleanup, OPER_EMAIL, TES_PERMS);
    erroresA.length = 0;
    // --- A (second real user) arms the run
    await pa.goto('/tesoreria/corridas/nueva');
    await pa.waitForLoadState('networkidle');
    await pa.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
    await pa.getByRole('option', { name: new RegExp(CUENTA_MILLET.banco) }).click();
    const cbProv = pa.getByRole('checkbox', { name: `Elegir todas las facturas de ${PROV.razon}` });
    await expect(cbProv, 'the authorized invoice must be offered as a liability for the run').toBeVisible({ timeout: 20_000 });
    await pa.getByLabel('Nombre (opcional)').fill(nombreCorrida);
    await foto(pa, '10a-corrida-nueva-pasivos-disponibles');
    await cbProv.check();
    await expect(pa.getByText(/1 facturas? de 1 proveedor/)).toBeVisible();
    await foto(pa, '10b-corrida-seleccion-con-totales');
    await pa.getByRole('button', { name: 'Crear corrida en borrador' }).click();
    await pa.waitForURL(/\/tesoreria\/corridas\/[0-9a-f-]{36}$/);
    corridaId = pa.url().split('/').pop()!;
    await expect(pa.getByRole('heading', { name: nombreCorrida })).toBeVisible();
    await foto(pa, '10c-corrida-en-borrador');
    await pa.getByRole('button', { name: 'Enviar a autorización' }).click();
    await expect(pa.getByText('En autorización', { exact: true }).first()).toBeVisible();
    await foto(pa, '10d-corrida-en-autorizacion-firmas-0-de-1');
    expect(erroresA, 'API errors seen by user A while arming the run').toEqual([]);

    // --- B signs
    await page.goto(`/tesoreria/corridas/${corridaId}`);
    await expect(page.getByRole('button', { name: 'Firmar y autorizar' })).toBeEnabled();
    await foto(page, '10e-firmante-ve-la-corrida');
    await page.getByRole('button', { name: 'Firmar y autorizar' }).click();
    const dlg = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Firmar la corrida' });
    await expect(dlg.getByRole('button', { name: 'Firmar y autorizar' })).toBeEnabled();
    await page.waitForTimeout(300);
    await foto(page, '10f-dialogo-firmar-corrida');
    await dlg.getByRole('button', { name: 'Firmar y autorizar' }).click();
    await expect(page.getByText('Autorizada', { exact: true }).first()).toBeVisible();
    await foto(page, '10g-corrida-autorizada');

    // --- B generates the BBVA layout
    const descarga = page.waitForEvent('download');
    const lres = await apiCallOn(page, () => page.getByRole('button', { name: 'Generar archivo' }).click(), { method: 'POST', url: /\/corridas\/[0-9a-f-]+\/layout\?/, status: 200 });
    const layout = await lres.json();
    const archivo = fs.readFileSync(await (await descarga).path(), 'utf8');
    await expect(page.getByText('Archivo generado con 1 líneas')).toBeVisible();
    await foto(page, '10h-layout-bbva-generado');
    expect(layout).toMatchObject({ banco: 'BBVA', lineasValidas: 1, lineasExcluidas: 0 });
    expect(layout.total).toBeCloseTo(total, 2);
    expect(archivo, 'BBVA layout line: CLABE, amount, holder and reference').toContain(`${PROV_CLABE},${total.toFixed(2)},`);
    fs.mkdirSync('.verification/DEMO-COMPRA-PAGO/evidence/test/screens', { recursive: true });
    fs.writeFileSync('.verification/DEMO-COMPRA-PAGO/evidence/test/screens/10i-layout-bbva-contenido.txt', `${layout.nombreArchivo}\n\n${archivo}\n`);

    // --- B registers the payment of the run (bank reference of the transfer)
    await page.getByLabel('Referencia bancaria').fill(referencia);
    await foto(page, '10j-registrar-pago-de-la-corrida');
    const pres = await apiCallOn(page, () => page.getByRole('button', { name: 'Registrar pago', exact: true }).click(),
      { method: 'POST', url: new RegExp(`/corridas/${corridaId}/ejecuciones?$|/corridas/${corridaId}/`), status: [200, 201, 204] });
    expect(JSON.stringify(JSON.parse(pres.request().postData() ?? '{}'))).toContain(referencia);
    await expect(page.getByText('Pago registrado')).toBeVisible();
    await foto(page, '10k-pago-registrado-corrida-ejecutada');
    await page.getByRole('button', { name: 'Cerrar corrida' }).click();
    await expect(page.getByText('Corrida cerrada')).toBeVisible();
    await foto(page, '10l-corrida-cerrada');
  });

  await test.step('11. Reload: invoice Pagada and OC Cerrada (events back to CxP and Compras)', async () => {
    await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`))).estado, {
      timeout: 40_000, message: 'EVENT BRIDGE: CxP never got PagoFacturaProveedorEvent.',
    }).toBe(4);
    const fac = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`));
    expect(fac.saldoPendiente).toBe(0);
    expect(fac.importePagado).toBeCloseTo(total, 2);
    await page.goto(`/cxp/facturas/${facturaId}`);
    await reloadAndExpect(page, async () => { await expect(page.getByText('Pagada').first()).toBeVisible(); });
    await expectNoPlaceholders(page);
    await foto(page, '11a-factura-pagada');

    await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado, {
      timeout: 40_000, message: 'EVENT BRIDGE: Compras never closed the OC (factura.pago-aplicado.v1).',
    }).toBe(4); // Cerrada
    await page.goto(`/compras/ordenes/${ocId}`);
    await reloadAndExpect(page, async () => { await expect(page.getByText('Cerrada').first()).toBeVisible(); });
    await expectNoPlaceholders(page);
    await foto(page, '11-factura-pagada-oc-cerrada');
  });

  await test.step('11b. Double payment: Tesoreria no longer lists the liability and a second payment is refused', async () => {
    await expect.poll(async () => {
      const r = await ok(await guard.api.get(`/api/v1/tesoreria/pasivos-pendientes?proveedorId=${prov.id}`));
      return r.items.some((p: { facturaProveedorId: string }) => p.facturaProveedorId === facturaId);
    }, { timeout: 40_000, message: 'the paid liability must leave pasivos-pendientes' }).toBe(false);
    await page.goto('/tesoreria/pagos');
    await page.waitForLoadState('networkidle');
    await expect(page.getByRole('heading', { name: /pagos/i }).first()).toBeVisible();
    await expect(page.getByRole('checkbox', { name: new RegExp(`Seleccionar .*${folioFac}`) }), 'a paid invoice must not be selectable again').toHaveCount(0);
    await foto(page, '11b-pagos-sin-el-pasivo-pagado');
    // second payment to the same liability, fresh Idempotency-Key, so the refusal is the business rule and not a replay
    const segundo = await page.request.fetch(`${apiBaseURL}/api/v1/tesoreria/pagos`, {
      method: 'POST', headers: { authorization: bearerB.v, 'Idempotency-Key': uuid() },
      data: { cuentaProveedorId: PROV_CUENTA_ID, cuentaBancariaId: CUENTA_MILLET.id, fechaValor: today(), referenciaBancaria: `${referencia}-2`, aplicaciones: [{ facturaProveedorId: facturaId, importe: total }] },
    });
    const txt = await segundo.text();
    expect(segundo.status(), `second payment must be refused with 4xx, got ${segundo.status()}: ${txt.slice(0, 300)}`).toBeGreaterThanOrEqual(400);
    expect(segundo.status(), txt.slice(0, 300)).toBeLessThan(500);
    expect((await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`))).importePagado).toBeCloseTo(total, 2);
  });

  // ---- steps beyond the payment: reported, never hidden. A failure here is recorded and the journey continues.
  const hallazgos: string[] = [];
  const intentar = async (nombre: string, fn: () => Promise<void>) => {
    try { await test.step(nombre, fn); } catch (e) {
      hallazgos.push(`${nombre}: ${String((e as Error).message).split('\n').slice(0, 3).join(' | ')}`);
      await shot(page, `ERROR-${nombre.split('.')[0]}`).catch(() => {});
    }
  };

  await intentar('12. Bank reconciliation: the BBVA statement line matches the payment of the run', async () => {
    const meses = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
    const d = new Date();
    const dd = `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
    const saldoFinal = Math.round((CUENTA_MILLET.saldoInicial - total) * 100) / 100;
    const texto = ['Fecha,Descripcion,Referencia,Cargo,Abono,Saldo',
      `${dd},"PAGO SPEI ${PROV.razon.toUpperCase().normalize('NFD').replace(/[̀-ͯ]/g, '').slice(0, 40)}",${referencia},${total.toFixed(2)},,${saldoFinal.toFixed(2)}`].join('\n');
    await page.goto('/tesoreria/conciliacion');
    await expect(page.getByRole('heading', { name: 'Conciliación bancaria' })).toBeVisible();
    const combo = (i: number) => page.getByText(['Cuenta Bancaria', 'Año', 'Mes'][i], { exact: true }).first().locator('xpath=following::button[@role="combobox"][1]');
    await combo(0).click();
    await page.getByRole('option', { name: new RegExp(CUENTA_MILLET.numero.slice(-4)) }).click();
    await combo(1).click();
    await page.getByRole('option', { name: String(d.getFullYear()), exact: true }).click();
    await combo(2).click();
    await page.getByRole('option', { name: meses[d.getMonth()] }).click();
    await page.getByRole('button', { name: 'Nueva conciliación' }).click();
    await page.getByRole('button', { name: 'Abrir Conciliación' }).click();
    await expect(page.getByText('Conciliación abierta con éxito').first()).toBeVisible();
    await page.getByRole('button', { name: 'Cargar Extracto BBVA' }).click();
    const dlg = page.getByRole('dialog');
    await dlg.getByRole('textbox').last().fill(texto);
    await foto(page, '12a-conciliacion-cargar-extracto-bbva');
    await apiCallOn(page, () => dlg.getByRole('button', { name: 'Cargar y Procesar' }).click(), { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201] });
    await expect(page.getByText('Extracto cargado y procesado correctamente').first()).toBeVisible();
    await expect(page.getByText('1 de 1 líneas emparejadas'), 'the statement line must auto-match the payment (same reference and amount)').toBeVisible();
    await foto(page, '12b-conciliacion-linea-emparejada-con-el-pago');
  });

  await intentar('13. Accounting: journal entries / events generated by the purchase-to-pay journey', async () => {
    await page.goto('/contabilidad/polizas');
    await page.waitForLoadState('networkidle');
    await foto(page, '13a-contabilidad-polizas');
    const r = await guard.api.get('/api/v1/contabilidad/polizas?limit=50');
    const cuerpo = await r.json();
    const n = Array.isArray(cuerpo) ? cuerpo.length : (cuerpo.items?.length ?? 0);
    test.info().annotations.push({ type: 'polizas', description: `${n} pólizas visibles tras el recorrido` });
    const rech = await (await guard.api.get('/api/v1/contabilidad/motor/rechazos')).json();
    const rechazos = (Array.isArray(rech) ? rech : (rech.items ?? [])) as { causa: string; tipoEvento: string; estado: unknown }[];
    test.info().annotations.push({ type: 'rechazos-contables', description: rechazos.map((x) => `${x.tipoEvento}:${x.causa}:${x.estado}`).join(' | ') || 'ninguno' });
    await page.goto('/contabilidad/motor');
    await page.waitForLoadState('networkidle');
    await foto(page, '13b-contabilidad-bandeja-de-eventos');
    expect(n, 'no journal entry exists after receiving, invoicing and paying: the accounting engine did not post any event').toBeGreaterThan(0);
    expect(rechazos.map((x) => `${x.tipoEvento}:${x.causa}`), 'events of the journey left in the accounting inbox').toEqual([]);
  });
  expect(hallazgos, 'steps beyond the payment that did not work end to end').toEqual([]);

  expect(erroresA, 'API errors seen by user A').toEqual([]);
});
