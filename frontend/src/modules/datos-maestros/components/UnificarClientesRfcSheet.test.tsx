import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse, type JsonBodyType } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { UnificarClientesRfcSheet } from '@/modules/datos-maestros/components/UnificarClientesRfcSheet';
import { PERMISO_UNIFICAR_CLIENTES, type CambioMaestroAw } from '@/modules/datos-maestros/api/sincronizacion-aw';

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' }, empresas: [], currentEmpresaId: 'e-1', permisos, errorMessage: null,
  });
}

const conflicto: CambioMaestroAw = {
  id: 'cf-1', tipoEntidad: 'Cliente', entidadId: null, referenciaExterna: '5121', campo: 'RFC repetido',
  valorAntes: null, valorDespues: 'ABC010101AB1', dueno: 'AW', tipo: 'RfcCompartido', estado: 'Pendiente',
  causa: 'Dos o más clientes de A+W con la misma clave fiscal ABC010101AB1: AW-5027 y AW-5028.', versionEvento: 'h',
  versionMatriz: 'v', ejecucionId: null, ocurridoEnUtc: '2026-10-17T10:00:00Z', resolucion: null, resueltoEnUtc: null, version: 3,
};
const base = { tieneRfc: true, estatus: 'Activo', unificadoEnClienteId: null, ultimoPedidoAw: null, cotizacionesAw: 0, ultimaCotizacionAw: null };
const grupo = {
  rfc: 'ABC010101AB1', principalSugeridoId: 'c-1', motivoSugerencia: 'Ya tiene el RFC en el ERP.', resuelto: false,
  clientes: [
    { ...base, clienteId: 'c-1', referenciaAw: '5027', razonSocial: 'Vidrios Uno', pedidosAw: 4, ultimoPedidoAw: '2026-10-01T00:00:00Z', pedidosErp: 2, accionSugerida: 'Principal' },
    { ...base, clienteId: 'c-2', referenciaAw: '5028', razonSocial: 'Vidrios Dos', pedidosAw: 0, pedidosErp: 0, accionSugerida: 'Fusionar' },
  ],
};

function montar(onClose = vi.fn(), g: JsonBodyType = grupo) {
  mswServer.use(http.get('*/clientes/rfc-repetidos/ABC010101AB1', () => HttpResponse.json(g)));
  render(<UnificarClientesRfcSheet conflicto={conflicto} onClose={onClose} />, { wrapper: createQueryWrapper() });
  return onClose;
}

describe('<UnificarClientesRfcSheet>', () => {
  beforeEach(() => setPermisos([PERMISO_UNIFICAR_CLIENTES]));

  it('carga el grupo con el principal sugerido preseleccionado y la consecuencia de cada cliente', async () => {
    montar();
    expect(await screen.findByRole('heading', { name: 'Unificar clientes con RFC ABC010101AB1' })).toBeInTheDocument();
    expect(await screen.findByRole('radio', { name: /Vidrios Uno/ })).toBeChecked();
    expect(screen.getByRole('group', { name: 'Cliente principal' })).toBeInTheDocument();
    expect(screen.getByText('Se fusiona')).toBeInTheDocument();
    expect(screen.getByText(/A\+W no se modifica\. Los pedidos/)).toBeInTheDocument();
  });

  it('un cliente con solo cotizaciones en A+W se agrupa, no se fusiona', async () => {
    montar(vi.fn(), {
      ...grupo,
      clientes: [
        grupo.clientes[0],
        { ...base, clienteId: 'c-3', referenciaAw: '5029', razonSocial: 'Vidrios Tres', pedidosAw: 0, pedidosErp: 0, cotizacionesAw: 3, ultimaCotizacionAw: '2026-10-05T00:00:00Z', accionSugerida: 'Agrupar' },
      ],
    });
    expect(await screen.findByText('Se agrupa')).toBeInTheDocument();
    expect(screen.queryByText('Se fusiona')).not.toBeInTheDocument();
    expect(screen.getByText(/Cotizaciones A\+W: 3 \(última/)).toBeInTheDocument();
  });

  it('unifica con rfc, principal y motivo y envía Idempotency-Key; cierra al terminar', async () => {
    let body: unknown = null;
    let key: string | null = null;
    mswServer.use(http.post('*/clientes/rfc-repetidos/unificar', async ({ request }) => {
      body = await request.json();
      key = request.headers.get('Idempotency-Key');
      return HttpResponse.json({ ...grupo, resuelto: true });
    }));
    const onClose = montar();
    const unificar = await screen.findByRole('button', { name: 'Unificar' });
    expect(unificar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Mismo cliente fiscal' } });
    fireEvent.click(unificar);
    await waitFor(() => expect(body).toEqual({ rfc: 'ABC010101AB1', principalClienteId: 'c-1', motivo: 'Mismo cliente fiscal' }));
    expect(key).toBeTruthy();
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('un error de Problem Details se muestra inline con role alert', async () => {
    mswServer.use(http.post('*/clientes/rfc-repetidos/unificar', () =>
      HttpResponse.json({ title: 'Conflicto', detail: 'El grupo cambió.', status: 409 }, { status: 409, headers: { 'Content-Type': 'application/problem+json' } })));
    montar();
    fireEvent.change(await screen.findByLabelText('Motivo'), { target: { value: 'Mismo cliente fiscal' } });
    fireEvent.click(screen.getByRole('button', { name: 'Unificar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El grupo cambió.');
  });

  it('404 muestra que el RFC ya no tiene clientes repetidos', async () => {
    mswServer.use(http.get('*/clientes/rfc-repetidos/ABC010101AB1', () =>
      HttpResponse.json({ title: 'No encontrado', status: 404 }, { status: 404, headers: { 'Content-Type': 'application/problem+json' } })));
    render(<UnificarClientesRfcSheet conflicto={conflicto} onClose={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Este RFC ya no tiene clientes repetidos.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Unificar' })).not.toBeInTheDocument();
  });

  it('sin permiso queda de solo lectura con explicación', async () => {
    setPermisos([]);
    montar();
    expect(await screen.findByText(/Necesitas el permiso de unificar clientes/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Vidrios Uno/ })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Unificar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mantener separados' })).not.toBeInTheDocument();
  });

  it('«Mantener separados» resuelve el conflicto con su versión', async () => {
    let body: unknown = null;
    mswServer.use(http.post('*/clientes/conflictos-aw/cf-1/resolver', async ({ request }) => {
      body = await request.json();
      return new HttpResponse(null, { status: 204 });
    }));
    montar();
    fireEvent.click(await screen.findByRole('button', { name: 'Mantener separados' }));
    await waitFor(() => expect(body).toEqual({ resolucion: 'MantenerSeparados', version: 3 }));
  });
});
