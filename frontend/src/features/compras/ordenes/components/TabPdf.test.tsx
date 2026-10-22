import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { TabPdf } from './TabPdf';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { apiFetch } from '@/lib/auth/api-client';

vi.mock('@/lib/auth/api-client', () => ({ apiFetch: vi.fn() }));

const oc = {
  id: 'oc-ficticia', folio: 'OC-MID2026-000001', estado: EstadoOrdenCompra.Autorizada,
  cicloAutorizacion: 1, impresiones: 2, ultimaImpresionEn: '2026-10-20T12:00:00Z',
  ultimaImpresionPorNombre: 'Usuario de prueba',
} as OrdenCompraDetalleResponse;

describe('TabPdf', () => {
  it('usa vista previa al abrir y una descarga nueva por clic', async () => {
    vi.mocked(apiFetch).mockImplementation(async () => ({ ok: true, blob: async () => new Blob(['PDF ficticio']) }) as Response);
    vi.stubGlobal('URL', class extends URL {
      static createObjectURL = vi.fn(() => 'blob:pdf-ficticio');
      static revokeObjectURL = vi.fn();
    });
    const create = vi.spyOn(URL, 'createObjectURL');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    const view = render(<QueryClientProvider client={client}><TabPdf oc={oc} /></QueryClientProvider>);
    await waitFor(() => {
      const error = client.getQueryCache().getAll()[0]?.state.error;
      if (error) throw error;
      expect(screen.getByRole('button', { name: 'Descargar PDF' })).toBeInTheDocument();
    });
    expect(apiFetch).toHaveBeenCalledWith('/api/v1/compras/ordenes/oc-ficticia/pdf?vistaPrevia=true', expect.any(Object));
    expect(screen.getByText(/Impreso 2 veces/)).toHaveTextContent('Usuario de prueba');
    fireEvent.click(screen.getByRole('button', { name: 'Descargar PDF' }));
    await waitFor(() => expect(click).toHaveBeenCalledOnce());
    expect(apiFetch).toHaveBeenCalledWith('/api/v1/compras/ordenes/oc-ficticia/pdf');
    expect(invalidate).toHaveBeenCalled();
    view.unmount();
    client.clear();
    create.mockRestore(); revoke.mockRestore(); click.mockRestore();
  });
});
