import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalInlineForm } from '@/modules/administracion/components/SucursalInlineForm';

import { TipoSucursal } from '@/modules/administracion/api/types';

const sucursalExistente = {
  id: 's-1',
  clave: 'MID',
  nombre: 'Mérida',
  tipo: TipoSucursal.Taller,
  estatus: 0,
  version: 1,
};

describe('<SucursalInlineForm> — smoke', () => {
  it('modo agregar: muestra "Agregar sucursal" + border dashed', () => {
    const { container } = render(
      <SucursalInlineForm empresaId="e-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /agregar sucursal/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /agregar sucursal/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
    expect(form?.className).not.toMatch(/border-amber/);
  });

  it('modo editar: muestra "Guardar cambios" + border amber + clave read-only', () => {
    const { container } = render(
      <SucursalInlineForm
        empresaId="e-1"
        sucursal={sucursalExistente}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /guardar cambios/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /editar sucursal mid/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-amber/);

    // Clave debe ser readOnly en modo editar
    const claveInput = screen.getByPlaceholderText('MID') as HTMLInputElement;
    expect(claveInput.readOnly).toBe(true);
  });

  it('Esc llama onCancel cuando no está pending', () => {
    const onCancel = vi.fn();
    render(
      <SucursalInlineForm empresaId="e-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.keyDown(screen.getByRole('form'), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('botón Cancelar llama onCancel', () => {
    const onCancel = vi.fn();
    render(
      <SucursalInlineForm empresaId="e-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('renderiza selector de tipo con opciones Taller y Planta', () => {
    render(
      <SucursalInlineForm empresaId="e-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    const select = screen.getByRole('combobox', { name: /tipo de sucursal/i }) as HTMLSelectElement;
    expect(select).toBeInTheDocument();
    expect(select.value).toBe(String(TipoSucursal.Taller));

    expect(screen.getByRole('option', { name: /taller/i })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /planta/i })).toBeInTheDocument();
  });
});

