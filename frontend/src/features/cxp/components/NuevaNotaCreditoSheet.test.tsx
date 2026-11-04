import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaNotaCreditoSheet } from './NuevaNotaCreditoSheet';

const mutate = vi.fn();
vi.mock('@/features/cxp/api/useNotasYAnticipos', () => ({
  useCapturarNotaCredito: () => ({ mutate, isPending: false }),
}));
vi.mock('@/features/cxp/components/CfdiPorProcesarPicker', () => ({
  CfdiPorProcesarPicker: () => null,
}));
vi.mock('@/components/erp', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/components/erp')>();
  return {
    ...real,
    ProveedorSelector: ({ onChange }: { onChange: (id: string) => void }) => (
      <button type="button" onClick={() => onChange('11111111-1111-4111-8111-111111111111')}>
        elegir proveedor
      </button>
    ),
  };
});

describe('<NuevaNotaCreditoSheet>', () => {
  it('«Devolución (03)» capturada a mano se envía con relación 03 (F-ALM-02)', async () => {
    render(<NuevaNotaCreditoSheet open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText(/^UUID CFDI\b(?! relacionado)/), {
      target: { value: '4B2B280E-C510-4F58-A9D1-E5B0004FB321' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'elegir proveedor' }));
    fireEvent.change(screen.getByLabelText(/UUID CFDI relacionado/), {
      target: { value: '5C3C391F-D621-4069-B0E2-F6C1115FC432' },
    });
    fireEvent.change(screen.getByLabelText(/^Total/), { target: { value: '3780' } });

    const tipo = screen.getByRole('combobox', { name: /Tipo NC/ });
    fireEvent.keyDown(tipo, { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Devolución (03)' }));

    fireEvent.click(screen.getByRole('button', { name: 'Capturar NC' }));
    await waitFor(() => expect(mutate).toHaveBeenCalled());
    expect(mutate.mock.calls[0][0].command).toMatchObject({ tipo: 2, tipoRelacionCfdi: 3 });
  });
});
