import * as THREE from "three";
import type { PlayerStyle } from "../core/colors";
import { PIECES } from "../core/pieces";
import type { UnitState } from "../core/types";
import { PORTRAIT_ICON_DIST, addPortraitLights, portraitCameraDir } from "../render/portraitFrame";
import { createCheckerGround } from "../render/portraitGround";

const SIZE = 96;
const cache = new Map<string, string>();
const pending = new Map<string, Promise<string>>();

let loadVisual: ((unit: UnitState, style?: PlayerStyle) => Promise<THREE.Group>) | null = null;
let renderer: THREE.WebGLRenderer | null = null;
let scene: THREE.Scene | null = null;
let camera: THREE.PerspectiveCamera | null = null;

export function bindPieceIconLoader(load: (unit: UnitState, style?: PlayerStyle) => Promise<THREE.Group>): void {
  loadVisual = load;
}

export function stubUnit(definitionId: string, ownerId: 0 | 1 = 0): UnitState {
  const def = PIECES[definitionId];
  return {
    unitId: -1,
    definitionId,
    ownerId,
    currentNodeId: 0,
    patterns: def ? structuredClone(def.patterns) : [],
    skills: def ? structuredClone(def.skills) : [],
    actions: def ? structuredClone(def.actions ?? []) : [],
    hasMovedThisTurn: false,
    hasEverMoved: false,
    isAlive: true,
    heldItem: null,
  };
}

function iconKey(definitionId: string, ownerId: 0 | 1, style?: PlayerStyle): string {
  const look = style ? `${style.primary}:${style.secondary}:${style.pattern}` : "";
  return `p7:${definitionId}:${ownerId}:${look}`;
}

function ensureRenderer(): { renderer: THREE.WebGLRenderer; scene: THREE.Scene; camera: THREE.PerspectiveCamera } {
  if (renderer && scene && camera) return { renderer, scene, camera };
  const canvas = document.createElement("canvas");
  canvas.width = SIZE;
  canvas.height = SIZE;
  renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: "low-power" });
  renderer.setSize(SIZE, SIZE, false);
  renderer.setClearColor(0x5a5854, 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  scene = new THREE.Scene();
  addPortraitLights(scene);
  scene.add(createCheckerGround());
  camera = new THREE.PerspectiveCamera(32, 1, 0.08, 20);
  return { renderer, scene, camera };
}

function disposeTree(root: THREE.Object3D): void {
  root.traverse((child) => {
    const mesh = child as THREE.Mesh;
    if (!mesh.isMesh) return;
    const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
    for (const mat of mats) mat.dispose();
  });
}

async function snapshot(definitionId: string, ownerId: 0 | 1, style?: PlayerStyle): Promise<string> {
  if (!loadVisual) return "";
  try {
    const visual = await loadVisual(stubUnit(definitionId, ownerId), style);
    const ctx = ensureRenderer();
    ctx.scene.add(visual);
    visual.updateWorldMatrix(true, true);
    const box = new THREE.Box3().setFromObject(visual);
    if (!box.isEmpty()) {
      const size = box.getSize(new THREE.Vector3());
      const center = box.getCenter(new THREE.Vector3());
      const fov = (ctx.camera.fov * Math.PI) / 180;
      const halfY = size.y * 0.5;
      const halfX = Math.max(size.x, size.z) * 0.5;
      const dist = (Math.max(halfY, halfX) / Math.tan(fov / 2)) * PORTRAIT_ICON_DIST;
      ctx.camera.position.copy(center).addScaledVector(portraitCameraDir(), dist);
      ctx.camera.near = Math.max(0.04, dist - Math.max(halfY, halfX) * 2.4);
      ctx.camera.far = dist + Math.max(halfY, halfX) * 5;
      ctx.camera.lookAt(center.x, center.y + size.y * 0.02, center.z);
      ctx.camera.updateProjectionMatrix();
    }
    ctx.renderer.render(ctx.scene, ctx.camera);
    const url = ctx.renderer.domElement.toDataURL("image/png");
    ctx.scene.remove(visual);
    disposeTree(visual);
    return url;
  } catch {
    return "";
  }
}

export function pieceIconUrl(definitionId: string, ownerId: 0 | 1 = 0, style?: PlayerStyle): Promise<string> {
  const key = iconKey(definitionId, ownerId, style);
  const hit = cache.get(key);
  if (hit) return Promise.resolve(hit);
  const inflight = pending.get(key);
  if (inflight) return inflight;
  const job = snapshot(definitionId, ownerId, style).then((url) => {
    if (url) cache.set(key, url);
    pending.delete(key);
    return url;
  });
  pending.set(key, job);
  return job;
}
