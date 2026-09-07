# Problemas resueltos

Registro vivo de incidentes y soluciones de **este proyecto**.
**Orden:** lo más reciente arriba. El agente debe consultar este archivo antes de depurar.

---

## 2026-09-06 — Captura de bomber explotaba antes de aterrizar

**Síntoma:** Quien capturaba al que llevaba bomba moría sin terminar de entrar a la casilla.
**Contexto:** Melee + `OnBreak` al morir.
**Causa:** Se detonaba el ítem al matar a la víctima, antes de mover al atacante.
**Solución:** Primero matar y mover (o disparar a distancia), después `breakItem`. En el render, `finishTravel` siempre corre antes de la explosión.
**Prevención:** La explosión por captura es post-move, no pre-move.

---

## 2026-09-06 — Explosión sin naranja bajo la montaña

**Síntoma:** Al volar una roca, el destello naranja no se veía en esa casilla.
**Contexto:** `playExplosion` pinta emissive en `this.tiles`.
**Causa:** Las casillas Impassable no se registraban en `tiles` (solo la roca). El emissive de la tapa queda tapado por el mesh de la montaña.
**Solución:** Registrar el piso de la montaña en `tiles` y dibujar un pad naranja aditivo a ras del tablero en cada casilla del blast.
**Prevención:** Efectos de casilla no pueden depender solo de `tiles` pickeables.

---

## 2026-09-06 — Bomba al morir no explotaba

**Síntoma:** Matar un Bomber (o a quien lleve pólvora) dropeaba el ítem; las montañas no se rompían.
**Contexto:** `OnBreak` solo corría al Ignite o si otra explosión pisaba el ítem en el piso.
**Causa:** `dropOnDeath` soltaba el barril/bomba en vez de romperlo.
**Solución:** Si el ítem tiene `OnBreak`, al morir se rompe (explota). El blast parte montañas. `playOutcome` usa `setState` para reconstruir el tablero.
**Prevención:** Muerte + ítem con `OnBreak` = `triggerBreak`, no drop.

---

## 2026-09-06 — Ignite no rompía montañas

**Síntoma:** La explosión mataba piezas e ítems pero las rocas/paredes seguían.
**Contexto:** Acción PowderBarrel / Bomb en Three.js.
**Causa:** El blast no cambiaba `Impassable` a `Normal`. Además `playAction` asignaba `this.state = next` y `setState` no reconstruía el tablero.
**Solución:** En cada casilla del blast, si es montaña → `Normal`. Terminar la animación con `setState(next)`.
**Prevención:** Efectos que cambian terreno tienen que mutar `currentType` y salir por `setState`.

---

## 2026-09-06 — Transmutar no mostraba la piedra

**Síntoma:** El scroll de transmutación hacía las chispas pero no aparecía la roca.
**Contexto:** Efecto visual de uso de pergamino en Three.js.
**Causa:** `playScrollUse` asignaba `this.state = next` y solo sincronizaba piezas/ítems. Después `setState(next)` comparaba next contra next y no reconstruía el tablero.
**Solución:** Terminar el efecto con `setState(next)` mientras `this.state` sigue siendo prev, para que `terrainChanged` dispare `rebuildBoard`.
**Prevención:** Después de un efecto, no pisar `this.state` a mano. Usar `setState` si el terreno o el tablero pueden haber cambiado.

---

## 2026-09-06 — Muerte: otra vez hacia adelante

**Síntoma:** Tras el roll sobre el tablero, las piezas volvieron a caer de cara al rival.
**Contexto:** `playDeath` en Three.js.
**Causa:** `right = cross(up, fallDir)` invierte el eje respecto de `cross(up, facing)`. El tip −deg alrededor de ese eje las tira hacia adelante.
**Solución:** El plano de caída es la espalda ±45° (`facing` con yaw, no `fallDir`). Tip −deg alrededor de `cross(up, facing)`.
**Prevención:** No construir `right` desde `fallDir`. En Three.js el tip es negativo sobre `cross(up, facing)`.

---

## 2026-09-06 — Muerte: roll de tronco, no de tablero

**Síntoma:** Tras caer, la pieza rodaba sobre su eje largo (como un tronco).
**Contexto:** Animación de muerte en Three.js.
**Causa:** El roll usaba el eje `fallDir` y la dirección de caída era 360° al azar.
**Solución:** Caer hacia atrás ±45°. El giro es yaw sobre Y con la base fija, interpolando el ángulo (no slerp).
**Prevención:** El roll de muerte es un barrido sobre el tablero, apoyada de costado en la base.

---

## 2026-09-06 — HUD hover quedaba pegado

**Síntoma:** El segundo renglón del HUD seguía mostrando la última pieza al sacar el mouse.
**Contexto:** `#hud-hover-slot` se oculta con el atributo `hidden`.
**Causa:** `.hud-unit { display: flex }` pisa el `display: none` del atributo `hidden` (estilo de autor gana al UA).
**Solución:** `#hud-hover-slot[hidden] { display: none; }` y vaciar nombre/texto al limpiar.
**Prevención:** Si una clase fija `display`, el `[hidden]` de ese nodo necesita una regla propia.

---

## 2026-09-06 — Ítem persistido sin modelo 3D

**Síntoma:** Una pieza vuelve a la partida con un ítem guardado (p. ej. ballesta) pero se ve un cubo / no carga el GLB.
**Contexto:** `persist` guarda el ítem en la pieza del army; al spawnear se rehidrata.
**Causa:** `toPersistedItem` / `rehydrateItem` no copiaban `model`. El held visual solo carga GLB si `item.model` existe.
**Solución:** Persistir `model` y, al rehidratar, completar por `kind` (`Crossbow` → `items/crossbow`).
**Prevención:** Todo ítem con GLB tiene que rehidratar `model` desde el kind, no solo desde el JSON.

---

## 2026-09-06 — No se podía mover: crash al seleccionar

**Síntoma:** Click en una pieza no selecciona / no se puede mover.
**Contexto:** Casillas multi-material (tapa + lados oscuros).
**Causa:** `selectedNode` hacía `mesh.material.emissive` asumiendo un solo material. Con un array, tira y corta el click.
**Solución:** Usar el material de la tapa (`materials[2]`). El pick va por el plano y=0, no por las caras laterales hondas.
**Prevención:** Si `Mesh.material` es array, no tratarlo como `MeshStandardMaterial`.

---

## 2026-09-06 — Muerte: caían hacia adelante

**Síntoma:** Al morir, las piezas se tiran y ruedan hacia adelante.
**Contexto:** Port de `DeathAnimationCoroutine` en Three.js.
**Causa:** Se usó “lejos del golpe” en vez de `-facing`, y el ángulo de tip de Unity (+86°) en un motor diestro se ve al revés.
**Solución:** Caer siempre hacia la espalda (`-forward`). Tip −86° en Three.js. El roll sigue sobre ese eje.
**Prevención:** La dirección de la muerte es la espalda de la pieza, no la del ataque.

---

## 2026-09-06 — Terreno en el grafo, no como edges faltantes

**Síntoma:** Pozos y paredes se trataban como si no hubiera casilla; el leaper exigía aterrizar en el codo del L.
**Contexto:** Movimiento por ejes en `chess-threejs`.
**Causa:** La topología (quién está conectado) se mezcló con la pasabilidad. Destroyed = hueco; Abyss/Impassable son nodos con edges.
**Solución:** El grafo incluye pozos y paredes. El walk sigue edges de un eje. Leapers y ranged vuelan Abyss; Impassable corta el vuelo. Solo el destino final tiene que ser pasable.
**Prevención:** No borrar edges por tipo de terreno. Huecos (Destroyed) sí quedan fuera del grafo.

---

## 2026-09-06 — Orbes sobre casillas impasables

**Síntoma:** No se puede caminar a las casillas de los Force Field Generators.
**Contexto:** Port `chess-threejs` con terreno aleatorio.
**Causa:** d4/e5 (nodos 27/36) están en la zona eligible de abyss/paredes. El click en el orbe además comía el click y no movía.
**Solución:** Reservar esas casillas al generar terreno. Click en orbe con pieza elegida = mover ahí.
**Prevención:** No poner ítems ni spawn sobre tiles que el generador de terreno puede romper.

## 2026-09-06 — Three.js: muerte plana, tablero sin obstáculos, orbes inertes

**Síntoma:** En Unity la pieza cae hacia atrás y rueda; el tablero tiene huecos y paredes; los orbes cian se recogen. En Three.js la muerte era un tip genérico, el tablero era 8×8 liso y los ítems no se usaban.
**Contexto:** Port `chess-threejs`.
**Causa:** No se portó `DeathAnimationCoroutine` (dirección del golpe + roll), ni `CreateBoardWithAbyss` (5% abyss + 5% walls), y el pickup era solo un click en una esfera chica en la esquina de la casilla.
**Solución:** Caída en dos fases (tip 86° + roll 28–78°) lejos del atacante, usando `fromNode` y matando en el 30% del melee antes de ocupar la casilla. Tablero con huecos/paredes espejados; ítems solo en casillas pasables. Orbe centrado + botón *Pick up Force Field*.
**Prevención:** Al portar presentación, revisar BoardFactory, UnitRenderer death e items de `ChessFactory`, no solo movimiento.

## 2026-09-06 — Lancer invisible o sin ataque en Three.js

**Síntoma:** No se ven lancers; no corre el clip de ataque.
**Contexto:** Port `chess-threejs`. El GLB tiene `SkinnedMesh` + `Lancer_Move` / `Lancer_Attack`.
**Causa:** `Object3D.clone` no copia bien el skeleton. La copia en `public/models` estaba desactualizada (sin `_Attack`).
**Solución:** Clonar con `SkeletonUtils.clone`. Recopiar GLBs desde `Assets/Resources/Pieces/Models`. Escalar el lancer con la escala del peón, como en Unity.
**Prevención:** Tras cambiar un GLB en Unity, volver a copiarlo a `chess-threejs/public/models`. No usar `clone()` crudo en meshes con huesos.

## 2026-09-06 — Three.js: GLB cargan pero no se dibujan

**Síntoma:** El tablero y los forcefields se ven; las piezas GLB están en la escena (bounds correctos) pero `renderer.info.render.triangles` no las cuenta.
**Contexto:** Port `chess-threejs`, tint de materiales al color del jugador.
**Causa:** `mesh.material = mats.map(...)` siempre asigna un **array**. Three trata eso como multi-material; sin `geometry.groups` no dibuja el mesh.
**Solución:** Si hay un solo material, asignar `next[0]`, no el array.
**Prevención:** No wrappear en array un `Mesh.material` de un solo slot.

## 2026-09-06 — WebGL/editor: tablero y piezas invisibles, un magenta

**Síntoma:** En web (y luego en Play Mode) no se ve el tablero ni las piezas. Queda un magenta (error shader) y la UI sigue andando.
**Contexto:** `PC_Renderer` estaba en **Deferred**. Los materiales runtime usaban `Shader.Find` de URP Lit. Los GLB traen `glTF/PbrMetallicRoughness` (Built-in). Forcefield incluía `Core.hlsl`.
**Causa:** WebGL no banka bien Deferred (MRT). Un shader solo con `UniversalForward` no pinta en Deferred (`FallBack Off` = invisible, no magenta). Los GLB Built-in no tienen pass URP. Forcefield roto = el único magenta visible.
**Solución:** Restaurar `PC_Renderer` Deferred + SSAO (como cuando Windows se veía bien). `RuntimeShaders` = `Shader.Find(URP Lit)` en editor/Windows; en player WebGL clona `Resources/Mat` y convierte glTF Built-in. El build WebGL usa `Mobile_RPAsset` (Forward) solo al buildear.
**Prevención:** No cambiar el renderer de PC para “arreglar” Web. No meter shaders custom en el camino de materiales del editor.

## 2026-09-06 — GLB sigue flotando después del fit inicial

**Síntoma:** Las piezas importadas siguen en el aire.
**Contexto:** El fit corría al crear el objeto, antes de parent, posición de casilla y escala 0.8. Algunos GLB traen un mesh/locator chico en el origen.
**Causa:** Ese locator hace que `min.y` parezca 0 y el modelo real queda arriba. Después la escala 0.8 ya no se reasienta.
**Solución:** Calcular bounds solo con meshes grandes (≥8% del más grande) e invocar `SnapVisualToGround` al final de `Initialize`.
**Prevención:** Asentar el visual después del transform final, no solo en el factory.

## 2026-09-06 — GLB: peones enormes y piezas flotando

**Síntoma:** Peones importados mucho más grandes que el resto. Piezas nuevas "volando" sobre el tablero.
**Contexto:** `ImportedMeshGenerator` escalaba cada GLB a `PieceHeight` (peón 1.5, rey 2.0) y apoyaba con `Renderer.bounds`.
**Causa:** Un set de Blender ya viene proporcionado; forzar altura por pieza infla el peón. `Renderer.bounds` del GLB a veces no coincide con la base del mesh, así que el offset en Y queda mal.
**Solución:** Alturas relativas de ajedrez (peón ~50% del rey), tope de footprint 0.78, y asiento usando las 8 esquinas de cada `MeshFilter`.
**Prevención:** No reescalar cada modelo a la misma altura-objetivo. No usar `Renderer.bounds` crudo para pivot/base de GLB.

## 2026-09-06 — Play Mode bloqueado: else huérfano y TypeLoadException de glTFast

**Síntoma:** Unity no entra a Play. CS1525 `Invalid expression term 'else'` en `UnitRenderer.cs` ~383. También `TypeLoadException: Could not load type 'GLTFast.AnimationMethod' from assembly 'glTFast'`.
**Contexto:** Al pasar UpdateVisuals a `ForEachPieceRenderer` quedó un `else if` sin `if`.
**Causa:** El `else` quedó colgando del lambda. El TypeLoadException de glTFast suele ser efecto de un domain reload incompleto por los errores de compile.
**Solución:** Restaurar el fallback Unicode con `if (!hasPieceMesh && _unicodeText != null)`. Recompilar. Si AnimationMethod sigue fallando: reimportar el paquete o bajar `com.unity.cloud.gltfast` a 6.19.0.
**Prevención:** No dejar `else` atado a un `ForEach`/lambda. Después de un error de compile, no tratar el TypeLoadException de packages como causa raíz hasta que el proyecto compile.
