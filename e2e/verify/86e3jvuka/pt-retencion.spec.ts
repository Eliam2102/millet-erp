/**
 * PT-02 (86e3jvuka): retención del pedido en 69 y liberación a 70, vista desde la pantalla de piezas (Demo).
 * Las piezas entran por el lector real de A+W Demo; el estado Retenido/Liberado lo decide la app.
 */
import { test, expect, shot } from '../../_harness/fixtures';
import { crearPedidos, escanear, PEDIDO_CONTADO, PEDIDO_REPARTO, PEDIDO_SERIE_5000, prepararOrigenDemo } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.describe.configure({ mode: 'serial' });

const PIEZAS = '/producto-terminado/piezas';
const API = '/api/v1/producto-terminado';

test.beforeAll(() => { prepararOrigenDemo(); crearPedidos(); });
// guard.api reuses the bearer the app itself sent: load the screen first so there is one
test.beforeEach(async ({ page }) => {
  await page.goto(PIEZAS);
  await page.waitForLoadState('networkidle');
});

type Estado = { estado: string; motivo: string | null } | null;

async function pagina(guard: any, pedido: string) {
  const r = await guard.api.get(`${API}/piezas?pedido=${pedido}&pageSize=100`);
  expect(r.status()).toBe(200);
  return r.json();
}

async function esperarEstado(guard: any, pedido: string, piezas: number, estado: string, motivo: string) {
  await expect.poll(async () => {
    const j = await pagina(guard, pedido);
    const e: Estado = j.estadoPedido ?? null;
    return `${j.total}|${e?.estado}|${e?.motivo}`;
  }, { timeout: 90_000, intervals: [1000, 2000] }).toBe(`${piezas}|${estado}|${motivo}`);
}

async function buscar(page: import('@playwright/test').Page, pedido: string) {
  await page.goto(PIEZAS);
  await expect(page.getByRole('heading', { name: 'Piezas en producto terminado' })).toBeVisible();
  await page.getByLabel('Pedido').fill(pedido);
  await expect(page.getByRole('region', { name: 'Estado del pedido' })).toBeVisible();
}

test.describe('PT-02 retención y liberación del pedido', () => {
  test('arranque: origen A+W en Demo', async ({ guard }) => {
    const r = await guard.api.put('/api/v1/integraciones/aw/origen', { Origen: 'Demo' }, { 'Idempotency-Key': crypto.randomUUID() });
    expect(r.status()).toBe(200);
    expect((await r.json()).origen).toBe('Demo');
  });

  test('1 pedido de contado con saldo: 8 de 10 piezas, Retenido por saldo pendiente', async ({ page, guard }) => {
    escanear(PEDIDO_CONTADO, 8);
    await esperarEstado(guard, PEDIDO_CONTADO, 8, 'Retenido', 'SaldoPendiente');
    await buscar(page, PEDIDO_CONTADO);
    const estado = page.getByRole('region', { name: 'Estado del pedido' });
    await expect(estado.getByText('Retenido', { exact: true })).toBeVisible();
    await expect(estado.getByText('Saldo pendiente')).toBeVisible();
    const avance = page.getByRole('region', { name: 'Avance por posición' });
    await expect(avance.getByRole('article').filter({ hasText: 'Posición 1' }).locator('dd')).toHaveText(['10', '8', '8', '0', '0', '2']);
    await shot(page, '01-pedido-4800117-retenido-por-saldo-pendiente-8-de-10');
  });

  test('2 saldo en cero libera el pedido una sola vez (Saldo cubierto)', async ({ page, guard }) => {
    const put = () => guard.api.put(`${API}/demo/pedidos/${PEDIDO_CONTADO}/saldo`, { saldo: 0 }, { 'Idempotency-Key': crypto.randomUUID() });
    const a = await put();
    expect(a.status()).toBe(200);
    expect((await a.json()).liberado).toBe(true);
    const b = await put();
    expect(b.status()).toBe(200);
    expect((await b.json()).liberado).toBe(false); // ya estaba liberado: no hay segundo cambio
    const j = await pagina(guard, PEDIDO_CONTADO);
    expect(j.estadoPedido).toMatchObject({ estado: 'Liberado', motivo: 'SaldoCubierto' });
    const desde = j.estadoPedido.desde;
    await buscar(page, PEDIDO_CONTADO);
    const estado = page.getByRole('region', { name: 'Estado del pedido' });
    await expect(estado.getByText('Liberado', { exact: true })).toBeVisible();
    await expect(estado.getByText('Saldo cubierto')).toBeVisible();
    await shot(page, '03-pedido-4800117-liberado-por-saldo-cubierto');
    // la segunda llamada no movió la fecha de liberación
    expect((await pagina(guard, PEDIDO_CONTADO)).estadoPedido.desde).toBe(desde);
  });

  test('3 pedido de reparto con 3 de 10 piezas: Liberado desde la primera pieza', async ({ page, guard }) => {
    escanear(PEDIDO_REPARTO, 3);
    await esperarEstado(guard, PEDIDO_REPARTO, 3, 'Liberado', 'Reparto');
    await buscar(page, PEDIDO_REPARTO);
    const estado = page.getByRole('region', { name: 'Estado del pedido' });
    await expect(estado.getByText('Liberado', { exact: true })).toBeVisible();
    await expect(estado.getByText('Pedido de reparto')).toBeVisible();
    const avance = page.getByRole('region', { name: 'Avance por posición' });
    await expect(avance.getByRole('article').filter({ hasText: 'Posición 1' }).locator('dd')).toHaveText(['10', '3', '3', '0', '0', '7']);
    await shot(page, '04-pedido-4800118-reparto-liberado-con-3-de-10-piezas');
  });

  test('4 pedido de serie 5000 con crédito: Liberado por decisión de Crédito', async ({ page, guard }) => {
    escanear(PEDIDO_SERIE_5000, 4);
    await esperarEstado(guard, PEDIDO_SERIE_5000, 4, 'Liberado', 'DecisionCxc');
    await buscar(page, PEDIDO_SERIE_5000);
    const estado = page.getByRole('region', { name: 'Estado del pedido' });
    await expect(estado.getByText('Liberado', { exact: true })).toBeVisible();
    await expect(estado.getByText('Decisión de Crédito')).toBeVisible();
    await shot(page, '05-pedido-5000231-liberado-por-decision-de-credito');
  });

  test('5 origen Real: el saldo de prueba se rechaza con 409 PT_SOLO_DEMO y la pantalla no cambia', async ({ page, guard }) => {
    guard.allowStatus(409);
    // pedido nuevo retenido para comprobar que no cambia
    const antes = (await pagina(guard, PEDIDO_CONTADO)).estadoPedido;
    const real = await guard.api.put('/api/v1/integraciones/aw/origen', { Origen: 'Real' }, { 'Idempotency-Key': crypto.randomUUID() });
    expect(real.status()).toBe(200);
    try {
      const r = await guard.api.put(`${API}/demo/pedidos/${PEDIDO_CONTADO}/saldo`, { saldo: 500 }, { 'Idempotency-Key': crypto.randomUUID() });
      expect(r.status()).toBe(409);
      expect((await r.json()).code).toBe('PT_SOLO_DEMO');
      expect((await pagina(guard, PEDIDO_CONTADO)).estadoPedido).toEqual(antes);
      await buscar(page, PEDIDO_CONTADO);
      const estado = page.getByRole('region', { name: 'Estado del pedido' });
      await expect(estado.getByText('Liberado', { exact: true })).toBeVisible();
      await expect(estado.getByText('Saldo cubierto')).toBeVisible();
      await shot(page, '06-origen-real-rechazo-409-la-pantalla-no-cambia');
    } finally {
      await guard.api.put('/api/v1/integraciones/aw/origen', { Origen: 'Demo' }, { 'Idempotency-Key': crypto.randomUUID() });
    }
  });
});
