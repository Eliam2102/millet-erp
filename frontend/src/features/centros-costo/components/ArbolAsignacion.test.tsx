import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { ArbolAsignacion } from './ArbolAsignacion';
import {
  NivelAlcance,
  TriEstado,
  type ArbolAsignacionResponse,
} from '@/features/centros-costo/api/types';

/**
 * Tests del componente del árbol de asignación (FE-PR3). Cubren el grueso
 * del DoD sin el UsuarioSelector: el checkbox refleja EXACTO el tri-estado
 * del DTO (el UI no re-deriva — le damos un padre `Parcial` con hijos que,
 * re-derivados, se verían distinto, y debe mostrar lo del DTO), la
 * convención del clic (parcial→asignar=true, todo→false) con el
 * NivelAlcance/nodoId/grupoId correcto por nivel, y el disabled.
 */

// Árbol: Dim1 "101 CONKAL" (Parcial) → GrupoDim2 "OPERACIONES" (Parcial) →
// Dim2 "20PDMC CORTE" (Parcial) → GrupoDim3 "LINEA" (Parcial) → 2 Dim3
// (una asignada, otra no). El Dim1 va Parcial aunque abajo haya de todo.
const ARBOL: ArbolAsignacionResponse = {
  usuarioId: 'u-1',
  esAlcanceTotal: false,
  resumen: {
    dim1Vivas: 1, dim1Completas: 0, dim2Vivas: 1, dim2Completas: 0,
    dim3Vivas: 2, dim3Asignadas: 1,
  },
  dim1s: [
    {
      id: 'd1', clave: '101', nombre: 'CONKAL', estado: TriEstado.Parcial,
      dim3Vivas: 2, dim3Asignadas: 1,
      grupos: [
        {
          id: 'g2', nombre: 'OPERACIONES', estado: TriEstado.Parcial,
          dim3Vivas: 2, dim3Asignadas: 1,
          dim2s: [
            {
              id: 'd2', clave: '20PDMC', nombre: 'CORTE', estado: TriEstado.Parcial,
              dim3Vivas: 2, dim3Asignadas: 1,
              grupos: [
                {
                  id: 'g3', nombre: 'LINEA', estado: TriEstado.Parcial,
                  dim3Vivas: 2, dim3Asignadas: 1,
                  dim3s: [
                    { id: 'm1', clave: 'MCLC101', nombre: 'GANTRY', asignada: true },
                    { id: 'm2', clave: 'MCLC102', nombre: 'MESA', asignada: false },
                  ],
                },
              ],
            },
          ],
        },
      ],
    },
  ],
};

afterEach(cleanup);

describe('<ArbolAsignacion>', () => {
  it('el checkbox del Dim1 refleja el tri-estado del DTO (indeterminate para Parcial)', () => {
    render(<ArbolAsignacion arbol={ARBOL} onMarcar={() => {}} />);
    const chk = screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' });
    // Radix expone el estado en aria-checked: 'mixed' = indeterminate.
    expect(chk).toHaveAttribute('aria-checked', 'mixed');
  });

  it('clic en un padre PARCIAL marca asignar=true (convención: parcial→completa) con el nivel correcto', () => {
    const onMarcar = vi.fn();
    render(<ArbolAsignacion arbol={ARBOL} onMarcar={onMarcar} />);

    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' }),
    );
    expect(onMarcar).toHaveBeenCalledWith({
      nivel: NivelAlcance.Dim1,
      nodoId: 'd1',
      grupoId: null,
      asignar: true,
    });
  });

  it('un grupo marca con nodoId=padre y grupoId=grupo (acotado al padre)', () => {
    const onMarcar = vi.fn();
    render(<ArbolAsignacion arbol={ARBOL} onMarcar={onMarcar} />);

    // Expandir Dim1 para ver el grupo.
    fireEvent.click(screen.getByRole('button', { name: 'Expandir 101 · CONKAL' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Alcance de OPERACIONES' }));

    expect(onMarcar).toHaveBeenCalledWith({
      nivel: NivelAlcance.GrupoDim2BajoDim1,
      nodoId: 'd1', // el Dim1 padre
      grupoId: 'g2', // el grupo
      asignar: true,
    });
  });

  it('una hoja ASIGNADA (checked) marca asignar=false al clic', () => {
    const onMarcar = vi.fn();
    render(<ArbolAsignacion arbol={ARBOL} onMarcar={onMarcar} />);

    // Expandir hasta las hojas.
    fireEvent.click(screen.getByRole('button', { name: 'Expandir 101 · CONKAL' }));
    fireEvent.click(screen.getByRole('button', { name: 'Expandir OPERACIONES' }));
    fireEvent.click(screen.getByRole('button', { name: 'Expandir 20PDMC · CORTE' }));
    fireEvent.click(screen.getByRole('button', { name: 'Expandir LINEA' }));

    const gantry = screen.getByRole('checkbox', { name: 'Alcance de MCLC101 · GANTRY' });
    expect(gantry).toHaveAttribute('aria-checked', 'true');
    fireEvent.click(gantry);
    expect(onMarcar).toHaveBeenCalledWith({
      nivel: NivelAlcance.Dim3,
      nodoId: 'm1',
      grupoId: null,
      asignar: false, // asignada → clic → desmarca
    });

    const mesa = screen.getByRole('checkbox', { name: 'Alcance de MCLC102 · MESA' });
    expect(mesa).toHaveAttribute('aria-checked', 'false');
  });

  it('disabled deshabilita todos los checkboxes (alcance total / marcado en curso)', () => {
    render(<ArbolAsignacion arbol={ARBOL} onMarcar={() => {}} disabled />);
    expect(
      screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' }),
    ).toBeDisabled();
  });

  it('columna ASIGNADAS: color semántico del tri-estado (05 §7.3)', () => {
    // Tres Dim1 en los tres estados; la columna mapea el `estado` del DTO.
    const tresEstados: ArbolAsignacionResponse = {
      usuarioId: 'u-1', esAlcanceTotal: false,
      resumen: {
        dim1Vivas: 3, dim1Completas: 1, dim2Vivas: 0, dim2Completas: 0,
        dim3Vivas: 40, dim3Asignadas: 26,
      },
      dim1s: [
        { id: 't', clave: 'T', nombre: 'TODA', estado: TriEstado.Todo,
          dim3Vivas: 12, dim3Asignadas: 12, grupos: [] },
        { id: 'p', clave: 'P', nombre: 'PART', estado: TriEstado.Parcial,
          dim3Vivas: 16, dim3Asignadas: 14, grupos: [] },
        { id: 'n', clave: 'N', nombre: 'NADA', estado: TriEstado.Ninguno,
          dim3Vivas: 16, dim3Asignadas: 0, grupos: [] },
      ],
    };
    render(<ArbolAsignacion arbol={tresEstados} onMarcar={() => {}} />);

    // Todas → píldora emerald "Todas · 12".
    const todo = screen.getByText('Todas · 12');
    expect(todo).toHaveClass('bg-emerald-100');
    // Parcial → píldora amber "Parcial · 14 de 16".
    const parcial = screen.getByText('Parcial · 14 de 16');
    expect(parcial).toHaveClass('bg-amber-100');
    // Ninguna → texto gris SIN píldora (ni emerald ni amber).
    const ninguna = screen.getByText('Ninguna · 0 de 16');
    expect(ninguna).not.toHaveClass('bg-emerald-100');
    expect(ninguna).not.toHaveClass('bg-amber-100');
    expect(ninguna).toHaveClass('text-muted-foreground');
  });

  it('axe-core: cero violations', async () => {
    const { container } = render(<ArbolAsignacion arbol={ARBOL} onMarcar={() => {}} />);
    const results: AxeResults = await axe.run(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
  });
});
