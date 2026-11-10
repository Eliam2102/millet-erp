import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AvisosRetornoAwSection } from '@/modules/datos-maestros/components/AvisosRetornoAwSection';

const resumen = {
  pendientes: 2, detenidos: 0, masAntiguoEnUtc: '2026-11-09T06:00:00Z',
  masAntiguoTipoEvento: 'facturacion.factura-venta.timbrada.v1', masAntiguoIntentos: 5,
  masAntiguoProximoIntentoUtc: '2026-11-09T11:00:00Z', masAntiguoUltimoError: 'SQL 53: A network-related error', alerta: true,
};

describe('<AvisosRetornoAwSection>', () => {
  it('muestra pendientes, el más antiguo con su error y la alerta de más de 4 h', async () => {
    mswServer.use(http.get('*/integraciones/aw/retorno/pendientes/resumen', () => HttpResponse.json(resumen)));
    render(<AvisosRetornoAwSection />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Más de 4 h sin A+W')).toBeInTheDocument();
    expect(screen.getByText('SQL 53: A network-related error')).toBeInTheDocument();
    expect(screen.getByText(/5 intentos/)).toBeInTheDocument();
  });

  it('«Reprocesar dead-letter» llama al endpoint con Idempotency-Key', async () => {
    let llave: string | null = null;
    mswServer.use(
      http.get('*/integraciones/aw/retorno/pendientes/resumen', () => HttpResponse.json({ ...resumen, pendientes: 0, alerta: false })),
      http.post('*/integraciones/aw/retorno/dead-letter/reprocesar', ({ request }) => {
        llave = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ leidos: 3, aplicados: 3, pendientes: 0, invalidos: 0, fallidos: 0 });
      }),
    );
    render(<AvisosRetornoAwSection />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Al día')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Reprocesar dead-letter' }));
    await waitFor(() => expect(llave).toBeTruthy());
  });
});
