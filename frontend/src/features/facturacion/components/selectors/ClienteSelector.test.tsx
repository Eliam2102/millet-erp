import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
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
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])),
      http.get('*/api/v1/facturacion/catalogos/clientes/c-9', () => HttpResponse.json({ id: 'c-9', clave: 'CLI-9', razonSocial: 'Cliente guardado' })),
    );
    render(<ClienteSelector value="c-9" onChange={() => {}} initialLabel="CLI-9 · Cliente guardado" />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('CLI-9 · Cliente guardado')).toBeInTheDocument();
  });
  it('A21 conserva código y nombre seleccionados cuando los resultados vuelven a vaciarse', async () => {
    mswServer.use(http.get('*/api/v1/facturacion/catalogos/clientes', ({ request }) =>
      HttpResponse.json(new URL(request.url).searchParams.get('q') ? [cliente(1, 'Nombre igual'), cliente(2, 'Nombre igual')] : []),
    ));
    function Controlado() {
      const [id, setId] = useState<string | null>(null);
      return <ClienteSelector value={id} onChange={item => setId(item?.id ?? null)} />;
    }
    render(<Controlado />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cliente' }));
    fireEvent.change(screen.getByLabelText('Buscar cliente'), { target: { value: 'igual' } });
    fireEvent.click(await screen.findByRole('option', { name: /CLI-2/ }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Seleccionar cliente' })).toHaveTextContent('CLI-2 · Nombre igual'));
  });

  it('T004 resuelve por ID una etiqueta fría incompleta sin cambiar el receptor', async () => {
    const id = '11111111-1111-4111-8111-111111111111';
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])),
      http.get(`*/api/v1/facturacion/catalogos/clientes/${id}`, () => HttpResponse.json({ id, clave: 'CLI-FRIA', razonSocial: 'Nombre igual' })),
    );
    render(<ClienteSelector value={id} onChange={onChange} initialLabel="Nombre igual" />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Seleccionar cliente' })).toHaveTextContent('CLI-FRIA · Nombre igual'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('resuelve cambios de ID con nombres iguales y nunca ejecuta onChange por el lookup', async () => {
    const a = '11111111-1111-4111-8111-111111111111';
    const b = '22222222-2222-4222-8222-222222222222';
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])),
      http.get('*/api/v1/facturacion/catalogos/clientes/:id', ({ params }) => HttpResponse.json({ id: params.id, clave: params.id === a ? 'CLI-A' : 'CLI-B', razonSocial: 'Nombre igual' })),
    );
    const view = render(<ClienteSelector value={a} onChange={onChange} initialLabel="Nombre igual" />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('CLI-A · Nombre igual'));
    view.rerender(<ClienteSelector value={b} onChange={onChange} initialLabel="Nombre igual" />);
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('CLI-B · Nombre igual'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('un ID histórico ausente conserva su etiqueta sin mostrar el GUID ni cambiar el receptor', async () => {
    const id = '33333333-3333-4333-8333-333333333333';
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])),
      http.get(`*/api/v1/facturacion/catalogos/clientes/${id}`, () => HttpResponse.json({ title: 'Cliente sin registro', status: 404 }, { status: 404 })),
    );
    render(<ClienteSelector value={id} onChange={onChange} initialLabel="Cliente histórico" />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('combobox')).toHaveTextContent('Cliente histórico');
    await waitFor(() => expect(screen.getByRole('combobox')).not.toHaveTextContent(id));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('cliente inactivo ya elegido puede mostrar etiqueta pero no se añade como opción de búsqueda', async () => {
    const id = '44444444-4444-4444-8444-444444444444';
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes', () => HttpResponse.json([])),
      http.get(`*/api/v1/facturacion/catalogos/clientes/${id}`, () => HttpResponse.json({ id, clave: 'CLI-HIST', razonSocial: 'Cliente histórico' })),
    );
    render(<ClienteSelector value={id} onChange={onChange} initialLabel="Cliente histórico" />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('CLI-HIST · Cliente histórico'));
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.queryByRole('option', { name: /CLI-HIST/ })).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('una etiqueta histórica ausente no impide buscar y elegir otro cliente activo', async () => {
    const id = '55555555-5555-4555-8555-555555555555';
    const consulta = vi.fn();
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/facturacion/catalogos/clientes/:id', () => {
        consulta();
        return HttpResponse.json({ title: 'Cliente sin registro', status: 404 }, { status: 404 });
      }),
      http.get('*/api/v1/facturacion/catalogos/clientes', ({ request }) => HttpResponse.json(new URL(request.url).searchParams.get('q') ? [cliente(8, 'Cliente activo')] : [])),
    );
    render(<ClienteSelector value={id} onChange={onChange} initialLabel="Cliente histórico" />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(consulta).toHaveBeenCalled());
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByLabelText('Buscar cliente'), { target: { value: 'activo' } });
    fireEvent.click(await screen.findByRole('option', { name: /CLI-8/ }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 'c-8', clave: 'CLI-8' }));
  });

});
