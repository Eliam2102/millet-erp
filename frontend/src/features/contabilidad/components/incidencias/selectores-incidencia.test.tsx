import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { etiquetaPoliza } from '../../lib/etiquetas-incidencia';
import { PolizaAutorizadaSelector } from './PolizaAutorizadaSelector';
import { ReaperturaSelector } from './ReaperturaSelector';

const POLIZAS = '*/api/v1/contabilidad/polizas';
const REAPERTURAS = '*/api/v1/contabilidad/periodos/reaperturas';

function poliza(o: Record<string, unknown> = {}) {
  return {
    id: 'p1', folio: 'AS-000123', tipoOperacionId: 't1', tipoClave: 'AS', numero: 123, fecha: '2026-10-15',
    periodoAnio: 2026, periodoNumero: 10, concepto: 'FIX ajuste de prueba', origen: 'Manual', estado: 'Autorizada',
    esAnulacion: false, ...o,
  };
}

function reapertura(o: Record<string, unknown> = {}) {
  return {
    id: 'r1', anio: 2026, numero: 10, periodo: '2026-10', evidenciaReferencia: 'FIX oficio 001', creadaEn: '2026-11-10T15:00:00Z',
    creadaPor: 'FIX contador', venceEn: '2026-11-20T15:00:00Z', vigente: true, cerradaEn: null, ...o,
  };
}

describe('H-U29 · selectores de la incidencia', () => {
  it('etiquetaPoliza muestra folio, periodo y concepto (nunca el identificador)', () => {
    expect(etiquetaPoliza(poliza())).toBe('AS-000123 · 2026-10 · FIX ajuste de prueba');
  });

  it('PolizaAutorizadaSelector busca autorizadas por folio en el servidor y devuelve el id elegido', async () => {
    const urls: URL[] = [];
    mswServer.use(
      http.get(POLIZAS, ({ request }) => {
        const url = new URL(request.url);
        urls.push(url);
        const q = url.searchParams.get('q');
        const items = q === '123' ? [poliza()] : [poliza(), poliza({ id: 'p2', folio: 'AS-000999', concepto: 'FIX otra' })];
        return HttpResponse.json({ items, total: items.length, offset: 0, limit: 20 });
      }),
    );
    const elegidas: (string | null)[] = [];
    render(<PolizaAutorizadaSelector value={null} onChange={(id) => elegidas.push(id)} excluirId="p2" />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar póliza' }));
    fireEvent.change(await screen.findByPlaceholderText(/Folio/), { target: { value: '123' } });
    await waitFor(() => expect(urls.some((u) => u.searchParams.get('q') === '123')).toBe(true));
    expect(urls.every((u) => u.searchParams.get('estado') === 'Autorizada')).toBe(true);
    expect(screen.queryByText('AS-000999')).toBeNull(); // excluida

    fireEvent.click(await screen.findByText('AS-000123'));
    expect(elegidas).toEqual(['p1']);
  });

  it('ReaperturaSelector lista las reaperturas del periodo y devuelve el id elegido', async () => {
    let visto: URL | null = null;
    mswServer.use(
      http.get(REAPERTURAS, ({ request }) => {
        visto = new URL(request.url);
        return HttpResponse.json([reapertura(), reapertura({ id: 'r2', vigente: false, evidenciaReferencia: 'FIX oficio 000' })]);
      }),
    );
    const elegidas: (string | null)[] = [];
    render(<ReaperturaSelector id="sel" anio={2026} numero={10} value={null} onChange={(id) => elegidas.push(id)} />, {
      wrapper: createQueryWrapper(),
    });

    const select = await screen.findByRole('combobox');
    await waitFor(() => expect(select).not.toBeDisabled());
    expect(visto!.searchParams.get('anio')).toBe('2026');
    expect(visto!.searchParams.get('numero')).toBe('10');
    expect(screen.getByRole('option', { name: /FIX oficio 000/ }).textContent).toContain('concluida');

    fireEvent.change(select, { target: { value: 'r1' } });
    expect(elegidas).toEqual(['r1']);
  });

  it('ReaperturaSelector avisa cuando el periodo no tiene reaperturas', async () => {
    mswServer.use(http.get(REAPERTURAS, () => HttpResponse.json([])));
    render(<ReaperturaSelector anio={2026} numero={10} value={null} onChange={() => {}} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('option', { name: 'El periodo no tiene reaperturas' })).toBeTruthy();
    expect(screen.getByRole('combobox')).toBeDisabled();
  });
});
