import { describe, expect, it, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { ConflictDialogProvider } from '@/components/erp/collaboration/ConflictDialogProvider';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';

/**
 * <c>useConflictDialog()</c> abre / cierra el dialog singleton del shell.
 * Tests cubren: lanza fuera del provider, modo simple, modo preserve,
 * close programático, callback <c>onRefrescar</c> al click.
 */

function TriggerSimple({ onRefrescar }: { onRefrescar: () => void }) {
  const { openSimple } = useConflictDialog();
  return (
    <button
      type="button"
      onClick={() =>
        openSimple({ traceId: 'trace-abc', onRefrescar })
      }
    >
      trigger-simple
    </button>
  );
}

function TriggerPreserve() {
  const { openPreserve } = useConflictDialog();
  return (
    <button
      type="button"
      onClick={() =>
        openPreserve({
          form: { reset: () => {} },
          localValues: { campo: 'local' },
          baselineValues: { campo: 'baseline' },
          latestRemote: { campo: 'remote' },
          fieldLabels: { campo: 'Campo' },
          traceId: 'trace-pres',
          onRefrescar: () => {},
        })
      }
    >
      trigger-preserve
    </button>
  );
}

function TriggerClose() {
  const { close } = useConflictDialog();
  return (
    <button type="button" onClick={close}>
      trigger-close
    </button>
  );
}

describe('<ConflictDialogProvider> + useConflictDialog()', () => {
  it('lanza si se usa fuera del provider', () => {
    function FueraDelProvider() {
      useConflictDialog();
      return <div />;
    }
    // Silencia el error en consola que React imprime al re-throw.
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<FueraDelProvider />)).toThrow(
      /useConflictDialog\(\) debe usarse dentro de <ConflictDialogProvider>/,
    );
    errorSpy.mockRestore();
  });

  it('modo simple: openSimple → dialog visible con traceId; click "Refrescar" invoca onRefrescar', () => {
    const onRefrescar = vi.fn();
    render(
      <ConflictDialogProvider>
        <TriggerSimple onRefrescar={onRefrescar} />
      </ConflictDialogProvider>,
    );

    // Antes del trigger: dialog cerrado.
    expect(
      screen.queryByText(/actualizada por otro usuario/i),
    ).not.toBeInTheDocument();

    // Disparamos el trigger.
    act(() => {
      screen.getByText('trigger-simple').click();
    });

    // Modo simple: título "esta requisición fue actualizada".
    expect(
      screen.getByText(/Esta requisición fue actualizada/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/trace-abc/i)).toBeInTheDocument();

    // Botón único: "Refrescar y revisar".
    const btn = screen.getByRole('button', { name: /refrescar y revisar/i });
    act(() => btn.click());

    expect(onRefrescar).toHaveBeenCalledOnce();
    // Después del click el dialog se cierra.
    expect(
      screen.queryByText(/Esta requisición fue actualizada/i),
    ).not.toBeInTheDocument();
  });

  it('modo preserve: openPreserve → dialog con sección de cambios y CTA "Reaplicar mis cambios"', () => {
    render(
      <ConflictDialogProvider>
        <TriggerPreserve />
      </ConflictDialogProvider>,
    );

    act(() => {
      screen.getByText('trigger-preserve').click();
    });

    // Título de modo preserve.
    expect(
      screen.getByText(/Otro usuario modificó esta requisición/i),
    ).toBeInTheDocument();
    // CTA primaria.
    expect(
      screen.getByRole('button', { name: /reaplicar mis cambios/i }),
    ).toBeInTheDocument();
    // CTA secundaria.
    expect(
      screen.getByRole('button', { name: /solo refrescar/i }),
    ).toBeInTheDocument();
  });

  it('close() cierra programáticamente sin invocar onRefrescar', () => {
    const onRefrescar = vi.fn();
    render(
      <ConflictDialogProvider>
        <TriggerSimple onRefrescar={onRefrescar} />
        <TriggerClose />
      </ConflictDialogProvider>,
    );

    act(() => screen.getByText('trigger-simple').click());
    expect(
      screen.getByText(/Esta requisición fue actualizada/i),
    ).toBeInTheDocument();

    act(() => screen.getByText('trigger-close').click());
    expect(
      screen.queryByText(/Esta requisición fue actualizada/i),
    ).not.toBeInTheDocument();
    expect(onRefrescar).not.toHaveBeenCalled();
  });

  it('cancelar (botón Cancelar) cierra sin invocar onRefrescar', () => {
    const onRefrescar = vi.fn();
    render(
      <ConflictDialogProvider>
        <TriggerSimple onRefrescar={onRefrescar} />
      </ConflictDialogProvider>,
    );

    act(() => screen.getByText('trigger-simple').click());
    act(() => {
      screen.getByRole('button', { name: /cancelar/i }).click();
    });

    expect(
      screen.queryByText(/Esta requisición fue actualizada/i),
    ).not.toBeInTheDocument();
    expect(onRefrescar).not.toHaveBeenCalled();
  });
});
