# RtvRandomPicks

Plugin de **Rock The Vote** para servidores de Counter-Strike 2, hecho con [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp). Recupera el comportamiento clásico del RTV / MapChooser de SourceMod en CS:GO: los jugadores piden cambiar de mapa, se abre una votación con mapas elegidos al azar y gana el más votado.

## Descripción

- **RTV** — los jugadores escriben `rtv` o `!rtv`. Al llegar al umbral se abre una votación con mapas **al azar** de la lista, más los nominados. Si gana un mapa, el cambio es inmediato al terminar la votación.
- **No cambiar** — la votación de RTV incluye la opción de quedarse en el mapa actual. Si gana, el RTV se reinicia y vuelve a bloquearse durante `RtvDelaySeconds`.
- **Votación de fin de mapa** — arranca sola poco antes de que se agote `mp_timelimit`, con la opción **Extender mapa**. El mapa ganador se aplica al terminar la ronda o al agotarse el tiempo, lo que ocurra antes.
- **Nominaciones** — cada jugador puede nominar un mapa, que entra con prioridad en la siguiente votación.
- **Mapas recientes** — los últimos mapas jugados no salen en la elección al azar.
- **Colección de Workshop** — la lista de mapas se rellena sola a partir de la colección del servidor.
- **Idiomas** — inglés y español, según el idioma del cliente de cada jugador.

Otros detalles:

- Los empates se resuelven al azar.
- Si nadie vota, se mantiene el mapa. En la votación de fin de mapa, al agotarse el tiempo se elige uno al azar.
- La votación termina en cuanto han votado todos los jugadores.
- Si se completa un RTV cuando la votación de fin de mapa ya eligió mapa, se cambia directamente a ese mapa.
- El tiempo del mapa solo corre mientras hay jugadores: un servidor vacío no vota ni cambia de mapa.

## Requisitos

- Servidor dedicado de CS2 con [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) y [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) `>= 1.0.367`
- [.NET 8 SDK](https://dotnet.microsoft.com/download) o superior, solo para compilar

## Tutorial

### 1. Compilar

```
git clone https://github.com/AlexViu/rtv-random-picks.git
cd rtv-random-picks
dotnet build -c Release
```

El resultado queda en `bin/Release/net8.0/`.

### 2. Instalar en el servidor

Todas las rutas son relativas a `game/csgo/addons/counterstrikesharp/`.

1. Crea la carpeta `plugins/RtvRandomPicks/`.
2. Copia dentro `RtvRandomPicks.dll` y la carpeta `lang/` desde `bin/Release/net8.0/`.

```
plugins/
└── RtvRandomPicks/
    ├── RtvRandomPicks.dll
    └── lang/
        ├── en.json
        └── es.json
```

3. Reinicia el servidor o cambia de mapa. En el primer arranque se genera la configuración en `configs/plugins/RtvRandomPicks/RtvRandomPicks.json`.

Si desinstalas otro plugin de RTV, hazlo antes: dos plugins respondiendo a `!rtv` se pisan.

### 3. Definir la lista de mapas

La lista sale de dos fuentes que se combinan. Puedes usar una o las dos.

**Opción A: colección de Workshop (recomendada)**

Si el servidor arranca con `+host_workshop_collection <id>`, no hay que hacer nada: el plugin lee ese valor. Para usar otra colección, ponla en la configuración:

```json
"WorkshopCollectionId": "3736332535"
```

Los mapas se consultan en la API de Steam (no hace falta API key) y se guardan en `configs/plugins/RtvRandomPicks/workshop_cache.json` durante `WorkshopCacheHours` horas. Si añades mapas a la colección y quieres verlos ya, borra ese fichero y cambia de mapa.

**Opción B: `rtv_maps.json`**

Crea `configs/plugins/RtvRandomPicks/rtv_maps.json` (hay una plantilla en la raíz de este repositorio). Sirve para añadir mapas oficiales o mapas de Workshop sueltos:

```json
{
  "de_dust2":  { "ws": false, "display": "Dust 2",    "mapid": "" },
  "mg_mymap":  { "ws": true,  "display": "My WS Map", "mapid": "1234567890" }
}
```

| Campo | Descripción |
|---|---|
| clave | Nombre del mapa tal como lo usa el servidor (`de_dust2`, `mg_mymap`...) |
| `ws` | `true` si es un mapa de Workshop, `false` si es oficial |
| `display` | Nombre que se muestra en los menús y en el chat |
| `mapid` | ID del mapa en Workshop (solo si `ws` es `true`) |

Si un mapa está en los dos sitios, se usa la entrada de `rtv_maps.json`.

### 4. Configurar el tiempo de mapa

La votación automática de fin de mapa depende de `mp_timelimit`. Ponlo en tu `server.cfg` o en el cfg del modo de juego:

```
mp_timelimit 20
```

Con `mp_timelimit 0` no hay votación automática y el mapa solo cambia por RTV.

### 5. Ajustar la configuración

Edita `configs/plugins/RtvRandomPicks/RtvRandomPicks.json` y cambia de mapa para aplicar los cambios.

| Clave | Por defecto | Descripción |
|---|---|---|
| `RtvThreshold` | `0.6` | Fracción de jugadores necesaria para el RTV (0.6 = 60%) |
| `RtvDelaySeconds` | `90` | Segundos tras empezar el mapa, o tras una votación sin cambio, hasta permitir RTV |
| `VoteSeconds` | `30` | Duración de la votación |
| `MapsInVote` | `5` | Mapas en la votación |
| `ExcludeRecentMaps` | `3` | Mapas anteriores que no entran en la elección al azar |
| `DontChangeOption` | `true` | Opción "No cambiar" en el RTV |
| `EndVoteSecondsBeforeEnd` | `120` | Segundos antes del fin de `mp_timelimit` para la votación automática (0 = desactivada) |
| `ExtendMinutes` | `10` | Minutos que añade "Extender mapa" (0 = sin opción) |
| `MaxExtends` | `2` | Extensiones máximas por mapa |
| `MapsFile` | `rtv_maps.json` | Lista estática de mapas |
| `WorkshopCollectionId` | `""` | Colección de Workshop (vacío = `host_workshop_collection`) |
| `WorkshopCacheHours` | `24` | Horas de validez de la caché de la colección |

### 6. Usarlo en el juego

**Comandos de jugador**

| Comando | Descripción |
|---|---|
| `!rtv` o `rtv` | Votar para cambiar de mapa |
| `!nominate` o `nominate` | Abrir el menú de nominaciones |
| `!nominate <texto>` | Filtrar por nombre; si solo coincide un mapa, lo nomina directamente |
| `!nomlist` | Ver las nominaciones actuales |
| `!timeleft` o `timeleft` | Tiempo restante y siguiente mapa, si ya se votó |

**Comandos de administrador**

| Comando | Permiso | Descripción |
|---|---|---|
| `css_forcertv` (`!forcertv`) | `@css/changemap` | Abrir una votación de mapa ya |

**Menú de votación**

Aparece en el centro de la pantalla:

- **W / S** — subir y bajar
- **E** — elegir
- **R** — cerrar

Junto a cada opción se ve el número de votos en tiempo real. El menú de votación no congela al jugador; el de `!nominate` sí, para poder recorrer la lista con W/S sin moverse.

**Ejemplo de una partida**

1. Empieza el mapa. Durante los primeros 90 segundos el RTV está bloqueado.
2. Un jugador nomina con `!nominate lego`.
3. Varios jugadores escriben `rtv`. Al llegar al 60% se abre la votación con el mapa nominado, cuatro al azar y "No cambiar".
4. Gana un mapa: el servidor cambia al terminar la votación.
5. Si nadie hace RTV, a falta de 2 minutos para el `mp_timelimit` se abre la votación de fin de mapa con la opción "Extender mapa 10 min". El ganador se aplica al terminar la ronda.

## Créditos

El menú WASD y la lectura de colecciones de Workshop parten de [SimpleRTV-CS2](https://github.com/josesilvaruiz/SimpleRTV-CS2).

## Licencia

[MIT](LICENSE)
