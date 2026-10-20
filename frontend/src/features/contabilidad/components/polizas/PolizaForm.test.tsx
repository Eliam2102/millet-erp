import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PolizaForm } from './PolizaForm';

const API = '*/api/v1/contabilidad';
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json(
    { title: 'x', status, ...body },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );

function servidor() {
  mswServer.use(
    http.get(`${API}/tipos-documento`, () =>
      HttpResponse.json([
        {
          id: 't1',
          clave: 'TT',
          nombre: 'FIX Traspasos',
          activo: true,
          esPrueba: false,
          version: 1,
          tipoSat: 'Diario',
        },
      ]),
    ),
    http.get(`${API}/movimientos/sucursales`, () =>
      HttpResponse.json([{ id: 's1', clave: 'MER', nombre: 'FIX Mérida', activa: true }]),
    ),
  );
}

describe('<PolizaForm>', () => {
  it('el indicador de cuadre muestra cargos, abonos y diferencia en vivo', async () => {
    servidor();
    render(<PolizaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    const cuadre = screen.getByRole('status', { name: 'Cuadre de la póliza' });
    expect(cuadre).toHaveTextContent('No cuadra: captura los importes.');

    fireEvent.change(screen.getByLabelText('Cargo', { selector: '#pol-p0-cargo' }), {
      target: { value: '1500.50' },
    });
    fireEvent.change(screen.getByLabelText('Abono', { selector: '#pol-p1-abono' }), {
      target: { value: '1000' },
    });
    expect(cuadre).toHaveTextContent('Total cargos$1,500.50');
    expect(cuadre).toHaveTextContent('Total abonos$1,000.00');
    expect(cuadre).toHaveTextContent('Diferencia$500.50');
    expect(cuadre).toHaveTextContent('No cuadra: cargos y abonos no son iguales.');

    fireEvent.change(screen.getByLabelText('Abono', { selector: '#pol-p1-abono' }), {
      target: { value: '1500.50' },
    });
    expect(cuadre).toHaveTextContent('Diferencia$0.00');
    expect(cuadre).toHaveTextContent('Cuadra: cargos y abonos son iguales.');

    // Capturar un abono en la partida que ya tenía cargo limpia el cargo (una partida va de un solo lado).
    fireEvent.change(screen.getByLabelText('Abono', { selector: '#pol-p0-abono' }), {
      target: { value: '10' },
    });
    expect(screen.getByLabelText('Cargo', { selector: '#pol-p0-cargo' })).toHaveValue('');
  });

  it('periodo 13 fija la fecha al 31 de diciembre y viaja periodoNumero 13; los errores del servidor se pintan por partida', async () => {
    servidor();
    const cuerpos: Record<string, unknown>[] = [];
    mswServer.use(
      http.post(`${API}/polizas`, async ({ request }) => {
        cuerpos.push((await request.json()) as Record<string, unknown>);
        return problem(400, {
          errores: [
            {
              campo: 'partidas[1].cuentaId',
              codigo: 'X',
              mensaje: 'Partida 2: Elija la cuenta contable.',
            },
          ],
        });
      }),
    );
    render(<PolizaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('option', { name: 'TT — FIX Traspasos (Diario)' });
    fireEvent.change(screen.getByLabelText(/^Tipo de operación(?:\s*\*)?$/), {
      target: { value: 't1' },
    });
    fireEvent.change(screen.getByLabelText(/^Concepto(?:\s*\*)?$/, { selector: '#pol-concepto' }), {
      target: { value: 'FIX Ajuste de auditoría' },
    });
    fireEvent.click(screen.getByLabelText('Periodo 13 (ajustes de auditoría)'));
    expect(screen.getByLabelText(/^Fecha(?:\s*\*)?$/)).toHaveValue(
      `${new Date().getFullYear()}-12-31`,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Guardar borrador' }));

    expect(
      await screen.findByText('Partida 2: Elija la cuenta contable.', {
        selector: '#pol-p1-cuenta-err span',
      }),
    ).toBeInTheDocument();
    await waitFor(() => expect(cuerpos).toHaveLength(1));
    expect(cuerpos[0]).toMatchObject({
      tipoOperacionId: 't1',
      periodoNumero: 13,
      concepto: 'FIX Ajuste de auditoría',
    });
    expect((cuerpos[0].partidas as unknown[]).length).toBe(2);
  });
});
