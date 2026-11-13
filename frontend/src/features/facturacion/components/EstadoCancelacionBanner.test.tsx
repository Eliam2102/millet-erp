import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoCancelacionBanner } from './EstadoCancelacionBanner';
import type { ConsultarCancelacionResponse } from '@/features/facturacion/api/types';

const AVISO =
  'El SAT aún no refleja este CFDI (205). Puede tardar; reintenta en unos minutos (el SAT puede demorar hasta 72 h tras el timbrado). Reintento sugerido: 11/11/2026 13:00.';

const cancelacion = (extra: Partial<ConsultarCancelacionResponse> = {}): ConsultarCancelacionResponse => ({
  comprobanteId: 'f1',
  estadoComprobante: 'Timbrado',
  solicitudId: 's1',
  estadoSolicitud: 'Solicitada',
  motivoSat: '02',
  estatusSat: null,
  mensajeError: AVISO,
  solicitadaEn: '2026-11-11T18:00:00Z',
  resueltaEn: null,
  pendienteReintentoSat: true,
  ...extra,
});

afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<EstadoCancelacionBanner> — H-U31', () => {
  it('205 del SAT: «Pendiente de reintento», aviso con fecha y botón que reenvía la misma solicitud', async () => {
    useAuthStore.setState({ permisos: ['facturacion.cancelaciones.autorizar'] });
    const llamadas: { idem: boolean }[] = [];
    mswServer.use(
      http.post('*/api/v1/facturacion/comprobantes/f1/cancelar/reintentar', ({ request }) => {
        llamadas.push({ idem: !!request.headers.get('Idempotency-Key') });
        return HttpResponse.json({
          solicitudId: 's1',
          comprobanteId: 'f1',
          estadoComprobante: 'Cancelado',
          estadoSolicitud: 'Aceptada',
          estatusSat: 'Cancelado',
          pendienteReintentoSat: false,
        });
      }),
    );
    render(<EstadoCancelacionBanner comprobanteId="f1" cancelacion={cancelacion()} puedeRetirar={false} />, {
      wrapper: createQueryWrapper(),
    });

    expect(screen.getByText(/Pendiente de reintento/)).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Reintento sugerido: 11/11/2026 13:00');
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar ahora' }));
    await waitFor(() => expect(llamadas).toEqual([{ idem: true }]));
  });

  it('sin permiso de autorizar no hay botón; sin 205 no aparece', () => {
    const { rerender } = render(
      <EstadoCancelacionBanner comprobanteId="f1" cancelacion={cancelacion()} puedeRetirar={false} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByRole('button', { name: 'Reintentar ahora' })).not.toBeInTheDocument();

    useAuthStore.setState({ permisos: ['facturacion.cancelaciones.autorizar'] });
    rerender(
      <EstadoCancelacionBanner
        comprobanteId="f1"
        cancelacion={cancelacion({ estadoSolicitud: 'EnProceso', mensajeError: null, pendienteReintentoSat: false })}
        puedeRetirar={false}
      />,
    );
    expect(screen.queryByRole('button', { name: 'Reintentar ahora' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Pendiente de reintento/)).not.toBeInTheDocument();
  });
});
