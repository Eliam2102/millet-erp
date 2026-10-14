import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useCorteCaja } from '@/features/facturacion/api/useCajaSesiones';
import type { CorteCajaResponse } from '@/features/facturacion/api/types';

const corteStub: CorteCajaResponse = {
  encabezado: {
    sesionId: 'ses-1',
    caja: 'Caja Mostrador',
    sucursal: 'MID — Mérida',
    cajeroUsuarioId: 'usr-1',
    cajero: 'Ana Cajera',
    estado: 'Cerrada',
    diaOperacion: '2026-10-13',
    fechaApertura: '2026-10-13T14:00:00Z',
    fechaCierre: '2026-10-13T23:00:00Z',
    fondoApertura: 500,
    efectivoEsperado: 1544,
    efectivoContado: 1544,
    diferencia: 0,
    cierreExtemporaneo: false,
    notasCierre: null,
  },
  porFormaPago: [
    { formaPago: '01', descripcion: 'Efectivo', cobros: 1, montoSistema: 1544, montoDeclarado: 1544 },
  ],
  reporte: {
    titulo: 'Corte de caja · Caja Mostrador · 2026-10-13',
    generadoEn: '2026-10-13T23:01:00Z',
    filtrosAplicados: { Cajero: 'Ana Cajera' },
    columnas: [{ clave: 'importe', etiqueta: 'Importe en caja', tipo: 'moneda' }],
    filas: [],
    totales: { importe: 1544 },
  },
};

describe('useCorteCaja (FIX-12)', () => {
  it('consulta el corte de la sesión', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas/sesiones/:id/corte', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json(corteStub);
      }),
    );
    const { result } = renderHook(() => useCorteCaja('ses-1'), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVista).toContain('/cajas/sesiones/ses-1/corte');
    expect(result.current.data?.encabezado.cajero).toBe('Ana Cajera');
  });

  it('no dispara sin sesión', () => {
    const { result } = renderHook(() => useCorteCaja(''), { wrapper: createQueryWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
  });
});
