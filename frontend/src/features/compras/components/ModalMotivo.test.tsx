import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ModalMotivo } from '@/features/compras/components/ModalMotivo';

/**
 * El modal envuelve un Radix Dialog que renderiza en Portal con
 * animación. En jsdom las animaciones se saltan, pero el contenido
 * del Portal se busca con <c>screen</c> globalmente (no con
 * <c>container</c> del render). Tests pragmáticos: render por variante,
 * disabled del confirm sin motivo. Interacción con Select de Radix
 * cubierta por inspección + smoke test del flujo en UF7-PR1.
 */

function setupMotivos() {
  mswServer.use(
    http.get('*/api/v1/compras/motivos-rechazo', () => HttpResponse.json([])),
  );
}

describe('<ModalMotivo>', () => {
  it('open=false: no renderiza nada en el DOM', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={false}
        onOpenChange={() => {}}
        variante="rechazar"
        folio="MID2026-000042"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByText(/Rechazar requisición/i)).not.toBeInTheDocument();
  });

  it('variante "rechazar": muestra título rojo y CTA "Confirmar rechazo"', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={() => {}}
        variante="rechazar"
        folio="MID2026-000042"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText(/Rechazar requisición/i)).toBeInTheDocument();
    expect(screen.getByText('MID2026-000042')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /confirmar rechazo/i }),
    ).toBeInTheDocument();
  });

  it('variante "eliminar": muestra título y CTA "Confirmar eliminación"', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={() => {}}
        variante="eliminar"
        folio="MID2026-000099"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText(/Eliminar requisición/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /confirmar eliminación/i }),
    ).toBeInTheDocument();
  });

  it('variante "cancelar": muestra título naranja y CTA "Confirmar cancelación"', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={() => {}}
        variante="cancelar"
        folio="MID2026-000010"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText(/Cancelar requisición/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /confirmar cancelación/i }),
    ).toBeInTheDocument();
    // Texto extra solo en cancelar.
    expect(
      screen.getByText(/no podrá reactivarse/i),
    ).toBeInTheDocument();
  });

  it('botón confirmar arranca disabled (no hay motivo seleccionado)', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={() => {}}
        variante="rechazar"
        folio="MID2026-000042"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    const cta = screen.getByRole('button', { name: /confirmar rechazo/i });
    expect(cta).toBeDisabled();
  });

  it('cancelar dispara onOpenChange(false)', async () => {
    setupMotivos();
    const onOpenChange = vi.fn();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={onOpenChange}
        variante="rechazar"
        folio="MID2026-000042"
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    const btnCancelar = screen.getByRole('button', { name: /^cancelar$/i });
    btnCancelar.click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('isPending=true: deshabilita CTA y cancel, muestra "Procesando…"', () => {
    setupMotivos();
    render(
      <ModalMotivo
        open={true}
        onOpenChange={() => {}}
        variante="rechazar"
        folio="MID2026-000042"
        onConfirm={() => {}}
        isPending
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText(/procesando/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^cancelar$/i })).toBeDisabled();
  });

});
