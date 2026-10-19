import { afterEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedorToleranciaSection } from './ProveedorToleranciaSection';

const permiso = PermisosCanonicos.DatosMaestrosProveedoresToleranciaEditar;
afterEach(() => { cleanup(); useAuthStore.setState({ permisos: [] }); });

function mostrar(permisos: string[], montoMxn: number | null = 5, esPorcentaje = false) {
  useAuthStore.setState({ status: 'authenticated', permisos, accessToken: 'test-token' });
  return render(<ProveedorToleranciaSection proveedorId="p-demo" montoMxn={montoMxn} esPorcentaje={esPorcentaje} />, { wrapper: createQueryWrapper() });
}

describe('Tolerancia del proveedor', () => {
  it('Compras consulta el monto sin controles de edición', () => {
    mostrar([PermisosCanonicos.DatosMaestrosProveedoresGestionar]);
    expect(screen.getByText('$5.00 MXN')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
  });

  it.each([['1.50', 1.5], ['0', 0], ['', null]] as const)('CxP guarda %s con idempotencia', async (texto, esperado) => {
    const enviados: unknown[] = [];
    mswServer.use(http.put('*/api/v1/datos-maestros/proveedores/p-demo/tolerancia', async ({ request }) => {
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      enviados.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    mostrar([permiso]);
    fireEvent.click(screen.getByRole('button', { name: 'Editar tolerancia' }));
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: texto } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar tolerancia' }));
    await waitFor(() => expect(enviados).toEqual([{ montoMxn: esperado, esPorcentaje: false }]));
    await waitFor(() => expect(screen.queryByRole('textbox')).not.toBeInTheDocument());
  });

  it('CxP guarda la tolerancia en porcentaje y rechaza más de 100 %', async () => {
    const enviados: unknown[] = [];
    mswServer.use(http.put('*/api/v1/datos-maestros/proveedores/p-demo/tolerancia', async ({ request }) => {
      enviados.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    mostrar([permiso], null);
    fireEvent.click(screen.getByRole('button', { name: 'Editar tolerancia' }));
    fireEvent.click(screen.getByLabelText('En porcentaje de la OC'));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '100.5' } });
    expect(screen.getByRole('button', { name: 'Guardar tolerancia' })).toBeDisabled();
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '2.5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar tolerancia' }));
    await waitFor(() => expect(enviados).toEqual([{ montoMxn: 2.5, esPorcentaje: true }]));
  });

  it('muestra la tolerancia en porcentaje guardada', () => {
    mostrar([PermisosCanonicos.DatosMaestrosProveedoresGestionar], 2.5, true);
    expect(screen.getByText('2.5 % del total de la OC')).toBeInTheDocument();
  });

  it('rechaza negativos y permite cancelar sin enviar', async () => {
    mostrar([permiso], null);
    fireEvent.click(screen.getByRole('button', { name: 'Editar tolerancia' }));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '-1' } });
    expect(screen.getByRole('button', { name: 'Guardar tolerancia' })).toBeDisabled();
    expect(screen.getByRole('alert')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(screen.getByText('Usa la tolerancia general de Administración')).toBeInTheDocument();
  });
});
