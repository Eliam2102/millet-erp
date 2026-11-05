import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NuevoAnticipo, ResolverAnticipo } from './FinanzasPanel';
import type { Anticipo, Gasto } from '../api/finanzas';
import type { ExpedienteDetalle } from '../api/types';

const mocks = vi.hoisted(() => ({ mutateAsync: vi.fn().mockResolvedValue({}) }));
vi.mock('../api/finanzas', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/finanzas')>()),
  useMutarFinanzas: () => ({ mutateAsync: mocks.mutateAsync }),
  useFinanzas: () => ({ data: undefined }),
}));
vi.mock('../api/hooks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/hooks')>()),
  useExpedientes: () => ({ data: undefined }),
}));
vi.mock('./ReferenciaSelector', () => ({
  ReferenciaSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange('agente')}>
      Elegir agente de prueba
    </button>
  ),
}));
const data = {
  expediente: { id: 'e' },
  documentos: [{ id: 'd', estado: 1, adjuntoId: 'doc', tipoDocumentoId: 't', numeroVersion: 1 }],
  requisitos: [{ id: 't', nombre: 'Evidencia' }],
} as unknown as ExpedienteDetalle;
const a = { id: 'a', agenteId: 'agente', aplicado: null, saldo: null } as Anticipo;
const gastos = [
  { id: 'g', proveedorId: 'agente', concepto: 'Honorarios', factura: 'H-1', importeMxn: 7500 },
  { id: 'otro', proveedorId: 'otro', concepto: 'Ajeno', importeMxn: 1 },
] as Gasto[];
describe('P14 anticipo del agente', () => {
  it('oculta gastos para PECA y los muestra para el anticipo general', () => {
    const { rerender } = render(
      <ResolverAnticipo
        data={data}
        a={{ ...a, cuentaPeca: true }}
        gastos={gastos}
        onClose={() => {}}
      />,
    );
    expect(screen.queryByText('Gastos del agente a comprobar')).not.toBeInTheDocument();
    rerender(
      <ResolverAnticipo
        data={data}
        a={{ ...a, cuentaPeca: false }}
        gastos={gastos}
        onClose={() => {}}
      />,
    );
    expect(screen.getByText('Gastos del agente a comprobar')).toBeInTheDocument();
    expect(screen.queryByText(/Ajeno/)).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox')).toHaveAccessibleName(/Honorarios/);
  });
  it('envía saldo-al-agente con referencia y evidencia', async () => {
    render(
      <ResolverAnticipo
        data={data}
        a={{ ...a, aplicado: 142500, saldo: 7500 }}
        onClose={() => {}}
      />,
    );
    fireEvent.change(screen.getByLabelText('Resolución *'), {
      target: { value: 'saldo-al-agente' },
    });
    fireEvent.change(screen.getByLabelText('Referencia *'), { target: { value: 'Cuenta agente' } });
    fireEvent.change(screen.getByLabelText('Fecha *'), { target: { value: '2026-10-03' } });
    fireEvent.change(screen.getByLabelText('Comprobante de pago o resolución *'), {
      target: { value: 'doc' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos/a/resolver',
        body: expect.objectContaining({
          tipo: 'saldo-al-agente',
          importe: 7500,
          referencia: 'Cuenta agente',
          evidenciaId: 'doc',
        }),
      }),
    );
  });
});

describe('P19 corrección de comprobación', () => {
  it('envía una nueva comprobación con motivo a la ruta de corrección', async () => {
    render(
      <ResolverAnticipo
        data={data}
        a={{ ...a, aplicado: 47000, saldo: 3000 }}
        corregir
        onClose={() => {}}
      />,
    );
    fireEvent.change(screen.getByLabelText('Motivo de corrección *'), {
      target: { value: 'Impuestos corregidos' },
    });
    fireEvent.change(screen.getByLabelText('IVA MXN *'), { target: { value: '46000' } });
    fireEvent.change(screen.getByLabelText('Pedimento pagado *'), { target: { value: 'doc' } });
    fireEvent.change(screen.getByLabelText('Comprobante de pago o resolución *'), {
      target: { value: 'doc' },
    });
    fireEvent.change(screen.getByLabelText('Fecha *'), { target: { value: '2026-10-04' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos/a/corregir',
        body: expect.objectContaining({
          motivo: 'Impuestos corregidos',
          impuestos: { iva: 46000, igi: 0, prv: 0 },
        }),
      }),
    );
  });
});

describe('P19 solicitud de anticipo', () => {
  it('requiere cálculo e importe y envía solicitud sin referencia manual', async () => {
    render(<NuevoAnticipo data={data} onClose={() => {}} />);
    expect(screen.getByLabelText('Cálculo de impuestos (archivo) *')).toBeRequired();
    expect(screen.queryByLabelText('Referencia del pago de Tesorería')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Elegir agente de prueba' }));
    fireEvent.change(screen.getByLabelText('Pedimento *'), { target: { value: 'PED-P19' } });
    fireEvent.change(screen.getByLabelText('IVA MXN *'), { target: { value: '50000' } });
    fireEvent.change(screen.getByLabelText('Importe solicitado MXN *'), {
      target: { value: '50000' },
    });
    fireEvent.change(screen.getByLabelText('Cálculo de impuestos (archivo) *'), {
      target: { value: 'doc' },
    });
    fireEvent.change(screen.getByLabelText('Fecha *'), { target: { value: '2026-10-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar anticipo' }));
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos',
        body: expect.objectContaining({
          agenteId: 'agente',
          importe: 50000,
          soporteCalculoId: 'doc',
        }),
      }),
    );
    expect(mocks.mutateAsync.mock.lastCall?.[0].body).not.toHaveProperty('referenciaPago');
  });
});
