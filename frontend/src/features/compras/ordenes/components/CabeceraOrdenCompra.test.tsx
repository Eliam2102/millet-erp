import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CabeceraOrdenCompra } from '@/features/compras/ordenes/components/CabeceraOrdenCompra';
import {
  EstadoOrdenCompra,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const mutate = vi.fn();
vi.mock('@/features/compras/ordenes/api/useActualizarCabeceraOc', () => ({
  useActualizarCabeceraOc: () => ({ mutate, isPending: false }),
}));
vi.mock('@/components/erp/collaboration/conflict-dialog-context', () => ({
  useConflictDialog: () => ({ openSimple: vi.fn() }),
}));
vi.mock('@tanstack/react-query', async (orig) => ({
  ...(await orig<typeof import('@tanstack/react-query')>()),
  useQueryClient: () => ({}),
}));
// Selectores reales hacen fetch de catálogos; aquí basta un botón que elige un id fijo.
vi.mock('@/components/erp/selectors/ProveedorSelector', () => ({
  ProveedorSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('prov-nuevo')}>elegir proveedor</button>
  ),
}));
vi.mock('@/components/erp/selectors/CondicionesPagoSelector', () => ({
  CondicionesPagoSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('cp-nuevo')}>elegir condiciones</button>
  ),
}));
vi.mock('@/components/erp/selectors/UsoPrincipalSelector', () => ({
  UsoPrincipalSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('uso-nuevo')}>elegir uso</button>
  ),
}));

function makeOc(overrides: Partial<OrdenCompraDetalleResponse> = {}): OrdenCompraDetalleResponse {
  return {
    id: 'oc-1',
    empresaId: 'e-1',
    folio: 'OC-MID2026-000001',
    folioAnio: 2026,
    proveedorId: 'p-1',
    sucursalDestinoId: 's-1',
    condicionesPagoId: 'cp-1',
    usoPrincipalId: 'up-1',
    moneda: 'MXN',
    tipoCambio: null,
    compradorTitularId: 'u-1',
    encargadoComprasId: 'u-1',
    fechaDocumento: '2026-05-12',
    estado: EstadoOrdenCompra.Borrador,
    version: 1,
    lineas: [],
    adjuntos: [],
    ...overrides,
  } as unknown as OrdenCompraDetalleResponse;
}

function setPermisos(permisos: string[]) {
  useAuthStore.setState({ permisos } as never);
}

const resolver = (id: string | null | undefined) => id ?? '—';

beforeEach(() => {
  mutate.mockReset();
  setPermisos([PermisosCanonicos.ComprasOrdenesCrear]);
});

describe('CabeceraOrdenCompra — campos por capturar', () => {
  it('con TBD muestra badge «Por capturar» y callout que lista solo lo que falta', () => {
    render(
      <CabeceraOrdenCompra
        oc={makeOc({ camposPorCapturar: ['proveedor', 'usoPrincipal'] })}
        resolverNombre={resolver}
      />,
    );
    expect(screen.getAllByText('Por capturar')).toHaveLength(2);
    expect(screen.getByRole('note')).toHaveTextContent(
      'Falta proveedor y uso principal. Captúralos para poder enviar la OC a autorización.',
    );
  });

  it('sin TBD no hay callout ni badge', () => {
    render(<CabeceraOrdenCompra oc={makeOc({ camposPorCapturar: [] })} resolverNombre={resolver} />);
    expect(screen.queryByRole('note')).toBeNull();
    expect(screen.queryByText('Por capturar')).toBeNull();
  });

  it('sin permiso no hay botón de edición y el callout dice quién puede capturar', () => {
    setPermisos([]);
    render(
      <CabeceraOrdenCompra
        oc={makeOc({ camposPorCapturar: ['proveedor'] })}
        resolverNombre={resolver}
      />,
    );
    expect(screen.queryByRole('button', { name: 'Editar cabecera' })).toBeNull();
    expect(screen.getByRole('note')).toHaveTextContent(
      'Pide a un comprador con permiso de crear OC que los capture',
    );
  });

  it('fuera de Borrador/Rechazada no hay botón de edición', () => {
    render(
      <CabeceraOrdenCompra
        oc={makeOc({ estado: EstadoOrdenCompra.Autorizada })}
        resolverNombre={resolver}
      />,
    );
    expect(screen.queryByRole('button', { name: 'Editar cabecera' })).toBeNull();
  });

  it('guardar manda solo los campos elegidos con Idempotency-Key', async () => {
    render(
      <CabeceraOrdenCompra
        oc={makeOc({ camposPorCapturar: ['proveedor', 'condicionesPago', 'usoPrincipal'] })}
        resolverNombre={resolver}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Editar cabecera' }));
    fireEvent.click(screen.getByRole('button', { name: 'elegir proveedor' }));
    fireEvent.click(screen.getByRole('button', { name: 'elegir uso' }));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cabecera' }));

    await waitFor(() => expect(mutate).toHaveBeenCalledTimes(1));
    const [args] = mutate.mock.calls[0];
    expect(args.ordenCompraId).toBe('oc-1');
    expect(args.command).toEqual({ proveedorId: 'prov-nuevo', usoPrincipalId: 'uso-nuevo' });
    expect(typeof args.idempotencyKey).toBe('string');
  });
});
