import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import type { CorridaResponse } from '@/features/tesoreria/api/types';
import { DetalleCorrida } from './DetalleCorrida';

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/lib/auth/auth-store', async (original) => {
  const real = await original<typeof import('@/lib/auth/auth-store')>();
  const useAuthStore = Object.assign(
    (sel: (s: { user: { id: string } }) => unknown) => sel({ user: { id: 'armador' } }),
    { getState: real.useAuthStore.getState, subscribe: real.useAuthStore.subscribe, setState: real.useAuthStore.setState },
  );
  return { ...real, useAuthStore };
});

const corrida = {
  id: 'cor-1', nombre: 'Corrida MXN', estado: 'Borrador', moneda: 'MXN', cuentaBancariaId: 'cta-1',
  bruto: 500_000, retenciones: 0, total: 500_000, solicitadaPor: 'armador', autorizadaPor: null, autorizadaEn: null,
  creadaEn: '2026-11-13T12:00:00Z', firmasRequeridas: 1, firmasRecibidas: 0, rolRequerido: null, motivoCierre: null,
  avisos: null, version: 4, proveedores: [], lineas: [{ facturaProveedorId: 'f-1' }], firmas: [], ejecutado: 0,
  pendiente: 500_000, oficioGeneradoEn: null,
} as unknown as CorridaResponse;

function montar(onEnviar: (body: unknown) => void) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/corridas/cor-1', () => HttpResponse.json(corrida)),
    http.post('*/api/v1/tesoreria/corridas/cor-1/enviar-autorizacion', async ({ request }) => {
      onEnviar(await request.json());
      return HttpResponse.json({ ...corrida, estado: 'EnAutorizacion' });
    }),
  );
  render(<DetalleCorrida id="cor-1" />, { wrapper: createQueryWrapper() });
}

describe('<DetalleCorrida> · M-147 saldo del portal', () => {
  it('con saldo sin fecha ni evidencia avisa y no envía', async () => {
    const onEnviar = vi.fn();
    montar(onEnviar);
    fireEvent.change(await screen.findByLabelText(/Saldo en el portal/), { target: { value: '650000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar a autorización' }));
    expect(await screen.findByText(/Indica la fecha de consulta del portal/)).toHaveAttribute('id', 'saldo-portal-error');
    expect(onEnviar).not.toHaveBeenCalled();
  });

  it('envía saldo, fecha y evidencia del portal en el cuerpo', async () => {
    let body: unknown = null;
    montar((b) => (body = b));
    fireEvent.change(await screen.findByLabelText(/Saldo en el portal/), { target: { value: '650000' } });
    fireEvent.change(screen.getByLabelText('Fecha de consulta'), { target: { value: '2026-11-13' } });
    fireEvent.change(screen.getByLabelText('Referencia de la evidencia'), { target: { value: 'Captura BBVA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar a autorización' }));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toEqual({ saldoPortal: 650000, fechaConsultaPortal: '2026-11-13', evidenciaSaldoPortal: 'Captura BBVA' });
  });
});

describe('<DetalleCorrida> · M-147 saldo del portal obligatorio al firmar', () => {
  const enAutorizacion = { ...corrida, estado: 'EnAutorizacion', solicitadaPor: 'otro', version: 5 } as unknown as CorridaResponse;

  function montarFirma(onFirmar: (body: unknown) => void) {
    mswServer.use(
      http.get('*/api/v1/tesoreria/corridas/cor-1', () => HttpResponse.json(enAutorizacion)),
      http.post('*/api/v1/tesoreria/corridas/cor-1/autorizar', async ({ request }) => {
        onFirmar(await request.json());
        return HttpResponse.json({ ...enAutorizacion, estado: 'Autorizada' });
      }),
    );
    render(<DetalleCorrida id="cor-1" />, { wrapper: createQueryWrapper() });
  }

  it('sin saldo, fecha ni evidencia no abre la confirmación ni firma', async () => {
    const onFirmar = vi.fn();
    montarFirma(onFirmar);
    expect(await screen.findByLabelText(/Saldo en el portal/)).toBeRequired();
    fireEvent.click(screen.getByRole('button', { name: 'Firmar y autorizar' }));
    expect(await screen.findByText(/Para firmar captura el saldo del portal/)).toHaveAttribute('id', 'saldo-portal-error');
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(onFirmar).not.toHaveBeenCalled();
  });

  it('con los tres datos confirma y manda el saldo del portal en el cuerpo de la firma', async () => {
    let body: unknown = null;
    montarFirma((b) => (body = b));
    fireEvent.change(await screen.findByLabelText(/Saldo en el portal/), { target: { value: '650000' } });
    fireEvent.change(screen.getByLabelText('Fecha de consulta'), { target: { value: '2026-11-13' } });
    fireEvent.change(screen.getByLabelText('Referencia de la evidencia'), { target: { value: 'Captura BBVA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Firmar y autorizar' }));
    const confirmar = await screen.findAllByRole('button', { name: 'Firmar y autorizar' });
    fireEvent.click(confirmar[confirmar.length - 1]);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toEqual({ saldoPortal: 650000, fechaConsultaPortal: '2026-11-13', evidenciaSaldoPortal: 'Captura BBVA' });
  });
});
