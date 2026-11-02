/**
 * DEMO-COMPRA-PAGO, gaps of the demo script that had no screenshot or were never proven on screen:
 *   DEMO-02 (a)+(e)  OC without price is rejected on transmit with a readable message; the quotation is attached through the UI.
 *   DEMO-03 (d)      Import OC: 422 without incoterm, no UUID in "Comprador titular" / "Encargado de compras", then it goes out.
 *   DEMO-04 (f)      Mass approval of OCs with the checkboxes visibly selected before approving.
 *
 * (b) pay sheet and (c) "Cargar CFDI" / "Capturar factura desde OC" sheets live in compra-a-pago.spec.ts (DEMO-01), because they
 * need the whole purchase-to-payment chain (which uses SQL arrange helpers and therefore cannot run against dev).
 *
 * These three specs use NO sql(): everything is created through the UI or the app's own API, so they can run in dev.
 * Data uses neutral names (no "E2E-" prefix) wherever the harness allows it.
 */
import { apiCallOn, expect, expectNoPlaceholders, reloadAndExpect, shot, test } from '../../_harness/fixtures';
import type { Page } from '../../_harness/fixtures';
import { asRole, crearArticulo, CC_SEED, DEPTO_COMPRAS, MID, ok, OPER_EMAIL, proveedorSeed, rand, SA_EMAIL, warm } from '../86e3pc50v/_data';
import { designarAprobador, JEFE_PERMS, PERSONA } from './_demo';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(600_000);

const A_PERMS = [
  'compras.requisiciones.crear', 'compras.requisiciones.leer', 'compras.requisiciones.editar',
  'compras.ordenes.crear', 'compras.ordenes.leer', 'compras.ordenes.adjuntar', 'compras.ordenes.logistica',
  'compras.ordenes.importacion', 'compras.ordenes.transmitir',
  'centros_costo.dim3.leer-todos', 'compartido.catalogos.leer', 'datos_maestros.proveedores.leer',
];
const UUID_RE = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;
const PDF = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

async function foto(p: Page, nombre: string) {
  await p.waitForLoadState('networkidle');
  await p.waitForTimeout(1200); // visual settle only (toast / sheet animations)
  await shot(p, nombre);
}

async function elegirOpcion(page: Page, scope: ReturnType<Page['locator']>, nombre: string, opcion?: RegExp) {
  await scope.getByRole('combobox', { name: nombre }).click();
  const o = opcion ? page.getByRole('option', { name: opcion }) : page.getByRole('listbox').getByRole('option');
  await o.first().click();
}

const campo = (form: ReturnType<Page['locator']>, etiqueta: RegExp) =>
  form.locator('div.space-y-1', { has: form.page().locator('label', { hasText: etiqueta }) }).locator('input').first();

async function conceder(guard: any, cleanup: any, email: string, codigos: string[]) {
  const me = await ok(await guard.api.get('/api/auth/me'));
  const users = await ok(await guard.api.get('/api/v1/identidad/usuarios?limit=100'));
  const userId: string = users.items.find((u: { email: string }) => u.email === email).id;
  const cat = await ok(await guard.api.get('/api/v1/identidad/permisos'));
  const base = `/api/v1/identidad/usuarios/${userId}/empresas/${me.currentEmpresaId}`;
  const previos = (await ok(await guard.api.get(`${base}/permisos`))).permisos
    .filter((p: any) => p.origen === 'Concedido' || p.origen === 'Denegado')
    .map((p: any) => ({ permisoId: p.permisoId, efecto: p.origen === 'Concedido' ? 'Conceder' : 'Denegar', motivo: null }));
  cleanup.add(async () => {
    const r = await guard.api.put(`${base}/permisos-override`, { overrides: previos });
    if (r.status() >= 300) throw new Error(`could not restore overrides: ${r.status()}`);
  });
  const nuevos = codigos.map((c) => cat.items.find((x: any) => x.codigo === c)).filter(Boolean)
    .filter((p: any) => !previos.some((q: any) => q.permisoId === p.id)).map((p: any) => ({ permisoId: p.id, efecto: 'Conceder', motivo: 'Demo compra a pago' }));
  const put = await guard.api.put(`${base}/permisos-override`, { overrides: [...previos, ...nuevos] });
  expect(put.status(), await put.text()).toBe(200);
}

/** Arrange: umbrals so that N1 alone authorizes small OCs (two users only). */
async function umbrales(guard: any) {
  await ok(await guard.api.put('/api/v1/compras/umbrales-aprobacion', { departamentoId: '00000000-0000-0000-0000-000000000000', umbralMenorCuantia: 1000, umbralMonto: 10000, moneda: 'MXN' }));
  await ok(await guard.api.put('/api/v1/compras/umbrales-aprobacion', { departamentoId: DEPTO_COMPRAS, umbralMenorCuantia: 1000, umbralMonto: 10000, moneda: 'MXN' }));
}

/**
 * Arrange (API): the JefeAlmacen of MID (another real user) designated as N2 approver of the RQ matrix (C3.1). With «Modo de nivel 2»
 * at its default SoloDisponibilidad (VILO decision 18-oct) an RQ whose article has no stock asks for N2, and B cannot sign both levels.
 */
async function jefeAlmacen(browser: any, guard: any, cleanup: any) {
  const J2 = await asRole(browser, PERSONA.jefeAlmacen.rol, PERSONA.jefeAlmacen.email, '/compras/requisiciones');
  cleanup.add(async () => { await J2.context.close(); });
  await conceder(guard, cleanup, PERSONA.jefeAlmacen.email, JEFE_PERMS);
  await designarAprobador(guard, cleanup, 1, MID, PERSONA.jefeAlmacen.email);
  return J2;
}

/**
 * Arrange (API): a transmitted RQ of user A authorized by B (N1) and, since nothing is in stock, by the JefeAlmacen (N2), so A can
 * "Convertir a OC". No price.
 * Clasificación «Orden de compra» (1): the article has no category and naturaleza Bien, and the RQ type filter
 * (ArticulosPorClasificacion, 86e3tfedd) only admits such articles in an OC-type RQ; Materia prima/Pinturas need a mapped category.
 */
async function rqAutorizada(A: Awaited<ReturnType<typeof asRole>>, J2: Awaited<ReturnType<typeof asRole>>, guard: any, cantidad: number, descripcion: string) {
  const art = await crearArticulo(guard, { clave: `INS-${rand()}` });
  const r = await ok(await A.api.post('/api/v1/compras/requisiciones', {
    sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: 1, prioridad: 1,
    fechaSolicitud: new Date().toISOString(), descripcion: `${descripcion} ${rand()}`, fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null,
  }));
  const id = (r.id ?? r.requisicionId) as string;
  await ok(await A.api.post(`/api/v1/compras/requisiciones/${id}/lineas`, { articuloId: art.id, cantidad, unidadMedida: 'PZA', precioEstimadoMonto: 0, precioEstimadoMoneda: 'MXN', centroCostoId: CC_SEED }));
  await ok(await A.api.post(`/api/v1/compras/requisiciones/${id}/transmitir`));
  await ok(await guard.api.post(`/api/v1/compras/requisiciones/${id}/autorizaciones`, { nivel: 1 }));
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`))).estado, 'no stock: N1 alone leaves the RQ EnAutorizacion').toBe(1);
  await ok(await J2.api.post(`/api/v1/compras/requisiciones/${id}/autorizaciones`, { nivel: 2 }));
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`))).estado, 'RQ Autorizada').toBe(2);
  return id;
}

/** UI: A converts the RQ into an OC (supplier, payment terms, main use) and lands on the OC detail. */
async function ocDesdeRq(pa: Page, rqId: string, opts: { importacion?: boolean } = {}) {
  await pa.goto(`/compras/requisiciones/${rqId}`);
  await pa.waitForLoadState('networkidle');
  await pa.locator('[data-action="convertir-a-oc"]').click();
  const descartar = pa.getByRole('button', { name: 'Descartar y empezar de nuevo' });
  if (await descartar.isVisible({ timeout: 2000 }).catch(() => false)) await descartar.click();
  const sheet = pa.getByRole('dialog').last();
  await expect(sheet.locator('form[data-component="sheet-nueva-oc"]')).toBeVisible();
  await sheet.getByRole('combobox', { name: 'Seleccionar proveedor' }).click();
  await pa.getByLabel('Buscar proveedor por clave').fill('PROV-EST');
  await pa.getByRole('option', { name: /PROV-EST/ }).first().click();
  await elegirOpcion(pa, sheet, 'Seleccionar condiciones de pago');
  await elegirOpcion(pa, sheet, 'Seleccionar uso principal');
  if (opts.importacion) await sheet.locator('[data-field="es-importacion"]').check();
  const creada = pa.waitForResponse((x) => x.request().method() === 'POST' && /\/compras\/ordenes$/.test(x.url()));
  await sheet.locator('[data-action="submit"]').click();
  const res = await creada;
  expect(res.status()).toBe(201);
  const id = (await res.json()).id as string;
  await pa.waitForURL(new RegExp(`/compras/ordenes/${id}`));
  await pa.waitForLoadState('networkidle');
  return id;
}

/** UI: attaches a document of the given type from the "Adjuntos" tab and checks the real POST, the list and the persisted state. */
async function adjuntarPorUi(pa: Page, guard: any, ocId: string, clave: 'cotizacion' | 'ficha_tecnica', nombre: string) {
  await pa.getByRole('tab', { name: 'Adjuntos' }).click();
  const mgr = pa.locator('[data-component="adjuntos-manager"]');
  await mgr.locator('input[type="file"]').setInputFiles({ name: nombre, mimeType: 'application/pdf', buffer: PDF });
  await expect(mgr.locator('[data-component="adjuntos-staging"]')).toContainText(nombre);
  await mgr.getByRole('combobox', { name: 'Tipo de documento' }).click();
  await pa.getByRole('option', { name: new RegExp(`\\(${clave}\\)`) }).click();
  await apiCallOn(pa, () => mgr.locator('[data-action="confirmar-upload"]').click(),
    { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/adjuntos`), status: [200, 201] });
  await expect(pa.getByText(`"${nombre}" adjuntado.`)).toBeVisible();
  await expect(mgr.locator('[data-component="adjuntos-list"]')).toContainText(nombre);
  const oc = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
  expect(oc.adjuntos.map((a: { nombreArchivo: string }) => a.nombreArchivo), 'attachment persisted in the backend').toContain(nombre);
}

/** UI: buyer quotes the price (and optionally the tariff fraction) on line 1. */
async function ponerPrecio(pa: Page, ocId: string, precio: number, fraccion?: string) {
  await pa.getByRole('tab', { name: 'Líneas' }).click();
  await pa.getByRole('button', { name: 'Editar línea 1' }).click();
  const form = pa.locator('form[aria-label^="Editar línea"]');
  await campo(form, /^Precio/).fill(String(precio));
  if (fraccion) await form.locator('[data-field="fraccion-arancelaria"]').fill(fraccion);
  const res = await apiCallOn(pa, () => form.getByRole('button', { name: 'Guardar cambios' }).click(),
    { url: new RegExp(`/compras/ordenes/${ocId}/lineas/`), status: [200, 204] });
  expect(res.request().postData() ?? '').toContain(String(precio));
}

async function transmitir(pa: Page, ocId: string, status: number[]) {
  await pa.getByRole('button', { name: 'Transmitir a autorización' }).click();
  return apiCallOn(pa, () => pa.getByRole('button', { name: 'Transmitir', exact: true }).click(),
    { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/transmitir$`), status });
}

test('DEMO-02 [a][e] @evidence OC without price is rejected on transmit; quotation attached through the UI', async ({ page, guard, cleanup, browser }) => {
  guard.expectApiMutationError(422);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const pa = A.page;
  await conceder(guard, cleanup, OPER_EMAIL, A_PERMS);
  await umbrales(guard);
  await proveedorSeed(guard);
  const J2 = await jefeAlmacen(browser, guard, cleanup);
  const rqId = await rqAutorizada(A, J2, guard, 10, 'Insumos de oficina');

  const ocId = await ocDesdeRq(pa, rqId);
  const oc0 = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
  expect(oc0.lineas[0].precioUnitario ?? 0, 'the OC line starts without price').toBe(0);

  // (e) quotation attached with the real upload control
  await adjuntarPorUi(pa, guard, ocId, 'cotizacion', 'cotizacion-papeleria.pdf');
  await foto(pa, 'DEMO-02-01-cotizacion-adjunta');
  await reloadAndExpect(pa, async () => {
    await pa.getByRole('tab', { name: 'Adjuntos' }).click();
    await expect(pa.locator('[data-component="adjuntos-list"]')).toContainText('cotizacion-papeleria.pdf');
  });

  // (a) transmit WITHOUT price -> readable rejection
  const rej = await transmitir(pa, ocId, [422]);
  expect(await rej.text()).toContain('OC_PRECIO_REQUERIDO');
  await expect(pa.getByText(/Captura el precio de las líneas .* antes de enviar a autorización/).first()).toBeVisible();
  expect(await pa.locator('body').innerText()).not.toMatch(UUID_RE);
  await foto(pa, 'DEMO-02-02-oc-sin-precio-rechazada');
  expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado, 'OC must stay in Borrador').toBe(0);

  // with price it goes out
  await pa.reload();
  await pa.waitForLoadState('networkidle');
  await ponerPrecio(pa, ocId, 15);
  await reloadAndExpect(pa, async () => { await expect(pa.getByRole('button', { name: 'Transmitir a autorización' })).toBeVisible(); });
  const okRes = await transmitir(pa, ocId, [200, 204]);
  expect(okRes.ok()).toBe(true);
  const folio = (await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).folio as string;
  await expect(pa.getByText(`OC ${folio} transmitida a autorización.`)).toBeVisible();
  await expectNoPlaceholders(pa);
  await foto(pa, 'DEMO-02-03-oc-con-precio-transmitida');
  expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado).toBe(1); // EnAutorizacionJefeCompras
});

test('DEMO-03 [d] @evidence import OC: 422 without incoterm shows no UUIDs, then goes out with incoterm and country', async ({ page, guard, cleanup, browser }) => {
  guard.expectApiMutationError(422);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const pa = A.page;
  await conceder(guard, cleanup, OPER_EMAIL, A_PERMS);
  await umbrales(guard);
  await proveedorSeed(guard);
  const J2 = await jefeAlmacen(browser, guard, cleanup);
  const rqId = await rqAutorizada(A, J2, guard, 10, 'Refacciones importadas');

  const ocId = await ocDesdeRq(pa, rqId, { importacion: true });
  await ponerPrecio(pa, ocId, 15, '70052101');
  await adjuntarPorUi(pa, guard, ocId, 'cotizacion', 'cotizacion-importacion.pdf');
  await adjuntarPorUi(pa, guard, ocId, 'ficha_tecnica', 'ficha-tecnica.pdf');
  await pa.reload();
  await pa.waitForLoadState('networkidle');

  // import form (no incoterm / country)
  await pa.getByRole('tab', { name: 'Información' }).click();
  await pa.getByRole('tab', { name: 'Importación' }).click();
  await pa.locator('[data-action="editar-info-importacion"]').click();
  const f = pa.locator('form[data-component="info-importacion-edit"]');
  await f.getByLabel('BL / AWB').fill('MEDU1234567');
  await f.getByRole('combobox', { name: /Tipo de transporte/ }).click();
  await pa.getByRole('option', { name: /Mar/ }).first().click();
  await f.getByLabel('Agente aduanal / naviera').fill('Agencia aduanal del Pacifico SA');
  await f.getByLabel('Puerto / aduana de entrada').fill('Manzanillo');
  await f.getByLabel('Fecha estimada de arribo (ETA)').fill('2026-12-15');
  const patch = await apiCallOn(pa, () => f.locator('[data-action="submit-info-importacion"]').click(),
    { method: 'PATCH', url: 'informacion-importacion', status: [200, 204] });
  expect(patch.request().postData() ?? '').toContain('MEDU1234567');
  await pa.waitForLoadState('networkidle');

  // transmit without incoterm -> 422 with the toast, and no UUID anywhere on the page
  await pa.getByRole('tab', { name: 'Información' }).click();
  const rej = await transmitir(pa, ocId, [422]);
  expect(await rej.text()).toContain('IMPORT_INCOTERM_REQUERIDO');
  await expect(pa.getByText(/Falta el incoterm de la operación/)).toBeVisible();
  // "Comprador titular" / "Encargado de compras" live in the header sub-tab: show it while the toast is still on screen
  await pa.locator('[data-subtab="cabecera"]').click();
  await expect(pa.getByText('Comprador titular').first()).toBeVisible();
  const texto = await pa.locator('body').innerText();
  expect(texto, 'no UUID may be visible (Comprador titular / Encargado de compras must show name or dash)').not.toMatch(UUID_RE);
  await expect(pa.getByText('Comprador titular').first()).toBeVisible();
  await expect(pa.getByText('Encargado de compras').first()).toBeVisible();
  expect.soft(texto, 'neutral demo data').not.toMatch(/E2E/i);
  await shot(pa, 'DEMO-03-01-importacion-422-sin-incoterm'); // no settle wait: the error toast must still be visible
  expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado, 'OC must stay in Borrador').toBe(0);

  // incoterm + country -> goes out
  await pa.reload();
  await pa.waitForLoadState('networkidle');
  await pa.getByRole('tab', { name: 'Información' }).click();
  await pa.getByRole('tab', { name: 'Importación' }).click();
  await pa.locator('[data-action="editar-info-importacion"]').click();
  const f2 = pa.locator('form[data-component="info-importacion-edit"]');
  await f2.getByRole('combobox', { name: /Incoterm/ }).click();
  await pa.getByRole('option', { name: /FOB/ }).first().click();
  await f2.getByLabel('País de origen').fill('CN');
  await apiCallOn(pa, () => f2.locator('[data-action="submit-info-importacion"]').click(),
    { method: 'PATCH', url: 'informacion-importacion', status: [200, 204] });
  await pa.waitForLoadState('networkidle');
  const okRes = await transmitir(pa, ocId, [200, 204]);
  expect(okRes.ok()).toBe(true);
  const folio = (await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).folio as string;
  await expect(pa.getByText(`OC ${folio} transmitida a autorización.`)).toBeVisible();
  expect(await pa.locator('body').innerText()).not.toMatch(UUID_RE);
  await expectNoPlaceholders(pa);
  await foto(pa, 'DEMO-03-02-importacion-enviada');
  await reloadAndExpect(pa, async () => { await expect(pa.getByText('En autorización', { exact: false }).first()).toBeVisible(); });
  expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado).toBe(1);
});

test('DEMO-04 [f] @evidence mass approval of OCs: checkboxes visibly selected before approving', async ({ page, guard, cleanup, browser }) => {
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const pa = A.page;
  await conceder(guard, cleanup, OPER_EMAIL, A_PERMS);
  await umbrales(guard);
  await proveedorSeed(guard);
  const J2 = await jefeAlmacen(browser, guard, cleanup);

  const ids: string[] = []; const folios: string[] = [];
  for (const desc of ['Papeleria mensual', 'Toner para impresoras']) {
    const rqId = await rqAutorizada(A, J2, guard, 6, desc);
    const ocId = await ocDesdeRq(pa, rqId);
    await adjuntarPorUi(pa, guard, ocId, 'cotizacion', 'cotizacion-proveedor.pdf');
    await ponerPrecio(pa, ocId, 15);
    await pa.reload();
    await pa.waitForLoadState('networkidle');
    expect((await transmitir(pa, ocId, [200, 204])).ok()).toBe(true);
    ids.push(ocId);
    folios.push((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).folio as string);
  }

  await page.goto('/compras/ordenes/pendientes-autorizacion');
  await page.waitForLoadState('networkidle');
  // B (super-admin) holds N1 and N2, so the level tab switcher must exist: it is not optional
  await page.getByRole('tab', { name: /Nivel 1/ }).click();
  for (const folio of folios) await page.getByRole('checkbox', { name: `Seleccionar ${folio}` }).check();
  for (const folio of folios) await expect(page.getByRole('checkbox', { name: `Seleccionar ${folio}` })).toBeChecked();
  const boton = page.getByRole('button', { name: 'Aprobar seleccionadas (2)' });
  await expect(boton).toBeVisible();
  await foto(page, 'DEMO-04-01-oc-pendientes-seleccion');

  const res = await apiCallOn(page, () => boton.click(),
    { method: 'POST', url: /\/compras\/ordenes\/autorizaciones-masivas$/, status: [200, 207] });
  expect(JSON.stringify(JSON.parse(res.request().postData() ?? '{}'))).toContain(ids[0]);
  expect(res.request().postData() ?? '').toContain(ids[1]);
  const resumen = page.locator('[data-component="resumen-aprobacion-masiva"]');
  await expect(resumen).toContainText('2 aprobadas');
  await expect(resumen).toContainText('0 con errores');
  for (const folio of folios) await expect(resumen).toContainText(folio);
  await expectNoPlaceholders(page);
  await foto(page, 'DEMO-04-02-oc-aprobacion-masiva-resultado');
  for (const id of ids) expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${id}`))).estado, 'OC authorized by N1').toBe(3);
});
