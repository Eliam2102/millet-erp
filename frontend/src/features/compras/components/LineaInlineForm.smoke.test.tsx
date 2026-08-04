import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { LineaResponse } from '@/features/compras/api/types';

function makeLinea(overrides: Partial<LineaResponse> = {}): LineaResponse {
  return {
    id: 'l-1',
    posicion: 1,
    articuloId: 'art-1',
    cantidad: 5,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    cuentaContableId: null,
    centroCostoId: null,
    centroCostoClave: null,
    centroCostoNombre: null,
    proyecto: null,
    fechaRequerida: null,
    notas: null,
    cantDeAlmacen: 0,
    cantDeCompra: 0,
    cantRecibida: 0,
    cantPendiente: 5,
    reservaId: null,
    ...overrides,
  };
}

beforeEach(() => {
  // Catálogo de artículos mínimo para que el <ArticuloSelector> de
  // dentro del form pueda montar sin error.
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    // Modo agregar dispara el prellenado del CC-Máquina (/dim3/buscar?limit=2);
    // lista vacía → sin prellenado, no interfiere con estos smoke tests.
    http.get('*/api/v1/centros-costo/dim3/buscar', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<LineaInlineForm> — smoke', () => {
  it('modo agregar: muestra "Agregar línea" + border dashed primary', () => {
    const { container } = render(
      <LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /agregar línea/i }),
    ).toBeInTheDocument();
    // El aria-label del form lo identifica como modo agregar.
    expect(
      screen.getByRole('form', { name: /agregar línea/i }),
    ).toBeInTheDocument();
    // Border dashed (modo agregar).
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-dashed/);
    expect(form?.className).not.toMatch(/border-amber/);
  });

  it('modo editar: muestra "Guardar cambios" + border amber', () => {
    const { container } = render(
      <LineaInlineForm
        requisicionId="rq-1"
        linea={makeLinea({ posicion: 3 })}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('button', { name: /guardar cambios/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('form', { name: /editar línea 3/i }),
    ).toBeInTheDocument();
    // Border amber (modo editar) — UX warning de "modificando registro
    // existente".
    const form = container.querySelector('form');
    expect(form?.className).toMatch(/border-amber/);
    expect(form?.className).not.toMatch(/border-dashed/);
  });

  it('Esc llama onCancel cuando no está pending', () => {
    const onCancel = vi.fn();
    render(
      <LineaInlineForm requisicionId="rq-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    const form = screen.getByRole('form', { name: /agregar línea/i });
    fireEvent.keyDown(form, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('botón Cancelar llama onCancel', () => {
    const onCancel = vi.fn();
    render(
      <LineaInlineForm requisicionId="rq-1" onCancel={onCancel} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('toggle "+ Detalles" expande la fila opcional', () => {
    render(
      <LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    // Colapsado por default. El toggle dice "Detalles (cuenta contable,
    // proyecto, notas)" — el CC-Máquina ya NO vive aquí (Fase E PR2: salió
    // a su sub-fila siempre-visible).
    const toggle = screen.getByRole('button', {
      name: /^Detalles \(cuenta contable/i,
    });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    // Expandido: quedan 2 inputs "opcional" (proyecto + cuenta contable);
    // el CC-Máquina se movió fuera.
    expect(
      screen.getAllByPlaceholderText('opcional').length,
    ).toBeGreaterThanOrEqual(2);
  });

  it('CC-Máquina: el picker está siempre visible (fuera de Detalles) en modo agregar', () => {
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    // Sin expandir "Detalles": el combobox de máquina ya está montado.
    expect(
      screen.getByRole('combobox', { name: /seleccionar máquina/i }),
    ).toBeInTheDocument();
  });

  it('CC-Máquina: en edición muestra el initialLabel del DTO enriquecido (clave — nombre)', () => {
    render(
      <LineaInlineForm
        requisicionId="rq-1"
        linea={makeLinea({
          centroCostoId: 'm-1',
          centroCostoClave: 'MCLC101',
          centroCostoNombre: 'Gantry',
        })}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('MCLC101 — Gantry')).toBeInTheDocument();
  });
});
