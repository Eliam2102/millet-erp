import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TiposDocumentoTab } from './TiposDocumentoTab';

const API = '*/api/v1/contabilidad/tipos-documento';
const TIPO = { id: 't1', clave: 'TT', nombre: 'FIX Traspasos', activo: true, esPrueba: false, version: 2, tipoSat: 'Diario' };

describe('<TiposDocumentoTab>', () => {
  it('muestra y edita el tipo SAT con If-Match', async () => {
    const puts: { body: unknown; ifMatch: string | null }[] = [];
    mswServer.use(
      http.get(API, () => HttpResponse.json([TIPO])),
      http.put(`${API}/t1`, async ({ request }) => {
        puts.push({ body: await request.json(), ifMatch: request.headers.get('If-Match') });
        return HttpResponse.json({ ...TIPO, tipoSat: 'Egreso', version: 3 });
      }),
    );
    render(<TiposDocumentoTab puedeAdministrar />, { wrapper: createQueryWrapper() });
    const sat = await screen.findByLabelText('Tipo SAT de TT');
    expect(sat).toHaveValue('Diario');
    fireEvent.change(sat, { target: { value: 'Egreso' } });
    await waitFor(() => expect(puts).toHaveLength(1));
    expect(puts[0]).toEqual({ body: { nombre: 'FIX Traspasos', activo: true, tipoSat: 'Egreso' }, ifMatch: '"2"' });
  });

  it('un tipo real exige clave de dos letras; uno de prueba conserva clave libre', async () => {
    const posts: unknown[] = [];
    mswServer.use(
      http.get(API, () => HttpResponse.json([])),
      http.post(API, async ({ request }) => {
        posts.push(await request.json());
        return HttpResponse.json(TIPO, { status: 201 });
      }),
    );
    render(<TiposDocumentoTab puedeAdministrar />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByLabelText('De prueba')); // queda como tipo real
    fireEvent.change(screen.getByLabelText('Clave'), { target: { value: 'TTX' } });
    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'FIX Traspasos' } });
    const agregar = screen.getByRole('button', { name: 'Agregar tipo' });
    expect(agregar).toBeDisabled();
    expect(screen.getByText(/son dos letras \(TT, PC, RF…\)/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Clave'), { target: { value: 'tt' } });
    fireEvent.change(screen.getByLabelText('Tipo SAT'), { target: { value: 'Ingreso' } });
    fireEvent.click(agregar);
    await waitFor(() => expect(posts).toHaveLength(1));
    expect(posts[0]).toEqual({ clave: 'TT', nombre: 'FIX Traspasos', esPrueba: false, tipoSat: 'Ingreso' });
  });
});
