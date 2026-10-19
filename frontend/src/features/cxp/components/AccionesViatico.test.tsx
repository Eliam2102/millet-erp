import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoSolicitudViaticos } from '@/features/cxp/api/types';
import { AccionesViatico, MOTIVO_SIN_EMPLEADO, type SolicitudAccionable } from './AccionesViatico';

const solicitud = (estado: EstadoSolicitudViaticos): SolicitudAccionable => ({
  id: 'v-1',
  version: 3,
  estado,
  destino: 'Monterrey',
  montoSolicitado: 1500,
  excedePolitica: false,
});

function meConEmpleado(empleadoId: string | null) {
  mswServer.use(
    http.get('*/api/auth/me', () => HttpResponse.json({ userId: 'u-test', permisos: [], empleadoId })),
  );
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_pagar.viaticos.autorizar-jefe',
      'cuentas_por_pagar.viaticos.marcar-pagado',
    ],
  });
});

afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, user: null, permisos: [] }));

describe('<AccionesViatico>', () => {
  it('deshabilita «Autorizar como jefe» con el motivo cuando el usuario no tiene empleado', async () => {
    meConEmpleado(null);
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.Solicitada)} />, {
      wrapper: createQueryWrapper(),
    });
    const boton = screen.getByRole('button', { name: 'Autorizar como jefe' });
    await waitFor(() => expect(boton).toBeDisabled());
    expect(boton).toHaveAttribute('title', MOTIVO_SIN_EMPLEADO);
    expect(boton).toHaveAccessibleDescription(MOTIVO_SIN_EMPLEADO);
  });

  it('habilita «Autorizar como jefe» cuando el usuario tiene empleado', async () => {
    meConEmpleado('e-1');
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.Solicitada)} />, {
      wrapper: createQueryWrapper(),
    });
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.getByRole('button', { name: 'Autorizar como jefe' })).toBeEnabled();
  });

  it('«Marcar pagado» pide confirmación con el monto y no dispara hasta confirmar', async () => {
    meConEmpleado('e-1');
    const llamadas = vi.fn();
    mswServer.use(
      http.post('*/api/v1/cuentas-por-pagar/viaticos/v-1/marcar-anticipo-pagado', () => {
        llamadas();
        return HttpResponse.json({});
      }),
    );
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.AutorizadaPorJefe)} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('button', { name: 'Marcar pagado' }));
    const dialogo = await screen.findByRole('alertdialog');
    expect(dialogo).toHaveTextContent('Monterrey');
    expect(dialogo).toHaveTextContent('1,500.00');
    expect(llamadas).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(llamadas).not.toHaveBeenCalled();
  });

  it('«Rechazar» abre el diálogo de motivo (5-500) en vez de window.prompt', async () => {
    meConEmpleado('e-1');
    const prompt = vi.spyOn(window, 'prompt');
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.Solicitada)} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
    const confirmar = await screen.findByRole('button', { name: 'Rechazar solicitud' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: 'Sin presupuesto' } });
    expect(confirmar).toBeEnabled();
    expect(prompt).not.toHaveBeenCalled();
  });
});
