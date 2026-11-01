/**
 * 86e3ke2d3 / P8.3 - portal de proveedores (cierre). Evidencia de UI, entorno test.
 * Serial: cada prueba deja listo lo que usa la siguiente (acceso dado de alta en la ficha, opinión subida, complemento ligado, correo de pago).
 */
import fs from 'node:fs';
import path from 'node:path';
import type { Browser, Page } from '@playwright/test';
import { test, expect, shot } from '../../_harness/fixtures';
import { baseURL } from '../../_harness/session';
import {
  FACTURAS, FECHA_PAGO_PPD, IDS, PDF_OPINION, PROVS, UUID, avisosPendientes, sql, codigoDe, esperarMensaje, mailpitUrl, mailpitVaciar, seed, xmlComplementoPago,
} from './_data';

const VIEWPORT = { width: 1600, height: 900 };
const dinero = (n: number) => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
const pintado = async (page: Page) => {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.animate-pulse, [aria-busy="true"]')).toHaveCount(0, { timeout: 20_000 });
};

/** Portal en un contexto limpio (sin sesión de Entra): correo -> código de Mailpit -> Entrar. */
async function entrarAlPortal(browser: Browser, email: string, viewport = VIEWPORT, alAbrir?: (page: Page) => Promise<void>) {
  const context = await browser.newContext({ baseURL, viewport });
  const page = await context.newPage();
  await page.goto('/portal-proveedores');
  await alAbrir?.(page);
  await page.getByLabel('Correo autorizado').fill(email);
  await page.getByRole('button', { name: 'Enviar código' }).click();
  await expect(page.getByLabel('Código de acceso')).toBeVisible();
  const codigo = await codigoDe(email);
  await page.getByLabel('Código de acceso').fill(codigo);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('tab', { name: 'Mis facturas' })).toBeVisible();
  await pintado(page);
  return { context, page };
}
const fila = (page: Page, folio: string) => page.getByRole('row').filter({ hasText: folio });
const subir = async (page: Page, xml: string, nombre: string) => {
  await page.getByRole('tab', { name: 'Subir factura o complemento' }).click();
  await page.getByLabel('Archivo XML').setInputFiles({ name: nombre, mimeType: 'text/xml', buffer: Buffer.from(xml, 'utf8') });
  await page.getByRole('button', { name: 'Subir documento' }).click();
};
const fixtures = path.resolve(process.cwd(), 'e2e/verify/86e3ke2d3/fixtures');

test.setTimeout(120_000);
test.describe.configure({ mode: 'serial' });

test.afterEach(async ({}, info) => {
  if (info.status === 'passed') return;
  try {
    console.log('[diag] outbox:', sql(`select event_type, published_at, attempts, left(coalesce(last_error,''),300) from tesoreria.integration_events_outbox order by occurred_at desc limit 3`));
    console.log('[diag] pagos_local:', sql(`select cubierto_repp, importe from cuentas_por_pagar.pagos_proveedor_local where factura_proveedor_id::text like '00000019%'`));
    fs.copyFileSync(path.resolve(process.cwd(), '.verification/_test-env/test.backend.log'), '/tmp/claude-1000/-home-devgeo-Agentes-2026-10-31-tareas/cf4ec765-9cd4-4bbe-802c-cdd341b92012/scratchpad/ke2d3-backend.log');
  } catch (e) { console.log('[diag] failed', String(e)); }
});

test.beforeAll(async () => {
  seed();
  await mailpitVaciar();
  fs.mkdirSync(fixtures, { recursive: true });
  fs.writeFileSync(path.join(fixtures, 'complemento-pago-perfiles-valido.xml'),
    xmlComplementoPago({ uuid: UUID.reppValido, rfcEmisor: PROVS.perfiles.rfc, emisor: PROVS.perfiles.razon, docRelacionado: UUID.perfilesPpd, fechaPago: FECHA_PAGO_PPD, monto: FACTURAS.perfilesPpd.total, folio: '2231' }));
  fs.writeFileSync(path.join(fixtures, 'complemento-pago-factura-de-otro-proveedor.xml'),
    xmlComplementoPago({ uuid: UUID.reppAjeno, rfcEmisor: PROVS.perfiles.rfc, emisor: PROVS.perfiles.razon, docRelacionado: UUID.bajioEnCorrida, fechaPago: FECHA_PAGO_PPD, monto: FACTURAS.bajioEnCorrida.total, folio: '2232' }));
});

test.describe('P8.3 portal de proveedores', () => {
  test.use({ role: 'super-admin', viewport: VIEWPORT });

  test('R1 ficha del proveedor: «Acceso al portal» con correo autorizado', async ({ page }) => {
    await page.goto(`/admin/datos-maestros/proveedores/${PROVS.bajio.id}`);
    await pintado(page);
    const sec = page.getByRole('region', { name: 'Acceso al portal' });
    await sec.scrollIntoViewIfNeeded();
    await sec.getByLabel('Correo autorizado').fill(PROVS.bajio.email);
    await sec.getByRole('button', { name: 'Agregar correo' }).click();
    await expect(sec.getByText(PROVS.bajio.email)).toBeVisible();
    await expect(sec.getByText('Activo', { exact: true })).toBeVisible();
    await sec.scrollIntoViewIfNeeded();
    await shot(page, '01-ficha-proveedor-acceso-al-portal-con-correo-autorizado');
  });

  test('R2 portal: código por correo y «Mis facturas» solo del proveedor, con fecha programada y vencimiento', async ({ browser }) => {
    const { context, page } = await entrarAlPortal(browser, PROVS.bajio.email);
    await expect(page.getByRole('columnheader', { name: 'Fecha programada de pago' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Vencimiento' })).toBeVisible();
    // solo sus facturas: ninguna de Selladores, Perfiles ni Embalajes
    for (const f of [FACTURAS.selladores1, FACTURAS.perfilesPpd, FACTURAS.embalajes]) await expect(page.getByText(f.folio)).toHaveCount(0);
    await expect(page.getByRole('row')).toHaveCount(4); // encabezado + 3 facturas
    await expect(fila(page, FACTURAS.bajioEnCorrida.folio)).toContainText('Programada para pago');
    await expect(fila(page, FACTURAS.bajioEnCorrida.folio)).not.toContainText('Sin programar');
    await expect(fila(page, FACTURAS.bajioSinProgramar.folio)).toContainText('Sin programar');
    await shot(page, '02-portal-mis-facturas-fecha-programada-y-sin-programar');
    await context.close();
  });

  test('R2b portal en teléfono (390x844): acceso y «Mis facturas»', async ({ browser }) => {
    const { context, page } = await entrarAlPortal(browser, PROVS.bajio.email, { width: 390, height: 844 },
      (p) => shot(p, '15-portal-acceso-movil-390'));
    await expect(page.getByRole('tab', { name: 'Mis facturas', selected: true })).toBeVisible();
    await shot(page, '16-portal-mis-facturas-movil-390');
    await context.close();
  });

  test('R3 sin opinión de cumplimiento: portal y corrida detenidos; al subirla en el expediente se liberan', async ({ page, browser }) => {
    // Portal del proveedor sin opinión vigente
    const portal = await entrarAlPortal(browser, PROVS.selladores.email);
    await expect(fila(portal.page, FACTURAS.selladores1.folio)).toContainText('Detenida: documentación');
    await expect(fila(portal.page, FACTURAS.selladores1.folio)).toContainText('Falta tu opinión de cumplimiento vigente');
    await shot(portal.page, '03-portal-factura-detenida-por-documentacion');

    // ERP: Tesorería > Nueva corrida
    const abrirNuevaCorrida = async () => {
      await page.goto('/tesoreria/corridas/nueva');
      await pintado(page);
      await page.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
      await page.getByRole('option', { name: /BBVA México/ }).first().click();
      await expect(page.getByText(PROVS.embalajes.razon)).toBeVisible();
    };
    await abrirNuevaCorrida();
    const grupo = `Elegir todas las facturas de ${PROVS.selladores.razon}`;
    await expect(page.getByText(/Detenida por documentación: falta su opinión de cumplimiento vigente/)).toBeVisible();
    await expect(page.getByRole('checkbox', { name: grupo })).toBeDisabled();
    await expect(page.getByRole('checkbox', { name: `Elegir factura ${FACTURAS.selladores1.folio}` })).toBeDisabled();
    await expect(page.getByRole('checkbox', { name: `Elegir todas las facturas de ${PROVS.embalajes.razon}` })).toBeEnabled();
    await shot(page, '04-nueva-corrida-proveedor-detenido-casilla-deshabilitada');

    // Se sube una opinión vigente en el expediente
    await page.goto(`/admin/datos-maestros/proveedores/${PROVS.selladores.id}`);
    await pintado(page);
    const fila32d = page.locator('li[data-tipo="opinion_cumplimiento"]');
    await fila32d.scrollIntoViewIfNeeded();
    await expect(fila32d).toContainText('Sin documento');
    await fila32d.getByRole('button', { name: /Adjuntar Opinión de cumplimiento/ }).click();
    await fila32d.getByLabel(/Archivo \(PDF/).setInputFiles({ name: 'opinion-cumplimiento-32d.pdf', mimeType: 'application/pdf', buffer: PDF_OPINION });
    await fila32d.getByRole('button', { name: 'Subir documento' }).click();
    await expect(fila32d).toContainText('Vigente');
    await fila32d.scrollIntoViewIfNeeded();
    await shot(page, '05-expediente-opinion-de-cumplimiento-vigente');

    // Ambas pantallas lo ven liberado, sin paso manual
    await portal.context.close();
    const vista = (await entrarAlPortal(browser, PROVS.selladores.email)).page;
    await pintado(vista);
    await expect(fila(vista, FACTURAS.selladores1.folio)).not.toContainText('Detenida');
    await expect(fila(vista, FACTURAS.selladores1.folio)).toContainText('Programada para pago');
    await shot(vista, '06-portal-factura-liberada-tras-subir-la-opinion');
    await abrirNuevaCorrida();
    await expect(page.getByText(/Detenida por documentación/)).toHaveCount(0);
    await expect(page.getByRole('checkbox', { name: grupo })).toBeEnabled();
    await shot(page, '07-nueva-corrida-proveedor-liberado-casilla-habilitada');
    await vista.context().close();
  });

  test('R4 complemento de pago (CFDI P): se rechaza el de otro proveedor y el correcto se liga al pago', async ({ browser }) => {
    const { context, page } = await entrarAlPortal(browser, PROVS.perfiles.email);
    const f = fila(page, FACTURAS.perfilesPpd.folio);
    await expect(f).toContainText('Detenida: falta el complemento de pago');
    await shot(page, '08-portal-factura-detenida-falta-complemento-de-pago');
    await page.getByRole('tab', { name: 'Pagos y complementos que debo' }).click();
    await expect(page.getByText(`Falta el complemento de pago por ${dinero(FACTURAS.perfilesPpd.total)}`)).toBeVisible();
    await shot(page, '09-portal-pagos-falta-el-complemento');

    // Rechazo: el XML relaciona una factura que no es de este proveedor
    await subir(page, fs.readFileSync(path.join(fixtures, 'complemento-pago-factura-de-otro-proveedor.xml'), 'utf8'), 'complemento-pago-2232.xml');
    const aviso = page.getByRole('alert');
    await expect(aviso).toContainText('Recibimos tu documento');
    await expect(aviso).toContainText('Cuentas por Pagar lo revisará');
    await expect(aviso).not.toContainText('Ligamos tu complemento');
    await shot(page, '10-portal-complemento-de-otra-factura-queda-recibido-cxp-lo-revisara');
    await page.getByRole('tab', { name: 'Pagos y complementos que debo' }).click();
    await expect(page.getByText(`Falta el complemento de pago por ${dinero(FACTURAS.perfilesPpd.total)}`)).toBeVisible();

    // Éxito: DoctoRelacionado = UUID de su factura, FechaPago y ImpPagado del pago
    await subir(page, fs.readFileSync(path.join(fixtures, 'complemento-pago-perfiles-valido.xml'), 'utf8'), 'complemento-pago-2231.xml');
    await expect(page.getByRole('alert')).toContainText('Ligamos tu complemento a tu pago');
    await shot(page, '11-portal-complemento-ligado-a-tu-pago');
    // La página no invalida su caché al subir (hallazgo): se recarga para leer lo ya ligado. El listener corre por outbox cada ~5 s.
    await expect(async () => {
      await page.reload();
      await expect(page.getByRole('tab', { name: 'Mis facturas' })).toBeVisible();
      await page.getByRole('tab', { name: 'Pagos y complementos que debo' }).click();
      await expect(page.getByRole('row').filter({ hasText: FACTURAS.perfilesPpd.folio })).toContainText('Al corriente', { timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    await shot(page, '12-portal-pagos-al-corriente');
    await expect(async () => {
      await page.reload();
      await page.getByRole('tab', { name: 'Mis facturas' }).click();
      await expect(fila(page, FACTURAS.perfilesPpd.folio)).not.toContainText('Detenida', { timeout: 2_000 });
    }).toPass({ timeout: 30_000 });
    await shot(page, '13-portal-factura-liberada-de-falta-repp');
    await context.close();
  });

  test('R5 aviso de pago: el correo llega a Mailpit', async ({ browser }) => {
    test.setTimeout(540_000); // el worker de avisos corre cada 5 minutos
    expect(avisosPendientes()).toBeGreaterThanOrEqual(0);
    const m = await esperarMensaje(PROVS.bajio.email, /te realizó un pago/i, 480_000);
    const context = await browser.newContext({ viewport: VIEWPORT });
    const page = await context.newPage();
    await page.goto(mailpitUrl(m.ID));
    await expect(page.getByText('Millet te realizó un pago').first()).toBeVisible();
    const html = page.frameLocator('iframe#preview-html');
    await expect(html.getByText(FACTURAS.bajioPagada.folio)).toBeVisible({ timeout: 15_000 });
    await expect(html.getByText(/23,200\.00 MXN/)).toBeVisible();
    await shot(page, '14-mailpit-aviso-de-pago-al-proveedor');
    await context.close();
    void IDS;
  });
});
