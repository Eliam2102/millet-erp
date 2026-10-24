import { describe, expect, it } from 'vitest';
import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { MedidasHojaFila } from '@/features/almacen/components/MedidasHojaFila';
import {
  construirFilas,
  totalesM2,
  type MedidaHoja,
} from '@/features/almacen/components/nueva-recepcion-helpers';
import { FilaRecepcionSchema } from '@/features/almacen/schemas/recepcion';
import type { LineaRecibible, VarianteVidrio } from '@/features/almacen/api/types';

/** F1-MP-01 S3: renglones «medida + hojas» con m² por renglón. */

const A: VarianteVidrio = { varianteId: 'a', claveVariante: '2440x3660', anchoMm: 2440, altoMm: 3660, areaHojaM2: 8.9304 };
const B: VarianteVidrio = { varianteId: 'b', claveVariante: '2140x3300', anchoMm: 2140, altoMm: 3300, areaHojaM2: 7.062 };

function Host({ inicial }: { inicial: MedidaHoja[] }) {
  const [medidas, setMedidas] = useState(inicial);
  return (
    <MedidasHojaFila idBase="f" posicion={1} variantes={[A, B]} medidas={medidas} disabled={false} onChange={setMedidas} />
  );
}

describe('totalesM2', () => {
  it('suma hojas × m² por hoja por renglón (20 × 2440×3660 + 15 × 2140×3300)', () => {
    const t = totalesM2(
      [
        { varianteId: 'a', hojas: 20, danadas: 2 },
        { varianteId: 'b', hojas: 15, danadas: 0 },
      ],
      [A, B],
    );
    expect(t).toEqual({ cantidad: 284.538, danada: 17.8608 });
  });
});

describe('<MedidasHojaFila>', () => {
  it('muestra los m² de cada renglón y el total, y permite agregar y quitar medidas', () => {
    render(<Host inicial={[{ varianteId: 'a', hojas: 20, danadas: 0 }]} />);
    expect(screen.getByText('178.6080 m²')).toBeInTheDocument();
    expect(screen.getByText('Total 178.6080 m²')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /agregar medida/i }));
    expect(screen.getAllByLabelText(/hojas recibidas/i)).toHaveLength(2);
    fireEvent.change(screen.getAllByLabelText(/hojas recibidas/i)[1]!, { target: { value: '15' } });
    expect(screen.getByRole('button', { name: /agregar medida/i })).toBeDisabled(); // ya están las 2 medidas

    fireEvent.click(screen.getByRole('button', { name: /quitar renglón 2/i }));
    expect(screen.getAllByLabelText(/hojas recibidas/i)).toHaveLength(1);
  });

  it('muestra el error accesible', () => {
    render(
      <MedidasHojaFila idBase="f" posicion={1} variantes={[A]} medidas={[{ varianteId: null, hojas: 0, danadas: 0 }]}
        disabled={false} error="Elige la medida de la hoja." onChange={() => {}} />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Elige la medida de la hoja.');
  });
});

describe('construirFilas + esquema', () => {
  const recibible: LineaRecibible = {
    lineaOcId: '00000000-0000-4000-8000-000000000001', articuloId: '00000000-0000-4000-8000-000000000002', unidadMedida: 'M2',
    cantidadSolicitada: 1000, cantidadRecibida: 0, pendiente: 1000, maximoRecibible: 1000, variantes: [A, B],
  };
  const fila = () =>
    construirFilas(
      [{ id: recibible.lineaOcId, articuloId: recibible.articuloId, posicion: 1, unidadMedida: 'M2', cantidad: 1000, cantidadRecibida: 0 } as never],
      [recibible],
    )[0]!;

  it('una línea de vidrio arranca con un renglón de medida vacío', () => {
    expect(fila().medidas).toEqual([{ varianteId: null, hojas: 0, danadas: 0 }]);
    expect(fila().variantes).toHaveLength(2);
  });

  it('sin medida elegida el esquema pide «Elige la medida de la hoja.»', () => {
    const r = FilaRecepcionSchema.safeParse({ ...fila(), incluida: true, ubicacionId: '00000000-0000-4000-8000-000000000003' });
    expect(r.error?.issues.map((i) => i.message)).toContain('Elige la medida de la hoja.');
  });

  it('acepta hojas enteras con medida y rechaza repetir la medida', () => {
    const ok = FilaRecepcionSchema.safeParse({
      ...fila(), incluida: true, ubicacionId: '00000000-0000-4000-8000-000000000003', cantidad: 178.608,
      medidas: [{ varianteId: 'a', hojas: 20, danadas: 0 }],
    });
    expect(ok.success).toBe(true);
    const repetida = FilaRecepcionSchema.safeParse({
      ...fila(), incluida: true, ubicacionId: '00000000-0000-4000-8000-000000000003', cantidad: 10,
      medidas: [{ varianteId: 'a', hojas: 1, danadas: 0 }, { varianteId: 'a', hojas: 1, danadas: 0 }],
    });
    expect(repetida.error?.issues.map((i) => i.message)).toContain('Una medida no puede repetirse en la misma partida.');
  });
});
