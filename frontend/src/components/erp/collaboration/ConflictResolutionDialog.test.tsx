import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import {
  ConflictResolutionDialog,
  type ConflictDialogForm,
} from '@/components/erp/collaboration/ConflictResolutionDialog';

function fakeForm(): ConflictDialogForm & { reset: ReturnType<typeof vi.fn> } {
  return { reset: vi.fn() };
}

describe('<ConflictResolutionDialog> — modo simple', () => {
  it('muestra mensaje genérico y dos botones (Cancelar + Refrescar)', () => {
    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
      />,
    );

    expect(
      screen.getByText(/fue actualizada por otro usuario/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /refrescar y revisar/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /cancelar/i }),
    ).toBeInTheDocument();
    // Modo simple NO muestra los botones de modo preserve.
    expect(
      screen.queryByRole('button', { name: /reaplicar/i }),
    ).not.toBeInTheDocument();
  });

  it('"Refrescar y revisar" invoca onRefrescar y cierra el dialog', () => {
    const onRefrescar = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={onOpenChange}
        onRefrescar={onRefrescar}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: /refrescar y revisar/i }),
    );
    expect(onRefrescar).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('muestra traceId cuando se pasa', () => {
    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        traceId="00-abc-def-01"
      />,
    );
    expect(screen.getByText(/00-abc-def-01/)).toBeInTheDocument();
  });
});

describe('<ConflictResolutionDialog> — modo preserve, sin solapes', () => {
  it('detecta cambios locales que no solapan con remotos y NO muestra diff filtrado', () => {
    // Capturador editó cantidad; colega editó descripcion (no solapan).
    const baseline = { cantidad: 5, descripcion: 'Original', precio: 100 };
    const local = { cantidad: 7, descripcion: 'Original', precio: 100 };
    const remote = {
      cantidad: 5,
      descripcion: 'Cambio del colega',
      precio: 100,
    };

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={fakeForm()}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
        fieldLabels={{
          cantidad: 'Cantidad',
          descripcion: 'Descripción',
        }}
      />,
    );

    expect(
      screen.getByText(/no se solapan con los del otro usuario/i),
    ).toBeInTheDocument();
    // El expandible "Ver todos los cambios remotos" debe estar
    // disponible porque hay 1 cambio remoto (descripcion).
    expect(
      screen.getByRole('button', { name: /1 campo/i }),
    ).toBeInTheDocument();

    // Sección "Tus cambios pendientes" muestra cantidad como cambio local.
    expect(screen.getByText('Cantidad')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument(); // baseline
    expect(screen.getByText('7')).toBeInTheDocument(); // local
  });

  it('expande "Ver todos los cambios remotos" al click', () => {
    const baseline = { cantidad: 5, descripcion: 'A' };
    const local = { cantidad: 7, descripcion: 'A' };
    const remote = { cantidad: 5, descripcion: 'B' };

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={fakeForm()}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
      />,
    );

    const verTodos = screen.getByRole('button', { name: /1 campo/i });
    expect(verTodos).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(verTodos);
    expect(verTodos).toHaveAttribute('aria-expanded', 'true');
    // Ahora aparece la fila del cambio remoto.
    expect(screen.getByText('B')).toBeInTheDocument();
  });
});

describe('<ConflictResolutionDialog> — modo preserve, con solapes', () => {
  it('muestra diff filtrado de campos que ambos tocaron', () => {
    const baseline = { cantidad: 5, descripcion: 'Original' };
    const local = { cantidad: 7, descripcion: 'Mía' };
    const remote = { cantidad: 9, descripcion: 'De colega' };

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={fakeForm()}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
      />,
    );

    expect(screen.getByText('Cambios remotos relevantes')).toBeInTheDocument();
    // Mensaje de "no se solapan" NO debe aparecer.
    expect(screen.queryByText(/no se solapan/i)).not.toBeInTheDocument();
    // Valores remotos visibles.
    expect(screen.getByText('De colega')).toBeInTheDocument();
    expect(screen.getByText('9')).toBeInTheDocument();
  });

  it('"Reaplicar mis cambios" hace form.reset({...remote, ...cambiosLocales}) y cierra', () => {
    const baseline = { cantidad: 5, descripcion: 'Orig' };
    const local = { cantidad: 7, descripcion: 'Orig' };
    const remote = { cantidad: 5, descripcion: 'Cambio remoto' };

    const form = fakeForm();
    const onOpenChange = vi.fn();

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={onOpenChange}
        onRefrescar={() => {}}
        form={form}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: /reaplicar mis cambios/i }),
    );

    // Default: hace merge — remoto + cambio local (cantidad=7).
    expect(form.reset).toHaveBeenCalledWith({
      cantidad: 7, // local sobrescribe remoto
      descripcion: 'Cambio remoto', // remoto se preserva
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('"Reaplicar" usa onReaplicar override si está provisto, NO hace form.reset default', () => {
    const baseline = { cantidad: 5 };
    const local = { cantidad: 7 };
    const remote = { cantidad: 9 };

    const form = fakeForm();
    const onReaplicar = vi.fn();

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={form}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
        onReaplicar={onReaplicar}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: /reaplicar mis cambios/i }),
    );

    expect(onReaplicar).toHaveBeenCalledTimes(1);
    expect(form.reset).not.toHaveBeenCalled();
  });

  it('"Solo refrescar (descartar mis cambios)" hace form.reset(remote) e invoca onRefrescar', () => {
    const baseline = { cantidad: 5 };
    const local = { cantidad: 7 };
    const remote = { cantidad: 9 };

    const form = fakeForm();
    const onRefrescar = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={onOpenChange}
        onRefrescar={onRefrescar}
        form={form}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /solo refrescar/i }));

    expect(form.reset).toHaveBeenCalledWith({ cantidad: 9 });
    expect(onRefrescar).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('"Cancelar" cierra sin tocar el form ni invocar onRefrescar', () => {
    const form = fakeForm();
    const onRefrescar = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={onOpenChange}
        onRefrescar={onRefrescar}
        form={form}
        localValues={{ a: 1 }}
        baselineValues={{ a: 1 }}
        latestRemote={{ a: 2 }}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(form.reset).not.toHaveBeenCalled();
    expect(onRefrescar).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('aplica fieldLabels a los nombres de columna del diff', () => {
    const baseline = { requisitanteId: 'u1' };
    const local = { requisitanteId: 'u2' };
    const remote = { requisitanteId: 'u3' };

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={fakeForm()}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
        fieldLabels={{ requisitanteId: 'Requisitante' }}
      />,
    );

    expect(screen.getAllByText('Requisitante').length).toBeGreaterThan(0);
    // El humanizado por default ("Requisitante id") NO aparece.
    expect(screen.queryByText('Requisitante id')).not.toBeInTheDocument();
  });

  it('muestra "—" para valores null/empty/undefined en el diff', () => {
    const baseline = { descripcion: 'Algo' };
    const local = { descripcion: null };
    const remote = { descripcion: '' };

    render(
      <ConflictResolutionDialog
        open
        onOpenChange={() => {}}
        onRefrescar={() => {}}
        form={fakeForm()}
        localValues={local}
        baselineValues={baseline}
        latestRemote={remote}
      />,
    );

    // En la sección de diff aparece "—" en dos celdas (local null y
    // remote string vacío), getAllByText devuelve >=2.
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2);
  });
});
