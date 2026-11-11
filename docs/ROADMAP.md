# Hoja de ruta

## v0.1 - Primera implementacion (actual)
- [x] Lector de repeticiones desde código fuente auditable
- [x] Clasificacion de cofres y atribucion al jugador de la repeticion
- [x] Deteccion de archivos terminados y reintentos
- [x] Persistencia SQLite sin duplicar sesiones
- [x] Panel HTML y exportacion CSV
- [x] Tests unitarios internos y accion de CI (aun no ejecutada)
- [ ] Build comprobado en Windows
- [ ] Precision comprobada con replays reales de la version actual

## v0.2 - Robustez
- [ ] Aumentar cobertura de eventos de replays reales y analizar errores de `errors.log`
- [ ] Tests de regresion anonimizados con al menos dos versiones de Fortnite
- [ ] Ajustar deduplicacion de cofres dinamicos y cofres que reaparecen
- [ ] Historial separado por EpicId/perfil
- [ ] Opciones de reanalizar partidas si cambia el parser

## v0.3 - Experiencia de usuario
- [ ] Aplicacion Windows con bandeja del sistema y ajustes visuales
- [ ] Overlay seguro (ventana propia, sin inyeccion en el juego)
- [ ] Gráficas de tendencias y filtros por periodo
- [ ] Instalador y actualización opcional con firma/verificación
