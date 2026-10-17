import { describe, expect, it } from 'vitest';
import {
  EstadoConteo,
  EstadoDevolucionProveedor,
  EstadoMovimiento,
  EstatusCatalogo,
  NivelReorden,
} from '@/features/almacen/api/types';
import {
  ESTADO_CONTEO,
  ESTADO_DEVOLUCION_PROVEEDOR,
  ESTADO_MOVIMIENTO,
  ESTATUS_CATALOGO,
  NIVEL_REORDEN,
} from './estado-variante';

describe('estado-variante Almacén', () => {
  it.each([
    ['movimiento', EstadoMovimiento, ESTADO_MOVIMIENTO],
    ['devolución', EstadoDevolucionProveedor, ESTADO_DEVOLUCION_PROVEEDOR],
    ['conteo', EstadoConteo, ESTADO_CONTEO],
    ['catálogo', EstatusCatalogo, ESTATUS_CATALOGO],
    ['nivel', NivelReorden, NIVEL_REORDEN],
  ] as const)('%s tiene entrada para cada valor del enum', (_n, enumObj, mapa) => {
    for (const v of Object.values(enumObj)) {
      const e = (mapa as Record<number, { etiqueta: string; variante: string }>)[v];
      expect(e?.etiqueta).toBeTruthy();
      expect(e?.variante).toBeTruthy();
    }
  });

  it('aprobación de conteo ya no es gris para Rechazado ni Aplicado', () => {
    expect(ESTADO_CONTEO[EstadoConteo.Rechazado].variante).toBe('danger');
    expect(ESTADO_CONTEO[EstadoConteo.Aplicado].variante).toBe('success');
  });
});
