import * as THREE from "three";
import { portraitWash } from "../core/colors";
import type { ItemState, UnitState } from "../core/types";
import { PORTRAIT_LIVE_DIST, addPortraitLights, portraitCameraDir } from "./portraitFrame";
import { createCheckerGround } from "./portraitGround";

export class PortraitView {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(32, 1, 0.08, 20);
  private readonly renderer: THREE.WebGLRenderer;
  private readonly clock = new THREE.Clock();
  private visual: THREE.Object3D | null = null;
  private token = 0;
  private raf = 0;
  private visible = false;
  private currentKey = "";
  private look = new THREE.Vector3();

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly loadVisual: (unit: UnitState) => Promise<THREE.Group>,
    private readonly loadItemVisual?: (item: ItemState) => Promise<THREE.Group>
  ) {
    this.renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: true,
      powerPreference: "low-power",
    });
    this.setBackdrop();
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    addPortraitLights(this.scene);
    this.scene.add(createCheckerGround());
    this.resize();
    window.addEventListener("resize", this.resize);
    this.loop();
  }

  dispose(): void {
    cancelAnimationFrame(this.raf);
    window.removeEventListener("resize", this.resize);
    this.clear();
    this.renderer.dispose();
  }

  clear(): void {
    this.token += 1;
    this.currentKey = "";
    this.visible = false;
    this.setBackdrop();
    if (!this.visual) return;
    this.scene.remove(this.visual);
    this.visual.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      for (const mat of mats) mat.dispose();
    });
    this.visual = null;
    this.renderer.clear();
  }

  show(unit: UnitState | null, primary?: string): void {
    if (!unit) {
      this.clear();
      return;
    }
    this.setBackdrop(primary);
    this.showLoaded(
      `unit:${unit.unitId}:${unit.definitionId}:${unit.ownerId}:${unit.heldItem?.id ?? unit.heldItem?.kind ?? ""}:${primary ?? ""}`,
      () => this.loadVisual(unit)
    );
  }

  showItem(item: ItemState | null): void {
    if (!item || !this.loadItemVisual) {
      this.clear();
      return;
    }
    this.setBackdrop();
    this.showLoaded(`item:${item.id}:${item.kind}:${item.model ?? item.shape}`, () => this.loadItemVisual!(item));
  }

  private showLoaded(key: string, load: () => Promise<THREE.Group>): void {
    if (key === this.currentKey) {
      this.visible = true;
      return;
    }
    this.currentKey = key;
    this.visible = true;
    const token = ++this.token;
    void load().then((visual) => {
      if (token !== this.token) {
        this.disposeTree(visual);
        return;
      }
      if (this.visual) this.disposeTree(this.visual);
      this.visual = visual;
      this.scene.add(visual);
      this.frame(visual);
    });
  }

  private disposeTree(root: THREE.Object3D): void {
    this.scene.remove(root);
    root.traverse((child) => {
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh) return;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      for (const mat of mats) mat.dispose();
    });
  }

  private frame(object: THREE.Object3D): void {
    object.updateWorldMatrix(true, true);
    const pieceBox = this.framingBox(object);
    const fullBox = new THREE.Box3().setFromObject(object);
    if (pieceBox.isEmpty() && fullBox.isEmpty()) return;
    const box = pieceBox.isEmpty() ? fullBox : pieceBox;
    const size = box.getSize(new THREE.Vector3());
    const span = fullBox.isEmpty() ? size : fullBox.getSize(new THREE.Vector3());
    const radius = Math.max(span.x, span.y, span.z, size.x, size.y, size.z) * 0.5;
    const dist = (radius / Math.tan((this.camera.fov * Math.PI) / 360)) * PORTRAIT_LIVE_DIST;
    this.look.set(box.min.x + size.x * 0.5, box.min.y + size.y * 0.42, box.min.z + size.z * 0.5);
    this.camera.position.copy(this.look).addScaledVector(portraitCameraDir(), dist);
    this.camera.near = Math.max(0.04, dist - radius * 2.6);
    this.camera.far = dist + radius * 5;
    this.camera.lookAt(this.look);
    this.camera.updateProjectionMatrix();
    this.camera.updateMatrixWorld(true);
    this.panToNdcCenter(box);
  }

  private framingBox(root: THREE.Object3D): THREE.Box3 {
    const boxes: THREE.Box3[] = [];
    root.traverse((child) => {
      if (this.isDecor(child)) return;
      const mesh = child as THREE.Mesh;
      if (!mesh.isMesh || !mesh.geometry) return;
      mesh.geometry.computeBoundingBox();
      if (!mesh.geometry.boundingBox) return;
      boxes.push(mesh.geometry.boundingBox.clone().applyMatrix4(mesh.matrixWorld));
    });
    if (!boxes.length) return new THREE.Box3();
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

  private isDecor(obj: THREE.Object3D): boolean {
    let cur: THREE.Object3D | null = obj;
    while (cur) {
      if (cur.name === "heldItem" || cur.name === "forcefield") return true;
      cur = cur.parent;
    }
    return false;
  }

  private panToNdcCenter(box: THREE.Box3): void {
    const point = new THREE.Vector3();
    let minX = Infinity;
    let maxX = -Infinity;
    let minY = Infinity;
    let maxY = -Infinity;
    for (const x of [box.min.x, box.max.x]) {
      for (const y of [box.min.y, box.max.y]) {
        for (const z of [box.min.z, box.max.z]) {
          point.set(x, y, z).project(this.camera);
          minX = Math.min(minX, point.x);
          maxX = Math.max(maxX, point.x);
          minY = Math.min(minY, point.y);
          maxY = Math.max(maxY, point.y);
        }
      }
    }
    if (!Number.isFinite(minX)) return;
    const midX = (minX + maxX) / 2;
    const midY = (minY + maxY) / 2;
    const dist = this.camera.position.distanceTo(this.look);
    const worldH = 2 * Math.tan((this.camera.fov * Math.PI) / 360) * dist;
    const worldW = worldH * this.camera.aspect;
    const right = new THREE.Vector3();
    const up = new THREE.Vector3();
    this.camera.matrixWorld.extractBasis(right, up, new THREE.Vector3());
    const pan = right.multiplyScalar(midX * (worldW / 2)).add(up.multiplyScalar(midY * (worldH / 2)));
    this.camera.position.add(pan);
    this.look.add(pan);
    this.camera.lookAt(this.look);
  }

  private setBackdrop(primary?: string): void {
    const hex = primary ? portraitWash(primary) : "#5a5854";
    this.renderer.setClearColor(new THREE.Color(hex), 1);
    this.canvas.parentElement?.style.setProperty("--portrait-wash", hex);
  }

  private readonly resize = (): void => {
    const css = Math.max(this.canvas.clientWidth || 88, 64);
    const px = Math.round(css * Math.min(devicePixelRatio, 2));
    this.renderer.setSize(px, px, false);
    this.camera.aspect = 1;
    this.camera.updateProjectionMatrix();
    if (this.visual) this.frame(this.visual);
  };

  private readonly loop = (): void => {
    this.raf = requestAnimationFrame(this.loop);
    if (!this.visible || !this.visual) return;
    this.clock.getDelta();
    this.renderer.render(this.scene, this.camera);
  };
}
