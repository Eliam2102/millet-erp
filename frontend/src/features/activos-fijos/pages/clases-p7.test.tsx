import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ClasesPage } from './ClasesPage';
import { ConfiguracionLibroPage } from './ConfiguracionLibroPage';
const state = vi.hoisted(() => ({ mutate: vi.fn() }));
vi.mock('../api/hooks', () => ({
  useClasesActivo: () => ({
    data: [
      {
        id: 'c',
        version: 1,
        clave: '165',
        nombre: 'Telefonía',
        tasaFiscalAnual: null,
        depreciable: true,
        tasaConfirmada: false,
        requiereRevision: true,
        activa: true,
        cuentaActivoId: 'a',
        cuentaDepreciacionId: null,
        cuentaGastoId: null,
      },
    ],
  }),
  useGuardarActivo: () => ({ mutate: state.mutate }),
}));
vi.mock('../api/libro', () => ({ useLibroQuery: () => ({ data: null }) }));
vi.mock('@/features/contabilidad/api/hooks', () => ({
  useCuenta: (id: string | null) => ({
    data: id ? { data: { codigo: '165.01', nombre: 'Telefonía' } } : null,
  }),
}));
vi.mock('../components/CuentaActivoSelector', () => ({ CuentaActivoSelector: () => null }));
beforeEach(() => state.mutate.mockReset());
it('muestra cuentas, tasa pendiente y revisión sin transformar una tasa vacía en cero', () => {
  render(<ClasesPage />);
  expect(screen.getByText('165.01 · Telefonía')).toBeInTheDocument();
  expect(screen.getByText('Tasa por confirmar')).toBeInTheDocument();
  expect(screen.getByText('Revisar clase anterior')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Editar' }));
  const tasa = screen.getByLabelText('Tasa fiscal anual (sólo reporte)');
  fireEvent.change(tasa, { target: { value: '10' } });
  fireEvent.change(tasa, { target: { value: '' } });
  expect(screen.getByLabelText(/Tasa confirmada con Millet/)).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Guardar clase' }));
  expect(state.mutate).toHaveBeenCalledWith(
    expect.objectContaining({
      body: expect.objectContaining({ tasaFiscalAnual: null, tasaConfirmada: false }),
    }),
    expect.anything(),
  );
});
it('inicia mes siguiente por confirmar y permite guardar mismo mes confirmado', () => {
  render(<ConfiguracionLibroPage />);
  expect(screen.getByLabelText('Mes siguiente al inicio de uso')).toBeChecked();
  expect(screen.getByText('Por confirmar con Millet')).toBeInTheDocument();
  fireEvent.click(screen.getByLabelText('Mismo mes del inicio de uso'));
  fireEvent.click(screen.getByLabelText('Inicio confirmado con Millet'));
  fireEvent.click(screen.getByRole('button', { name: 'Guardar configuración' }));
  expect(state.mutate).toHaveBeenCalledWith(
    expect.objectContaining({
      body: expect.objectContaining({
        depreciarMismoMes: true,
        inicioDepreciacionConfirmado: true,
      }),
    }),
    expect.anything(),
  );
});

it('guarda tasas contable y fiscal diferentes', () => {
  render(<ClasesPage />);
  fireEvent.click(screen.getByRole('button', { name: 'Editar' }));
  fireEvent.change(screen.getByLabelText('Tasa contable anual (genera póliza)'), {
    target: { value: '33' },
  });
  fireEvent.change(screen.getByLabelText('Tasa fiscal anual (sólo reporte)'), {
    target: { value: '30' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar clase' }));
  expect(state.mutate).toHaveBeenCalledWith(
    expect.objectContaining({
      body: expect.objectContaining({ tasaContableAnual: 33, tasaFiscalAnual: 30 }),
    }),
    expect.anything(),
  );
});
