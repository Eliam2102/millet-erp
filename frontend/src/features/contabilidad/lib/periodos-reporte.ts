import { useMemo } from 'react';
import { useEjercicios } from '../api/periodos';

export interface PeriodoElegido {
  anio: number;
  numero: number;
}

/** Periodos de los ejercicios existentes (más reciente primero) y el de por omisión: el mes en curso o el abierto más reciente. */
export function usePeriodosContables() {
  const ejercicios = useEjercicios({ retry: 0 });
  const periodos = useMemo(
    () =>
      (ejercicios.data ?? [])
        .flatMap((e) => e.periodos)
        .sort((a, b) => b.anio - a.anio || b.numero - a.numero),
    [ejercicios.data],
  );
  const hoy = new Date();
  const porDefecto =
    periodos.find((p) => p.anio === hoy.getFullYear() && p.numero === hoy.getMonth() + 1) ??
    periodos.find((p) => p.estado === 'Abierto') ??
    periodos[0];
  return {
    periodos,
    porDefecto: porDefecto ? { anio: porDefecto.anio, numero: porDefecto.numero } : null,
    isLoading: ejercicios.isLoading,
  };
}

export const clavePeriodo = (p: PeriodoElegido | null) => (p ? `${p.anio}-${p.numero}` : '');

export function leerPeriodo(valor: string): PeriodoElegido | null {
  const [anio, numero] = valor.split('-').map(Number);
  return anio && numero ? { anio, numero } : null;
}

