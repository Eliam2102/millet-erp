import { test, expect, apiCallOn, shot } from '../../_harness/fixtures';
import { configurarPacSandbox } from '../_pac/pac-sandbox';
import { CARTERA, CLIENTE, FACTURA, FOLIO, PENDIENTE, RAZON, seed, sql } from './_data';

test.use({ role: 'super-admin' });
test.use({ viewport: { width: 1600, height: 900 } });
test.describe.configure({ mode: 'serial' });
test.beforeAll(() => seed());

const saldoCartera = () => sql(`select total - monto_pagado - monto_nc from cuentas_por_cobrar.factura_cartera where id='${CARTERA}'`);

test('R-01 [AC4] the confirmed 4,000 deposit lands in the manual REP tray and nothing is stamped', async ({ page, guard }) => {
  await page.goto('/facturacion/repp/pendientes');
  const fila = page.getByRole('row').filter({ hasText: RAZON });
  await expect(fila).toBeVisible();
  await expect(fila).toContainText(FOLIO);
  await expect(fila).toContainText('4,000.00 MXN');
  await expect(fila).toContainText('Pendiente');
  await shot(page, '01-pago-confirmado-en-bandeja-rep');
  const api = await (await guard.api.get('/api/v1/facturacion/repp/pendientes?estado=Pendiente')).json();
  expect(api.items.map((p: any) => p.id)).toContain(PENDIENTE);
  expect(sql(`select count(*) from facturacion.recibo_pago`)).toBe('0');
  expect(saldoCartera()).toBe('10000.00');
});

test('R-02 [AC4] receivables: before any REP the invoice of 10,000 still owes 10,000', async ({ page }) => {
  await page.goto(`/cxc/cartera?clienteId=${CLIENTE}`);
  await expect(page.getByText(RAZON).first()).toBeVisible();
  await expect(page.getByText('10,000.00').first()).toBeVisible();
  await shot(page, '02-cartera-saldo-10000-sin-rep');
});

test('R-03 [AC4] emitting from the tray fails for real (no PAC in the test env): no REP, pending stays', async ({ page }) => {
  await page.goto('/facturacion/repp/pendientes');
  await page.getByRole('checkbox', { name: `Seleccionar pago de ${RAZON}` }).check();
  const res = await apiCallOn(page, () => page.getByRole('button', { name: /Emitir seleccionados \(1\)/ }).click(),
    { method: 'POST', url: /repp\/pendientes\/emitir-lote$/, status: [200, 201] });
  const r = await res.json();
  expect(r[0].emitido).toBe(false);
  await expect(page.getByRole('status').filter({ hasText: 'Resultado del lote' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: RAZON })).toContainText('Pendiente');
  await shot(page, '03-emision-falla-pago-sigue-pendiente-en-bandeja');
  expect(sql(`select count(*) from facturacion.recibo_pago`)).toBe('0');
  expect(sql(`select estado from facturacion.repp_pendiente where id='${PENDIENTE}'`)).toBe('Pendiente');
  expect(saldoCartera()).toBe('10000.00');
});

test('R-04 [AC4] receivables after the failed REP: balance still 10,000', async ({ page }) => {
  await page.goto(`/cxc/cartera?clienteId=${CLIENTE}`);
  await expect(page.getByText(RAZON).first()).toBeVisible();
  await expect(page.getByText('10,000.00').first()).toBeVisible();
  await shot(page, '04-cartera-saldo-10000-rep-fallido');
});

test('R-05 [AC4] statement of account is blocked while the REP is pending', async ({ page, guard }) => {
  guard.expectApiMutationError(422); // the 422 ESTADO_CUENTA_REPP_PENDIENTE is the expected business block
  await page.goto(`/cxc/estado-cuenta?clienteId=${CLIENTE}`);
  await expect(page.getByText('4,000.00').first()).toBeVisible();
  await shot(page, '05-estado-de-cuenta-bloqueado-por-rep-pendiente');
});

// ---- Post-stamp half. Needs the PAC sandbox (user-secrets) and PR #321 (period by UTC). Order matters: R-03..R-05 ran WITHOUT a PAC.
test('R-06 [AC4] after configuring the sandbox PAC, emitting the pending 4,000 from the tray stamps it with a UUID', async ({ page, guard }) => {
  await configurarPacSandbox(page, guard);
  // the test DB has no accounting periods: open the fiscal year like e2e/verify/86e3pc50v does (arrange, not the action under test)
  const anio = 2026;
  if (sql(`select count(*) from contabilidad.ejercicios_contables where anio=${anio}`) === '0')
    expect((await guard.api.post('/api/v1/contabilidad/periodos/ejercicios', { anio })).ok()).toBeTruthy();
  sql(`update contabilidad.periodos_contables set estado=1, abierto_por='E2E', abierto_en=now() where anio=${anio} and numero between 1 and 12 and estado=0`);
  // the REP reserves a CFDI folio (TipoDocumentoSerie.Cfdi = 2) for the issuing branch: create it like an admin would, once
  const me = await (await guard.api.get('/api/auth/me')).json();
  const mid = sql(`select id from compartido.sucursales where clave='MID'`);
  if (sql(`select count(*) from compartido.series where empresa_id='${me.currentEmpresaId}' and tipo_documento=2 and activa and deleted_at is null and (sucursal_id='${mid}' or sucursal_id is null)`) === '0') {
    const serie = await guard.api.post('/api/v1/admin/series',
      { id: crypto.randomUUID(), empresaId: me.currentEmpresaId, sucursalId: mid, tipoDocumento: 2, prefijo: 'RMID', sufijo: null, reinicioPeriodo: 0, folioInicial: 1 },
      { 'Idempotency-Key': crypto.randomUUID() });
    expect(serie.status()).toBe(201);
  }
  // the earlier failed attempt only left an error mark on the pending (no REP exists): clear it so the tray lets us retry
  sql(`update facturacion.repp_pendiente set ultimo_error_codigo=null, ultimo_error_mensaje=null, intento_recibo_pago_id=null where id='${PENDIENTE}'`);
  await page.goto('/facturacion/repp/pendientes');
  await page.getByRole('checkbox', { name: `Seleccionar pago de ${RAZON}` }).check();
  const res = await apiCallOn(page, () => page.getByRole('button', { name: /Emitir seleccionados \(1\)/ }).click(),
    { method: 'POST', url: /repp\/pendientes\/emitir-lote$/, status: [200, 201] });
  const r = await res.json();
  expect(r[0].emitido, JSON.stringify(r[0])).toBe(true);
  await page.getByRole('tab', { name: 'Emitidos' }).click();
  await expect(page.getByRole('row').filter({ hasText: RAZON })).toContainText('Emitido');
  await shot(page, '06-rep-emitido-desde-bandeja');
  const uuid = sql(`select uuid from facturacion.comprobante where tipo=4`);
  expect(uuid).toMatch(/^[0-9a-f-]{36}$/i);
});

test('R-07 [AC4] the stamped REP shows parcialidad 1 and saldo insoluto 6,000.00 for the invoice of 10,000', async ({ page }) => {
  const id = sql(`select id from facturacion.comprobante where tipo=4`);
  await page.goto(`/facturacion/repp/${id}`);
  const fila = page.getByRole('row').filter({ hasText: FOLIO });
  await expect(fila).toBeVisible();
  await expect(fila).toContainText('4000.00');
  await expect(fila).toContainText('6000.00');
  await expect(fila.getByRole('cell', { name: '1', exact: true })).toBeVisible();
  await shot(page, '07-rep-detalle-parcialidad-1-saldo-6000');
  expect(sql(`select num_parcialidad||'/'||saldo_insoluto from facturacion.recibo_pago_factura`)).toBe('1/6000.00');
});

// R-08 (cartera 6,000): cartera only changes when CxC consumes ReciboPagoTimbrado over the Outbox/Service Bus, both OFF in the
// test env. Nothing in the UI can trigger it and faking it by SQL is not allowed: reported, not asserted here.
