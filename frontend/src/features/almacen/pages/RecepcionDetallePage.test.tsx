import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RecepcionDetallePage } from '@/features/almacen/pages/RecepcionDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoMovimiento } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'r-1' }),
  useSearch: () => ({}),
}));

const SUB_ID = '00000008-0001-0000-0000-000000000004';
const OC_ID = '019e5ba8-41b2-7ee8-8f40-4435f4808968';
const ART_ID = '00000005-0002-0000-0000-000000000001';

const detalleConNombres = {
  id: 'r-1',
  folio: 'M-ENT2026-000005',
  fechaMovimiento: '2026-06-01',
  subAlmacenId: SUB_ID,
  subAlmacenClave: 'ALM-CEN',
  subAlmacenNombre: 'Almacén Central',
  ordenCompraId: OC_ID,
  ordenCompraFolio: 'OC-MID2026-000050',
  cfdiRecibidoId: null,
  facturaId: null,
  cfdiUuidFiscal: null,
  facturaFolio: null,
  estado: EstadoMovimiento.Registrado,
  version: 1,
  observaciones: null,
  registradoAt: null,
  registradoPor: null,
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
    permisos: ['almacen.entradas.leer'],
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

describe('<RecepcionDetallePage> — nombres resueltos', () => {
  it('muestra nombres (sub-almacén, folio OC, artículo) y NO los ids crudos', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json(detalleConNombres),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('ALM-CEN · Almacén Central')).toBeInTheDocument(),
    );
    expect(screen.getByText('OC-MID2026-000050')).toBeInTheDocument();
    expect(screen.getByText('Tornillo M6')).toBeInTheDocument();

    // Con nombre/folio disponible, ningún id crudo se pinta.
    expect(screen.queryByText(SUB_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(OC_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(ART_ID)).not.toBeInTheDocument();
  });

  it('cae a los ids crudos cuando el backend no resuelve nombres', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          subAlmacenClave: null,
          subAlmacenNombre: null,
          ordenCompraFolio: null,
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

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText(SUB_ID)).toBeInTheDocument());
    expect(screen.getByText(OC_ID)).toBeInTheDocument();
    expect(screen.getByText(ART_ID)).toBeInTheDocument();
  });

  it('muestra UUID fiscal del CFDI y folio de factura; cae al id truncado si no resuelven', async () => {
    const CFDI_ID = '019e0000-0000-7000-8000-000000000001';
    const FACT_ID = '019e0000-0000-7000-8000-000000000002';
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          cfdiRecibidoId: CFDI_ID,
          cfdiUuidFiscal: '5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B',
          facturaId: FACT_ID,
          facturaFolio: null, // el puerto no resolvió la factura
        }),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    // CFDI resuelto: se pinta el folio fiscal del SAT, no el GUID interno.
    await waitFor(() =>
      expect(
        screen.getByText('5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B'),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByText(CFDI_ID)).not.toBeInTheDocument();
    // Factura sin resolver: id truncado, nunca el GUID completo.
    expect(screen.getByText('019e0000…')).toBeInTheDocument();
    expect(screen.queryByText(FACT_ID)).not.toBeInTheDocument();
  });
});
