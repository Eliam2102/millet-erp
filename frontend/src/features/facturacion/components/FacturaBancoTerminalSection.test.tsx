import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { FacturaBancoTerminalSection } from './FacturaBancoTerminalSection';

const BASE = '*/api/v1/facturacion/terminal-tarjetas/factura-banco';

const candidato = (id: string, total: number) => ({
  id, uuid: `${id.toUpperCase()}-0000-0000-0000-000000000000`, rfcEmisor: 'BAN010101AAA', serie: 'A', folio: id,
  fechaCfdi: '2026-11-01T00:00:00Z', subtotal: total / 1.16, iva: total - total / 1.16, total, moneda: 'MXN',
  tipo: 'Ingreso', estado: 'PorProcesar',
});

function cuadre(cfdi: string | null) {
  const base = { anio: 2026, mes: 10, cargos: 30, comisionCargos: 6000, ivaCargos: 960, totalCargos: 6960 };
  if (cfdi === 'f1')
    return { ...base, cfdiRecibidoId: 'f1', uuidCfdi: 'F1', rfcEmisor: 'BAN010101AAA', subtotalFactura: 6000,
      ivaFactura: 960, totalFactura: 6960, diferenciaComision: 0, diferenciaIva: 0, diferenciaTotal: 0, cuadra: true };
  if (cfdi === 'f2')
    return { ...base, cfdiRecibidoId: 'f2', uuidCfdi: 'F2', rfcEmisor: 'BAN010101AAA', subtotalFactura: 6034.48,
      ivaFactura: 965.52, totalFactura: 7000, diferenciaComision: 34.48, diferenciaIva: 5.52, diferenciaTotal: 40, cuadra: false };
  if (cfdi === 'f3')
    return { ...base, cfdiRecibidoId: 'f3', uuidCfdi: 'F3', rfcEmisor: 'BAN010101AAA', subtotalFactura: 6000.01,
      ivaFactura: 960, totalFactura: 6960.01, diferenciaComision: 0.01, diferenciaIva: 0, diferenciaTotal: 0.01, cuadra: true };
  return { ...base, cfdiRecibidoId: null, uuidCfdi: null, rfcEmisor: null, subtotalFactura: null, ivaFactura: null,
    totalFactura: null, diferenciaComision: null, diferenciaIva: null, diferenciaTotal: null, cuadra: false };
}

describe('86e3xt2q8 · factura mensual del banco', () => {
  function servir(onLigar?: (body: unknown) => void) {
    mswServer.use(
      http.get(`${BASE}/candidatos`, () => HttpResponse.json([candidato('f1', 6960), candidato('f2', 7000), candidato('f3', 6960.01)])),
      http.get(`${BASE}/cuadre`, ({ request }) =>
        HttpResponse.json(cuadre(new URL(request.url).searchParams.get('cfdiRecibidoId')))),
      http.get(BASE, () => HttpResponse.json([])),
      http.post(BASE, async ({ request }) => {
        onLigar?.(await request.json());
        return HttpResponse.json({ id: 'fb1', cfdiRecibidoId: 'f1', uuidCfdi: 'F1', rfcEmisor: 'BAN010101AAA', anio: 2026,
          mes: 10, comision: 6000, ivaComision: 960, total: 6960, cargos: 30, ligadaEn: '2026-11-03T18:00:00Z' });
      }),
    );
  }

  it('con una factura que no cuadra muestra la diferencia de $40 y no deja ligar', async () => {
    servir();
    render(<FacturaBancoTerminalSection />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Mes de los cargos'), { target: { value: '2026-10' } });
    const select = await screen.findByLabelText('Factura del banco (CFDI recibido)');
    await screen.findByRole('option', { name: /7,000\.00/ });
    fireEvent.change(select, { target: { value: 'f2' } });

    expect(await screen.findByText('No cuadra')).toBeInTheDocument();
    expect(screen.getByText(/Diferencia de \$40\.00: no se liga nada/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Ligar factura/ })).toBeDisabled();
    expect(screen.getByText('La factura no cuadra con los cargos del mes.')).toBeInTheDocument();
  });

  it('86e3zcbjx · con un centavo de diferencia cuadra y avisa que va a redondeo', async () => {
    servir();
    render(<FacturaBancoTerminalSection />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Mes de los cargos'), { target: { value: '2026-10' } });
    await screen.findByRole('option', { name: /6,960\.01/ });
    fireEvent.change(screen.getByLabelText('Factura del banco (CFDI recibido)'), { target: { value: 'f3' } });

    expect(await screen.findByText('Cuadra')).toBeInTheDocument();
    expect(screen.getByText(/la diferencia de \$0\.01 va a redondeo/)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: /Ligar factura/ })).toBeEnabled());
  });

  it('con una factura que cuadra la liga con el mes y el CFDI elegidos', async () => {
    let enviado: unknown = null;
    servir((b) => { enviado = b; });
    render(<FacturaBancoTerminalSection />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Mes de los cargos'), { target: { value: '2026-10' } });
    await screen.findByRole('option', { name: /6,960\.00/ });
    fireEvent.change(screen.getByLabelText('Factura del banco (CFDI recibido)'), { target: { value: 'f1' } });

    expect(await screen.findByText('Cuadra')).toBeInTheDocument();
    const ligar = screen.getByRole('button', { name: /Ligar factura/ });
    await waitFor(() => expect(ligar).toBeEnabled());
    fireEvent.click(ligar);
    await waitFor(() => expect(enviado).toEqual({ anio: 2026, mes: 10, cfdiRecibidoId: 'f1' }));
  });
});

it('muestra la causa y la acción cuando no puede consultar las facturas', async () => {
  mswServer.use(
    http.get(`${BASE}/candidatos`, () => HttpResponse.json({ title: 'CFDI_ERROR', detail: 'Invalid payload', status: 500 }, { status: 500 })),
    http.get(`${BASE}/cuadre`, () => HttpResponse.json(cuadre(null))),
    http.get(BASE, () => HttpResponse.json([])),
  );
  render(<FacturaBancoTerminalSection />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('No se pudieron consultar las facturas del banco')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  expect(screen.queryByText(/CFDI_ERROR|Invalid payload/)).not.toBeInTheDocument();
});
