import type { Auxiliar, CentroOpcion, CentroRef, DimensionCentro } from '../api/dimensiones-types';
import type { PartidaRequest } from '../api/polizas-types';
import type { CuentaOpcion } from '../components/dimensiones/CuentaSelector';
import { centavos } from './polizas';

/** Estado de una partida en captura (póliza manual o plantilla recurrente): importes como texto mientras se escriben. */
export interface PartidaForm {
  key: string;
  cuenta: CuentaOpcion | null;
  concepto: string;
  sucursalId: string;
  dim1: CentroOpcion | null;
  dim2: CentroOpcion | null;
  dim3: CentroOpcion | null;
  proyecto: string;
  cliente: Auxiliar | null;
  proveedor: Auxiliar | null;
  banco: Auxiliar | null;
  cargo: string;
  abono: string;
}

export const MAX_PARTIDAS = 500;

export function partidaVacia(sucursalId = ''): PartidaForm {
  return {
    key: crypto.randomUUID(), cuenta: null, concepto: '', sucursalId, dim1: null, dim2: null, dim3: null,
    proyecto: '', cliente: null, proveedor: null, banco: null, cargo: '', abono: '',
  };
}

/** El detalle trae la referencia mínima del centro; el selector solo pinta clave y nombre. */
export function centroDe(ref: CentroRef | null | undefined, nivel: DimensionCentro): CentroOpcion | null {
  return ref ? { id: ref.id, nivel, clave: ref.clave, nombre: ref.nombre, dim1Id: '', dim2Id: null, dim1Clave: '', dim2Clave: null } : null;
}

/** `1,234.50` → 1234.5; lo que no es número cuenta como cero. */
export function numeroImporte(s: string): number {
  const n = Number(s.replace(/,/g, ''));
  return Number.isFinite(n) ? n : 0;
}

export function totalesPartidas(partidas: readonly PartidaForm[]) {
  return {
    totalCargos: centavos(partidas.reduce((s, p) => s + numeroImporte(p.cargo), 0)),
    totalAbonos: centavos(partidas.reduce((s, p) => s + numeroImporte(p.abono), 0)),
  };
}

export function partidasACuerpo(partidas: readonly PartidaForm[]): PartidaRequest[] {
  return partidas.map<PartidaRequest>((p) => ({
    cuentaId: p.cuenta?.id ?? '',
    concepto: p.concepto.trim() || null,
    cargo: numeroImporte(p.cargo),
    abono: numeroImporte(p.abono),
    sucursalId: p.sucursalId,
    dim1Id: p.dim1?.id ?? null,
    dim2Id: p.dim2?.id ?? null,
    dim3Id: p.dim3?.id ?? null,
    proyecto: p.proyecto.trim() || null,
    clienteId: p.cliente?.id ?? null,
    proveedorId: p.proveedor?.id ?? null,
    cuentaBancariaId: p.banco?.id ?? null,
  }));
}
