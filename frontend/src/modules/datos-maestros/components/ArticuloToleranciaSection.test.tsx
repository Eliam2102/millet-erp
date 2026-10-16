import { afterEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ArticuloToleranciaSection } from './ArticuloToleranciaSection';

const permiso = PermisosCanonicos.DatosMaestrosArticulosToleranciaEditar;
afterEach(() => { cleanup(); useAuthStore.setState({ permisos: [] }); });

function mostrar(permisos: string[], porcentaje: number | null = 5) {
  useAuthStore.setState({ status: 'authenticated', permisos, accessToken: 'test-token' });
  return render(<ArticuloToleranciaSection articuloId="a-demo" porcentaje={porcentaje} />, { wrapper: createQueryWrapper() });
}

describe('Tolerancia de recepción del artículo', () => {
  it('sin permiso de edición muestra solo lectura, sin botones', () => {
    mostrar([PermisosCanonicos.DatosMaestrosArticulosGestionar]);
    expect(screen.getByText('5%')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('con permiso guarda el porcentaje con idempotencia', async () => {
    const enviados: unknown[] = [];
    mswServer.use(http.put('*/api/v1/datos-maestros/articulos/a-demo/tolerancia-recepcion', async ({ request }) => {
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      enviados.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    mostrar([permiso]);
    fireEvent.click(screen.getByRole('button', { name: 'Editar tolerancia' }));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar tolerancia' }));
    await waitFor(() => expect(enviados).toEqual([{ porcentaje: 5 }]));
    await waitFor(() => expect(screen.queryByRole('textbox')).not.toBeInTheDocument());
  });

  it('100.01 muestra el error y deshabilita guardar', () => {
    mostrar([permiso], null);
    fireEvent.click(screen.getByRole('button', { name: 'Editar tolerancia' }));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '100.01' } });
    expect(screen.getByRole('button', { name: 'Guardar tolerancia' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent('Ingresa un porcentaje entre 0 y 100 con hasta 2 decimales.');
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(screen.getByText('Sin tolerancia (0 %)')).toBeInTheDocument();
  });
});
