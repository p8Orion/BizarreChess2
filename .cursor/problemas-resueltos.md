# Problemas resueltos

Registro vivo de incidentes y soluciones de **este proyecto**.
**Orden:** lo más reciente arriba. El agente debe consultar este archivo antes de depurar.

---

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
