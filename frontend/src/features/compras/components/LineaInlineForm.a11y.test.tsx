import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';

beforeEach(() => {
  // Catálogo de artículos mínimo para que el <ArticuloSelector> de
  // dentro del form pueda montar sin error.
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    // Modo agregar dispara el prellenado del CC-Máquina (/dim3/buscar?limit=2);
    // lista vacía → sin prellenado, no interfiere con estos smoke tests.
    http.get('*/api/v1/compras/requisiciones/:id/lineas/centro-costo-captura', () => HttpResponse.json({ heredado: null, puedeElegir: true, unicaOpcion: null, mensaje: null })),
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

describe('<LineaInlineForm> — accesibilidad y campos (UX-F2-COM-02)', () => {
  it('cada campo crítico tiene label asociado y Centro de costo es obligatorio', () => {
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByLabelText(/^Artículo/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Cantidad/)).toHaveAttribute('aria-required', 'true');
    expect(screen.getByLabelText(/^Fecha/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Centro de costo/)).toHaveAttribute('aria-required', 'true');
  });

  it('no ofrece Cuenta contable (Q2) y los detalles exponen Proyecto y Notas con label', () => {
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('button', { name: /^Detalles/ }));
    expect(screen.queryByLabelText(/cuenta contable/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Proyecto')).toBeInTheDocument();
    expect(screen.getByLabelText('Notas')).toBeInTheDocument();
  });
});
