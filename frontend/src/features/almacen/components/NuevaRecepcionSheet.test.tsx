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
const useOrdenCompraMock = vi.hoisted(() => vi.fn((_id: string | null) => ({ data: undefined })));
vi.mock('@/features/compras/ordenes/api/useOrdenCompra', () => ({
  useOrdenCompra: useOrdenCompraMock,
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

describe('<NuevaRecepcionSheet> — confirmación de descarte', () => {
  it('sin cambios, Esc cierra directo sin pedir confirmación', () => {
    const onOpenChange = renderSheet();

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.queryByText('¿Descartar los cambios?')).toBeNull();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('con cambios sin guardar, Esc abre el diálogo y «Seguir editando» no cierra', () => {
    const onOpenChange = renderSheet();
    escribirObservaciones();

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.getByText('¿Descartar los cambios?')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Seguir editando' }));
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('con cambios sin guardar, Cancelar abre el diálogo y «Descartar» cierra', () => {
    const onOpenChange = renderSheet();
    escribirObservaciones();

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('cambiar de variante con datos pide confirmación', () => {
    renderSheet();
    escribirObservaciones();

    fireEvent.click(screen.getByRole('button', { name: /Variante B/ }));

    expect(screen.getByText('¿Cambiar el tipo de recepción?')).toBeTruthy();
  });
});

describe('<NuevaRecepcionSheet> — OC inicial', () => {
  it('siembra la OC recibida en ordenCompraIdInicial', () => {
    useOrdenCompraMock.mockClear();
    render(<NuevaRecepcionSheet open onOpenChange={vi.fn()} ordenCompraIdInicial="oc-123" />, {
      wrapper: createQueryWrapper(),
    });
    expect(useOrdenCompraMock).toHaveBeenCalledWith('oc-123');
  });
});
