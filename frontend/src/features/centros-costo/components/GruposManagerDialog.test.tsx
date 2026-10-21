import { expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { GruposManagerDialog } from './GruposManagerDialog';

it('pagina diez grupos cargados y filtra sin realizar nuevas consultas', async () => {
  let consultas = 0;
  mswServer.use(
    http.get('*/api/v1/centros-costo/grupos-dim2/', ({ request }) => {
      consultas++;
      expect(new URL(request.url).searchParams.get('limit')).toBe('100');
      return HttpResponse.json({
        items: Array.from({ length: 13 }, (_, i) => ({
          id: `grupo-${i}`,
          nombre: `Grupo ${String(i + 1).padStart(2, '0')}`,
          estatus: i % 3,
          version: 1,
        })),
        total: 13,
        offset: 0,
        limit: 100,
      });
    }),
  );
  render(<GruposManagerDialog open onOpenChange={() => {}} recurso="grupos-dim2" />, {
    wrapper: createQueryWrapper(),
  });
  await screen.findByText('Grupo 01');
  expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(11);
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
  expect(screen.getByText('Grupo 13')).toBeInTheDocument();
  expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(4);
  fireEvent.mouseDown(screen.getByRole('tab', { name: 'En revisión' }), {
    button: 0,
    ctrlKey: false,
  });
  expect(screen.getByText('1–4 de 4')).toBeInTheDocument();
  expect(screen.queryByText('Grupo 01')).not.toBeInTheDocument();
  fireEvent.change(screen.getByRole('textbox', { name: 'Buscar grupos cargados' }), {
    target: { value: '12' },
  });
  expect(screen.getByText('Grupo 12')).toBeInTheDocument();
  expect(screen.getByText('1–1 de 1')).toBeInTheDocument();
  expect(consultas).toBe(1);
});
