import { describe, expect, it } from 'vitest';
import { formatearFechaDocumento } from './fecha-documento';
import { TipoDocumentoTrazabilidad as T } from './types';

describe('fecha de trazabilidad', () => {
  it.each(['2026-10-10', '2026-10-10T00:00:00+00:00'])('conserva el día de la factura de proveedor (%s)', (fecha) => {
    expect(formatearFechaDocumento(fecha, T.FacturaProveedor)).toBe('10/10/2026');
  });
  it('localiza en México los instantes de otros documentos', () => {
    expect(formatearFechaDocumento('2026-10-10T02:00:00Z', T.PagoProveedor)).toBe('9/10/2026');
  });
});
