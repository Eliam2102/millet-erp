import { render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RecientesInicio } from './RecientesInicio';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

it('muestra nombres legibles, respeta la etiqueta y elimina repeticiones en el resumen', async () => {
  mswServer.use(
    http.get('*/api/v1/administracion/auditoria', () => HttpResponse.json({ items: [] })),
  );
  mswServer.use(
    http.get('*/api/*', () =>
      HttpResponse.json({
        items: [
          {
            id: '1',
            entidad: 'SolicitudCatalogo',
            entidadEtiqueta: 'SolicitudCatalogo 01a88e9',
            resumen: 'Modificó SolicitudCatalogo SolicitudCatalogo',
            timestamp: new Date().toISOString(),
            operacion: 'Modificar',
          },
          {
            id: '2',
            entidad: 'OrdenCompra',
            entidadEtiqueta: 'OC-2026-001',
            resumen: 'Creó OrdenCompra',
            timestamp: new Date().toISOString(),
            operacion: 'Crear',
          },
        ],
      }),
    ),
  );
  const { container } = render(<RecientesInicio usuarioId="u" empresaId="e" />, {
    wrapper: createQueryWrapper(),
  });
  expect(await screen.findByText('Solicitud de catálogo')).not.toHaveClass('font-mono');
  expect(screen.getByText('01a88e9')).toHaveClass('font-mono', 'text-ink-muted');
  expect(screen.getByText('Modificó Solicitud de catálogo')).toBeInTheDocument();
  expect(screen.getByText('OC-2026-001')).toBeInTheDocument();
  expect(container.textContent).not.toMatch(/SolicitudCatalogo|OrdenCompra/);
});

it('no repite la etiqueta en el detalle, muestra «vacío» y recorta sin desbordar', async () => {
  mswServer.use(
    http.get('*/api/*', () =>
      HttpResponse.json({
        items: [
          {
            id: '9',
            entidad: 'LineaRequisicion',
            entidadEtiqueta: 'Linea requisicion 01a1a39c',
            resumen: 'Modificó Linea requisicion 01a1a39c — Notas: null → 9',
            timestamp: new Date().toISOString(),
            operacion: 'Modificar',
          },
        ],
      }),
    ),
  );
  render(<RecientesInicio usuarioId="u" empresaId="e" />, { wrapper: createQueryWrapper() });
  const detalle = await screen.findByText('Modificó · Notas: vacío → 9');
  expect(detalle).toHaveClass('line-clamp-2', 'break-words');
  expect(detalle.closest('a')).toHaveClass('min-w-0');
});
