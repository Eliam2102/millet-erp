import { describe, expect, it } from 'vitest';
import { etiquetaEstadoCancelacion } from './cancelacion-sat';

describe('etiquetaEstadoCancelacion (H-U31)', () => {
  it('con 205 del SAT muestra «Pendiente de reintento»', () => {
    expect(etiquetaEstadoCancelacion({ estadoSolicitud: 'Solicitada', pendienteReintentoSat: true })).toBe(
      'Pendiente de reintento',
    );
  });
  it('sin 205 usa la etiqueta del estado', () => {
    expect(etiquetaEstadoCancelacion({ estadoSolicitud: 'PendienteAutorizacion' })).toBe('Pendiente de autorización');
    expect(etiquetaEstadoCancelacion({ estadoSolicitud: null })).toBe('');
  });
});
