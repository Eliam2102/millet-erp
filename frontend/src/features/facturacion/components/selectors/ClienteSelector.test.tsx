import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteSelector } from './ClienteSelector';

const cliente = (i: number, razonSocial: string) => ({
  id: `c-${i}`,
  clave: `CLI-${i}`,
  razonSocial,
  rfc: `PEGC80010${i}AB1`,
  regimenFiscal: '612',
  codigoPostalFiscal: '97000',
  usoCfdiDefault: 'G03',
  formaPagoDefault: '01',
  metodoPagoDefault: 'PUE',
  monedaDefault: 'MXN',
  esGenerico: false,
  numRegIdTrib: null,
  paisResidencia: null,
  domicilioExtranjeroCalle: null,
  domicilioExtranjeroEstado: null,
  domicilioExtranjeroCodigoPostal: null,
  datosFiscalesCompletos: true,
});

describe('<ClienteSelector> · búsqueda por palabras (86e3tfdx8)', () => {
  it('busca en una sola caja con q y entrega el cliente completo', async () => {
    const consultas: string[] = [];
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', ({ request }) => {
        const url = new URL(request.url);
        consultas.push(url.search);
        return HttpResponse.json(url.searchParams.get('q') ? [cliente(1, 'PÉREZ GARCÍA CARLOS')] : []);
      }),
    );
    const onChange = vi.fn();
    render(<ClienteSelector value={null} onChange={onChange} />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cliente' }));
    expect(screen.queryByLabelText('Buscar cliente por RFC')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Buscar cliente'), { target: { value: 'carlos perez' } });
    fireEvent.click(await screen.findByText('PÉREZ GARCÍA CARLOS'));

    expect(consultas.some((q) => q.includes('q=carlos+perez') && q.includes('limit=50'))).toBe(true);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ id: 'c-1', regimenFiscal: '612' }));
  });

  it('con 50 resultados pide afinar la búsqueda', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () =>
        HttpResponse.json(Array.from({ length: 50 }, (_, i) => cliente(i, `Cliente Norte ${i}`))),
      ),
    );
    render(<ClienteSelector value={null} onChange={() => {}} />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cliente' }));
    fireEvent.change(screen.getByLabelText('Buscar cliente'), { target: { value: 'norte' } });

    await waitFor(() => expect(screen.getByText('Afina la búsqueda para ver más.')).toBeInTheDocument());
  });

  it('con initialLabel muestra la etiqueta y no el id', () => {
    mswServer.use(http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])));
    render(<ClienteSelector value="c-9" onChange={() => {}} initialLabel="CLI-9 · Cliente guardado" />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('CLI-9 · Cliente guardado')).toBeInTheDocument();
  });
});
