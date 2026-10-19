import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaSolicitudViaticosSheet } from './NuevaSolicitudViaticosSheet';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));
vi.mock('@/components/erp', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/components/erp')>();
  return {
    ...real,
    PuestoSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
      <button type="button" onClick={() => onChange('11111111-1111-4111-8111-111111111111')}>
        Elegir puesto
      </button>
    ),
    EmpleadoSelector: () => null,
  };
});

function politicas(items: unknown[]) {
  mswServer.use(
    http.get('*/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos', () =>
      HttpResponse.json({ items, offset: 0, limit: 200, total: items.length }),
    ),
  );
}

describe('<NuevaSolicitudViaticosSheet>', () => {
  it('sin política para puesto + destino avisa y deshabilita Solicitar con motivo', async () => {
    politicas([]);
    render(<NuevaSolicitudViaticosSheet open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Elegir puesto' }));
    expect(await screen.findByText('Sin política de viáticos')).toBeInTheDocument();
    const boton = screen.getByRole('button', { name: 'Solicitar viáticos' });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute('title', expect.stringContaining('pide a RH'));
  });

  it('con política muestra el tope en vivo (monto máx/día × días)', async () => {
    politicas([
      {
        id: 'p-1',
        puestoId: '11111111-1111-4111-8111-111111111111',
        tipoDestino: 1,
        montoMaxDia: 800,
        diasMax: 5,
        moneda: 'MXN',
        version: 1,
      },
    ]);
    render(<NuevaSolicitudViaticosSheet open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Elegir puesto' }));
    await waitFor(() => expect(screen.getByText('Tope de la política')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Solicitar viáticos' })).toBeEnabled();
  });
});
