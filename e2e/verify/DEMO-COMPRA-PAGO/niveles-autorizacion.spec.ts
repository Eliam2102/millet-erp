/**
 * DEMO-COMPRA-PAGO, approval levels of the demo script:
 *   DEMO-00 (paso 0)  /compras/configuracion shows the "Umbrales de aprobación" table with the General (empresa) row and the
 *                     Compras row (N1 1,000 / N2 10,000) and, per row, the "Modo de nivel 2" selector at its default «Solo por
 *                     disponibilidad» (ficha C3.1: the mode is per department; VILO decision 18-oct). Thresholds are arranged through
 *                     the API, the assertion is on screen.
 *   DEMO-05 (6b/6c)   An OC above the N2 threshold: B signs N1 by UI, it moves to the "Nivel 2 (Dirección)" tab flagged "Falta N2",
 *                     B cannot sign N2 (same person), and C (almacen-corporativo, a different real user) signs N2 by UI -> Autorizada
 *                     with two distinct signers.
 * No sql(): both run in test and in dev (disposable copy).
 */
import { apiCallOn, expect, expectNoPlaceholders, reloadAndExpect, test } from '../../_harness/fixtures';
import { asRole, CORP_EMAIL, crearArticulo, DEPTO_COMPRAS, ok, OPER_EMAIL, proveedorSeed, rand, SA_EMAIL, warm } from '../86e3pc50v/_data';
import { A_PERMS, abrirPeriodoActual, captureBearer, C_PERMS, conceder, foto, lista, ocSinRqTransmitida, umbralesGuion } from './_demo';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(600_000);

test('DEMO-00 [paso 0] @evidence configuration screen: Modo de nivel 2 and thresholds table (General + Compras)', async ({ page, guard }) => {
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  await umbralesGuion(guard);
  const deptos = lista(await ok(await guard.api.get('/api/v1/catalogos/departamentos?limit=200')));
  const compras: string = deptos.find((d: { id: string }) => d.id === DEPTO_COMPRAS)?.nombre;
  expect(compras, 'department Compras must exist in the catalog').toBeTruthy();

  await page.goto('/compras/configuracion');
  await page.waitForLoadState('networkidle');
  await expect(page.getByRole('heading', { name: 'Umbrales de aprobación' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Modo de nivel 2' })).toBeVisible();
  const valor = (etiqueta: string) => page.getByLabel(etiqueta);
  for (const [fila, n1, n2] of [['General (empresa)', 1000, 10000], [compras, 1000, 10000]] as const) {
    await expect(page.getByRole('row', { name: new RegExp(fila.replace(/[()]/g, '\\$&')) }).first(), `row "${fila}" must be listed`).toBeVisible();
    expect(Number((await valor(`Umbral N1 de ${fila}`).inputValue()).replace(/[^0-9.]/g, ''))).toBe(n1);
    expect(Number((await valor(`Umbral N2 de ${fila}`).inputValue()).replace(/[^0-9.]/g, ''))).toBe(n2);
    await expect(page.getByRole('combobox', { name: `Modo de nivel 2 de ${fila}` }), `row "${fila}" must show the default mode`)
      .toHaveText('Solo por disponibilidad');
  }
  await expectNoPlaceholders(page);
  await foto(page, '00-configuracion-modo-y-umbrales');
  await reloadAndExpect(page, async () => {
    await expect(page.getByLabel(`Umbral N2 de ${compras}`)).toHaveValue(/10[,.]?000/);
  });
});

test('DEMO-05 [6b][6c] @evidence OC above N2: B signs N1, it waits in the Nivel 2 tab ("Falta N2"), C signs N2 -> Autorizada with two signers', async ({ page, guard, cleanup, browser }) => {
  guard.expectApiMutationError(422); // B trying to sign level 2 after having signed level 1
  const bearerB = captureBearer(page);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await asRole(browser, 'almacen-operativo', OPER_EMAIL, '/compras/requisiciones');
  cleanup.add(async () => { await A.context.close(); });
  const C = await asRole(browser, 'almacen-corporativo', CORP_EMAIL, '/compras/ordenes/pendientes-autorizacion');
  cleanup.add(async () => { await C.context.close(); });
  await conceder(guard, cleanup, OPER_EMAIL, A_PERMS);
  await conceder(guard, cleanup, CORP_EMAIL, C_PERMS);
  await umbralesGuion(guard);
  await abrirPeriodoActual(page, bearerB);
  const prov = await proveedorSeed(guard);
  const art = await crearArticulo(guard, { clave: `INS-${rand()}` });
  const oc = await ocSinRqTransmitida({ guard, page, A, bearerB, provId: prov.id, articuloId: art.id, cantidad: 1, precio: 12000, motivo: 'Compra de equipo mayor' });
  const ocId = oc.id;
  const det0 = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
  const folio: string = det0.folio;
  expect(det0.estado).toBe(1);

  await test.step('6b. B signs N1 from the OC detail; the OC moves to Dirección', async () => {
    await page.goto(`/compras/ordenes/${ocId}`);
    await page.waitForLoadState('networkidle');
    await page.getByRole('button', { name: 'Aprobar Nivel 1' }).click();
    const res = await apiCallOn(page, () => page.locator('[role=dialog],[role=alertdialog]').getByRole('button', { name: 'Aprobar Nivel 1' }).click(),
      { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(1);
    await expect(page.getByText(`OC ${folio} aprobada Nivel 1.`)).toBeVisible();
    await foto(page, '06b-oc-grande-n1-firmada');
    expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`))).estado, 'N1 alone must NOT authorize an OC above N2').toBe(2);

    await page.goto('/compras/ordenes/pendientes-autorizacion');
    await page.waitForLoadState('networkidle');
    await page.getByRole('tab', { name: /Nivel 2 \(Dirección\)/ }).click();
    const fila = page.locator('tr', { hasText: folio });
    await expect(fila, 'the OC must wait in the Nivel 2 tab').toBeVisible();
    await expect(fila.getByText('Falta N2')).toBeVisible();
    await foto(page, '06b-pendientes-nivel-2-falta-n2');
    // the same person cannot sign the second level
    const mismo = await guard.api.post(`/api/v1/compras/ordenes/${ocId}/autorizaciones`, { nivel: 2 });
    expect(mismo.status(), 'B signing N2 after N1 must be refused (OC_AUTOAUTORIZACION / OC_FIRMA_MISMA_PERSONA)').toBe(422);
  });

  await test.step('6c. C (another real user) signs N2 by UI', async () => {
    const pc = C.page;
    await pc.goto('/compras/ordenes/pendientes-autorizacion');
    await pc.waitForLoadState('networkidle');
    await pc.getByRole('tab', { name: /Nivel 2 \(Dirección\)/ }).click();
    await expect(pc.locator('tr', { hasText: folio }).getByText('Falta N2')).toBeVisible();
    await foto(pc, '06c-pendientes-nivel-2-vista-de-direccion');
    await pc.goto(`/compras/ordenes/${ocId}`);
    await pc.waitForLoadState('networkidle');
    await pc.getByRole('button', { name: 'Aprobar Nivel 2' }).click();
    const res = await apiCallOn(pc, () => pc.locator('[role=dialog],[role=alertdialog]').getByRole('button', { name: 'Aprobar Nivel 2' }).click(),
      { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/autorizaciones$`), status: [200, 201, 204] });
    expect(JSON.parse(res.request().postData() ?? '{}').nivel).toBe(2);
    await expect(pc.getByText(`OC ${folio} aprobada Nivel 2.`)).toBeVisible();
    await reloadAndExpect(pc, async () => { await expect(pc.getByText('Autorizada').first()).toBeVisible(); });
    await expectNoPlaceholders(pc);
    await foto(pc, '06c-oc-grande-autorizada-n2');
    const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
    expect(det.estado).toBe(3);
    expect(det.autorizaciones.length, 'two signatures (N1 and N2) are expected').toBeGreaterThanOrEqual(2);
    expect(new Set(det.autorizaciones.map((a: { usuarioId: string }) => a.usuarioId)).size, 'N1 and N2 must come from two different users').toBe(2);
  });
});
