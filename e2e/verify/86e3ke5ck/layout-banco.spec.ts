/**
 * T9.3 (86e3ke5ck) - archivo de layout de pago en lote por banco (BBVA primero).
 * Todo el flujo va por la UI real (armar, enviar, firmar, generar archivo) con la sesión de Entra de super-admin: es la única sesión
 * vigente con permisos de Tesorería. Como «quien arma no autoriza», justo después de armar cada corrida se registra por SQL (BD de
 * prueba) que la solicitó Óscar, y super-admin la firma como segunda persona. Pasivos, proveedores y cuentas se siembran por SQL (_data.ts).
 */
import fs from 'node:fs';
import type { Page } from '@playwright/test';
import { test, expect, apiCallOn, expectNoPlaceholders, shot } from '../../_harness/fixtures';
import { BANCO_MILLET, PROVS, grupo, seed, sql, sumaNeto, type Prov } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.describe.configure({ mode: 'serial' });
test.setTimeout(240_000);
test.skip(true, 'T9.3 layout de pago en lote oculto en la UI: pagos masivos son Fase 2 (Eliam, 03-nov).');
test.beforeAll(() => seed());

const S: { A?: string; B?: string; C?: string } = {};
const OUT = '.verification/86e3ke5ck/evidence/test/screens';
const money = (n: number) => n.toFixed(2);
const mx = (n: number) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const excerpt = (name: string, text: string) => { fs.mkdirSync(OUT, { recursive: true }); fs.writeFileSync(`${OUT}/${name}.txt`, text); };

const OSCAR = "(select id from identidad.usuarios where entra_oid = 'a0cf682d-45fe-42e6-a87f-790f5357c97f')";

/** Arma la corrida con las facturas de `provs` (checkbox por proveedor) y, si `enviar`, la manda a autorización a nombre de otra persona. */
async function armar(page: Page, prefix: string, nombre: string, provs: Prov[], enviar: boolean, capturas: boolean) {
  await page.goto('/tesoreria/corridas/nueva');
  await page.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
  await page.getByRole('option', { name: new RegExp(BANCO_MILLET) }).click();
  await expect(page.getByRole('checkbox', { name: `Elegir todas las facturas de ${provs[0].razon}` })).toBeVisible();
  await page.getByLabel('Nombre (opcional)').fill(nombre);
  if (capturas) await shot(page, `${prefix}a-nueva-corrida-pasivos-disponibles`);
  for (const [i, p] of provs.entries()) {
    const cb = page.getByRole('checkbox', { name: `Elegir todas las facturas de ${p.razon}` });
    await cb.scrollIntoViewIfNeeded();
    await cb.check();
    if (capturas && i === 5) await shot(page, `${prefix}b-seleccion-a-la-mitad`);
  }
  await expect(page.getByText(new RegExp(`${provs.length} facturas? de ${provs.length} proveedor`))).toBeVisible();
  if (capturas) {
    await page.getByRole('button', { name: 'Crear corrida en borrador' }).scrollIntoViewIfNeeded();
    await shot(page, `${prefix}c-seleccion-completa-con-totales`);
  }
  await page.getByRole('button', { name: 'Crear corrida en borrador' }).click();
  await page.waitForURL(/\/tesoreria\/corridas\/[0-9a-f-]{36}$/);
  const id = page.url().split('/').pop()!;
  await expect(page.getByRole('heading', { name: nombre })).toBeVisible();
  await expect(page.getByText('Borrador', { exact: true }).first()).toBeVisible();
  if (capturas) await shot(page, `${prefix}d-corrida-en-borrador`);
  if (enviar) {
    sql(`update tesoreria.corrida_pago set solicitada_por = ${OSCAR} where id = '${id}'`);
    await page.reload();
    await page.getByRole('button', { name: 'Enviar a autorización' }).click();
    await expect(page.getByText('En autorización', { exact: true }).first()).toBeVisible();
    if (capturas) await shot(page, `${prefix}e-corrida-enviada-a-autorizacion-firmas-0-de-1`);
  }
  return id;
}

/** super-admin (no armó la corrida) firma y autoriza desde el detalle. */
async function autorizar(page: Page, id: string, prefix: string, capturas: boolean) {
  await page.goto(`/tesoreria/corridas/${id}`);
  await expect(page.getByRole('button', { name: 'Firmar y autorizar' })).toBeEnabled();
  if (capturas) await shot(page, `${prefix}f-firmante-ve-corrida-en-autorizacion`);
  await page.getByRole('button', { name: 'Firmar y autorizar' }).click();
  // La confirmación es un AlertDialog: se espera visible (sin animación a medias) antes de capturar.
  const dlg = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Firmar la corrida' });
  await expect(dlg.getByRole('button', { name: 'Firmar y autorizar' })).toBeEnabled();
  await page.waitForTimeout(300);
  if (capturas) await shot(page, `${prefix}g-dialogo-firmar-corrida`);
  await dlg.getByRole('button', { name: 'Firmar y autorizar' }).click();
  await expect(page.getByText('Autorizada', { exact: true }).first()).toBeVisible();
  if (capturas) await shot(page, `${prefix}h-corrida-autorizada`);
}

const generar = async (page: Page) => {
  const dl = page.waitForEvent('download');
  const res = await apiCallOn(page, () => page.getByRole('button', { name: 'Generar archivo' }).click(), { method: 'POST', url: /\/corridas\/[0-9a-f-]+\/layout\?/, status: 200 });
  const download = await dl;
  return { r: await res.json(), download, texto: fs.readFileSync(await download.path(), 'utf8') };
};

test('T9.3-1 [CA1] corrida autorizada de 12 pagos nacionales BBVA: el archivo trae 12 líneas con neto, CLABE y referencia, y el total coincide con la corrida', async ({ page, guard }) => {
  const A = grupo('A');
  S.A = await armar(page, '01', 'Pago a proveedores nacionales 04-nov', A, true, true);
  await autorizar(page, S.A, '02', true);
  const neto = sumaNeto(A);
  await expect(page.getByText(mx(neto)).first()).toBeVisible(); // "Neto a transferir"
  await page.getByRole('button', { name: 'Generar archivo' }).scrollIntoViewIfNeeded();
  await shot(page, '03-corrida-autorizada-boton-generar-archivo');

  const { r, download, texto } = await generar(page);
  await expect(page.getByText('Archivo generado con 12 líneas')).toBeVisible();
  await shot(page, '04-archivo-bbva-generado-aviso-12-lineas');

  // respuesta del servidor
  expect(r.esReimpresion).toBe(false);
  expect(r.banco).toBe('BBVA');
  expect(r.lineasValidas).toBe(12);
  expect(r.lineasExcluidas).toBe(0);
  expect(r.excepciones).toHaveLength(0);
  expect(r.total).toBeCloseTo(neto, 2);
  // archivo descargado
  expect(download.suggestedFilename()).toBe(r.nombreArchivo);
  expect(download.suggestedFilename()).toMatch(/^PAGOS_BBVA_NacionalSpei_Pago_a_proveedores_nacionales_04-nov_\d{8}_\d{6}\.txt$/);
  expect(texto).toBe(r.contenido);
  const lineas = texto.split('\r\n');
  expect(lineas).toHaveLength(12);
  let suma = 0;
  for (const p of A) {
    const l = lineas.find((x) => x.startsWith(`${p.clabe},`));
    expect(l, `línea de ${p.razon}`).toBeTruthy();
    const [cuenta, importe, titular, referencia, concepto] = l!.split(',');
    expect(cuenta).toMatch(/^\d{18}$/);
    expect(importe).toBe(money(p.total));
    expect(referencia).toBe(p.folio);
    expect(concepto).toBe(`PAGO ${p.folio}`);
    expect(titular).toBe(p.razon.slice(0, 40));
    suma += Number(importe);
  }
  expect(Math.round(suma * 100) / 100).toBeCloseTo(neto, 2); // total del archivo = neto de la corrida
  // la retención del transportista (4 %) baja su importe: 24,000 + IVA 3,840 - 960 = 26,880.00
  expect(lineas.find((x) => x.includes(',TC-88231,'))).toContain(',26880.00,');
  excerpt('05-archivo-bbva-contenido', `${download.suggestedFilename()}\n\n${texto}\n\n# ${lineas.length} líneas · suma ${money(suma)} · neto de la corrida ${money(neto)}\n`);
  // verificación independiente en BD: un registro de archivo, 12 líneas, total
  expect(sql(`select count(*) || '|' || coalesce(max(total),0) || '|' || max(lineas_validas) || '|' || max(veces_descargado) from tesoreria.corrida_pago_archivo where corrida_id='${S.A}'`)).toBe(`1|${money(neto)}|12|1`);
  expect(sql(`select count(*) from tesoreria.corrida_pago_archivo_linea l join tesoreria.corrida_pago_archivo a on a.id=l.archivo_layout_id where a.corrida_id='${S.A}'`)).toBe('12');
  await expectNoPlaceholders(page);
});

test('T9.3-2 [CA2] proveedor con CLABE inválida: queda fuera del archivo y aparece en excepciones con la causa', async ({ page, guard }) => {
  const Bs = grupo('B');
  const mala = Bs.find((p) => p.invalida)!;
  const buenas = Bs.filter((p) => !p.invalida);
  S.B = await armar(page, '06', 'Pago urgente materiales 06-nov', Bs, true, true);
  await autorizar(page, S.B, '07', false);
  await page.getByRole('button', { name: 'Generar archivo' }).scrollIntoViewIfNeeded();
  await shot(page, '08-corrida-con-proveedor-clabe-invalida-autorizada');

  const { r, download, texto } = await generar(page);
  await expect(page.getByText('Archivo generado con 2 líneas')).toBeVisible();
  await expect(page.getByText(/1 pasivos quedaron fuera por inconsistencias bancarias/)).toBeVisible();
  await shot(page, '09-ERROR-clabe-invalida-aviso-1-pasivo-fuera-del-archivo');

  expect(r.lineasValidas).toBe(2);
  expect(r.lineasExcluidas).toBe(1);
  expect(r.excepciones).toHaveLength(1);
  expect(r.excepciones[0]).toMatchObject({ proveedorRazonSocial: mala.razon, folioProveedor: mala.folio, causa: 'La CLABE no pasa la validación del dígito verificador.' });
  expect(r.excepciones[0].importeNeto).toBeCloseTo(mala.total, 2);
  // fuera del archivo
  const lineas = texto.split('\r\n');
  expect(lineas).toHaveLength(2);
  expect(texto).not.toContain(mala.clabe);
  expect(texto).not.toContain(mala.folio);
  for (const p of buenas) expect(lineas.some((l) => l.startsWith(`${p.clabe},${money(p.total)},`))).toBe(true);
  expect(r.total).toBeCloseTo(sumaNeto(buenas), 2); // el total del archivo NO incluye lo excluido
  expect(r.total).toBeLessThan(sumaNeto(Bs));
  excerpt('10-excepciones-api-y-archivo', `# Excepciones devueltas por POST /api/v1/tesoreria/corridas/{id}/layout\n${JSON.stringify(r.excepciones, null, 2)}\n\n# Archivo ${download.suggestedFilename()} (${lineas.length} líneas)\n${texto}\n`);
  expect(sql(`select lineas_validas || '|' || lineas_excluidas from tesoreria.corrida_pago_archivo where corrida_id='${S.B}'`)).toBe('2|1');
  expect(sql(`select count(*) from tesoreria.corrida_pago_archivo_linea l join tesoreria.corrida_pago_archivo a on a.id=l.archivo_layout_id where a.corrida_id='${S.B}' and l.cuenta_destino='${mala.clabe}'`)).toBe('0');
});

test('T9.3-3 [CA3] archivo ya generado y pedido otra vez: se entrega marcado como reimpresión y no se crea otro registro', async ({ page }) => {
  expect(S.A, 'depende de T9.3-1').toBeTruthy();
  const antes = sql(`select id || '|' || nombre_archivo || '|' || veces_descargado from tesoreria.corrida_pago_archivo where corrida_id='${S.A}'`);
  expect(antes).toMatch(/\|1$/);
  await page.goto(`/tesoreria/corridas/${S.A}`);
  await expect(page.getByRole('button', { name: 'Generar archivo' })).toBeVisible();
  const { r, download, texto } = await generar(page);
  await expect(page.getByText('Archivo descargado (Reimpresión)')).toBeVisible();
  await shot(page, '11-segundo-pedido-aviso-archivo-descargado-reimpresion');

  expect(r.esReimpresion).toBe(true);
  expect(r.lineasValidas).toBe(12);
  expect(download.suggestedFilename()).toBe(antes.split('|')[1]); // mismo archivo, mismo nombre
  expect(texto.split('\r\n')).toHaveLength(12);
  expect(r.archivoId).toBe(antes.split('|')[0]);
  // sin nuevo registro: sigue 1 archivo y 12 líneas; solo sube el contador de descargas
  expect(sql(`select count(*) || '|' || max(veces_descargado) from tesoreria.corrida_pago_archivo where corrida_id='${S.A}'`)).toBe('1|2');
  expect(sql(`select count(*) from tesoreria.corrida_pago_archivo_linea l join tesoreria.corrida_pago_archivo a on a.id=l.archivo_layout_id where a.corrida_id='${S.A}'`)).toBe('12');
  excerpt('12-reimpresion-api-y-bd', `archivoId (1.ª vez) = ${antes.split('|')[0]}\narchivoId (reimpresión) = ${r.archivoId}\nesReimpresion = ${r.esReimpresion}\nregistros de archivo en BD para la corrida: 1, veces_descargado: 2\nmismo contenido que la 1.ª vez: ${fs.readFileSync(`${OUT}/05-archivo-bbva-contenido.txt`, 'utf8').includes(texto)}\n`);
  expect(fs.readFileSync(`${OUT}/05-archivo-bbva-contenido.txt`, 'utf8')).toContain(texto);
});

test('T9.3-4 [CA4] corrida en borrador: no se puede generar el archivo (la UI no ofrece el botón y el servidor responde 422)', async ({ page, guard }) => {
  const C = grupo('C');
  S.C = await armar(page, '13', 'Compra de refacciones 07-nov', C, false, false);
  await page.goto(`/tesoreria/corridas/${S.C}`);
  await expect(page.getByText('Borrador', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Generar archivo' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Descargar oficio' })).toHaveCount(0);
  await shot(page, '13-corrida-en-borrador-sin-boton-generar-archivo');

  guard.allowStatus(422);
  const res = await guard.api.post(`/api/v1/tesoreria/corridas/${S.C}/layout?banco=BBVA&tipoPago=NacionalSpei`);
  expect(res.status()).toBe(422);
  const prob = await res.json();
  expect(prob.code ?? prob.errorCode ?? JSON.stringify(prob)).toContain('CORRIDA_LAYOUT_NO_AUTORIZADA');
  expect(prob.detail).toContain('solo se genera para corridas autorizadas');
  excerpt('14-ERROR-borrador-api-422', `POST /api/v1/tesoreria/corridas/${S.C}/layout -> ${res.status()}\n${JSON.stringify(prob, null, 2)}\n`);
  expect(sql(`select count(*) from tesoreria.corrida_pago_archivo where corrida_id='${S.C}'`)).toBe('0');
  // también el GET de descarga lo rechaza
  const get = await guard.api.get(`/api/v1/tesoreria/corridas/${S.C}/layout/descargar`);
  expect(get.status()).toBe(422);
  expect(sql(`select count(*) from tesoreria.corrida_pago_archivo where corrida_id='${S.C}'`)).toBe('0');
});
