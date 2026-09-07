import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { GLTFLoader, type GLTF } from "three/examples/jsm/loaders/GLTFLoader.js";
import { clone as cloneSkinned } from "three/examples/jsm/utils/SkeletonUtils.js";
import { Board, definitionFromPublic } from "../core/board";
import { DEFAULT_STYLE_P1, DEFAULT_STYLE_P2, normalizeStyles, type PlayerStyle } from "../core/colors";
import { createPatternMap } from "./patterns";
import type { ActionExecution, MoveExecution } from "../core/gameState";
import { hexCellExists, hexWorldZ, isStructuralGap } from "../core/hex";
import { PIECES } from "../core/pieces";
import { BoardLayout, ItemState, MoveTargets, NodeType, PublicState, UnitState } from "../core/types";
import {
  bakeCheckerGround,
  createGroundGeometry,
  fogRandFromSeed,
  fogRange,
  fogSeedFromNodes,
  subtleFogTint,
} from "./background";
import {
  createCircleMap,
  createCrosshairMap,
  createForcefieldMaterial,
  createMarkerMaterial,
  createRingMap,
  createWoodFallback,
} from "./shaders";

const TILE = 1;
const THICKNESS = TILE * 8;
const SLAB = TILE + 0.003;
const ROCK_NAMES = ["terrain/rock_01", "terrain/rock_02", "terrain/rock_03"] as const;
const ROCK_FOOTPRINT = 0.88;
const ITEM_FOOTPRINT = 0.69;
const ITEM_PAD_RADIUS = 0.38;
const PICK_PAD_READY = 0xffc94a;
const ROCK_MAX_HEIGHT = 1.35;
const MAX_FOOTPRINT = 0.78;
const KING_HEIGHT = 1.9;
const MOVE_SPEED = 3.4;
const LIGHT_WOOD = new THREE.Color(0xf2d9b3);
const MID_WOOD = new THREE.Color(0xb07840);
const DARK_WOOD = new THREE.Color(0x2a1810);
const SIDE_WOOD = new THREE.Color(0x6e4528);
const HEX_WOOD = [new THREE.Color(0xf0d6b0), new THREE.Color(0xb07840), new THREE.Color(0x5c3a22)];
const WOOD_SHADES = [LIGHT_WOOD, MID_WOOD, DARK_WOOD];

const TARGET_HEIGHT: Record<string, number> = {
  King: KING_HEIGHT,
  Queen: KING_HEIGHT * 0.92,
  Bishop: KING_HEIGHT * 0.76,
  Knight: KING_HEIGHT * 0.68,
  Rook: KING_HEIGHT * 0.62,
  Pawn: KING_HEIGHT * 0.5,
  Lancer: KING_HEIGHT * 0.5,
  Bomber: KING_HEIGHT * 0.5,
  Defender: KING_HEIGHT * 0.52,
  Camel: KING_HEIGHT * 0.68,
  Cannon: KING_HEIGHT * 0.62,
  Crossbowman: KING_HEIGHT * 0.72,
};

/** Screen-left for P1 (looking +Z) is +X, so file A (x=0) is mirrored. */
function fileWorldX(x: number, width: number): number {
  return width - 1 - x;
}

export function tileCenter(x: number, y: number, layout: BoardLayout = "square", width = 8): THREE.Vector3 {
  const z = layout === "hex-offset" ? hexWorldZ(x, y) : y;
  return new THREE.Vector3(fileWorldX(x, width) + 0.5, 0, z + 0.5);
}

function stateLayout(state: PublicState): BoardLayout {
  return state.layout ?? (state.boardId === "hexa" ? "hex-offset" : "square");
}

function nodeCenter(state: PublicState, nodeId: number): THREE.Vector3 {
  return tileCenter(nodeId % state.width, Math.floor(nodeId / state.width), stateLayout(state), state.width);
}

/** 30% into the last tile (penultimate → destination), not 30% of the whole trip. */
function meleeApproach(from: THREE.Vector3, to: THREE.Vector3): THREE.Vector3 {
  const dir = to.clone().sub(from);
  const dist = dir.length();
  if (dist < 1e-6) return to.clone();
  const lastStart = to.clone().addScaledVector(dir.normalize(), -Math.min(dist, 1));
  return lastStart.lerp(to, 0.3);
}

function isGoldish(color: THREE.Color): boolean {
  return color.r > 0.55 && color.g > 0.4 && color.b < 0.35;
}

function itemTint(item: ItemState): string | undefined {
  return item.kind === "TransmuteScroll" ? item.color : undefined;
}

const SCROLL_SPARK: Record<string, number> = {
  EscapeScroll: 0x3a68e8,
  TransmuteScroll: 0xb8b8b8,
};

function isBlueBandMaterial(material: THREE.Material): boolean {
  const name = material.name ?? "";
  if (/blue|portal/i.test(name)) return true;
  if (!("color" in material)) return false;
  const color = (material as THREE.MeshStandardMaterial).color;
  return color.b > color.r + 0.08 && color.b > color.g + 0.08;
}

function resolveClip(clips: THREE.AnimationClip[], name: string, allowSingle = true): THREE.AnimationClip | undefined {
  let exact: THREE.AnimationClip | undefined;
  let suffix: THREE.AnimationClip | undefined;
  let contains: THREE.AnimationClip | undefined;
  let only: THREE.AnimationClip | undefined;
  let count = 0;
  for (const clip of clips) {
    count++;
    only = clip;
    const n = clip.name;
    if (n.toLowerCase() === name.toLowerCase()) exact = clip;
    else if (n.toLowerCase().endsWith(`_${name.toLowerCase()}`) || n.toLowerCase().endsWith(`.${name.toLowerCase()}`)) {
      suffix ??= clip;
    } else if (n.toLowerCase().includes(name.toLowerCase())) contains ??= clip;
  }
  return exact ?? suffix ?? contains ?? (allowSingle && count === 1 ? only : undefined);
}

interface PieceActor {
  group: THREE.Group;
  mixer?: THREE.AnimationMixer;
  move?: THREE.AnimationClip;
  attack?: THREE.AnimationClip;
  forcefield?: THREE.Mesh;
  fieldMat?: THREE.ShaderMaterial;
  heldItem?: THREE.Group;
  heldItemId?: string;
  traveling: boolean;
}

export type InspectHover = { kind: "unit"; unitId: number } | { kind: "item"; itemId: string };

export class GameView {
  private readonly scene = new THREE.Scene();
  private readonly camera: THREE.PerspectiveCamera;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly controls: OrbitControls;
  private readonly raycaster = new THREE.Raycaster();
  private readonly pointer = new THREE.Vector2();
  private readonly clock = new THREE.Clock();
  private readonly loader = new GLTFLoader();
  private readonly modelCache = new Map<string, Promise<GLTF>>();
  private readonly tiles = new Map<number, THREE.Mesh>();
  private readonly pieces = new Map<number, PieceActor>();
  private readonly itemMeshes = new Map<string, THREE.Group>();
  private readonly markers = new THREE.Group();
  private readonly hoverMarkers = new THREE.Group();
  private readonly abilityMarkers = new THREE.Group();
  private readonly circleMap = createCircleMap();
  private readonly ringMap = createRingMap();
  private readonly crossMap = createCrosshairMap();
  private woodLight: THREE.Texture = createWoodFallback(true);
  private woodDark: THREE.Texture = createWoodFallback(false);
  private boardRoot = new THREE.Group();
  private ground: THREE.Mesh | null = null;
  private groundMap: THREE.CanvasTexture | null = null;
  private fogTint: THREE.Color | null = null;
  private readonly rockSources: THREE.Group[] = [];
  private state: PublicState | null = null;
  private colors: [PlayerStyle, PlayerStyle] = [DEFAULT_STYLE_P1, DEFAULT_STYLE_P2];
  private readonly patternMaps = new Map<string, THREE.CanvasTexture>();
  private pointerDown: { x: number; y: number } | null = null;
  private raf = 0;
  private pawnScale: number | null = null;
  busy = false;
  onTileClick: ((nodeId: number) => void) | null = null;
  onItemClick: ((itemId: string, nodeId: number) => void) | null = null;
  onInspectHover: ((target: InspectHover | null) => void) | null = null;

  constructor(private readonly canvas: HTMLCanvasElement) {
    this.woodLight.wrapS = this.woodLight.wrapT = THREE.RepeatWrapping;
    this.woodDark.wrapS = this.woodDark.wrapT = THREE.RepeatWrapping;
    this.scene.background = new THREE.Color(0x5c4a38);
    this.camera = new THREE.PerspectiveCamera(42, 1, 0.1, 48);
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.48;
    this.controls.minDistance = 6;
    this.controls.maxDistance = 22;
    this.scene.add(new THREE.AmbientLight(0xffe8c8, 0.48));
    const key = new THREE.DirectionalLight(0xfff1d6, 1.25);
    key.position.set(7, 14, 5);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    this.scene.add(key);
    this.scene.add(new THREE.HemisphereLight(0xfff4dc, 0x3a2a1c, 0.28));
    this.scene.add(this.boardRoot);
    this.scene.add(this.markers);
    this.scene.add(this.hoverMarkers);
    this.scene.add(this.abilityMarkers);
    this.applyAtmosphere(8, 8, "square", false);
    this.resetCamera(8, 8, 0);
    canvas.addEventListener("pointerdown", this.onPointerDown);
    canvas.addEventListener("pointerup", this.onPointerUp);
    canvas.addEventListener("pointermove", this.onPointerMove);
    canvas.addEventListener("pointerleave", this.onPointerLeave);
    window.addEventListener("resize", this.resize);
    this.resize();
    void this.loadWood();
    void this.loadRocks();
    this.loop();
  }

  dispose(): void {
    cancelAnimationFrame(this.raf);
    window.removeEventListener("resize", this.resize);
    this.canvas.removeEventListener("pointerdown", this.onPointerDown);
    this.canvas.removeEventListener("pointerup", this.onPointerUp);
    this.canvas.removeEventListener("pointermove", this.onPointerMove);
    this.canvas.removeEventListener("pointerleave", this.onPointerLeave);
    this.controls.dispose();
    this.disposeGround();
    this.disposeBoardRoot();
    this.renderer.dispose();
  }

  refreshAtmosphere(): void {
    this.fogTint = null;
  }

  setColors(colors: [PlayerStyle, PlayerStyle]): void {
    this.colors = normalizeStyles(colors);
    if (!this.state) return;
    for (const unit of this.state.units) {
      const actor = this.pieces.get(unit.unitId);
      if (actor) this.tint(actor.group, unit.ownerId, unit.definitionId);
    }
  }

  setState(state: PublicState, localPlayerId = 0): void {
    const sizeChanged =
      !this.state ||
      this.state.width !== state.width ||
      this.state.height !== state.height ||
      stateLayout(this.state) !== stateLayout(state);
    const terrainChanged =
      !this.state ||
      this.state.boardId !== state.boardId ||
      this.state.nodes.length !== state.nodes.length ||
      this.state.nodes.some(
        (n, i) => n.currentType !== state.nodes[i]?.currentType || n.isActive !== state.nodes[i]?.isActive
      );
    this.colors = normalizeStyles(state.playerColors);
    this.state = state;
    if (sizeChanged || terrainChanged) {
      this.rebuildBoard(state);
      if (sizeChanged) this.resetCamera(state.width, state.height, localPlayerId, stateLayout(state));
    }
    this.syncPieces(state, true);
    this.syncItems(state);
  }

  async playOutcome(prev: PublicState, next: PublicState, move: MoveExecution): Promise<void> {
    this.busy = true;
    this.colors = normalizeStyles(next.playerColors);
    this.state = prev;
    this.syncPieces(prev, false);
    this.syncItems(prev);
    const actor = this.pieces.get(move.unitId);
    const from = nodeCenter(next, move.fromNode);
    const to = nodeCenter(next, move.toNode);
    if (actor) actor.group.position.copy(from);

    if (move.captureBlocked && move.forcefieldConsumedUnitId != null) {
      await this.playAttack(actor, false);
      await this.breakForcefield(move.forcefieldConsumedUnitId);
    } else if (move.isRangedCapture) {
      await this.playAttack(actor, false);
      if (move.capturedUnitId != null) await this.playDeath(move.capturedUnitId);
    } else if (move.isCapture) {
      await this.approachAndStrike(actor, from, to);
      if (move.capturedUnitId != null) await this.playDeath(move.capturedUnitId);
      await this.finishTravel(actor, to);
    } else {
      await this.travel(actor, from, to, false);
    }

    const blastOrigins = move.explosionOrigins ?? [];
    if (blastOrigins.length) {
      await Promise.all([
        this.shatterItems(move.destroyedItemIds ?? []),
        ...blastOrigins.map((origin, i) =>
          this.wait(i * 0.16).then(() => this.playExplosion(origin, move.blastNodes ?? []))
        ),
      ]);
      await Promise.all(
        (move.killedUnitIds ?? []).filter((id) => id !== move.capturedUnitId).map((id) => this.playDeath(id))
      );
    }

    this.setState(next);
    this.busy = false;
  }

  async playAction(prev: PublicState, next: PublicState, action: ActionExecution): Promise<void> {
    this.busy = true;
    this.colors = normalizeStyles(next.playerColors);
    this.state = prev;
    this.syncPieces(prev, false);
    this.syncItems(prev);
    if (action.actionId === "TransmuteScroll" || action.actionId === "EscapeScroll") {
      await this.playScrollUse(prev, next, action);
      this.busy = false;
      return;
    }
    const origins = action.explosionOrigins.length ? action.explosionOrigins : [action.originNode];
    await Promise.all([
      this.shatterItems(action.destroyedItemIds),
      ...origins.map((origin, i) =>
        this.wait(i * 0.16).then(() => this.playExplosion(origin, action.blastNodes))
      ),
    ]);
    await Promise.all(action.killedUnitIds.map((id) => this.playDeath(id)));
    this.setState(next);
    this.busy = false;
  }

  setPickableItems(itemIds: string[]): void {
    const ready = new Set(itemIds);
    for (const [id, group] of this.itemMeshes) {
      const pickable = ready.has(id);
      group.userData.pickable = pickable;
      const pad = group.userData.pad as THREE.Mesh | undefined;
      if (pad) pad.visible = pickable;
    }
  }

  setHighlights(targets: MoveTargets | null, ownerId = 0): void {
    this.paintMarkers(this.markers, targets, this.selectionColors(ownerId), 0.012, 1, 1);
  }

  setHoverHighlights(targets: MoveTargets | null, ownerId = 0): void {
    this.paintMarkers(this.hoverMarkers, targets, this.hoverColors(ownerId), 0.02, 0.92, 0.48);
  }

  setAbilityHighlights(nodeIds: number[] | null, colorHex = "#ff6a3d"): void {
    this.abilityMarkers.clear();
    if (!nodeIds?.length || !this.state) return;
    const color = new THREE.Color(colorHex);
    for (const id of nodeIds) {
      this.addMarker(this.abilityMarkers, id, this.ringMap, color, 0.96, 0.03, 0.9);
    }
  }

  private selectionColors(ownerId: number): { move: THREE.Color; capture: THREE.Color; ranged: THREE.Color } {
    return {
      move: new THREE.Color(ownerId === 0 ? "#3dff3d" : "#ff4d4d"),
      capture: new THREE.Color("#ff8080"),
      ranged: new THREE.Color("#f0c14b"),
    };
  }

  private hoverColors(ownerId: number): { move: THREE.Color; capture: THREE.Color; ranged: THREE.Color } {
    const fallback = ownerId === 0 ? DEFAULT_STYLE_P1.primary : DEFAULT_STYLE_P2.primary;
    const primary = new THREE.Color(this.colors[ownerId]?.primary ?? fallback);
    const hsl = { h: 0, s: 0, l: 0 };
    primary.getHSL(hsl);
    if (hsl.l < 0.38) primary.offsetHSL(0, 0.12, 0.28);
    if (hsl.l > 0.78) primary.offsetHSL(0, 0.18, -0.22);
    const capture = primary.clone().offsetHSL(0.04, 0.12, 0.04);
    const ranged = primary.clone().offsetHSL(-0.08, 0.08, 0.08);
    return { move: primary, capture, ranged };
  }

  private paintMarkers(
    group: THREE.Group,
    targets: MoveTargets | null,
    colors: { move: THREE.Color; capture: THREE.Color; ranged: THREE.Color },
    y: number,
    scale: number,
    opacity: number
  ): void {
    group.clear();
    if (!targets || !this.state) return;
    const both = new Set(targets.both);
    for (const id of [...targets.moveOnly, ...targets.both]) {
      this.addMarker(group, id, this.circleMap, colors.move, 0.68 * scale, y, opacity);
    }
    for (const id of [...targets.captureOnly, ...targets.both]) {
      this.addMarker(group, id, this.ringMap, both.has(id) ? colors.move : colors.capture, 0.9 * scale, y + 0.002, opacity);
    }
    for (const id of targets.rangedCapture) {
      this.addMarker(group, id, this.crossMap, colors.ranged, scale, y + 0.004, opacity);
    }
  }

  selectedNode(nodeId: number | null): void {
    for (const [id, mesh] of this.tiles) {
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const top = (mats[2] ?? mats[0]) as THREE.MeshStandardMaterial;
      if (!top.emissive) continue;
      top.emissive.setHex(id === nodeId ? 0x3d2a12 : 0x000000);
      top.emissiveIntensity = id === nodeId ? 0.35 : 0;
    }
  }

  private addMarker(
    group: THREE.Group,
    nodeId: number,
    map: THREE.Texture,
    color: THREE.Color,
    size: number,
    y: number,
    opacity: number
  ): void {
    const state = this.state;
    const node = state?.nodes[nodeId];
    if (!state || !node || !node.isActive || node.currentType === NodeType.Abyss) return;
    const at = nodeCenter(state, nodeId);
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(size, size), createMarkerMaterial(map, color, opacity));
    mesh.rotation.x = -Math.PI / 2;
    mesh.position.set(at.x, y, at.z);
    mesh.renderOrder = group === this.abilityMarkers ? 3 : group === this.hoverMarkers ? 2 : 1;
    group.add(mesh);
  }

  private async loadWood(): Promise<void> {
    const loader = new THREE.TextureLoader();
    const load = (file: string, fallback: THREE.Texture) =>
      new Promise<THREE.Texture>((resolve) => {
        loader.load(
          `${import.meta.env.BASE_URL}textures/${file}`,
          (tex) => {
            tex.colorSpace = THREE.SRGBColorSpace;
            tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
            tex.repeat.set(1, 1);
            resolve(tex);
          },
          undefined,
          () => resolve(fallback)
        );
      });
    const wood = await load("Wood2.jpg", this.woodDark);
    this.woodLight = wood;
    this.woodDark = wood;
    if (this.state) this.rebuildBoard(this.state);
    else this.applyAtmosphere(8, 8, "square", false);
  }

  private async loadRocks(): Promise<void> {
    try {
      const scenes = await Promise.all(ROCK_NAMES.map((name) => this.getModel(name)));
      this.rockSources.length = 0;
      for (const gltf of scenes) this.rockSources.push(gltf.scene);
    } catch (err) {
      console.warn("Rock obstacles failed to load", err);
    }
    if (this.state) this.rebuildBoard(this.state);
  }

  private placeRock(nodeId: number, x: number, y: number, wood: THREE.Color, map: THREE.Texture): void {
    if (this.rockSources.length === 0) return;
    const src = this.rockSources[nodeId % this.rockSources.length];
    if (!src) return;
    const visual = src.clone(true);
    visual.traverse((child) => {
      child.frustumCulled = false;
      const mesh = child as THREE.Mesh;
      if (mesh.isMesh) {
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        if (/base/i.test(mesh.name) || /base/i.test(mesh.parent?.name ?? "")) {
          mesh.material = new THREE.MeshStandardMaterial({
            color: wood,
            map,
            roughness: 0.62,
            metalness: 0.02,
          });
          return;
        }
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        mesh.material = mats.length === 1 ? (mats[0] as THREE.Material).clone() : mats.map((m) => (m as THREE.Material).clone());
      }
    });
    visual.rotation.y = ((nodeId * 2) % 4) * (Math.PI / 2);
    this.fitRock(visual);
    const layout = this.state ? stateLayout(this.state) : "square";
    const at = tileCenter(x, y, layout, this.state?.width ?? 8);
    visual.position.x += at.x;
    visual.position.z += at.z;
    this.boardRoot.add(visual);
  }

  private fitRock(visual: THREE.Object3D): void {
    visual.updateWorldMatrix(true, true);
    const box = this.visualBounds(visual);
    if (!box) return;
    const size = box.getSize(new THREE.Vector3());
    let scale = 1;
    const footprint = Math.max(size.x, size.z);
    if (footprint > 0.0001) scale = ROCK_FOOTPRINT / footprint;
    if (size.y * scale > ROCK_MAX_HEIGHT) scale = ROCK_MAX_HEIGHT / size.y;
    visual.scale.multiplyScalar(scale);
    visual.updateWorldMatrix(true, true);
    const fitted = this.visualBounds(visual);
    if (!fitted) return;
    const center = fitted.getCenter(new THREE.Vector3());
    visual.position.x += -center.x;
    visual.position.z += -center.z;
    visual.position.y += -fitted.min.y;
  }

  private tileGeometry(id: number): THREE.BoxGeometry {
    const geo = new THREE.BoxGeometry(SLAB, THICKNESS, SLAB);
    const uv = geo.attributes.uv;
    const turns = (id * 7 + 3) % 4;
    const ox = ((id * 17) % 10) / 10;
    const oy = ((id * 13) % 7) / 7;
    const flipU = id % 2 === 0 ? 1 : -1;
    const flipV = (id * 3) % 2 === 0 ? 1 : -1;
    for (let i = 8; i < 12; i++) {
      let u = (uv.getX(i) - 0.5) * flipU;
      let v = (uv.getY(i) - 0.5) * flipV;
      if (turns === 1) [u, v] = [-v, u];
      else if (turns === 2) [u, v] = [-u, -v];
      else if (turns === 3) [u, v] = [v, -u];
      uv.setXY(i, u + 0.5 + ox, v + 0.5 + oy);
    }
    const sox = ((id * 11) % 8) / 8;
    const soy = ((id * 19) % 10) / 10;
    for (const start of [0, 4, 16, 20]) {
      for (let i = start; i < start + 4; i++) {
        uv.setXY(i, uv.getX(i) + sox, uv.getY(i) * THICKNESS + soy);
      }
    }
    uv.needsUpdate = true;
    return geo;
  }

  private disposeBoardRoot(): void {
    const geos = new Set<THREE.BufferGeometry>();
    const mats = new Set<THREE.Material>();
    this.boardRoot.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      if (mesh.userData.ownGeometry) geos.add(mesh.geometry);
      const list = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      for (const mat of list) {
        if (mesh.userData.ownGeometry) mats.add(mat);
      }
    });
    for (const mat of mats) {
      const map = (mat as THREE.MeshStandardMaterial).map;
      if (map && map !== this.woodLight && map !== this.woodDark) map.dispose();
      mat.dispose();
    }
    for (const geo of geos) geo.dispose();
    this.scene.remove(this.boardRoot);
  }

  private rebuildBoard(state: PublicState): void {
    this.disposeBoardRoot();
    this.boardRoot = new THREE.Group();
    this.tiles.clear();
    const layout = stateLayout(state);
    const sideMat = new THREE.MeshStandardMaterial({
      color: SIDE_WOOD,
      map: this.woodDark,
      roughness: 0.92,
      metalness: 0.03,
    });
    for (let y = 0; y < state.height; y++) {
      for (let x = 0; x < state.width; x++) {
        const id = y * state.width + x;
        const node = state.nodes[id];
        const type = node?.currentType ?? NodeType.Normal;
        const at = tileCenter(x, y, layout, state.width);
        if (isStructuralGap(type, x, y, state.width, state.height, layout)) continue;
        if (!node?.isActive || type === NodeType.Abyss || type === NodeType.Destroyed) continue;
        const shade = state.shades?.[id] ?? (state.lights[id] ? 0 : 2);
        const palette = layout === "hex-offset" ? HEX_WOOD : WOOD_SHADES;
        const wood = palette[shade] ?? DARK_WOOD;
        const impassable = type === NodeType.Impassable;
        const woodMap = shade === 2 ? this.woodDark : this.woodLight;
        const topMat = new THREE.MeshStandardMaterial({
          color: wood,
          map: woodMap,
          roughness: 0.62,
          metalness: 0.02,
        });
        const mesh = new THREE.Mesh(this.tileGeometry(id), [sideMat, sideMat, topMat, sideMat, sideMat, sideMat]);
        mesh.position.set(at.x, -THICKNESS / 2, at.z);
        mesh.rotation.y = ((id * 5) % 4) * (Math.PI / 2);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        mesh.userData.ownGeometry = true;
        if (!impassable) mesh.userData.nodeId = id;
        this.tiles.set(id, mesh);
        this.boardRoot.add(mesh);
        if (impassable) this.placeRock(id, x, y, wood, woodMap);
      }
    }
    const w = state.width;
    const z0 = layout === "hex-offset" ? -0.5 : 0;
    const z1 = layout === "hex-offset" ? state.height - 0.5 : state.height;
    const depth = z1 - z0;
    const voidFloor = new THREE.Mesh(
      new THREE.PlaneGeometry(w + 1.2, depth + 1.2),
      new THREE.MeshBasicMaterial({ color: 0x050403 })
    );
    voidFloor.rotation.x = -Math.PI / 2;
    voidFloor.position.set(w / 2, -THICKNESS - 0.08, (z0 + z1) / 2);
    voidFloor.userData.ownGeometry = true;
    this.boardRoot.add(voidFloor);
    const frameMat = new THREE.MeshStandardMaterial({
      color: 0x3a2616,
      map: this.woodDark,
      roughness: 0.78,
    });
    const frameT = 0.22;
    const frameH = THICKNESS + 0.1;
    const frameY = -THICKNESS / 2 - 0.03;
    const north = new THREE.Mesh(new THREE.BoxGeometry(w + frameT * 2, frameH, frameT), frameMat);
    north.position.set(w / 2, frameY, z0 - frameT / 2);
    const south = new THREE.Mesh(new THREE.BoxGeometry(w + frameT * 2, frameH, frameT), frameMat);
    south.position.set(w / 2, frameY, z1 + frameT / 2);
    const west = new THREE.Mesh(new THREE.BoxGeometry(frameT, frameH, depth), frameMat);
    west.position.set(-frameT / 2, frameY, (z0 + z1) / 2);
    const east = new THREE.Mesh(new THREE.BoxGeometry(frameT, frameH, depth), frameMat);
    east.position.set(w + frameT / 2, frameY, (z0 + z1) / 2);
    north.userData.ownGeometry = true;
    south.userData.ownGeometry = true;
    west.userData.ownGeometry = true;
    east.userData.ownGeometry = true;
    this.boardRoot.add(north, south, west, east);
    this.addCoordLabels(w, state.height, layout);
    this.scene.add(this.boardRoot);
    this.applyAtmosphere(
      w,
      state.height,
      layout,
      true,
      fogSeedFromNodes(
        w,
        state.height,
        state.nodes.map((n) => n.currentType)
      )
    );
  }

  private disposeGround(): void {
    if (this.ground) {
      this.scene.remove(this.ground);
      this.ground.geometry.dispose();
      const mat = this.ground.material;
      if (Array.isArray(mat)) mat.forEach((m) => m.dispose());
      else mat.dispose();
      this.ground = null;
    }
    this.groundMap?.dispose();
    this.groundMap = null;
  }

  private applyAtmosphere(width: number, height: number, layout: BoardLayout, cutBoard: boolean, seed?: number): void {
    const baked = bakeCheckerGround({
      width,
      height,
      layout,
      wood: this.woodLight,
      cutBoard,
    });
    this.groundMap?.dispose();
    this.groundMap = baked.map;
    baked.map.anisotropy = this.renderer.capabilities.getMaxAnisotropy();
    const mat = new THREE.MeshStandardMaterial({
      map: baked.map,
      roughness: 0.9,
      metalness: 0,
      transparent: true,
      alphaTest: 0.04,
      depthWrite: true,
    });
    if (this.ground) {
      this.ground.geometry.dispose();
      const prev = this.ground.material;
      if (Array.isArray(prev)) prev.forEach((m) => m.dispose());
      else prev.dispose();
      this.ground.geometry = createGroundGeometry(baked.size);
      this.ground.material = mat;
    } else {
      this.ground = new THREE.Mesh(createGroundGeometry(baked.size), mat);
      this.ground.receiveShadow = true;
      this.ground.raycast = () => {};
      this.scene.add(this.ground);
    }
    this.ground.position.set(baked.cx, -0.002, baked.cz);
    if (!this.fogTint) {
      const rand = seed != null ? fogRandFromSeed(seed) : Math.random;
      this.fogTint = subtleFogTint(baked.mid, rand);
    }
    const haze = this.fogTint;
    this.scene.background = haze.clone();
    const fog = fogRange(width, height);
    this.scene.fog = new THREE.Fog(haze.clone(), fog.near, fog.far);
    this.camera.far = Math.max(fog.far * 1.35, this.controls.maxDistance + 8);
    this.camera.updateProjectionMatrix();
  }

  private addCoordLabels(width: number, height: number, layout: BoardLayout = "square"): void {
    const files = "abcdefghij";
    const z0 = layout === "hex-offset" ? -0.5 : 0;
    for (let x = 0; x < width; x++) {
      const mark = this.coordSprite(files[x] ?? String(x + 1));
      mark.position.set(fileWorldX(x, width) + 0.5, 0.16, z0 - 0.38);
      this.boardRoot.add(mark);
    }
    for (let y = 0; y < height; y++) {
      const mark = this.coordSprite(String(y + 1));
      mark.position.set(width + 0.38, 0.16, (layout === "hex-offset" ? hexWorldZ(0, y) : y) + 0.5);
      this.boardRoot.add(mark);
    }
  }

  private coordSprite(text: string): THREE.Sprite {
    const canvas = document.createElement("canvas");
    canvas.width = 64;
    canvas.height = 64;
    const ctx = canvas.getContext("2d")!;
    ctx.clearRect(0, 0, 64, 64);
    ctx.fillStyle = "#d8c9a8";
    ctx.font = "600 36px Segoe UI, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(text, 32, 34);
    const map = new THREE.CanvasTexture(canvas);
    map.colorSpace = THREE.SRGBColorSpace;
    const sprite = new THREE.Sprite(
      new THREE.SpriteMaterial({ map, transparent: true, depthWrite: false, depthTest: false })
    );
    sprite.scale.set(0.28, 0.28, 1);
    return sprite;
  }

  private syncPieces(state: PublicState, snap: boolean): void {
    const alive = new Set<number>();
    for (const unit of state.units) {
      if (!unit.isAlive) continue;
      alive.add(unit.unitId);
      let actor = this.pieces.get(unit.unitId);
      if (!actor) {
        const group = new THREE.Group();
        group.userData.unitId = unit.unitId;
        this.scene.add(group);
        actor = { group, traveling: false };
        this.pieces.set(unit.unitId, actor);
        void this.loadPiece(actor, unit);
      }
      if (snap && !actor.traveling) {
        actor.group.position.copy(nodeCenter(state, unit.currentNodeId));
      }
      actor.group.rotation.y = unit.ownerId === 0 ? 0 : Math.PI;
      this.syncForcefield(actor, unit);
      this.syncHeldItem(actor, unit);
    }
    for (const [id, actor] of this.pieces) {
      if (alive.has(id) || actor.group.userData.dying) continue;
      this.scene.remove(actor.group);
      this.pieces.delete(id);
    }
  }

  private syncItems(state: PublicState): void {
    const seen = new Set(state.items.map((i) => i.id));
    for (const item of state.items) {
      let group = this.itemMeshes.get(item.id);
      if (!group) {
        group = this.makeItem(item);
        this.itemMeshes.set(item.id, group);
        this.scene.add(group);
      }
      const at = nodeCenter(state, item.nodeId);
      group.position.set(at.x + 0.32, 0, at.z + 0.32);
      group.userData.itemId = item.id;
      group.userData.nodeId = item.nodeId;
    }
    for (const [id, group] of this.itemMeshes) {
      if (seen.has(id) || group.userData.leaving) continue;
      this.scene.remove(group);
      this.itemMeshes.delete(id);
    }
  }

  private makeItem(item: ItemState): THREE.Group {
    const group = new THREE.Group();
    const pad = this.makePickupPad();
    group.add(pad);
    group.userData.pad = pad;
    if (item.model) {
      void this.loadItemModel(group, item.model, itemTint(item));
      return group;
    }
    const visual = this.makeItemPrimitive(item);
    group.userData.spin = visual;
    group.add(visual);
    return group;
  }

  private makePickupPad(): THREE.Mesh {
    const pad = new THREE.Mesh(
      new THREE.CircleGeometry(ITEM_PAD_RADIUS, 32),
      new THREE.MeshBasicMaterial({
        color: PICK_PAD_READY,
        transparent: true,
        opacity: 0.92,
        depthWrite: false,
        side: THREE.DoubleSide,
      })
    );
    pad.name = "pickupPad";
    pad.rotation.x = -Math.PI / 2;
    pad.position.y = 0.012;
    pad.userData.pickupPad = true;
    pad.visible = false;
    return pad;
  }

  private makeItemPrimitive(item: ItemState): THREE.Group {
    const color = new THREE.Color(item.color);
    const visual = new THREE.Group();
    const geo =
      item.shape === "cube"
        ? new THREE.BoxGeometry(0.33, 0.33, 0.33)
        : item.shape === "capsule"
          ? new THREE.CapsuleGeometry(0.11, 0.28, 6, 12)
          : new THREE.SphereGeometry(0.24, 22, 16);
    const mesh = new THREE.Mesh(
      geo,
      new THREE.MeshStandardMaterial({
        color,
        emissive: color,
        emissiveIntensity: 0.55,
        roughness: 0.2,
        metalness: 0.4,
      })
    );
    mesh.castShadow = true;
    mesh.position.y = item.shape === "cube" ? 0.165 : item.shape === "capsule" ? 0.28 : 0.24;
    visual.add(mesh);
    if (item.shape !== "cube") {
      const halo = new THREE.Mesh(
        new THREE.SphereGeometry(0.3, 16, 12),
        new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.2 })
      );
      halo.position.y = 0.24;
      visual.add(halo);
    }
    return visual;
  }

  private async loadItemModel(group: THREE.Group, model: string, tint?: string): Promise<void> {
    try {
      const visual = await this.buildItemVisual(model, ITEM_FOOTPRINT, tint);
      if (visual) {
        visual.position.y += 0.02;
        group.userData.spin = visual;
        group.add(visual);
      }
    } catch (err) {
      console.warn(`Item model ${model} failed to load`, err);
    }
  }

  private async buildItemVisual(model: string, footprint: number, tint?: string): Promise<THREE.Group | null> {
    const gltf = await this.getModel(model);
    const visual = gltf.scene.clone(true);
    const dye = tint ? new THREE.Color(tint) : null;
    visual.traverse((child) => {
      child.frustumCulled = false;
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const cloned = mats.map((m) => {
        const next = (m as THREE.Material).clone();
        if (dye && "color" in next && isBlueBandMaterial(next)) {
          (next as THREE.MeshStandardMaterial).color.copy(dye);
        }
        return next;
      });
      mesh.material = cloned.length === 1 ? cloned[0] : cloned;
    });
    this.fitItem(visual, footprint);
    return visual;
  }

  private fitItem(visual: THREE.Object3D, footprint = ITEM_FOOTPRINT): void {
    visual.updateWorldMatrix(true, true);
    const box = new THREE.Box3().setFromObject(visual);
    if (box.isEmpty()) return;
    const size = box.getSize(new THREE.Vector3());
    const span = Math.max(size.x, size.y, size.z);
    const scale = span > 1e-6 ? footprint / span : 1;
    visual.scale.multiplyScalar(scale);
    visual.updateWorldMatrix(true, true);
    const fitted = new THREE.Box3().setFromObject(visual);
    const center = fitted.getCenter(new THREE.Vector3());
    visual.position.x += -center.x;
    visual.position.z += -center.z;
    visual.position.y += -fitted.min.y;
  }

  private syncForcefield(actor: PieceActor, unit: UnitState): void {
    const active = unit.skills.some((s) => s.id === "Forcefield" && s.isActive);
    if (active && !actor.forcefield) {
      const color = new THREE.Color(unit.ownerId === 0 ? 0xffe08a : 0x4ec8ff);
      const mat = createForcefieldMaterial(color);
      const shield = new THREE.Mesh(new THREE.SphereGeometry(0.58, 28, 20), mat);
      shield.name = "forcefield";
      shield.scale.setScalar(1.12);
      shield.position.y = 0.58;
      actor.group.add(shield);
      actor.forcefield = shield;
      actor.fieldMat = mat;
    } else if (!active && actor.forcefield && !actor.group.userData.breakingField) {
      actor.group.remove(actor.forcefield);
      actor.forcefield = undefined;
      actor.fieldMat = undefined;
    }
  }

  private syncHeldItem(actor: PieceActor, unit: UnitState): void {
    const item = unit.heldItem;
    const id = item?.id;
    if (id === actor.heldItemId) return;
    this.clearHeldItem(actor);
    if (!item) return;
    actor.heldItemId = item.id;
    void this.attachHeldItem(actor, unit, item);
  }

  private clearHeldItem(actor: PieceActor): void {
    if (actor.heldItem) actor.group.remove(actor.heldItem);
    actor.heldItem = undefined;
    actor.heldItemId = undefined;
  }

  private async attachHeldItem(actor: PieceActor, unit: UnitState, item: ItemState): Promise<void> {
    const token = item.id;
    try {
      const held = await this.makeHeldVisual(item, unit.definitionId);
      if (actor.heldItemId !== token) return;
      actor.heldItem = held;
      actor.group.add(held);
    } catch (err) {
      console.warn(`Held item ${item.kind} failed to load`, err);
    }
  }

  private async makeHeldVisual(item: ItemState, pieceId: string): Promise<THREE.Group> {
    const root = new THREE.Group();
    root.name = "heldItem";
    const height = TARGET_HEIGHT[pieceId] ?? 1.2;
    root.position.set(0.34, height * 0.46, 0.02);
    root.rotation.set(-0.15, 0.28, 0);
    if (item.model) {
      const visual = await this.buildItemVisual(item.model, ITEM_FOOTPRINT, itemTint(item));
      if (visual) root.add(visual);
    } else {
      root.add(this.makeItemPrimitive(item));
    }
    return root;
  }

  async createItemVisual(item: ItemState): Promise<THREE.Group> {
    if (item.model) {
      const visual = await this.buildItemVisual(item.model, ITEM_FOOTPRINT, itemTint(item));
      if (visual) return visual;
    }
    return this.makeItemPrimitive(item);
  }

  async createPieceVisual(unit: UnitState): Promise<THREE.Group> {
    const built = await this.buildVisual(unit);
    if (unit.heldItem) {
      const held = await this.makeHeldVisual(unit.heldItem, unit.definitionId);
      built.visual.add(held);
    }
    return built.visual;
  }

  private async loadPiece(actor: PieceActor, unit: UnitState): Promise<void> {
    const { visual, gltf } = await this.buildVisual(unit);
    actor.group.add(visual);
    if (gltf?.animations.length) {
      actor.mixer = new THREE.AnimationMixer(visual);
      actor.move = resolveClip(gltf.animations, "Move", true);
      actor.attack = resolveClip(gltf.animations, "Attack", false);
      actor.mixer.addEventListener("finished", () => undefined);
    }
  }

  private async buildVisual(unit: UnitState): Promise<{ visual: THREE.Group; gltf?: GLTF }> {
    const def = PIECES[unit.definitionId];
    const model = def?.model ?? unit.definitionId.toLowerCase();
    try {
      if (unit.definitionId === "Lancer") await this.ensurePawnScale();
      const gltf = await this.getModel(model);
      const visual = cloneSkinned(gltf.scene) as THREE.Group;
      visual.traverse((child) => {
        child.visible = true;
        child.frustumCulled = false;
        const mesh = child as THREE.Mesh;
        if (mesh.isMesh && mesh.geometry) {
          const count = mesh.geometry.index?.count ?? mesh.geometry.attributes.position?.count ?? Infinity;
          mesh.geometry.setDrawRange(0, count);
          mesh.geometry.computeBoundingSphere();
        }
      });
      this.tint(visual, unit.ownerId, unit.definitionId);
      this.fit(visual, unit.definitionId);
      return { visual, gltf };
    } catch (err) {
      console.warn(`Piece ${unit.definitionId} failed to load`, err);
      return { visual: this.fallback(unit) };
    }
  }

  private async ensurePawnScale(): Promise<void> {
    if (this.pawnScale != null) return;
    try {
      const pawn = await this.getModel("pawn");
      const box = new THREE.Box3().setFromObject(pawn.scene);
      const size = box.getSize(new THREE.Vector3());
      this.pawnScale = size.y > 0.0001 ? TARGET_HEIGHT.Pawn / size.y : 1;
    } catch {
      this.pawnScale = 1;
    }
  }

  private getModel(name: string): Promise<GLTF> {
    const existing = this.modelCache.get(name);
    if (existing) return existing;
    const url = `${import.meta.env.BASE_URL}models/${name}.glb`;
    const pending = this.loader.loadAsync(url);
    this.modelCache.set(name, pending);
    return pending;
  }

  private isDecorMesh(child: THREE.Object3D): boolean {
    for (let node: THREE.Object3D | null = child; node; node = node.parent) {
      if (node.name === "forcefield" || node.name === "heldItem") return true;
    }
    return false;
  }

  private styleOf(ownerId: number): PlayerStyle {
    return this.colors[ownerId] ?? (ownerId === 0 ? DEFAULT_STYLE_P1 : DEFAULT_STYLE_P2);
  }

  private patternMap(style: PlayerStyle, field: THREE.Color, tiles: number): THREE.CanvasTexture {
    const key = `${style.pattern}|${style.secondary}|#${field.getHexString()}|${tiles}`;
    const cached = this.patternMaps.get(key);
    if (cached) return cached;
    const tex = createPatternMap(style.pattern, `#${field.getHexString()}`, style.secondary, tiles);
    this.patternMaps.set(key, tex);
    return tex;
  }

  private tint(root: THREE.Object3D, ownerId: number, pieceId?: string): void {
    const style = this.styleOf(ownerId);
    const primary = new THREE.Color(style.primary);
    const secondary = new THREE.Color(style.secondary);
    const tiles = pieceId === "Lancer" ? 4 : 3;
    root.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh || this.isDecorMesh(child)) return;
      mesh.castShadow = true;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      if (!mesh.userData.heraldryBase) {
        mesh.userData.heraldryBase = mats.map((mat) => {
          const std = mat as THREE.MeshStandardMaterial;
          return {
            color: std.color?.clone() ?? new THREE.Color(0xffffff),
            name: std.name ?? "",
            metalness: std.metalness ?? 0,
          };
        });
      }
      const bases = mesh.userData.heraldryBase as { color: THREE.Color; name: string; metalness: number }[];
      const next = mats.map((mat, i) => {
        const cloned = (mat as THREE.MeshStandardMaterial).clone();
        const base = bases[i] ?? { color: cloned.color, name: cloned.name ?? "", metalness: cloned.metalness ?? 0 };
        const name = (base.name || cloned.name || "").toLowerCase();
        const role =
          name.includes("accent") || base.metalness > 0.55 || isGoldish(base.color)
            ? "accent"
            : name.includes("secondary")
              ? "secondary"
              : "primary";
        if (role === "accent") {
          cloned.color.copy(secondary);
          cloned.map = null;
        } else if (role === "secondary") {
          cloned.color.copy(base.color).lerp(primary, 0.4);
          cloned.map = null;
        } else if (cloned.color) {
          const field = base.color.clone().lerp(primary, 0.5);
          cloned.color.set(0xffffff);
          cloned.map = this.patternMap(style, field, tiles);
        }
        return cloned;
      });
      mesh.material = next.length === 1 ? next[0] : next;
    });
  }

  private fit(visual: THREE.Object3D, pieceId: string): void {
    visual.updateWorldMatrix(true, true);
    const box = this.visualBounds(visual);
    if (!box) return;
    const size = box.getSize(new THREE.Vector3());
    const target = TARGET_HEIGHT[pieceId] ?? KING_HEIGHT * 0.7;
    let scale = size.y > 0.0001 ? target / size.y : 1;
    if (pieceId === "Lancer" && this.pawnScale != null) {
      scale = this.pawnScale;
    } else {
      const footprint = Math.max(size.x, size.z);
      if (footprint > 0.0001) scale = Math.min(scale, MAX_FOOTPRINT / footprint);
    }
    visual.scale.multiplyScalar(scale);
    visual.updateWorldMatrix(true, true);
    const fitted = this.visualBounds(visual);
    if (!fitted) return;
    const center = fitted.getCenter(new THREE.Vector3());
    visual.position.x += -center.x;
    visual.position.z += -center.z;
    visual.position.y += -fitted.min.y;
  }

  private visualBounds(root: THREE.Object3D): THREE.Box3 | null {
    const boxes: THREE.Box3[] = [];
    root.updateWorldMatrix(true, true);
    root.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh || !mesh.geometry) return;
      mesh.geometry.computeBoundingBox();
      if (!mesh.geometry.boundingBox) return;
      const box = mesh.geometry.boundingBox.clone().applyMatrix4(mesh.matrixWorld);
      boxes.push(box);
    });
    if (!boxes.length) return null;
    let max = 0;
    for (const box of boxes) {
      const s = box.getSize(new THREE.Vector3());
      max = Math.max(max, s.x, s.y, s.z);
    }
    const keep = max * 0.08;
    const merged = new THREE.Box3();
    let started = false;
    for (const box of boxes) {
      const s = box.getSize(new THREE.Vector3());
      if (Math.max(s.x, s.y, s.z) < keep) continue;
      if (!started) {
        merged.copy(box);
        started = true;
      } else merged.union(box);
    }
    return started ? merged : boxes[0];
  }

  private fallback(unit: UnitState): THREE.Group {
    const color = new THREE.Color(this.styleOf(unit.ownerId).primary);
    const height = TARGET_HEIGHT[unit.definitionId] ?? 1.2;
    const group = new THREE.Group();
    const body = new THREE.Mesh(
      new THREE.CylinderGeometry(0.18, 0.26, height * 0.7, 16),
      new THREE.MeshStandardMaterial({ color })
    );
    body.position.y = height * 0.35;
    body.castShadow = true;
    group.add(body);
    return group;
  }

  private playClip(actor: PieceActor | undefined, clip: THREE.AnimationClip | undefined, loop: boolean): void {
    if (!actor?.mixer || !clip) return;
    actor.mixer.stopAllAction();
    const action = actor.mixer.clipAction(clip);
    action.reset();
    action.setLoop(loop ? THREE.LoopRepeat : THREE.LoopOnce, loop ? Infinity : 1);
    action.clampWhenFinished = !loop;
    action.play();
  }

  private async travel(actor: PieceActor | undefined, from: THREE.Vector3, to: THREE.Vector3, melee: boolean): Promise<void> {
    if (melee) {
      await this.approachAndStrike(actor, from, to);
      await this.finishTravel(actor, to);
      return;
    }
    if (!actor) return;
    actor.traveling = true;
    actor.group.position.copy(from);
    this.playClip(actor, actor.move, true);
    await this.lerpPos(actor.group, from, to, actor.move == null);
    actor.mixer?.stopAllAction();
    actor.traveling = false;
    actor.group.position.copy(to);
  }

  private async approachAndStrike(actor: PieceActor | undefined, from: THREE.Vector3, to: THREE.Vector3): Promise<void> {
    if (!actor) return;
    actor.traveling = true;
    actor.group.position.copy(from);
    this.playClip(actor, actor.move, true);
    const approach = meleeApproach(from, to);
    await this.lerpPos(actor.group, from, approach, actor.move == null);
    actor.mixer?.stopAllAction();
    await this.playAttack(actor, true);
  }

  private async finishTravel(actor: PieceActor | undefined, to: THREE.Vector3): Promise<void> {
    if (!actor) return;
    this.playClip(actor, actor.move, true);
    await this.lerpPos(actor.group, actor.group.position.clone(), to, actor.move == null);
    actor.mixer?.stopAllAction();
    actor.traveling = false;
    actor.group.position.copy(to);
  }

  private lerpPos(obj: THREE.Object3D, from: THREE.Vector3, to: THREE.Vector3, bounce: boolean): Promise<void> {
    const dist = from.distanceTo(to);
    const duration = Math.max(dist / MOVE_SPEED, 0.12);
    return this.tween(duration, (t) => {
      obj.position.lerpVectors(from, to, t);
      if (bounce) obj.position.y = Math.sin(t * Math.PI) * 0.18;
    });
  }

  private async playAttack(actor: PieceActor | undefined, melee: boolean): Promise<void> {
    if (!actor) return;
    if (actor.attack && actor.mixer) {
      this.playClip(actor, actor.attack, false);
      await this.wait(Math.max(actor.attack.duration, 0.25));
      actor.mixer.stopAllAction();
      return;
    }
    const start = actor.group.scale.clone();
    await this.tween(0.1, (t) => actor.group.scale.copy(start).multiplyScalar(1 + 0.3 * t));
    await this.tween(0.1, (t) => actor.group.scale.copy(start).multiplyScalar(1.3 - 0.3 * t));
    actor.group.scale.copy(start);
    void melee;
  }

  private async shatterItems(itemIds: string[]): Promise<void> {
    await Promise.all(itemIds.map((id) => this.shatterItem(id)));
  }

  private async shatterItem(itemId: string): Promise<void> {
    const group = this.itemMeshes.get(itemId);
    if (!group) return;
    group.userData.leaving = true;
    const start = group.scale.clone();
    await this.tween(0.42, (t) => {
      const fade = 1 - t;
      group.scale.copy(start).multiplyScalar(1 + t * 1.15);
      group.position.y += 0.012;
      group.traverse((child) => {
        const mesh = child as THREE.Mesh;
        if (!mesh.isMesh) return;
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        for (const mat of mats) {
          const m = mat as THREE.Material & { opacity?: number };
          m.transparent = true;
          m.depthWrite = false;
          if (typeof m.opacity === "number") m.opacity = fade;
        }
      });
    });
    this.scene.remove(group);
    this.itemMeshes.delete(itemId);
  }

  private async playScrollUse(prev: PublicState, next: PublicState, action: ActionExecution): Promise<void> {
    const color = new THREE.Color(SCROLL_SPARK[action.actionId] ?? 0xb8b8b8);
    const origin = nodeCenter(prev, action.originNode);
    origin.y = 0.48;
    if (action.actionId === "EscapeScroll" && action.destNode != null) {
      const dest = nodeCenter(prev, action.destNode);
      dest.y = 0.48;
      const actor = this.pieces.get(action.unitId);
      await Promise.all([
        this.playSparkBurst(origin, color),
        actor ? this.travel(actor, origin.clone().setY(0), dest.clone().setY(0), false) : Promise.resolve(),
      ]);
      await this.playSparkBurst(dest, color);
    } else {
      const bursts = [this.playSparkBurst(origin, color)];
      if (action.destNode != null) {
        const dest = nodeCenter(prev, action.destNode);
        dest.y = 0.32;
        bursts.push(this.playSparkBurst(dest, color));
      }
      await Promise.all(bursts);
    }
    this.setState(next);
  }

  private async playSparkBurst(origin: THREE.Vector3, color: THREE.Color): Promise<void> {
    const group = new THREE.Group();
    this.scene.add(group);

    const light = new THREE.PointLight(color, 0, 5);
    light.position.copy(origin);
    group.add(light);

    const hot = color.clone().lerp(new THREE.Color(0xffffff), 0.4);
    const ringMat = new THREE.MeshBasicMaterial({
      color: hot,
      transparent: true,
      opacity: 0.75,
      side: THREE.DoubleSide,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const ring = new THREE.Mesh(new THREE.RingGeometry(0.08, 0.2, 28), ringMat);
    ring.rotation.x = -Math.PI / 2;
    ring.position.set(origin.x, 0.04, origin.z);
    group.add(ring);

    const sparkGeo = new THREE.SphereGeometry(0.032, 7, 5);
    const sparks: { mesh: THREE.Mesh; vel: THREE.Vector3 }[] = [];
    for (let i = 0; i < 18; i++) {
      const mat = new THREE.MeshBasicMaterial({
        color: i % 3 === 0 ? hot : color,
        transparent: true,
        opacity: 1,
        depthWrite: false,
        blending: THREE.AdditiveBlending,
      });
      const mesh = new THREE.Mesh(sparkGeo, mat);
      mesh.position.copy(origin);
      const vel = new THREE.Vector3((Math.random() - 0.5) * 2.8, 0.9 + Math.random() * 2.1, (Math.random() - 0.5) * 2.8);
      sparks.push({ mesh, vel });
      group.add(mesh);
    }

    await this.tween(0.48, (t) => {
      const grow = 1 - (1 - t) * (1 - t);
      const fade = 1 - t;
      ring.scale.setScalar(1 + grow * 3.4);
      ringMat.opacity = 0.7 * fade;
      light.intensity = 3.4 * fade;
      for (const spark of sparks) {
        spark.mesh.position.addScaledVector(spark.vel, 0.016);
        spark.vel.y -= 0.038;
        (spark.mesh.material as THREE.MeshBasicMaterial).opacity = fade;
      }
    });

    this.scene.remove(group);
    ring.geometry.dispose();
    ringMat.dispose();
    sparkGeo.dispose();
    for (const spark of sparks) (spark.mesh.material as THREE.Material).dispose();
  }

  private async playExplosion(originNode: number, blastNodes: number[]): Promise<void> {
    const state = this.state;
    if (!state) return;
    const origin = nodeCenter(state, originNode);
    origin.y = 0.38;
    const group = new THREE.Group();
    this.scene.add(group);

    const light = new THREE.PointLight(0xff7a18, 0, 10);
    light.position.copy(origin);
    group.add(light);

    const fireMat = new THREE.MeshBasicMaterial({
      color: 0xffee88,
      transparent: true,
      opacity: 0.95,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const fire = new THREE.Mesh(new THREE.SphereGeometry(0.18, 18, 14), fireMat);
    fire.position.copy(origin);
    group.add(fire);

    const smokeMat = new THREE.MeshBasicMaterial({
      color: 0x4a2a18,
      transparent: true,
      opacity: 0.35,
      depthWrite: false,
    });
    const smoke = new THREE.Mesh(new THREE.SphereGeometry(0.22, 14, 10), smokeMat);
    smoke.position.copy(origin);
    group.add(smoke);

    const ringMat = new THREE.MeshBasicMaterial({
      color: 0xffaa33,
      transparent: true,
      opacity: 0.85,
      side: THREE.DoubleSide,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const ring = new THREE.Mesh(new THREE.RingGeometry(0.12, 0.28, 36), ringMat);
    ring.rotation.x = -Math.PI / 2;
    ring.position.set(origin.x, 0.05, origin.z);
    group.add(ring);

    const sparks: { mesh: THREE.Mesh; vel: THREE.Vector3 }[] = [];
    const sparkGeo = new THREE.SphereGeometry(0.045, 8, 6);
    for (let i = 0; i < 22; i++) {
      const mat = new THREE.MeshBasicMaterial({
        color: i % 3 === 0 ? 0xfff3a8 : 0xff6a12,
        transparent: true,
        opacity: 1,
        depthWrite: false,
        blending: THREE.AdditiveBlending,
      });
      const mesh = new THREE.Mesh(sparkGeo, mat);
      mesh.position.copy(origin);
      const vel = new THREE.Vector3((Math.random() - 0.5) * 4.2, 1.4 + Math.random() * 2.8, (Math.random() - 0.5) * 4.2);
      sparks.push({ mesh, vel });
      group.add(mesh);
    }

    const flashed = new Map<number, { color: number; intensity: number }>();
    const flashPads: THREE.Mesh[] = [];
    for (const nodeId of blastNodes) {
      const node = state.nodes[nodeId];
      if (node && node.isActive && node.currentType !== NodeType.Abyss) {
        const at = nodeCenter(state, nodeId);
        const pad = new THREE.Mesh(
          new THREE.CircleGeometry(0.46, 28),
          new THREE.MeshBasicMaterial({
            color: 0xff6a14,
            transparent: true,
            opacity: 0.9,
            depthWrite: false,
            blending: THREE.AdditiveBlending,
          })
        );
        pad.rotation.x = -Math.PI / 2;
        pad.position.set(at.x, 0.045, at.z);
        group.add(pad);
        flashPads.push(pad);
      }
      const mesh = this.tiles.get(nodeId);
      if (!mesh) continue;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const top = (mats[2] ?? mats[0]) as THREE.MeshStandardMaterial;
      if (!top.emissive) continue;
      flashed.set(nodeId, { color: top.emissive.getHex(), intensity: top.emissiveIntensity });
    }

    await this.tween(0.62, (t) => {
      const grow = 1 - (1 - t) * (1 - t);
      const fade = 1 - t;
      fire.scale.setScalar(1 + grow * 5.2);
      fireMat.opacity = 0.95 * fade;
      smoke.scale.setScalar(1 + grow * 6.4);
      smoke.position.y = origin.y + grow * 0.55;
      smokeMat.opacity = 0.38 * fade;
      ring.scale.setScalar(1 + grow * 7.5);
      ringMat.opacity = 0.8 * fade;
      light.intensity = 8 * fade;
      for (const spark of sparks) {
        spark.mesh.position.addScaledVector(spark.vel, 0.018);
        spark.vel.y -= 0.045;
        (spark.mesh.material as THREE.MeshBasicMaterial).opacity = fade;
      }
      for (const pad of flashPads) {
        const mat = pad.material as THREE.MeshBasicMaterial;
        mat.opacity = 0.9 * fade;
      }
      for (const [nodeId, prev] of flashed) {
        const mesh = this.tiles.get(nodeId);
        if (!mesh) continue;
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        const top = (mats[2] ?? mats[0]) as THREE.MeshStandardMaterial;
        top.emissive.setHex(0xff6a14);
        top.emissiveIntensity = prev.intensity + 1.6 * fade;
      }
    });

    for (const [nodeId, prev] of flashed) {
      const mesh = this.tiles.get(nodeId);
      if (!mesh) continue;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const top = (mats[2] ?? mats[0]) as THREE.MeshStandardMaterial;
      top.emissive.setHex(prev.color);
      top.emissiveIntensity = prev.intensity;
    }
    this.scene.remove(group);
    fire.geometry.dispose();
    smoke.geometry.dispose();
    ring.geometry.dispose();
    sparkGeo.dispose();
    fireMat.dispose();
    smokeMat.dispose();
    ringMat.dispose();
    for (const spark of sparks) (spark.mesh.material as THREE.Material).dispose();
    for (const pad of flashPads) {
      pad.geometry.dispose();
      (pad.material as THREE.Material).dispose();
    }
  }

  private async playDeath(unitId: number): Promise<void> {
    const actor = this.pieces.get(unitId);
    if (!actor) return;
    actor.group.userData.dying = true;
    actor.mixer?.stopAllAction();
    if (actor.forcefield) {
      actor.group.remove(actor.forcefield);
      actor.forcefield = undefined;
      actor.fieldMat = undefined;
    }

    const start = actor.group.position.clone();
    const startRot = actor.group.quaternion.clone();
    const facing = new THREE.Vector3();
    actor.group.getWorldDirection(facing);
    facing.y = 0;
    if (facing.lengthSq() < 1e-4) facing.set(0, 0, 1);
    facing.normalize();
    // Tip −deg around cross(up, facing): same as Unity +86° in a left-handed engine.
    // Fall plane is the back, with a small yaw so they don't always drop dead straight.
    facing.applyAxisAngle(new THREE.Vector3(0, 1, 0), THREE.MathUtils.degToRad((Math.random() * 2 - 1) * 45));
    const right = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), facing);
    if (right.lengthSq() < 1e-4) right.set(1, 0, 0);
    right.normalize();
    const up = new THREE.Vector3(0, 1, 0);
    const along = facing.clone().negate();

    const side = Math.random() < 0.5 ? -1 : 1;
    const tipDeg = 78 + Math.random() * 16;
    const sweepDeg = 16 + Math.random() * 22;
    const spinDeg = 50 + Math.random() * 70;
    const rollDur = 0.48 + Math.random() * 1.15;
    const easePow = 1.15 + Math.random() * 2.1;
    const easeIn = Math.random() < 0.5;
    const spinRate = 0.65 + Math.random() * 0.85;
    const afterTip = new THREE.Quaternion().setFromAxisAngle(right, THREE.MathUtils.degToRad(-tipDeg)).multiply(startRot);
    const planted = start.clone();
    planted.y = start.y + 0.02;

    await this.tween(0.38, (t) => {
      const e = t * t;
      actor.group.position.lerpVectors(start, planted, e);
      actor.group.quaternion.slerpQuaternions(startRot, afterTip, e);
    });
    await this.tween(rollDur, (t) => {
      const eased = easeIn ? t ** easePow : 1 - (1 - t) ** easePow;
      const yaw = new THREE.Quaternion().setFromAxisAngle(up, THREE.MathUtils.degToRad(sweepDeg) * side * eased);
      const spin = new THREE.Quaternion().setFromAxisAngle(along, THREE.MathUtils.degToRad(spinDeg) * side * Math.min(1, eased * spinRate));
      actor.group.position.copy(planted);
      actor.group.quaternion.copy(yaw).multiply(spin).multiply(afterTip);
    });
    await this.tween(0.42, (t) => {
      actor.group.traverse((child) => {
        const mesh = child as THREE.Mesh;
        if (!mesh.isMesh) return;
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        for (const mat of mats) {
          const m = mat as THREE.Material;
          m.transparent = true;
          m.depthWrite = false;
          m.opacity = 1 - t;
        }
      });
    });
    this.scene.remove(actor.group);
    this.pieces.delete(unitId);
  }

  private async breakForcefield(unitId: number): Promise<void> {
    const actor = this.pieces.get(unitId);
    if (!actor?.fieldMat || !actor.forcefield) return;
    actor.group.userData.breakingField = true;
    const mat = actor.fieldMat;
    await this.tween(0.45, (t) => {
      mat.uniforms.uBreak.value = t;
      actor.forcefield!.scale.setScalar(1.12 + t * 0.35);
    });
    actor.group.remove(actor.forcefield);
    actor.forcefield = undefined;
    actor.fieldMat = undefined;
    actor.group.userData.breakingField = false;
  }

  private tween(duration: number, update: (t: number) => void): Promise<void> {
    return new Promise((resolve) => {
      const start = performance.now();
      const step = () => {
        const t = Math.min((performance.now() - start) / (duration * 1000), 1);
        update(t);
        if (t < 1) requestAnimationFrame(step);
        else resolve();
      };
      step();
    });
  }

  private wait(seconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, seconds * 1000));
  }

  private resetCamera(width: number, height: number, localPlayerId: number, layout: BoardLayout = "square"): void {
    const cx = width / 2;
    const z0 = layout === "hex-offset" ? -0.5 : 0;
    const z1 = layout === "hex-offset" ? height - 0.5 : height;
    const cz = (z0 + z1) / 2;
    const span = Math.max(width, z1 - z0);
    this.controls.target.set(cx, 0, cz);
    this.controls.maxDistance = 12 + span;
    const lift = layout === "hex-offset" ? 2.4 : 0;
    this.camera.position.set(
      cx,
      4.8 + span * 0.3 + lift,
      localPlayerId === 0 ? cz - (3.8 + span * 0.82) : cz + 3.8 + span * 0.82
    );
    this.controls.update();
  }

  private readonly resize = (): void => {
    const w = this.canvas.clientWidth || window.innerWidth;
    const h = this.canvas.clientHeight || window.innerHeight;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h, false);
  };

  private setPointer(event: PointerEvent): void {
    const rect = this.canvas.getBoundingClientRect();
    this.pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    this.raycaster.setFromCamera(this.pointer, this.camera);
  }

  private pickNodeId(): number | null {
    const state = this.state;
    if (!state) return null;
    const hit = new THREE.Vector3();
    if (!this.raycaster.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), hit)) return null;
    const layout = stateLayout(state);
    const worldX = Math.floor(hit.x);
    const x = state.width - 1 - worldX;
    const odd = ((x % 2) + 2) % 2 === 1;
    const y = layout === "hex-offset" ? Math.floor(hit.z + (odd ? 0.5 : 0)) : Math.floor(hit.z);
    if (x < 0 || y < 0 || x >= state.width || y >= state.height) return null;
    if (layout === "hex-offset" && !hexCellExists(x, y, state.width, state.height)) return null;
    return y * state.width + x;
  }

  private pickUnitId(): number | null {
    const pieceHits = this.raycaster.intersectObjects(
      [...this.pieces.values()].map((actor) => actor.group),
      true
    );
    for (const hit of pieceHits) {
      let obj: THREE.Object3D | null = hit.object;
      while (obj && obj.userData.unitId == null) obj = obj.parent;
      if (obj?.userData.unitId == null || obj.userData.dying) continue;
      return obj.userData.unitId as number;
    }
    if (!this.state) return null;
    const nodeId = this.pickNodeId();
    if (nodeId == null) return null;
    return unitOnNode(this.state, nodeId)?.unitId ?? null;
  }

  private pickItemId(): string | null {
    const hits = this.raycaster.intersectObjects([...this.itemMeshes.values()], true);
    for (const hit of hits) {
      let obj: THREE.Object3D | null = hit.object;
      while (obj && obj.userData.itemId == null) obj = obj.parent;
      if (!obj || obj.userData.leaving) continue;
      return obj.userData.itemId as string;
    }
    if (!this.state) return null;
    const nodeId = this.pickNodeId();
    if (nodeId == null) return null;
    return this.state.items.find((item) => item.nodeId === nodeId)?.id ?? null;
  }

  private pickInspectTarget(): InspectHover | null {
    const unitId = this.pickUnitId();
    if (unitId != null) return { kind: "unit", unitId };
    const itemId = this.pickItemId();
    if (itemId != null) return { kind: "item", itemId };
    return null;
  }

  private emitHover(event: PointerEvent): void {
    if (this.busy) return;
    this.setPointer(event);
    this.onInspectHover?.(this.pickInspectTarget());
  }

  private readonly onPointerDown = (event: PointerEvent): void => {
    this.pointerDown = { x: event.clientX, y: event.clientY };
  };

  private readonly onPointerMove = (event: PointerEvent): void => {
    if (this.pointerDown) return;
    this.emitHover(event);
  };

  private readonly onPointerLeave = (): void => {
    if (this.pointerDown) return;
    this.onInspectHover?.(null);
  };

  private readonly onPointerUp = (event: PointerEvent): void => {
    if (!this.pointerDown || this.busy) return;
    const dx = event.clientX - this.pointerDown.x;
    const dy = event.clientY - this.pointerDown.y;
    this.pointerDown = null;
    if (dx * dx + dy * dy > 16) {
      this.emitHover(event);
      return;
    }
    this.setPointer(event);
    const pads = [...this.itemMeshes.values()]
      .filter((group) => group.userData.pickable)
      .map((group) => group.userData.pad as THREE.Object3D | undefined)
      .filter((pad): pad is THREE.Object3D => !!pad);
    const itemHits = this.raycaster.intersectObjects(pads, false);
    if (itemHits[0]) {
      let obj: THREE.Object3D | null = itemHits[0].object;
      while (obj && !obj.userData.itemId) obj = obj.parent;
      if (obj?.userData.itemId) {
        this.onItemClick?.(obj.userData.itemId, obj.userData.nodeId);
        return;
      }
    }
    const unitId = this.pickUnitId();
    if (unitId != null) {
      const node = this.state?.units.find((u) => u.unitId === unitId)?.currentNodeId;
      if (typeof node === "number") {
        this.onTileClick?.(node);
        this.emitHover(event);
        return;
      }
    }
    const nodeId = this.pickNodeId();
    if (typeof nodeId === "number") this.onTileClick?.(nodeId);
    this.emitHover(event);
  };

  private readonly loop = (): void => {
    this.raf = requestAnimationFrame(this.loop);
    const dt = this.clock.getDelta();
    const t = this.clock.elapsedTime;
    for (const actor of this.pieces.values()) {
      actor.mixer?.update(dt);
      if (actor.fieldMat) actor.fieldMat.uniforms.uTime.value = t;
    }
    for (const group of this.itemMeshes.values()) {
      const spin = group.userData.spin as THREE.Object3D | undefined;
      if (spin) spin.rotation.y += dt * 0.8;
    }
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  };
}

export function unitOnNode(state: PublicState, nodeId: number): UnitState | undefined {
  return state.units.find((u) => u.isAlive && u.currentNodeId === nodeId);
}

export function itemOnNode(state: PublicState, nodeId: number): ItemState | undefined {
  return state.items.find((i) => i.nodeId === nodeId);
}

export function boardFromState(state: PublicState): Board {
  return new Board(definitionFromPublic(state), state.nodes);
}
