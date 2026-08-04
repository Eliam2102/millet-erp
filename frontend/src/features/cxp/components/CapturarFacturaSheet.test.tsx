import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CapturarFacturaSheet } from '@/features/cxp/components/CapturarFacturaSheet';
import { useAuthStore } from '@/lib/auth/auth-store';

// useNavigate necesita contexto de router; lo sustituimos por un no-op y
// conservamos el resto de los exports reales del paquete.
vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>(
      '@tanstack/react-router',
    );
  return { ...actual, useNavigate: () => () => {} };
});

const OC_ID = '11111111-1111-4111-8111-111111111111';
const PROV_ID = '22222222-2222-4222-8222-222222222222';
const SUC_ID = '33333333-3333-4333-8333-333333333333';

const ocResumen = {
  id: OC_ID,
  folio: 'OC-MID2026-000123',
  folioAnio: 2026,
  estado: 3, // Autorizada
  subEstadoRecepcion: 0,
  subEstadoFacturacion: 0,
  subEstadoPago: 0,
  proveedorId: PROV_ID,
  proveedorNombre: 'Proveedor Demo SA',
  sucursalId: SUC_ID,
  compradorTitularId: '44444444-4444-4444-8444-444444444444',
  moneda: 'MXN',
  fechaDocumento: '2026-06-01T00:00:00Z',
  referenciaProveedor: null,
};

function mockEndpoints() {
  mswServer.use(
    http.get('*/api/v1/compras/ordenes', () =>
      HttpResponse.json({ items: [ocResumen], page: 1, pageSize: 200, totalCount: 1 }),
    ),
    // Detalle de OC: lo consume el LineaOcSelector de cada línea al elegir OC.
    http.get('*/api/v1/compras/ordenes/:id', () =>
      HttpResponse.json({ id: OC_ID, lineas: [] }),
    ),
    http.get('*/api/v1/catalogos/proveedores', () =>
      HttpResponse.json({
        items: [
          {
            id: PROV_ID,
            clave: 'PRV-001',
            razonSocial: 'Proveedor Demo SA',
            nombreComercial: null,
            rfc: 'XAXX010101000',
            estatus: 'Activo',
          },
        ],
        offset: 0,
        limit: 500,
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({
        items: [{ id: SUC_ID, clave: 'MID', nombre: 'Mérida', estatus: 'Activo' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
  );
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['cuentas_por_pagar.facturas.capturar'],
    errorMessage: null,
  });
  mockEndpoints();
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

describe('<CapturarFacturaSheet> — OcSelector deriva proveedor/sucursal', () => {
  it('estado inicial: la OC se elige con selector y proveedor/sucursal están read-only y vacíos', () => {
    render(<CapturarFacturaSheet open onOpenChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // La OC es un combobox (selector), no un input de UUID crudo.
    expect(
      screen.getByRole('combobox', { name: /orden de compra/i }),
    ).toBeInTheDocument();

    // Proveedor y sucursal son read-only (disabled) y arrancan vacíos.
    const proveedor = screen.getByLabelText(
      'Proveedor derivado de la orden de compra',
    );
    const sucursal = screen.getByLabelText(
      'Sucursal derivada de la orden de compra',
    );
    expect(proveedor).toBeDisabled();
    expect(sucursal).toBeDisabled();
    expect(proveedor).toHaveValue('');
    expect(sucursal).toHaveValue('');
  });

  it('al elegir una OC, deriva proveedor y sucursal y los muestra read-only', async () => {
    render(<CapturarFacturaSheet open onOpenChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // Abrir el selector: la opción muestra la razón social del resumen
    // (proveedorNombre, resuelto server-side), NO el GUID del proveedor.
    fireEvent.click(screen.getByRole('combobox', { name: /orden de compra/i }));
    expect(await screen.findByText('Proveedor Demo SA')).toBeInTheDocument();
    expect(screen.queryByText(PROV_ID)).not.toBeInTheDocument();

    fireEvent.click(await screen.findByText('OC-MID2026-000123'));

    // Proveedor derivado del resumen, mostrado como razón social y disabled.
    const proveedor = await screen.findByDisplayValue('Proveedor Demo SA');
    expect(proveedor).toBeDisabled();

    // Sucursal derivada del resumen, resuelta a "clave · nombre" y disabled.
    const sucursal = await screen.findByDisplayValue('MID · Mérida');
    expect(sucursal).toBeDisabled();
  });
});
