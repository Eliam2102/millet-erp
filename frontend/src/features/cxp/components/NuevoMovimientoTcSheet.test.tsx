import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NuevoMovimientoTcSheet } from './NuevoMovimientoTcSheet';

const UUID = '11111111-1111-4111-8111-111111111111';
const mocks = vi.hoisted(() => ({ sinCfdi: vi.fn() }));

vi.mock('@/lib/api', () => ({
  useFormIdempotencyKey: () => 'key',
  esApiError: () => false,
  applyServerErrors: () => false,
}));
vi.mock('@/features/cxp/api/useTarjetasCredito', () => ({
  useRegistrarMovimientoTcSinCfdi: () => ({ mutate: mocks.sinCfdi, isPending: false }),
  useRegistrarMovimientoTcConCfdi: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock('@/features/cxp/components/TarjetaSelector', () => ({
  TarjetaSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange(UUID)}>elegir tarjeta</button>
  ),
}));
vi.mock('@/features/cxp/components/CfdiPorProcesarPicker', () => ({ CfdiPorProcesarPicker: () => null }));
vi.mock('@/components/erp', () => ({
  ProveedorSelector: () => null,
  UsuarioSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange(UUID)}>elegir usuario</button>
  ),
}));
vi.mock('@/features/cxp/components/ClasificacionNoDeducibleSelect', () => ({
  ClasificacionNoDeducibleSelect: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('COMIDAS')}>elegir clasificación</button>
  ),
}));

function llenarSinCfdi() {
  fireEvent.click(screen.getByText('elegir tarjeta'));
  fireEvent.click(screen.getByText('elegir usuario'));
  fireEvent.change(screen.getByPlaceholderText('Como aparece en el ticket'), { target: { value: 'Restaurante' } });
  fireEvent.change(screen.getByPlaceholderText('Alimentos clientes'), { target: { value: 'Comidas' } });
  fireEvent.change(screen.getAllByRole('spinbutton')[0], { target: { value: '350' } });
}

describe('NuevoMovimientoTcSheet · P8.6', () => {
  it('sin CFDI no guarda sin clasificación y la envía cuando se elige', async () => {
    mocks.sinCfdi.mockClear();
    render(<NuevoMovimientoTcSheet open onOpenChange={vi.fn()} />);
    llenarSinCfdi();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar (sin CFDI)' }));
    expect(await screen.findByText('Selecciona la clasificación del gasto no deducible.')).toBeInTheDocument();
    expect(mocks.sinCfdi).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText('elegir clasificación'));
    fireEvent.click(screen.getByRole('button', { name: 'Registrar (sin CFDI)' }));
    await waitFor(() => expect(mocks.sinCfdi).toHaveBeenCalled());
    expect(mocks.sinCfdi.mock.calls[0][0].command).toEqual(
      expect.objectContaining({ clasificacionNoDeducible: 'COMIDAS', montoOriginal: 350 }),
    );
  });
});
