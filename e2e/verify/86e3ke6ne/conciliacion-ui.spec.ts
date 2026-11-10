/**
 * T9.2 (86e3ke6ne) bank reconciliation UI: BBVA import, matching, balanced close and signed acta.
 * Data: SQL-seeded books + generated BBVA statements in the TEST DB (see _data.ts). Serial: each test uses its own account.
 */
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { test, expect, apiCallOn, shot } from '../../_harness/fixtures';
import { CUENTAS, DATOS_C1, DATOS_C2, DATOS_C3, DATOS_C4, DATOS_C5, fmt, idConciliacion, seedAll, sql } from './_data';

test.use({ role: 'super-admin' });
test.use({ viewport: { width: 1600, height: 900 } });
test.beforeAll(() => seedAll());

const RUTA = '/tesoreria/conciliacion';
const API = '/api/v1/tesoreria/conciliaciones';
const EVID = path.resolve(process.cwd(), '.verification/86e3ke6ne/evidence/test');
const idem = () => ({ 'Idempotency-Key': randomUUID() });

async function elegirPeriodo(page: Page, ult4: string) {
  await page.goto(RUTA);
  await expect(page.getByRole('heading', { name: 'Conciliación bancaria' })).toBeVisible();
  const combos = { nth: (i: number) => page.getByText(['Cuenta Bancaria', 'Año', 'Mes'][i], { exact: true }).first().locator('xpath=following::button[@role="combobox"][1]') };
  await combos.nth(0).click();
  await page.getByRole('option', { name: new RegExp(ult4) }).click();
  await combos.nth(1).click();
  await page.getByRole('option', { name: '2026', exact: true }).click();
  await combos.nth(2).click();
  await page.getByRole('option', { name: 'Septiembre' }).click();
}

async function abrirPeriodoEImportar(page: Page, ult4: string, texto: string, opts: { saldo?: number } = {}) {
  await elegirPeriodo(page, ult4);
  await expect(page.getByText('Sin conciliación para este período')).toBeVisible();
  await page.getByRole('button', { name: 'Nueva conciliación' }).click();
  await page.getByRole('button', { name: 'Abrir Conciliación' }).click();
  await expect(page.getByText('Conciliación abierta con éxito').first()).toBeVisible();
  await importar(page, texto, opts);
}

async function importar(page: Page, texto: string, opts: { saldo?: number } = {}) {
  await page.getByRole('button', { name: 'Cargar extracto', exact: true }).click();
  const dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').last().fill(texto);
  if (opts.saldo !== undefined) await dlg.locator('input[type=number]').fill(String(opts.saldo));
  await shot(page, `${shotPrefix}-modal-cargar-extracto`.replace(/^-/, ''));
  const res = await apiCallOn(page, () => dlg.getByRole('button', { name: 'Cargar y Procesar' }).click(),
    { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201] });
  expect(JSON.parse(res.request().postData() ?? '{}').formato).toBe('BBVA');
  await expect(page.getByText('Extracto cargado y procesado correctamente').first()).toBeVisible();
  await expect(dlg).toBeHidden();
}
let shotPrefix = '';

const detalle = async (guard: any, id: string) => (await guard.api.get(`${API}/${id}`)).json();
const filaLinea = (page: Page, texto: string | RegExp) => page.locator('h3:has-text("Extracto Bancario")').locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]').getByRole('row').filter({ hasText: texto });
async function verTablas(page: Page) {
  await page.getByRole('heading', { name: 'Extracto Bancario' }).evaluate((el) => el.scrollIntoView({ block: 'start' }));
  await page.waitForTimeout(300);
}
async function scrollTablaLineas(page: Page, y: number) {
  await page.getByRole('heading', { name: 'Extracto Bancario' }).locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]')
    .locator('.overflow-y-auto').first().evaluate((el, top) => { el.scrollTop = top; }, y);
  await page.waitForTimeout(250);
}

test('CA9.5 @evidence importing the September BBVA statement twice does not duplicate lines', async ({ page, guard }) => {
  const c = CUENTAS.ca95;
  shotPrefix = '01';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C1.texto);
  await verTablas(page);
  await expect(page.getByText('Líneas reportadas por el banco (50)')).toBeVisible();
  const id = idConciliacion(c.numero);
  const lineasPrimera = sql(`select count(*) from tesoreria.extracto_linea where conciliacion_id='${id}'`);
  const movsPrimera = sql(`select count(*) from tesoreria.movimiento_bancario where cuenta_bancaria_id='${`00000009-0b0b-4000-8000-${String(c.n).padStart(12, '0')}`}'`);
  expect(lineasPrimera).toBe('50');
  await shot(page, '01-extracto-bbva-septiembre-importado-50-lineas');
  await scrollTablaLineas(page, 0);

  // second import of the very same file
  shotPrefix = '02';
  await importar(page, DATOS_C1.texto);
  await verTablas(page);
  await expect(page.getByText('Líneas reportadas por el banco (50)')).toBeVisible();
  await shot(page, '02-reimportar-sin-duplicar-sigue-en-50-lineas');
  expect(sql(`select count(*) from tesoreria.extracto_linea where conciliacion_id='${id}'`)).toBe('50');
  expect(sql(`select count(distinct linea_hash) from tesoreria.extracto_linea where conciliacion_id='${id}'`)).toBe('50');
  expect(sql(`select count(*) from tesoreria.movimiento_bancario where cuenta_bancaria_id='00000009-0b0b-4000-8000-${String(c.n).padStart(12, '0')}'`)).toBe(movsPrimera);
  const d = await detalle(guard, id);
  expect(d.lineas).toHaveLength(50);
  expect(d.lineas.filter((l: any) => l.estado === 2)).toHaveLength(46); // matches untouched by the second import
});

test('C1 @evidence 50 lines, 46 with exact reference: auto-match leaves 46 matched and 4 pending', async ({ page, guard }) => {
  const c = CUENTAS.c1;
  shotPrefix = '03';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C1.texto);
  const id = idConciliacion(c.numero);
  await expect(page.getByText('46 de 50 líneas emparejadas')).toBeVisible();
  await verTablas(page);
  await shot(page, '03a-conciliacion-46-de-50-emparejadas-primeras-lineas');
  await scrollTablaLineas(page, 700);
  await shot(page, '03b-lineas-del-extracto-a-mitad-de-la-lista');
  await scrollTablaLineas(page, 99999);
  await shot(page, '03c-ultimas-lineas-del-extracto');

  // filter pending: exactly the 4 bank-only lines
  await page.getByRole('combobox').filter({ hasText: /Todos \(50\)/ }).click();
  await page.getByRole('option', { name: 'Pendientes (4)' }).click();
  await scrollTablaLineas(page, 0);
  for (const t of ['COMISION MANEJO DE CUENTA', 'IVA COMISION MANEJO DE CUENTA', 'INTERESES GANADOS DEL PERIODO', 'DEPOSITO EN EFECTIVO VENTANILLA'])
    await expect(filaLinea(page, t).first()).toBeVisible();
  await shot(page, '04-filtro-pendientes-4-lineas-solo-del-banco');

  const d = await detalle(guard, id);
  const por = (e: number) => d.lineas.filter((l: any) => l.estado === e).length;
  expect([por(2), por(1), por(3)]).toEqual([46, 4, 0]);
  expect(d.lineas.filter((l: any) => l.estado === 2).every((l: any) => l.matchTipo === 1)).toBe(true); // all by reference + amount
  expect(sql(`select count(*) from tesoreria.movimiento_bancario where cuenta_bancaria_id='00000009-0b0b-4000-8000-${String(c.n).padStart(12, '0')}' and estado_conciliacion=2`)).toBe('46');
});

test('C2 @evidence a $1,000 line with two possible $1,000 movements is not auto-matched: goes to review with both candidates', async ({ page, guard }) => {
  const c = CUENTAS.c2;
  shotPrefix = '05';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C2.texto);
  const id = idConciliacion(c.numero);
  await verTablas(page);
  const fila = filaLinea(page, 'DEPOSITO SPEI CLIENTE');
  await expect(fila.getByText('En revisión')).toBeVisible();
  await expect(fila.getByText('2 candidatos posibles')).toBeVisible();
  await shot(page, '05-linea-1000-en-revision-con-2-candidatos');

  const d = await detalle(guard, id);
  const linea = d.lineas.find((l: any) => l.descripcion === 'DEPOSITO SPEI CLIENTE');
  expect(linea.estado).toBe(3);
  expect(linea.matchMovimientoId).toBeNull();
  const m1000 = d.movimientosPeriodo.filter((m: any) => m.monto === 1000);
  expect(m1000).toHaveLength(2);
  expect([...linea.candidatosRevisionIds].sort()).toEqual(m1000.map((m: any) => m.id).sort());
  expect(m1000.every((m: any) => m.estadoConciliacion === 1)).toBe(true); // neither movement was consumed

  // reviewer picks the line: both $1,000 candidates are offered on the books side
  await fila.getByTitle('Seleccionar para matching manual').click();
  await expect(page.getByText('Modo emparejamiento activo')).toBeVisible();
  const libros = page.getByRole('heading', { name: 'Movimientos del Libro Auxiliar' }).locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]');
  await expect(libros.getByRole('row').filter({ hasText: 'SPEI260915710412' })).toBeVisible();
  await expect(libros.getByRole('row').filter({ hasText: 'SPEI260915710877' })).toBeVisible();
  await libros.getByRole('row').filter({ hasText: 'SPEI260915710412' }).getByRole('checkbox').click();
  await libros.evaluate((el) => el.scrollIntoView({ block: 'start' }));
  await page.waitForTimeout(300);
  await shot(page, '06-revision-manual-ambos-candidatos-1000-uno-elegido');
  await apiCallOn(page, () => page.getByRole('button', { name: 'Emparejar Manual' }).click(),
    { method: 'POST', url: /emparejar-manual$/, status: [200, 201] });
  await expect(page.getByText('Emparejamiento manual realizado correctamente').first()).toBeVisible();
  await expect(filaLinea(page, 'DEPOSITO SPEI CLIENTE').getByText('Emparejada')).toBeVisible();
  await expect(libros.getByRole('row').filter({ hasText: 'SPEI260915710877' }).getByText('Pendiente')).toBeVisible();
  await shot(page, '07-linea-1000-emparejada-a-mano-el-otro-1000-queda-pendiente');
});

test('C3 / CA9.8 @evidence bank fee $350 + IVA not in the system: created from the line with its concept and matched', async ({ page, guard }) => {
  const c = CUENTAS.c3;
  shotPrefix = '08';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C3.texto);
  const id = idConciliacion(c.numero);
  await verTablas(page);
  const fila = filaLinea(page, 'COMISION POR MANEJO DE CUENTA');
  await expect(fila.getByText('Pendiente')).toBeVisible();
  await expect(page.getByText('5 de 6 líneas emparejadas')).toBeVisible();
  await shot(page, '09-linea-comision-406-pendiente-no-esta-en-libros');
  const antes = await detalle(guard, id);
  expect(antes.diferencia).toBe(-406);

  await fila.getByTitle('Alta asistida (comisión / interés / depósito)').click();
  const dlg = page.getByRole('dialog');
  await expect(dlg.getByText('Alta asistida desde extracto')).toBeVisible();
  await expect(dlg.getByText('Comisión bancaria')).toBeVisible(); // concept suggested from the line
  const nums = dlg.locator('input[type=number]');
  await expect(nums.nth(0)).toHaveValue('350.00');
  await expect(nums.nth(1)).toHaveValue('56.00');
  await shot(page, '10-alta-asistida-comision-350-mas-iva-56-concepto-sugerido');
  const res = await apiCallOn(page, () => dlg.getByRole('button', { name: 'Crear Movimiento y Emparejar' }).click(),
    { method: 'POST', url: /alta-asistida$/, status: [200, 201] });
  const body = JSON.parse(res.request().postData() ?? '{}');
  expect(body).toMatchObject({ incluyeIva: true, montoBase: 350, montoIva: 56 });
  await expect(page.getByText('Movimiento registrado y emparejado con éxito').first()).toBeVisible();
  await expect(dlg).toBeHidden();

  await expect(filaLinea(page, 'COMISION POR MANEJO DE CUENTA').getByText('Emparejada')).toBeVisible();
  await expect(page.getByText('6 de 6 líneas emparejadas')).toBeVisible();
  const libros = page.getByRole('heading', { name: 'Movimientos del Libro Auxiliar' }).locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]');
  await libros.evaluate((el) => el.scrollIntoView({ block: 'start' }));
  await page.waitForTimeout(300);
  await expect(libros.getByRole('row').filter({ hasText: 'Comisión bancaria' })).toContainText('350.00');
  await expect(libros.getByRole('row').filter({ hasText: 'Impuestos' })).toContainText('56.00');
  await shot(page, '11-comision-y-iva-creados-en-libros-con-su-concepto-y-emparejados');
  await page.getByRole('tab', { name: /Bitácora de Auditoría/ }).click();
  await expect(page.getByRole('cell', { name: /Alta asistida comisión \+ IVA/ })).toBeVisible();
  await shot(page, '12-bitacora-alta-asistida-comision-mas-iva');

  const d = await detalle(guard, id);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.getByRole('tab', { name: /Emparejamiento Extracto/ }).click();
  await page.locator('main, body').first().evaluate(() => document.querySelector('.overflow-y-auto')?.scrollTo(0, 0));
  await shot(page, d.diferencia === 0 ? '13-diferencia-0-tras-alta-asistida' : '13-ERROR-diferencia-sigue-en-menos-406-tras-alta-asistida');
  expect.soft(d.diferencia, 'saldo libros must include the movements created by alta asistida').toBe(0);
  const movs = sql(`select c.nombre || ':' || m.monto || ':' || m.estado_conciliacion from tesoreria.movimiento_bancario m join tesoreria.concepto_movimiento c on c.id=m.concepto_id where m.cuenta_bancaria_id='00000009-0b0b-4000-8000-${String(c.n).padStart(12, '0')}' and m.monto in (350, 56) order by m.monto desc`);
  expect(movs.split('\n')).toEqual(['Comisión bancaria:350.00:2', 'Impuestos:56.00:2']);
});

test('C4 / CA9.9 @evidence $50 difference cannot close; with difference 0 it closes and the signed acta is generated', async ({ page, guard }) => {
  const c = CUENTAS.c4;
  shotPrefix = '13';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C4.ext1.texto);
  const id = idConciliacion(c.numero);
  await expect(page.getByText('7 de 7 líneas emparejadas')).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await expect(page.getByText('⚠ Descuadre pendiente')).toBeVisible();
  const cerrar = page.getByRole('button', { name: 'Cerrar con Acta' });
  await expect(cerrar).toBeDisabled();
  await shot(page, '14-diferencia-50-descuadre-boton-cerrar-bloqueado');
  const d1 = await detalle(guard, id);
  expect(d1.diferencia).toBe(50);

  // the backend refuses as well (not only the disabled button)
  const rechazo = await guard.api.post(`${API}/${id}/cerrar`, { firmanteNombre: 'Marisol Treviño Garza', firmantePuesto: 'Gerente de Tesorería' }, idem());
  expect(rechazo.status()).toBeGreaterThanOrEqual(400);
  expect(rechazo.status()).toBeLessThan(500);
  expect(await rechazo.text()).toMatch(/CONCILIACION_DESCUADRADA|50\.00/);
  expect((await detalle(guard, id)).estado).toBe(1);

  // supplement statement with the cheque the bank finally paid -> difference 0
  shotPrefix = '15';
  await importar(page, DATOS_C4.ext2.texto);
  await expect(page.getByText('8 de 8 líneas emparejadas')).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await expect(page.getByText('✓ Conciliación cuadrada ($0.00)')).toBeVisible();
  await expect(cerrar).toBeEnabled();
  await shot(page, '16-diferencia-0-conciliacion-cuadrada-cerrar-habilitado');
  expect((await detalle(guard, id)).diferencia).toBe(0);

  // only Gerente or Jefe de Tesorería may sign
  const noFirma = await guard.api.post(`${API}/${id}/cerrar`, { firmanteNombre: 'Ana Lucía Ortega', firmantePuesto: 'Auxiliar contable' }, idem());
  expect(noFirma.status()).toBeGreaterThanOrEqual(400);
  expect(await noFirma.text()).toMatch(/CONCILIACION_FIRMANTE_ROL_INVALIDO|Gerente o Jefe/);

  await cerrar.click();
  const dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('Marisol Treviño Garza');
  await dlg.getByRole('combobox').click();
  await page.getByRole('option', { name: 'Jefe de Tesorería' }).click();
  await shot(page, '17-modal-cierre-firmante-jefe-de-tesoreria');
  const [descarga, res] = await Promise.all([
    page.waitForEvent('download'),
    apiCallOn(page, () => dlg.getByRole('button', { name: 'Firmar, Cerrar y Descargar Acta' }).click(),
      { method: 'POST', url: /\/cerrar$/, status: 200 }),
  ]);
  expect(JSON.parse(res.request().postData() ?? '{}')).toEqual({ firmanteNombre: 'Marisol Treviño Garza', firmantePuesto: 'Jefe de Tesorería' });
  fs.mkdirSync(`${EVID}/screens`, { recursive: true });
  const pdfPath = `${EVID}/screens/acta-conciliacion-bbva-septiembre-2026.pdf`;
  await descarga.saveAs(pdfPath);
  const pdf = fs.readFileSync(pdfPath);
  expect(pdf.subarray(0, 4).toString()).toBe('%PDF');
  expect(pdf.length).toBeGreaterThan(1500);
  await expect(page.getByText('Conciliación cerrada con éxito').first()).toBeVisible();
  await expect(dlg).toBeHidden();
  await page.waitForTimeout(500);
  await expect(page.getByText('Cerrada por: Marisol Treviño Garza')).toBeVisible();
  await expect(page.locator('p:text-is("Jefe de Tesorería")')).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await shot(page, '18-conciliacion-cerrada-firmada-por-jefe-de-tesoreria-acta-pdf');

  const d = await detalle(guard, id);
  expect(d).toMatchObject({ estado: 2, firmanteNombre: 'Marisol Treviño Garza', firmantePuesto: 'Jefe de Tesorería', diferencia: 0 });
  expect(d.actaGeneradaEn).toBeTruthy();
  const acta = await guard.api.get(`${API}/${id}/acta`);
  expect(acta.status()).toBe(200);
  expect(acta.headers()['content-type']).toContain('application/pdf');
  // once closed, the toolbar to import / match is gone
  await expect(page.getByRole('button', { name: 'Cargar extracto', exact: true })).toHaveCount(0);
  expect(fmt(50)).toBe('50.00');
});

test('C5 / T9.2-a @evidence wrongly matched line: unmatch with reason goes back to pending and the log keeps who and why', async ({ page, guard }) => {
  const c = CUENTAS.c5;
  shotPrefix = '19';
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C5.texto);
  const id = idConciliacion(c.numero);
  await expect(page.getByText('6 de 6 líneas emparejadas')).toBeVisible();
  await verTablas(page);
  const objetivo = DATOS_C5.movs[2];
  const fila = filaLinea(page, objetivo.ref!);
  await expect(fila.getByText('Emparejada')).toBeVisible();
  await shot(page, '20-seis-lineas-emparejadas-una-sera-desemparejada');

  const motivo = 'Emparejada por coincidencia de referencia, pero el pago corresponde a otra factura del proveedor';
  await fila.getByTitle('Desemparejar (requiere motivo)').click();
  const dlg = page.getByRole('dialog');
  await expect(dlg.getByRole('button', { name: 'Desemparejar y Registrar' })).toBeDisabled(); // motivo is mandatory
  await dlg.getByRole('textbox').fill(motivo);
  await shot(page, '21-modal-desemparejar-con-motivo-capturado');
  const res = await apiCallOn(page, () => dlg.getByRole('button', { name: 'Desemparejar y Registrar' }).click(),
    { method: 'POST', url: /desemparejar$/, status: [200, 201] });
  expect(JSON.parse(res.request().postData() ?? '{}').motivo).toBe(motivo);
  await expect(page.getByText('Línea desemparejada y registrada en bitácora de auditoría').first()).toBeVisible();
  await expect(page.getByText('5 de 6 líneas emparejadas')).toBeVisible();
  await expect(filaLinea(page, objetivo.ref!).getByText('Pendiente')).toBeVisible();
  const libros = page.getByRole('heading', { name: 'Movimientos del Libro Auxiliar' }).locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]');
  await expect(libros.getByRole('row').filter({ hasText: objetivo.ref! }).getByText('Pendiente')).toBeVisible();
  await verTablas(page);
  await shot(page, '22-linea-de-regreso-a-pendiente-y-su-movimiento-liberado');

  await page.getByRole('tab', { name: /Bitácora de Auditoría \(1\)/ }).click();
  const filaBit = page.getByRole('row').filter({ hasText: 'Desemparejamiento' });
  await expect(filaBit).toContainText(motivo);
  await shot(page, '23-bitacora-desemparejamiento-con-motivo-y-usuario');

  const d = await detalle(guard, id);
  const linea = d.lineas.find((l: any) => l.referencia === objetivo.ref);
  expect(linea).toMatchObject({ estado: 1, matchMovimientoId: null });
  expect(d.bitacora).toHaveLength(1);
  expect(d.bitacora[0]).toMatchObject({ accion: 'Desemparejamiento', motivo, extractoLineaId: linea.id });
  expect(d.bitacora[0].usuarioId).toMatch(/^[0-9a-f-]{36}$/);
  expect(d.bitacora[0].usuarioId).not.toBe('00000000-0000-0000-0000-000000000000');
  // the API also rejects an unmatch without reason
  guard.allowStatus(400, 422);
  const sinMotivo = await guard.api.post(`${API}/${id}/desemparejar`, { extractoLineaId: d.lineas[0].id, motivo: ' ' }, idem());
  expect([400, 422]).toContain(sinMotivo.status());
});
