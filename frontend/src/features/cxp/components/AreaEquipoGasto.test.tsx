import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AreaEquipoGasto } from './AreaEquipoGasto';
import type { CentroGastoOpcion, DimensionesGasto } from '@/features/cxp/api/useDimensionesGasto';

/** H1 · Área/Equipo en la captura de gastos de CxP: un solo id hacia el formulario. */

const SUCURSAL = '11111111-1111-1111-1111-111111111111';

const VENTAS: CentroGastoOpcion = {
  id: '22222222-2222-2222-2222-222222222222',
  nivel: 2,
  clave: '20VTAS',
  nombre: 'Ventas',
  dim2Id: null,
  dim2Clave: null,
};

function dims(parcial: Partial<DimensionesGasto> = {}): DimensionesGasto {
  return { sucursalId: SUCURSAL, exigeArea: false, exigeEquipo: false, cuentaCodigo: '601-01', ...parcial };
}

afterEach(cleanup);

describe('<AreaEquipoGasto>', () => {
  it('al elegir un área emite su id con nivel 2', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/gastos/centros', () => HttpResponse.json([VENTAS])),
    );
    const onChange = vi.fn();
    render(<AreaEquipoGasto id="t" dimensiones={dims()} value={null} onChange={onChange} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox', { name: /Área/ }));
    fireEvent.click(await screen.findByText('Ventas'));

    expect(onChange).toHaveBeenCalledWith(VENTAS.id, 2);
  });

  it('con exigeArea marca el área como obligatoria y explica la regla C-03', () => {
    render(
      <AreaEquipoGasto id="t" dimensiones={dims({ exigeArea: true })} value={null} onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );

    expect(screen.getByRole('combobox', { name: /Área/ })).toHaveAttribute('aria-required', 'true');
    expect(screen.getByRole('combobox', { name: /Equipo/ })).not.toHaveAttribute('aria-required');
    expect(screen.getByText(/La cuenta 601-01 exige área \(regla de dimensiones C-03\)/)).toBeInTheDocument();
  });
});
