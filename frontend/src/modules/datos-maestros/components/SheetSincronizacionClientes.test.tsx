import { afterEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SheetSincronizacionClientes } from '@/modules/datos-maestros/components/SheetSincronizacionClientes';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const URL_LISTA = '*/clientes/sincronizacion/ejecuciones';

const ej = (estado: string, extra = {}) => ({
  id: 'e-1', tipo: 'Barrido', estado, leidos: 5, creados: 1, actualizados: 2,
  sinCambios: 1, pendientes: 0, conflictos: 1, errores: 1,
  iniciadaEnUtc: '2026-09-01T10:00:00Z', terminadaEnUtc: null, actor: 'admin',
  reintentoDeId: null, errorGeneral: null, ...extra,
});
const lista = (items: unknown[]) => HttpResponse.json({ items, offset: 0, limit: 20, total: items.length });

function abrir() {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'a@b.com', nombre: 'A' }, empresas: [], currentEmpresaId: 'e',
    permisos: [PermisosCanonicos.DatosMaestrosClientesSincronizar], errorMessage: null,
  });
  return render(<SheetSincronizacionClientes open onOpenChange={() => {}} />, {
    wrapper: createQueryWrapper(),
  });
}

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

describe('<SheetSincronizacionClientes>', () => {
  it('estado vacío', async () => {
    mswServer.use(http.get(URL_LISTA, () => lista([])));
    abrir();
    expect(await screen.findByText('Sin ejecuciones.')).toBeInTheDocument();
  });

  it('estado de error', async () => {
    mswServer.use(
      http.get(URL_LISTA, () =>
        HttpResponse.json({ type: 'x', title: 'Fallo interno', status: 500 }, {
          status: 500, headers: { 'Content-Type': 'application/problem+json' },
        }),
      ),
    );
    abrir();
    expect(await screen.findByText('Fallo interno')).toBeInTheDocument();
  });

  it('403 muestra sin permiso', async () => {
    mswServer.use(
      http.get(URL_LISTA, () =>
        HttpResponse.json({ type: 'x', title: 'Forbidden', status: 403 }, {
          status: 403, headers: { 'Content-Type': 'application/problem+json' },
        }),
      ),
    );
    abrir();
    expect(await screen.findByText('Sin permiso')).toBeInTheDocument();
  });

  it('Parcial y Fallida no se muestran como éxito', async () => {
    mswServer.use(
      http.get(URL_LISTA, () =>
        lista([ej('Parcial', { id: 'a' }), ej('Fallida', { id: 'b' })]),
      ),
    );
    abrir();
    expect(await screen.findByText('Parcial (con errores)')).toBeInTheDocument();
    expect(screen.getByText('Fallida')).toBeInTheDocument();
    expect(screen.queryByText('Completa')).not.toBeInTheDocument();
  });

  it('Pendiente muestra "En cola" y el detalle hace polling hasta terminar', async () => {
    let llamadas = 0;
    mswServer.use(
      http.get(URL_LISTA, () => lista([ej('Pendiente')])),
      http.get(`${URL_LISTA}/e-1`, () => {
        llamadas++;
        return HttpResponse.json({
          ejecucion: ej(llamadas === 1 ? 'Pendiente' : 'Completa'),
          errores: [{ referencia: 'AW-9', codigo: 'conflicto_correlacion', mensaje: 'Existe un cliente manual' }],
          erroresTruncados: false,
        });
      }),
    );
    abrir();
    const fila = await screen.findByText('En cola', { selector: 'span *, span' });
    fila.closest('button')!.click();
    expect(await screen.findByText(/ref\. AW-9/)).toBeInTheDocument();
    await waitFor(() => expect(llamadas).toBeGreaterThan(1), { timeout: 6000 });
    expect(await screen.findByText('Completa')).toBeInTheDocument();
  }, 10_000);

  it('muestra causa, acción sugerida y avisa cuando hay más errores de los listados', async () => {
    mswServer.use(
      http.get(URL_LISTA, () => lista([ej('Parcial')])),
      http.get(`${URL_LISTA}/e-1`, () =>
        HttpResponse.json({
          ejecucion: ej('Parcial'),
          errores: [{ referencia: '91002', codigo: 'moneda_sin_equivalencia', mensaje: "la moneda '<indf>' no tiene equivalencia" }],
          erroresTruncados: true,
        }),
      ),
    );
    abrir();
    const fila = await screen.findByText('Parcial (con errores)');
    fila.closest('button')!.click();
    expect(await screen.findByText('Moneda sin equivalencia')).toBeInTheDocument();
    expect(screen.getByText(/Configura el mapeo de moneda y reintenta/)).toBeInTheDocument();
    expect(screen.getByText(/solo los primeros errores/)).toBeInTheDocument();
  });

  it('reintentar por fila envía la referencia al endpoint de reintentos', async () => {
    let body: unknown = null;
    mswServer.use(
      http.get(URL_LISTA, () => lista([ej('Parcial')])),
      http.get(`${URL_LISTA}/e-1`, () =>
        HttpResponse.json({
          ejecucion: ej('Parcial'),
          errores: [{ referencia: 'AW-9', codigo: 'conflicto_correlacion', mensaje: 'Existe un cliente manual' }],
          erroresTruncados: false,
        }),
      ),
      http.post('*/clientes/sincronizacion/reintentos', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ejecucion: ej('Completa', { id: 'r-1' }), errores: [], erroresTruncados: false });
      }),
    );
    abrir();
    const fila = await screen.findByText('Parcial (con errores)');
    fila.closest('button')!.click();
    (await screen.findByRole('button', { name: 'Reintentar referencia AW-9' })).click();
    await waitFor(() => expect(body).toEqual({ referencia: 'AW-9' }));
  });
});
