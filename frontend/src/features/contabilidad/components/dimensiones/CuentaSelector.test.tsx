import { beforeAll, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CuentaSelector } from './CuentaSelector';

beforeAll(() => {
  // cmdk usa ResizeObserver y scrollIntoView, que jsdom no trae.
  globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
  window.HTMLElement.prototype.scrollIntoView ??= () => {};
});

const rubroGastos = {
  id: 'r-600',
  codigo: '600.00.00.00',
  nombre: 'GASTOS DE OPERACIÓN',
  clase: 'Rubro',
  tipo: 'Titulo',
};

describe('<CuentaSelector> — grupo de cuentas en reglas (K10.2)', () => {
  it('con incluirRubros pide también los rubros y los marca como grupo', async () => {
    const urls: string[] = [];
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', ({ request }) => {
        urls.push(request.url);
        return HttpResponse.json({ items: [rubroGastos], total: 1 });
      }),
    );
    render(<CuentaSelector value={null} onChange={() => {}} incluirRubros />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));
    expect(await screen.findByText('(grupo)')).toBeInTheDocument();
    expect(urls.at(-1)).not.toContain('clase=');
  });

  it('sin incluirRubros solo pide cuentas', async () => {
    const urls: string[] = [];
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', ({ request }) => {
        urls.push(request.url);
        return HttpResponse.json({ items: [], total: 0 });
      }),
    );
    render(<CuentaSelector value={null} onChange={() => {}} />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox'));
    expect(await screen.findByText('No hay cuentas activas.')).toBeInTheDocument();
    expect(urls.at(-1)).toContain('clase=Cuenta');
  });
});
