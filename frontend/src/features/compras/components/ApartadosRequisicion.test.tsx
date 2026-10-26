import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ApartadosRequisicion } from '@/features/compras/components/ApartadosRequisicion';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { LineaResponse } from '@/features/compras/api/types';

const RQ = 'rq-1';
const LINEA = 'l-1';
const URL_LIBERAR = `*/api/v1/compras/requisiciones/${RQ}/lineas/${LINEA}/liberar-apartado`;

function setup(permisos: string[], comprometidaEnOc = false) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 't',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
  mswServer.use(
    http.get(`*/api/v1/compras/requisiciones/${RQ}/apartados`, () =>
      HttpResponse.json([
        {
          lineaId: LINEA,
          pendiente: 5,
          liberada: 2,
          unidadMedida: 'PZA',
          ultimoMotivo: 'Ya no se necesita',
        },
      ]),
    ),
  );
  const lineas = [
    { id: LINEA, posicion: 1, articuloClave: 'A1', articuloNombre: 'Tornillo' } as LineaResponse,
  ];
  render(
    <ApartadosRequisicion
      requisicionId={RQ}
      lineas={lineas}
      enabled
      comprometidaEnOc={comprometidaEnOc}
    />,
    {
      wrapper: createQueryWrapper(),
    },
  );
}

const noCompra = () => fireEvent.click(screen.getByLabelText(/No, ya no lo necesita/));
const siCompra = () => fireEvent.click(screen.getByLabelText(/Sí, el requisitante aún lo necesita/));

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    user: null,
    permisos: [],
    currentEmpresaId: null,
  });
});

describe('<ApartadosRequisicion>', () => {
  it('muestra el apartado y oculta la acción sin permiso', async () => {
    setup([]);
    expect(await screen.findByText(/Apartado: 5 PZA/)).toBeInTheDocument();
    expect(screen.getByText(/liberado 2/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /liberar apartado/i })).toBeNull();
  });

  it('valida motivo vacío y cantidad mayor al pendiente', async () => {
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    const enviar = dialog.querySelector('button:last-of-type') as HTMLButtonElement;
    fireEvent.click(enviar);
    expect(await screen.findByText(/motivo es obligatorio/i)).toBeInTheDocument();

    const cantidad = screen.getByLabelText(/cantidad a liberar/i);
    fireEvent.change(cantidad, { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'x' } });
    fireEvent.click(enviar);
    expect(await screen.findByText(/no puede exceder lo apartado/i)).toBeInTheDocument();
  });

  it('envía POST con body e Idempotency-Key', async () => {
    let body: unknown = null;
    let key: string | null = null;
    mswServer.use(
      http.post(URL_LIBERAR, async ({ request }) => {
        body = await request.json();
        key = request.headers.get('Idempotency-Key');
        return new HttpResponse(null, { status: 204 });
      }),
    );
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    const cantidad = screen.getByLabelText(/cantidad a liberar/i);
    fireEvent.change(cantidad, { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText(/motivo/i), {
      target: { value: '  Ya no se necesita ' },
    });
    noCompra();
    fireEvent.click(dialog.querySelector('button:last-of-type') as HTMLButtonElement);
    await waitFor(() =>
      expect(body).toEqual({ cantidad: 3, motivo: 'Ya no se necesita', mandarACompra: false }),
    );
    expect(key).toBeTruthy();
  });

  it('doble clic síncrono envía un solo POST', async () => {
    let llamadas = 0;
    mswServer.use(
      http.post(URL_LIBERAR, async () => {
        llamadas++;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(screen.getByLabelText(/cantidad a liberar/i), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'Ya no se necesita' } });
    noCompra();
    const enviar = dialog.querySelector('button:last-of-type') as HTMLButtonElement;
    fireEvent.click(enviar);
    fireEvent.click(enviar);
    await waitFor(() => expect(llamadas).toBe(1));
    await new Promise((r) => setTimeout(r, 50));
    expect(llamadas).toBe(1);
  });

  it('reintento con mismos valores reusa la Idempotency-Key; cambiar motivo la renueva', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.post(URL_LIBERAR, ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'));
        return HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla',
            status: 422,
            code: 'APARTADO_SIN_PENDIENTE',
            detail: 'No hay apartado pendiente.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }),
    );
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    const enviar = dialog.querySelector('button:last-of-type') as HTMLButtonElement;
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'x' } });
    noCompra();
    fireEvent.click(enviar);
    await waitFor(() => expect(keys).toHaveLength(1));
    await screen.findByText('No hay apartado pendiente.');
    fireEvent.click(enviar);
    await waitFor(() => expect(keys).toHaveLength(2));
    expect(keys[1]).toBe(keys[0]);
    await waitFor(() => expect(enviar).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'otro' } });
    fireEvent.click(enviar);
    await waitFor(() => expect(keys).toHaveLength(3));
    expect(keys[2]).not.toBe(keys[0]);
  });

  it('muestra el detail del 422 inline', async () => {
    mswServer.use(
      http.post(URL_LIBERAR, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla',
            status: 422,
            code: 'APARTADO_SIN_PENDIENTE',
            detail: 'No hay apartado pendiente.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'x' } });
    noCompra();
    fireEvent.click(dialog.querySelector('button:last-of-type') as HTMLButtonElement);
    expect(await screen.findByText('No hay apartado pendiente.')).toBeInTheDocument();
  });

  it('exige elegir si va a compra y manda true al elegir Sí', async () => {
    let body: unknown = null;
    mswServer.use(
      http.post(URL_LIBERAR, async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado]);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    const dialog = await screen.findByRole('dialog');
    const enviar = dialog.querySelector('button:last-of-type') as HTMLButtonElement;
    fireEvent.change(screen.getByLabelText(/motivo/i), { target: { value: 'Otra obra' } });
    fireEvent.click(enviar);
    expect(await screen.findByText(/indica si lo liberado se manda a compra/i)).toBeInTheDocument();
    expect(body).toBeNull();
    siCompra();
    fireEvent.click(enviar);
    await waitFor(() =>
      expect(body).toEqual({ cantidad: 5, motivo: 'Otra obra', mandarACompra: true }),
    );
  });

  it('con la RQ en una OC no deja mandar a compra', async () => {
    setup([PermisosCanonicos.ComprasRequisicionesLiberarApartado], true);
    fireEvent.click(await screen.findByRole('button', { name: /liberar apartado/i }));
    await screen.findByRole('dialog');
    expect(screen.getByLabelText(/Sí, el requisitante aún lo necesita/)).toBeDisabled();
    expect(screen.getByLabelText(/No, ya no lo necesita/)).not.toBeDisabled();
    expect(screen.getByText(/ya está en una orden de compra/i)).toBeInTheDocument();
  });
});
