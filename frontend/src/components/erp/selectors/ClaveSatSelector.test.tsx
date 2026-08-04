import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClaveSatSelector } from '@/components/erp/selectors/ClaveSatSelector';

/**
 * <ClaveSatSelector/> — catálogos SAT en vivo vía FiscalAPI (FAC-DET-PR3).
 * Cubre: búsqueda lazy + selección (entrega el item completo) y el modo
 * degradado 503 (CATALOGO_SAT_NO_DISPONIBLE → captura manual +
 * fallbackItems) — requisito de aceptación: el flujo nunca se bloquea.
 */

function abrir() {
  fireEvent.click(screen.getByRole('combobox'));
}

describe('<ClaveSatSelector>', () => {
  it('busca al escribir y entrega el item seleccionado por onChange', async () => {
    const buscados: string[] = [];
    mswServer.use(
      http.get('*/api/v1/catalogos/sat/clave-unidad', ({ request }) => {
        buscados.push(new URL(request.url).searchParams.get('buscar') ?? '');
        return HttpResponse.json([
          { codigo: 'MTK', descripcion: 'Metro cuadrado' },
        ]);
      }),
    );
    const onChange = vi.fn();

    render(
      <ClaveSatSelector catalogo="clave-unidad" value={null} onChange={onChange} />,
      { wrapper: createQueryWrapper() },
    );

    abrir();
    fireEvent.change(
      screen.getByLabelText(/buscar clave sat por código o descripción/i),
      { target: { value: 'metro' } },
    );

    // Debounce 300 ms: la request sale con el término completo.
    fireEvent.click(await screen.findByText('Metro cuadrado'));

    expect(onChange).toHaveBeenCalledWith({
      codigo: 'MTK',
      descripcion: 'Metro cuadrado',
    });
    await waitFor(() => expect(buscados).toContain('metro'));
  });

  it('objeto-imp consulta sin texto (lista el catálogo completo)', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/sat/objeto-imp', () =>
        HttpResponse.json([
          { codigo: '01', descripcion: 'No objeto de impuesto.' },
          { codigo: '02', descripcion: 'Sí objeto de impuesto.' },
        ]),
      ),
    );

    render(
      <ClaveSatSelector catalogo="objeto-imp" value={null} onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );

    abrir();
    expect(await screen.findByText('Sí objeto de impuesto.')).toBeInTheDocument();
  });

  it('503 CATALOGO_SAT_NO_DISPONIBLE degrada a captura manual + fallbackItems', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/sat/objeto-imp', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'El catálogo SAT no está disponible.',
            status: 503,
            code: 'CATALOGO_SAT_NO_DISPONIBLE',
          },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const onChange = vi.fn();

    render(
      <ClaveSatSelector
        catalogo="objeto-imp"
        value={null}
        onChange={onChange}
        fallbackItems={[{ codigo: '02', descripcion: 'Sí objeto de impuesto' }]}
      />,
      { wrapper: createQueryWrapper() },
    );

    abrir();
    expect(
      await screen.findByText(/catálogo sat no disponible/i),
    ).toBeInTheDocument();

    // Opción local del fallback sigue seleccionable.
    fireEvent.click(screen.getByRole('button', { name: /02/ }));
    expect(onChange).toHaveBeenCalledWith({
      codigo: '02',
      descripcion: 'Sí objeto de impuesto',
    });

    // Captura manual: el código se normaliza a mayúsculas.
    abrir();
    await screen.findByText(/catálogo sat no disponible/i);
    fireEvent.change(screen.getByLabelText(/código sat manual/i), {
      target: { value: 'h87' },
    });
    fireEvent.click(screen.getByRole('button', { name: /usar código/i }));
    expect(onChange).toHaveBeenLastCalledWith({ codigo: 'H87', descripcion: '' });
  });

  it('muestra initialLabel como cold value y cae al código crudo sin label', () => {
    const { rerender } = render(
      <ClaveSatSelector
        catalogo="clave-prod-serv"
        value="43211701"
        onChange={() => {}}
        initialLabel="43211701 · Computadoras"
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('43211701 · Computadoras')).toBeInTheDocument();

    rerender(
      <ClaveSatSelector
        catalogo="clave-prod-serv"
        value="43211701"
        onChange={() => {}}
      />,
    );
    expect(screen.getByText('43211701')).toBeInTheDocument();
  });
});
