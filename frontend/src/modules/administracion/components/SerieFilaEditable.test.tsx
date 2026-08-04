import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SerieFilaEditable } from '@/modules/administracion/components/SerieFilaEditable';

/**
 * Validación crítica del UF-Admin-PR6 §5.2: cambiar el RadioGroup de
 * <c>ReinicioPeriodo</c> y darle Guardar DEBE mostrar el AlertDialog
 * antes de disparar el PATCH (UX defensiva — folios huérfanos en
 * período anterior).
 */

const SERIE_OC = {
  id: 'srv-1',
  empresaId: 'e-1',
  sucursalId: null,
  tipoDocumento: 1,
  prefijo: 'OC',
  sufijo: null,
  reinicioPeriodo: 1, // Anual
  activa: true,
  version: 1,
};

describe('<SerieFilaEditable> — confirm dialog en cambio de ReinicioPeriodo', () => {
  it('muestra AlertDialog y NO patchea hasta confirmar', async () => {
    let patchInvocado = false;
    mswServer.use(
      http.get('*/api/v1/admin/series/srv-1', () =>
        HttpResponse.json({
          serie: SERIE_OC,
          proximoFolioPreview: 'OC-2026-000001',
        }),
      ),
      http.patch('*/api/v1/admin/series/srv-1', () => {
        patchInvocado = true;
        return HttpResponse.json(SERIE_OC);
      }),
    );

    render(
      <SerieFilaEditable serie={SERIE_OC} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );

    // Simula click en radio "Mensual" (label visible).
    const mensualLabel = await screen.findByText(/^mensual$/i);
    fireEvent.click(mensualLabel);

    // Submit del form.
    const guardar = screen.getByRole('button', { name: /guardar cambios/i });
    fireEvent.click(guardar);

    // El AlertDialog debe aparecer ANTES del PATCH.
    await waitFor(() =>
      expect(
        screen.getByRole('alertdialog', undefined),
      ).toBeInTheDocument(),
    );
    expect(
      screen.getByText(/cambiar el reinicio del periodo/i),
    ).toBeInTheDocument();
    expect(patchInvocado).toBe(false);

    // Confirmar dispara el PATCH.
    const confirmar = screen.getByRole('button', { name: /confirmar cambio/i });
    fireEvent.click(confirmar);

    await waitFor(() => expect(patchInvocado).toBe(true));
  });

  it('si NO cambia ReinicioPeriodo, patchea directo sin AlertDialog', async () => {
    let patchInvocado = false;
    mswServer.use(
      http.get('*/api/v1/admin/series/srv-1', () =>
        HttpResponse.json({
          serie: SERIE_OC,
          proximoFolioPreview: 'OC-2026-000001',
        }),
      ),
      http.patch('*/api/v1/admin/series/srv-1', () => {
        patchInvocado = true;
        return HttpResponse.json({ ...SERIE_OC, prefijo: 'OC2' });
      }),
    );

    const onSaved = vi.fn();
    render(
      <SerieFilaEditable
        serie={SERIE_OC}
        onCancel={() => {}}
        onSaved={onSaved}
      />,
      { wrapper: createQueryWrapper() },
    );

    // Cambia solo el prefijo (el input tiene placeholder "OC").
    const prefijoEl = screen.getByPlaceholderText('OC') as HTMLInputElement;
    fireEvent.change(prefijoEl, { target: { value: 'OC2' } });

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(patchInvocado).toBe(true));
    // No AlertDialog.
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});
