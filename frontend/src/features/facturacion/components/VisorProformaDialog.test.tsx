import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient } from '@tanstack/react-query';
import { createQueryWrapper } from '@/test/test-query-client';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { VisorProformaDialog } from './VisorProformaDialog';

const generarBlobSpy = vi.hoisted(() => vi.fn());
vi.mock('@/components/erp/reportes/exporters/proforma-cfdi-exporter', () => ({
  generarProformaPdfBlob: generarBlobSpy,
}));

const PROFORMA = {
  id: 'pf-2',
  pedidoFacturableId: 'p-1',
  folio: 'PF-MID-001-2',
  version: 2,
  generadaEn: '2026-10-21T15:30:00Z',
  generadaPor: 'Ana López',
  numeroPedido: 'MID-001',
  avisos: [],
} as unknown as ProformaPedidoResponse;

let creados: string[];
let revocados: Set<string>;
const sinFragmento = (src: string | null) => (src ?? '').split('#')[0];

beforeEach(() => {
  creados = [];
  revocados = new Set();
  generarBlobSpy.mockReset();
  generarBlobSpy.mockResolvedValue(new Blob(['%PDF'], { type: 'application/pdf' }));
  URL.createObjectURL = vi.fn(() => {
    const url = `blob:proforma-${creados.length + 1}`;
    creados.push(url);
    return url;
  });
  URL.revokeObjectURL = vi.fn((url: string) => {
    revocados.add(url);
  });
});
afterEach(() => vi.restoreAllMocks());

function Visor({ proforma }: { proforma: ProformaPedidoResponse | null }) {
  return (
    <StrictMode>
      <VisorProformaDialog
        proforma={proforma}
        descargando={false}
        onDescargar={() => {}}
        onCerrar={() => {}}
      />
    </StrictMode>
  );
}

describe('<VisorProformaDialog> (U1.8a)', () => {
  it('el PDF del visor nunca apunta a un object URL revocado (StrictMode monta, desmonta y remonta)', async () => {
    render(<Visor proforma={PROFORMA} />, { wrapper: createQueryWrapper() });

    const iframe = await screen.findByTitle('Vista previa de la proforma PF-MID-001-2');
    await waitFor(() => expect(iframe.getAttribute('src')).toMatch(/^blob:/));
    expect(revocados.has(sinFragmento(iframe.getAttribute('src')))).toBe(false);
  });

  it('al volver a abrir la misma proforma no reutiliza el URL revocado al cerrar', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { rerender } = render(<Visor proforma={PROFORMA} />, {
      wrapper: createQueryWrapper(client),
    });
    const primero = await screen.findByTitle('Vista previa de la proforma PF-MID-001-2');
    await waitFor(() => expect(primero.getAttribute('src')).toMatch(/^blob:/));

    rerender(<Visor proforma={null} />);
    await waitFor(() =>
      expect(screen.queryByTitle(/Vista previa de la proforma/)).not.toBeInTheDocument(),
    );
    rerender(<Visor proforma={PROFORMA} />);

    const segundo = await screen.findByTitle('Vista previa de la proforma PF-MID-001-2');
    await waitFor(() => expect(segundo.getAttribute('src')).toMatch(/^blob:/));
    expect(revocados.has(sinFragmento(segundo.getAttribute('src')))).toBe(false);
  });

  it('libera el object URL al cerrar el visor', async () => {
    const { rerender } = render(<Visor proforma={PROFORMA} />, { wrapper: createQueryWrapper() });
    const iframe = await screen.findByTitle('Vista previa de la proforma PF-MID-001-2');
    await waitFor(() => expect(iframe.getAttribute('src')).toMatch(/^blob:/));

    rerender(<Visor proforma={null} />);
    await waitFor(() => expect(creados.every((u) => revocados.has(u))).toBe(true));
  });
});
