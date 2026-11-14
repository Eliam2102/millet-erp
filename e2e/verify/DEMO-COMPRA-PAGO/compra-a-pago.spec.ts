/**
 * DEMO-COMPRA-PAGO: purchase-to-payment journey driven through the UI with ONE REAL ENTRA USER PER BUSINESS ROLE (no shortcuts that
 * contradict the fichas). People (see PERSONA in _demo.ts):
 *   A  fio@ (almacen-operativo, MID) ......... requests (RQ), receives in Almacén, captures the invoice in CxP, arms the payment run
 *   J1 qa.catalogos@ (designated JefeDpto) ... signs RQ level 1 (only the department head of the RQ may: C3.1 / #352)
 *   J2 oscar@ (designated JefeAlmacen MID) ... signs RQ level 2 (shortage => N2 by availability, default mode SoloDisponibilidad)
 *   C  luis.ibarra@ (compras) ................ buyer: OC from the RQ, quotes the price, transmits (cannot sign it)
 *   B  geovanny@ (super-admin) ............... signs the OC, authorizes the invoice in CxP, signs the payment run, reconciles the bank
 * Separation: RQ author != RQ signers (J1 != J2) != buyer != OC signer; CxP capture (A) != CxP authorization (B); run armed by A, signed by B.
 *
 * Steps: 1 A creates RQ  2 A transmits  3 B/C refused (403 REQUISICION_AUTORIZADOR_NO_FACULTADO), J1 signs N1, J1 sees N2 disabled,
 *   J2 signs N2, stock in Almacén is set aside (apartado, CA3.4)  4 mass approval N1 (J1) + N2 (J2)  5 C creates/quotes/transmits the OC
 *   6 B signs the OC (Comprador titular with name)  7 A receives  8 A uploads the CFDI and captures the invoice  9 B authorizes it
  *   10 A arms the run, B signs, payment, close (BBVA layout hidden: Phase 2)  11 invoice Pagada / OC Cerrada
 *   12 bank reconciliation (same statement twice does not duplicate, CA9.5; charge shown with «-»)  13 pólizas EM, TT, PP exact (C1.5-a, K10.5).
 *
 * Shortcuts kept because the sources allow them: chart of accounts + role->account mapping seeded (C1.5 roles DEMO), Outbox + local
 * delivery ON in the verify config (C1.5 §5), BBVA statement built by the spec in our own CSV layout («formato DEMO»: P-018/P-019 have no
 * real layout yet). Supplier master data / bank account / Millet BBVA account are SQL (no API sets a validated CLABE). Test DB only.
 */
import { apiCallOn, expect, expectNoPlaceholders, reloadAndExpect, shot, test } from '../../_harness/fixtures';
import type { Page } from '../../_harness/fixtures';
import {
  abrirSheetRecepcion, asRole, CC_SEED, DEPTO_COMPRAS, elegirBin, MID, ok, OPER_EMAIL, proveedorSeed, rand,
  SA_EMAIL, today, uuid, warm, type Escenario,
} from '../86e3pc50v/_data';
import { apiBaseURL } from '../86e3pc50v/_data';
import {
  A_PERMS, TES_PERMS, CUENTA_MILLET, PROV_CLABE, PROV_CUENTA_ID, sembrarCuentaMillet, sembrarCatalogoContable, abrirRecepcion, PROV, sembrarProveedor, abrirPeriodoActual, adjuntarCotizacionPorUi, articulo, campo, captureBearer, conceder, crearBinNeutro, elegir, foto, lista, ocSinRqTransmitida,
  umbralesGuion, PERSONA, JEFE_PERMS, COMPRADOR_PERMS, CXP_PERMS, designarAprobador,
} from './_demo';
import { sql } from '../86e3ke5ck/_data';
import { capturarSaldoPortal } from '../DEMO-COMPRA-PAGO-VALIDACIONES/_data';

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

test('DEMO-01 [AC] @evidence purchase-to-payment with one real user per role: RQ -> OC -> reception -> invoice -> payment -> closed', async ({ page, guard, cleanup, browser }) => {
  // ---------- session B (primary) and session A ----------
  const bearerB = captureBearer(page);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const pa = A.page;
  await pa.setViewportSize({ width: 1600, height: 900 });
  const J1 = await asRole(browser, PERSONA.jefeDepto.rol, PERSONA.jefeDepto.email, '/compras/requisiciones');
  const J2 = await asRole(browser, PERSONA.jefeAlmacen.rol, PERSONA.jefeAlmacen.email, '/compras/requisiciones');
  const C = await asRole(browser, PERSONA.comprador.rol, PERSONA.comprador.email, '/compras/ordenes');
  cleanup.add(async () => { await J1.context.close(); await J2.context.close(); await C.context.close(); });
  const pj1 = J1.page; const pj2 = J2.page; const pc = C.page;
  for (const p of [pj1, pj2, pc]) await p.setViewportSize({ width: 1600, height: 900 });
  const erroresA: string[] = []; const erroresC: string[] = [];
  pa.on('response', (r) => { if (r.url().includes('/api/') && r.status() >= 400) erroresA.push(`${r.request().method()} ${r.url()} ${r.status()}`); });
  pc.on('response', (r) => { if (r.url().includes('/api/') && r.status() >= 400) erroresC.push(`${r.request().method()} ${r.url()} ${r.status()}`); });
  guard.allowStatus(403, 422); // negative checks of other people trying to sign

  // ---------- arrange (API as B; never the action under test) ----------
  await conceder(guard, cleanup, OPER_EMAIL, [...A_PERMS.filter((c) => !c.startsWith('compras.ordenes.')), ...CXP_PERMS]);
  await conceder(guard, cleanup, PERSONA.jefeDepto.email, JEFE_PERMS);
  await conceder(guard, cleanup, PERSONA.jefeAlmacen.email, JEFE_PERMS);
  await conceder(guard, cleanup, PERSONA.comprador.email, COMPRADOR_PERMS);
  // RQ authorization matrix (C3.1): N1 = JefeDpto of the RQ department, N2 = JefeAlmacen of the RQ branch
  const j1Id = await designarAprobador(guard, cleanup, 0, DEPTO_COMPRAS, PERSONA.jefeDepto.email);
  const j2Id = await designarAprobador(guard, cleanup, 1, MID, PERSONA.jefeAlmacen.email);
  expect(j1Id).not.toBe(j2Id);
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
  // Without SQL the only way to get stock is the real purchase path: C (buyer) creates and transmits the OC, B signs it, A receives it.
  const art = await articulo(guard, 'Resma de papel bond carta 500 hojas');
  const bin = await crearBinNeutro(guard, art.id);
  const stock = { articuloId: art.id, articuloClave: art.clave, binId: bin.id, binClave: bin.clave };
  {
    const oc = await ocSinRqTransmitida({ guard, page, A: C, bearerB, provId: prov.id, articuloId: art.id, cantidad: STOCK, precio: 90, motivo: 'Reposición de existencias de almacén' });
    await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 1 }));
    const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
    expect(det.estado, 'the stock OC must end Autorizada with one N1 signature (general threshold arranged above)').toBe(3);
    await ok(await A.api.post('/api/v1/almacen/recepciones', {
      ordenCompraId: oc.id, fechaMovimiento: today(), cfdiUuidFiscal: uuid().toUpperCase(), observaciones: 'Existencias iniciales',
      lineas: [{ articuloId: art.id, lineaOcId: det.lineas[0].id, cantidad: STOCK, ubicacionId: bin.id }],
    }, { 'Idempotency-Key': uuid() }));
  }

  const codigo = async (r: any) => { const t = await r.text(); try { return `${r.status()} ${JSON.parse(t).code ?? ''}`.trim(); } catch { return `${r.status()} ${t.slice(0, 120)}`; } };
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
    // A cannot approve their own RQ: the button is there, disabled, with the reason
    const aprobarRq = pa.getByRole('button', { name: 'Aprobar Nivel 1' });
    await expect(aprobarRq).toBeDisabled();
    await aprobarRq.locator('..').hover();
    await expect(pa.getByRole('tooltip').getByText('Capturaste esta requisición: no puedes autorizarla. Debe autorizarla otra persona.').first()).toBeVisible();
    await foto(pa, '02b-autor-no-puede-autorizar-su-requisicion');
    // with the matrix configured the «not the designated signer» check answers first (403); without it, 422 REQUISICION_AUTOAUTORIZACION
    const t = await codigo(await A.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 1 }));
    expect(t, 'A authorizing its own RQ').toMatch(/^(403 REQUISICION_AUTORIZADOR_NO_FACULTADO|422 REQUISICION_AUTOAUTORIZACION)$/);
    erroresA.length = 0; // that refusal is the expected negative case
  });

  await test.step('3a. Only the designated department head signs N1: B (super-admin) and the buyer are refused', async () => {
    await page.goto(`/compras/requisiciones/${rqId}`);
    await page.waitForLoadState('networkidle');
    const n1B = page.getByRole('button', { name: 'Aprobar Nivel 1' });
    await expect(n1B, 'super-admin is not the JefeDpto: «Aprobar Nivel 1» disabled').toBeDisabled();
    await n1B.locator('..').hover();
    await expect(page.getByRole('tooltip').getByText('Le corresponde al jefe del departamento del solicitante.').first()).toBeVisible();
    await foto(page, '03a-super-admin-no-facultado-n1');
    expect(await codigo(await guard.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 1 })), 'super-admin has no bypass').toBe('403 REQUISICION_AUTORIZADOR_NO_FACULTADO');
    expect(await codigo(await C.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 1 }))).toMatch(/^403/);
  });

  await test.step('3b. J1 (JefeDpto) signs N1 from the RQ detail; the shortage keeps it waiting for N2', async () => {
    await pj1.goto(`/compras/requisiciones/${rqId}`);
    await pj1.waitForLoadState('networkidle');
    await pj1.getByRole('button', { name: 'Aprobar Nivel 1' }).click();
    const res = await apiCallOn(pj1, () => pj1.getByRole('alertdialog').getByRole('button', { name: 'Confirmar' }).click(),
      { method: 'POST', url: new RegExp(`/compras/requisiciones/${rqId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(1);
    await expect(pj1.getByText('Autorización Nivel 1 registrada')).toBeVisible();
    const rq = await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}`));
    expect(rq.estado, 'SoloDisponibilidad: 6 of 10 must be bought, so N2 is still required (EnAutorizacion)').toBe(1);
    expect(rq.autorizadorN1Id).toBe(j1Id);
    expect(rq.autorizadorN2Id).toBe(j2Id);
    await pj1.reload(); await pj1.waitForLoadState('networkidle');
    const n2 = pj1.getByRole('button', { name: 'Aprobar Nivel 2' });
    await expect(n2, 'who signed N1 sees N2 disabled (AUTORIZACION_FIRMA_MISMA_PERSONA)').toBeDisabled();
    await n2.locator('..').hover();
    await expect(pj1.getByRole('tooltip').getByText('Ya firmaste el Nivel 1: el Nivel 2 debe firmarlo otra persona.').first()).toBeVisible();
    await foto(pj1, '03b-jefe-depto-firmo-n1-ve-n2-deshabilitado');
    const t = await codigo(await J1.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 2 }));
    test.info().annotations.push({ type: 'J1 firma N2 por API', description: t });
    expect(t).toMatch(/^(403 REQUISICION_AUTORIZADOR_NO_FACULTADO|422 AUTORIZACION_FIRMA_MISMA_PERSONA)$/);
    expect(await codigo(await guard.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 2 })), 'super-admin is not the JefeAlmacen').toBe('403 REQUISICION_AUTORIZADOR_NO_FACULTADO');
  });

  await test.step('3c. J2 (JefeAlmacen MID) signs N2; the 4 pieces in stock are set aside (apartado, CA3.4)', async () => {
    await pj2.goto(`/compras/requisiciones/${rqId}`);
    await pj2.waitForLoadState('networkidle');
    await pj2.getByRole('button', { name: 'Aprobar Nivel 2' }).click();
    const res = await apiCallOn(pj2, () => pj2.getByRole('alertdialog').getByRole('button', { name: 'Confirmar' }).click(),
      { method: 'POST', url: new RegExp(`/compras/requisiciones/${rqId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(2);
    await expect(pj2.getByText('Autorización Nivel 2 registrada')).toBeVisible();
    await reloadAndExpect(pj2, async () => { await expect(pj2.getByText('Listo para surtir').first()).toBeVisible(); });
    await foto(pj2, '03c-jefe-almacen-firma-n2');
    expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}`))).estado).toBe(3);
    // CA3.4: the stock that covers the RQ is reserved for it
    const apartados = lista(await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rqId}/apartados`)));
    test.info().annotations.push({ type: 'apartados', description: JSON.stringify(apartados) });
    expect(apartados.reduce((a: number, x: { pendiente: number }) => a + x.pendiente, 0), 'pieces in stock set aside for the RQ').toBe(STOCK);
    await page.goto(`/compras/requisiciones/${rqId}`);
    await page.waitForLoadState('networkidle');
    const sec = page.getByRole('region', { name: 'Existencia apartada' });
    await expect(sec).toBeVisible();
    await expect(sec).toContainText(`Apartado: ${STOCK}`);
    await sec.scrollIntoViewIfNeeded();
    await foto(page, '03d-existencia-apartada-al-autorizar');
  });

  await test.step('4. Mass approval from Pendientes: J1 signs N1 of TWO RQs, then J2 signs their N2', async () => {
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

    const masiva = async (p: Page, quien: string, nivel: number) => {
      await p.goto('/compras/pendientes');
      await p.waitForLoadState('networkidle');
      await expect(p.getByRole('heading', { name: 'Requisiciones por autorizar' })).toBeVisible();
      for (const h of ['Nivel', 'Disponibilidad', 'Recurrente'])
        await expect(p.getByRole('columnheader', { name: h, exact: true }), `column "${h}" must be visible in Pendientes`).toBeVisible();
      for (const f of folios) await p.getByRole('checkbox', { name: `Seleccionar ${f}` }).check();
      for (const f of folios) await expect(p.getByRole('checkbox', { name: `Seleccionar ${f}` })).toBeChecked();
      await expect(p.getByRole('button', { name: /Aprobar seleccionadas \(2\)/ })).toBeVisible();
      await foto(p, `04${quien}-pendientes-seleccion-n${nivel}`);
      await p.getByRole('button', { name: /Aprobar seleccionadas \(2\)/ }).click();
      const confirmar = p.getByRole('alertdialog').or(p.getByRole('dialog')).filter({ hasText: 'Aprobar 2 requisiciones' });
      await expect(confirmar).toBeVisible();
      const res = await apiCallOn(p, () => confirmar.getByRole('button', { name: 'Aprobar 2', exact: true }).click(),
        { method: 'POST', url: /requisiciones\/autorizaciones-masivas$/, status: [200, 207] });
      const body = JSON.parse(res.request().postData() ?? '{}');
      expect(body.nivel).toBe(nivel);
      for (const id of ids) expect(body.ids).toContain(id);
      const r = await res.json();
      expect((r.resultados ?? []).filter((x: { ok: boolean }) => !x.ok), 'every RQ of the batch signed').toEqual([]);
      await foto(p, `04${quien}-aprobacion-masiva-n${nivel}`);
    };
    await masiva(pj1, 'a-jefe-depto', 1);
    for (const id of ids) expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`))).estado, 'items without stock: N2 still missing (EnAutorizacion)').toBe(1);
    await masiva(pj2, 'b-jefe-almacen', 2);
    for (const f of folios) await expect(pj2.getByRole('checkbox', { name: `Seleccionar ${f}` })).toHaveCount(0);
    for (const id of ids) expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`))).estado).toBe(3);
  });

  let ocId = ''; let ocFolio = '';
  await test.step('5. C (buyer) creates the OC from the RQ, quotes the price and transmits (C cannot sign it)', async () => {
    await pc.goto(`/compras/requisiciones/${rqId}`);
    await pc.waitForLoadState('networkidle');
    await pc.locator('[data-action="convertir-a-oc"]').click();
    const sheet = pc.getByRole('dialog').last();
    await expect(sheet.locator('form[data-component="sheet-nueva-oc"]')).toBeVisible();
    await elegir(pc, sheet, 'Seleccionar proveedor', { buscar: { label: 'Buscar por clave, RFC o nombre…', texto: PROV.clave }, opcion: new RegExp(PROV.clave) });
    await elegir(pc, sheet, 'Seleccionar condiciones de pago');
    await sheet.getByRole('combobox', { name: 'Seleccionar uso principal' }).click();
    const usos = await pc.getByRole('option').allInnerTexts();
    test.info().annotations.push({ type: 'usos-principales', description: usos.join(' | ') });
    await pc.getByRole('option', { name: /Consumo|Operaci|Mantenimiento|Gasto|Insumo/i }).first().click().catch(async () => { await pc.getByRole('option').first().click(); });
    // the sheet runs two calls: POST /ordenes (empty header) and then POST /ordenes/{id}/lineas/desde-requisicion
    const creada = pc.waitForResponse((r) => r.request().method() === 'POST' && /\/compras\/ordenes$/.test(r.url()));
    const res = await apiCallOn(pc, () => sheet.locator('[data-action="submit"]').click(),
      { method: 'POST', url: /\/compras\/ordenes\/[^/]+\/lineas\/desde-requisicion$/, status: [200, 201] });
    const cres = await creada;
    expect(cres.status()).toBe(201);
    const cbody = JSON.parse(cres.request().postData() ?? '{}');
    expect(cbody.proveedorId).toBe(prov.id);
    expect(JSON.parse(res.request().postData() ?? '{}').requisicionId).toBe(rqId);
    ocId = (await cres.json()).id;
    await pc.waitForURL(new RegExp(`/compras/ordenes/${ocId}`));
    await pc.waitForLoadState('networkidle');
    const oc0 = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    ocFolio = oc0.folio;
    expect(oc0.lineas, 'the OC must carry only the shortage, not what Almacén already has').toHaveLength(1);
    expect(oc0.lineas[0].cantidad).toBe(FALTANTE);

    // the buyer quotes the price (RQ went without price)
    await pc.getByRole('tab', { name: 'Partidas' }).click();
    await pc.getByRole('button', { name: 'Editar partida 1' }).click();
    const form = pc.locator('form[aria-label^="Editar partida"]');
    await campo(form, /^Precio/).fill(String(PRECIO));
    await apiCallOn(pc, () => form.getByRole('button', { name: 'Guardar cambios' }).click(),
      { url: new RegExp(`/compras/ordenes/${ocId}/lineas/`), status: [200, 204] });
    await expect(pc.getByText(`$${FALTANTE * PRECIO}.00`).filter({ visible: true }).first()).toBeVisible();
    // «Datos para la factura del proveedor» are required before transmitting (OC_DATOS_FACTURA_INCOMPLETOS): captured from the header editor
    await foto(pc, '05a-oc-revision-previa-faltan-requisitos');
    await pc.getByRole('tab', { name: 'Información' }).click();
    await pc.getByRole('button', { name: 'Editar cabecera' }).click();
    const cab = pc.locator('form[aria-label="Editar cabecera"]');
    for (const [campoNombre, opcion] of [['Uso de CFDI', /^G03/], ['Método de pago', /^PUE/], ['Forma de pago', /^03 /]] as const) {
      await cab.getByLabel(campoNombre).click();
      await pc.getByRole('option', { name: opcion }).click();
    }
    await foto(pc, '05b-oc-datos-para-la-factura');
    await apiCallOn(pc, () => cab.getByRole('button', { name: 'Guardar cabecera' }).click(), { method: 'PATCH', url: new RegExp(`/compras/ordenes/${ocId}$`), status: [200, 204] });
    await adjuntarCotizacionPorUi(pc, guard, ocId);
    await pc.reload();
    await pc.waitForLoadState('networkidle');

    await pc.getByRole('button', { name: 'Transmitir a autorización' }).click();
    await apiCallOn(pc, () => pc.getByRole('button', { name: 'Transmitir', exact: true }).click(),
      { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/transmitir$`), status: [200, 204] });
    await expect(pc.getByText(`OC ${ocFolio} transmitida a autorización.`)).toBeVisible();

    // whoever captured the OC cannot authorize it: the button is disabled with the reason ...
    const aprobar = pc.getByRole('button', { name: 'Aprobar Nivel 1' });
    await expect(aprobar, 'C holds autorizar-nivel1 but captured the OC: "Aprobar Nivel 1" disabled').toBeDisabled();
    await aprobar.locator('..').hover();
    await expect(pc.getByRole('tooltip').getByText('Capturaste esta orden de compra: no puedes aprobarla. Debe aprobarla otra persona.').first()).toBeVisible();
    // ... and the API refuses it too (403 or 422)
    const t = (await C.api.post(`/api/v1/compras/ordenes/${ocId}/autorizaciones`, { nivel: 1 })).status();
    expect([403, 422], `C signing its own OC answered ${t}`).toContain(t);
    erroresC.length = 0; // that refusal is the expected negative case
    await foto(pc, '05-oc-creada-sin-poder-firmar');
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
    const ocFirmada = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    expect(ocFirmada.estado).toBe(3);
    // the header names the buyer («Comprador titular»), not «—» (#351)
    expect(ocFirmada.compradorNombre, 'OC detail carries the buyer name').toBeTruthy();
    await page.getByRole('tab', { name: 'Información' }).click();
    const titular = page.getByText('Comprador titular', { exact: true }).first();
    await expect(titular).toBeVisible();
    await expect(titular.locator('xpath=..')).toContainText(ocFirmada.compradorNombre);
    await titular.scrollIntoViewIfNeeded();
    await foto(page, '06b-oc-comprador-titular-con-nombre');
  });

  const uuidFiscal = uuid().toUpperCase();
  let lineaOcId = ''; let recFolio = ''; let subtotal = 0; let iva = 0; let ocInicial: any;
  await test.step('7. A (Almacén MID) receives the exact quantity (tolerance 0 %)', async () => {
    ocInicial = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    lineaOcId = ocInicial.lineas[0].id;
    subtotal = ocInicial.lineas[0].subtotalLinea; iva = Math.round(subtotal * 0.16 * 100) / 100;
    expect(subtotal).toBe(FALTANTE * PRECIO);
    const dlg = await abrirRecepcion(pa, ocFolio);
    await elegirBin(pa, { binClave: stock.binClave } as Escenario);
    await dlg.getByRole('button', { name: /Captura el folio fiscal/ }).click();
    await dlg.getByLabel(/Folio fiscal/).fill(uuidFiscal);
    await dlg.getByLabel('Cantidad a recibir, partida 1').fill(String(FALTANTE));
    const res = await apiCallOn(pa, () => dlg.getByRole('button', { name: 'Registrar recepción' }).click(),
      { method: 'POST', url: /\/almacen\/recepciones$/, status: 201 });
    recFolio = (await res.json()).folio;
    expect(recFolio, 'the reception returns its folio').toBeTruthy();
    const body = JSON.parse(res.request().postData() ?? '{}');
    expect(body.ordenCompraId).toBe(ocId);
    expect(body.lineas[0]).toMatchObject({ lineaOcId, cantidad: FALTANTE, articuloId: stock.articuloId });
    await foto(pa, '07-recepcion');
    // independent check of the stored quantity, seen from Compras through the event (Almacen -> Compras)
    await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).lineas[0].cantidadRecibida, {
      timeout: 40_000, message: 'EVENT BRIDGE: Compras never recorded the received quantity (OcRecepcionRegistrada).',
    }).toBe(FALTANTE);
    await expect.poll(async () => String((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).subEstadoRecepcion), {
      timeout: 40_000, message: 'EVENT BRIDGE: Compras never received OcRecepcionRegistrada. Run the API with the Service Bus emulator and Outbox enabled (see header).',
    }).not.toBe(String(ocInicial.subEstadoRecepcion));
  });

  let facturaId = ''; const folioFac = String(10000 + Math.floor(Math.random() * 90000)); // numeric, like the supplier's own folio
  await test.step('8. A (CxP clerk) uploads the CFDI (supplier RFC) and captures the reconciled invoice', async () => {
    await pa.goto('/cxp/cfdis');
    await pa.waitForLoadState('networkidle');
    await foto(pa, '08a-cxp-cfdis-recibidos-pantalla');
    await pa.getByRole('button', { name: 'Cargar CFDI' }).first().click();
    const sheet = pa.getByRole('dialog');
    await sheet.locator('#cfdi-xml').setInputFiles({ name: 'factura-proveedor.xml', mimeType: 'text/xml', buffer: Buffer.from(cfdiXml(uuidFiscal, prov.rfc, subtotal, iva, folioFac)) });
    await expect(sheet.getByRole('button', { name: 'Cargar CFDI' })).toBeEnabled();
    await foto(pa, '08b-cargar-cfdi-hoja-abierta');
    const cres = await apiCallOn(pa, () => sheet.getByRole('button', { name: 'Cargar CFDI' }).click(),
      { method: 'POST', url: /\/cfdis\/cargar$/, status: [200, 201] });
    const cfdi = await cres.json();
    expect(cfdi.id).toBeTruthy();
    await pa.getByLabel('Buscar por UUID o folio').fill(uuidFiscal);
    await pa.getByRole('button', { name: new RegExp(`Acciones para CFDI ${uuidFiscal}`, 'i') }).click();
    await pa.getByRole('menuitem', { name: 'Capturar factura' }).click();

    const cap = pa.getByRole('dialog', { name: 'Capturar factura desde OC' });
    await expect(cap).toBeVisible();
    await cap.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
    await pa.getByPlaceholder('Folio o referencia…').fill(ocFolio);
    await expect(pa.getByRole('option', { name: new RegExp(ocFolio) }, ), 'EVENT BRIDGE: the authorized OC must be visible in CxP (OrdenCompraAutorizadaEvent)').toBeVisible({ timeout: 30_000 });
    await pa.getByRole('option', { name: new RegExp(ocFolio) }).click();
    await cap.getByRole('button', { name: /Importar 1 partida/ }).click();
    await cap.getByRole('combobox', { name: 'Seleccionar línea de OC' }).click();
    await pa.getByRole('option').first().click();
    await foto(pa, '08c-capturar-factura-desde-oc-hoja-abierta');
    const res = await apiCallOn(pa, () => cap.getByRole('button', { name: 'Capturar factura' }).click(),
      { method: 'POST', url: /\/cuentas-por-pagar\/facturas$/, status: [200, 201] });
    const body = JSON.parse(res.request().postData() ?? '{}');
    expect(body).toMatchObject({ ordenCompraId: ocId, uuidCfdi: uuidFiscal, cfdiRecibidoId: cfdi.id });
    expect(body.lineas[0].lineaOcId).toBe(lineaOcId);
    expect(body.total).toBeCloseTo(subtotal + iva, 2);
    facturaId = (await res.json()).id;
    await expect(pa.getByText('Factura capturada')).toBeVisible();
    await expect(pa.getByText('Factura cancelada por diferencia en la conciliación')).toHaveCount(0);
    await foto(pa, '08-factura-conciliada');
    const fac = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`));
    expect(fac.estado).toBe(1); // Capturada, not cancelled by tolerance
    expect(fac.proveedorId).toBe(prov.id);
  });

  await test.step('9. B authorizes the invoice in CxP (3-way match: OC + reception + CFDI)', async () => {
    await page.goto(`/cxp/facturas/${facturaId}`);
    await page.waitForLoadState('networkidle');
    await expect(page.getByText('Sin diferencia').first(), 'the 3-way match must show «Sin diferencia»').toBeVisible();
    // folio as SERIE-FOLIO with no double prefix
    await expect(page.getByText(`A-${folioFac}`, { exact: true }).first()).toBeVisible();
    await expect(page.getByText(new RegExp(`[A-Z]-[A-Z]-${folioFac}`))).toHaveCount(0);
    // «Recepción» shows the folio of the OC reception (3-way match uses it) instead of «—»
    const facDet = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${facturaId}`));
    expect(facDet.recepcionFolio, 'invoice detail carries the reception folio').toContain(recFolio);
    await expect(page.getByText(recFolio).first()).toBeVisible();
    // due date: supplier credit days (or OC terms); if it equals the document date the UI says it is cash
    if (String(facDet.fechaVencimiento).slice(0, 10) === String(facDet.fechaDocumento).slice(0, 10))
      await expect.soft(page.getByText('(contado, sin días de crédito)'), 'cash invoice (OC terms «Contado»): the header must say so').toBeVisible();
    else
      await expect(page.getByText('(contado, sin días de crédito)')).toHaveCount(0);
    test.info().annotations.push({ type: 'vencimiento', description: `documento ${facDet.fechaDocumento} vence ${facDet.fechaVencimiento}` });
    await foto(page, '09a-cxp-factura-folio-recepcion-y-vencimiento');
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
    await pa.getByRole('option').filter({ hasText: CUENTA_MILLET.banco }).filter({ hasText: CUENTA_MILLET.numero.slice(-4) }).first().click();
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
    await capturarSaldoPortal(page);
    await page.getByRole('button', { name: 'Firmar y autorizar' }).click();
    const dlg = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Firmar la corrida' });
    await expect(dlg.getByRole('button', { name: 'Firmar y autorizar' })).toBeEnabled();
    await page.waitForTimeout(300);
    await foto(page, '10f-dialogo-firmar-corrida');
    await dlg.getByRole('button', { name: 'Firmar y autorizar' }).click();
    await expect(page.getByText('Autorizada', { exact: true }).first()).toBeVisible();
    // the signature shows who signed (name), not the user GUID
    const firmas = (await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`))).firmas as { usuarioId: string; usuarioNombre?: string }[];
    expect(firmas).toHaveLength(1);
    expect(firmas[0].usuarioNombre, 'signature carries the signer name').toBeTruthy();
    await expect(page.getByText(firmas[0].usuarioNombre!).first()).toBeVisible();
    await expect(page.getByText(firmas[0].usuarioId)).toHaveCount(0);
    await foto(page, '10g-corrida-autorizada-firma-con-nombre');

    // --- T9.3 BBVA layout: hidden in the UI, bulk payments are Phase 2 (Eliam, 03-nov)

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
    await page.getByRole('button', { name: 'Cargar extracto', exact: true }).click();
    const dlg = page.getByRole('dialog');
    await dlg.getByRole('textbox').last().fill(texto);
    await foto(page, '12a-conciliacion-cargar-extracto-bbva-formato-DEMO');
    await apiCallOn(page, () => dlg.getByRole('button', { name: 'Cargar y Procesar' }).click(), { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201] });
    await expect(page.getByText('Extracto cargado y procesado correctamente').first()).toBeVisible();
    await expect(page.getByText('1 de 1 líneas emparejadas'), 'the statement line must auto-match the payment (same reference and amount)').toBeVisible();
    // the bank movement of the payment is labelled «Pago a proveedor», not «Sin concepto»
    const libro = page.getByRole('row').filter({ hasText: `Ref: ${referencia}` }).filter({ hasText: 'Conciliado' });
    await expect(libro).toHaveCount(1);
    await expect(libro).toContainText('Pago a proveedor');
    await expect(libro).not.toContainText('Sin concepto');
    await foto(page, '12b-conciliacion-linea-emparejada-concepto-pago-a-proveedor-formato-DEMO');
    // the statement charge is shown as negative (the API sends the absolute amount, #351)
    const cargo = page.getByRole('row').filter({ hasText: referencia }).filter({ hasText: /PAGO SPEI/ }).first();
    const montoTxt = `$${total.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    await expect(cargo).toContainText(`-${montoTxt}`);
    await expect(cargo).not.toContainText(`+${montoTxt}`);
    await cargo.scrollIntoViewIfNeeded();
    await foto(page, '12c-cargo-del-extracto-con-signo-menos');
    // CA9.5: importing the SAME statement again does not duplicate its lines
    const antes = await ok(await guard.api.get(`/api/v1/tesoreria/conciliaciones?cuentaBancariaId=${CUENTA_MILLET.id}`));
    const conc = lista(antes).find((c: { periodoAnio: number; periodoMes: number }) => c.periodoAnio === d.getFullYear() && c.periodoMes === d.getMonth() + 1);
    expect(conc, 'the open reconciliation of the month').toBeTruthy();
    await page.getByRole('button', { name: 'Cargar extracto', exact: true }).click();
    await page.getByRole('dialog').getByRole('textbox').last().fill(texto);
    const r2 = await apiCallOn(page, () => page.getByRole('dialog').getByRole('button', { name: 'Cargar y Procesar' }).click(), { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201, 409, 422] });
    const resumen2 = await r2.json().catch(() => ({}));
    test.info().annotations.push({ type: 'extracto-repetido', description: `${r2.status()} totalLineas=${resumen2.totalLineas}` });
    const det = await ok(await guard.api.get(`/api/v1/tesoreria/conciliaciones/${conc.id}`));
    expect((det.lineas ?? []).length, 'same statement imported twice: still ONE line').toBe(1);
    await expect(page.getByText('1 de 1 líneas emparejadas')).toBeVisible();
    await foto(page, '12d-extracto-repetido-no-duplica-formato-DEMO');
  });

  await intentar('13. Accounting: exactly the pólizas EM, TT and PP of this purchase, with their amounts; the OC bridge account ends at 0', async () => {
    await page.goto('/contabilidad/polizas');
    await page.waitForLoadState('networkidle');
    await foto(page, '13a-contabilidad-polizas');
    const rech = await (await guard.api.get('/api/v1/contabilidad/motor/rechazos')).json();
    const rechazos = (Array.isArray(rech) ? rech : (rech.items ?? [])) as { causa: string; tipoEvento: string; estado: unknown }[];
    test.info().annotations.push({ type: 'rechazos-contables', description: rechazos.map((x) => `${x.tipoEvento}:${x.causa}:${x.estado}`).join(' | ') || 'ninguno' });
    // our documents: the reception (folio), the invoice (id / CFDI UUID / supplier folio) and the payment (bank reference)
    const marcas = [recFolio, facturaId, uuidFiscal, uuidFiscal.toLowerCase(), referencia, `A-${folioFac}`];
    const nuestras = async () => {
      const r = lista(await ok(await guard.api.get('/api/v1/contabilidad/polizas?limit=200')));
      const out: any[] = [];
      for (const p of r.filter((x: { tipoClave: string }) => ['EM', 'TT', 'PP', 'PC', 'SM'].includes(x.tipoClave))) {
        const det = await ok(await guard.api.get(`/api/v1/contabilidad/polizas/${p.id}`));
        if (marcas.some((m) => JSON.stringify(det).includes(m))) out.push({ ...det, tipoClave: p.tipoClave, totalCargos: p.totalCargos, totalAbonos: p.totalAbonos });
      }
      return out;
    };
    await expect.poll(async () => (await nuestras()).map((p) => p.tipoClave).sort().join(','), { timeout: 60_000, message: 'pólizas of the journey (EM reception, TT invoice, PP payment)' }).toBe('EM,PP,TT');
    const pol = await nuestras();
    const de = (t: string) => pol.find((p) => p.tipoClave === t);
    const sum = (p: any, k: 'cargo' | 'abono') => Math.round(p.partidas.reduce((a: number, x: any) => a + (x[k] ?? 0), 0) * 100) / 100;
    test.info().annotations.push({ type: 'polizas', description: pol.map((p) => `${p.folio ?? p.tipoClave} ${sum(p, 'cargo')}/${sum(p, 'abono')}`).join(' | ') });
    expect(sum(de('EM'), 'cargo'), 'EM: inventory at the received subtotal').toBeCloseTo(subtotal, 2);
    expect(sum(de('TT'), 'cargo'), 'TT: bridge (subtotal) + IVA = invoice total').toBeCloseTo(total, 2);
    expect(sum(de('PP'), 'cargo'), 'PP: supplier paid = invoice total').toBeCloseTo(total, 2);
    for (const p of pol) expect(sum(p, 'cargo'), `${p.tipoClave} balanced`).toBeCloseTo(sum(p, 'abono'), 2);
    // bridge account of the OC (role MERCANCIA_RECIBIDA_POR_FACTURAR): EM credits it, TT debits it -> 0
    const puente = sql(`select c.codigo from contabilidad.roles_cuenta r join contabilidad.cuentas_contables c on c.id = r.cuenta_id where r.codigo = 'MERCANCIA_RECIBIDA_POR_FACTURAR' and r.deleted_at is null limit 1`);
    expect(puente, 'the bridge role is mapped to an account').toBeTruthy();
    const saldoPuente = Math.round(pol.flatMap((p) => p.partidas).filter((x: any) => x.cuentaCodigo === puente).reduce((a: number, x: any) => a + (x.cargo ?? 0) - (x.abono ?? 0), 0) * 100) / 100;
    expect(saldoPuente, `bridge account ${puente} of the OC must end at 0`).toBe(0);
    const em = de('EM');
    await page.goto(`/contabilidad/polizas/${em.id}`).catch(() => {});
    await page.waitForLoadState('networkidle');
    await foto(page, '13b-poliza-EM-entrada-de-mercancia');
    for (const t of ['TT', 'PP']) { await page.goto(`/contabilidad/polizas/${de(t).id}`).catch(() => {}); await page.waitForLoadState('networkidle'); await foto(page, `13${t === 'TT' ? 'c' : 'd'}-poliza-${t}`); }
    expect(rechazos.map((x) => `${x.tipoEvento}:${x.causa}`), 'events of the journey left in the accounting inbox').toEqual([]);
  });
  expect(hallazgos, 'steps beyond the payment that did not work end to end').toEqual([]);

  expect(erroresA, 'API errors seen by user A').toEqual([]);
  expect(erroresC, 'API errors seen by user C').toEqual([]);
});
