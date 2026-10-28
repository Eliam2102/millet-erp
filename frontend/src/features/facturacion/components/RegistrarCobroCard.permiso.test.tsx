import { render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { createQueryWrapper } from '@/test/test-query-client';
import { RegistrarCobroCard } from './RegistrarCobroCard';

const useSesionActual = vi.hoisted(() => vi.fn(() => ({ data: undefined })));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@/features/facturacion/api/useCajaSesiones', () => ({
  useSesionActual,
  useRegistrarCobro: () => ({ mutate: vi.fn(), isPending: false }),
}));

describe('86e3tfdwu · cobro sin permiso de caja', () => {
  it('no consulta la sesión (el endpoint respondería 403) ni muestra el cobro', () => {
    const { container } = render(
      <RegistrarCobroCard comprobanteId="c-1" folio="FV-1" total={100} moneda="MXN" />,
      { wrapper: createQueryWrapper() },
    );
    expect(useSesionActual).toHaveBeenCalledWith({ enabled: false });
    expect(container).toBeEmptyDOMElement();
  });
});
