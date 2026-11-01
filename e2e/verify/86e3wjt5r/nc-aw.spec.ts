/**
 * 86e3wjt5r - NC de clientes desde A+W: llegan "Por autorizar", excepciones y autorizar/timbrar.
 * Ingesta real por el worker (ver _data.ts). Serial: cada fila depende de la ingesta de la anterior.
 * Ingesta 100% real por el worker. El timbrado va contra el PAC sandbox (helper _pac); nunca se simula un timbrado.
 */
import { expect, newRoleContext, shot, test } from '../../_harness/fixtures';
import { configurarPacSandbox } from '../_pac/pac-sandbox';
import { acuse, CLIENTE, CLIENTE_REF, FACTURA_A, NC_EXCEDE, NC_OK, NC_SIN_FACTURA, ncAw, ncEnErp, seed, sql, sucursalId } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.describe.configure({ mode: 'serial' });
test.setTimeout(120_000);

const BANDEJA = '/facturacion/notas-credito';
const EXCEPCIONES = '/facturacion/pedidos/excepciones';

test.beforeAll(async () => {
  seed();
  ncAw(NC_SIN_FACTURA, 'A-9999', '4500899', 800, 'Bonificación por rayones en espejo biselado');
  ncAw(NC_EXCEDE, 'A-1043', '4500872', 9000, 'Bonificación por retraso en entrega de canceles');
  for (const n of [NC_SIN_FACTURA, NC_EXCEDE]) await acuse(n);
});

test('N-01 [AC1] la NC de 1,500 de A+W aparece en "Notas de crédito por autorizar", sin timbrar', async ({ page, guard }) => {
  await page.goto('/facturacion/repp/pendientes'); // página liviana que llama a la API (captura el bearer)
  await page.waitForLoadState('networkidle');
  // Arrange: serie activa de Nota de crédito (tipo 3) para la sucursal de la factura, como la crearía un administrador
  const me = await (await guard.api.get('/api/auth/me')).json();
  const suc = sucursalId();
  if (sql(`select count(*) from compartido.series where empresa_id='${me.currentEmpresaId}' and tipo_documento=3 and activa and deleted_at is null and (sucursal_id='${suc}' or sucursal_id is null)`) === '0') {
    const serie = await guard.api.post('/api/v1/admin/series',
      { id: crypto.randomUUID(), empresaId: me.currentEmpresaId, sucursalId: suc, tipoDocumento: 3, prefijo: 'NCM', sufijo: null, reinicioPeriodo: 0, folioInicial: 1 },
      { 'Idempotency-Key': crypto.randomUUID() });
    expect(serie.status()).toBe(201);
  }
  // Ingesta real: la fila de la tabla puente la lee el worker y crea la NC "Por autorizar"
  ncAw(NC_OK, FACTURA_A, '4500871', 1500, 'Bonificación por diferencia de medidas en cristal templado 6 mm');
  const ack = await acuse(NC_OK);
  expect(ack, JSON.stringify(ack)).toMatchObject({ resultado: 1 });
  await page.goto('/facturacion');
  await expect(page.getByRole('link', { name: /Notas de crédito por autorizar/ }).first()).toBeVisible();
  await shot(page, '01-facturacion-acceso-notas-por-autorizar');

  await page.goto(BANDEJA);
  await expect(page.getByRole('heading', { name: 'Notas de crédito por autorizar' })).toBeVisible();
  const fila = page.getByRole('row').filter({ hasText: NC_OK });
  await expect(fila).toHaveCount(1);
  await expect(fila).toContainText(CLIENTE);
  await expect(fila).toContainText(FACTURA_A);
  await expect(fila).toContainText('01 · Bonificación');
  await expect(fila).toContainText('1,500.00 MXN');
  await expect(fila).toContainText(/\d{4}-\d{2}-\d{2}/);
  await expect(fila.getByRole('button', { name: 'Autorizar' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: NC_SIN_FACTURA })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: NC_EXCEDE })).toHaveCount(0);
  await shot(page, '02-notas-por-autorizar-nc-1500');

  const api = await (await guard.api.get('/api/v1/facturacion/notas-credito?estado=PorAutorizar&limit=100')).json();
  const nc = api.items.find((i: any) => i.folioAw === NC_OK);
  expect(nc).toMatchObject({ estado: 'PorAutorizar', total: 1500, facturaFolio: FACTURA_A, motivo: 'Bonificacion', receptorNombre: CLIENTE });
  expect(sql(`select coalesce(uuid,'') from facturacion.comprobante where id='${nc.id}'`)).toBe(''); // no timbrada
});

test('N-02 [AC2] la misma NC ingerida dos veces sigue siendo una sola fila', async ({ page }) => {
  expect(ncEnErp(NC_OK)).toBe(1);
  // Re-entrega: A+W no recibió el acuse, la fila vuelve a pendiente y el siguiente barrido la relee
  const { sqlAw } = await import('./_data');
  sqlAw(`update dbo.aw_solicitud_nota_credito set resultado=null, motivo=null, procesada_at=null where numero_nota='${NC_OK}'`);
  const r = await acuse(NC_OK);
  expect(r.motivo).toContain('idempotente');
  expect(ncEnErp(NC_OK)).toBe(1);
  await page.goto(BANDEJA);
  await expect(page.getByRole('row').filter({ hasText: NC_OK })).toHaveCount(1);
  await shot(page, '03-nc-ingerida-dos-veces-una-sola-fila');
});

test('N-03 [AC3] NC de una factura inexistente: excepción con su motivo y sin NC creada', async ({ page }) => {
  expect(ncEnErp(NC_SIN_FACTURA)).toBe(0);
  await page.goto(EXCEPCIONES);
  await expect(page.getByRole('heading', { name: 'Excepciones de ingesta' })).toBeVisible();
  const fila = page.getByRole('row').filter({ hasText: `NC ${NC_SIN_FACTURA}` });
  await expect(fila).toHaveCount(1);
  await expect(fila).toContainText('no hay factura timbrada vigente para el folio A-9999');
  await shot(page, '04-excepcion-factura-inexistente');
});

test('N-04 [AC4] NC de 9,000 sobre factura con saldo de 5,000: excepción "importe mayor que el saldo"', async ({ page }) => {
  expect(ncEnErp(NC_EXCEDE)).toBe(0);
  await page.goto(EXCEPCIONES);
  const fila = page.getByRole('row').filter({ hasText: `NC ${NC_EXCEDE}` });
  await expect(fila).toHaveCount(1);
  await expect(fila).toContainText(/importe 9,000\.00 mayor que el saldo 5,000\.00 de la factura A-1043/);
  await shot(page, '05-excepcion-importe-mayor-que-saldo');
});

test('N-05 [AC5] Autorizar: confirmación y timbrado real contra el PAC sandbox; la NC deja "Por autorizar"', async ({ page, guard }) => {
  await configurarPacSandbox(page, guard);
  const anio = 2026;
  if (sql(`select count(*) from contabilidad.ejercicios_contables where anio=${anio}`) === '0')
    expect((await guard.api.post('/api/v1/contabilidad/periodos/ejercicios', { anio })).ok()).toBeTruthy();
  sql(`update contabilidad.periodos_contables set estado=1, abierto_por='E2E', abierto_en=now() where anio=${anio} and numero between 1 and 12 and estado=0`);

  await page.goto(BANDEJA);
  const fila = page.getByRole('row').filter({ hasText: NC_OK });
  await fila.getByRole('button', { name: 'Autorizar' }).click();
  const dialogo = page.getByRole('alertdialog');
  await expect(dialogo).toContainText('¿Autorizar y timbrar la nota de crédito?');
  await expect(dialogo).toContainText(NC_OK);
  await expect(dialogo).toContainText('1,500.00');
  await page.waitForTimeout(600); // fin de la animación del diálogo
  await shot(page, '06-confirmar-autorizar-y-timbrar');

  const res = page.waitForResponse((r) => /notas-credito\/.+\/autorizar$/.test(r.url()) && r.request().method() === 'POST');
  guard.allowStatus(400, 409, 422, 500, 502, 503);
  guard.expectApiMutationError(400, 409, 422, 500, 502, 503);
  await dialogo.getByRole('button', { name: 'Autorizar y timbrar' }).click();
  const respuesta = await res;
  const cuerpo = await respuesta.text();
  console.log('autorizar ->', respuesta.status(), cuerpo.slice(0, 500));
  console.log('mensaje ->', await page.getByRole('alert').or(page.getByRole('status')).filter({ hasText: /Nota|nota/ }).first().innerText());
  await page.waitForTimeout(500);
  await shot(page, '07-resultado-autorizar');
  expect(respuesta.status(), cuerpo.slice(0, 500)).toBe(200);
  const r = JSON.parse(cuerpo);
  expect(r.uuid).toMatch(/^[0-9a-f-]{36}$/i);
  await expect(page.getByRole('status').filter({ hasText: `UUID ${r.uuid}` })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: NC_OK })).toHaveCount(0);
  await shot(page, '08-nc-timbrada-ya-no-por-autorizar');
  const nc = sql(`select estado||'|'||coalesce(uuid,'') from facturacion.comprobante where id='${r.id}'`);
  expect(nc).toBe(`4|${r.uuid}`);
});

test('N-06 [AC6] un rol sin facturacion.notas-credito.autorizar no ve "Autorizar"', async ({ browser }) => {
  const { context, page } = await newRoleContext(browser, 'auditor');
  try {
    await page.goto(BANDEJA);
    const login = page.getByRole('button', { name: 'Continuar con Microsoft' });
    const titulo = page.getByRole('heading', { name: 'Notas de crédito por autorizar' });
    await login.or(titulo).waitFor();
    // La sesión guardada del auditor expira (MSAL timed_out): sin sesión vigente no hay evidencia honesta que tomar.
    test.skip(await login.isVisible(), 'sesión Entra del rol auditor vencida: renovar con scripts/login-entra.mjs auditor');
    await expect(titulo).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: NC_OK })).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Autorizar' })).toHaveCount(0);
    await expect(page.getByRole('columnheader', { name: 'Acción' })).toHaveCount(0);
    await shot(page, '09-auditor-sin-boton-autorizar');
  } finally { await context.close(); }
});
