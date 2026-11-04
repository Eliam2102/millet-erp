import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { NuevaComprobacionCajaChicaSheet } from './NuevaComprobacionCajaChicaSheet';

const PROVEEDOR = '22222222-2222-4222-8222-222222222222';

vi.mock('@/components/erp', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/components/erp')>();
  return {
    ...real,
    ProveedorSelector: ({ value }: { value: string | null }) => <span>proveedor: {value ?? 'ninguno'}</span>,
    SucursalSelector: () => null,
    EmpleadoSelector: () => null,
  };
});
vi.mock('@/features/cxp/components/CfdiPorProcesarPicker', () => ({
  CfdiPorProcesarPicker: ({ onSelect }: { onSelect: (c: unknown) => void }) => (
    <button
      type="button"
      onClick={() => onSelect({
        id: 'cfdi-1', uuidCfdi: '5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B', rfcEmisor: 'FSS990101AB1', tipo: 1,
        folio: '10', serie: 'A', fechaCfdi: '2026-11-04T00:00:00Z', total: 290, subtotal: 250,
        impuestosTrasladados: 40, retenciones: 0, tipoCambio: null, moneda: 'MXN', canalOrigen: 1,
        fechaRecepcion: '2026-11-04T00:00:00Z', estado: 1,
      })}
    >
      Vincular CFDI
    </button>
  ),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' }, empresas: [], currentEmpresaId: 'e',
    permisos: ['cuentas_por_pagar.cfdis.leer'], errorMessage: null,
  });
});
afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [] }));

describe('<NuevaComprobacionCajaChicaSheet>', () => {
  it('F-CXP-07: al vincular un CFDI precarga el proveedor único de su RFC emisor', async () => {
    let rfcBuscado: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', ({ request }) => {
        rfcBuscado = new URL(request.url).searchParams.get('rfc');
        return HttpResponse.json({ items: [{ id: PROVEEDOR, rfc: 'FSS990101AB1' }], offset: 0, limit: 2, total: 1 });
      }),
      http.get('*/api/v1/catalogos/proveedores/:id', () => HttpResponse.json({ id: PROVEEDOR, rfc: 'FSS990101AB1' })),
    );
    render(<NuevaComprobacionCajaChicaSheet open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });

    expect(screen.getByText('proveedor: ninguno')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Vincular CFDI' }));

    expect(await screen.findByText(`proveedor: ${PROVEEDOR}`)).toBeInTheDocument();
    expect(rfcBuscado).toBe('FSS990101AB1');
  });
});
