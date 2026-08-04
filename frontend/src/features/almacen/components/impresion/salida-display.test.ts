import { describe, expect, it } from 'vitest';
import {
  EstadoMovimiento,
  type SalidaDetalle,
  type SalidaLineaItem,
} from '@/features/almacen/api/types';
import {
  articuloLabel,
  ccMaquinaLabel,
  destinatarioLabel,
  requisicionLabel,
  rqRegularizadoraLabel,
  subAlmacenLabel,
} from './salida-display';

const SUB_ID = '00000008-0001-0000-0000-000000000001';
const ART_ID = '00000005-0002-0000-0000-000000000002';
const RQ_ID = '019e937e-aaaa-bbbb-cccc-0000005218ad';
const PERSONA_ID = '019e70cf-aaaa-bbbb-cccc-00000ace19c6';

function salida(over: Partial<SalidaDetalle> = {}): SalidaDetalle {
  return {
    id: 's-1',
    folio: 'M-SAL2026-000002',
    fechaMovimiento: '2026-06-01',
    subAlmacenId: SUB_ID,
    subAlmacenClave: 'ALM-CEN',
    subAlmacenNombre: 'Almacén Central',
    rqId: RQ_ID,
    rqFolio: 'MID2026-000123',
    esPorVale: false,
    valeBlobRef: null,
    personaDestinatariaId: PERSONA_ID,
    personaDestinatariaNombre: 'Juan Pérez',
    estado: EstadoMovimiento.Registrado,
    version: 1,
    observaciones: null,
    registradoAt: null,
    registradoPor: null,
    rqRegularizadoraId: null,
    rqRegularizadoraFolio: null,
    lineas: [
      {
        id: 'l-1',
        posicion: 1,
        articuloId: ART_ID,
        articuloClave: 'ACC-001',
        articuloDescripcion: 'Tornillo M6',
        cantidad: 2,
        unidadMedida: 'PZA',
        costoUnitarioMxn: 10,
        montoTotalMxn: 20,
        centroCostoId: null,
        centroCostoClave: null,
        centroCostoNombre: null,
        proyectoId: null,
      },
    ],
    ...over,
  };
}

describe('salida-display — nombres con fallback al id', () => {
  it('muestra nombre/clave resueltos, no el id crudo', () => {
    const s = salida();
    expect(subAlmacenLabel(s)).toBe('ALM-CEN · Almacén Central');
    expect(subAlmacenLabel(s)).not.toContain(SUB_ID);

    expect(requisicionLabel(s)).toBe('MID2026-000123');
    expect(destinatarioLabel(s)).toBe('Juan Pérez');
    expect(articuloLabel(s.lineas[0])).toBe('ACC-001 · Tornillo M6');
    expect(articuloLabel(s.lineas[0])).not.toContain(ART_ID);
  });

  it('cae al id crudo cuando el backend no resolvió el nombre', () => {
    const s = salida({
      subAlmacenClave: null,
      subAlmacenNombre: null,
      rqFolio: null,
      personaDestinatariaNombre: null,
      lineas: [
        {
          id: 'l-1',
          posicion: 1,
          articuloId: ART_ID,
          articuloClave: null,
          articuloDescripcion: null,
          cantidad: 2,
          unidadMedida: 'PZA',
          costoUnitarioMxn: 10,
          montoTotalMxn: 20,
          centroCostoId: null,
          proyectoId: null,
        },
      ],
    });
    expect(subAlmacenLabel(s)).toBe(SUB_ID);
    expect(requisicionLabel(s)).toBe(RQ_ID);
    expect(destinatarioLabel(s)).toBe(PERSONA_ID);
    expect(articuloLabel(s.lineas[0])).toBe(ART_ID);
  });

  it('usa solo el nombre cuando no hay clave', () => {
    expect(subAlmacenLabel(salida({ subAlmacenClave: null }))).toBe('Almacén Central');
  });

  it('destinatario sin id ni nombre cae a guion', () => {
    expect(
      destinatarioLabel(
        salida({ personaDestinatariaId: null, personaDestinatariaNombre: null }),
      ),
    ).toBe('—');
  });

  it('rq regularizadora: folio sobre id', () => {
    expect(
      rqRegularizadoraLabel(
        salida({ rqRegularizadoraId: 'rr-id', rqRegularizadoraFolio: 'MID2026-000999' }),
      ),
    ).toBe('MID2026-000999');
  });
});

describe('ccMaquinaLabel — CC-Máquina en el comprobante (Fase E PR5)', () => {
  const linea = (over: Partial<SalidaLineaItem>): SalidaLineaItem => ({
    ...salida().lineas[0],
    ...over,
  });

  it('resuelto: "clave — nombre"', () => {
    expect(
      ccMaquinaLabel(
        linea({
          centroCostoId: 'm-1',
          centroCostoClave: 'MCLC101',
          centroCostoNombre: 'Gantry',
        }),
      ),
    ).toBe('MCLC101 — Gantry');
  });

  it('irresoluble (id sin clave/nombre): "No catalogado"', () => {
    expect(
      ccMaquinaLabel(
        linea({ centroCostoId: 'm-roto', centroCostoClave: null, centroCostoNombre: null }),
      ),
    ).toBe('No catalogado');
  });

  it('sin CC: guion largo "—" (U+2014)', () => {
    expect(ccMaquinaLabel(linea({ centroCostoId: null }))).toBe('—');
  });
});
