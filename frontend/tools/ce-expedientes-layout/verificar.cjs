const { chromium } = require(process.env.CE_LAYOUT_PLAYWRIGHT || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const out = process.env.CE_LAYOUT_OUTPUT || path.resolve('../..', 'outputs/CE_layout_2026-11-11');
fs.mkdirSync(out, { recursive: true });
(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'chrome' });
  const page = await browser.newPage();
  page.on('console', (m) => {
    if (m.type() === 'error') console.error('Console:', m.text());
  });
  const errors = [];
  page.on('pageerror', (e) => (errors.push(e.message), console.error('Browser:', e.message)));
  await page.route('**/*', (route) =>
    new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort(),
  );
  const results = [];
  async function check(state) {
    const measures = await page.evaluate(() => ({
      width: innerWidth,
      document: document.documentElement.scrollWidth,
      overflows: [
        ...document.querySelectorAll(
          '[aria-label="Detalle del expediente"] dd, [aria-label="Detalle del expediente"] header, [aria-label="Detalle del expediente"] form',
        ),
      ]
        .filter((el) => el.scrollWidth > el.clientWidth + 1)
        .map((el) => el.textContent.slice(0, 80)),
    }));
    if (measures.document > measures.width || measures.overflows.length)
      await page.screenshot({ path: path.join(out, 'ultima-verificacion.png'), fullPage: true });
    assert(
      measures.document <= measures.width,
      `${state}: page overflow ${JSON.stringify(measures)}`,
    );
    assert.deepEqual(measures.overflows, [], `${state}: clipped content`);
    results.push({ state, ...measures });
  }
  for (const [width, height] of [
    [1280, 800],
    [1366, 768],
    [1440, 900],
    [1920, 1080],
    [960, 540],
    [390, 844],
  ]) {
    await page.setViewportSize({ width, height });
    for (const long of [false, true]) {
      await page.goto(`http://127.0.0.1:5187/${long ? '?largo=1' : ''}`);
      await page.getByRole('heading', { name: 'CE-EXP-2026-000001', exact: true }).waitFor();
      await page.evaluate(() => document.fonts.ready);
      await check(`${width}x${height}-${long ? 'long' : 'normal'}-summary`);
      await page.getByRole('tab', { name: 'Incidencias', exact: true }).click();
      await check(`${width}x${height}-${long ? 'long' : 'normal'}-closed`);
      if (!long && [1366, 1920].includes(width))
        await page.screenshot({ path: path.join(out, `expedientes-${width}.png`), fullPage: true });
      await page.getByRole('button', { name: 'Mostrar expedientes', exact: true }).focus();
      await page.keyboard.press('Enter');
      assert.equal(
        await page
          .getByRole('button', { name: 'Ocultar expedientes' })
          .getAttribute('aria-expanded'),
        'true',
      );
      await check(`${width}-${long ? 'long' : 'normal'}-open`);
      if (long && width === 1366)
        await page.screenshot({
          path: path.join(out, 'expedientes-1366-largo-lista.png'),
          fullPage: true,
        });
      await page.getByRole('button', { name: 'Editar datos', exact: true }).click();
      await page.getByLabel('Referencia', { exact: false }).fill('Captura ficticia conservada');
      await page.getByRole('button', { name: 'Ocultar expedientes' }).click();
      assert.equal(
        await page.getByLabel('Referencia', { exact: false }).inputValue(),
        'Captura ficticia conservada',
      );
      await check(`${width}-${long ? 'long' : 'normal'}-edit`);
    }
  }
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.goto('http://127.0.0.1:5187/?largo=1');
  await page.getByRole('tab', { name: 'Resumen', exact: true }).waitFor();
  const tabs = await page.getByRole('tab').all();
  assert.equal(tabs.length, 10);
  await tabs[0].focus();
  for (let i = 1; i < 10; i++) {
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(
      (i) => document.querySelectorAll('[role=tab]')[i]?.getAttribute('aria-selected') === 'true',
      i,
      { timeout: 5000 },
    );
    assert.equal(await tabs[i].getAttribute('aria-selected'), 'true');
    const b = await tabs[i].boundingBox();
    assert(b.x >= 0 && b.x + b.width <= 1367, 'Focused tab clipped');
    await check(`keyboard-tab-${i}`);
  }
  await page.getByRole('tab', { name: 'Incidencias', exact: true }).click();
  await page.getByRole('button', { name: 'Registrar incidencia', exact: true }).click();
  await check('incidence-form');
  await page.screenshot({
    path: path.join(out, 'expedientes-1366-incidencia.png'),
    fullPage: true,
  });
  await page.emulateMedia({ media: 'print' });
  assert.equal(await page.getByRole('button', { name: 'Mostrar expedientes' }).isVisible(), false);
  await page.emulateMedia({ media: 'screen' });
  await page.goto('http://127.0.0.1:5187/?lectura=1');
  await page.getByRole('tab', { name: 'Resumen', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Editar datos', exact: true }).count(), 0);
  assert.deepEqual(errors, [], 'Browser errors');
  fs.writeFileSync(path.join(out, 'medidas.json'), JSON.stringify({ results, errors }, null, 2));
  console.log(
    `${results.length} comprobaciones de distribución aprobadas; cero errores del navegador.`,
  );
  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
