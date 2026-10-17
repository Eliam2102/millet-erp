import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SincronizacionAwPage } from '@/modules/datos-maestros/components/SincronizacionAwPage';

const conflicto = {
  id: 'k-1', tipoEntidad: 'Cliente', entidadId: 'c-2', referenciaExterna: 'AW-62', campo: 'RFC repetido entre clientes de A+W',
  valorAntes: 'AW-61', valorDespues: 'DEMO010101AB1', dueno: 'AW', tipo: 'RfcCompartido', estado: 'Pendiente',
  causa: 'Dos clientes de A+W con la misma clave fiscal DEMO010101AB1: 62 y AW-61. No se fusionan.',
  versionEvento: 'abcdef0123456789', versionMatriz: '0-propuesta', ejecucionId: null,
  ocurridoEnUtc: '2026-10-17T10:00:00Z', resolucion: null, resueltoEnUtc: null, version: 3,
};

describe('<SincronizacionAwPage>', () => {
  it('muestra el conflicto, resuelve con «Mantener separados» y muestra la versión de la matriz', async () => {
    let body: unknown = null;
    mswServer.use(
      http.get('*/clientes/conflictos-aw', ({ request }) =>
        HttpResponse.json(new URL(request.url).searchParams.get('estado') === 'Pendiente' ? [conflicto] : [])),
      http.get('*/sincronizacion-aw/matriz', () =>
        HttpResponse.json({ version: '0-propuesta', campos: [{ entidad: 'Cliente', campo: 'Razón social', dueno: 'ERP', siDifiere: 'Se conserva el ERP.' }] })),
      http.post('*/clientes/conflictos-aw/k-1/resolver', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<SincronizacionAwPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText(/misma clave fiscal DEMO010101AB1/)).toBeInTheDocument();
    expect(await screen.findByText(/Versión 0-propuesta/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Mantener separados' }));
    expect(screen.getByText('Los dos clientes quedan separados. Esto no fusiona ni liga identidades.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(body).toEqual({ resolucion: 'MantenerSeparados', version: 3 }));
  });
});
