import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Distribucion } from '../api/finanzas';
import { DistribucionTabla } from './FinanzasPanel';

const distribucion: Distribucion = {
  numeroVersion: 1,
  fecha: '2026-10-20',
  usuarioId: 'usuario',
  renglones: [
    {
      gastoId: 'g',
      conceptoId: 'c',
      base: 1,
      partidaId: 'p',
      baseUsada: 200000,
      importeMxn: 10000,
    },
  ],
  partidas: [
    {
      partidaId: 'p',
      descripcion: 'Vidrio importado',
      clienteId: null,
      importeMxn: 10000,
      diferenciaMxn: 10000,
      inventarioMxn: 0,
      gastoImportacionMxn: 10000,
      tieneSalidas: true,
      costoUnitarioAntes: 100,
      costoUnitarioDespues: 100,
    },
  ],
};

describe('Vista previa de costo puesto', () => {
  it('muestra el gasto tardío completo y conserva el costo unitario', () => {
    render(<DistribucionTabla d={distribucion} />);
    expect(screen.getByText('Vidrio importado')).toBeInTheDocument();
    expect(screen.getByText('Valor en aduana: 200,000')).toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'A gastos de importación' }),
    ).toBeInTheDocument();
    expect(screen.getByText('$0.00')).toBeInTheDocument();
    expect(screen.getAllByText('$10,000.00')).toHaveLength(3);
    expect(screen.getAllByText('$100.00')).toHaveLength(2);
    expect(screen.getByRole('note')).toHaveTextContent(
      'Parte de la mercancía ya salió: el gasto de esas partidas va completo a gastos de importación y no cambia su costo',
    );
  });
  it('no muestra aviso cuando toda la mercancía sigue en existencia', () => {
    render(
      <DistribucionTabla
        d={{
          ...distribucion,
          partidas: [
            {
              ...distribucion.partidas[0],
              tieneSalidas: false,
              inventarioMxn: 10000,
              gastoImportacionMxn: 0,
              costoUnitarioDespues: 110,
            },
          ],
        }}
      />,
    );
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
    expect(
      screen.getByRole('columnheader', { name: 'A gastos de importación' }),
    ).toBeInTheDocument();
    expect(screen.getByText('$110.00')).toBeInTheDocument();
  });
  it('muestra el aviso aunque la partida con salidas tenga ajuste cero', () => {
    render(
      <DistribucionTabla
        d={{
          ...distribucion,
          partidas: [
            { ...distribucion.partidas[0], partidaId: 'sin-salidas', tieneSalidas: false },
            { ...distribucion.partidas[0], diferenciaMxn: 0, gastoImportacionMxn: 0 },
          ],
        }}
      />,
    );
    expect(screen.getByRole('note')).toBeInTheDocument();
  });
});
