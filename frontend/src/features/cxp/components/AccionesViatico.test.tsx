import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoSolicitudViaticos } from '@/features/cxp/api/types';
import { AccionesViatico, MOTIVO_EXCESO_SIN_AUTORIZAR, MOTIVO_SIN_EMPLEADO, type SolicitudAccionable } from './AccionesViatico';

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

  it('«Devolver para corregir» exige comentario y envía POST /devolver con la versión', async () => {
    meConEmpleado('e-1');
    const llamada = vi.fn();
    mswServer.use(
      http.post('*/api/v1/cuentas-por-pagar/viaticos/v-1/devolver', async ({ request }) => {
        llamada(await request.json(), request.headers.get('X-Expected-Version'));
        return HttpResponse.json({ id: 'v-1', estado: 9, version: 4 });
      }),
    );
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.Solicitada)} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('button', { name: 'Devolver para corregir' }));
    await screen.findByLabelText('Comentario para el solicitante');
    const botones = screen.getAllByRole('button', { name: 'Devolver para corregir' });
    const enDialogo = botones[botones.length - 1]!;
    expect(enDialogo).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Comentario para el solicitante'), {
      target: { value: 'El monto no cuadra con la agenda' },
    });
    fireEvent.click(enDialogo);
    await waitFor(() => expect(llamada).toHaveBeenCalledWith({ comentario: 'El monto no cuadra con la agenda' }, '3'));
  });

  it('una solicitud devuelta ofrece «Corregir y reenviar» al solicitante y no la autorización', () => {
    meConEmpleado('e-1');
    useAuthStore.setState({
      permisos: ['cuentas_por_pagar.viaticos.autorizar-jefe', 'cuentas_por_pagar.viaticos.solicitar'],
    });
    render(<AccionesViatico s={solicitud(EstadoSolicitudViaticos.Devuelta)} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Corregir y reenviar' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Autorizar como jefe' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).toBeNull();
  });
  // M-449: la comprobación fuera de política no se libera hasta que Dirección de Finanzas autoriza el exceso.
  describe('comprobación fuera de política', () => {
    const fuera = (autorizado: string | null): SolicitudAccionable => ({
      ...solicitud(EstadoSolicitudViaticos.ComprobacionCapturada),
      comprobacionFueraDePolitica: true,
      excesoComprobacion: 500,
      excesoAutorizadoPor: autorizado,
    });

    it('sin autorización: «Liberar» deshabilitado con su motivo y DF ve «Autorizar exceso» con el monto', async () => {
      meConEmpleado('e-1');
      useAuthStore.setState({ permisos: ['cuentas_por_pagar.viaticos.liberar', 'cuentas_por_pagar.viaticos.autorizar-df'] });
      render(<AccionesViatico s={fuera(null)} />, { wrapper: createQueryWrapper() });

      const liberar = screen.getByRole('button', { name: 'Liberar' });
      expect(liberar).toBeDisabled();
      expect(liberar).toHaveAttribute('title', MOTIVO_EXCESO_SIN_AUTORIZAR);
      fireEvent.click(screen.getByRole('button', { name: 'Autorizar exceso' }));
      expect(await screen.findByRole('alertdialog')).toHaveTextContent('500.00');
    });

    it('con el exceso autorizado se puede liberar y ya no se ofrece autorizar', () => {
      meConEmpleado('e-1');
      useAuthStore.setState({ permisos: ['cuentas_por_pagar.viaticos.liberar', 'cuentas_por_pagar.viaticos.autorizar-df'] });
      render(<AccionesViatico s={fuera('df-1')} />, { wrapper: createQueryWrapper() });

      expect(screen.getByRole('button', { name: 'Liberar' })).toBeEnabled();
      expect(screen.queryByRole('button', { name: 'Autorizar exceso' })).toBeNull();
    });

    it('sin permiso de Dirección de Finanzas no aparece «Autorizar exceso»', () => {
      meConEmpleado('e-1');
      useAuthStore.setState({ permisos: ['cuentas_por_pagar.viaticos.liberar'] });
      render(<AccionesViatico s={fuera(null)} />, { wrapper: createQueryWrapper() });

      expect(screen.queryByRole('button', { name: 'Autorizar exceso' })).toBeNull();
    });
  });
});
