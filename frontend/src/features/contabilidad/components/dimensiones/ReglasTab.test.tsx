import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ETIQUETA_DIMENSION } from '../../lib/dimensiones';
import { ReglasTab } from './ReglasTab';

const base = {
  cuentaId: 'c1', cuentaCodigo: 'FIX-501.01', cuentaNombre: 'FIX Mantenimiento', tipoDocumentoId: 't1', tipoDocumentoClave: 'FIX-FP',
  tipoDocumentoNombre: 'FIX Factura de proveedor', dimension: 'Dim2', nombreDimension: ETIQUETA_DIMENSION.Dim2, esPrueba: true, nota: null, version: 1,
};
const vigente = { ...base, id: 'r1', requerimiento: 'Obligatorio', vigenteDesde: '2026-10-01', vigenteHasta: null, estado: 'Vigente', editable: false };
const futura = {
  ...base, id: 'r2', tipoDocumentoId: null, tipoDocumentoClave: null, tipoDocumentoNombre: null, dimension: 'Dim3', nombreDimension: ETIQUETA_DIMENSION.Dim3,
  requerimiento: 'NoAplica', vigenteDesde: '2026-11-01', vigenteHasta: '2026-12-31', estado: 'Futura', editable: true,
};

function servidor(cierres: unknown[] = []) {
  mswServer.use(
    http.get('*/api/v1/contabilidad/reglas-dimension', () => HttpResponse.json({ items: [vigente, futura], total: 2, offset: 0, limit: 10 })),
    http.get('*/api/v1/contabilidad/tipos-documento', () => HttpResponse.json([])),
    http.post('*/api/v1/contabilidad/reglas-dimension/:id/cerrar', async ({ request, params }) => {
      cierres.push({ id: params.id, body: await request.json(), ifMatch: request.headers.get('If-Match') });
      return HttpResponse.json({ ...vigente, vigenteHasta: '2026-10-31', estado: 'Vigente', version: 2 });
    }),
  );
}

describe('<ReglasTab>', () => {
  it('muestra requerimiento, vigencia, estado y la marca de prueba de cada regla', async () => {
    servidor();
    render(<ReglasTab puedeAdministrar={false} />, { wrapper: createQueryWrapper() });
    const filas = await screen.findAllByRole('row');
    const primera = within(filas[1]);
    expect(primera.getByText('FIX-FP — FIX Factura de proveedor')).toBeInTheDocument();
    expect(primera.getByText('Obligatoria')).toBeInTheDocument();
    expect(primera.getByText('Desde 1 oct 2026')).toBeInTheDocument();
    expect(primera.getByText('Vigente')).toBeInTheDocument();
    expect(primera.getByText('Prueba')).toBeInTheDocument();
    const segunda = within(filas[2]);
    expect(segunda.getByText('Todos los tipos')).toBeInTheDocument();
    expect(segunda.getByText('No aplica')).toBeInTheDocument();
    expect(segunda.getByText('1 nov 2026 – 31 dic 2026')).toBeInTheDocument();
    // Sin permiso de configuración no hay acciones.
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Nueva regla/ })).not.toBeInTheDocument();
  });

  it('con permiso: solo la regla futura se edita; una vigente se cierra con If-Match', async () => {
    const cierres: unknown[] = [];
    servidor(cierres);
    render(<ReglasTab puedeAdministrar />, { wrapper: createQueryWrapper() });
    const filas = await screen.findAllByRole('row');
    expect(within(filas[1]).queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(within(filas[2]).getByRole('button', { name: 'Editar' })).toBeInTheDocument();

    fireEvent.click(within(filas[1]).getByRole('button', { name: 'Cerrar vigencia' }));
    const dialogo = await screen.findByRole('dialog');
    fireEvent.change(within(dialogo).getByLabelText('Vigente hasta'), { target: { value: '2026-10-31' } });
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cerrar vigencia' }));
    await waitFor(() => expect(cierres).toEqual([{ id: 'r1', body: { vigenteHasta: '2026-10-31' }, ifMatch: '"1"' }]));
  });

  it('error del servidor al cerrar: se muestra el mensaje de negocio', async () => {
    servidor();
    mswServer.use(http.post('*/api/v1/contabilidad/reglas-dimension/:id/cerrar', () => HttpResponse.json(
      { type: 'x', title: 'Regla', status: 422, code: 'CONTAB_REGLA_VIGENCIA_RETROACTIVA', detail: 'La fecha final de una regla en vigor no puede ser anterior a hoy.' },
      { status: 422 },
    )));
    render(<ReglasTab puedeAdministrar />, { wrapper: createQueryWrapper() });
    const filas = await screen.findAllByRole('row');
    fireEvent.click(within(filas[1]).getByRole('button', { name: 'Cerrar vigencia' }));
    const dialogo = await screen.findByRole('dialog');
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cerrar vigencia' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('no puede ser anterior a hoy');
  });
});
