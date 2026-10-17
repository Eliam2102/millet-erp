import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { AsignarUbicacionEnLinea } from '@/features/almacen/components/AsignarUbicacionEnLinea';

const permiso = vi.hoisted(() => ({ valor: true }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.valor }));

describe('<AsignarUbicacionEnLinea>', () => {
  it('con permiso muestra el botón «Asignar ubicación»', () => {
    permiso.valor = true;
    render(<AsignarUbicacionEnLinea articuloId="a" onAsignada={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Asignar ubicación' })).toBeInTheDocument();
  });

  it('sin permiso muestra el aviso y no el botón', () => {
    permiso.valor = false;
    render(<AsignarUbicacionEnLinea articuloId="a" onAsignada={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('note')).toHaveTextContent(/no tiene rack asignado/i);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
