# Hoja de ruta

## Estado actual — v0.3.4

- [x] Lectura de repeticiones de Fortnite sin acceder al proceso del juego
- [x] Clasificación de cofres y atribución al propietario de la repetición
- [x] Registro de jugadores del mismo equipo cuando el replay permite identificarlos
- [x] Detección de repeticiones terminadas y monitorización cada 6 segundos
- [x] Estadísticas de Cero construcción BR (solo Solo, Duo, Trio y Squad)
- [x] SQLite, deduplicación, migraciones, historial y exportación CSV
- [x] Panel HTML con gráficas, filtros, posición final y paginación
- [x] Fecha de fin y duración del replay; recuperación desde replays históricos disponibles
- [x] Paginación de 10 a 100 partidas en incrementos de 10
- [x] Totales observados de party por periodo y filtros, y Fecha unificada con inicio/fin
- [x] Compilación y pruebas automáticas en Windows con GitHub Actions
- [x] Distribución autónoma en EXE y variante portable
- [x] Publicación de versiones mediante GitHub Releases

## Prioridades para mejorar la fiabilidad

- [ ] Validar los recuentos con repeticiones reales de varias versiones de Fortnite
- [ ] Preparar fixtures de regresión anonimizadas para el parser y la atribución de compañeros
- [ ] Revisar casos de cofres dinámicos, reaparecidos y eventos que no pueden atribuirse
- [ ] Comprobar la compatibilidad del lector tras actualizaciones del juego
- [ ] Reforzar la distribución: firma digital de Windows y verificaciones de integridad

## Mejoras futuras

- [ ] Estadísticas de victorias, Top 10 y porcentajes basadas en la posición final
- [ ] Separación opcional de perfiles cuando varios jugadores usen el mismo ordenador
- [ ] Opción de reanalizar partidas cuando cambie el lector
- [ ] Interfaz de bandeja del sistema, ajustes visuales y cierre cómodo del monitor
- [ ] Instalador o actualizaciones opcionales
- [ ] Evaluar un overlay propio seguro, sin inyección en el juego
