import { describe, expect, it } from 'vitest';
import { resultadoEmision } from './resultado-emision';

const respuesta = { id: 'f-1', folio: 'FA-1', total: 4640, version: 1, estado: 'Timbrado', uuid: 'uuid-prueba' };

describe('Resultado real de emisión', () => {
  it('solo anuncia éxito cuando está timbrada y tiene UUID', () => {
    expect(resultadoEmision(respuesta)).toMatchObject({ tipo: 'success', descripcion: 'UUID uuid-prueba' });
    expect(resultadoEmision({ ...respuesta, uuid: null }).tipo).toBe('warning');
    expect(resultadoEmision({ ...respuesta, uuid: ' ' }).tipo).toBe('warning');
  });
  it.each(['403', '400', '305'])('muestra rechazo %s del PAC y permite reintentar', (codigo) => {
    expect(resultadoEmision({ ...respuesta, estado: 'TimbradoFallido', uuid: null,
      timbradoErrorCodigo: codigo, timbradoErrorMensaje: 'Rechazo del PAC',
    })).toMatchObject({ tipo: 'error', descripcion: `${codigo}: Rechazo del PAC`, reintentable: true });
  });
  it.each(['TimbradoEnProceso', 'PendientePedimento'])('no confunde %s con éxito ni invita a duplicar el timbrado', (estado) => {
    expect(resultadoEmision({ ...respuesta, estado, uuid: null })).toMatchObject({ tipo: 'warning', reintentable: false });
  });
  it('M19: sin saldo del proveedor de timbrado lo dice en español y sin código técnico', () => {
    const r = resultadoEmision({ ...respuesta, estado: 'TimbradoFallido', uuid: null,
      timbradoErrorCodigo: 'PAC_SIN_SALDO',
      timbradoErrorMensaje: 'El proveedor de timbrado no tiene saldo para timbrar (disponible: 0, requerido: 1). No es un error de los datos fiscales: avisa a Sistemas para que recargue los timbres y después reintenta el timbrado.',
    });
    expect(r).toMatchObject({ tipo: 'error', reintentable: true });
    expect(r.titulo).toContain('el proveedor de timbrado no tiene saldo');
    expect(r.descripcion).not.toMatch(/PAC_SIN_SALDO|Sorry|datos fiscales con/);
  });
});
