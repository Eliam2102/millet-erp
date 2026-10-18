import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';

// Dependencias de datos y navegación: el test solo ejercita el cierre del sheet.
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@/features/almacen/api', () => ({
  useRegistrarRecepcionFactura: () => ({ mutate: vi.fn(), isPending: false }),
  useRegistrarRecepcionPackingList: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock('@/features/almacen/api/useRecepciones', () => ({
  useCfdisParaRecepcion: () => ({ data: undefined }),
  useLineasRecibibles: () => ({ data: undefined }),
}));
vi.mock('@/features/compras/ordenes/api/useOrdenCompra', () => ({
  useOrdenCompra: () => ({ data: undefined }),
}));

afterEach(() => {
  vi.restoreAllMocks();
});

function renderSheet() {
  const onOpenChange = vi.fn();
  render(<NuevaRecepcionSheet open onOpenChange={onOpenChange} />, {
    wrapper: createQueryWrapper(),
  });
  return onOpenChange;
}

function escribirObservaciones() {
  // El textarea de Observaciones no queda asociado a su <label> (TextAreaField
  // no recibe id), así que se localiza por elemento; es el único textarea del sheet.
  const textarea = document.querySelector('textarea');
  if (!textarea) throw new Error('No se encontró el textarea de Observaciones');
  fireEvent.change(textarea, { target: { value: 'Cambio sin guardar' } });
}

describe('<NuevaRecepcionSheet> — confirmación de descarte (patrón P4)', () => {
  it('sin cambios, Esc cierra directo sin pedir confirmación', () => {
    const confirmSpy = vi.spyOn(window, 'confirm');
    const onOpenChange = renderSheet();

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('con cambios sin guardar, Esc pide confirmación y no cierra si se rechaza', () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const onOpenChange = renderSheet();
    escribirObservaciones();

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(confirmSpy).toHaveBeenCalledWith(
      expect.stringContaining('cambios sin guardar'),
    );
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('con cambios sin guardar, Cancelar pide confirmación y cierra si se acepta', () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onOpenChange = renderSheet();
    escribirObservaciones();

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
