import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { UmbralesAprobacionSection } from '@/features/compras/components/UmbralesAprobacionSection';
import { UMBRAL_GENERAL_ID } from '@/features/compras/schemas/umbral-aprobacion';

describe('<UmbralesAprobacionSection> — umbral general', () => {
  it('muestra la fila "General (empresa)" aunque no haya departamentos y guarda con Guid.Empty', async () => {
    const puts: {
      departamentoId: string;
      umbralMonto: number;
      modoNivel2?: string;
      montoMinimoDisponibilidad?: number | null;
    }[] = [];
    mswServer.use(
      http.get('*/api/v1/catalogos/departamentos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
      http.get('*/api/v1/compras/umbrales-aprobacion', () => HttpResponse.json([])),
      http.put('*/api/v1/compras/umbrales-aprobacion', async ({ request }) => {
        const body = (await request.json()) as {
          departamentoId: string;
          umbralMonto: number;
          modoNivel2?: string;
          montoMinimoDisponibilidad?: number | null;
        };
        puts.push(body);
        return HttpResponse.json({
          departamentoId: body.departamentoId,
          umbralMenorCuantia: null,
          umbralMonto: body.umbralMonto,
          moneda: 'MXN',
          vigenteDesde: '2026-10-15',
          modoNivel2: 'Ambos',
          montoMinimoDisponibilidad: null,
        });
      }),
    );

    render(<UmbralesAprobacionSection puedeEditar />, { wrapper: createQueryWrapper() });

    const input = await screen.findByRole('textbox', { name: 'Umbral N2 de General (empresa)' });
    expect(screen.getByText(/N1 = menor cuantía \(aprobación rápida\)/)).toBeInTheDocument();
    fireEvent.change(input, { target: { value: '50000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar umbral de General (empresa)' }));

    await waitFor(() => expect(puts).toHaveLength(1));
    expect(puts[0]).toMatchObject({
      departamentoId: UMBRAL_GENERAL_ID,
      umbralMonto: 50000,
      modoNivel2: 'Ambos',
      montoMinimoDisponibilidad: null,
    });
  });

  it('captura el mínimo por disponibilidad de la fila general y lo envía en el PUT', async () => {
    const puts: { montoMinimoDisponibilidad?: number | null }[] = [];
    mswServer.use(
      http.get('*/api/v1/catalogos/departamentos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
      http.get('*/api/v1/compras/umbrales-aprobacion', () => HttpResponse.json([])),
      http.put('*/api/v1/compras/umbrales-aprobacion', async ({ request }) => {
        const body = (await request.json()) as { montoMinimoDisponibilidad?: number | null };
        puts.push(body);
        return HttpResponse.json({
          departamentoId: UMBRAL_GENERAL_ID,
          umbralMenorCuantia: null,
          umbralMonto: 50000,
          moneda: 'MXN',
          vigenteDesde: '2026-10-15',
          modoNivel2: 'Ambos',
          montoMinimoDisponibilidad: 1500,
        });
      }),
    );

    render(<UmbralesAprobacionSection puedeEditar />, { wrapper: createQueryWrapper() });

    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Umbral N2 de General (empresa)' }),
      {
        target: { value: '50000' },
      },
    );
    fireEvent.change(
      screen.getByRole('textbox', { name: 'Mínimo por disponibilidad de General (empresa)' }),
      { target: { value: '1500' } },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Guardar umbral de General (empresa)' }));

    await waitFor(() => expect(puts).toHaveLength(1));
    expect(puts[0]).toMatchObject({ montoMinimoDisponibilidad: 1500 });
  });
});
