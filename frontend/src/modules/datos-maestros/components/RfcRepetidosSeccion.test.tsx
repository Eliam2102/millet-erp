import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { RfcRepetidosSeccion } from '@/modules/datos-maestros/components/RfcRepetidosSeccion';
import { PERMISO_UNIFICAR_CLIENTES } from '@/modules/datos-maestros/api/sincronizacion-aw';

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' }, empresas: [], currentEmpresaId: 'e-1', permisos, errorMessage: null,
  });
}

const base = { tieneRfc: true, estatus: 'Activo', unificadoEnClienteId: null, ultimoPedidoAw: null };
const grupo = {
  rfc: 'ABC010101AB1', principalSugeridoId: 'c-1', motivoSugerencia: 'Ya tiene el RFC en el ERP.', resuelto: false,
  clientes: [
    { ...base, clienteId: 'c-1', referenciaAw: '5027', razonSocial: 'Vidrios Uno', pedidosAw: 4, ultimoPedidoAw: '2026-10-01T00:00:00Z', pedidosErp: 2, accionSugerida: 'Principal' },
    { ...base, clienteId: 'c-2', referenciaAw: '5028', razonSocial: 'Vidrios Dos', pedidosAw: 0, pedidosErp: 0, accionSugerida: 'Fusionar' },
    { ...base, clienteId: 'c-3', referenciaAw: '5029', razonSocial: 'Vidrios Tres', pedidosAw: 3, pedidosErp: 0, accionSugerida: 'Agrupar' },
  ],
};

function montar() {
  mswServer.use(http.get('*/clientes/rfc-repetidos', () => HttpResponse.json({ total: 1, offset: 0, limit: 10, items: [grupo] })));
  render(<RfcRepetidosSeccion />, { wrapper: createQueryWrapper() });
}

describe('<RfcRepetidosSeccion>', () => {
  beforeEach(() => setPermisos([PERMISO_UNIFICAR_CLIENTES]));

  it('muestra el grupo con el principal sugerido preseleccionado', async () => {
    montar();
    expect(await screen.findByText('ABC010101AB1')).toBeInTheDocument();
    expect(screen.getByText(/Ya tiene el RFC en el ERP/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Principal: Vidrios Uno' })).toBeChecked();
    expect(screen.getByRole('heading', { name: 'RFC repetidos (1)' })).toBeInTheDocument();
  });

  it('cambiar el principal cambia el texto del diálogo', async () => {
    montar();
    await screen.findByText('ABC010101AB1');
    fireEvent.click(screen.getByRole('button', { name: 'Unificar en el principal' }));
    expect(await screen.findByText(/El principal será Vidrios Uno/)).toBeInTheDocument();
    expect(screen.getByText(/se fusiona/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Principal: Vidrios Dos' }));
    fireEvent.click(screen.getByRole('button', { name: 'Unificar en el principal' }));
    expect(await screen.findByText(/El principal será Vidrios Dos/)).toBeInTheDocument();
    expect(screen.queryByText(/se fusiona/)).not.toBeInTheDocument();
  });

  it('unifica con rfc, principal y motivo, y envía Idempotency-Key', async () => {
    let body: unknown = null;
    let key: string | null = null;
    mswServer.use(http.post('*/clientes/rfc-repetidos/unificar', async ({ request }) => {
      body = await request.json();
      key = request.headers.get('Idempotency-Key');
      return HttpResponse.json({ ...grupo, resuelto: true });
    }));
    montar();
    await screen.findByText('ABC010101AB1');
    fireEvent.click(screen.getByRole('button', { name: 'Unificar en el principal' }));
    const confirmar = await screen.findByRole('button', { name: 'Confirmar unificación' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'Mismo cliente fiscal' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(body).toEqual({ rfc: 'ABC010101AB1', principalClienteId: 'c-1', motivo: 'Mismo cliente fiscal' }));
    expect(key).toBeTruthy();
  });

  it('sin permiso el botón queda deshabilitado', async () => {
    setPermisos([]);
    montar();
    await screen.findByText('ABC010101AB1');
    expect(screen.getByRole('button', { name: 'Unificar en el principal' })).toBeDisabled();
  });

  it('vacío y error', async () => {
    mswServer.use(http.get('*/clientes/rfc-repetidos', () => HttpResponse.json({ total: 0, offset: 0, limit: 10, items: [] })));
    render(<RfcRepetidosSeccion />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('No hay RFC repetidos pendientes.')).toBeInTheDocument();
  });
});
