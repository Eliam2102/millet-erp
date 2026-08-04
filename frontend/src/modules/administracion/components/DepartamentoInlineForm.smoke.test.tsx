import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { DepartamentoInlineForm } from '@/modules/administracion/components/DepartamentoInlineForm';

const departamentoExistente = {
  id: 'd-1',
  clave: 'COMP',
  nombre: 'Compras',
  estatus: 0,
  version: 1,
};

describe('<DepartamentoInlineForm> — smoke', () => {
  it('modo agregar: muestra "Agregar departamento" + border dashed', () => {
    const { container } = render(
      <DepartamentoInlineForm empresaId="e-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /agregar departamento/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
  });

  it('modo editar: clave read-only + border amber + label "Guardar cambios"', () => {
    const { container } = render(
      <DepartamentoInlineForm
        empresaId="e-1"
        departamento={departamentoExistente}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /guardar cambios/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-amber/);
    const claveInput = screen.getByPlaceholderText('COMP') as HTMLInputElement;
    expect(claveInput.readOnly).toBe(true);
  });

  it('Esc llama onCancel', () => {
    const onCancel = vi.fn();
    render(
      <DepartamentoInlineForm empresaId="e-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.keyDown(screen.getByRole('form'), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
