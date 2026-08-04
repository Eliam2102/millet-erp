import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import type { MonedaResponse } from '@/modules/catalogos/api';

/**
 * <MonedaSelector> sobre el catálogo compartido.monedas. El endpoint
 * GET /api/v1/catalogos/monedas devuelve un array plano (MonedaResponse[]).
 * value/onChange operan sobre el CÓDIGO ISO, no el id.
 */

// id distinto del código a propósito: prueba que onChange devuelve el código,
// no el id/guid del catálogo.
function moneda(
  codigo: string,
  nombre: string,
  activa = true,
): MonedaResponse {
  return { id: `guid-${codigo}`, codigo, nombre, decimales: 2, activa, version: 1 };
}

describe('<MonedaSelector>', () => {
  it('cold value: resuelve el código guardado a "codigo · nombre"', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/monedas', () =>
        HttpResponse.json([
          moneda('MXN', 'Peso Mexicano'),
          moneda('USD', 'Dólar EUA'),
        ]),
      ),
    );

    render(<MonedaSelector value="MXN" onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    expect(
      await screen.findByText('MXN · Peso Mexicano'),
    ).toBeInTheDocument();
  });

  it('vacío (value null) muestra el placeholder', () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    );

    render(
      <MonedaSelector value={null} onChange={() => {}} placeholder="Elige moneda" />,
      { wrapper: createQueryWrapper() },
    );

    expect(screen.getByText('Elige moneda')).toBeInTheDocument();
  });

  it('lista solo monedas activas y devuelve el CÓDIGO al seleccionar (no el id)', async () => {
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/catalogos/monedas', () =>
        HttpResponse.json([
          moneda('MXN', 'Peso Mexicano', true),
          moneda('XTS', 'Moneda Inactiva', false),
        ]),
      ),
    );

    render(<MonedaSelector value={null} onChange={onChange} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));

    // La activa aparece; la inactiva queda filtrada.
    expect(await screen.findByText('Peso Mexicano')).toBeInTheDocument();
    expect(screen.queryByText('Moneda Inactiva')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Peso Mexicano'));

    // Devuelve 'MXN' (código), no 'guid-MXN' (id del catálogo).
    expect(onChange).toHaveBeenCalledWith('MXN');
  });
});
