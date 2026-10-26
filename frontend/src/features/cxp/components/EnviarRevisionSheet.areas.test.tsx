import { fireEvent, render, screen } from '@testing-library/react';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { EnviarRevisionSheet } from './EnviarRevisionSheet';

vi.mock('@/lib/api', () => ({ useFormIdempotencyKey: () => 'DEMO-FICTICIA' }));
vi.mock('@/features/cxp/api/useRevision', () => ({
  useEnviarFacturaARevision: () => ({ isPending: false }),
  useDependenciasRevisoras: () => ({ data: [
    { id: 'dep-compras', codigo: 'COMPRAS', nombre: 'Compras y Adquisiciones' },
    { id: 'dep-cxp', codigo: 'CXP', nombre: 'Cuentas por Pagar' },
  ] }),
  useMotivosRevision: () => ({ data: [
    { id: 'm-danos', nombre: 'Daños o defectos en mercancía', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'COMPRAS', areaAvisadaCodigo: null },
    { id: 'm-noconf', nombre: 'No conformidad', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'AREA_USUARIA', areaAvisadaCodigo: 'COMPRAS' },
  ] }),
}));

beforeAll(() => {
  // Radix Select necesita estas APIs que jsdom no trae.
  Element.prototype.hasPointerCapture = () => false;
  Element.prototype.setPointerCapture = () => {};
  Element.prototype.releasePointerCapture = () => {};
  Element.prototype.scrollIntoView = () => {};
});

// user-event no es dependencia del proyecto: se usa fireEvent sobre el Select real.
async function elegir(combo: string, opcion: RegExp | string) {
  fireEvent.keyDown(screen.getByRole('combobox', { name: combo }), { key: 'Enter' });
  fireEvent.click(await screen.findByRole('option', { name: opcion }));
}

describe('Motivo preselecciona el área revisora', () => {
  it('elegir Daños deja Compras en el área y sigue editable; AREA_USUARIA pide elegirla', async () => {
    render(<EnviarRevisionSheet open onOpenChange={vi.fn()} factura={null} />);

    await elegir('Motivo', /Daños/);
    expect(screen.getByRole('combobox', { name: 'Área revisora' })).toHaveTextContent('Compras y Adquisiciones');

    await elegir('Área revisora', 'Cuentas por Pagar');
    expect(screen.getByRole('combobox', { name: 'Área revisora' })).toHaveTextContent('Cuentas por Pagar');

    await elegir('Motivo', /No conformidad/);
    expect(screen.getByText('Elige el área que pidió la compra.')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Área revisora' })).not.toHaveTextContent('Cuentas por Pagar');
  });
});
