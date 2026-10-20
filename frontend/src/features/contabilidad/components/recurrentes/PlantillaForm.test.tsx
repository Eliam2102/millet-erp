import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { plantilla } from '../../lib/__fixtures__/recurrentes-fix';
import type { PlantillaRecurrente } from '../../api/recurrentes-types';
import { PlantillaForm } from './PlantillaForm';

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
    http.get(`${API}/cuentas/:id`, ({ params }) =>
      HttpResponse.json({
        id: params.id,
        codigo: `FIX-${String(params.id)}`,
        nombre: 'FIX Cuenta',
        version: 1,
      }),
    ),
  );
}

const guardar = () => screen.getByRole('button', { name: 'Guardar plantilla' });

describe('<PlantillaForm>', () => {
  it('no se guarda hasta que cuadra; valida el número de periodos (1 a 120)', async () => {
    servidor();
    render(<PlantillaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('option', { name: 'TT — FIX Traspasos (Diario)' });
    fireEvent.change(screen.getByLabelText(/^Nombre de la plantilla(?:\s*\*)?$/), {
      target: { value: 'FIX Provisión de renta' },
    });
    fireEvent.change(screen.getByLabelText(/^Tipo de operación(?:\s*\*)?$/), {
      target: { value: 't1' },
    });
    fireEvent.change(screen.getByLabelText(/^Concepto de las pólizas(?:\s*\*)?$/), {
      target: { value: 'FIX Provisión mensual' },
    });

    const cuadre = screen.getByRole('status', { name: 'Cuadre de la póliza' });
    expect(guardar()).toBeDisabled();
    expect(guardar()).toHaveAttribute(
      'title',
      'La plantilla debe cuadrar: cargos y abonos iguales y distintos de cero',
    );

    fireEvent.change(screen.getByLabelText('Cargo', { selector: '#rec-p0-cargo' }), {
      target: { value: '5000' },
    });
    fireEvent.change(screen.getByLabelText('Abono', { selector: '#rec-p1-abono' }), {
      target: { value: '4000' },
    });
    expect(cuadre).toHaveTextContent('Diferencia$1,000.00');
    expect(guardar()).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Abono', { selector: '#rec-p1-abono' }), {
      target: { value: '5000' },
    });
    expect(cuadre).toHaveTextContent('Cuadra: cargos y abonos son iguales.');
    expect(guardar()).toBeEnabled();

    fireEvent.change(screen.getByLabelText(/^Número de periodos(?:\s*\*)?$/), {
      target: { value: '121' },
    });
    expect(screen.getByText('De 1 a 120 periodos.')).toBeInTheDocument();
    expect(guardar()).toBeDisabled();
    expect(guardar()).toHaveAttribute('title', 'El número de periodos va de 1 a 120');

    fireEvent.change(screen.getByLabelText(/^Número de periodos(?:\s*\*)?$/), {
      target: { value: '12' },
    });
    expect(guardar()).toBeEnabled();
    expect(screen.getByText(/^Vigencia: /)).toBeInTheDocument();
  });

  it('envía el cuerpo de la plantilla y pinta por partida los errores del servidor ({ field, message })', async () => {
    servidor();
    const cuerpos: Record<string, unknown>[] = [];
    let idem: string | null = null;
    mswServer.use(
      http.post(`${API}/polizas-recurrentes`, async ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        cuerpos.push((await request.json()) as Record<string, unknown>);
        return problem(400, {
          errores: [
            {
              field: 'Partidas[0].Dim2Id',
              code: 'X',
              message: 'Partida 1: la cuenta exige departamento.',
            },
          ],
        });
      }),
    );
    render(<PlantillaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('option', { name: 'TT — FIX Traspasos (Diario)' });
    fireEvent.change(screen.getByLabelText(/^Nombre de la plantilla(?:\s*\*)?$/), {
      target: { value: 'FIX Renta' },
    });
    fireEvent.change(screen.getByLabelText(/^Tipo de operación(?:\s*\*)?$/), {
      target: { value: 't1' },
    });
    fireEvent.change(screen.getByLabelText(/^Concepto de las pólizas(?:\s*\*)?$/), {
      target: { value: 'FIX Provisión' },
    });
    fireEvent.change(screen.getByLabelText(/^Año de inicio(?:\s*\*)?$/), {
      target: { value: '2026' },
    });
    fireEvent.change(screen.getByLabelText(/^Mes de inicio(?:\s*\*)?$/), {
      target: { value: '3' },
    });
    fireEvent.change(screen.getByLabelText(/^Número de periodos(?:\s*\*)?$/), {
      target: { value: '6' },
    });
    fireEvent.change(screen.getByLabelText(/^Día del mes(?:\s*\*)?$/), { target: { value: '15' } });
    fireEvent.click(screen.getByLabelText('Reversa automática el día 1 del mes siguiente'));
    fireEvent.change(screen.getByLabelText('Cargo', { selector: '#rec-p0-cargo' }), {
      target: { value: '1,250.50' },
    });
    fireEvent.change(screen.getByLabelText('Abono', { selector: '#rec-p1-abono' }), {
      target: { value: '1250.50' },
    });
    fireEvent.click(guardar());

    expect(
      await screen.findByText('Partida 1: la cuenta exige departamento.', {
        selector: '#rec-p0-dim2Id-err span',
      }),
    ).toBeInTheDocument();
    await waitFor(() => expect(cuerpos).toHaveLength(1));
    expect(idem).toBeTruthy();
    expect(cuerpos[0]).toMatchObject({
      nombre: 'FIX Renta',
      tipoOperacionId: 't1',
      concepto: 'FIX Provisión',
      anioInicio: 2026,
      mesInicio: 3,
      numeroPeriodos: 6,
      diaDelMes: 15,
      reversaAutomatica: true,
      naceAutorizada: false,
    });
    const partidas = cuerpos[0].partidas as { cargo: number; abono: number }[];
    expect(partidas.map((p) => [p.cargo, p.abono])).toEqual([
      [1250.5, 0],
      [0, 1250.5],
    ]);
  });

  it('al editar carga la plantilla, completa la cuenta con el catálogo y guarda con If-Match', async () => {
    servidor();
    let ifMatch: string | null = null;
    const p = plantilla({ estado: 'Borrador' }) as unknown as PlantillaRecurrente;
    mswServer.use(
      http.put(`${API}/polizas-recurrentes/r1`, ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        return HttpResponse.json(p);
      }),
    );
    const onGuardada = vi.fn();
    render(<PlantillaForm plantilla={p} onGuardada={onGuardada} onCancelar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByLabelText(/^Nombre de la plantilla(?:\s*\*)?$/)).toHaveValue(
      'FIX Provisión de renta',
    );
    expect(screen.getByLabelText(/^Día del mes(?:\s*\*)?$/)).toHaveValue('0');
    expect(screen.getByLabelText('Reversa automática el día 1 del mes siguiente')).toBeChecked();
    expect(await screen.findByText('FIX-c1')).toBeInTheDocument();
    await screen.findByRole('option', { name: 'TT — FIX Traspasos (Diario)' });
    fireEvent.click(guardar());
    await waitFor(() => expect(onGuardada).toHaveBeenCalled());
    expect(ifMatch).toBe('"2"');
  });
});
