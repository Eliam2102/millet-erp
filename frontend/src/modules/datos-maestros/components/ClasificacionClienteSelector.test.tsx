import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClasificacionClienteSelector } from '@/modules/datos-maestros/components/ClasificacionClienteSelector';

const ID_GOBIERNO = 'x';

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/datos-maestros/clasificaciones-cliente', () => HttpResponse.json([
      { id: 'c-constructora', nombre: 'Constructora', descripcion: '', activa: true, version: 1 },
      { id: ID_GOBIERNO, nombre: 'Gobierno', descripcion: '', activa: false, version: 1 },
    ])),
  );
});

describe('<ClasificacionClienteSelector> — clasificación inactiva asignada', () => {
  it('muestra la inactiva asignada con su marca y el aviso', async () => {
    render(<ClasificacionClienteSelector value={ID_GOBIERNO} onChange={() => {}} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Gobierno \(inactiva\)/)).toBeInTheDocument();
    expect(screen.getByText(/Clasificación inactiva: puedes conservarla/)).toBeInTheDocument();
  });

  it('sin valor no ofrece la inactiva', async () => {
    render(<ClasificacionClienteSelector value="" onChange={() => {}} />, { wrapper: createQueryWrapper() });
    // El trigger se habilita solo cuando el catálogo ya cargó (está deshabilitado mientras carga).
    await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled());
    expect(screen.queryByText(/Gobierno/)).toBeNull();
  });
});
