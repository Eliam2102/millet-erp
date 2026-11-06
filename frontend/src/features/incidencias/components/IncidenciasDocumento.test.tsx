import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { IncidenciasDocumento } from './IncidenciasDocumento';
import { cerrarSesion, fila, sesion } from '@/features/incidencias/test-utils';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
}));

afterEach(cerrarSesion);

const props = { modulo: 'almacen', tipo: 'recepcion', id: 'doc-1', etiqueta: 'REC-000123' };

describe('<IncidenciasDocumento>', () => {
  it('sin permiso de ver no se muestra ni consulta', () => {
    sesion([]);
    const { container } = render(<IncidenciasDocumento {...props} />, { wrapper: createQueryWrapper() });
    expect(container).toBeEmptyDOMElement();
  });

  it('consulta por documento y pone primero las pendientes', async () => {
    sesion(['compartido.incidencias.ver']);
    let url = '';
    mswServer.use(
      http.get('*/api/v1/incidencias', ({ request }) => {
        url = request.url;
        return HttpResponse.json({
          items: [
            fila({ id: 'a', numero: 1, estado: 'Resuelta', responsableUsuarioId: 'otro', responsableNombre: 'Jefe de almacén' }),
            fila({ id: 'b', numero: 2, estado: 'EnAtencion' }),
          ],
          total: 2,
          offset: 0,
          limit: 50,
        });
      }),
    );
    render(<IncidenciasDocumento {...props} />, { wrapper: createQueryWrapper() });
    const folios = await screen.findAllByText(/^INC-/);
    expect(folios.map((f) => f.textContent)).toEqual(['INC-2', 'INC-1']);
    const params = new URL(url).searchParams;
    expect(params.get('modulo')).toBe('almacen');
    expect(params.get('tipoDocumento')).toBe('recepcion');
    expect(params.get('documentoId')).toBe('doc-1');
    expect(screen.getByText(/Asignada a ti/)).toBeInTheDocument();
    expect(screen.getByText(/Jefe de almacén/)).toBeInTheDocument();
  });

  it('sin incidencias lo dice; «Registrar incidencia» solo con paso y permiso', async () => {
    sesion(['compartido.incidencias.ver']);
    mswServer.use(http.get('*/api/v1/incidencias', () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 50 })));
    const { rerender } = render(<IncidenciasDocumento {...props} paso="alm.recepcion.diferencia-oc" />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText('Este documento no tiene incidencias.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar incidencia' })).not.toBeInTheDocument();

    sesion(['compartido.incidencias.ver', 'compartido.incidencias.registrar']);
    rerender(<IncidenciasDocumento {...props} paso="alm.recepcion.diferencia-oc" />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Registrar incidencia' })).toBeInTheDocument());
  });
});
