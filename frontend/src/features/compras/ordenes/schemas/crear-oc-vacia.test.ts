import { describe, expect, it } from 'vitest';
import {
  CrearOrdenCompraVaciaSchema,
  DEFAULT_CREAR_OC_VACIA,
} from '@/features/compras/ordenes/schemas/crear-oc-vacia';

const VALIDO = {
  ...DEFAULT_CREAR_OC_VACIA,
  sucursalDestinoId: '00000003-0002-0000-0000-000000000001',
  proveedorId: '00000005-0001-0000-0000-000000000001',
  condicionesPagoId: '00000002-0003-0000-0000-000000000001',
  usoPrincipalId: '00000002-0004-0000-0000-000000000001',
  fechaDocumento: '2026-05-12',
};

describe('CrearOrdenCompraVaciaSchema', () => {
  it('happy path: valida con todos los IDs requeridos + MXN sin TC', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse(VALIDO);
    expect(result.success).toBe(true);
  });

  it('rechaza si falta sucursalDestinoId', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      sucursalDestinoId: '',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza UUID malformado en proveedorId', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      proveedorId: 'not-a-uuid',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza moneda no-ISO 4217 (no 3 letras mayúsculas)', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      moneda: 'mxn',
    });
    expect(result.success).toBe(false);
  });

  it('moneda USD requiere tipoCambio (FOC + CHECK constraint backend)', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      moneda: 'USD',
      tipoCambio: null,
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const tcError = result.error.issues.find((i) =>
        i.path.includes('tipoCambio'),
      );
      expect(tcError?.message).toMatch(/Tipo de cambio/i);
    }
  });

  it('moneda USD con tipoCambio > 0 valida', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      moneda: 'USD',
      tipoCambio: 17.5,
    });
    expect(result.success).toBe(true);
  });

  it('sinRequisicionPrevia=true sin motivo: rechaza con error en motivoSinRequisicion', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      sinRequisicionPrevia: true,
      motivoSinRequisicion: '',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const motivoError = result.error.issues.find((i) =>
        i.path.includes('motivoSinRequisicion'),
      );
      expect(motivoError?.message).toMatch(/motivo/i);
    }
  });

  it('sinRequisicionPrevia=true con motivo válido: valida', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      sinRequisicionPrevia: true,
      motivoSinRequisicion: 'Compra urgente autorizada por correo',
    });
    expect(result.success).toBe(true);
  });

  it('observaciones > 2000 caracteres: rechaza', () => {
    const result = CrearOrdenCompraVaciaSchema.safeParse({
      ...VALIDO,
      observaciones: 'a'.repeat(2001),
    });
    expect(result.success).toBe(false);
  });
});
