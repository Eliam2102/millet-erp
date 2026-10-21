import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DistribucionTabla } from './FinanzasPanel';
describe('Vista previa de costo puesto', () => {
  it('muestra el gasto tardío separado y la base antes de confirmar', () => {
    render(
      <DistribucionTabla
        d={{
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
              inventarioMxn: 4000,
              costoVentasMxn: 6000,
              costoUnitarioAntes: 100,
              costoUnitarioDespues: 110,
            },
          ],
        }}
      />,
    );
    expect(screen.getByText('Vidrio importado')).toBeInTheDocument();
    expect(screen.getByText('Valor en aduana: 200,000')).toBeInTheDocument();
    expect(screen.getByText('$4,000.00')).toBeInTheDocument();
    expect(screen.getByText('$6,000.00')).toBeInTheDocument();
    expect(screen.getByText('$110.00')).toBeInTheDocument();
  });
});
