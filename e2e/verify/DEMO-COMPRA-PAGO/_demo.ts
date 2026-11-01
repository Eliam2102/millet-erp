/** Shared arrange/UI helpers of the DEMO-COMPRA-PAGO specs. Arrange goes through the app's own API; the few preconditions that have no API (supplier master data, compliance opinion, bank account) are SQL and marked `SQL:`. Test DB only. */
import { apiCallOn, expect, shot } from '../../_harness/fixtures';
import type { Page } from '../../_harness/fixtures';
import { sql } from '../86e3ke5ck/_data';
import { apiBaseURL, CC_SEED, DEPTO_COMPRAS, MID, ok, rand, SUB_INSUMOS_MID, today, uuid } from '../86e3pc50v/_data';

/** Evidence screenshot: waits for the UI to settle (animations, refetch) so the PNG shows the final state, not a transition. */
export async function foto(p: Page, nombre: string) {
  await p.waitForLoadState('networkidle');
  await p.waitForTimeout(1500); // visual settle only (sheet/toast animations), not a sync for the assertion
  await shot(p, nombre);
}

/** Combobox helper for the shared selectors (Popover + Command): opens by accessible name, optional search box, picks an option. */
export async function elegir(page: Page, scope: ReturnType<Page['locator']>, nombre: string, opts: { buscar?: { label: string | RegExp; texto: string }; opcion?: string | RegExp } = {}) {
  await scope.getByRole('combobox', { name: nombre }).click();
  if (opts.buscar) await page.getByPlaceholder(opts.buscar.label as string).fill(opts.buscar.texto);
  const option = opts.opcion ? page.getByRole('option', { name: opts.opcion }) : page.getByRole('listbox').getByRole('option');
  await option.first().click();
}

/** Field labels are bound to their inputs (htmlFor): find the input of the form by its label. */
export const campo = (form: ReturnType<Page['locator']>, etiqueta: RegExp) => form.getByLabel(etiqueta).first();

/** Grants permissions to a user through the app's own admin API and registers the exact revert (same mechanism as `perfil` in _data.ts). */
export async function conceder(guard: any, cleanup: { add: (f: () => Promise<unknown>) => void }, email: string, codigos: string[]) {
  const me = await ok(await guard.api.get('/api/auth/me'));
  const empresaId: string = me.currentEmpresaId;
  const users = await ok(await guard.api.get('/api/v1/identidad/usuarios?limit=100'));
  const userId: string = users.items.find((u: { email: string }) => u.email === email).id;
  const cat = await ok(await guard.api.get('/api/v1/identidad/permisos'));
  const base = `/api/v1/identidad/usuarios/${userId}/empresas/${empresaId}`;
  const previos = (await ok(await guard.api.get(`${base}/permisos`))).permisos
    .filter((p: { origen: string }) => p.origen === 'Concedido' || p.origen === 'Denegado')
    .map((p: { permisoId: string; origen: string }) => ({ permisoId: p.permisoId, efecto: p.origen === 'Concedido' ? 'Conceder' : 'Denegar', motivo: null }));
  const nuevos = codigos
    .map((c) => cat.items.find((x: { codigo: string }) => x.codigo === c))
    .filter((p: unknown) => !!p)
    .filter((p: { id: string }) => !previos.some((q: { permisoId: string }) => q.permisoId === p.id))
    .map((p: { id: string }) => ({ permisoId: p.id, efecto: 'Conceder', motivo: 'E2E DEMO-COMPRA-PAGO' }));
  const put = await guard.api.put(`${base}/permisos-override`, { overrides: [...previos, ...nuevos] });
  // SuperAdmin ya tiene todos los permisos y la API protege sus overrides (422): no hay nada que conceder ni restaurar.
  if (put.status() === 422 && (await put.text()).includes('super_admin_permisos_protegidos')) return;
  expect(put.status(), await put.text()).toBe(200);
  cleanup.add(async () => {
    const r = await guard.api.put(`${base}/permisos-override`, { overrides: previos });
    if (r.status() >= 300) throw new Error(`could not restore overrides: ${r.status()}`);
  });
}

/** Captures the bearer the app itself sends (same mechanism as `warm`), to call endpoints that need extra headers (If-Match, Idempotency-Key, multipart). */
export function captureBearer(p: Page) {
  const h = { v: '' };
  p.on('request', (r) => { const a = r.headers()['authorization']; if (a && r.url().includes('/api/')) h.v = a; });
  return h;
}
export const lista = (r: any): any[] => (Array.isArray(r) ? r : r.items);

/** Arrange (API, user B): the accounting period of the current month must be open (reception and invoice are dated today). Creates the fiscal year if missing. */
export async function abrirPeriodoActual(page: Page, bearer: { v: string }) {
  const call = (method: string, path: string, extra: Record<string, unknown> = {}) =>
    page.request.fetch(`${apiBaseURL}${path}`, { method, headers: { authorization: bearer.v, 'Idempotency-Key': uuid(), ...((extra.headers as object) ?? {}) }, data: extra.data as any });
  const anio = new Date().getFullYear();
  const mes = new Date().getMonth() + 1;
  let ej = lista(await ok(await call('GET', '/api/v1/contabilidad/periodos/ejercicios'))).find((e: { anio: number }) => e.anio === anio);
  if (!ej) ej = await ok(await call('POST', '/api/v1/contabilidad/periodos/ejercicios', { data: { anio } }));
  const p = ej.periodos.find((x: { numero: number }) => x.numero === mes);
  expect(p, `fiscal year ${anio} has no period ${mes}`).toBeTruthy();
  expect(p.estado, `period ${mes}/${anio} is Cerrado: reception and invoice would be rejected; reopen it from Contabilidad > Periodos`).not.toBe('Cerrado');
  if (p.estado === 'NoAbierto' || p.estado === 0) {
    await ok(await call('POST', `/api/v1/contabilidad/periodos/ejercicios/${ej.id}/abrir`, { headers: { 'If-Match': `"${ej.version}"` }, data: { numeros: [mes], motivo: 'Operación de compras' } }));
  }
}

/** Arrange (API): rack bin with a neutral key, with the article assigned. */
export async function crearBinNeutro(guard: any, articuloId: string) {
  const clave = `RACK-${rand()}`;
  const u = await ok(await guard.api.post('/api/v1/almacen/ubicaciones', { subAlmacenId: SUB_INSUMOS_MID, clave, nombre: `${clave} bin`, estatus: 0 }));
  await ok(await guard.api.post('/api/v1/almacen/asignaciones', { ubicacionId: u.id, articuloId }));
  return { id: u.id as string, clave };
}

/** UI: attaches the supplier quotation from the "Adjuntos" tab (real upload control) and checks it persisted. */
export async function adjuntarCotizacionPorUi(pa: Page, guard: any, ocId: string) {
  const nombre = 'cotizacion-papeleria.pdf';
  await pa.getByRole('tab', { name: 'Adjuntos' }).click();
  const mgr = pa.locator('[data-component="adjuntos-manager"]');
  await mgr.locator('input[type="file"]').setInputFiles({ name: nombre, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n') });
  await expect(mgr.locator('[data-component="adjuntos-staging"]')).toContainText(nombre);
  await mgr.getByRole('combobox', { name: 'Tipo de documento' }).click();
  await pa.getByRole('option', { name: /\(cotizacion\)/ }).click();
  await apiCallOn(pa, () => mgr.locator('[data-action="confirmar-upload"]').click(),
    { method: 'POST', url: new RegExp(`/compras/ordenes/${ocId}/adjuntos`), status: [200, 201] });
  await expect(pa.getByText(`"${nombre}" adjuntado.`)).toBeVisible();
  const oc = await ok(await guard.api.get(`/api/v1/compras/ordenes/${ocId}`));
  expect(oc.adjuntos.map((a: { nombreArchivo: string }) => a.nombreArchivo), 'quotation persisted in the backend').toContain(nombre);
}


export const A_PERMS = [
  'compras.requisiciones.crear', 'compras.requisiciones.leer', 'compras.requisiciones.editar',
  'compras.ordenes.crear', 'compras.ordenes.crear-sin-rq', 'compras.ordenes.leer', 'compras.ordenes.adjuntar', 'compras.ordenes.transmitir',
  'centros_costo.dim3.leer-todos', 'compartido.catalogos.leer', 'datos_maestros.proveedores.leer',
];
/** Permissions the second signer (C, almacen-corporativo) needs to see the OC of another branch and sign level 2. */
export const C_PERMS = [
  'compras.ordenes.leer', 'compras.ordenes.autorizar-nivel1', 'compras.ordenes.autorizar-nivel2', 'compras.ordenes.leer-todas-sucursales', 'compras.ordenes.gestionar-todas-sucursales',
  'compras.requisiciones.leer', 'compras.requisiciones.leer-todas-sucursales', 'compartido.catalogos.leer', 'datos_maestros.proveedores.leer',
];
export const UMBRAL_GENERAL = '00000000-0000-0000-0000-000000000000';

/** Arrange (API as B): approval thresholds of the demo script: N1 up to 1,000 and N2 over 10,000, for the company (general) and for Compras. */
export async function umbralesGuion(guard: any) {
  for (const departamentoId of [UMBRAL_GENERAL, DEPTO_COMPRAS])
    await ok(await guard.api.put('/api/v1/compras/umbrales-aprobacion', { departamentoId, umbralMenorCuantia: 1000, umbralMonto: 10000, moneda: 'MXN', modoNivel2: 'SoloMonto' }));
}

const PDF = () => Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

/**
 * Arrange (API): an OC WITHOUT requisition, one line, created by A and transmitted by A after B uploaded both required attachments
 * (quotation + authorization e-mail, OC_SIN_RQ_CORREO_REQUERIDO). The author cannot sign, so B (another real user) signs afterwards.
 */
export async function ocSinRqTransmitida(o: { guard: any; page: Page; A: { api: { post: (p: string, d?: unknown) => Promise<any>; patch: (p: string, d?: unknown) => Promise<any> } }; bearerB: { v: string }; provId: string; articuloId: string; cantidad: number; precio: number; motivo: string }) {
  const { guard, page, A } = o;
  const oc = await ok(await A.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: o.provId,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: o.motivo, observaciones: o.motivo,
  }));
  await ok(await A.api.patch(`/api/v1/compras/ordenes/${oc.id}`, DATOS_FACTURA));
  await ok(await A.api.post(`/api/v1/compras/ordenes/${oc.id}/lineas`, {
    articuloId: o.articuloId, cantidad: o.cantidad, unidadMedida: 'PZA', precioUnitario: o.precio, departamentoSolicitanteId: DEPTO_COMPRAS, centroCostoId: CC_SEED,
  }));
  const tipos = lista(await ok(await guard.api.get('/api/v1/compras/catalogos/tipos-documento-oc')));
  for (const [clave, archivo] of [['cotizacion', 'cotizacion-proveedor.pdf'], ['correo_autorizacion', 'correo-autorizacion.pdf']]) {
    const tipoId = tipos.find((t: { clave: string }) => t.clave === clave)?.id;
    expect(tipoId, `document type "${clave}" missing in the catalog`).toBeTruthy();
    const up = await page.request.fetch(`${apiBaseURL}/api/v1/compras/ordenes/${oc.id}/adjuntos`, {
      method: 'POST', headers: { authorization: o.bearerB.v, 'Idempotency-Key': uuid() },
      multipart: { tipoDocumentoId: tipoId, archivo: { name: archivo, mimeType: 'application/pdf', buffer: PDF() } },
    });
    expect([200, 201], `${clave} upload answered ${up.status()}: ${(await up.text()).slice(0, 300)}`).toContain(up.status());
  }
  await ok(await A.api.post(`/api/v1/compras/ordenes/${oc.id}/transmitir`));
  return oc as { id: string; folio?: string };
}

/** OC header «datos para la factura del proveedor» (required to transmit): G03 gastos en general, PUE, 03 transferencia. */
export const DATOS_FACTURA = { usoCfdiClave: 'G03', metodoPago: 'PUE', formaPagoClave: '03' };

/** Supplier of the demo (glass distributor). */
export const PROV = { id: '00000029-0003-4000-8000-000000000101', clave: 'PRV-4101', razon: 'Papelería y Suministros Industriales del Bajío S.A. de C.V.', rfc: 'PSI9901013A7' };

/** CLABE with the 3-7-1 check digit (same algorithm as the backend's Clabe value object). */
export function clabe(banco: string, plaza: string, cuenta: string): string {
  const base = banco + plaza + cuenta;
  const w = [3, 7, 1];
  const s = base.split('').reduce((a, d, i) => a + ((Number(d) * w[i % 3]) % 10), 0);
  return base + String((10 - (s % 10)) % 10);
}

export const PROV_CUENTA_ID = '00000029-0003-4000-8000-000000000301';
export const PROV_CLABE = clabe('012', '180', '00412398765');

/**
 * SQL: supplier master data with a validated BBVA account and a CURRENT «opinión de cumplimiento» (32-D) in the expediente.
 * No API/UI path creates a supplier with a validated bank account without a second person and real files; the demo only needs the result.
 * `conOpinion=false` leaves the supplier without opinion (invoices are held, payment runs refuse it: #319 / 86e3ke2d3).
 */
export function sembrarProveedor(conOpinion = true) {
  sql(`delete from compartido.adjuntos where entidad_id = '${PROV.id}'; delete from compartido.proveedor_cuentas_bancarias where proveedor_id = '${PROV.id}'; delete from compartido.proveedores where id = '${PROV.id}';`);
  sql(`insert into compartido.proveedores (id, clave, razon_social, rfc, tipo_persona, estatus, version, created_at, updated_at, condiciones_pago_dias, pais, origen, email)
    values ('${PROV.id}', '${PROV.clave}', '${PROV.razon}', '${PROV.rfc}', 1, 0, 1, now(), now(), 30, 'MEX', 0, 'cobranza@papeleriabajio.example');
  insert into compartido.proveedor_cuentas_bancarias (id, proveedor_id, banco, clabe, moneda, titular, predeterminada, estado, capturada_por_nombre, capturada_en, validada_por_nombre, validada_en, version, created_at, updated_at)
    values ('${PROV_CUENTA_ID}', '${PROV.id}', 'BBVA México', '${PROV_CLABE}', 'MXN', '${PROV.razon}', true, 1, 'Compras', now(), 'Tesorería', now(), 1, now(), now());`);
  if (conOpinion)
    sql(`insert into compartido.adjuntos (id, tipo_entidad, entidad_id, tipo_documento_id, nombre_archivo, content_type, tamano_bytes, hash_sha256, blob_ref, vigente_hasta, subido_por_id, subido_en, version, created_at, updated_at)
      values ('00000029-0003-4000-8000-000000000401', 'proveedor', '${PROV.id}', '00000011-0001-0000-0000-000000000007', 'opinion-cumplimiento-32d.pdf', 'application/pdf', 20480, '${'ab'.repeat(32)}', 'opinion/${PROV.id}', (current_date + 25), '00000029-0003-4000-8000-000000000501', now(), 1, now(), now());`);
}

/** Arrange (API): catalog article with a realistic name (unmapped category + non-service nature = «Orden de compra» requisitions). */
export async function articulo(guard: any, nombre: string) {
  const clave = `ART-${rand()}`;
  const r = await ok(await guard.api.post('/api/v1/catalogos/articulos', { clave, nombre, unidadMedidaId: '00000002-0007-0000-0000-000000000001', naturaleza: 0, categoriaId: null }));
  return { id: r.id as string, clave, nombre };
}

/** UI: opens "Nueva recepción" (variant A, with invoice), picks the OC and ticks line 1. */
export async function abrirRecepcion(page: Page, ocFolio: string) {
  await page.goto('/almacen/recepciones');
  await page.waitForLoadState('networkidle');
  await page.getByRole('button', { name: /Nueva recepci/ }).first().click();
  const dlg = page.getByRole('dialog', { name: 'Nueva recepción' });
  await expect(dlg).toBeVisible();
  await dlg.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
  await page.getByPlaceholder('Folio o referencia…').fill(ocFolio);
  await page.getByRole('option', { name: new RegExp(ocFolio) }).click();
  await dlg.getByLabel(/Incluir partida 1/).check();
  return dlg;
}

/** Permissions the arming user (A) needs in Tesorería: arm a payment run and see accounts / liabilities of her branch. */
export const TES_PERMS = ['tesoreria.corridas.crear', 'tesoreria.cuentas.ver', 'tesoreria.pasivos.ver', 'tesoreria.documentos.leer-todas-sucursales', 'cuentas_por_pagar.facturas.leer'];

/** Millet's BBVA paying account (SQL: CLABE and the BBVA statement profile are not settable from the UI). */
export const CUENTA_MILLET = { id: '00000029-0003-4000-8000-000000000601', banco: 'BBVA México', numero: '0112345678', saldoInicial: 1250000 };
export function sembrarCuentaMillet() {
  const emp = sql('select id from compartido.empresas limit 1');
  const d = new Date();
  const corte = new Date(d.getFullYear(), d.getMonth(), 0); // last day of the previous month
  const corteISO = `${corte.getFullYear()}-${String(corte.getMonth() + 1).padStart(2, '0')}-${String(corte.getDate()).padStart(2, '0')}`;
  sql(`delete from tesoreria.cuenta_bancaria where id = '${CUENTA_MILLET.id}';
    insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, perfil_extracto, activa, sucursal, titular, finalidad, saldo_inicial, fecha_corte_saldo_inicial, motivo_saldo_inicial, version, created_at, updated_at)
    values ('${CUENTA_MILLET.id}', '${emp}', '${CUENTA_MILLET.banco}', '${CUENTA_MILLET.numero}', '${clabe('012', '180', '00112345678')}', 'MXN', 'BBVA', true, 'MID', 'Vidrios Millet S.A. de C.V.', 'Pagos a proveedores', ${CUENTA_MILLET.saldoInicial}, '${corteISO}', 'Saldo de apertura', 1, now(), now());`);
}

/**
 * SQL: minimal chart of accounts + role→account mapping. The test DB ships the account roles WITHOUT account (Contabilidad maps the real ones, «V12 pendiente»),
 * so every purchase event lands in the «bandeja contable» (RolSinCuenta). Creating accounts through the API opens an approval request each, hence SQL.
 */
export function sembrarCatalogoContable() {
  const emp = sql('select id from compartido.empresas limit 1');
  const id = (n: number) => `00000029-0004-4000-8000-${String(n).padStart(12, '0')}`;
  // [n, codigo, nombre, padre, nivel, naturaleza(0 deudora/1 acreedora), tipo(0 titulo/1 afectable), control, roles]
  const cuentas: Array<[number, string, string, number | null, number, number, number, number, string[]]> = [
    [1, '1000-0000', 'Activo', null, 1, 0, 0, 0, []],
    [2, '1110-0001', 'Bancos BBVA México', 1, 2, 0, 1, 0, ['BANCOS', 'BANCOS_MN']],
    [3, '1150-0001', 'Inventario de insumos', 1, 2, 0, 1, 0, ['INVENTARIO']],
    [4, '1190-0001', 'IVA acreditable pagado', 1, 2, 0, 1, 0, ['IVA_ACREDITABLE_PAGADO']],
    [5, '1190-0002', 'IVA acreditable pendiente de pago', 1, 2, 0, 1, 0, ['IVA_ACREDITABLE_PENDIENTE']],
    [6, '1180-0001', 'Anticipo a proveedores', 1, 2, 0, 1, 0, ['ANTICIPO_A_PROVEEDORES']],
    [7, '2000-0000', 'Pasivo', null, 1, 1, 0, 0, []],
    [8, '2100-0001', 'Proveedores nacionales', 7, 2, 1, 1, 2, ['PROVEEDORES']],
    [9, '2110-0001', 'Mercancía recibida por facturar', 7, 2, 1, 1, 0, ['MERCANCIA_RECIBIDA_POR_FACTURAR']],
    [10, '2120-0001', 'Retenciones de impuestos por pagar', 7, 2, 1, 1, 0, ['RETENCIONES_POR_PAGAR']],
    [11, '5000-0000', 'Costos y gastos', null, 1, 0, 0, 0, []],
    [12, '5100-0001', 'Gastos de operación', 11, 2, 0, 1, 0, ['GASTO', 'CONSUMO']],
  ];
  sql(cuentas.map(([n, cod, nom, padre, nivel, nat, tipo, ctl]) =>
    `insert into contabilidad.cuentas_contables (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control, version, created_at, updated_at, clase, no_afectable_manual)
     select '${id(n)}', '${emp}', '${cod}', '${nom}', ${padre ? `'${id(padre)}'` : 'null'}, ${nivel}, ${nat}, ${tipo}, 0, ${ctl}, 1, now(), now(), 0, false
     where not exists (select 1 from contabilidad.cuentas_contables where empresa_id='${emp}' and codigo='${cod}');`).join('\n'));
  for (const [n, , , , , , , , roles] of cuentas) for (const rol of roles)
    sql(`with r as (select id from contabilidad.roles_cuenta where empresa_id='${emp}' and codigo='${rol}' and cuenta_id is null and deleted_at is null),
      u as (update contabilidad.roles_cuenta set cuenta_id='${id(n)}', updated_at=now() where id in (select id from r) returning id)
      insert into contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
      select gen_random_uuid(), '${emp}', id, null, '${id(n)}', 'Contabilidad', now(), 'Mapeo inicial del catálogo' from u;`);
}
