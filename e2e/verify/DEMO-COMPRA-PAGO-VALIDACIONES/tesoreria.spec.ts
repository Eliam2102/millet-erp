/**
 * Validaciones de TESORERÍA: corridas de pago, layout bancario, proveedor bloqueado / sin opinión de cumplimiento y pago repetido.
 * La corrida se arma con la sesión de super-admin (única vigente): por eso «quien armó no autoriza» se prueba con la misma persona,
 * y para los casos que necesitan una segunda firma la corrida se registra a nombre de otra persona (arrange por SQL en BD de prueba).
 */
import type { Page } from '@playwright/test';
import { test, expect, shot, type Guard } from '../../_harness/fixtures';
import { ok, otroUsuarioId, rechazo, sql, today, uuid, foto } from './_data';
import { BANCO_MILLET, CUENTA_MILLET, PROVS, prov, seed, totalDe } from './_tesoreria';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(180_000);
test.beforeEach(async ({ page }) => { await page.goto('/tesoreria/corridas'); await page.waitForLoadState('networkidle'); }); // el token de la API se captura de la primera llamada de la página
test.beforeAll(() => seed());

const S: { yo?: string } = {};
const API = '/api/v1/tesoreria';
const settle = async (p: Page) => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(700); };
const toast = (p: Page, re: RegExp) => p.locator('[data-sonner-toast]').filter({ hasText: re }).first();
const ver = (c: { version: number }) => ({ 'X-Expected-Version': String(c.version), 'Idempotency-Key': uuid() });

async function crearCorrida(guard: Guard, facturas: string[], nombre: string) {
  return ok(await guard.api.post(`${API}/corridas`, { cuentaBancariaId: CUENTA_MILLET, facturas, nombre })) as Promise<{ id: string; version: number; estado: string }>;
}
const corrida = async (guard: Guard, id: string) => ok(await guard.api.get(`${API}/corridas/${id}`)) as Promise<{ id: string; version: number; estado: string | number }>;
const factura = (n: number) => `00000019-0004-4000-8000-${String(700 + n).padStart(12, '0')}`;

async function prep(guard: Guard) {
  if (S.yo) return;
  S.yo = (await ok(await guard.api.get('/api/auth/me'))).userId;
}

test('V-TS-01 quien armó la corrida no la autoriza', async ({ page, guard }) => {
  await prep(guard);
  const c = await crearCorrida(guard, [factura(1)], 'Pago a proveedores 04-nov');
  const env = await ok(await guard.api.post(`${API}/corridas/${c.id}/enviar-autorizacion`, undefined, ver(c)));
  await page.goto(`/tesoreria/corridas/${c.id}`);
  await settle(page);
  await expect(page.getByRole('button', { name: 'Firmar y autorizar' })).toBeDisabled();
  await expect(page.getByText('Quien armó la corrida no puede autorizarla; pide la firma al Gerente o al Jefe de Tesorería')).toBeVisible();
  await foto(page, 'V-TS-01-quien-armo-la-corrida-no-puede-autorizarla');

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`${API}/corridas/${c.id}/autorizar`, undefined, ver(env)), 422, 'CORRIDA_MISMO_USUARIO');
  expect(p.detail).toContain('Quien armó la corrida no puede autorizarla');
  expect((await corrida(guard, c.id)).estado, 'sigue En autorización').toMatch(/EnAutorizacion|2/);
});

test('V-TS-02 corrida en borrador no genera el archivo de layout', async ({ page, guard }) => {
  await prep(guard);
  const c = await crearCorrida(guard, [factura(5)], 'Compra de gases 09-nov');
  await page.goto(`/tesoreria/corridas/${c.id}`);
  await settle(page);
  await expect(page.getByText('Borrador', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Generar archivo' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Descargar oficio' })).toHaveCount(0);
  await foto(page, 'V-TS-02-corrida-en-borrador-sin-boton-generar-archivo');

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`${API}/corridas/${c.id}/layout?banco=BBVA&tipoPago=NacionalSpei`), 422, 'CORRIDA_LAYOUT_NO_AUTORIZADA');
  expect(p.detail).toContain('solo se genera para corridas autorizadas');
});

test('V-TS-03 CLABE inválida: el pago queda fuera del archivo y se avisa con la causa', async ({ page, guard }) => {
  await prep(guard);
  const mala = prov(3);
  const buena = prov(6);
  const c = await crearCorrida(guard, [factura(3), factura(6)], 'Pago urgente materiales 06-nov');
  sql(`update tesoreria.corrida_pago set solicitada_por = '${otroUsuarioId(S.yo!)}' where id = '${c.id}'`);
  const env = await ok(await guard.api.post(`${API}/corridas/${c.id}/enviar-autorizacion`, undefined, ver(await corrida(guard, c.id))));
  await ok(await guard.api.post(`${API}/corridas/${c.id}/autorizar`, undefined, ver(env)));
  await page.goto(`/tesoreria/corridas/${c.id}`);
  await settle(page);
  await expect(page.getByText('Autorizada', { exact: true }).first()).toBeVisible();
  const dl = page.waitForEvent('download');
  const res = page.waitForResponse((r) => /\/corridas\/[0-9a-f-]+\/layout\?/.test(r.url()) && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Generar archivo' }).click();
  const r = await (await res).json();
  await dl;
  await expect(page.getByText('Archivo generado con 1 líneas')).toBeVisible();
  await expect(page.getByText(/1 pasivos quedaron fuera por inconsistencias bancarias/)).toBeVisible();
  await foto(page, 'V-TS-03-clabe-invalida-queda-fuera-del-archivo');

  expect(r.lineasValidas).toBe(1);
  expect(r.lineasExcluidas).toBe(1);
  expect(r.excepciones[0]).toMatchObject({ proveedorRazonSocial: mala.razon, folioProveedor: mala.folio, causa: 'La CLABE no pasa la validación del dígito verificador.' });
  expect(r.contenido).not.toContain(mala.clabe);
  expect(r.contenido).toContain(buena.clabe);
});

test('V-TS-04 proveedor bloqueado: no se arma corrida ni se registra pago', async ({ page, guard }) => {
  await prep(guard);
  const b = prov(4);
  await page.goto('/tesoreria/corridas/nueva');
  await settle(page);
  await page.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
  await page.getByRole('option', { name: new RegExp(BANCO_MILLET) }).click();
  // la pantalla lo muestra deshabilitado con el motivo (antes dejaba elegirlo y el servidor rechazaba la corrida)
  await expect(page.getByRole('checkbox', { name: `Elegir todas las facturas de ${b.razon}` }), 'la pantalla no deja elegirlo').toBeDisabled();
  await expect(page.getByText('Proveedor bloqueado: Opinión de cumplimiento negativa').first()).toBeVisible();
  await foto(page, 'V-TS-04-proveedor-bloqueado-deshabilitado-en-nueva-corrida');
  guard.allowStatus(422);
  await page.goto('/tesoreria/pagos');
  await settle(page);
  await expect(page.getByText('Proveedor bloqueado')).toBeVisible();
  await foto(page, 'V-TS-04b-pagos-marca-al-proveedor-bloqueado');

  const c = await rechazo(await guard.api.post(`${API}/corridas`, { cuentaBancariaId: CUENTA_MILLET, facturas: [factura(4)], nombre: 'Pago a abrasivos' }), 422, 'PROVEEDOR_BLOQUEADO');
  expect(c.detail).toContain('no admite pagos');
  const pago = await rechazo(await guard.api.post(`${API}/pagos`, {
    cuentaBancariaId: CUENTA_MILLET, fechaValor: today(), referenciaBancaria: 'SPEI-ABRASIVOS',
    aplicaciones: [{ facturaProveedorId: factura(4), importe: totalDe(b) }],
  }, { 'Idempotency-Key': uuid() }), 422, 'PROVEEDOR_BLOQUEADO');
  expect(pago.detail).toContain(b.razon);
  expect(sql(`select count(*) from tesoreria.aplicacion_pago_proveedor where proveedor_id='${b.id}'`), 'no se registró pago').toBe('0');
  expect(sql(`select count(*) from tesoreria.corrida_pago_linea l join tesoreria.corrida_pago c on c.id=l.corrida_id where l.factura_proveedor_id='${factura(4)}'`), 'no se armó corrida').toBe('0');
});

test('V-TS-05 proveedor sin opinión de cumplimiento: sus facturas quedan detenidas por documentación', async ({ page, guard }) => {
  await prep(guard);
  const s = prov(2);
  await page.goto('/tesoreria/corridas/nueva');
  await settle(page);
  await page.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
  await page.getByRole('option', { name: new RegExp(BANCO_MILLET) }).click();
  await expect(page.getByText('Detenida por documentación: falta su opinión de cumplimiento vigente')).toBeVisible();
  await expect(page.getByRole('checkbox', { name: `Elegir todas las facturas de ${s.razon}` }), 'la pantalla no deja elegirla').toBeDisabled();
  await foto(page, 'V-TS-05-proveedor-sin-opinion-de-cumplimiento-factura-detenida');

  guard.allowStatus(422);
  const c = await rechazo(await guard.api.post(`${API}/corridas`, { cuentaBancariaId: CUENTA_MILLET, facturas: [factura(2)], nombre: 'Pago a selladores' }), 422, 'CORRIDA_PASIVO_DETENIDO_DOCUMENTACION');
  expect(c.detail).toContain('falta su opinión de cumplimiento vigente');
});

test('V-TS-06 el mismo pago registrado dos veces no se duplica', async ({ page, guard }) => {
  await prep(guard);
  const h = prov(7);
  const importe = totalDe(h);
  await page.goto('/tesoreria/pagos');
  await settle(page);
  await page.getByRole('checkbox', { name: `Seleccionar ${h.folio}` }).check();
  await page.getByRole('button', { name: /Registrar pago \(1\)/ }).click();
  const sheet = page.getByRole('dialog', { name: 'Registrar pago a proveedor' });
  await expect(sheet).toBeVisible();
  await sheet.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
  await page.getByRole('option', { name: new RegExp(BANCO_MILLET) }).first().click();
  await sheet.getByLabel(`Importe para ${h.folio}`).fill(String(importe));
  await sheet.getByLabel('Referencia bancaria').fill('SPEI2611041788');
  const post = page.waitForResponse((r) => r.url().endsWith('/api/v1/tesoreria/pagos') && r.request().method() === 'POST');
  await sheet.getByRole('button', { name: 'Registrar pago' }).click();
  const primero = await post;
  expect(primero.status()).toBe(201);
  await expect(page.getByText('Pago registrado')).toBeVisible();
  const body = JSON.parse(primero.request().postData() ?? '{}');

  // Segundo intento con el mismo contenido (y una clave de idempotencia nueva: el rechazo es la regla de negocio, no un reintento)
  guard.allowStatus(422);
  const segundo = await rechazo(await guard.api.post(`${API}/pagos`, body, { 'Idempotency-Key': uuid() }), 422, /PAGO_|PASIVO_/);
  expect(segundo.detail).toBeTruthy();
  console.log(`[V-TS-06] segundo pago: ${segundo.code} - ${segundo.detail}`);
  expect(sql(`select count(*) || '|' || sum(importe_aplicado) from tesoreria.aplicacion_pago_proveedor where factura_proveedor_id='${`00000019-0004-4000-8000-${String(707).padStart(12, '0')}`}' and not revertida`), 'un solo pago aplicado').toBe(`1|${importe.toFixed(2)}`);
  await page.goto('/tesoreria/pagos');
  await settle(page);
  await expect(page.getByRole('checkbox', { name: `Seleccionar ${h.folio}` }), 'la factura pagada ya no se puede seleccionar').toHaveCount(0);
  await foto(page, 'V-TS-06-pago-repetido-se-rechaza-y-la-factura-ya-no-aparece');
  void PROVS;
});
