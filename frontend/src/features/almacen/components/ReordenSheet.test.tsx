import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReordenSheet } from '@/features/almacen/components/ReordenSheet';
import {
  NivelReorden,
  ObjetivoReposicion,
  type ConfiguracionReordenListItem,
} from '@/features/almacen/api/types';

// Capturamos las variables (incl. idempotencyKey) pasadas al mutate.
const editarMutate = vi.fn();
const crearMutate = vi.fn();

vi.mock('@/features/almacen/api/useReorden', () => ({
  useCrearReorden: () => ({ mutate: crearMutate, isPending: false }),
  useEditarReorden: () => ({ mutate: editarMutate, isPending: false }),
}));

// Stubs de los selectores: en edición van disabled y no aportan al test;
// evitan el react-query de catálogos.
vi.mock('@/components/erp', () => ({
  ArticuloSelector: () => <div data-testid="articulo-selector" />,
  EntidadReordenSelector: () => <div data-testid="entidad-selector" />,
}));

const EDITANDO: ConfiguracionReordenListItem = {
  id: 'cfg-1',
  articuloId: '00000000-0000-0000-0000-0000000000a1',
  nivel: NivelReorden.Almacen,
  entidadId: '00000000-0000-0000-0000-0000000000b1',
  minimo: 10,
  maximo: 100,
  puntoReorden: 30,
  autoRequisicion: true,
  objetivo: ObjetivoReposicion.Maximo,
  estatus: 0,
  articuloClave: 'ART-1',
  articuloDescripcion: 'Silicón',
};

beforeEach(() => {
  editarMutate.mockClear();
  crearMutate.mockClear();
});

describe('<ReordenSheet> — Idempotency-Key por submit (PR B)', () => {
  it('regenera la llave en cada submit: un reintento tras un fallo usa una llave DISTINTA', async () => {
    // Modo edición: el form arranca pre-poblado y válido, así el submit dispara
    // onSubmit sin tener que llenar los selectores. El mock NO llama onSuccess,
    // así que el sheet queda abierto (simula el primer intento fallido) y se
    // puede reintentar.
    render(<ReordenSheet open onOpenChange={() => {}} editando={EDITANDO} />, {
      wrapper: createQueryWrapper(),
    });

    const guardar = screen.getByRole('button', { name: /Guardar cambios/i });

    // Primer submit (el "fallido").
    fireEvent.click(guardar);
    await waitFor(() => expect(editarMutate).toHaveBeenCalledTimes(1));

    // Reintento del usuario (mismo sheet, mismo body).
    fireEvent.click(guardar);
    await waitFor(() => expect(editarMutate).toHaveBeenCalledTimes(2));

    const key1 = editarMutate.mock.calls[0][0].idempotencyKey as string;
    const key2 = editarMutate.mock.calls[1][0].idempotencyKey as string;

    expect(key1).toBeTruthy();
    expect(key2).toBeTruthy();
    // El fix: llave fresca por submit → el reintento NO reusa la quemada (evita 409).
    expect(key1).not.toBe(key2);
  });
});
