import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { UbicacionesTab } from './UbicacionesTab';

const API = '*/api/v1/contabilidad';
const SUCURSALES = [
  { id: 's1', clave: '101', nombre: 'FIX Conkal', activa: true },
  { id: 's4', clave: '104', nombre: 'FIX Cancún', activa: true },
];
const UBICACIONES = [
  { dim1Id: 'u1', clave: '101', nombre: 'CONKAL', activo: true, sucursal: SUCURSALES[0] },
  { dim1Id: 'u4', clave: '104', nombre: 'CANCUN', activo: true, sucursal: null },
];
const CENTROS = [
  { dim2Id: 'c1', clave: '40CB00', nombre: 'CONTABILIDAD', activo: true, dim1Clave: '101', corporativo: true },
  { dim2Id: 'c2', clave: '20PDMC', nombre: 'CORTE', activo: true, dim1Clave: '101', corporativo: false },
];

function servidor(pedidos: unknown[] = []) {
  mswServer.use(
    http.get(`${API}/ubicaciones-sucursal`, () => HttpResponse.json(UBICACIONES)),
    http.get(`${API}/sucursales`, () => HttpResponse.json(SUCURSALES)),
    http.get(`${API}/centros-corporativos`, () => HttpResponse.json(CENTROS)),
    http.put(`${API}/ubicaciones-sucursal/:id`, async ({ request, params }) => {
      pedidos.push({ ruta: 'ubicacion', id: params.id, body: await request.json() });
      return HttpResponse.json({ ...UBICACIONES[1], sucursal: SUCURSALES[1] });
    }),
    http.put(`${API}/centros-corporativos/:id`, async ({ request, params }) => {
      pedidos.push({ ruta: 'corporativo', id: params.id, body: await request.json() });
      return HttpResponse.json({ ...CENTROS[1], corporativo: true });
    }),
  );
}

describe('<UbicacionesTab>', () => {
  it('liga una ubicación a su sucursal y marca un CeCo como corporativo', async () => {
    const pedidos: unknown[] = [];
    servidor(pedidos);
    render(<UbicacionesTab puedeAdministrar />, { wrapper: createQueryWrapper() });
    const select = await screen.findByLabelText('Sucursal de la ubicación 104');
    await waitFor(() => expect(within(select).getByRole('option', { name: '104 — FIX Cancún' })).toBeInTheDocument());
    expect(select).toHaveValue('');
    expect(screen.getByLabelText('Sucursal de la ubicación 101')).toHaveValue('s1');

    fireEvent.change(select, { target: { value: 's4' } });
    fireEvent.click(await screen.findByLabelText('Corporativo 20PDMC'));
    await waitFor(() => expect(pedidos).toEqual([
      { ruta: 'ubicacion', id: 'u4', body: { sucursalId: 's4' } },
      { ruta: 'corporativo', id: 'c2', body: { corporativo: true } },
    ]));
  });

  it('sin permiso de configuración solo muestra el estado', async () => {
    servidor();
    render(<UbicacionesTab puedeAdministrar={false} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Sin sucursal')).toBeInTheDocument();
    expect(within(screen.getByText('40CB00').closest('tr')!).getByText('Corporativo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Sucursal de la ubicación 101')).not.toBeInTheDocument();
  });
});
