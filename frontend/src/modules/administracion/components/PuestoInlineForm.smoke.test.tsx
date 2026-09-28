import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PuestoInlineForm } from '@/modules/administracion/components/PuestoInlineForm';
import type { PuestoListItem } from '@/features/catalogos/api';

const puestoConDepartamento: PuestoListItem = {
  id: 'p-1',
  clave: 'GER',
  nombre: 'Gerente General',
  estatus: 0,
  departamentoId: 'd-1',
  departamentoNombre: 'Dirección',
  rolSugeridoId: null,
};

describe('<PuestoInlineForm> — smoke y desvinculación', () => {
  it('modo agregar: muestra "Agregar puesto" y selector de departamento de referencia', () => {
    const { container } = render(
      <PuestoInlineForm onCancel={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      screen.getByRole('button', { name: /agregar puesto/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/departamento de referencia/i)).toBeInTheDocument();
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
  });

  it('modo editar: clave disabled y permite desvincular el departamento', async () => {
    mswServer.use(
      http.patch('*/api/v1/admin/puestos/p-1', async () => {
        return HttpResponse.json({
          id: 'p-1',
          clave: 'GER',
          nombre: 'Gerente General',
          estatus: 0,
          departamentoId: null,
          rolSugeridoId: null,
        });
      }),
    );

    const onSaved = vi.fn();
    render(
      <PuestoInlineForm
        puesto={puestoConDepartamento}
        onCancel={vi.fn()}
        onSaved={onSaved}
      />,
      { wrapper: createQueryWrapper() },
    );

    const claveInput = screen.getByDisplayValue('GER') as HTMLInputElement;
    expect(claveInput.disabled).toBe(true);

    // Guardar cambios directamente (sin modificar depto o limpiando)
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled();
    });
  });

  it('reintentar con clave distinta envía una idempotency key nueva', async () => {
    const keysEnviadas: string[] = [];
    mswServer.use(
      http.post('*/api/v1/admin/puestos', async ({ request }) => {
        keysEnviadas.push(request.headers.get('idempotency-key') ?? '');
        const body = (await request.json()) as { clave?: string; nombre?: string };
        if (body.clave === 'DUP') {
          return HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Conflicto',
              status: 409,
              code: 'PUESTO_CLAVE_DUPLICADA',
              detail: 'Ya existe un puesto con esa clave.',
            },
            { status: 409 },
          );
        }
        return HttpResponse.json({
          id: 'p-new',
          clave: body.clave,
          nombre: body.nombre,
          estatus: 0,
          departamentoId: null,
          rolSugeridoId: null,
        });
      }),
    );

    render(<PuestoInlineForm onCancel={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });

    const claveInput = screen.getByPlaceholderText('GER');
    const nombreInput = screen.getByPlaceholderText('Gerente');

    // Primer intento con clave DUP (duplicada)
    fireEvent.change(claveInput, { target: { value: 'DUP' } });
    fireEvent.change(nombreInput, { target: { value: 'Puesto Prueba' } });
    fireEvent.click(screen.getByRole('button', { name: /agregar puesto/i }));

    await waitFor(() => {
      expect(keysEnviadas.length).toBe(1);
    });

    // Segundo intento cambiando clave a OK (válida) en el mismo formulario montado
    fireEvent.change(claveInput, { target: { value: 'OK' } });
    fireEvent.click(screen.getByRole('button', { name: /agregar puesto/i }));

    await waitFor(() => {
      expect(keysEnviadas.length).toBe(2);
    });

    // Las dos keys deben ser distintas (evitando 422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY)
    expect(keysEnviadas[0]).not.toBe(keysEnviadas[1]);
    expect(keysEnviadas[0]).toBeTruthy();
    expect(keysEnviadas[1]).toBeTruthy();
  });
});
