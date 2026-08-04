import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { Dim3Picker } from './Dim3Picker';
import {
  EstatusCatalogo,
  type Dim3BusquedaItem,
} from '@/features/centros-costo/api/types';

/**
 * Tests del selector server-side de Dim3 (Fase E, PR1). El picker es
 * endpoint-agnóstico: aquí se prueba contra una ruta cualquiera con MSW. El
 * FILTRADO vs ABIERTO lo decide el backend detrás de esa URL — no el
 * componente —, así que estos tests valen para ambos modos.
 */

const ENDPOINT = '/api/v1/centros-costo/dim3/buscar';

const GANTRY: Dim3BusquedaItem = {
  id: 'm-1',
  clave: 'MCLC101',
  nombre: 'Gantry',
  grupoDim3Nombre: 'LINEA',
  dim2Clave: '20PDMC',
  dim2Nombre: 'Corte',
  dim1Clave: '101',
  dim1Nombre: 'Conkal',
  estatus: EstatusCatalogo.Activo,
};

function mockBusqueda(
  items: Dim3BusquedaItem[],
  onUrl?: (url: string) => void,
) {
  mswServer.use(
    http.get(`*${ENDPOINT}`, ({ request }) => {
      onUrl?.(request.url);
      return HttpResponse.json(items);
    }),
  );
}

afterEach(cleanup);

describe('<Dim3Picker>', () => {
  it('muestra el placeholder cuando value es null', () => {
    render(<Dim3Picker value={null} onChange={() => {}} endpoint={ENDPOINT} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('Buscar máquina…')).toBeInTheDocument();
  });

  it('al seleccionar dispara onChange(id) y onSelect(item)', async () => {
    mockBusqueda([GANTRY]);
    const onChange = vi.fn();
    const onSelect = vi.fn();
    render(
      <Dim3Picker
        value={null}
        onChange={onChange}
        onSelect={onSelect}
        endpoint={ENDPOINT}
      />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('combobox'));
    // La opción ahora es de 2 líneas: L1 "clave — nombre" (no "Gantry" solo).
    fireEvent.click(await screen.findByText('MCLC101 — Gantry'));

    expect(onChange).toHaveBeenCalledWith('m-1');
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'm-1' }));
  });

  it('cada opción son 2 líneas: "clave — nombre" (L1) y "Dim1 · Dim2" (L2), sin badge', async () => {
    // Administrativa: Dim2 = nombre de la Dim3 (área = unidad por diseño del
    // catálogo). Regla A: L2 se muestra igual; el tono gris la lee como contexto.
    const ADMIN: Dim3BusquedaItem = {
      id: 'm-2',
      clave: 'CHDIR01',
      nombre: 'Dirección de Capital Humano',
      grupoDim3Nombre: 'ADMIN',
      dim2Clave: 'DCH',
      dim2Nombre: 'Dirección de Capital Humano',
      dim1Clave: '101',
      dim1Nombre: 'Conkal',
      estatus: EstatusCatalogo.Activo,
    };
    mockBusqueda([GANTRY, ADMIN]);
    render(<Dim3Picker value={null} onChange={() => {}} endpoint={ENDPOINT} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('combobox'));

    // Productiva: L1 "clave — nombre", L2 "Dim1 · Dim2" (nombres distintos).
    expect(await screen.findByText('MCLC101 — Gantry')).toBeInTheDocument();
    expect(screen.getByText('Conkal · Corte')).toBeInTheDocument();

    // Administrativa: L1 y L2 repiten el nombre; Regla A lo acepta (2 nodos
    // distintos, "CHDIR01 — …" vs "Conkal · …").
    expect(
      screen.getByText('CHDIR01 — Dirección de Capital Humano'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Conkal · Dirección de Capital Humano'),
    ).toBeInTheDocument();

    // Código muerto borrado: ni clave-sola ni badge "(inactiva)".
    expect(screen.queryByText('MCLC101')).not.toBeInTheDocument();
    expect(screen.queryByText('(inactiva)')).not.toBeInTheDocument();
  });

  it('manda ?q= al escribir (debounce) y solo busca con el popover abierto', async () => {
    let urlVisto: string | null = null;
    mockBusqueda([], (url) => {
      urlVisto = url;
    });
    render(<Dim3Picker value={null} onChange={() => {}} endpoint={ENDPOINT} />, {
      wrapper: createQueryWrapper(),
    });

    // Cerrado: la query no corre (enabled: open) → sin request.
    expect(urlVisto).toBeNull();

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(
      screen.getByPlaceholderText('Buscar máquina…'),
      { target: { value: 'gantry' } },
    );
    await waitFor(() => expect(urlVisto).toContain('q=gantry'));
  });

  it('usa initialLabel en el trigger sin abrir (no depende del top-N)', () => {
    render(
      <Dim3Picker
        value="m-1"
        onChange={() => {}}
        endpoint={ENDPOINT}
        initialLabel="MCLC101 — Gantry"
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('MCLC101 — Gantry')).toBeInTheDocument();
  });

  it('deshabilita el trigger con disabled', () => {
    render(
      <Dim3Picker
        value={null}
        onChange={() => {}}
        endpoint={ENDPOINT}
        disabled
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('combobox')).toBeDisabled();
  });

  it('marca aria-invalid cuando invalid', () => {
    render(
      <Dim3Picker value={null} onChange={() => {}} endpoint={ENDPOINT} invalid />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-invalid', 'true');
  });
});
