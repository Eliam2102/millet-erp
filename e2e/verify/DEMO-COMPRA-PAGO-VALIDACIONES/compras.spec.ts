/**
 * Validaciones de COMPRAS (requisición y orden de compra): el usuario hace algo indebido en la pantalla, la app lo bloquea con un
 * mensaje claro y el servidor también lo rechaza (se llama a la API directamente y se afirma status y código).
 * Sesión: super-admin (única vigente). «Quien capturó no firma» se prueba con la misma persona, que es justo el caso.
 */
import { SA_EMAIL, warm } from '../86e3pc50v/_data';
import type { Page } from '@playwright/test';
import { test, expect, shot, type Guard } from '../../_harness/fixtures';
import {
  abrirContabilidad, captureBearer, crearArticulo, crearRq, nombrarCatalogos, ocTransmitida, ok, otroUsuarioId, PROV_PERFILES, PROV_VIDRIO,
  rand, rechazo, sql, umbrales, uuid, today, foto } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(180_000);
// warm() completes the SSO sign-in if the saved session needs it and waits for the workspace, so guard.api has the page's token
test.beforeEach(async ({ page }) => { await warm(page, '/compras/requisiciones', SA_EMAIL); });

const S: { bien?: { id: string; clave: string; nombre: string }; servicio?: { id: string; clave: string; nombre: string }; yo?: string } = {};

const settle = async (p: Page) => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(700); };
const toast = (p: Page, re: RegExp) => p.locator('[data-sonner-toast]').filter({ hasText: re }).first();

/** Catálogos, umbrales y artículos (una vez por worker; si Playwright reinicia el worker tras un fallo se vuelve a preparar). */
async function prep(guard: Guard) {
  if (S.bien) return;
  nombrarCatalogos();
  await abrirContabilidad(guard);
  await umbrales(guard, 1000, 10000);
  S.bien = await crearArticulo(guard, { clave: `VID-FLT-06-${rand()}`, nombre: 'Vidrio flotado claro 6 mm hoja 2.44 x 3.60 m' });
  S.servicio = await crearArticulo(guard, { clave: `SRV-INS-${rand()}`, nombre: 'Servicio de instalación de canceles', naturaleza: 1 });
  S.yo = (await ok(await guard.api.get('/api/auth/me'))).userId;
}

test('V-RQ-01 requisición sin líneas no se puede enviar a autorización', async ({ page, guard }) => {
  await prep(guard);
  const rq = await crearRq(guard, { descripcion: 'Reposición de vidrio flotado para línea de corte' });
  await page.goto(`/compras/requisiciones/${rq.id}`);
  await settle(page);
  const transmitir = page.getByRole('button', { name: 'Transmitir', exact: true });
  await expect(transmitir).toBeDisabled();
  await transmitir.locator('..').hover();
  await expect(page.getByRole('tooltip').getByText('Agrega al menos una línea antes de transmitir a autorización.').first()).toBeVisible();
  await foto(page, 'V-RQ-01-requisicion-sin-lineas-no-se-puede-transmitir');

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/transmitir`), 422, 'TRANSMITIR_SIN_LINEAS');
  expect(p.detail).toContain('al menos una línea');
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rq.id}`))).estado, 'sigue en Borrador').toBe(0);
});

test('V-RQ-02 artículo que no corresponde al tipo de requisición se rechaza', async ({ page, guard }) => {
  await prep(guard);
  // Requisición de SERVICIO (clasificación 0): un artículo de bien (vidrio) no aplica.
  const rq = await crearRq(guard, { clasificacion: 0, descripcion: 'Instalación de canceles en obra Torre Altavista' });
  await page.goto(`/compras/requisiciones/${rq.id}`);
  await settle(page);
  await page.getByRole('button', { name: /Agregar partida/ }).first().click();
  const form = page.locator('form[aria-label="Agregar partida"]');
  await expect(form).toBeVisible();
  await form.getByRole('combobox', { name: 'Seleccionar artículo' }).click();
  await page.getByLabel('Buscar artículo', { exact: true }).fill(S.bien!.clave);
  await page.waitForTimeout(1200);
  await expect(page.getByRole('option', { name: new RegExp(S.bien!.clave) }), 'el selector no ofrece artículos de otro tipo').toHaveCount(0);
  await foto(page, 'V-RQ-02-articulo-de-otro-tipo-no-aparece-en-requisicion-de-servicio');

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/lineas`, {
    articuloId: S.bien!.id, cantidad: 5, unidadMedida: 'PZA', precioEstimadoMonto: 0, precioEstimadoMoneda: 'MXN', centroCostoId: null,
  }), 422, 'ARTICULO_NO_APLICA_A_CLASIFICACION');
  expect(p.detail).toContain(S.bien!.clave);
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rq.id}`))).lineas, 'no se agregó la línea').toHaveLength(0);
});

test('V-RQ-04 rechazar una requisición exige motivo', async ({ page, guard }) => {
  await prep(guard);
  const rq = await crearRq(guard, { descripcion: 'Compra de perfiles de aluminio para ventanería', articuloId: S.bien!.id, cantidad: 40 });
  await ok(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/transmitir`));
  await page.goto(`/compras/requisiciones/${rq.id}`);
  await settle(page);
  await page.getByRole('button', { name: 'Rechazar', exact: true }).click();
  const dlg = page.getByRole('dialog');
  await expect(dlg).toBeVisible();
  const confirmar = dlg.getByRole('button', { name: 'Confirmar rechazo' });
  await expect(confirmar, 'sin motivo el botón queda bloqueado').toBeDisabled();
  await expect(confirmar).toHaveAttribute('title', 'Selecciona un motivo');
  await foto(page, 'V-RQ-04-rechazo-sin-motivo-boton-bloqueado');

  guard.allowStatus(400, 422);
  await rechazo(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/rechazar`, { motivoId: '00000000-0000-0000-0000-000000000000', motivoTexto: null }), [400, 422], /motivo/i);
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rq.id}`))).estado, 'sigue En autorización').toBe(1);
});

test('V-RQ-03 la misma persona no autoriza su propia requisición', async ({ page, guard }) => {
  await prep(guard);
  const rq = await crearRq(guard, { descripcion: 'Compra de herrajes para canceles', articuloId: S.bien!.id, cantidad: 20 });
  await ok(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/transmitir`));
  await page.goto(`/compras/requisiciones/${rq.id}`);
  await settle(page);
  const aprobar = page.getByRole('button', { name: 'Aprobar Nivel 1' });
  await expect(aprobar, 'quien capturó ve el botón, pero deshabilitado').toBeDisabled();
  await aprobar.locator('..').hover();
  await expect(page.getByRole('tooltip').getByText('Capturaste esta requisición: no puedes autorizarla. Debe autorizarla otra persona.').first()).toBeVisible();
  await foto(page, 'V-RQ-03-quien-capturo-la-requisicion-no-puede-autorizarla');

  guard.allowStatus(422);
  const p = await rechazo(await guard.api.post(`/api/v1/compras/requisiciones/${rq.id}/autorizaciones`, { nivel: 1 }), 422, 'REQUISICION_AUTOAUTORIZACION');
  expect(p.detail).toContain('Quien capturó la requisición');
  expect((await ok(await guard.api.get(`/api/v1/compras/requisiciones/${rq.id}`))).estado, 'sigue En autorización').toBe(1);
});

test('V-OC-01 proveedor bloqueado: no admite órdenes de compra nuevas', async ({ page, guard }) => {
  await prep(guard);
  const bearer = captureBearer(page);
  await page.goto('/compras/ordenes');
  await settle(page);
  // OC en borrador con un proveedor sano; luego se bloquea a otro proveedor y se intenta asignárselo.
  const oc = await ok(await guard.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: '00000005-0003-0000-0000-000000000001', sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: PROV_VIDRIO,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: 'Reposición urgente de vidrio', observaciones: 'Reposición urgente de vidrio',
  }));
  await ok(await guard.api.post(`/api/v1/datos-maestros/proveedores/${PROV_PERFILES}/bloquear`, { motivo: 'Opinión de cumplimiento 32-D en sentido negativo' }));
  await page.goto(`/compras/ordenes/${oc.id}`);
  await settle(page);
  await page.getByRole('button', { name: 'Editar cabecera' }).click();
  const form = page.locator('form[aria-label="Editar cabecera"]');
  await form.getByRole('group', { name: 'Proveedor' }).getByRole('combobox').click();
  // el selector lo muestra deshabilitado con el motivo: no se puede elegir (antes dejaba elegirlo y el servidor lo rechazaba al guardar)
  const opcion = page.getByRole('option', { name: /Perfiles de Aluminio Occidente/ }).first();
  await expect(opcion).toBeVisible();
  await expect.soft(opcion, 'el proveedor bloqueado se ofrece deshabilitado').toHaveAttribute('aria-disabled', 'true');
  await expect.soft(opcion).toContainText('Bloqueado: Opinión de cumplimiento 32-D en sentido negativo');
  await foto(page, 'V-OC-01-proveedor-bloqueado-deshabilitado-en-el-selector');
  await page.keyboard.press('Escape');
  guard.allowStatus(422);

  const p = await rechazo(await guard.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: '00000005-0003-0000-0000-000000000001', sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: PROV_PERFILES,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: 'Compra de perfiles', observaciones: 'Compra de perfiles',
  }), 422, 'PROVEEDOR_BLOQUEADO');
  expect(p.detail).toContain('bloqueado');
  void bearer;
  await ok(await guard.api.post(`/api/v1/datos-maestros/proveedores/${PROV_PERFILES}/desbloquear`, { motivo: 'Opinión regularizada' }));
});


test('V-OC-02 quien capturó la orden de compra no puede firmarla', async ({ page, guard }) => {
  await prep(guard);
  const bearer = captureBearer(page);
  await page.goto('/compras/ordenes');
  await settle(page);
  const oc = await ocTransmitida({ guard, page, bearer, provId: PROV_VIDRIO, articuloId: S.bien!.id, cantidad: 5, precio: 800, motivo: 'Reposición de existencias de vidrio flotado' });
  await page.goto(`/compras/ordenes/${oc.id}`);
  await settle(page);
  const aprobar = page.getByRole('button', { name: 'Aprobar Nivel 1' });
  await expect(aprobar, 'quien capturó ve el botón, pero deshabilitado').toBeDisabled();
  await aprobar.locator('..').hover();
  await expect(page.getByRole('tooltip').getByText('Capturaste esta orden de compra: no puedes aprobarla. Debe aprobarla otra persona.').first()).toBeVisible();
  await foto(page, 'V-OC-02-quien-capturo-la-orden-no-puede-firmarla');
  guard.allowStatus(422);

  const p = await rechazo(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 1 }), 422, 'OC_AUTOAUTORIZACION');
  expect(p.detail).toContain('Quien capturó');
  expect((await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`))).estado, 'sigue en autorización de Jefe de Compras').toBe(1);
});

test('V-OC-03 la misma persona no puede dar las dos firmas; sobre el umbral falta la firma de Dirección y no se envía ni se recibe', async ({ page, guard }) => {
  await prep(guard);
  const bearer = captureBearer(page);
  await page.goto('/compras/ordenes');
  await settle(page);
  // 20 piezas x 900 = 18,000 + IVA: por encima del umbral de 10,000 => exige Nivel 2.
  const oc = await ocTransmitida({ guard, page, bearer, provId: PROV_VIDRIO, articuloId: S.bien!.id, cantidad: 20, precio: 900, motivo: 'Compra de vidrio flotado para obra Torre Altavista' });
  // La captura pasa a otra persona (arrange): así el firmante actual sí puede dar el Nivel 1.
  sql(`update compras.ordenes_compra set comprador_titular_id = '${otroUsuarioId(S.yo!)}' where id = '${oc.id}'`);
  await page.goto(`/compras/ordenes/${oc.id}`);
  await settle(page);
  await page.getByRole('button', { name: 'Aprobar Nivel 1' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Aprobar Nivel 1' }).click();
  await expect(toast(page, /aprobada Nivel 1/)).toBeVisible();
  await settle(page);
  let det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  expect(det.estado, 'sobre el umbral: tras el Nivel 1 queda en Dirección, no Autorizada').toBe(2);

  // Misma persona intenta el Nivel 2
  await page.getByRole('button', { name: 'Aprobar Nivel 2' }).click();
  guard.allowStatus(422);
  guard.expectApiMutationError(422);
  await page.getByRole('alertdialog').getByRole('button', { name: 'Aprobar Nivel 2' }).click();
  await expect(toast(page, /persona distinta/)).toBeVisible();
  await foto(page, 'V-OC-03a-misma-persona-no-puede-dar-las-dos-firmas');
  await rechazo(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 2 }), 422, 'OC_FIRMA_MISMA_PERSONA');

  // La OC sigue sin autorizar: no se puede enviar al proveedor ni recibir
  const env = await rechazo(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/envios`, { destinatario: 'ventas@flotadobajio.mx', modo: 'manual' }), 422, 'OC_ENVIO_NO_AUTORIZADA');
  expect(env.detail).toContain('Autoriza la orden');
  await page.reload();
  await settle(page);
  await foto(page, 'V-OC-03b-orden-sobre-umbral-pendiente-de-direccion');
  det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  expect(det.estado).toBe(2);
});
