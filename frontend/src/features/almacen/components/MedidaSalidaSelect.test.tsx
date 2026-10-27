import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MedidaSalidaSelect } from '@/features/almacen/components/MedidaSalidaSelect';

/** F1-MP-01 S4: la medida se ofrece solo si el artículo tiene existencia por medida. */
const fila = (extra: object) => ({
  ubicacionId: 'u1', clave: 'R-1', nombre: 'Rack', subAlmacenClave: 'S', almacenClave: 'A', esDefault: false,
  cantidad: 10, cantidadDisponible: 10, costoPromedioMxn: 1, ...extra,
});

describe('<MedidaSalidaSelect>', () => {
  it('no muestra nada para un artículo sin existencia por medida', async () => {
    mswServer.use(http.get('*/api/v1/almacen/saldos/por-ubicacion', () => HttpResponse.json([fila({})])));
    const { container } = render(<MedidaSalidaSelect articuloId="a1" value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await new Promise((r) => setTimeout(r, 50));
    expect(container).toBeEmptyDOMElement();
  });

  it('con medidas muestra el selector obligatorio', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos/por-ubicacion', () =>
        HttpResponse.json([
          fila({ productoAwVarianteId: 'v1', anchoMm: 2440, altoMm: 3660, areaHojaM2: 8.9304, cantidad: 178.608 }),
          fila({ ubicacionId: 'u2', productoAwVarianteId: 'v2', anchoMm: 2140, altoMm: 3300, areaHojaM2: 7.062, cantidad: 105.93 }),
        ]),
      ),
    );
    render(<MedidaSalidaSelect articuloId="a1" value={null} onChange={() => {}} ariaLabel="Medida de la hoja, partida 1" />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByRole('combobox', { name: 'Medida de la hoja, partida 1' })).toBeInTheDocument();
  });
});
