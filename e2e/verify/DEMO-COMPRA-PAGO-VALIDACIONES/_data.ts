/**
 * Arrange helpers for the VALIDACIONES evidence (purchase-to-pay error and validation cases). TEST env only.
 * The action under test always goes through the UI (and then the API is called directly to prove the server rejects it too);
 * everything else is arranged through the app's own API, with SQL only where no endpoint exists (events bridge is off in test).
 */
import { execFileSync } from 'node:child_process';
import type { Page } from '@playwright/test';
import { apiBaseURL, env, envCfg } from '../../_harness/session';
import { expect, shot, type Guard } from '../../_harness/fixtures';

export const MID = '00000005-0003-0000-0000-000000000001';
export const SUB_INSUMOS_MID = '00000008-0001-0000-0000-000000000001';
export const PZA = '00000002-0007-0000-0000-000000000001';
export const DEPTO_COMPRAS = '00000005-0004-0000-0000-000000000001';
export const CC_SEED = '0000000c-0005-0000-0000-000000000001';
export const UMBRAL_GENERAL = '00000000-0000-0000-0000-000000000000';
export const PROV_VIDRIO = '00000005-0001-0000-0000-000000000001'; // PROV-EST, renamed below
export const PROV_PERFILES = '00000005-0001-0000-0000-000000000003'; // PROV-MAY, renamed below

export const rand = () => Math.random().toString(36).slice(2, 7).toUpperCase();
export const uuid = () => crypto.randomUUID();
export const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
export const esc = (s: string) => s.replace(/'/g, "''");

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[validaciones] seeding is only allowed in the test environment');
  const url = new URL(process.env.TEST_DATABASE_URL ?? envCfg.db.url);
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[validaciones] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}

export async function ok(res: { status(): number; text(): Promise<string> }, expected: number[] = [200, 201, 204]) {
  const body = await res.text();
  expect(expected, `unexpected status ${res.status()}: ${body.slice(0, 500)}`).toContain(res.status());
  return body ? JSON.parse(body) : null;
}
export const lista = (r: any): any[] => (Array.isArray(r) ? r : r.items);

/** Reads the problem+json of a rejected call and asserts status and code. Returns the problem (message in `detail`). */
export async function rechazo(res: { status(): number; text(): Promise<string> }, status: number | number[], code: string | RegExp) {
  const txt = await res.text();
  const st = Array.isArray(status) ? status : [status];
  expect(st, `status ${res.status()}: ${txt.slice(0, 400)}`).toContain(res.status());
  const p = txt ? JSON.parse(txt) : {};
  const got: string = p.code ?? p.errorCode ?? p.extensions?.code ?? JSON.stringify(p);
  const todo = `${got} ${p.detail ?? ''}`; // los errores de validación (400) llevan el motivo en `detail`
  if (typeof code === 'string') expect(todo, txt.slice(0, 400)).toContain(code); else expect(todo, txt.slice(0, 400)).toMatch(code);
  return p as { code?: string; detail?: string; title?: string; errors?: unknown };
}

/** Captures the bearer the app itself sends (to call endpoints that need multipart / extra headers). */
export function captureBearer(p: Page) {
  const h = { v: '' };
  p.on('request', (r) => { const a = r.headers()['authorization']; if (a && r.url().includes('/api/')) h.v = a; });
  return h;
}

/** Names of the test catalogs, made believable for the client presentation (no "demo" strings on screen). */
export function nombrarCatalogos() {
  sql(`update compartido.proveedores set razon_social = 'Vidrio Flotado del Bajío S.A. de C.V.', nombre_comercial = 'Flotado del Bajío' where id = '${PROV_VIDRIO}';
       update compartido.proveedores set razon_social = 'Perfiles de Aluminio Occidente S.A. de C.V.', nombre_comercial = 'Perfiles Occidente' where id = '${PROV_PERFILES}';
       update compartido.proveedor_cuentas_bancarias set banco = 'BBVA México', clabe = '012180004123987655', titular = 'Vidrio Flotado del Bajío S.A. de C.V.' where proveedor_id = '${PROV_VIDRIO}';
       update compartido.proveedor_cuentas_bancarias set banco = 'Banorte', clabe = '072320008712345608', titular = 'Perfiles de Aluminio Occidente S.A. de C.V.' where proveedor_id = '${PROV_PERFILES}';`);
}

/** Opens the 12 accounting periods of the current year (arrange). */
export async function abrirContabilidad(guard: Guard) {
  const anio = new Date().getFullYear();
  if (sql(`select count(*) from contabilidad.ejercicios_contables where anio=${anio}`) === '0')
    await ok(await guard.api.post('/api/v1/contabilidad/periodos/ejercicios', { anio }));
  sql(`update contabilidad.periodos_contables set estado=1, abierto_por='Contabilidad', abierto_en=now() where anio=${anio} and numero between 1 and 12 and estado=0`);
}

export async function crearArticulo(guard: Guard, o: { clave: string; nombre: string; naturaleza?: number }) {
  const r = await ok(await guard.api.post('/api/v1/catalogos/articulos', {
    clave: o.clave, nombre: o.nombre, unidadMedidaId: PZA, naturaleza: o.naturaleza ?? 0, categoriaId: null,
  }));
  return { id: r.id as string, clave: o.clave, nombre: o.nombre };
}

export async function crearBin(guard: Guard, articuloIds: string[], clave = `RK-${rand()}`) {
  const u = await ok(await guard.api.post('/api/v1/almacen/ubicaciones', { subAlmacenId: SUB_INSUMOS_MID, clave, nombre: `Rack ${clave}`, estatus: 0 }));
  for (const articuloId of articuloIds) await ok(await guard.api.post('/api/v1/almacen/asignaciones', { ubicacionId: u.id, articuloId }));
  return { id: u.id as string, clave };
}

/** Approval thresholds: N1 up to `menor`, N2 above `monto` (company-wide and for Compras). */
export async function umbrales(guard: Guard, menor: number, monto: number) {
  for (const departamentoId of [UMBRAL_GENERAL, DEPTO_COMPRAS])
    await ok(await guard.api.put('/api/v1/compras/umbrales-aprobacion', { departamentoId, umbralMenorCuantia: menor, umbralMonto: monto, moneda: 'MXN' }));
}

/** RQ (draft) of clasificación 1 = Orden de compra, with an optional line. Returns id + folio. */
export async function crearRq(guard: Guard, o: { clasificacion?: number; descripcion: string; articuloId?: string; cantidad?: number }) {
  const rq = await ok(await guard.api.post('/api/v1/compras/requisiciones', {
    sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: o.clasificacion ?? 1, prioridad: 1,
    fechaSolicitud: new Date().toISOString(), descripcion: o.descripcion, fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null,
  }));
  const id = (rq.id ?? rq.requisicionId) as string;
  if (o.articuloId)
    await ok(await guard.api.post(`/api/v1/compras/requisiciones/${id}/lineas`, {
      articuloId: o.articuloId, cantidad: o.cantidad ?? 10, unidadMedida: 'PZA', precioEstimadoMonto: 0, precioEstimadoMoneda: 'MXN', centroCostoId: CC_SEED,
    }));
  const det = await ok(await guard.api.get(`/api/v1/compras/requisiciones/${id}`));
  return { id, folio: det.folio as string };
}

const PDF = () => Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

/** OC without requisition, one line, with the two required attachments, transmitted by the session user (the author). */
export async function ocTransmitida(o: { guard: Guard; page: Page; bearer: { v: string }; provId: string; articuloId: string; cantidad: number; precio: number; motivo: string }) {
  const { guard, page } = o;
  const oc = await ok(await guard.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: o.provId,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: o.motivo, observaciones: o.motivo,
  }));
  await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/lineas`, {
    articuloId: o.articuloId, cantidad: o.cantidad, unidadMedida: 'PZA', precioUnitario: o.precio, departamentoSolicitanteId: DEPTO_COMPRAS, centroCostoId: CC_SEED,
  }));
  await ok(await guard.api.patch(`/api/v1/compras/ordenes/${oc.id}`, { usoCfdiClave: 'G03', metodoPago: 'PUE', formaPagoClave: '03' }));
  const tipos = lista(await ok(await guard.api.get('/api/v1/compras/catalogos/tipos-documento-oc')));
  for (const [clave, archivo] of [['cotizacion', 'cotizacion-proveedor.pdf'], ['correo_autorizacion', 'correo-autorizacion.pdf']]) {
    const tipoId = tipos.find((t: { clave: string }) => t.clave === clave)?.id;
    expect(tipoId, `document type "${clave}" missing`).toBeTruthy();
    const up = await page.request.fetch(`${apiBaseURL}/api/v1/compras/ordenes/${oc.id}/adjuntos`, {
      method: 'POST', headers: { authorization: o.bearer.v, 'Idempotency-Key': uuid() },
      multipart: { tipoDocumentoId: tipoId, archivo: { name: archivo, mimeType: 'application/pdf', buffer: PDF() } },
    });
    expect([200, 201], `${clave} upload ${up.status()}: ${(await up.text()).slice(0, 300)}`).toContain(up.status());
  }
  await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/transmitir`));
  const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  return { id: oc.id as string, folio: det.folio as string, lineaId: det.lineas[0].id as string, requiereNivel2: det.requiereNivel2 as boolean | undefined, estado: det.estado as number };
}

/** Another real user of the test DB (to make the author of an OC someone other than the session user). */
export const otroUsuarioId = (yoId: string) => sql(`select id from identidad.usuarios where id <> '${yoId}' order by created_at limit 1`);

/** Evidence screenshot after the animations (dialogs, toasts) have settled, so the PNG shows the final state and not a fade. */
export async function foto(p: Page, nombre: string) {
  await p.waitForLoadState('networkidle').catch(() => {});
  await p.waitForTimeout(1000);
  return shot(p, nombre);
}
