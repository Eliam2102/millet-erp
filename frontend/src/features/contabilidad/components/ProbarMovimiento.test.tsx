import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProbarMovimiento } from './ProbarMovimiento';

const URL_VALIDAR = '*/api/v1/contabilidad/cuentas/validar-movimiento';
const cuenta = { id: 'c1', codigo: 'FIX-102.02', nombre: 'FIX Deudores diversos', cuentaControl: 'Deudores' };

describe('<ProbarMovimiento>', () => {
  it('envía la cuenta y el origen elegido y explica el rechazo de una colectiva por captura manual', async () => {
    const pedidos: unknown[] = [];
    mswServer.use(http.post(URL_VALIDAR, async ({ request }) => {
      pedidos.push(await request.json());
      return HttpResponse.json({ valida: false, motivo: 'ControlSoloAuxiliar', cuenta });
    }));
    render(<ProbarMovimiento cuentaId="c1" />, { wrapper: createQueryWrapper() });
    expect([...screen.getByLabelText('Origen del movimiento').querySelectorAll('option')].map((o) => o.textContent))
      .toEqual(['Captura manual', 'Módulo de cuentas por cobrar', 'Módulo de cuentas por pagar']);

    fireEvent.click(screen.getByRole('button', { name: 'Probar' }));
    const estado = await screen.findByRole('status');
    expect(estado).toHaveTextContent('Rechaza');
    expect(estado).toHaveTextContent(/cuenta colectiva de deudores; solo se afecta desde su módulo/);
    expect(pedidos).toEqual([{ cuentaId: 'c1', origen: 'Manual' }]);
  });

  it('el módulo autorizado: acepta; cambiar el origen limpia el resultado anterior', async () => {
    mswServer.use(http.post(URL_VALIDAR, async ({ request }) => {
      const { origen } = (await request.json()) as { origen: string };
      return HttpResponse.json(origen === 'AuxiliarCxC'
        ? { valida: true, motivo: null, cuenta }
        : { valida: false, motivo: 'ControlSoloAuxiliar', cuenta });
    }));
    render(<ProbarMovimiento cuentaId="c1" />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Origen del movimiento'), { target: { value: 'AuxiliarCxC' } });
    fireEvent.click(screen.getByRole('button', { name: 'Probar' }));
    expect(await screen.findByRole('status')).toHaveTextContent(/acepta movimientos desde «Módulo de cuentas por cobrar»/);
    fireEvent.change(screen.getByLabelText('Origen del movimiento'), { target: { value: 'AuxiliarCxP' } });
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());
  });

  it.each([
    ['Titulo', /acumula/],
    ['Inactiva', /inactiva/],
    ['PendienteValidacion', /pendiente de validación/],
    ['Rubro', /rubro de reporte/],
  ])('motivo %s en lenguaje de usuario', async (motivo, texto) => {
    mswServer.use(http.post(URL_VALIDAR, () => HttpResponse.json({ valida: false, motivo, cuenta: { ...cuenta, cuentaControl: 'Ninguna' } })));
    render(<ProbarMovimiento cuentaId="c1" />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Probar' }));
    expect(await screen.findByRole('status')).toHaveTextContent(texto);
  });

  it('fallo de red: aviso recuperable', async () => {
    mswServer.use(http.post(URL_VALIDAR, () => HttpResponse.error()));
    render(<ProbarMovimiento cuentaId="c1" />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Probar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/No se pudo hacer la prueba/);
  });
});
