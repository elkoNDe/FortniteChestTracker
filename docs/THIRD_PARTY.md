# Dependencias y atribuciones

## LED-N/Fortnite-ChestCounter

- Proyecto: https://github.com/LED-N/Fortnite-ChestCounter
- Autor del código: © 2026 LED-N
- Licencia: MIT.
- Commit fijado en `scripts/bootstrap.ps1`: `1f387987737cfd606611d5b8d294d5db04af9eab`.
- Solo se toma el parser como referencia compilable; el código de nuestra app y nuestro panel es independiente.
- `ReplayAnalyzer.cs` y `ReplayProbe.cs` adaptan técnicas de seguimiento de eventos, deduplicación y cabeceras descritas/implementadas en ese proyecto.
- La copia fuente del tercero se descarga en `third_party/ChestCounter` y mantiene su `LICENSE` original.

## Shiqan/FortniteReplayDecompressor

- Proyecto: https://github.com/Shiqan/FortniteReplayDecompressor
- Licencia: MIT.
- Fuente del lector de replays posteriormente adaptado por LED-N. Los cambios sobre el parser están documentados en `lib/PATCHES.md` de LED-N.

## Microsoft.Data.Sqlite

- Proyecto: https://www.nuget.org/packages/Microsoft.Data.Sqlite/
- Paquete NuGet de Microsoft para el acceso SQLite. Se restaura de la fuente NuGet configurada por el SDK durante `dotnet restore`.

Las licencias de todas las dependencias y sus avisos de copyright deben conservarse al redistribuir código o binarios.
