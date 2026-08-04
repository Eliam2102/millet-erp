import { describe, expect, it } from 'vitest';
import {
  BANDEJA_OC_PRESETS,
  detectarPresetActivo,
} from '@/features/compras/ordenes/lib/presets-bandeja-oc';
import {
  EstadoOrdenCompra,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import { DEFAULT_BANDEJA_OC_SEARCH } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

describe('BANDEJA_OC_PRESETS', () => {
  it('expone 6 presets state-based (sin user-aware todavía)', () => {
    expect(BANDEJA_OC_PRESETS).toHaveLength(6);
    expect(BANDEJA_OC_PRESETS.map((p) => p.id)).toEqual([
      'pendientes-n1',
      'pendientes-n2',
      'sin-recepcion',
      'sin-factura',
      'sin-pago',
      'cerradas',
    ]);
  });

  it('cada preset tiene id estable, label legible y search válido', () => {
    for (const p of BANDEJA_OC_PRESETS) {
      expect(p.id).toMatch(/^[a-z][a-z0-9-]+$/);
      expect(p.label.length).toBeGreaterThan(0);
      expect(p.search.page).toBe(1);
      expect(p.search.pageSize).toBe(50);
    }
  });

  it('preset "pendientes-n1" filtra por estado=EnAutorizacionJefeCompras', () => {
    const p = BANDEJA_OC_PRESETS.find((x) => x.id === 'pendientes-n1');
    expect(p?.search.estado).toBe(EstadoOrdenCompra.EnAutorizacionJefeCompras);
  });

  it('preset "sin-recepcion" combina Autorizada + SinRecepcion', () => {
    const p = BANDEJA_OC_PRESETS.find((x) => x.id === 'sin-recepcion');
    expect(p?.search.estado).toBe(EstadoOrdenCompra.Autorizada);
    expect(p?.search.subEstadoRecepcion).toBe(SubEstadoRecepcion.SinRecepcion);
  });
});

describe('detectarPresetActivo', () => {
  it('default search (sin filtros): no matchea ningún preset', () => {
    expect(detectarPresetActivo(DEFAULT_BANDEJA_OC_SEARCH)).toBeNull();
  });

  it('search exactamente igual a un preset: lo detecta', () => {
    const p = BANDEJA_OC_PRESETS[2]; // sin-recepcion
    expect(detectarPresetActivo(p.search)?.id).toBe(p.id);
  });

  it('search del preset + page distinto: NO matchea (preset reinicia el cursor)', () => {
    const p = BANDEJA_OC_PRESETS[0];
    const modificado = { ...p.search, page: 2 };
    expect(detectarPresetActivo(modificado)).toBeNull();
  });

  it('search del preset + filtro extra: NO matchea', () => {
    const p = BANDEJA_OC_PRESETS[0];
    const modificado = { ...p.search, proveedorId: 'p-extra' };
    expect(detectarPresetActivo(modificado)).toBeNull();
  });
});
