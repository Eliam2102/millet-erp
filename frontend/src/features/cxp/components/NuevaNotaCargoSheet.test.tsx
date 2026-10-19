import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaNotaCargoSheet } from './NuevaNotaCargoSheet';
import { vi } from 'vitest';

vi.mock('@/components/erp', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/components/erp')>();
  return { ...real, ProveedorSelector: () => null, SucursalSelector: () => null };
});

describe('<NuevaNotaCargoSheet>', () => {
  it('asocia labels a sus controles y oculta el GUID crudo de devolución', () => {
    render(<NuevaNotaCargoSheet open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    expect(screen.getByLabelText(/Concepto/)).toBeInTheDocument();
    expect(screen.queryByText(/Devolución a proveedor/)).toBeNull();
  });

  it('con cambios, cerrar pide confirmar el descarte antes de cerrar', async () => {
    const cambios: boolean[] = [];
    render(<NuevaNotaCargoSheet open onOpenChange={(o) => cambios.push(o)} />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText(/Concepto/), { target: { value: 'Flete duplicado' } });
    await waitFor(() => expect(screen.getByLabelText(/Concepto/)).toHaveValue('Flete duplicado'));
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(await screen.findByText('¿Descartar los cambios?')).toBeInTheDocument();
    expect(cambios).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
    expect(cambios).toEqual([false]);
  });

  it('sin cambios, cancelar cierra directo', () => {
    const cambios: boolean[] = [];
    render(<NuevaNotaCargoSheet open onOpenChange={(o) => cambios.push(o)} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(cambios).toEqual([false]);
  });
});
