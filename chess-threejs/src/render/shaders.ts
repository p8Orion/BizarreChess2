import * as THREE from "three";

export function createForcefieldMaterial(color: THREE.Color): THREE.ShaderMaterial {
  return new THREE.ShaderMaterial({
    transparent: true,
    depthWrite: false,
    side: THREE.FrontSide,
    uniforms: {
      uColor: { value: color.clone() },
      uTime: { value: 0 },
      uFresnelPower: { value: 2 },
      uNoiseScale: { value: 8 },
      uNoiseSpeed: { value: 0.5 },
      uPulseIntensity: { value: 0.8 },
      uEdgeGlow: { value: 1.2 },
      uBreak: { value: 0 },
    },
    vertexShader: `
      varying vec3 vNormal;
      varying vec3 vWorldPos;
      void main() {
        vNormal = normalize(mat3(modelMatrix) * normal);
        vec4 world = modelMatrix * vec4(position, 1.0);
        vWorldPos = world.xyz;
        gl_Position = projectionMatrix * viewMatrix * world;
      }
    `,
    fragmentShader: `
      uniform vec3 uColor;
      uniform float uTime;
      uniform float uFresnelPower;
      uniform float uNoiseScale;
      uniform float uNoiseSpeed;
      uniform float uPulseIntensity;
      uniform float uEdgeGlow;
      uniform float uBreak;
      varying vec3 vNormal;
      varying vec3 vWorldPos;

      float hash(vec3 p) {
        p = fract(p * 0.3183099 + 0.1);
        p *= 17.0;
        return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
      }

      float noise(vec3 p) {
        vec3 i = floor(p);
        vec3 f = fract(p);
        f = f * f * (3.0 - 2.0 * f);
        return mix(
          mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x),
              mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
          mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x),
              mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y),
          f.z);
      }

      float fbm(vec3 p) {
        float value = 0.0;
        float amplitude = 0.5;
        float frequency = 1.0;
        for (int i = 0; i < 4; i++) {
          value += amplitude * noise(p * frequency);
          amplitude *= 0.5;
          frequency *= 2.0;
        }
        return value;
      }

      void main() {
        vec3 viewDir = normalize(cameraPosition - vWorldPos);
        float fresnel = pow(1.0 - clamp(dot(normalize(vNormal), viewDir), 0.0, 1.0), uFresnelPower);
        float t = uTime * uNoiseSpeed;
        float cloud = fbm(vWorldPos * uNoiseScale + vec3(t, t * 0.7, t * 0.3));
        float pulse = sin(uTime * 3.0) * 0.5 + 0.5;
        float pulseEffect = 1.0 + pulse * uPulseIntensity;
        float alpha = 0.22 * (fresnel * uEdgeGlow + cloud * 0.5) * pulseEffect;
        alpha = clamp(alpha + uBreak * 0.35, 0.0, 1.0);
        vec3 color = uColor * (1.0 + cloud * 0.3) + fresnel * uColor * 0.5;
        color += vec3(uBreak);
        gl_FragColor = vec4(color, alpha * (1.0 - uBreak * 0.35));
      }
    `,
  });
}

export function createMarkerMaterial(map: THREE.Texture, color: THREE.Color, opacity = 1): THREE.MeshBasicMaterial {
  return new THREE.MeshBasicMaterial({
    map,
    color,
    transparent: true,
    opacity,
    depthWrite: false,
    side: THREE.DoubleSide,
  });
}

function canvasTexture(draw: (ctx: CanvasRenderingContext2D, size: number) => void): THREE.CanvasTexture {
  const size = 128;
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext("2d")!;
  draw(ctx, size);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function createCircleMap(): THREE.CanvasTexture {
  return canvasTexture((ctx, size) => {
    const c = size / 2;
    const r = c - 2;
    const g = ctx.createRadialGradient(c, c, r - 6, c, c, r);
    g.addColorStop(0, "rgba(255,255,255,0.62)");
    g.addColorStop(1, "rgba(255,255,255,0)");
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(c, c, r, 0, Math.PI * 2);
    ctx.fill();
  });
}

export function createRingMap(): THREE.CanvasTexture {
  return canvasTexture((ctx, size) => {
    const c = size / 2;
    const outer = c - 2;
    const inner = outer * (0.35 / 0.45);
    ctx.strokeStyle = "rgba(255,255,255,0.72)";
    ctx.lineWidth = outer - inner;
    ctx.beginPath();
    ctx.arc(c, c, (outer + inner) / 2, 0, Math.PI * 2);
    ctx.stroke();
  });
}

export function createCrosshairMap(): THREE.CanvasTexture {
  return canvasTexture((ctx, size) => {
    const c = size / 2;
    const lineW = size * 0.055;
    const lineL = size * 0.48;
    const gap = size * 0.12;
    ctx.fillStyle = "rgba(255,255,255,0.78)";
    ctx.fillRect(c - lineL, c - lineW, lineL - gap, lineW * 2);
    ctx.fillRect(c + gap, c - lineW, lineL - gap, lineW * 2);
    ctx.fillRect(c - lineW, c - lineL, lineW * 2, lineL - gap);
    ctx.fillRect(c - lineW, c + gap, lineW * 2, lineL - gap);
  });
}

export function createWoodFallback(light: boolean): THREE.CanvasTexture {
  return canvasTexture((ctx, size) => {
    ctx.fillStyle = light ? "#e8c992" : "#6b4328";
    ctx.fillRect(0, 0, size, size);
    for (let i = 0; i < 18; i++) {
      ctx.strokeStyle = light ? `rgba(120,80,40,${0.08 + (i % 5) * 0.02})` : `rgba(20,10,6,${0.12 + (i % 4) * 0.03})`;
      ctx.lineWidth = 1 + (i % 3);
      ctx.beginPath();
      ctx.moveTo(-10, i * 8);
      ctx.bezierCurveTo(40, i * 8 + 6, 80, i * 8 - 4, 140, i * 8 + 3);
      ctx.stroke();
    }
  });
}
