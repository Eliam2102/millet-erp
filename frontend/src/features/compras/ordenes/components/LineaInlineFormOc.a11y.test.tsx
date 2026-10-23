import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';

/**
 * Fase E PR3 — Centro de costo (Dim3) en la partida de OC. El campo es CONDICIONAL
 * (ADR-0050): partida MANUAL → picker ABIERTO por proxy (el comprador elige);
 * partida HEREDADA de RQ → display BLOQUEADO read-only (1:1 de la RQ, no editable).
 */

const OC = {
  id: 'oc-1',
  moneda: 'MXN',
} as unknown as OrdenCompraDetalleResponse;

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    // El picker solo consulta con el popover abierto; el mount no lo dispara,
    // pero lo dejamos por robustez si algún día se prellena.
    http.get('*/api/v1/compras/ordenes/centros-costo/buscar', () =>
      HttpResponse.json([]),
    ),
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

describe('<LineaInlineFormOc> — campos accesibles y críticos (UX-F2-COM-02)', () => {
  it('Departamento solicitante y Centro de costo son obligatorios y visibles sin abrir Detalles', () => {
    render(<LineaInlineFormOc oc={OC} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByLabelText(/^Departamento solicitante/)).toHaveAttribute('aria-required', 'true');
    expect(screen.getByLabelText(/^Centro de costo/)).toHaveAttribute('aria-required', 'true');
    expect(screen.getByLabelText(/^Artículo/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Cantidad/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Precio/)).toBeInTheDocument();
  });
});
