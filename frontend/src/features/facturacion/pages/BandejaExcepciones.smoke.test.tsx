import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaExcepciones } from '@/features/facturacion/pages/BandejaExcepciones';
import { useAuthStore } from '@/lib/auth/auth-store';

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => setPermisos(['facturacion.pedidos.excepciones-resolver']));
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

const excepcion = {
  id: 'ex-1',
  origen: 'Aw',
  pedidoRef: 'AW-123',
  motivo: 'ClienteNoExiste',
  detalle: 'RFC sin alta en el maestro',
  resuelto: false,
  createdAt: '2026-05-30T10:00:00Z',
};

describe('<BandejaExcepciones> — smoke', () => {
  it('lista excepciones y permite resolver inline', async () => {
    let resuelto = false;
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/excepciones', () =>
        HttpResponse.json(resuelto ? [] : [excepcion]),
      ),
      http.post(
        '*/api/v1/facturacion/pedidos-facturables/excepciones/ex-1/resolver',
        () => {
          resuelto = true;
          return HttpResponse.json({ id: 'ex-1', resuelto: true });
        },
      ),
    );
    render(<BandejaExcepciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('AW-123')).toBeInTheDocument());
    expect(screen.getByText('ClienteNoExiste')).toBeInTheDocument();
    const btn = screen.getByRole('button', { name: /Marcar resuelta/i });
    fireEvent.click(btn);
    await waitFor(() =>
      expect(screen.queryByText('AW-123')).not.toBeInTheDocument(),
    );
  });

  it('estado empty cuando no hay excepciones pendientes', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/excepciones', () =>
        HttpResponse.json([]),
      ),
    );
    render(<BandejaExcepciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin excepciones/i)).toBeInTheDocument(),
    );
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/excepciones', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaExcepciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las excepciones/i),
      ).toBeInTheDocument(),
    );
  });
});
