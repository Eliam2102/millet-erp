import { describe, expect, it } from 'vitest';
import { CajaChicaLineaSchema } from '@/features/cxp/schemas/comprobaciones';
import { ComprobacionLineaSchema } from '@/features/cxp/schemas/viaticos';

const UUID = '11111111-1111-4111-8111-111111111111';

const cajaChica = {
  cfdiRecibidoId: null,
  uuidCfdi: null,
  proveedorId: UUID,
  folioProveedor: null,
  serieProveedor: null,
  fechaCfdi: '2026-10-20',
  subtotal: 100,
  descuentos: 0,
  impuestosTrasladados: 16,
  retenciones: 0,
  total: 116,
  fechaVencimiento: '2026-11-20',
  concepto: null,
  clasificacionNoDeducible: null as string | null,
};

const viatico = {
  cfdiRecibidoId: null,
  uuidCfdi: null,
  proveedorId: null,
  folioProveedor: null,
  fechaGasto: '2026-10-20',
  subtotal: 100,
  impuestosTrasladados: 16,
  retenciones: 0,
  total: 116,
  moneda: 'MXN',
  concepto: 'Alimentos',
  esTicketNoFiscal: true,
  clasificacionNoDeducible: null as string | null,
};

describe('clasificación no deducible por línea (P8.6)', () => {
  it('caja chica: un ticket sin clasificación no pasa; con clasificación sí', () => {
    expect(CajaChicaLineaSchema.safeParse(cajaChica).success).toBe(false);
    expect(CajaChicaLineaSchema.safeParse({ ...cajaChica, clasificacionNoDeducible: 'COMIDAS' }).success).toBe(true);
  });

  it('caja chica: línea con CFDI es deducible por omisión, y marcada sin elegir no pasa', () => {
    const conCfdi = { ...cajaChica, cfdiRecibidoId: UUID, uuidCfdi: UUID };
    expect(CajaChicaLineaSchema.safeParse(conCfdi).success).toBe(true);
    expect(CajaChicaLineaSchema.safeParse({ ...conCfdi, clasificacionNoDeducible: '' }).success).toBe(false);
    expect(CajaChicaLineaSchema.safeParse({ ...conCfdi, clasificacionNoDeducible: 'PROPINAS' }).success).toBe(true);
  });

  it('viáticos: ticket no fiscal exige clasificación; línea fiscal no', () => {
    expect(ComprobacionLineaSchema.safeParse(viatico).success).toBe(false);
    expect(ComprobacionLineaSchema.safeParse({ ...viatico, clasificacionNoDeducible: 'VIATICOS_SIN_CFDI' }).success).toBe(true);
    expect(ComprobacionLineaSchema.safeParse({ ...viatico, esTicketNoFiscal: false, uuidCfdi: UUID, proveedorId: UUID }).success).toBe(true);
  });
});
