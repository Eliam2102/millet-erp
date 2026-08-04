import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { useAuthStore } from '@/lib/auth/auth-store';

const ART = '11111111-1111-1111-1111-111111111111';
const SUB = '22222222-2222-2222-2222-222222222222';
const RACK = '33333333-3333-3333-3333-333333333333';

const UBIC_RACK = {
  id: RACK,
  subAlmacenId: SUB,
  clave: 'R-1',
  nombre: 'Rack 1',
  estatus: 0,
  esDefault: false,
  subAlmacenClave: 'INS',
  subAlmacenNombre: 'Insumos',
  almacenClave: 'ALM',
  almacenNombre: 'Almacén',
};
const UBIC_UNICA = { ...UBIC_RACK, id: 'unica', clave: 'ÚNICA', esDefault: true };

// Segundo rack, en OTRO sub-almacén/almacén — para el caso sin filtro por sub.
const RACK2 = '44444444-4444-4444-4444-444444444444';
const UBIC_RACK2 = {
  id: RACK2,
  subAlmacenId: 'sub2',
  clave: 'R-2',
  nombre: 'Rack 2',
  estatus: 0,
  esDefault: false,
  subAlmacenClave: 'INS2',
  subAlmacenNombre: 'Insumos 2',
  almacenClave: 'ALM2',
  almacenNombre: 'Almacén 2',
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' },
    empresas: [],
    currentEmpresaId: 'e',
    permisos: ['almacen.almacenes.leer', 'almacen.ubicaciones.leer'],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<UbicacionBinSelector>', () => {
  it('modo asignación: solo las ubicaciones ASIGNADAS del artículo', async () => {
    // asignaciones del artículo → RACK; ubicaciones del sub-almacén → RACK+ÚNICA.
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({
          items: [
            { id: 'a1', ubicacionId: RACK, articuloId: ART, estatus: 0, articuloClave: null, articuloDescripcion: null },
          ],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
      http.get('*/api/v1/almacen/ubicaciones', () =>
        HttpResponse.json({
          items: [UBIC_RACK, UBIC_UNICA],
          offset: 0,
          limit: 500,
          total: 2,
        }),
      ),
    );
    render(
      <UbicacionBinSelector
        modo="asignacion"
        articuloId={ART}
        subAlmacenId={SUB}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('combobox'));
    // El rack asignado aparece con su ruta de 3 niveles; la ÚNICA (no
    // asignada) NO.
    await waitFor(() =>
      expect(screen.getByText('ALM › INS · R-1')).toBeInTheDocument(),
    );
    expect(screen.queryByText('ÚNICA')).not.toBeInTheDocument();
  });

  it('modo asignación por default (requiereSubAlmacen=true): filtra por sub y se deshabilita sin él', () => {
    render(
      <UbicacionBinSelector
        modo="asignacion"
        articuloId={ART}
        subAlmacenId={null}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    const trigger = screen.getByRole('combobox');
    expect(trigger).toBeDisabled();
    expect(trigger).toHaveTextContent(/Elige artículo y sub-almacén primero/i);
  });

  it('requiereSubAlmacen=false: lista TODAS las asignadas del artículo, sin sub-almacén', async () => {
    // El artículo está asignado a bins en DOS sub-almacenes distintos; sin
    // filtro por sub deben aparecer los dos, cada uno con su ruta.
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({
          items: [
            { id: 'a1', ubicacionId: RACK, articuloId: ART, estatus: 0, articuloClave: null, articuloDescripcion: null },
            { id: 'a2', ubicacionId: RACK2, articuloId: ART, estatus: 0, articuloClave: null, articuloDescripcion: null },
          ],
          offset: 0,
          limit: 500,
          total: 2,
        }),
      ),
      http.get('*/api/v1/almacen/ubicaciones', () =>
        HttpResponse.json({
          items: [UBIC_RACK, UBIC_RACK2],
          offset: 0,
          limit: 500,
          total: 2,
        }),
      ),
    );
    render(
      <UbicacionBinSelector
        modo="asignacion"
        articuloId={ART}
        subAlmacenId={null}
        requiereSubAlmacen={false}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    const trigger = screen.getByRole('combobox');
    // Habilitado SIN sub-almacén.
    expect(trigger).not.toBeDisabled();
    fireEvent.click(trigger);
    expect(await screen.findByText('ALM › INS · R-1')).toBeInTheDocument();
    expect(screen.getByText('ALM2 › INS2 · R-2')).toBeInTheDocument();
  });

  it('modo saldo: ubicaciones con existencia (incluida ÚNICA) desde /por-ubicacion, con ruta de 3 niveles', async () => {
    let calledPorUbicacion = false;
    mswServer.use(
      http.get('*/api/v1/almacen/saldos/por-ubicacion', () => {
        calledPorUbicacion = true;
        // C1: el DTO de saldo ya trae subAlmacenClave/almacenClave.
        return HttpResponse.json([
          { ubicacionId: 'unica', clave: 'ÚNICA', nombre: 'Única', subAlmacenClave: 'INS', almacenClave: 'ALM', esDefault: true, cantidad: 8, cantidadDisponible: 8, costoPromedioMxn: 100 },
          { ubicacionId: RACK, clave: 'R-1', nombre: 'Rack 1', subAlmacenClave: 'INS', almacenClave: 'ALM', esDefault: false, cantidad: 5, cantidadDisponible: 5, costoPromedioMxn: 120 },
        ]);
      }),
    );
    render(
      <UbicacionBinSelector
        modo="saldo"
        articuloId={ART}
        subAlmacenId={SUB}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(calledPorUbicacion).toBe(true));
    // C1: la ruta completa "ALM › SUB · UBI", no solo la clave (mutación: si el
    // selector no propaga subAlmacenClave/almacenClave en modo saldo, degrada a
    // "R-1" y esta aserción falla).
    expect(await screen.findByText('ALM › INS · R-1')).toBeInTheDocument();
    // La ÚNICA aparece con su ruta + badge redundante.
    expect(screen.getByText('ALM › INS · ÚNICA')).toBeInTheDocument();
  });

  it('deshabilitado y con placeholder guía sin artículo/sub-almacén', () => {
    render(
      <UbicacionBinSelector
        modo="saldo"
        articuloId={null}
        subAlmacenId={null}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    const trigger = screen.getByRole('combobox');
    expect(trigger).toBeDisabled();
    expect(trigger).toHaveTextContent(/Elige artículo y sub-almacén primero/i);
  });
});
