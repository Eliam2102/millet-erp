import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SalidaDetallePage } from '@/features/almacen/pages/SalidaDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoMovimiento } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 's-1' }),
  useSearch: () => ({}),
}));

const SUB_ID = '00000008-0001-0000-0000-000000000001';
const ART_ID = '00000005-0002-0000-0000-000000000002';
const RQ_ID = '019e937e-aaaa-bbbb-cccc-0000005218ad';
const PERSONA_ID = '019e70cf-aaaa-bbbb-cccc-00000ace19c6';

const detalleConNombres = {
  id: 's-1',
  folio: 'M-SAL2026-000002',
  fechaMovimiento: '2026-06-01',
  subAlmacenId: SUB_ID,
  subAlmacenClave: 'ALM-CEN',
  subAlmacenNombre: 'Almacén Central',
  rqId: RQ_ID,
  rqFolio: 'MID2026-000123',
  esPorVale: false,
  valeBlobRef: null,
  personaDestinatariaId: PERSONA_ID,
  personaDestinatariaNombre: 'Juan Pérez',
  estado: EstadoMovimiento.Registrado,
  version: 1,
  observaciones: null,
  registradoAt: null,
  registradoPor: null,
  rqRegularizadoraId: null,
  rqRegularizadoraFolio: null,
  lineas: [
    {
      id: 'l-1',
      posicion: 1,
      articuloId: ART_ID,
      articuloClave: 'ACC-001',
      articuloDescripcion: 'Tornillo M6',
      cantidad: 2,
      unidadMedida: 'PZA',
      costoUnitarioMxn: 10,
      montoTotalMxn: 20,
      centroCostoId: '0c000000-0000-0000-0000-000000000001',
      centroCostoClave: 'MCLC101',
      centroCostoNombre: 'Gantry',
      proyectoId: null,
    },
  ],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.salidas.leer-todas'],
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

describe('<SalidaDetallePage> — nombres resueltos', () => {
  it('muestra nombres (sub-almacén, folio RQ, destinatario, artículo) y NO los ids/códigos crudos', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/salidas/s-1', () =>
        HttpResponse.json(detalleConNombres),
      ),
    );

    render(<SalidaDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('ALM-CEN · Almacén Central')).toBeInTheDocument(),
    );
    expect(screen.getByText('MID2026-000123')).toBeInTheDocument();
    expect(screen.getByText('Juan Pérez')).toBeInTheDocument();
    expect(screen.getByText('Tornillo M6')).toBeInTheDocument();
    // Fase E PR5: columna CC-Máquina con el nombre resuelto por el read-port.
    expect(screen.getByText('MCLC101 — Gantry')).toBeInTheDocument();

    // Assert clave: con nombre disponible, ningún id/código crudo se pinta.
    expect(screen.queryByText(SUB_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(RQ_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(PERSONA_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(ART_ID)).not.toBeInTheDocument();
  });

  it('cae a los ids crudos cuando el backend no resuelve nombres', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/salidas/s-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          subAlmacenClave: null,
          subAlmacenNombre: null,
          rqFolio: null,
          personaDestinatariaNombre: null,
          lineas: [
            {
              ...detalleConNombres.lineas[0],
              articuloClave: null,
              articuloDescripcion: null,
            },
          ],
        }),
      ),
    );

    render(<SalidaDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText(SUB_ID)).toBeInTheDocument(),
    );
    expect(screen.getByText(RQ_ID)).toBeInTheDocument();
    expect(screen.getByText(PERSONA_ID)).toBeInTheDocument();
    expect(screen.getByText(ART_ID)).toBeInTheDocument();
  });
});
