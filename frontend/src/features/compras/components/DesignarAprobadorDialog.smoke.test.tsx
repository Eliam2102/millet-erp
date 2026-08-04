import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DesignarAprobadorDialog } from '@/features/compras/components/DesignarAprobadorDialog';

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
}

describe('<DesignarAprobadorDialog> — smoke', () => {
  it('open=false: no renderiza', () => {
    setupCatalogos();
    render(
      <DesignarAprobadorDialog open={false} onOpenChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByText('Designar aprobador')).not.toBeInTheDocument();
  });

  it('open=true: muestra título, los 3 campos requeridos y CTA', () => {
    setupCatalogos();
    render(
      <DesignarAprobadorDialog open={true} onOpenChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('heading', { name: /designar aprobador/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Departamento')).toBeInTheDocument();
    expect(screen.getByText('Rol')).toBeInTheDocument();
    expect(screen.getByText('Usuario')).toBeInTheDocument();
    expect(screen.getByText(/motivo \(opcional\)/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /^designar$/i }),
    ).toBeInTheDocument();
  });
});
