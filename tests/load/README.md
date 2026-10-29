# Pruebas de carga con k6

El script `kitchen-chaos.js` simula salas de dos jugadores que hablan con el `GameHub` por SignalR.
En cada sala: crean y se unen, arrancan el nivel 1, **los dos intentan tomar la misma estación a la vez**
(tiene que ganar exactamente uno), se mueven unos segundos y se desconectan.

## Cómo correrlo

1. Levanta el juego (`dotnet run` en `src/KitchenChaos.Server`, puerto 5129).
2. Levanta Prometheus y Grafana (`docker compose up -d` en `observability/`) y abre el tablero en http://localhost:3000.
3. Desde la raíz del repo, elige un escenario con `SCENARIO`:

```bash
docker run --rm -v "$PWD/tests/load:/scripts" -e SCENARIO=carga grafana/k6:1.3.0 run /scripts/kitchen-chaos.js
```

En PowerShell cambia `$PWD/tests/load` por `${PWD}\tests\load`.

| `SCENARIO` | Qué hace | Duración |
|---|---|---|
| `humo` | una sala, para comprobar que todo conecta | ~3 s |
| `carga` | 2 salas nuevas por segundo | 1 min |
| `estres` | sube de 2 a 12 salas por segundo buscando dónde se degrada | 2 min |
| `pico` | tráfico bajo, subida brusca a 25 salas/s durante 10 s y vuelta | 70 s |
| `duracion` | 2 salas por segundo constantes (`-e DURACION=30m` para cambiarlo) | 10 min |

Si el juego corre en otra dirección: `-e BASE_URL=http://host.docker.internal:5129`.

## Con el juego en Docker (recomendado para estrés y pico)

Con el juego en tu computador, k6 le llega por la red de Docker Desktop, y ahí empiezan a fallar conexiones nuevas
con carga alta (el servidor no registra errores). Para medir el juego y no la red, córrelo en Docker, en la misma
red que k6:

```bash
cd observability
docker compose -f docker-compose.yml -f docker-compose.game.yml up -d --build
cd ..
docker run --rm --network kitchen-chaos-observability_default -v "$PWD/tests/load:/scripts" -e SCENARIO=estres -e BASE_URL=http://game:8080 grafana/k6:1.3.0 run /scripts/kitchen-chaos.js
```

El juego también queda en http://localhost:5130 y el tablero en http://localhost:3000. Para apagar todo:
`docker compose -f docker-compose.yml -f docker-compose.game.yml down` dentro de `observability/`.

## Qué mide

Umbrales (si alguno falla, k6 termina con error):

- `kc_invocation_ms` p95 < 500 ms: tiempo de respuesta de cada llamada al hub (y por separado `TakeIngredient`).
- `kc_invocation_ok` > 99 %: llamadas sin error.
- `kc_one_winner` = 100 %: en cada toma simultánea ganó exactamente un jugador.
- `kc_session_ok` > 99 %: salas completas sin errores.

k6 mide lo que ve el cliente. Lo que pasa dentro del servidor (conflictos, salas activas, CPU, P95 del hub)
se ve en el tablero de Grafana mientras corre la prueba.
