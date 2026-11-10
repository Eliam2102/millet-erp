import { describe, expect, it } from 'vitest';
import { RegistrarRecepcionFacturaSchema } from './recepcion';

/**
 * Tests del refine de vínculo fiscal obligatorio en variante A (§5.4):
 * la recepción con factura exige cfdiRecibidoId O cfdiUuidFiscal.
 */

const filaIncluida = {
  lineaOcId: '019e0000-0000-7000-8000-000000000001',
  articuloId: '019e0000-0000-7000-8000-000000000002',
  posicion: 1,
  unidadMedida: 'PZA',
  cantidadSolicitada: 10,
  cantidadYaRecibida: 0,
  pendiente: 10,
  maximo: 10,
  incluida: true,
  cantidad: 10,
  cantidadDanada: 0,
  ubicacionId: '019e0000-0000-7000-8000-000000000003',
};

const base = {
  ordenCompraId: '019e0000-0000-7000-8000-0000000000aa',
  subAlmacenId: '019e0000-0000-7000-8000-0000000000bb',
  fechaMovimiento: '2026-07-14',
  observaciones: null,
  filas: [filaIncluida],
};

describe('RegistrarRecepcionFacturaSchema — vínculo fiscal obligatorio', () => {
  it('acepta con cfdiRecibidoId y sin folio fiscal', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      cfdiUuidFiscal: null,
    });
    expect(r.success).toBe(true);
  });

  it('acepta con folio fiscal (UUID SAT) y sin cfdiRecibidoId', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: 'AD662D33-6934-459C-A128-BDF0393E0062',
    });
    expect(r.success).toBe(true);
  });

  it('rechaza sin cfdiRecibidoId ni folio fiscal, con error en cfdiRecibidoId', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: null,
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(
        r.error.issues.some((i) => i.path.join('.') === 'cfdiRecibidoId'),
      ).toBe(true);
    }
  });

  // B9 · U-4: recibir de más lo rechaza el servidor (RECEPCION_EXCEDE_PENDIENTE) y abre la incidencia.
  // Si la pantalla lo frenara antes, la petición no saldría y la incidencia nunca se abriría.
  it.each([100, 100.5, 101])('pendiente 100, cantidad %s: la pantalla deja enviar y decide el servidor', (cantidad) => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [{ ...filaIncluida, pendiente: 100, maximo: 100, cantidad }],
    });
    expect(r.success).toBe(true);
  });

  it('rechaza folio fiscal malformado', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: 'no-es-un-uuid',
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(
        r.error.issues.some((i) => i.path.join('.') === 'cfdiUuidFiscal'),
      ).toBe(true);
    }
  });
});

describe('RegistrarRecepcionFacturaSchema — cantidad dañada (ALM-04)', () => {
  const conFila = (extra: object) =>
    RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [{ ...filaIncluida, ...extra }],
    });

  it('rechaza dañada mayor que la cantidad recibida', () => {
    const r = conFila({ cantidadDanada: 11, motivoDano: 'Rota' });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.map((i) => i.message)).toContain(
        'La cantidad dañada no puede ser mayor que la cantidad recibida.',
      );
    }
  });

  it('rechaza dañada sin motivo', () => {
    const r = conFila({ cantidadDanada: 2, motivoDano: '  ' });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.map((i) => i.message)).toContain('Indica el motivo del daño.');
    }
  });

  it('acepta dañada cero sin motivo y dañada con motivo', () => {
    expect(conFila({ cantidadDanada: 0 }).success).toBe(true);
    expect(conFila({ cantidadDanada: 2, motivoDano: 'Hojas rotas', evidenciaBlobRef: 'blob://evidencia/dano.jpg' }).success).toBe(true);
  });
});

describe('RegistrarRecepcionFacturaSchema — rechazo en el acto y evidencia (G-10)', () => {
  const conFila = (extra: object, filas: object[] = []) =>
    RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [{ ...filaIncluida, ...extra }, ...filas],
    });
  const mensajes = (r: ReturnType<typeof conFila>) => (r.success ? [] : r.error.issues.map((i) => i.message));
  const evidencia = 'Adjunta una foto o documento como evidencia del daño o del rechazo.';

  it('dañado sin evidencia se rechaza; con evidencia pasa', () => {
    expect(mensajes(conFila({ cantidadDanada: 2, motivoDano: 'Golpe' }))).toContain(evidencia);
    expect(conFila({ cantidadDanada: 2, motivoDano: 'Golpe', evidenciaBlobRef: 'blob://x' }).success).toBe(true);
  });

  it('rechazado sin evidencia o sin motivo se rechaza', () => {
    expect(mensajes(conFila({ cantidad: 7, cantidadRechazada: 3, motivoRechazo: 'No es lo pedido' }))).toContain(evidencia);
    expect(mensajes(conFila({ cantidad: 7, cantidadRechazada: 3, evidenciaBlobRef: 'blob://x' }))).toContain('Indica el motivo del rechazo.');
    expect(conFila({ cantidad: 7, cantidadRechazada: 3, motivoRechazo: 'No es lo pedido', evidenciaBlobRef: 'blob://x' }).success).toBe(true);
  });

  it('una partida rechazada por completo no pide rack y no puede exceder lo pendiente', () => {
    const total = { cantidad: 0, cantidadRechazada: 10, motivoRechazo: 'Medida distinta', evidenciaBlobRef: 'blob://x', ubicacionId: null };
    const otra = { ...filaIncluida, lineaOcId: '019e0000-0000-7000-8000-000000000009', ubicacionId: '019e0000-0000-7000-8000-000000000003' };
    expect(conFila(total, [otra]).success).toBe(true);
    expect(mensajes(conFila({ ...total, cantidadRechazada: 11 }, [otra]))).toContain('Excede lo pendiente de la orden de compra (10).');
  });

  it('si todas las partidas se rechazan no hay recepción que registrar', () => {
    const total = { cantidad: 0, cantidadRechazada: 10, motivoRechazo: 'Medida distinta', evidenciaBlobRef: 'blob://x', ubicacionId: null };
    expect(mensajes(conFila(total))).toContain('Todas las partidas están rechazadas: no hay recepción que registrar.');
  });
});

describe('RegistrarRecepcionFacturaSchema — medida de la línea de OC (E1)', () => {
  const medida = { varianteId: 'v-1', anchoMm: 2440, altoMm: 3660, areaHojaM2: 8.9304 };
  const conFila = (extra: object) =>
    RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      // 20 hojas pendientes = 178.608 m².
      filas: [{ ...filaIncluida, unidadMedida: 'M2', pendiente: 178.608, maximo: 178.608, medida, cantidad: 20, lote: 'L-2610-01', ...extra }],
    });
  const mensajes = (r: ReturnType<typeof conFila>) => (r.success ? [] : r.error.issues.map((i) => i.message));

  it('el lote del proveedor es obligatorio con medida (artículo ligado a A+W) y máximo 50 caracteres', () => {
    expect(mensajes(conFila({ lote: null }))).toContain('Captura el lote del proveedor.');
    expect(mensajes(conFila({ lote: '   ' }))).toContain('Captura el lote del proveedor.');
    expect(mensajes(conFila({ lote: 'L'.repeat(51) }))).toContain('Máximo 50 caracteres');
    expect(conFila({ lote: ' L-9 ' }).success).toBe(true);
  });

  it('sin medida el lote es opcional', () => {
    expect(conFila({ medida: null, unidadMedida: 'PZA', pendiente: 20, maximo: 20, lote: null }).success).toBe(true);
  });

  it('acepta hojas enteras hasta el pendiente en hojas, con dañadas enteras', () => {
    expect(conFila({}).success).toBe(true);
    expect(conFila({ cantidad: 5, cantidadDanada: 2, motivoDano: 'Hojas rotas', evidenciaBlobRef: 'blob://x' }).success).toBe(true);
  });

  it('B9: 21 hojas con 20 pendientes se envían; el servidor rechaza y abre la incidencia', () => {
    expect(conFila({ cantidad: 21 }).success).toBe(true);
  });

  it('lo rechazado en el acto no puede pasar lo pendiente (el servidor no lo compara)', () => {
    expect(mensajes(conFila({ cantidad: 18, cantidadRechazada: 3, motivoRechazo: 'Rotas', evidenciaBlobRef: 'blob://x' }))).toContain(
      'Excede lo pendiente de la orden de compra (20 hojas). Almacén recibe exacto.',
    );
  });

  it('rechaza hojas fraccionarias y dañadas fraccionarias o mayores que lo recibido', () => {
    expect(mensajes(conFila({ cantidad: 2.5 }))).toContain('Con medida, captura la cantidad en hojas enteras.');
    expect(mensajes(conFila({ cantidad: 5, cantidadDanada: 1.5, motivoDano: 'x' }))).toContain(
      'Las hojas dañadas son enteras y no pueden exceder las recibidas.',
    );
    expect(mensajes(conFila({ cantidad: 5, cantidadDanada: 6, motivoDano: 'x' }))).toContain(
      'Las hojas dañadas son enteras y no pueden exceder las recibidas.',
    );
  });

  it('el motivo del daño sigue siendo obligatorio con hojas dañadas', () => {
    expect(mensajes(conFila({ cantidad: 5, cantidadDanada: 1, motivoDano: ' ' }))).toContain('Indica el motivo del daño.');
  });
});

describe('RegistrarRecepcionFacturaSchema — remisión y motivo de recepción repetida (G-3)', () => {
  const conCfdi = { ...base, cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc', cfdiUuidFiscal: null };

  it('acepta remisión y motivo opcionales', () => {
    expect(RegistrarRecepcionFacturaSchema.safeParse({ ...conCfdi, remision: 'R-55', motivoDuplicado: 'Segundo camión' }).success).toBe(true);
    expect(RegistrarRecepcionFacturaSchema.safeParse(conCfdi).success).toBe(true);
  });

  it('rechaza remisión de más de 50 caracteres y motivo de más de 500', () => {
    expect(RegistrarRecepcionFacturaSchema.safeParse({ ...conCfdi, remision: 'R'.repeat(51) }).success).toBe(false);
    expect(RegistrarRecepcionFacturaSchema.safeParse({ ...conCfdi, motivoDuplicado: 'm'.repeat(501) }).success).toBe(false);
  });
});
