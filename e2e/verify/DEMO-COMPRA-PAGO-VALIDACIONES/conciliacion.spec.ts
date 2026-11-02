/**
 * Validaciones de CONCILIACIÓN BANCARIA: no cierra con diferencia (CONCILIACION_DESCUADRADA) y desemparejar exige motivo.
 * Datos: libros sembrados por SQL + extracto BBVA de septiembre 2026 generado en _conciliacion.ts.
 */
import { SA_EMAIL, warm } from '../86e3pc50v/_data';
import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { test, expect, apiCallOn, shot } from '../../_harness/fixtures';
import { CUENTAS, DATOS_C4, DATOS_C5, idConciliacion, seedAll } from './_conciliacion';
import { rechazo, foto } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(180_000);
test.beforeAll(() => seedAll());
// warm() completes the SSO sign-in if the saved session needs it and waits for the workspace, so guard.api has the page's token
test.beforeEach(async ({ page }) => { await warm(page, '/tesoreria/conciliacion', SA_EMAIL); });

const API = '/api/v1/tesoreria/conciliaciones';
const idem = () => ({ 'Idempotency-Key': randomUUID() });

async function abrirPeriodoEImportar(page: Page, ult4: string, texto: string) {
  await page.goto('/tesoreria/conciliacion');
  await expect(page.getByRole('heading', { name: 'Conciliación bancaria' })).toBeVisible();
  const combo = (i: number) => page.getByText(['Cuenta Bancaria', 'Año', 'Mes'][i], { exact: true }).first().locator('xpath=following::button[@role="combobox"][1]');
  await combo(0).click();
  await page.getByRole('option', { name: new RegExp(ult4) }).click();
  await combo(1).click();
  await page.getByRole('option', { name: '2026', exact: true }).click();
  await combo(2).click();
  await page.getByRole('option', { name: 'Septiembre' }).click();
  await expect(page.getByText('Sin conciliación para este período')).toBeVisible();
  await page.getByRole('button', { name: 'Nueva conciliación' }).click();
  await page.getByRole('button', { name: 'Abrir Conciliación' }).click();
  await expect(page.getByText('Conciliación abierta con éxito').first()).toBeVisible();
  await page.getByRole('button', { name: 'Cargar Extracto BBVA' }).click();
  const dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').last().fill(texto);
  await apiCallOn(page, () => dlg.getByRole('button', { name: 'Cargar y Procesar' }).click(), { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201] });
  await expect(page.getByText('Extracto cargado y procesado correctamente').first()).toBeVisible();
  await expect(dlg).toBeHidden();
}

const detalle = async (guard: any, id: string) => (await guard.api.get(`${API}/${id}`)).json();

test('V-CO-01 la conciliación no se cierra con diferencia', async ({ page, guard }) => {
  const c = CUENTAS.c4;
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C4.ext1.texto);
  const id = idConciliacion(c.numero);
  await expect(page.getByText('7 de 7 líneas emparejadas')).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await expect(page.getByText('⚠ Descuadre pendiente')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cerrar con Acta' })).toBeDisabled();
  await foto(page, 'V-CO-01-conciliacion-con-diferencia-no-se-puede-cerrar');
  expect((await detalle(guard, id)).diferencia).toBe(50);

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`${API}/${id}/cerrar`, { firmanteNombre: 'Marisol Treviño Garza', firmantePuesto: 'Gerente de Tesorería' }, idem()), 422, 'CONCILIACION_DESCUADRADA');
  expect(p.detail).toContain('50.00');
  expect((await detalle(guard, id)).estado, 'sigue abierta').toBe(1);
});

test('V-CO-02 desemparejar una línea exige motivo', async ({ page, guard }) => {
  const c = CUENTAS.c5;
  await abrirPeriodoEImportar(page, c.numero.slice(-4), DATOS_C5.texto);
  const id = idConciliacion(c.numero);
  await expect(page.getByText('6 de 6 líneas emparejadas')).toBeVisible();
  await page.getByRole('heading', { name: 'Extracto Bancario' }).evaluate((el) => el.scrollIntoView({ block: 'start' }));
  const objetivo = DATOS_C5.movs[2];
  const fila = page.locator('h3:has-text("Extracto Bancario")').locator('xpath=ancestor::div[contains(@class,"rounded-lg")][1]').getByRole('row').filter({ hasText: objetivo.ref! });
  await fila.getByTitle('Desemparejar (requiere motivo)').click();
  const dlg = page.getByRole('dialog');
  await expect(dlg.getByRole('button', { name: 'Desemparejar y Registrar' }), 'sin motivo el botón queda bloqueado').toBeDisabled();
  await foto(page, 'V-CO-02-desemparejar-sin-motivo-boton-bloqueado');

  const d = await detalle(guard, id);
  guard.allowStatus(400, 422);
  await rechazo(await guard.api.post(`${API}/${id}/desemparejar`, { extractoLineaId: d.lineas[0].id, motivo: ' ' }, idem()), [400, 422], /motivo/i);
  expect((await detalle(guard, id)).lineas.filter((l: any) => l.estado !== 1 && l.matchMovimientoId).length, 'las seis líneas siguen emparejadas').toBe(6);
});
