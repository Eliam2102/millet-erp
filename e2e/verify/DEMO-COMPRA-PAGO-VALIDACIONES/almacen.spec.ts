/**
 * Validaciones de RECEPCIÓN DE ALMACÉN: cantidades, estado de la OC, cancelación y periodo contable.
 * Cada caso: la pantalla bloquea o muestra el mensaje y el servidor rechaza la misma operación (status y código afirmados).
 */
import type { Page } from '@playwright/test';
import { test, expect, shot, type Guard } from '../../_harness/fixtures';
import {
  abrirContabilidad, crearArticulo, crearBin, CC_SEED, DEPTO_COMPRAS, MID, nombrarCatalogos, ok, otroUsuarioId, PROV_VIDRIO, rand, rechazo, sql, today, uuid, foto } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(180_000);
test.beforeEach(async ({ page }) => { await page.goto('/almacen/recepciones'); await page.waitForLoadState('networkidle'); }); // el token de la API se captura de la primera llamada de la página

type Esc = { ocId: string; folio: string; lineaId: string; articuloId: string; articuloNombre: string; binId: string; binClave: string; cantidad: number };
const S: { yo?: string } = {};

const settle = async (p: Page) => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(700); };

/** OC autorizada (arrange: el estado se fija en BD, la firma de otra persona no es lo que se prueba aquí) con una línea de vidrio. */
async function ocAutorizada(guard: Guard, nombre: string, cantidad: number): Promise<Esc> {
  const art = await crearArticulo(guard, { clave: `VID-TMP-${rand()}`, nombre });
  const bin = await crearBin(guard, [art.id]);
  const oc = await ok(await guard.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: PROV_VIDRIO,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: 'Reposición de existencias', observaciones: 'Reposición de existencias',
  }));
  await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/lineas`, {
    articuloId: art.id, cantidad, unidadMedida: 'PZA', precioUnitario: 850, departamentoSolicitanteId: DEPTO_COMPRAS, centroCostoId: CC_SEED,
  }));
  const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  sql(`update compras.ordenes_compra set estado=3, fecha_contabilizacion=now() where id='${oc.id}'`);
  return { ocId: oc.id, folio: det.folio, lineaId: det.lineas[0].id, articuloId: art.id, articuloNombre: nombre, binId: bin.id, binClave: bin.clave, cantidad };
}

const cuerpo = (e: Esc, cantidad: number, extra: Record<string, unknown> = {}) => ({
  ordenCompraId: e.ocId, fechaMovimiento: today(), cfdiUuidFiscal: uuid().toUpperCase(), observaciones: 'Recepción de mercancía',
  lineas: [{ articuloId: e.articuloId, lineaOcId: e.lineaId, cantidad, ubicacionId: e.binId }], ...extra,
});

/** Abre «Nueva recepción», elige la OC, marca la línea, rack y folio fiscal, y deja la cantidad indicada. */
async function llenarRecepcion(page: Page, e: Esc, cantidad: number, fecha?: string) {
  await page.goto('/almacen/recepciones');
  await settle(page);
  await page.getByRole('button', { name: /Nueva recepci/ }).first().click();
  const dlg = page.getByRole('dialog', { name: 'Nueva recepción' });
  await expect(dlg).toBeVisible();
  await dlg.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
  await page.getByPlaceholder('Folio o referencia…').fill(e.folio);
  await page.getByRole('option', { name: new RegExp(e.folio) }).click();
  await dlg.getByLabel(/Incluir partida 1 en la recepción/).check();
  await dlg.getByRole('combobox', { name: 'Seleccionar ubicación' }).last().click();
  await page.getByRole('option', { name: new RegExp(e.binClave) }).click();
  await dlg.getByText(/Captura el folio fiscal/).click();
  await dlg.getByLabel(/Folio fiscal/).fill(uuid().toUpperCase());
  await dlg.getByLabel(/Cantidad a recibir, partida 1/).fill(String(cantidad));
  if (fecha) await dlg.getByLabel('Fecha de movimiento').fill(fecha);
  return dlg;
}

async function prep(guard: Guard) {
  if (S.yo) return;
  nombrarCatalogos();
  await abrirContabilidad(guard);
  S.yo = (await ok(await guard.api.get('/api/auth/me'))).userId;
}

test('V-RC-01 recibir más de lo ordenado se rechaza (Almacén recibe exacto, sin tolerancia en piezas)', async ({ page, guard }) => {
  await prep(guard);
  const e = await ocAutorizada(guard, 'Vidrio templado incoloro 10 mm', 100);
  const dlg = await llenarRecepcion(page, e, 120);
  // La pantalla bloquea antes de enviar: el aviso sale en la partida y no se hace ninguna llamada de registro
  await dlg.getByRole('button', { name: 'Registrar recepción' }).click();
  const aviso = page.getByText(/Excede lo pendiente de la orden de compra \(100\)\. Almacén recibe exacto\./).first();
  await aviso.scrollIntoViewIfNeeded();
  await expect(aviso).toBeVisible();
  expect(guard.calls.filter((c) => c.method === 'POST' && /\/almacen\/recepciones$/.test(c.url)), 'la pantalla no envía el registro').toHaveLength(0);
  await foto(page, 'V-RC-01-recibir-mas-de-lo-ordenado-se-rechaza');

  // Y el servidor rechaza lo mismo si se le llama directo
  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post('/api/v1/almacen/recepciones', cuerpo(e, 120)), 422, 'RECEPCION_EXCEDE_PENDIENTE');
  expect(p.detail).toContain('Almacén recibe exacto');
  expect(sql(`select count(*) from almacen.movimientos_inventario where oc_id='${e.ocId}'`), 'no se registró ningún movimiento').toBe('0');
});

test('V-RC-02 no se recibe contra una OC sin autorizar ni contra una OC cerrada', async ({ page, guard }) => {
  await prep(guard);
  const sinAut = await ocAutorizada(guard, 'Vidrio laminado 6+6 mm con PVB', 40);
  const cerrada = await ocAutorizada(guard, 'Espejo plateado 4 mm', 30);
  sql(`update compras.ordenes_compra set estado=1, fecha_contabilizacion=null where id='${sinAut.ocId}'`); // en autorización de Jefe de Compras
  sql(`update compras.ordenes_compra set estado=4 where id='${cerrada.ocId}'`);

  // La pantalla no ofrece esas órdenes: el selector solo lista las autorizadas con pendiente
  await page.goto('/almacen/recepciones');
  await settle(page);
  await page.getByRole('button', { name: /Nueva recepci/ }).first().click();
  const dlg = page.getByRole('dialog', { name: 'Nueva recepción' });
  await dlg.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
  await page.getByPlaceholder('Folio o referencia…').fill(sinAut.folio);
  await page.waitForTimeout(1200);
  await expect(page.getByRole('option', { name: new RegExp(sinAut.folio) }), 'la OC sin autorizar no se ofrece').toHaveCount(0);
  await foto(page, 'V-RC-02-oc-sin-autorizar-no-aparece-para-recibir');

  guard.allowStatus(422);
  const a = await rechazo(await guard.api.post('/api/v1/almacen/recepciones', cuerpo(sinAut, 10)), 422, 'RECEPCION_OC_NO_AUTORIZADA');
  expect(a.detail).toContain('no se puede recibir contra ella');
  const b = await rechazo(await guard.api.post('/api/v1/almacen/recepciones', cuerpo(cerrada, 10)), 422, 'RECEPCION_OC_NO_AUTORIZADA');
  expect(b.detail).toMatch(/cerrada/i);
  expect(sql(`select count(*) from almacen.movimientos_inventario where oc_id in ('${sinAut.ocId}','${cerrada.ocId}')`)).toBe('0');
});

test('V-RC-03 cancelar una recepción exige motivo y V-RC-04 quien la registró no la cancela', async ({ page, guard }) => {
  await prep(guard);
  const e = await ocAutorizada(guard, 'Vidrio flotado bronce 6 mm', 60);
  const rec = await ok(await guard.api.post('/api/v1/almacen/recepciones', cuerpo(e, 60)));
  const recId = (rec.id ?? rec.recepcionId) as string;
  expect(recId).toBeTruthy();

  // V-RC-04: la registró esta persona. La pantalla no ofrece el botón y lo explica; el servidor también lo rechaza.
  await page.goto(`/almacen/recepciones/${recId}`);
  await settle(page);
  await expect(page.getByRole('button', { name: 'Cancelar recepción' })).toHaveCount(0);
  await expect(page.getByText('Tú registraste esta recepción: la cancela otra persona con permiso.')).toBeVisible();
  await foto(page, 'V-RC-04-quien-registro-la-recepcion-no-puede-cancelarla');
  guard.allowStatus(422, 400);
  const mismo = await rechazo(await guard.api.post(`/api/v1/almacen/recepciones/${recId}/cancelar`, { motivoCodigo: 'CANTIDAD', motivoTexto: 'Se capturó mal la cantidad' }), 422, 'RECEPCION_CANCELA_MISMO_USUARIO');
  expect(mismo.detail).toContain('no puede cancelarla');

  // V-RC-03: otra persona la registró (arrange) y al cancelar el motivo es obligatorio
  sql(`update almacen.movimientos_inventario set registrado_por='${otroUsuarioId(S.yo!)}' where oc_id='${e.ocId}'`);
  await page.reload();
  await settle(page);
  await page.getByRole('button', { name: 'Cancelar recepción' }).click();
  const dlg = page.locator('[data-component="cancelar-recepcion-dialog"]');
  await expect(dlg).toBeVisible();
  const confirmar = dlg.getByRole('button', { name: /^Cancelar recepción$|Confirmar/ }).last();
  await expect(confirmar).toBeDisabled();
  await foto(page, 'V-RC-03-cancelar-recepcion-sin-motivo-boton-bloqueado');
  const estadoAntes = sql(`select string_agg(estado::text, ',') from almacen.movimientos_inventario where oc_id='${e.ocId}'`);
  await rechazo(await guard.api.post(`/api/v1/almacen/recepciones/${recId}/cancelar`, { motivoCodigo: '', motivoTexto: '' }), [400, 422], /motivo/i);
  expect(sql(`select string_agg(estado::text, ',') from almacen.movimientos_inventario where oc_id='${e.ocId}'`), 'la recepción no cambió de estado').toBe(estadoAntes);
});

test('V-RC-05 recepción con fecha de un periodo contable cerrado se rechaza', async ({ page, guard }) => {
  await prep(guard);
  const e = await ocAutorizada(guard, 'Vidrio claro 3 mm para portafolios', 25);
  const anio = new Date().getFullYear();
  sql(`update contabilidad.periodos_contables set estado=2 where anio=${anio} and numero=1`); // enero cerrado por Contabilidad
  const dlg = await llenarRecepcion(page, e, 25, `${anio}-01-15`);
  guard.allowStatus(422);
  guard.expectApiMutationError(422);
  await dlg.getByRole('button', { name: 'Registrar recepción' }).click();
  await expect(page.getByText(/cerrado/i).first()).toBeVisible();
  await foto(page, 'V-RC-05-recepcion-en-periodo-cerrado-se-rechaza');

  const p = await rechazo(await guard.api.post('/api/v1/almacen/recepciones', cuerpo(e, 25, { fechaMovimiento: `${anio}-01-15` })), 422, 'PERIODO_CONTABLE_NO_ADMITE');
  expect(p.detail).toContain(`${anio}-01`);
  expect(sql(`select count(*) from almacen.movimientos_inventario where oc_id='${e.ocId}'`)).toBe('0');
  sql(`update contabilidad.periodos_contables set estado=1 where anio=${anio} and numero=1`);
});
