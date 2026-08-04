import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { createQueryWrapper } from '@/test/test-query-client';
import { CierreMesPage } from '@/features/almacen/pages/CierreMesPage';
import { useAuthStore } from '@/lib/auth/auth-store';

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.cierre-mes.ejecutar'],
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

describe('<CierreMesPage> — smoke', () => {
  it('renderiza header + checklist + selector año/mes + botón ejecutar', () => {
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /Cierre de mes/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Checklist antes de cerrar/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/Periodo a cerrar/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Ejecutar cierre/i }),
    ).toBeInTheDocument();
  });

  it('axe-core: cero violations', async () => {
    const { container } = render(<CierreMesPage />, {
      wrapper: createQueryWrapper(),
    });
    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
