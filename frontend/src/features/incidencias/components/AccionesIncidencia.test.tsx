import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { QueryClient } from '@tanstack/react-query';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { AccionesIncidencia } from './AccionesIncidencia';
import { incidenciasKeys } from '@/features/incidencias/api/keys';
import { cerrarSesion, detalle, sesion } from '@/features/incidencias/test-utils';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
}));

afterEach(cerrarSesion);

function conCache(): QueryClient {
  const qc = createTestQueryClient();
  // gcTime 0 del cliente de pruebas recolectaría la entrada sin observador (en la app la observa el detalle).
  qc.setQueryDefaults(incidenciasKeys.detalle('inc-1'), { gcTime: Infinity });
  qc.setQueryData(incidenciasKeys.detalle('inc-1'), { data: detalle(), etag: '7' });
  return qc;
}

describe('<AccionesIncidencia>', () => {
  it('muestra solo las acciones que el servidor permite', () => {
    sesion([]);
    render(<AccionesIncidencia incidencia={detalle({ puedeAtender: false, puedeReasignar: false, puedeCancelar: true })} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Resolver' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar incidencia' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Atender' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reasignar' })).not.toBeInTheDocument();
  });

  it('sin ninguna acción permitida no pinta nada', () => {
    sesion([]);
    const { container } = render(
      <AccionesIncidencia
        incidencia={detalle({ puedeAtender: false, puedeResolver: false, puedeCancelar: false, puedeReasignar: false })}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('resolver exige la resolución y la manda con If-Match de la versión en caché', async () => {
    sesion([]);
    let ifMatch: string | null = null;
    let cuerpo: unknown = null;
    mswServer.use(
      http.post('*/api/v1/incidencias/inc-1/resolver', async ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        cuerpo = await request.json();
        return HttpResponse.json(detalle({ estado: 'Resuelta', puedeResolver: false }), { headers: { ETag: '"8"' } });
      }),
    );
    render(<AccionesIncidencia incidencia={detalle()} />, { wrapper: createQueryWrapper(conCache()) });
    fireEvent.click(screen.getByRole('button', { name: 'Resolver' }));
    const dialogo = await screen.findByRole('dialog');
    fireEvent.click(screen.getAllByRole('button', { name: 'Resolver' }).at(-1)!);
    expect(await screen.findByText('Escribe al menos 5 caracteres.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Resolución/), { target: { value: 'Se registró la recepción del pendiente.' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Resolver' }).at(-1)!);
    await waitFor(() => expect(cuerpo).toEqual({ resolucion: 'Se registró la recepción del pendiente.' }));
    expect(ifMatch).toBe('"7"');
    await waitFor(() => expect(dialogo).not.toBeInTheDocument());
  });

  it('un conflicto de versión pide revisar el estado actual', async () => {
    sesion([]);
    mswServer.use(
      http.post('*/api/v1/incidencias/inc-1/atender', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Conflicto', status: 409, code: 'CONCURRENCY_CONFLICT' },
          { status: 409 },
        ),
      ),
    );
    render(<AccionesIncidencia incidencia={detalle()} />, { wrapper: createQueryWrapper(conCache()) });
    fireEvent.click(screen.getByRole('button', { name: 'Atender' }));
    await screen.findByRole('dialog');
    fireEvent.click(screen.getAllByRole('button', { name: 'Atender' }).at(-1)!);
    expect(await screen.findByText(/Otra persona cambió la incidencia/)).toBeInTheDocument();
  });
});
