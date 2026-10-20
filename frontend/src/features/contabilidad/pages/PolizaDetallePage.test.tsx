import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { partida, poliza } from '../lib/__fixtures__/polizas-fix';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => {
    const attrs: Record<string, unknown> = { ...rest };
    delete attrs.search;
    return <a href={params ? to.replace('$id', params.id) : to} {...(attrs as object)}>{children}</a>;
  },
}));

import { PolizaDetallePage } from './PolizaDetallePage';

const API = '*/api/v1/contabilidad';
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json({ title: 'x', status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json' } });

function servidor(p: ReturnType<typeof poliza>) {
  mswServer.use(
    http.get(`${API}/polizas/:id/adjuntos`, () => HttpResponse.json([])),
    http.get('*/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
    http.get(`${API}/polizas/${p.id}`, () => HttpResponse.json(p, { headers: { ETag: `"${p.version}"` } })),
    http.get(`${API}/polizas`, () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 50 })),
    http.get(`${API}/periodos/ejercicios`, () => HttpResponse.json([{
      id: 'e1', anio: 2026, version: 1,
      periodos: [{ id: 'per11', ejercicioId: 'e1', anio: 2026, numero: 11, nombre: 'Noviembre', fechaInicio: '2026-11-01', fechaFin: '2026-11-30', estado: 'Abierto', esAjuste: false, abiertoPor: null, abiertoEn: null, cerradoPor: null, cerradoEn: null, reabiertoPor: null, reabiertoEn: null, version: 1 }],
    }])),
  );
}

function permisos(...extra: string[]) {
  useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPolizasLeer, ...extra] });
}

afterEach(() => useAuthStore.setState({ permisos: [], user: null }));

describe('<PolizaDetallePage>', () => {
  it('la anulación muestra importes negativos y enlaza a la póliza que anula', async () => {
    servidor(poliza({
      id: 'p2', folio: 'TT-000124', concepto: 'Anulación de TT-000123', polizaAnuladaId: 'p1', polizaAnuladaFolio: 'TT-000123',
      motivoAnulacion: 'FIX Captura duplicada del traspaso', totalCargos: -1234, totalAbonos: -1234,
      partidas: [partida({ cargo: -1234 }), partida({ id: 'pa2', linea: 2, cuentaCodigo: 'FIX-120', cargo: 0, abono: -1234 })],
    }));
    permisos(PermisosCanonicos.ContabilidadPolizasAnular);
    render(<PolizaDetallePage id="p2" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });

    const partidas = await screen.findByRole('region', { name: 'Partidas' });
    expect(within(partidas).getAllByText('-$1,234.00').length).toBeGreaterThanOrEqual(4); // 2 partidas + 2 totales
    expect(screen.getByRole('link', { name: 'TT-000123' })).toHaveAttribute('href', '/contabilidad/polizas/p1');
    expect(screen.getByText(/Anula a/)).toBeInTheDocument();
    expect(screen.getByText('Motivo: FIX Captura duplicada del traspaso')).toBeInTheDocument();
    expect(screen.getAllByText('Anulación').length).toBeGreaterThan(0);
    // Una anulación no se vuelve a anular.
    expect(screen.queryByRole('button', { name: 'Anular' })).not.toBeInTheDocument();
  });

  it('la original anulada enlaza a su anulación', async () => {
    servidor(poliza({ estado: 'Anulada', anuladaPorPolizaId: 'p2', anuladaPorFolio: 'TT-000124', motivoAnulacion: 'FIX Captura duplicada', anuladaPor: 'FIX Supervisor', anuladaEn: '2026-10-07T15:00:00Z' }));
    permisos(PermisosCanonicos.ContabilidadPolizasAnular);
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('link', { name: 'TT-000124' })).toHaveAttribute('href', '/contabilidad/polizas/p2');
    expect(screen.getByText(/Anulada por/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Anular' })).not.toBeInTheDocument();
  });

  it('anular exige motivo de 10 caracteres y pide periodo destino cuando el original ya no admite pólizas', async () => {
    servidor(poliza());
    const cuerpos: unknown[] = [];
    const onNavegar = vi.fn();
    mswServer.use(http.post(`${API}/polizas/p1/anular`, async ({ request }) => {
      const body = (await request.json()) as { periodoAnio: number | null };
      cuerpos.push({ body, ifMatch: request.headers.get('If-Match'), idem: !!request.headers.get('Idempotency-Key') });
      if (body.periodoAnio == null) return problem(422, { code: 'CONTAB_POLIZA_ANULACION_REQUIERE_PERIODO', detail: 'El periodo de la póliza ya está cerrado.' });
      return HttpResponse.json(poliza({ id: 'p2', folio: 'TT-000124', polizaAnuladaId: 'p1' }), { status: 201 });
    }));
    permisos(PermisosCanonicos.ContabilidadPolizasAnular);
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={onNavegar} />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'Anular' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Anular póliza' });
    fireEvent.change(within(dialogo).getByLabelText(/^Motivo(?:\s*\*)?$/), { target: { value: 'corto' } });
    expect(confirmar).toBeDisabled();
    expect(confirmar).toHaveAttribute('title', 'El motivo lleva al menos 10 caracteres');
    fireEvent.change(within(dialogo).getByLabelText(/^Motivo(?:\s*\*)?$/), { target: { value: 'FIX Captura duplicada' } });
    expect(confirmar).toBeEnabled();

    fireEvent.click(confirmar);
    expect(await within(dialogo).findByText('El periodo de la póliza ya está cerrado.')).toBeInTheDocument();
    const periodo = await within(dialogo).findByLabelText(/^Periodo de la anulación(?:\s*\*)?$/);
    expect(confirmar).toBeDisabled();
    await within(dialogo).findByRole('option', { name: '2026 · 11 Noviembre' });
    fireEvent.change(periodo, { target: { value: '2026-11' } });
    fireEvent.click(confirmar);

    await waitFor(() => expect(onNavegar).toHaveBeenCalledWith('p2'));
    expect(cuerpos[1]).toEqual({ body: { motivo: 'FIX Captura duplicada', periodoAnio: 2026, periodoNumero: 11 }, ifMatch: '"4"', idem: true });
  });

  it('quien capturó el borrador no puede autorizarlo; el botón explica por qué', async () => {
    servidor(poliza({ folio: null, numero: null, estado: 'Borrador', autorizadaPorId: null, autorizadaPor: null, autorizadaEn: null }));
    permisos(PermisosCanonicos.ContabilidadPolizasAutorizar, PermisosCanonicos.ContabilidadPolizasCapturar);
    useAuthStore.setState({ user: { id: 'u-analista', email: 'fix@example.com', nombre: 'FIX Analista' } });
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    const autorizar = await screen.findByRole('button', { name: 'Autorizar' });
    expect(autorizar).toBeDisabled();
    expect(autorizar).toHaveAttribute('title', 'No puedes autorizar una póliza que tú capturaste: la autoriza otro usuario.');
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Borrador');
  });

  it('autoriza el borrador de otro usuario con If-Match y muestra el folio asignado', async () => {
    servidor(poliza({ folio: null, numero: null, estado: 'Borrador', autorizadaPorId: null, autorizadaPor: null, autorizadaEn: null }));
    let ifMatch: string | null = null;
    mswServer.use(http.post(`${API}/polizas/p1/autorizar`, ({ request }) => {
      ifMatch = request.headers.get('If-Match');
      return HttpResponse.json(poliza());
    }));
    permisos(PermisosCanonicos.ContabilidadPolizasAutorizar);
    useAuthStore.setState({ user: { id: 'u-super', email: 'fix2@example.com', nombre: 'FIX Supervisor' } });
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    expect(await screen.findByText('Póliza autorizada con folio TT-000123.')).toBeInTheDocument();
    expect(ifMatch).toBe('"4"');
  });

  it('muestra el mensaje del servidor si la póliza no cuadra al autorizar', async () => {
    servidor(poliza({ folio: null, estado: 'Borrador', cuadra: true, autorizadaPorId: null, autorizadaPor: null, autorizadaEn: null }));
    mswServer.use(http.post(`${API}/polizas/p1/autorizar`, () => problem(422, { code: 'CONTAB_POLIZA_NO_CUADRA', detail: 'La póliza no cuadra: cargos 1,234.00 y abonos 1,000.00.' })));
    permisos(PermisosCanonicos.ContabilidadPolizasAutorizar);
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La póliza no cuadra: cargos 1,234.00 y abonos 1,000.00.');
  });
});
