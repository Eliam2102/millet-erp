import type { CorridaResponse, EstadoCorridaNombre, PasivoParaCorridaResponse } from '../api/types';
import type { TonoEstado } from './estados-ui';

export const ESTADO_CORRIDA_UI: Record<EstadoCorridaNombre, { tono: TonoEstado; texto: string }> = {
  Borrador: { tono: 'neutral', texto: 'Borrador' },
  EnAutorizacion: { tono: 'warning', texto: 'En autorización' },
  Autorizada: { tono: 'success', texto: 'Autorizada' },
  Ejecutada: { tono: 'info', texto: 'Ejecutada' },
  Cerrada: { tono: 'success', texto: 'Cerrada' },
  Rechazada: { tono: 'danger', texto: 'Rechazada' },
  Cancelada: { tono: 'neutral', texto: 'Cancelada' },
};

/** Qué falta para autorizar, en lenguaje de usuario; null si ya no aplica. */
export function faltaParaAutorizar(c: Pick<CorridaResponse, 'estado' | 'firmasRequeridas' | 'firmasRecibidas' | 'rolRequerido' | 'firmas'>): string | null {
  if (c.estado !== 'EnAutorizacion') return null;
  const faltan = Math.max(0, c.firmasRequeridas - c.firmasRecibidas);
  const tieneRol =
    c.rolRequerido == null ||
    c.firmas.some((f) => f.roles.split(',').some((r) => r.toLowerCase() === c.rolRequerido));
  const partes: string[] = [];
  if (faltan > 0) partes.push(faltan === 1 ? 'falta 1 firma' : `faltan ${faltan} firmas`);
  if (!tieneRol) partes.push(`al menos una firma debe ser del rol «${c.rolRequerido}»`);
  return partes.length > 0 ? partes.join('; ') : null;
}

export interface PagoPendiente {
  proveedorId: string;
  facturas: string[];
  neto: number;
}

/** Líneas aún sin pagar agrupadas por proveedor: cada grupo es un pago en la banca (un pago = un proveedor). */
export function pagosPendientes(lineas: Pick<CorridaResponse['lineas'][number], 'facturaProveedorId' | 'proveedorId' | 'neto' | 'ejecutada'>[]): PagoPendiente[] {
  const mapa = new Map<string, PagoPendiente>();
  for (const l of lineas) {
    if (l.ejecutada) continue;
    const g = mapa.get(l.proveedorId) ?? { proveedorId: l.proveedorId, facturas: [], neto: 0 };
    g.facturas.push(l.facturaProveedorId);
    g.neto = Math.round((g.neto + l.neto) * 100) / 100;
    mapa.set(l.proveedorId, g);
  }
  return [...mapa.values()];
}

export interface TotalesSeleccion {
  bruto: number;
  retenciones: number;
  neto: number;
  proveedores: number;
}

/** Totales de los pasivos elegidos (suma en centavos para no arrastrar error de punto flotante). */
export function totalesSeleccion(pasivos: PasivoParaCorridaResponse[], elegidos: ReadonlySet<string>): TotalesSeleccion {
  const sel = pasivos.filter((p) => elegidos.has(p.facturaProveedorId));
  const suma = (f: (p: PasivoParaCorridaResponse) => number) =>
    sel.reduce((a, p) => a + Math.round(f(p) * 100), 0) / 100;
  return {
    bruto: suma((p) => p.bruto),
    retenciones: suma((p) => p.retenciones),
    neto: suma((p) => p.neto),
    proveedores: new Set(sel.map((p) => p.proveedorId)).size,
  };
}
