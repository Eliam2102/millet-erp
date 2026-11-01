/**
 * Datos de la conciliación bancaria para las validaciones (cierre con diferencia y desemparejado sin motivo): movimientos de libros
 * sembrados por SQL en la BD de prueba y extracto BBVA en el perfil que lee el procesador (Fecha,Descripcion,Referencia,Cargo,Abono,Saldo).
 */
import { execFileSync } from 'node:child_process';
import { envCfg, env } from '../../_harness/session';

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[validaciones] seeding is only allowed in the test environment');
  const url = new URL(process.env.TEST_DATABASE_URL ?? envCfg.db.url);
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[validaciones] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}

const esc = (s: string) => s.replace(/'/g, "''");
const CONCEPTO = {
  pagoProveedor: '0000000b-1001-0000-0000-000000000001',
  cobroCliente: '0000000b-1001-0000-0000-000000000002',
  nomina: '0000000b-1001-0000-0000-000000000006',
  impuestos: '0000000b-1001-0000-0000-000000000005',
};
const SALDO_INICIAL = 1_250_000.0; // balance at 31/08/2026
export const MES = 9; // September 2026

export type Mov = { dia: number; desc: string; ref: string | null; sentido: 1 | 2; monto: number; concepto: string };
export type Linea = { dia: number; desc: string; ref: string | null; cargo: number | null; abono: number | null };

const CLIENTES = ['CONSTRUCTORA ALTAVISTA SA DE CV', 'EDIFICACIONES LOMAS VERDES', 'VENTANALES ROSALES SA DE CV', 'CRISTALERIA LOS PINOS',
  'FACHADAS DEL BAJIO SA DE CV', 'ALUMINIOS Y VIDRIOS DEL CENTRO', 'DESARROLLOS INMOBILIARIOS TAMARINDO', 'INTERIORES Y ACABADOS SAN ANGEL'];
const PROVEEDORES = ['HERRAJES INDUSTRIALES DEL NORTE', 'PERFILES Y ALUMINIO SALTILLO', 'ABRASIVOS TECNICOS QUERETARO', 'GASES INDUSTRIALES DEL BAJIO',
  'SELLADORES Y SILICONES MEXICANOS', 'EMPAQUES Y TARIMAS DE OCCIDENTE', 'TRANSPORTES LOGISTICOS PENINSULA', 'REFACCIONES PARA HORNOS INDUSTRIALES'];
const pad = (n: number, w = 2) => String(n).padStart(w, '0');
const r2 = (n: number) => Math.round(n * 100) / 100;

/** n deterministic, believable movements (unique amounts and references), none equal to $1,000, $406 or $50. */
export function genMovs(n: number, offset: number): Mov[] {
  const out: Mov[] = [];
  for (let i = 0; i < n; i++) {
    const k = i + offset;
    const dia = 1 + ((i * 7 + 3) % 28);
    const ingreso = i % 5 === 0 || i % 5 === 3;
    const monto = r2(3200 + ((k * 7919) % 91000) + ((k * 37) % 100) / 100 + 0.01 * (i % 7));
    const ref = `SPEI26${pad(MES)}${pad(dia)}${pad(offset % 100)}${pad(i, 2)}`;
    out.push(ingreso
      ? { dia, desc: `SPEI RECIBIDO ${CLIENTES[k % CLIENTES.length]}`, ref, sentido: 1, monto, concepto: CONCEPTO.cobroCliente }
      : { dia, desc: `PAGO SPEI ${PROVEEDORES[k % PROVEEDORES.length]}`, ref, sentido: 2, monto, concepto: CONCEPTO.pagoProveedor });
  }
  return out;
}

export const movALinea = (m: Mov): Linea => ({ dia: m.dia, desc: m.desc, ref: m.ref, cargo: m.sentido === 2 ? m.monto : null, abono: m.sentido === 1 ? m.monto : null });

/** BBVA statement CSV with running balance. Returns the text and the final balance. */
export function extracto(lineas: Linea[], saldoIni = SALDO_INICIAL) {
  const orden = [...lineas].sort((a, b) => a.dia - b.dia);
  let saldo = saldoIni;
  const rows = orden.map((l) => {
    saldo = r2(saldo + (l.abono ?? 0) - (l.cargo ?? 0));
    return [`${pad(l.dia)}/${pad(MES)}/2026`, `"${l.desc}"`, l.ref ?? '', l.cargo?.toFixed(2) ?? '', l.abono?.toFixed(2) ?? '', saldo.toFixed(2)].join(',');
  });
  return { texto: ['Fecha,Descripcion,Referencia,Cargo,Abono,Saldo', ...rows].join('\n'), saldoFinal: saldo, saldoIni, netoLineas: r2(saldo - saldoIni) };
}

export type Cuenta = { id: string; numero: string; ult4: string };
const cuentaId = (n: number) => `00000009-0b0b-4000-8000-${pad(n, 12)}`;

/** Creates a BBVA account (opening balance at 31/08/2026) and its book movements for September 2026. */
export function seedCuenta(n: number, numero: string, movs: Mov[]): Cuenta {
  const emp = sql('select id from compartido.empresas limit 1');
  if (!emp) throw new Error('[validaciones] no empresa in the test DB');
  const id = cuentaId(n);
  sql(`insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, moneda, perfil_extracto, activa, sucursal, titular, saldo_inicial, fecha_corte_saldo_inicial, motivo_saldo_inicial, version, created_at, updated_at)
       values ('${id}', '${emp}', 'BBVA', '${numero}', 'MXN', 'BBVA', true, 'Matriz Guadalajara', 'Vidrios Millet de México S.A. de C.V.', ${SALDO_INICIAL}, '2026-08-31', 'Saldo de apertura al corte de agosto', 1, now(), now());`);
  if (movs.length) {
    const vals = movs.map((m) => `(gen_random_uuid(), '${emp}', '${id}', ${m.sentido}, ${m.monto}, 'MXN', '2026-${pad(MES)}-${pad(m.dia)}', ${m.ref ? `'${esc(m.ref)}'` : 'null'}, '${m.concepto}', ${m.sentido === 1 ? 1 : 3}, 1, '00000000-0000-4000-8000-0000000000aa', now(), false, 1, now(), now())`);
    sql(`insert into tesoreria.movimiento_bancario (id, empresa_id, cuenta_bancaria_id, sentido, monto, moneda, fecha_valor, referencia_bancaria, concepto_id, estado_aplicacion, estado_conciliacion, creado_por, creado_en, fondos_verificados, version, created_at, updated_at) values ${vals.join(',\n')};`);
  }
  return { id, numero, ult4: numero.slice(-4) };
}

export const fmt = (n: number) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// ---------------------------------------------------------------- scenarios
// C4: everything matches but the books carry a $50.00 cheque that the bank has not shown yet -> difference $50.00.
export const DATOS_C4 = (() => {
  const base = genMovs(7, 300);
  const cheque: Mov = { dia: 28, desc: 'CHEQUE 004512 MENSAJERIA', ref: 'CH004512', sentido: 2, monto: 50, concepto: CONCEPTO.pagoProveedor };
  const ext1 = extracto(base.map(movALinea));
  // second statement (supplement) with the cheque cashed on the 29th; balance continues from the first one
  const ext2 = extracto([{ dia: 29, desc: 'CHEQUE PAGADO 004512', ref: 'CH004512', cargo: 50, abono: null }], ext1.saldoFinal);
  return { movs: [...base, cheque], ext1, ext2 };
})();

// C5: a simple month where everything matches by reference.
export const DATOS_C5 = (() => {
  const movs = genMovs(6, 400);
  return { movs, ...extracto(movs.map(movALinea)) };
})();

export const CUENTAS: Record<'c4' | 'c5', { n: number; numero: string }> = {
  c4: { n: 5, numero: '0118449024' },
  c5: { n: 6, numero: '0118441463' },
};

export function seedAll() {
  if (sql(`select count(*) from tesoreria.cuenta_bancaria where id = '${cuentaId(CUENTAS.c4.n)}'`) !== '0') return;
  seedCuenta(CUENTAS.c4.n, CUENTAS.c4.numero, DATOS_C4.movs);
  seedCuenta(CUENTAS.c5.n, CUENTAS.c5.numero, DATOS_C5.movs);
}

export const idConciliacion = (numero: string) =>
  sql(`select c.id from tesoreria.conciliacion c join tesoreria.cuenta_bancaria b on b.id = c.cuenta_bancaria_id where b.numero_cuenta = '${numero}'`);
