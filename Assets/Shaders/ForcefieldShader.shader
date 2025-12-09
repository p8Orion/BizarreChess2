Shader "BizarreChess/Forcefield"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.6, 1, 0.4)
        _FresnelPower ("Fresnel Power", Range(0.5, 5)) = 2.0
        _NoiseScale ("Noise Scale", Range(1, 20)) = 8.0
        _NoiseSpeed ("Noise Speed", Range(0.1, 3)) = 0.5
        _PulseIntensity ("Pulse Intensity", Range(0, 1)) = 0.8
        _EdgeGlow ("Edge Glow", Range(0, 2)) = 1.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Forcefield"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _FresnelPower;
                float _NoiseScale;
                float _NoiseSpeed;
                float _PulseIntensity;
                float _EdgeGlow;
            CBUFFER_END

            // Simple 3D noise function
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                
                return lerp(
                    lerp(
                        lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), f.x),
                        lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x),
                        f.y
                    ),
                    lerp(
                        lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                        lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x),
                        f.y
                    ),
                    f.z
                );
            }

            // Fractal brownian motion for cloud-like effect
            float fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * noise(p * frequency);
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                output.uv = input.uv;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // Fresnel effect - brighter at edges
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                
                // Animated noise for cloud effect
                float time = _Time.y * _NoiseSpeed;
                float3 noisePos = input.positionWS * _NoiseScale + float3(time, time * 0.7, time * 0.3);
                float cloudNoise = fbm(noisePos);
                
                // Pulse animation
                float pulse = sin(_Time.y * 3.0) * 0.5 + 0.5;
                float pulseEffect = 1.0 + pulse * _PulseIntensity;
                
                // Combine effects
                float alpha = _Color.a * (fresnel * _EdgeGlow + cloudNoise * 0.5) * pulseEffect;
                alpha = saturate(alpha);
                
                // Color with slight variation from noise
                float3 finalColor = _Color.rgb * (1.0 + cloudNoise * 0.3);
                finalColor += fresnel * _Color.rgb * 0.5; // Extra glow at edges
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    
    Fallback "Transparent/Diffuse"
}

