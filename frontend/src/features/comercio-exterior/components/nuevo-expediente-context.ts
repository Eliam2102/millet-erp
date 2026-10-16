import { createContext, useContext } from 'react';
export const NuevoExpedienteContext = createContext<{ abrir: () => void } | null>(null);
export function useNuevoExpediente() {
  const context = useContext(NuevoExpedienteContext);
  if (!context) throw new Error('Se requiere NuevoExpedienteProvider dentro del shell.');
  return context;
}
