# Data model
PreferenciaTabla: clave estable de vista/tabla; cantidad auto o entero en10/20/25/50/100. Persistir sólo cantidad, nunca filtros/registros. Valor desconocido/storage bloqueado: auto seguro.
PaginaTabla: offset entero>=0, limit entero>=1 acotado por contrato, total>=0. Cambio de filtro/tamaño reinicia offset; reducción de total limita página final. Consulta parcial: distinguir registros cargados y total remoto.
