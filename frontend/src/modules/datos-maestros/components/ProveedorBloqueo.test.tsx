import { afterEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProveedorDetalle } from '@/modules/datos-maestros/api/types';
import { ProveedorBloqueo } from './ProveedorBloqueo';

afterEach(() => { cleanup(); useAuthStore.setState({ permisos: [] }); });

function mostrar(permisos: string[], bloqueado: boolean) {
  useAuthStore.setState({ status: 'authenticated', permisos, accessToken: 'test-token' });
  const proveedor = { id: 'p-demo', bloqueado, motivoBloqueo: bloqueado ? 'Dirección revisa su cuenta' : null } as ProveedorDetalle;
  return render(<ProveedorBloqueo proveedor={proveedor} />, { wrapper: createQueryWrapper() });
}

describe('Bloqueo total del proveedor', () => {
  it('sin el permiso solo se ve el aviso con el motivo, sin acciones', () => {
    mostrar([], true);
    expect(screen.getByText(/Dirección revisa su cuenta/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('bloquear exige un motivo de 10 caracteres y lo envía con idempotencia', async () => {
    const enviados: unknown[] = [];
    mswServer.use(http.post('*/api/v1/datos-maestros/proveedores/p-demo/bloquear', async ({ request }) => {
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      enviados.push(await request.json());
      return HttpResponse.json({ id: 'p-demo', bloqueado: true });
    }));
    mostrar([PermisosCanonicos.DatosMaestrosProveedoresBloquear], false);
    fireEvent.click(screen.getByRole('button', { name: /Bloquear proveedor/ }));
    const enviar = screen.getByRole('button', { name: 'Bloquear' });
    fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'corto' } });
    expect(enviar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'Dirección revisa su cuenta' } });
    fireEvent.click(enviar);
    await waitFor(() => expect(enviados).toEqual([{ motivo: 'Dirección revisa su cuenta' }]));
  });

  it('un proveedor bloqueado ofrece desbloquear', () => {
    mostrar([PermisosCanonicos.DatosMaestrosProveedoresBloquear], true);
    expect(screen.getByRole('button', { name: /Desbloquear proveedor/ })).toBeInTheDocument();
  });
});
