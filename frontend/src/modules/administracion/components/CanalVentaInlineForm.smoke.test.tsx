import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CanalVentaInlineForm } from '@/modules/administracion/components/CanalVentaInlineForm';

const canalExistente = {
  id: 1,
  nombre: 'Tienda Cancún',
  estatus: 0,
  version: 1,
  claveAw: 'CANCUN',
};

describe('<CanalVentaInlineForm> — smoke (FAC-ING-PR3)', () => {
  it('modo agregar: muestra "Agregar canal" + border dashed', () => {
    const { container } = render(
      <CanalVentaInlineForm onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /agregar canal/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /agregar canal/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
    expect(form?.className).not.toMatch(/border-amber/);
  });

  it('modo editar: muestra "Guardar cambios" + border amber + claveAw prellenada', () => {
    const { container } = render(
      <CanalVentaInlineForm canal={canalExistente} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /guardar cambios/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /editar canal tienda cancún/i }),
    ).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-amber/);
    expect(screen.getByDisplayValue('CANCUN')).toBeInTheDocument();
  });

  it('editar: vaciar la clave A+W manda limpiarClaveAw=true en el PATCH', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch('*/api/v1/admin/canales-venta/1', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json({
          id: 1,
          nombre: 'Tienda Cancún',
          estatus: 0,
          version: 2,
          claveAw: null,
        });
      }),
    );
    const onSaved = vi.fn();
    render(
      <CanalVentaInlineForm
        canal={canalExistente}
        onCancel={() => {}}
        onSaved={onSaved}
      />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.change(screen.getByDisplayValue('CANCUN'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(bodyVisto).toMatchObject({
      nombre: 'Tienda Cancún',
      claveAw: null,
      limpiarClaveAw: true,
    });
  });

  it('Esc llama onCancel cuando no está pending', () => {
    const onCancel = vi.fn();
    render(<CanalVentaInlineForm onCancel={onCancel} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.keyDown(screen.getByRole('form'), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('botón Cancelar llama onCancel', () => {
    const onCancel = vi.fn();
    render(<CanalVentaInlineForm onCancel={onCancel} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
