import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LoteSalidaSelect } from '@/features/almacen/components/LoteSalidaSelect';

/** F1-MP-01 S4b: el lote se ofrece solo si la medida tiene existencia con lote, con las hojas disponibles de cada uno. */
const fila = (extra: object) => ({
  ubicacionId: 'u1', clave: 'R-1', nombre: 'Rack', subAlmacenClave: 'S', almacenClave: 'A', esDefault: false,
  cantidad: 89.304, cantidadDisponible: 89.304, costoPromedioMxn: 1,
  productoAwVarianteId: 'v1', anchoMm: 2440, altoMm: 3660, areaHojaM2: 8.9304, ...extra,
});

describe('<LoteSalidaSelect>', () => {
  it('no muestra nada si la medida solo tiene existencia sin lote', async () => {
    mswServer.use(http.get('*/api/v1/almacen/saldos/por-ubicacion', () => HttpResponse.json([fila({ lote: null })])));
    const { container } = render(
      <LoteSalidaSelect articuloId="a1" varianteId="v1" ubicacionId="u1" value={null} onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    await new Promise((r) => setTimeout(r, 50));
    expect(container).toBeEmptyDOMElement();
  });

  it('con lotes muestra el selector obligatorio y el lote elegido con sus hojas disponibles', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos/por-ubicacion', () =>
        HttpResponse.json([
          fila({ lote: 'L-2610-01' }),
          fila({ lote: 'L-2610-02', cantidad: 44.652 }),
          // Otro rack: no se ofrece cuando ya se eligió el rack u1.
          fila({ ubicacionId: 'u2', lote: 'L-OTRO' }),
        ]),
      ),
    );
    render(
      <LoteSalidaSelect articuloId="a1" varianteId="v1" ubicacionId="u1" value="L-2610-01" onChange={() => {}} ariaLabel="Lote, partida 1" />,
      { wrapper: createQueryWrapper() },
    );
    const combo = await screen.findByRole('combobox', { name: 'Lote, partida 1' });
    expect(combo).toHaveAttribute('aria-required', 'true');
    expect(combo).toHaveTextContent('L-2610-01 · 10 hojas disponibles');
  });
});
